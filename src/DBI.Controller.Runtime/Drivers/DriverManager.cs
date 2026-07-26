using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;

namespace DBI.Controller.Runtime.Drivers;

/// <summary>
/// Quản lý vòng đời driver và phân phát I/O theo bảng định tuyến.
/// </summary>
public class DriverManager
{
    private readonly List<IDriver> _drivers = new();

    public DriverManager(TagRoutingTable? routingTable = null)
    {
        RoutingTable = routingTable ?? new TagRoutingTable();
    }

    public TagRoutingTable RoutingTable { get; }

    public IReadOnlyList<IDriver> Drivers => _drivers.AsReadOnly();

    public void RegisterDriver(IDriver driver)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _drivers.Add(driver);
    }

    /// <summary>Gỡ hết driver — dùng khi deploy nạp cấu hình thiết bị mới.</summary>
    public void ClearDrivers() => _drivers.Clear();

    public async Task ConnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var driver in _drivers)
        {
            await driver.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Mỗi driver chỉ nhận <b>phần route của chính nó</b> — không thấy tag của driver khác (B-5).
    /// </summary>
    public async Task ReadInputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default)
    {
        foreach (var driver in _drivers)
        {
            await driver.ReadInputsAsync(memoryImage, RoutesFor(driver), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task WriteOutputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default)
    {
        foreach (var driver in _drivers)
        {
            await driver.WriteOutputsAsync(memoryImage, RoutesFor(driver), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DisconnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var driver in _drivers)
        {
            try
            {
                await driver.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Ngắt kết nối là thao tác dọn dẹp — một driver hỏng không được chặn các driver còn lại.
            }
        }
    }

    public IReadOnlyList<DeviceStateSnapshot> GetDeviceStates() =>
        _drivers.Select(d => new DeviceStateSnapshot(d.DriverId, d.State, d.LastError)).ToList();

    private IReadOnlyList<TagRoute> RoutesFor(IDriver driver) =>
        RoutingTable.GetRoutesForDevice(driver.DriverId);
}

/// <summary>Trạng thái một thiết bị tại một thời điểm — Studio hiển thị ở Device view (phase-09).</summary>
public record DeviceStateSnapshot(string DriverId, ConnectionState State, string? LastError);
