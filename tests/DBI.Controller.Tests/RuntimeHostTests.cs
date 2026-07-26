using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;
using DBI.Controller.Runtime.Host;
using Sample.Conveyor;

namespace DBI.Controller.Tests;

/// <summary>
/// Assembly người dùng thật để nạp trong test — dùng luôn <c>Sample.Conveyor.dll</c> đã build,
/// nó có sẵn một class kế thừa <c>ControllerProgram</c>.
/// </summary>
internal static class SampleAssembly
{
    public static byte[] Bytes => File.ReadAllBytes(typeof(ConveyorProgram).Assembly.Location);

    /// <summary>Assembly hợp lệ nhưng không có class nào kế thừa <c>ControllerProgram</c>.</summary>
    public static byte[] WithoutControllerProgram => File.ReadAllBytes(typeof(TagRoute).Assembly.Location);

    public static List<TagRoute> ConveyorRoutes(string device = "SIM") => new()
    {
        new TagRoute("StartButton", TagDataType.Bool, TagDirection.Input, device, "0"),
        new TagRoute("StopButton", TagDataType.Bool, TagDirection.Input, device, "1"),
        new TagRoute("SensorProduct", TagDataType.Bool, TagDirection.Input, device, "2"),
        new TagRoute("ConveyorRun", TagDataType.Bool, TagDirection.Output, device, "0"),
    };

    public static List<DeviceSpec> SimulationDevice(string device = "SIM") => new()
    {
        new DeviceSpec(device, "Simulation", new Dictionary<string, string>())
    };

    public static DeployRequest Deploy(SwapMode mode = SwapMode.ColdRestart) =>
        new(Bytes, ConveyorRoutes(), SimulationDevice(), mode);
}

/// <summary>Thư mục last-deploy riêng cho từng test — không đụng %PROGRAMDATA% thật.</summary>
internal sealed class TempStore : IDisposable
{
    public string Root { get; } = Path.Combine(
        Path.GetTempPath(), "dbi-runtime-tests", Guid.NewGuid().ToString("N"));

    public DeploymentStore Store => new(Root);

    public void Dispose()
    {
        try { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
    }
}

public class RuntimeHostTests
{
    // ── B-4: Runtime thật sự chạy scan ───────────────────────────────────────────

    [Fact]
    public async Task Deploy_NapAssemblyRoiChayScan_CycleCountTang()
    {
        using var temp = new TempStore();
        using var host = new RuntimeHost(temp.Store, scanIntervalMs: 5);

        var result = await host.DeployAsync(SampleAssembly.Deploy());

        Assert.True(result.Ok, result.Error);
        Assert.Equal(RuntimeState.Running, host.State);

        await WaitForCyclesAsync(host, atLeast: 5);

        var status = host.GetStatus();
        Assert.Equal(RuntimeState.Running, status.State);
        Assert.True(status.CycleCount >= 5, $"CycleCount = {status.CycleCount}");
    }

    [Fact]
    public async Task TruocKhiDeploy_TrangThaiLaNoProgram_VaStartNemLoiRoRang()
    {
        using var temp = new TempStore();
        using var host = new RuntimeHost(temp.Store);

        Assert.Equal(RuntimeState.NoProgram, host.State);

        var ex = Assert.Throws<InvalidOperationException>(() => host.Start());
        Assert.Contains("chương trình", ex.Message, StringComparison.OrdinalIgnoreCase);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Deploy_LogicChayThat_OutputPhanAnhInput()
    {
        using var temp = new TempStore();
        using var host = new RuntimeHost(temp.Store, scanIntervalMs: 5);

        await host.DeployAsync(SampleAssembly.Deploy());
        await WaitForCyclesAsync(host, atLeast: 3);

        var simulation = (Driver.Simulation.SimulationDriver)host.Drivers.Drivers.Single();
        simulation.SetInputBool("StartButton", true);

        await WaitUntilAsync(() => simulation.GetOutputBool("ConveyorRun"), TimeSpan.FromSeconds(2));

        Assert.True(simulation.GetOutputBool("ConveyorRun"));
    }

    // ── Deploy hỏng ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deploy_AssemblyKhongCoControllerProgram_BaoLoiRoRangKhongCrash()
    {
        using var temp = new TempStore();
        using var host = new RuntimeHost(temp.Store);

        var result = await host.DeployAsync(new DeployRequest(
            SampleAssembly.WithoutControllerProgram, new List<TagRoute>(), new List<DeviceSpec>()));

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.NoProgramEntryPoint, result.ErrorCode);
        Assert.Contains("ControllerProgram", result.Error);
        Assert.Equal(RuntimeState.NoProgram, host.State);
    }

    [Fact]
    public async Task Deploy_AssemblyRac_BaoLoiKhongCrash()
    {
        using var temp = new TempStore();
        using var host = new RuntimeHost(temp.Store);

        var result = await host.DeployAsync(new DeployRequest(
            new byte[] { 1, 2, 3, 4 }, new List<TagRoute>(), new List<DeviceSpec>()));

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.AssemblyLoadFailed, result.ErrorCode);
        Assert.Equal(RuntimeState.NoProgram, host.State);
    }

