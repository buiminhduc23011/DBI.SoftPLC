using System.Reflection;
using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Services.Runtime;

namespace DBI.Controller.Tests;

/// <summary>
/// <see cref="SynchronizationContext"/> giả lập UI thread: ghi lại thread nào thực sự chạy
/// callback, để khẳng định event của client được marshal đúng chỗ.
/// </summary>
internal sealed class RecordingSynchronizationContext : SynchronizationContext, IDisposable
{
    private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _queue = new();
    private readonly Thread _pump;

    public RecordingSynchronizationContext()
    {
        _pump = new Thread(Pump) { IsBackground = true, Name = "FakeUiThread" };
        _pump.Start();
    }

    public int UiThreadId => _pump.ManagedThreadId;

    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

    public override void Send(SendOrPostCallback d, object? state) => Post(d, state);

    /// <summary>Chạy <paramref name="action"/> trên "UI thread" và chờ xong.</summary>
    public void Invoke(Action action)
    {
        using var done = new ManualResetEventSlim();

        Post(_ =>
        {
            try { action(); }
            finally { done.Set(); }
        }, null);

        Assert.True(done.Wait(TimeSpan.FromSeconds(5)), "UI thread giả không xử lý kịp.");
    }

    private void Pump()
    {
        SetSynchronizationContext(this);

        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            callback(state);
    }

    public void Dispose() => _queue.CompleteAdding();
}

