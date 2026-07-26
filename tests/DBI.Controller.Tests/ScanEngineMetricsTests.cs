using System.Diagnostics;
using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;
using DBI.Controller.Runtime.Drivers;
using DBI.Controller.Runtime.Engine;
using DBI.Controller.Runtime.Safety;
using DBI.Controller.SDK;

namespace DBI.Controller.Tests;

internal sealed class BusyProgram : ControllerProgram
{
    private readonly TimeSpan _work;

    public BusyProgram(TimeSpan work) => _work = work;

    public override void Execute()
    {
        var spin = Stopwatch.StartNew();
        while (spin.Elapsed < _work) { }
    }
}

internal sealed class ThrowingProgram : ControllerProgram
{
    public override void Execute() => throw new InvalidOperationException("Chia cho 0 ở khối Conveyor.");
}

public class ScanEngineMetricsTests
{
    private static ScanEngine NewEngine(ControllerProgram program, int intervalMs, out SafetyCatchManager safety)
    {
        safety = new SafetyCatchManager { WriteToConsole = false };

        var memory = new MemorySnapshot();
        var engine = new ScanEngine(memory, new DriverManager(), safety) { ScanIntervalMs = intervalMs };

        program.Initialize(memory);
        engine.SetProgram(program);

        return engine;
    }

    // ── Công thức jitter ─────────────────────────────────────────────────────────

    [Fact]
    public void Jitter_LogicChayNhanh_KhongBiBaoJitterLon()
    {
        // Công thức cũ |thời gian thực thi - chu kỳ| báo jitter 19ms cho một chu kỳ 20ms đều tăm tắp
        // chỉ vì logic chạy hết 1ms. Đó là đo thời gian thực thi, không phải jitter.
        var engine = NewEngine(new BusyProgram(TimeSpan.Zero), intervalMs: 20, out _);

        engine.Start();
        Thread.Sleep(600);
        engine.Stop();

        var metrics = engine.Metrics;

        Assert.True(metrics.CycleCount >= 20, $"Chỉ chạy được {metrics.CycleCount} chu kỳ.");
        Assert.True(metrics.LastScanMs < 5, $"Thời gian thực thi {metrics.LastScanMs:F2}ms — logic rỗng phải gần 0.");
        Assert.True(metrics.AverageJitterMs < 5, $"Jitter trung bình {metrics.AverageJitterMs:F2}ms.");
    }

    [Fact]
    public void ScanTime_DoDungThoiGianThucThiCuaLogic()
    {
        var engine = NewEngine(new BusyProgram(TimeSpan.FromMilliseconds(8)), intervalMs: 20, out _);

        engine.Start();
        Thread.Sleep(400);
        engine.Stop();

        var metrics = engine.Metrics;

        Assert.InRange(metrics.LastScanMs, 6, 20);
        Assert.True(metrics.MaxScanMs >= metrics.LastScanMs);
    }

    [Fact]
    public void ChuKy_KhongBiCatCutPhanThapPhan()
    {
        // Thread.Sleep((int)(20 - 0.7)) cũ ngủ 19ms thay vì 19.3ms — lệch tích luỹ dần.
        // Deadline tuyệt đối phải giữ tổng thời gian đúng.
        var engine = NewEngine(new BusyProgram(TimeSpan.Zero), intervalMs: 20, out _);

        var clock = Stopwatch.StartNew();
        engine.Start();
        Thread.Sleep(1000);
        engine.Stop();
        clock.Stop();

        long cycles = engine.Metrics.CycleCount;
        double expected = clock.Elapsed.TotalMilliseconds / 20.0;

        // Cho phép lệch 15% vì có chi phí start/stop, nhưng chu kỳ 19ms sẽ lệch ~5% đều đặn một phía.
        Assert.InRange(cycles, expected * 0.85, expected * 1.15);
    }

    [Fact]
    public void Metrics_LaBanGhiBatBien_DocTuThreadKhacKhongBiTornRead()
    {
        var engine = NewEngine(new BusyProgram(TimeSpan.Zero), intervalMs: 5, out _);

        engine.Start();

        // Chụp 200 lần từ thread khác trong lúc scan thread đang ghi liên tục.
        for (int i = 0; i < 200; i++)
        {
            var snapshot = engine.Metrics;

            // Bất biến nội tại: max không bao giờ nhỏ hơn giá trị hiện tại của cùng ảnh chụp.
            Assert.True(snapshot.MaxScanMs >= snapshot.LastScanMs);
            Assert.True(snapshot.MaxJitterMs >= snapshot.JitterMs);
        }

        engine.Stop();
    }

    [Fact]
    public void ResetMetrics_DuaVeKhong()
    {
        var engine = NewEngine(new BusyProgram(TimeSpan.Zero), intervalMs: 5, out _);

        engine.Start();
        Thread.Sleep(100);
        engine.Stop();

        Assert.True(engine.Metrics.CycleCount > 0);

        engine.ResetMetrics();

        Assert.Equal(0, engine.Metrics.CycleCount);
        Assert.Equal(0, engine.Metrics.MaxScanMs);
    }