    [Fact]
    public async Task Deploy_LoaiDriverLa_BaoLoiKemDanhSachHoTro()
    {
        using var temp = new TempStore();
        using var host = new RuntimeHost(temp.Store);

        var result = await host.DeployAsync(new DeployRequest(
            SampleAssembly.Bytes,
            new List<TagRoute>(),
            new List<DeviceSpec> { new("PLC", "Siemens_S7", new Dictionary<string, string>()) }));

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.UnknownDriver, result.ErrorCode);
        Assert.Contains("Siemens_S7", result.Error);
    }

    // ── ADR-004: IProgramSwapper ─────────────────────────────────────────────────

    [Fact]
    public async Task Deploy_HotReload_TraVeErrorCodeRoRang()
    {
        using var temp = new TempStore();
        using var host = new RuntimeHost(temp.Store);

        var result = await host.DeployAsync(SampleAssembly.Deploy(SwapMode.HotReload));

        Assert.False(result.Ok);
        Assert.Equal(ErrorCodes.HotReloadUnsupported, result.ErrorCode);
        Assert.Contains("Cold Restart", result.Error);
    }

    [Fact]
    public async Task RuntimeHost_DiQuaIProgramSwapper_KhongGoiThangUserProgramLoader()
    {
        using var temp = new TempStore();
        var swapper = new RecordingSwapper();
        using var host = new RuntimeHost(temp.Store, swapper);

        await host.DeployAsync(SampleAssembly.Deploy());

        Assert.Equal(1, swapper.SwapCount);
    }

    // ── Stop: xả output xuống thiết bị thật (TC-D01) ─────────────────────────────

    [Fact]
    public async Task Stop_XaOutputXuongThietBiThat()
    {
        using var temp = new TempStore();
        using var host = new RuntimeHost(temp.Store, scanIntervalMs: 5);

        await host.DeployAsync(SampleAssembly.Deploy());

        var simulation = (Driver.Simulation.SimulationDriver)host.Drivers.Drivers.Single();
        simulation.SetInputBool("StartButton", true);
        await WaitUntilAsync(() => simulation.GetOutputBool("ConveyorRun"), TimeSpan.FromSeconds(2));

        host.Stop();

        Assert.Equal(RuntimeState.Stopped, host.State);
        Assert.False(simulation.GetOutputBool("ConveyorRun"));
    }

    [Fact]
    public async Task Deploy_KhiDangChay_DungVaXaOutputTruocKhiNapChuongTrinhMoi()
    {
        using var temp = new TempStore();
        var swapper = new RecordingSwapper();
        using var host = new RuntimeHost(temp.Store, swapper, scanIntervalMs: 5);

        await host.DeployAsync(SampleAssembly.Deploy());

        var simulation = (Driver.Simulation.SimulationDriver)host.Drivers.Drivers.Single();
        simulation.SetInputBool("StartButton", true);
        await WaitUntilAsync(() => simulation.GetOutputBool("ConveyorRun"), TimeSpan.FromSeconds(2));

        swapper.OnSwap = () => Assert.False(
            simulation.GetOutputBool("ConveyorRun"),
            "Output phải xả về 0 TRƯỚC khi nạp chương trình mới — nếu không băng tải vẫn quay.");

        await host.DeployAsync(SampleAssembly.Deploy());
    }

    // ── ADR-005: lưu last-deploy + hai chốt chặn ─────────────────────────────────

    [Fact]
    public async Task Deploy_LuuLastDeployKemChecksum()
    {
        using var temp = new TempStore();
        var store = temp.Store;

        using (var host = new RuntimeHost(store))
        {
            await host.DeployAsync(SampleAssembly.Deploy());
        }

        var stored = store.Load();

        Assert.NotNull(stored);
        Assert.Equal(SampleAssembly.Bytes.Length, stored!.AssemblyBytes.Length);
        Assert.Equal(4, stored.Manifest.TagRoutes.Count);
        Assert.Single(stored.Manifest.Devices);
    }

    [Fact]
    public async Task LastDeploy_ChecksumSai_CoiNhuChuaCoChuongTrinh()
    {
        using var temp = new TempStore();
        var store = temp.Store;

        using (var host = new RuntimeHost(store))
        {
            await host.DeployAsync(SampleAssembly.Deploy());
        }

        // Giả lập DLL hỏng trên đĩa.
        string dll = Path.Combine(store.RootDirectory, "program.dll");
        File.WriteAllBytes(dll, File.ReadAllBytes(dll).Concat(new byte[] { 0xFF }).ToArray());

        Assert.Null(store.Load());
        Assert.Equal(RuntimeState.NoProgram, store.DecideStartupState(store.Load()));
    }

    [Fact]
    public async Task LastCleanState_GhiMoiLanDoiTrangThai_KhongPhaiLucTat()
    {
        using var temp = new TempStore();
        var store = temp.Store;

        using var host = new RuntimeHost(store, scanIntervalMs: 5);
        await host.DeployAsync(SampleAssembly.Deploy());

        // Đang chạy — mất điện lúc này thì manifest trên đĩa phải đã ghi Running.
        Assert.Equal(RuntimeState.Running, store.Load()!.Manifest.LastCleanState);

        host.Stop();
        Assert.Equal(RuntimeState.Stopped, store.Load()!.Manifest.LastCleanState);
    }

    [Fact]
    public async Task ChotChan1_LanTruocFault_KhoiDongOStopped()
    {
        using var temp = new TempStore();
        var store = temp.Store;

        using (var host = new RuntimeHost(store))
        {
            await host.DeployAsync(SampleAssembly.Deploy());
        }

        store.UpdateLastCleanState(RuntimeState.Faulted);

        // Máy vừa hỏng vì logic — tự chạy lại chỉ tạo vòng lặp fault.
        Assert.Equal(RuntimeState.Stopped, store.DecideStartupState(store.Load()));
    }

    [Fact]
    public async Task ChotChan2_CoTepNoRun_LuonKhoiDongOStopped()
    {
        using var temp = new TempStore();
        var store = temp.Store;

        using (var host = new RuntimeHost(store))
        {
            await host.DeployAsync(SampleAssembly.Deploy());
        }

        store.UpdateLastCleanState(RuntimeState.Running);
        Assert.Equal(RuntimeState.Running, store.DecideStartupState(store.Load()));

        // Thợ bảo trì kéo phanh tay.
        File.WriteAllText(store.NoRunFilePath, "");

        Assert.True(store.HasNoRunFile);
        Assert.Equal(RuntimeState.Stopped, store.DecideStartupState(store.Load()));
    }

    [Fact]
    public async Task Autostart_LanTruocDangChay_ChecksumDung_KhongNoRun_ThiTuChay()
    {
        using var temp = new TempStore();
        var store = temp.Store;

        using (var host = new RuntimeHost(store, scanIntervalMs: 5))
        {
            await host.DeployAsync(SampleAssembly.Deploy());
            Assert.Equal(RuntimeState.Running, host.State);
        }

        store.UpdateLastCleanState(RuntimeState.Running);

        using var restarted = new RuntimeHost(temp.Store, scanIntervalMs: 5);
        bool autoStarted = await restarted.TryRestoreLastDeploymentAsync();

        Assert.True(autoStarted);
        Assert.Equal(RuntimeState.Running, restarted.State);
    }

    [Fact]
    public async Task Autostart_CoTepNoRun_NapChuongTrinhNhungKhongChay()
    {
        using var temp = new TempStore();
        var store = temp.Store;

        using (var host = new RuntimeHost(store, scanIntervalMs: 5))
        {
            await host.DeployAsync(SampleAssembly.Deploy());
        }

        store.UpdateLastCleanState(RuntimeState.Running);
        File.WriteAllText(store.NoRunFilePath, "");

        using var restarted = new RuntimeHost(temp.Store, scanIntervalMs: 5);
        bool autoStarted = await restarted.TryRestoreLastDeploymentAsync();

        Assert.False(autoStarted);
        Assert.Equal(RuntimeState.Stopped, restarted.State);
    }

    [Fact]
    public async Task Autostart_ChuaTungDeploy_TraVeNoProgram()
    {
        using var temp = new TempStore();
        using var host = new RuntimeHost(temp.Store);

        Assert.False(await host.TryRestoreLastDeploymentAsync());
        Assert.Equal(RuntimeState.NoProgram, host.State);
    }

    // ── Reset ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reset_XoaFaultVaDeMayODung_KhongTuChayLai()
    {
        using var temp = new TempStore();
        using var host = new RuntimeHost(temp.Store, scanIntervalMs: 5);

        await host.DeployAsync(SampleAssembly.Deploy());
        host.Stop();

        host.Reset();

        Assert.Equal(RuntimeState.Stopped, host.State);
    }

    // ── Tiện ích ─────────────────────────────────────────────────────────────────

    private static Task WaitForCyclesAsync(RuntimeHost host, long atLeast) =>
        WaitUntilAsync(() => host.Metrics.CycleCount >= atLeast, TimeSpan.FromSeconds(5));

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.True(condition(), "Hết thời gian chờ điều kiện.");
    }
}

/// <summary>Ghi lại số lần swap để khẳng định <c>RuntimeHost</c> đi qua interface, không gọi loader thẳng.</summary>
internal sealed class RecordingSwapper : IProgramSwapper
{
    private readonly ColdRestartSwapper _inner = new();

    public int SwapCount { get; private set; }

    /// <summary>Chạy ngay trước khi nạp chương trình mới — để kiểm tra thứ tự Stop.</summary>
    public Action? OnSwap { get; set; }

    public Task<SwapResult> SwapAsync(
        byte[] assemblyBytes, SwapMode mode, Core.Interfaces.IMemoryImage memoryImage, CancellationToken ct = default)
    {
        SwapCount++;
        OnSwap?.Invoke();

        return _inner.SwapAsync(assemblyBytes, mode, memoryImage, ct);
    }

    public void Unload() => _inner.Unload();
}
