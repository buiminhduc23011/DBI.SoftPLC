using DBI.Controller.Studio.Core.Models;

namespace DBI.Controller.Studio.Core.Services.Devices;

public sealed record DriverSetting(string Key, string Label, string DefaultValue, bool Required = true);

public sealed record DriverDescriptor(
    string DriverType,
    string DisplayName,
    string Description,
    string AddressPlaceholder,
    IReadOnlyList<DriverSetting> Settings);

public static class DriverCatalog
{
    // Khóa setting phải khớp CHÍNH XÁC những gì từng adapter đọc trong FromSpec —
    // adapter là nguồn sự thật, không được đổi chiều ngược lại.
    private static readonly IReadOnlyList<DriverDescriptor> Descriptors = new[]
    {
        new DriverDescriptor("Simulation", "Simulation", "Deterministic in-memory driver", "Sim_0", Array.Empty<DriverSetting>()),
        new DriverDescriptor("Modbus", "Modbus TCP/RTU", "Modbus device", "40001", new[]
        {
            new DriverSetting("ip", "IP Address", "127.0.0.1"), new DriverSetting("port", "Port", "502"),
            new DriverSetting("slaveId", "Slave ID", "1")
        }),
        new DriverDescriptor("Delta.PLC", "Delta PLC", "Delta DVP PLC", "D100", new[]
        {
            new DriverSetting("ip", "IP Address", "192.168.1.5"), new DriverSetting("port", "Port", "502"),
            new DriverSetting("slaveId", "Station", "1")
        }),
        new DriverDescriptor("Omron", "Omron PLC", "Omron FINS/HostLink PLC", "CIO100", new[]
        {
            new DriverSetting("ip", "IP Address", "192.168.1.10"), new DriverSetting("port", "Port", "9600")
        }),
        new DriverDescriptor("FactoryIO", "Factory I/O", "Factory I/O Modbus adapter", "Input_0", new[]
        {
            new DriverSetting("ip", "IP Address", "127.0.0.1"), new DriverSetting("port", "Port", "502"),
            new DriverSetting("slaveId", "Slave ID", "1")
        })
    };

    public static IReadOnlyList<DriverDescriptor> All => Descriptors;

    public static DriverDescriptor? Find(string driverType) =>
        Descriptors.FirstOrDefault(d => d.DriverType.Equals(driverType, StringComparison.OrdinalIgnoreCase));

    public static DeviceConfig CreateDefault(string driverType, string name)
    {
        var descriptor = Find(driverType) ?? throw new ArgumentException($"Unknown driver '{driverType}'.", nameof(driverType));
        return new DeviceConfig
        {
            Name = name,
            DriverType = descriptor.DriverType,
            Settings = descriptor.Settings.ToDictionary(s => s.Key, s => s.DefaultValue, StringComparer.OrdinalIgnoreCase)
        };
    }
}
