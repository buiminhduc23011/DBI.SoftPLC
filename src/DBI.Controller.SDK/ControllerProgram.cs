using DBI.Controller.Core.Interfaces;
using DBI.Controller.SDK.IO;

namespace DBI.Controller.SDK;

/// <summary>
/// Class cơ sở cho toàn bộ chương trình điều khiển C# của người dùng (tương tự OB1 trong PLC).
/// </summary>
public abstract class ControllerProgram
{
    private IOContainer? _io;

    /// <summary>
    /// Đối tượng IO trừu tượng cho phép truy cập IO.StartButton, IO.Conveyor...
    /// </summary>
    public dynamic IO
    {
        get
        {
            if (_io == null)
                throw new InvalidOperationException("Chương trình chưa được khởi tạo với Memory Image từ Runtime.");
            return _io;
        }
    }

    /// <summary>
    /// Đối tượng IOContainer mạnh kiểu cho phép dùng indexer IOContainer["StartButton"].
    /// </summary>
    public IOContainer IOStore
    {
        get
        {
            if (_io == null)
                throw new InvalidOperationException("Chương trình chưa được khởi tạo với Memory Image từ Runtime.");
            return _io;
        }
    }

    /// <summary>
    /// Khởi tạo Memory Image từ Runtime cho chương trình.
    /// </summary>
    public void Initialize(IMemoryImage memoryImage)
    {
        _io = new IOContainer(memoryImage);
    }

    /// <summary>
    /// Khởi chạy 1 lần duy nhất khi Soft PLC Runtime bắt đầu.
    /// </summary>
    public virtual void OnStart()
    {
    }

    /// <summary>
    /// Vòng lặp quét chính (Scan Cycle Loop) được Runtime gọi liên tục mỗi 20ms.
    /// </summary>
    public abstract void Execute();

    /// <summary>
    /// Khởi chạy 1 lần duy nhất khi Soft PLC Runtime dừng.
    /// </summary>
    public virtual void OnStop()
    {
    }
}
