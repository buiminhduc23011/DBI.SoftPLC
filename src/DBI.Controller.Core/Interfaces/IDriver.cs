using DBI.Controller.Core.Models;

namespace DBI.Controller.Core.Interfaces;

/// <summary>
/// Interface đại diện cho một Protocol Driver (Modbus, Factory I/O, Simulation, Siemens...).
/// </summary>
public interface IDriver
{
    string DriverId { get; }
    ConnectionState State { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task ReadInputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default);
    Task WriteOutputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
