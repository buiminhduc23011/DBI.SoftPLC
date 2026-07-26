using System.Diagnostics;
using DBI.Controller.Core.Interfaces;
using DBI.Controller.Runtime.Drivers;
using DBI.Controller.Runtime.Safety;
using DBI.Controller.SDK;

namespace DBI.Controller.Runtime.Engine;

/// <summary>
/// Ảnh chụp bất biến của chỉ số scan.
/// </summary>
/// <remarks>
/// Bất biến là chủ ý: <c>GetStatus</c> đọc từ thread IPC trong khi scan thread đang ghi. Nếu đây là
/// class mutable thì người đọc có thể thấy <c>CycleCount</c> của chu kỳ này ghép với
/// <c>LastScanMs</c> của chu kỳ khác (torn read).
/// </remarks>
public record ScanMetrics(
    long CycleCount,
    double LastScanMs,
    double MaxScanMs,
    double JitterMs,
    double MaxJitterMs,
    double AverageJitterMs)
{
    public static readonly ScanMetrics Empty = new(0, 0, 0, 0, 0, 0);
}

public class ScanEngine
{
    private readonly IMemoryImage _memoryImage;
    private readonly DriverManager _driverManager;
    private readonly SafetyCatchManager _safetyCatchManager;

    private ControllerProgram? _program;
    private volatile bool _isRunning;
    private Thread? _engineThread;

    private ScanMetrics _metrics = ScanMetrics.Empty;

    public int ScanIntervalMs { get; set; } = 20;

    /// <summary>Đọc được an toàn từ thread khác — luôn là một ảnh chụp nhất quán.</summary>
    public ScanMetrics Metrics => Volatile.Read(ref _metrics);

    public bool IsRunning => _isRunning;

    public ScanEngine(IMemoryImage memoryImage, DriverManager driverManager, SafetyCatchManager safetyCatchManager)
    {
        _memoryImage = memoryImage ?? throw new ArgumentNullException(nameof(memoryImage));
        _driverManager = driverManager ?? throw new ArgumentNullException(nameof(driverManager));
        _safetyCatchManager = safetyCatchManager ?? throw new ArgumentNullException(nameof(safetyCatchManager));
    }

    public void SetProgram(ControllerProgram? program) => _program = program;

    public void ResetMetrics() => Volatile.Write(ref _metrics, ScanMetrics.Empty);

    public void Start()
    {
        if (_isRunning) return;
        if (_program == null) throw new InvalidOperationException("Chưa thiết lập ControllerProgram cho ScanEngine.");

        _isRunning = true;
        _engineThread = new Thread(RunLoop)
        {
            Name = "DBI_SoftPLC_ScanEngine",
            Priority = ThreadPriority.Highest,
            IsBackground = true
        };
        _engineThread.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        _engineThread?.Join(TimeSpan.FromSeconds(2));
        _engineThread = null;
    }

    private void RunLoop()
    {
        // Không có dòng này thì Thread.Sleep bị chặn ở độ phân giải timer mặc định ~15.6ms của
        // Windows và jitter trung bình lên tới ~6ms — quá lớn cho chu kỳ 20ms.
        using var timerScope = new HighResolutionTimerScope();

        var clock = Stopwatch.StartNew();

        double intervalMs = ScanIntervalMs;
        double nextDeadlineMs = clock.Elapsed.TotalMilliseconds;
        double previousCycleStartMs = double.NaN;

        var metrics = ScanMetrics.Empty;
        double jitterSum = 0;
        long jitterSamples = 0;

        while (_isRunning && !_safetyCatchManager.IsFaulted)
        {
            double cycleStartMs = clock.Elapsed.TotalMilliseconds;

            try
            {
                // 1. Driver đọc phần cứng vào InputBuffer
                _driverManager.ReadInputsAsync(_memoryImage).GetAwaiter().GetResult();

                // 2. Chốt Input: InputBuffer -> InputSnapshot
                _memoryImage.SwapInputBuffers();

                // 3. Chạy logic người dùng
                _program?.Execute();

                // 4. Chốt Output: OutputState -> OutputBuffer
                _memoryImage.SwapOutputBuffers();

                // 5. Driver ghi OutputBuffer xuống phần cứng
                _driverManager.WriteOutputsAsync(_memoryImage).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _safetyCatchManager
                    .HandleUnhandledExceptionAsync(ex, _memoryImage, _driverManager)
                    .GetAwaiter().GetResult();

                _isRunning = false;
                break;
            }

            double elapsedMs = clock.Elapsed.TotalMilliseconds - cycleStartMs;

            // Jitter = độ lệch của CHU KỲ THỰC TẾ (đầu scan này so với đầu scan trước) với chu kỳ
            // đặt trước. Công thức cũ |thời gian thực thi - chu kỳ| đo thời gian thực thi, không
            // phải jitter: logic chạy nhanh 1ms trong chu kỳ 20ms đều tăm tắp vẫn bị báo jitter 19ms.
            double jitterMs = 0;
            if (!double.IsNaN(previousCycleStartMs))
            {
                jitterMs = Math.Abs(cycleStartMs - previousCycleStartMs - intervalMs);
                jitterSum += jitterMs;
                jitterSamples++;
            }

            previousCycleStartMs = cycleStartMs;

            metrics = new ScanMetrics(
                CycleCount: metrics.CycleCount + 1,
                LastScanMs: elapsedMs,
                MaxScanMs: Math.Max(metrics.MaxScanMs, elapsedMs),
                JitterMs: jitterMs,
                MaxJitterMs: Math.Max(metrics.MaxJitterMs, jitterMs),
                AverageJitterMs: jitterSamples == 0 ? 0 : jitterSum / jitterSamples);

            Volatile.Write(ref _metrics, metrics);

            nextDeadlineMs += intervalMs;

            // Chu kỳ quá tải: bỏ qua các deadline đã lỡ thay vì đuổi theo bằng một chuỗi chu kỳ
            // không nghỉ — đuổi theo chỉ làm hệ thống nghẹt thêm.
            double nowMs = clock.Elapsed.TotalMilliseconds;
            if (nextDeadlineMs < nowMs)
                nextDeadlineMs = nowMs;

            WaitUntil(clock, nextDeadlineMs);
        }
    }

    /// <summary>
    /// Chờ tới mốc thời gian tuyệt đối.
    /// </summary>
    /// <remarks>
    /// <c>Thread.Sleep((int)(interval - elapsed))</c> cũ cắt cụt phần thập phân: chu kỳ 20ms mà logic
    /// chạy 0.7ms thì ngủ 19ms thay vì 19.3ms — lệch tích luỹ dần. Ở đây ngủ phần thô rồi
    /// <see cref="SpinWait"/> nốt phần dư dưới 1ms.
    /// </remarks>
    private static void WaitUntil(Stopwatch clock, double deadlineMs)
    {
        // Với timer 1ms (HighResolutionTimerScope), Thread.Sleep sai số dưới 1ms — chừa 2ms để
        // spin nốt là đủ, không phải đốt CPU cả chu kỳ.
        const double SpinThresholdMs = 2.0;

        double remaining = deadlineMs - clock.Elapsed.TotalMilliseconds;
        if (remaining <= 0) return;

        if (remaining > SpinThresholdMs)
            Thread.Sleep((int)(remaining - SpinThresholdMs));

        var spinner = new SpinWait();
        while (clock.Elapsed.TotalMilliseconds < deadlineMs)
        {
            if (spinner.NextSpinWillYield) spinner.Reset();
            spinner.SpinOnce();
        }
    }
}