public class RuntimeClientTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // ── Kiến trúc: ranh giới ADR-001 ─────────────────────────────────────────────

    [Fact]
    public void Studio_KhongConThamChieuToiRuntime()
    {
        // DoD quan trọng nhất của phase-03. Nếu Studio vẫn build được khi đã xoá reference tới
        // Runtime thì ranh giới tiến trình là thật, không phải hình thức.
        var studioCore = typeof(IRuntimeClient).Assembly;

        var referenced = studioCore.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        Assert.DoesNotContain("DBI.Controller.Runtime", referenced);
        Assert.DoesNotContain("DBI.Controller.Driver.Simulation", referenced);
        Assert.Contains("DBI.Controller.Protocol", referenced);
    }

    [Fact]
    public void StudioWpf_KhongConThamChieuToiRuntime()
    {
        // Đọc thẳng tệp .csproj: assembly WPF không nạp được trong test host (net10.0 thường).
        string csproj = Path.Combine(
            FindRepositoryRoot(), "src", "DBI.Controller.Studio", "DBI.Controller.Studio.csproj");

        Assert.True(File.Exists(csproj), $"Không tìm thấy {csproj}");

        string content = File.ReadAllText(csproj);

        Assert.DoesNotContain("DBI.Controller.Runtime.csproj", content);
        Assert.DoesNotContain("DBI.Controller.Driver.", content);
        Assert.Contains("DBI.Controller.Protocol.csproj", content);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DBI.Controller.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }

    [Fact]
    public void LiveMonitoringServiceCu_DaBiXoa()
    {
        string path = Path.Combine(
            FindRepositoryRoot(), "src", "DBI.Controller.Studio", "Services", "LiveMonitoringService.cs");

        Assert.False(File.Exists(path), "LiveMonitoringService cũ phải bị xoá, thay bằng IRuntimeClient.");
    }

    // ── End-to-end với Runtime thật của phase-02 ─────────────────────────────────

    [Fact]
    public async Task Client_KetNoiRuntimeThat_DeployVaChayDuoc()
    {
        await using var fixture = new IpcFixture();
        await using var client = new NamedPipeRuntimeClient { AutoReconnect = false, StatusPollIntervalMs = 50 };

        Assert.True(await client.ConnectAsync(new RuntimeConnectionTarget(fixture.PipeName)));
        Assert.Equal(RuntimeClientState.Connected, client.State);

        var deploy = await client.DeployAsync(
            SampleAssembly.Bytes, SampleAssembly.ConveyorRoutes(), SampleAssembly.SimulationDevice());

        Assert.True(deploy.Ok, deploy.Error);

        await WaitUntilAsync(() => client.LastStatus?.State == RuntimeState.Running, Timeout);
        Assert.Equal(RuntimeState.Running, client.LastStatus!.State);
    }

    [Fact]
    public async Task Client_PollStatus_PhatEventDeuDan()
    {
        await using var fixture = new IpcFixture();
        await using var client = new NamedPipeRuntimeClient { AutoReconnect = false, StatusPollIntervalMs = 50 };

        int updates = 0;
        client.StatusUpdated += (_, _) => Interlocked.Increment(ref updates);

        await client.ConnectAsync(new RuntimeConnectionTarget(fixture.PipeName));

        await WaitUntilAsync(() => Volatile.Read(ref updates) >= 3, Timeout);
        Assert.True(Volatile.Read(ref updates) >= 3, $"Chỉ nhận {updates} lần cập nhật trạng thái.");
    }

    [Fact]
    public async Task Client_NhanPushTagDaDangKy()
    {
        await using var fixture = new IpcFixture(pushIntervalMs: 20);
        await using var client = new NamedPipeRuntimeClient { AutoReconnect = false, StatusPollIntervalMs = 100 };

        var received = new List<TagValueUpdate>();
        client.TagValueChanged += (_, update) => { lock (received) received.Add(update); };

        await client.ConnectAsync(new RuntimeConnectionTarget(fixture.PipeName));
        await client.DeployAsync(
            SampleAssembly.Bytes, SampleAssembly.ConveyorRoutes(), SampleAssembly.SimulationDevice());

        await client.SubscribeTagsAsync(new[] { "ConveyorRun" });

        var simulation = (Driver.Simulation.SimulationDriver)fixture.Host.Drivers.Drivers.Single();
        simulation.SetInputBool("StartButton", true);

        await WaitUntilAsync(() => { lock (received) return received.Count > 0; }, Timeout);

        lock (received)
        {
            Assert.All(received, u => Assert.Equal("ConveyorRun", u.TagName));
        }
    }

    [Fact]
    public async Task Client_LayDuocTrangThaiThietBi()
    {
        await using var fixture = new IpcFixture();
        await using var client = new NamedPipeRuntimeClient { AutoReconnect = false, StatusPollIntervalMs = 100 };

        await client.ConnectAsync(new RuntimeConnectionTarget(fixture.PipeName));
        await client.DeployAsync(
            SampleAssembly.Bytes, SampleAssembly.ConveyorRoutes(), SampleAssembly.SimulationDevice());

        var devices = await client.GetDeviceStatesAsync();

        var device = Assert.Single(devices);
        Assert.Equal("SIM", device.DriverId);
    }

    // ── Mất kết nối ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Client_RuntimeBienMat_BaoConnectionLostKhongTreo()
    {
        var fixture = new IpcFixture();
        await using var client = new NamedPipeRuntimeClient { AutoReconnect = false, StatusPollIntervalMs = 50 };

        string? lostReason = null;
        client.ConnectionLost += (_, reason) => lostReason = reason;

        await client.ConnectAsync(new RuntimeConnectionTarget(fixture.PipeName));
        await fixture.DisposeAsync();

        await WaitUntilAsync(() => lostReason is not null, Timeout);

        Assert.NotNull(lostReason);
        Assert.Equal(RuntimeClientState.Disconnected, client.State);
    }

    [Fact]
    public async Task Client_MatKetNoiGiuaLucDeploy_BaoLoiRoRangKhongTreo()
    {
        var fixture = new IpcFixture();
        var client = new NamedPipeRuntimeClient { AutoReconnect = false, StatusPollIntervalMs = 5_000 };

        await client.ConnectAsync(new RuntimeConnectionTarget(fixture.PipeName));

        // Giết Runtime rồi mới deploy — đúng tình huống rút dây mạng giữa chừng.
        await fixture.DisposeAsync();

        var result = await client.DeployAsync(
            SampleAssembly.Bytes, SampleAssembly.ConveyorRoutes(), SampleAssembly.SimulationDevice());

        Assert.False(result.Ok);
        Assert.Contains("Runtime", result.Error);

        await client.DisposeAsync();
    }

    [Fact]
    public async Task Client_TuKetNoiLai_KhiRuntimeQuayLai()
    {
        string pipeName = "DBI.Test." + Guid.NewGuid().ToString("N");

        await using var client = new NamedPipeRuntimeClient { AutoReconnect = true, StatusPollIntervalMs = 50 };

        var firstRuntime = new IpcFixture(pipeName: pipeName);
        Assert.True(await client.ConnectAsync(new RuntimeConnectionTarget(pipeName)));

        // Runtime chết.
        await firstRuntime.DisposeAsync();
        await WaitUntilAsync(() => client.State == RuntimeClientState.Reconnecting, Timeout);

        // Runtime lên lại trên đúng pipe cũ.
        await using var secondRuntime = new IpcFixture(pipeName: pipeName);

        await WaitUntilAsync(() => client.State == RuntimeClientState.Connected, TimeSpan.FromSeconds(30));
        Assert.Equal(RuntimeClientState.Connected, client.State);
    }

    // ── Marshal event về UI thread ───────────────────────────────────────────────

    [Fact]
    public async Task MoiEvent_RaiseTrenUiThread()
    {
        using var uiContext = new RecordingSynchronizationContext();
        await using var fixture = new IpcFixture();

        NamedPipeRuntimeClient? client = null;
        var eventThreadIds = new System.Collections.Concurrent.ConcurrentBag<int>();

        // Tạo client TRÊN "UI thread" — nó bắt SynchronizationContext ngay lúc khởi tạo.
        uiContext.Invoke(() =>
        {
            client = new NamedPipeRuntimeClient { AutoReconnect = false, StatusPollIntervalMs = 50 };
            client.StatusUpdated += (_, _) => eventThreadIds.Add(Environment.CurrentManagedThreadId);
            client.StateChanged += (_, _) => eventThreadIds.Add(Environment.CurrentManagedThreadId);
        });

        await client!.ConnectAsync(new RuntimeConnectionTarget(fixture.PipeName));
        await WaitUntilAsync(() => eventThreadIds.Count >= 3, Timeout);

        Assert.NotEmpty(eventThreadIds);
        Assert.All(eventThreadIds, id => Assert.Equal(uiContext.UiThreadId, id));

        await client.DisposeAsync();
    }

    // ── FakeRuntimeClient ────────────────────────────────────────────────────────

    [Fact]
    public async Task Fake_DeployRoiChay_DuDungChoUiKhiChuaCoRuntime()
    {
        await using var fake = new FakeRuntimeClient();

        Assert.True(await fake.ConnectAsync(new RuntimeConnectionTarget()));

        var deploy = await fake.DeployAsync(
            Array.Empty<byte>(), SampleAssembly.ConveyorRoutes(), SampleAssembly.SimulationDevice());

        Assert.True(deploy.Ok);
        Assert.Equal(RuntimeState.Running, fake.LastStatus!.State);
        Assert.Single(fake.DeviceStates);
    }

    [Fact]
    public async Task Fake_ChiPhatEventChoTagDaDangKy()
    {
        await using var fake = new FakeRuntimeClient();
        await fake.ConnectAsync(new RuntimeConnectionTarget());
        await fake.DeployAsync(Array.Empty<byte>(), SampleAssembly.ConveyorRoutes(), SampleAssembly.SimulationDevice());

        var received = new List<TagValueUpdate>();
        fake.TagValueChanged += (_, u) => received.Add(u);

        await fake.SubscribeTagsAsync(new[] { "ConveyorRun" });
        received.Clear();

        fake.SetTagValue("ConveyorRun", true);
        fake.SetTagValue("StartButton", true);      // chưa đăng ký

        var update = Assert.Single(received);
        Assert.Equal("ConveyorRun", update.TagName);
    }

    [Fact]
    public async Task Fake_GiaLapMatKetNoiVaFault()
    {
        await using var fake = new FakeRuntimeClient();
        await fake.ConnectAsync(new RuntimeConnectionTarget());

        FaultNotification? fault = null;
        string? lost = null;
        fake.FaultOccurred += (_, f) => fault = f;
        fake.ConnectionLost += (_, r) => lost = r;

        fake.SimulateFault("Chia cho 0 ở khối Conveyor");
        Assert.Equal("Chia cho 0 ở khối Conveyor", fault!.Message);
        Assert.Equal(RuntimeState.Faulted, fake.LastStatus!.State);

        fake.SimulateConnectionLost();
        Assert.NotNull(lost);
        Assert.Equal(RuntimeClientState.Disconnected, fake.State);
    }

    [Fact]
    public async Task Fake_KhongKetNoiDuoc_TraFalse()
    {
        await using var fake = new FakeRuntimeClient { CanConnect = false };

        Assert.False(await fake.ConnectAsync(new RuntimeConnectionTarget()));
        Assert.Equal(RuntimeClientState.Disconnected, fake.State);
    }

    [Fact]
    public async Task Fake_Stop_XaOutputVe0()
    {
        await using var fake = new FakeRuntimeClient();
        await fake.ConnectAsync(new RuntimeConnectionTarget());
        await fake.DeployAsync(Array.Empty<byte>(), SampleAssembly.ConveyorRoutes(), SampleAssembly.SimulationDevice());
        await fake.SubscribeTagsAsync(new[] { "ConveyorRun" });

        fake.SetTagValue("ConveyorRun", true);

        var received = new List<TagValueUpdate>();
        fake.TagValueChanged += (_, u) => received.Add(u);

        await fake.StopAsync();

        Assert.Contains(received, u => u.TagName == "ConveyorRun" && u.ValueJson == "false");
    }

    // ── RuntimeProcessLauncher ───────────────────────────────────────────────────

    [Fact]
    public async Task Launcher_DaCoRuntimeChay_KhongKhoiDongThem()
    {
        await using var fixture = new IpcFixture();
        var launcher = new RecordingLauncher();

        // Điều kiện tiên quyết của test: server đã thực sự lắng nghe. IpcFixture khởi động vòng
        // accept trên task riêng nên có độ trễ — không chờ thì test thành phép đo tốc độ máy.
        await WaitUntilAsync(() => launcher.IsPipeAvailable(fixture.PipeName), Timeout);
        Assert.True(launcher.IsPipeAvailable(fixture.PipeName), "Runtime giả chưa kịp lắng nghe.");

        var result = await launcher.EnsureRunningAsync(new RuntimeConnectionTarget(fixture.PipeName));

        Assert.Equal(LaunchOutcome.AlreadyRunning, result.Outcome);
        Assert.Equal(0, launcher.StartCount);
        Assert.True(result.CanConnect);
    }

    [Fact]
    public async Task Launcher_TargetORemote_KhongTuKhoiDong()
    {
        var launcher = new RecordingLauncher();

        var result = await launcher.EnsureRunningAsync(
            new RuntimeConnectionTarget(Host: "192.168.1.50"));

        Assert.Equal(LaunchOutcome.SkippedRemote, result.Outcome);
        Assert.Equal(0, launcher.StartCount);
        Assert.Contains("192.168.1.50", result.Message);
    }

    [Fact]
    public async Task Launcher_KhongTimThayTepThucThi_BaoLoiRoRang()
    {
        var launcher = new RecordingLauncher
        {
            SearchDirectory = Path.Combine(Path.GetTempPath(), "khong-ton-tai-" + Guid.NewGuid().ToString("N")),
            ExecutableName = "DBI.Controller.Runtime"
        };

        var result = await launcher.EnsureRunningAsync(
            new RuntimeConnectionTarget("DBI.Test.KhongCo." + Guid.NewGuid().ToString("N")));

        Assert.Equal(LaunchOutcome.Failed, result.Outcome);
        Assert.False(result.CanConnect);
        Assert.Contains("DBI.Controller.Runtime", result.Message);
    }

    [Fact]
    public void Launcher_KhongCoDuongTatMay()
    {
        // Studio KHÔNG BAO GIỜ giết Runtime — đóng Studio thì máy phải chạy tiếp (ADR-001).
        var methods = typeof(RuntimeProcessLauncher)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();

        Assert.DoesNotContain(methods, name =>
            name.Contains("Kill", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Terminate", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Shutdown", StringComparison.OrdinalIgnoreCase));

        // Và phải nói thẳng với người vận hành rằng đóng Studio không dừng máy.
        Assert.Contains("không dừng máy", RuntimeProcessLauncher.ClosingWhileRunningWarning,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(".", true)]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("192.168.1.50", false)]
    [InlineData("edge-device-01", false)]
    public void Target_PhanBietCucBoVaTuXa(string host, bool expectedLocal)
    {
        Assert.Equal(expectedLocal, new RuntimeConnectionTarget(Host: host).IsLocal);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(20);
        }
    }
}

/// <summary>Đếm số lần thật sự khởi động tiến trình, và không bao giờ khởi động thật trong test.</summary>
internal sealed class RecordingLauncher : RuntimeProcessLauncher
{
    public int StartCount { get; private set; }

    protected override System.Diagnostics.Process? StartProcess(string executable, RuntimeConnectionTarget target)
    {
        StartCount++;
        return null;
    }
}
