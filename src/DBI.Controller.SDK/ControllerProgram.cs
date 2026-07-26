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
    /// Cổng truy cập I/O — mạnh kiểu. Dùng <c>IO["StartButton"]</c> cho tới khi phase-06 sinh
    /// <c>IO.g.cs</c> từ Tag Table, sau đó dùng được <c>IO.StartButton</c>.
    /// </summary>
    public IOContainer IO => _io ?? throw new InvalidOperationException(
        "Chương trình chưa được khởi tạo với Memory Image từ Runtime.");

    /// <summary>
    /// Alias tương thích ngược của <see cref="IO"/>.
    /// </summary>
    [Obsolete("Dùng IO.")]
    public IOContainer IOStore => IO;

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