    // ── Determinism ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Chạy dài với vòng push IPC bật và client poll status — đúng kịch bản đáng lo nhất của
    /// ADR-001: Studio làm phiền Runtime tới mức nào.
    /// </summary>
    /// <remarks>
    /// Mặc định 5 giây để <c>dotnet test</c> không bị kéo dài. Đặt biến môi trường
    /// <c>DBI_DETERMINISM_SECONDS=60</c> để chạy đủ 60 giây như DoD mô tả trước khi bàn giao.
    /// </remarks>
    [Fact]
    public async Task Determinism_PushLoopBatVaClientPoll_JitterVanNho()
    {
        int seconds = int.TryParse(
            Environment.GetEnvironmentVariable("DBI_DETERMINISM_SECONDS"), out int configured)
            ? configured
            : 5;

        await using var fixture = new IpcFixture(scanIntervalMs: 20, pushIntervalMs: 100);
        await using var client = await fixture.ConnectClientAsync();

        await client.SendAsync(IpcFixture.Request(CommandType.Deploy, SampleAssembly.Deploy()));
        await client.SendAsync(IpcFixture.Request(
            CommandType.SubscribeTags,
            new SubscribeTagsRequest(new List<string> { "StartButton", "StopButton", "SensorProduct", "ConveyorRun" })));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));

        // Tiêu thụ push để hàng đợi không dồn lại.
        var pushDrain = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in client.ReceivePushAsync(cts.Token)) { }
            }
            catch (OperationCanceledException) { }
        });

        // Studio poll status 500ms một lần, đúng như plan mô tả.
        var pollLoop = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    await client.SendAsync(IpcFixture.Request(CommandType.GetStatus), cts.Token);
                    await Task.Delay(500, cts.Token);
                }
                catch (OperationCanceledException) { return; }
            }
        });

        // Đầu vào đổi liên tục để vòng push thực sự có việc.
        var simulation = (Driver.Simulation.SimulationDriver)fixture.Host.Drivers.Drivers.Single();
        var toggleLoop = Task.Run(async () =>
        {
            bool value = false;
            while (!cts.IsCancellationRequested)
            {
                simulation.SetInputBool("StartButton", value = !value);
                try { await Task.Delay(50, cts.Token); }
                catch (OperationCanceledException) { return; }
            }
        });

        fixture.Host.Metrics.ToString();   // chạm vào để chắc engine đã khởi động
        await Task.WhenAll(pushDrain, pollLoop, toggleLoop);

        var metrics = fixture.Host.Metrics;

        Assert.True(metrics.CycleCount > seconds * 20,
            $"Chỉ chạy {metrics.CycleCount} chu kỳ trong {seconds}s — chậm hơn nhiều so với chu kỳ 20ms.");

        Assert.True(metrics.AverageJitterMs < 2.0,
            $"Jitter trung bình {metrics.AverageJitterMs:F2}ms, vượt ngưỡng 2ms.");

        Assert.True(metrics.MaxJitterMs < 20.0,
            $"Có chu kỳ lệch {metrics.MaxJitterMs:F2}ms — tức chu kỳ thực > 40ms.");
    }

    // ── SafetyCatch ──────────────────────────────────────────────────────────────

    [Fact]
    public void LogicNemLoi_ScanDung_VaFaultCoCauTruc()
    {
        var engine = NewEngine(new ThrowingProgram(), intervalMs: 5, out var safety);

        FaultEvent? captured = null;
        safety.FaultOccurred += (_, fault) => captured = fault;

        engine.Start();
        Thread.Sleep(300);
        engine.Stop();

        Assert.True(safety.IsFaulted);
        Assert.NotNull(captured);
        Assert.Contains("Chia cho 0", captured!.Message);
        Assert.NotNull(captured.StackTrace);
    }

    [Fact]
    public async Task Fault_XoaForceTruocKhiXaOutput()
    {
        var safety = new SafetyCatchManager { WriteToConsole = false };
        var memory = new MemorySnapshot();

        var order = new List<string>();
        safety.ClearForcesRequested += (_, _) => order.Add("clear-force");
        safety.FaultOccurred += (_, _) => order.Add("notify");

        memory.SetBool("Motor", true);
        memory.SwapOutputBuffers();

        await safety.HandleUnhandledExceptionAsync(
            new InvalidOperationException("lỗi"), memory, new DriverManager());

        // Xoá force PHẢI trước: xả output xong mà force còn thì force lại kéo output lên.
        Assert.Equal(new[] { "clear-force", "notify" }, order);
        Assert.False(memory.GetRawOutputBool("Motor"));
    }
}
