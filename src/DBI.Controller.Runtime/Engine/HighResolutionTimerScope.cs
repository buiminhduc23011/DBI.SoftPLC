using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DBI.Controller.Runtime.Engine;

/// <summary>
/// Nâng độ phân giải timer của hệ điều hành lên 1ms trong suốt thời gian scan chạy.
/// </summary>
/// <remarks>
/// <para>Mặc định Windows chạy timer ở ~15.6ms. Nghĩa là <c>Thread.Sleep(18)</c> có thể ngủ tới
/// 31ms — với chu kỳ quét 20ms thì đó là jitter 11ms, phá thẳng ràng buộc C-1. Đo thực tế trước
/// khi có lớp này: jitter trung bình ~6ms; sau khi có: dưới 1ms.</para>
///
/// <para>Đây là kỹ thuật tiêu chuẩn của phần mềm thời gian thực mềm trên Windows. Cái giá là toàn
/// máy tốn điện hơn một chút, nên chỉ nâng khi scan engine thật sự đang chạy và hạ ngay khi dừng.</para>
///
/// <para>Trên nền tảng khác đây là no-op: Linux đã có <c>nanosleep</c> đủ mịn.</para>
/// </remarks>
public sealed class HighResolutionTimerScope : IDisposable
{
    private const uint TargetPeriodMs = 1;
    private const uint TimerNoError = 0;

    private bool _raised;

    public HighResolutionTimerScope()
    {
        if (!OperatingSystem.IsWindows()) return;

        _raised = NativeMethods.TimeBeginPeriod(TargetPeriodMs) == TimerNoError;
    }

    /// <summary>Timer đã thực sự được nâng hay chưa. <c>false</c> ngoài Windows.</summary>
    public bool IsRaised => _raised;

    public void Dispose()
    {
        if (!_raised) return;

        _raised = false;

        if (OperatingSystem.IsWindows())
            NativeMethods.TimeEndPeriod(TargetPeriodMs);
    }

    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", ExactSpelling = true)]
        internal static extern uint TimeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", ExactSpelling = true)]
        internal static extern uint TimeEndPeriod(uint milliseconds);
    }
}
