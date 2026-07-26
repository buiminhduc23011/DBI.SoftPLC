using DBI.Controller.Core.Models;

namespace DBI.Controller.Core.Interfaces;

/// <summary>
/// Interface đại diện cho một Protocol Driver (Modbus, Factory I/O, Simulation, Siemens...).
/// </summary>
/// <remarks>
/// Driver <b>chỉ được bọc</b> core client từ repo <c>DBI.Drivers</c> — không tự viết giao thức
/// (Constraint C-5).
/// </remarks>
public interface IDriver
{
    string DriverId { get; }
    ConnectionState State { get; }

    /// <summary>Mô tả lỗi gần nhất, <c>null</c> khi bình thường. Studio hiển thị ở Device view.</summary>
    string? LastError { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Đọc phần cứng và nạp vào InputBuffer của <paramref name="memoryImage"/>.
    /// </summary>
    /// <param name="routes">
    /// <b>Chỉ</b> những tag thuộc thiết bị này. Trước đây driver nhận cả memory image rồi tự đoán
    /// key nào là của mình (B-5); giờ Runtime cắt sẵn theo bảng định tuyến.
    /// </param>
    Task ReadInputsAsync(
        IMemoryImage memoryImage,
        IReadOnlyList<TagRoute> routes,
        CancellationToken cancellationToken = default);

    /// <summary>Lấy Output đã chốt từ <paramref name="memoryImage"/> và ghi xuống phần cứng.</summary>
    /// <param name="routes">Chỉ những tag thuộc thiết bị này.</param>
    Task WriteOutputsAsync(
        IMemoryImage memoryImage,
        IReadOnlyList<TagRoute> routes,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
