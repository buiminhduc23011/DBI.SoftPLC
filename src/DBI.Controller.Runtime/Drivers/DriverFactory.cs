using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Controller.Driver.Delta;
using DBI.Controller.Driver.FactoryIO;
using DBI.Controller.Driver.Modbus;
using DBI.Controller.Driver.Omron;
using DBI.Controller.Driver.Simulation;

namespace DBI.Controller.Runtime.Drivers;

public class UnknownDriverException : Exception
{
    public UnknownDriverException(string driverType, IEnumerable<string> known)
        : base($"Không nhận ra loại driver '{driverType}'. Loại đang hỗ trợ: {string.Join(", ", known)}.")
    {
        DriverType = driverType;
    }

    public string DriverType { get; }
}

/// <summary>
/// Dựng driver từ <see cref="DeviceSpec"/> Studio gửi xuống.
/// </summary>
/// <remarks>
/// Runtime mới là bên nạp driver (ADR-001) — Studio chỉ gửi <c>DeviceSpec</c> qua IPC, nên driver
/// DLL đóng gói kèm <b>Runtime</b>, không phải Studio.
/// <para><see cref="IDriver.DriverId"/> của driver dựng ra <b>phải</b> bằng
/// <see cref="DeviceSpec.Name"/> — <see cref="TagRoutingTable"/> tra route theo đúng tên đó.</para>
/// </remarks>
public class DriverFactory
{
    private readonly Dictionary<string, Func<DeviceSpec, IDriver>> _registry =
        new(StringComparer.OrdinalIgnoreCase);

    public DriverFactory()
    {
        Register(ModbusDriverAdapter.DriverTypeId, ModbusDriverAdapter.FromSpec, "Modbus", "ModbusTCP");
        Register(FactoryIODriver.DriverTypeId, FactoryIODriver.FromSpec, "FactoryIO", "Factory I/O");
        Register(DeltaPlcDriverAdapter.DriverTypeId, DeltaPlcDriverAdapter.FromSpec, "Delta", "DeltaPLC");
        Register(OmronPlcDriverAdapter.DriverTypeId, OmronPlcDriverAdapter.FromSpec, "Omron", "OmronFINS");
        Register("DBI.Controller.Driver.Simulation", spec => new SimulationDriver(spec.Name), "Simulation", "Sim");
    }

    public IReadOnlyCollection<string> KnownDriverTypes => _registry.Keys;

    public void Register(string driverType, Func<DeviceSpec, IDriver> factory, params string[] aliases)
    {
        _registry[driverType] = factory;

        foreach (string alias in aliases)
            _registry[alias] = factory;
    }

    public bool CanCreate(string driverType) => _registry.ContainsKey(driverType);

    public IDriver Create(DeviceSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!_registry.TryGetValue(spec.DriverType, out var factory))
            throw new UnknownDriverException(spec.DriverType, _registry.Keys);

        return factory(spec);
    }
}
