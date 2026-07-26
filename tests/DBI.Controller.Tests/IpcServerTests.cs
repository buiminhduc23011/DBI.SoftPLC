using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;
using DBI.Controller.Runtime.Drivers;
using DBI.Controller.Runtime.Host;
using DBI.Controller.Runtime.Ipc;

namespace DBI.Controller.Tests;

/// <summary>Driver luôn ném lỗi khi đọc — để dựng tình huống Fault end-to-end mà không cần phần cứng.</summary>
internal sealed class ThrowingDriver : IDriver
{
    public ThrowingDriver(string driverId) => DriverId = driverId;

    public string DriverId { get; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? LastError => "Thiết bị giả lập lỗi.";

    public Task ConnectAsync(CancellationToken ct = default)
    {
        State = ConnectionState.Connected;
        return Task.CompletedTask;
    }

    public Task ReadInputsAsync(IMemoryImage memory, IReadOnlyList<TagRoute> routes, CancellationToken ct = default) =>
        throw new InvalidOperationException("Băng tải mất tín hiệu encoder.");

    public Task WriteOutputsAsync(IMemoryImage memory, IReadOnlyList<TagRoute> routes, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        State = ConnectionState.Disconnected;
        return Task.CompletedTask;
    }
}

/// <summary>Dựng cặp Runtime ↔ Studio thật, nói chuyện qua NamedPipe thật.</summary>
internal sealed class IpcFixture : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly TempStore _temp = new();
    private readonly Task _serverLoop;

    public IpcFixture(DriverFactory? driverFactory = null, int scanIntervalMs = 5, int pushIntervalMs = 20)
    {
        PipeName = "DBI.Test." + Guid.NewGuid().ToString("N");

        Host = new RuntimeHost(_temp.Store, driverFactory: driverFactory, scanIntervalMs: scanIntervalMs);
        Server = new IpcServer(Host, PipeName) { PushIntervalMs = pushIntervalMs };

        _serverLoop = Task.Run(() => Server.RunAsync(_cts.Token));
    }

    public string PipeName { get; }
    public RuntimeHost Host { get; }
    public IpcServer Server { get; }

    public async Task<NamedPipeTransport> ConnectClientAsync()
    {
        var client = new NamedPipeTransport(PipeName);
        await client.ConnectAsync(_cts.Token);
        return client;
    }

    public static IpcRequest Request(CommandType type, object? payload = null) =>
        new(Guid.NewGuid().ToString("N"), type, payload is null ? null : ProtocolJson.Serialize(payload));

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        try { await _serverLoop; }
        catch (OperationCanceledException) { }

        await Server.DisposeAsync();
        Host.Dispose();
        _cts.Dispose();
        _temp.Dispose();
    }
}

