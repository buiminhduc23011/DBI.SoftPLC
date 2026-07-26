using DBI.Controller.Core.Interfaces;
using DBI.Controller.Runtime.Drivers;

namespace DBI.Controller.Runtime.Safety;

/// <param name="Message">Mô tả ngắn cho kỹ sư.</param>
/// <param name="StackTrace">Ngăn xếp gọi, để lần ra dòng code gây lỗi.</param>
/// <param name="OccurredAt">Thời điểm xảy ra, giờ UTC.</param>
public record FaultEvent(string Message, string? StackTrace, DateTimeOffset OccurredAt);

/// <summary>
/// Bắt lỗi chưa xử lý của logic người dùng và đưa máy về trạng thái an toàn.
/// </summary>
public class SafetyCatchManager
{
    public bool IsFaulted { get; private set; }
    public Exception? LastException { get; private set; }
    public FaultEvent? LastFault { get; private set; }

    /// <summary>
    /// Phát khi máy vào trạng thái Fault. <c>IpcServer</c> đẩy lên Studio qua kênh <c>fault</c> —
    /// trước đây chỉ <c>Console.WriteLine</c>, mà Studio chạy tiến trình riêng nên không thấy gì.
    /// </summary>
    public event EventHandler<FaultEvent>? FaultOccurred;

    /// <summary>
    /// Phát khi máy vào Fault, <b>trước</b> lúc xả output. Phase-11 nối vào đây để xoá sạch force —
    /// force còn sót lại sau fault sẽ ghi đè giá trị an toàn vừa xả xuống.
    /// </summary>
    public event EventHandler? ClearForcesRequested;

    /// <summary>Ghi log ra console. Chế độ headless không có Studio nên vẫn cần.</summary>
    public bool WriteToConsole { get; set; } = true;

    public async Task HandleUnhandledExceptionAsync(
        Exception ex,
        IMemoryImage memoryImage,
        DriverManager driverManager)
    {
        IsFaulted = true;
        LastException = ex;
        LastFault = new FaultEvent(
            $"{ex.GetType().Name}: {ex.Message}", ex.StackTrace, DateTimeOffset.UtcNow);

        if (WriteToConsole)
        {
            Console.Error.WriteLine($"🚨 SAFETY: logic người dùng ném lỗi — {LastFault.Message}");
        }

        // 1. Xoá force TRƯỚC khi xả output: xả xong mà force còn thì force lại kéo output lên.
        SafeInvoke(() => ClearForcesRequested?.Invoke(this, EventArgs.Empty), "xoá force");

        // 2. Đưa Output trong bộ nhớ về Safe State (False / 0)
        memoryImage.ClearAllOutputs();
        memoryImage.SwapOutputBuffers();

        // 3. Xả xuống thiết bị THẬT — bước quan trọng nhất, băng tải phải thực sự dừng
        try
        {
            await driverManager.WriteOutputsAsync(memoryImage).ConfigureAwait(false);

            if (WriteToConsole)
                Console.Error.WriteLine("⚠️ Đã xả toàn bộ Output về Safe State xuống thiết bị.");
        }
        catch (Exception driverEx)
        {
            if (WriteToConsole)
                Console.Error.WriteLine($"❌ Ghi khẩn cấp Output xuống driver thất bại: {driverEx.Message}");
        }

        // 4. Báo Studio sau cùng — máy đã an toàn rồi mới lo chuyện hiển thị.
        SafeInvoke(() => FaultOccurred?.Invoke(this, LastFault), "báo fault");
    }

    public void ResetFault()
    {
        IsFaulted = false;
        LastException = null;
        LastFault = null;
    }

    /// <summary>Subscriber ném lỗi không được phép chặn chuỗi xử lý an toàn.</summary>
    private void SafeInvoke(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            if (WriteToConsole)
                Console.Error.WriteLine($"❌ Lỗi khi {what}: {ex.Message}");
        }
    }
}