public class IpcServerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    // ── Handshake ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handshake_DungProtocolVersion_TraVeTrangThaiRuntime()
    {
        await using var fixture = new IpcFixture();
        await using var client = await fixture.ConnectClientAsync();

        var response = await client.SendAsync(IpcFixture.Request(
            CommandType.Handshake,
            new HandshakeRequest("1.0.0", ProtocolConstants.ProtocolVersion)));

        Assert.True(response.Ok, response.Error);

        var handshake = ProtocolJson.Deserialize<HandshakeResponse>(response.PayloadJson);
        Assert.Equal(ProtocolConstants.ProtocolVersion, handshake!.ProtocolVersion);
        Assert.Equal(RuntimeState.NoProgram, handshake.State);
    }

    [Fact]
    public async Task Handshake_LechProtocolVersion_TuChoiKemThongBaoRo()
    {
        await using var fixture = new IpcFixture();
        await using var client = await fixture.ConnectClientAsync();

        var response = await client.SendAsync(IpcFixture.Request(
            CommandType.Handshake, new HandshakeRequest("9.9.9", ProtocolConstants.ProtocolVersion + 1)));

        Assert.False(response.Ok);
        Assert.Contains(ErrorCodes.ProtocolMismatch, response.Error);
        Assert.Contains($"v{ProtocolConstants.ProtocolVersion}", response.Error);
    }

    // ── Deploy end-to-end qua pipe ───────────────────────────────────────────────

    [Fact]
    public async Task Deploy_QuaIpc_RuntimeChayVaCycleCountTang()
    {
        await using var fixture = new IpcFixture();
        await using var client = await fixture.ConnectClientAsync();

        var deployResponse = await client.SendAsync(IpcFixture.Request(
            CommandType.Deploy, SampleAssembly.Deploy()));

        Assert.True(deployResponse.Ok, deployResponse.Error);

        var status = await PollUntilAsync(client, s => s.State == RuntimeState.Running && s.CycleCount >= 5);

        Assert.Equal(RuntimeState.Running, status.State);
        Assert.True(status.CycleCount >= 5);
    }

    [Fact]
    public async Task Deploy_HotReload_TraVeErrorCodeQuaIpc()
    {
        await using var fixture = new IpcFixture();
        await using var client = await fixture.ConnectClientAsync();

        var response = await client.SendAsync(IpcFixture.Request(
            CommandType.Deploy, SampleAssembly.Deploy(SwapMode.HotReload)));

        Assert.False(response.Ok);
        Assert.Contains(ErrorCodes.HotReloadUnsupported, response.Error);
    }

    [Fact]
    public async Task StartStop_QuaIpc_DoiTrangThaiDung()
    {
        await using var fixture = new IpcFixture();
        await using var client = await fixture.ConnectClientAsync();

        await client.SendAsync(IpcFixture.Request(CommandType.Deploy, SampleAssembly.Deploy()));

        var stopped = await client.SendAsync(IpcFixture.Request(CommandType.Stop));
        Assert.Equal(RuntimeState.Stopped,
            ProtocolJson.Deserialize<StatusResponse>(stopped.PayloadJson)!.State);

        var started = await client.SendAsync(IpcFixture.Request(CommandType.Start));
        Assert.Equal(RuntimeState.Running,
            ProtocolJson.Deserialize<StatusResponse>(started.PayloadJson)!.State);
    }

    [Fact]
    public async Task GetDeviceStates_TraVeTrangThaiTungThietBi()
    {
        await using var fixture = new IpcFixture();
        await using var client = await fixture.ConnectClientAsync();

        await client.SendAsync(IpcFixture.Request(CommandType.Deploy, SampleAssembly.Deploy()));

        var response = await client.SendAsync(IpcFixture.Request(CommandType.GetDeviceStates));
        var devices = ProtocolJson.Deserialize<DeviceStatesResponse>(response.PayloadJson);

        var device = Assert.Single(devices!.Devices);
        Assert.Equal("SIM", device.DriverId);
        Assert.Equal(nameof(ConnectionState.Connected), device.State);
    }

    // ── SubscribeTags ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubscribeTags_ChiDayTagDaDangKy()
    {
        await using var fixture = new IpcFixture();
        await using var client = await fixture.ConnectClientAsync();

        await client.SendAsync(IpcFixture.Request(CommandType.Deploy, SampleAssembly.Deploy()));
        await client.SendAsync(IpcFixture.Request(
            CommandType.SubscribeTags, new SubscribeTagsRequest(new List<string> { "ConveyorRun" })));

        var simulation = (Driver.Simulation.SimulationDriver)fixture.Host.Drivers.Drivers.Single();
        simulation.SetInputBool("StartButton", true);

        var updates = await CollectPushesAsync(client, ProtocolConstants.TagChannel, count: 1);

        Assert.All(updates, u => Assert.Equal("ConveyorRun", u.TagName));
        Assert.Contains(updates, u => u.ValueJson == "true");
    }

    [Fact]
    public async Task SubscribeTags_ChiDayKhiGiaTriThayDoi()
    {
        await using var fixture = new IpcFixture(pushIntervalMs: 20);
        await using var client = await fixture.ConnectClientAsync();

        await client.SendAsync(IpcFixture.Request(CommandType.Deploy, SampleAssembly.Deploy()));

        // Cho tag có giá trị trước đã — tag chưa lần nào được ghi thì chưa tồn tại trong snapshot.
        var simulation = (Driver.Simulation.SimulationDriver)fixture.Host.Drivers.Drivers.Single();
        simulation.SetInputBool("StartButton", true);
        await WaitUntilAsync(() => simulation.GetOutputBool("ConveyorRun"), Timeout);

        await client.SendAsync(IpcFixture.Request(
            CommandType.SubscribeTags, new SubscribeTagsRequest(new List<string> { "ConveyorRun" })));

        // Lần đầu luôn có (gửi trạng thái hiện tại)...
        await CollectPushesAsync(client, ProtocolConstants.TagChannel, count: 1);

        // ...sau đó giá trị đứng yên nên phải im lặng, dù vòng push vẫn chạy 20ms một nhịp.
        var extra = await TryCollectPushesAsync(
            client, ProtocolConstants.TagChannel, TimeSpan.FromMilliseconds(500));

        Assert.Empty(extra);
    }

    // ── Nhiều client ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClientThuHai_BiTuChoiKemLyDoRoRang()
    {
        await using var fixture = new IpcFixture();
        await using var first = await fixture.ConnectClientAsync();

        await first.SendAsync(IpcFixture.Request(CommandType.GetStatus));

        await using var second = await fixture.ConnectClientAsync();

        string? refusal = null;
        second.Disconnected += (_, reason) => refusal = reason;

        // Client thứ hai nhận ngay một bản tin từ chối rồi bị đóng kết nối.
        await Task.WhenAny(
            Task.Run(async () =>
            {
                try { await second.SendAsync(IpcFixture.Request(CommandType.GetStatus)); }
                catch (IOException) { }
            }),
            Task.Delay(TimeSpan.FromSeconds(3)));

        await WaitUntilAsync(() => refusal is not null || !second.IsConnected, TimeSpan.FromSeconds(3));

        Assert.True(fixture.Server.HasClient);
    }

    [Fact]
    public async Task ClientNgatDotNgot_RuntimeVanChay_VaNhanLaiKetNoiMoi()
    {
        await using var fixture = new IpcFixture();

        var client = await fixture.ConnectClientAsync();
        await client.SendAsync(IpcFixture.Request(CommandType.Deploy, SampleAssembly.Deploy()));

        long cyclesBefore = fixture.Host.Metrics.CycleCount;

        // Kill Studio giữa chừng.
        await client.DisposeAsync();
        await WaitUntilAsync(() => !fixture.Server.HasClient, TimeSpan.FromSeconds(5));

        // Máy vẫn phải chạy — mất Studio không được làm dừng dây chuyền.
        Assert.Equal(RuntimeState.Running, fixture.Host.State);
        await WaitUntilAsync(() => fixture.Host.Metrics.CycleCount > cyclesBefore, TimeSpan.FromSeconds(5));

        // Và nhận lại kết nối mới.
        await using var reconnected = await fixture.ConnectClientAsync();
        var status = await reconnected.SendAsync(IpcFixture.Request(CommandType.GetStatus));

        Assert.True(status.Ok);
        Assert.Equal(RuntimeState.Running,
            ProtocolJson.Deserialize<StatusResponse>(status.PayloadJson)!.State);
    }

    // ── Fault ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Fault_DayLenStudioQuaKenhFault_CoCauTruc()
    {
        var factory = new DriverFactory();
        factory.Register("Faulting", spec => new ThrowingDriver(spec.Name));

        await using var fixture = new IpcFixture(factory);
        await using var client = await fixture.ConnectClientAsync();

        var faults = new List<FaultNotification>();
        var received = new TaskCompletionSource();

        _ = Task.Run(async () =>
        {
            await foreach (var push in client.ReceivePushAsync(CancellationToken.None))
            {
                if (push.Channel != ProtocolConstants.FaultChannel) continue;

                faults.Add(ProtocolJson.Deserialize<FaultNotification>(push.PayloadJson)!);
                received.TrySetResult();
            }
        });

        await client.SendAsync(IpcFixture.Request(CommandType.Deploy, new DeployRequest(
            SampleAssembly.Bytes,
            SampleAssembly.ConveyorRoutes("BAD"),
            new List<DeviceSpec> { new("BAD", "Faulting", new Dictionary<string, string>()) })));

        await Task.WhenAny(received.Task, Task.Delay(Timeout));

        var fault = Assert.Single(faults);
        Assert.Contains("encoder", fault.Message);
        Assert.NotNull(fault.StackTrace);
        Assert.True(fault.OccurredAtMs > 0);

        await WaitUntilAsync(() => fixture.Host.State == RuntimeState.Faulted, TimeSpan.FromSeconds(3));
        Assert.Equal(RuntimeState.Faulted, fixture.Host.State);
    }

    [Fact]
    public async Task Fault_RoiReset_DuaMayVeStoppedKhongTuChay()
    {
        var factory = new DriverFactory();
        factory.Register("Faulting", spec => new ThrowingDriver(spec.Name));

        await using var fixture = new IpcFixture(factory);
        await using var client = await fixture.ConnectClientAsync();

        await client.SendAsync(IpcFixture.Request(CommandType.Deploy, new DeployRequest(
            SampleAssembly.Bytes,
            SampleAssembly.ConveyorRoutes("BAD"),
            new List<DeviceSpec> { new("BAD", "Faulting", new Dictionary<string, string>()) })));

        await WaitUntilAsync(() => fixture.Host.State == RuntimeState.Faulted, Timeout);

        var reset = await client.SendAsync(IpcFixture.Request(CommandType.Reset));
        var status = ProtocolJson.Deserialize<StatusResponse>(reset.PayloadJson)!;

        Assert.Equal(RuntimeState.Stopped, status.State);
    }

    // ── Tiện ích ─────────────────────────────────────────────────────────────────

    private static async Task<StatusResponse> PollUntilAsync(
        NamedPipeTransport client, Func<StatusResponse, bool> predicate)
    {
        var deadline = DateTime.UtcNow + Timeout;
        StatusResponse? last = null;

        while (DateTime.UtcNow < deadline)
        {
            var response = await client.SendAsync(IpcFixture.Request(CommandType.GetStatus));
            last = ProtocolJson.Deserialize<StatusResponse>(response.PayloadJson);

            if (last is not null && predicate(last)) return last;

            await Task.Delay(20);
        }

        Assert.Fail($"Hết thời gian chờ. Trạng thái cuối: {last}");
        return last!;
    }

    private static async Task<List<TagValueUpdate>> CollectPushesAsync(
        NamedPipeTransport client, string channel, int count)
    {
        var collected = await TryCollectPushesAsync(client, channel, Timeout, count);

        Assert.True(collected.Count >= count, $"Chỉ nhận được {collected.Count}/{count} bản tin push.");
        return collected;
    }

    private static async Task<List<TagValueUpdate>> TryCollectPushesAsync(
        NamedPipeTransport client, string channel, TimeSpan window, int stopAfter = int.MaxValue)
    {
        var collected = new List<TagValueUpdate>();
        using var cts = new CancellationTokenSource(window);

        try
        {
            await foreach (var push in client.ReceivePushAsync(cts.Token))
            {
                if (push.Channel != channel) continue;

                var batch = ProtocolJson.Deserialize<TagValueBatch>(push.PayloadJson);
                if (batch is not null) collected.AddRange(batch.Updates);

                if (collected.Count >= stopAfter) break;
            }
        }
        catch (OperationCanceledException)
        {
            // Hết cửa sổ quan sát.
        }

        return collected;
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
