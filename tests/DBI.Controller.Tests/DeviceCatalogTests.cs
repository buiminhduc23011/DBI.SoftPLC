using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Devices;

namespace DBI.Controller.Tests;

public sealed class DeviceCatalogTests
{
    [Fact]
    public void Catalog_ContainsAllSupportedDriversWithDefaults()
    {
        Assert.Equal(5, DriverCatalog.All.Count);
        Assert.Equal("40001", DriverCatalog.Find("Modbus")!.AddressPlaceholder);
        Assert.Equal("127.0.0.1", DriverCatalog.CreateDefault("Modbus", "PLC_1").Settings["ip"]);
    }

    [Fact]
    public void Catalog_SettingsKeys_MatchWhatAdaptersRead()
    {
        // Adapter là nguồn sự thật: catalog không được sinh key mà FromSpec không đọc,
        // nếu không cấu hình từ UI sẽ bị driver bỏ qua trong im lặng.
        var expected = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Simulation"] = Array.Empty<string>(),
            ["Modbus"] = new[] { "ip", "port", "slaveId" },
            ["Delta.PLC"] = new[] { "ip", "port", "slaveId" },
            ["Omron"] = new[] { "ip", "port" },
            ["FactoryIO"] = new[] { "ip", "port", "slaveId" }
        };

        foreach (var (driverType, keys) in expected)
        {
            var descriptor = DriverCatalog.Find(driverType)!;
            Assert.Equal(keys, descriptor.Settings.Select(s => s.Key).ToArray());
        }
    }

    [Fact]
    public void RenameDevice_UpdatesAllReferencingTags()
    {
        string root = Path.Combine(Path.GetTempPath(), "dbi-device-" + Guid.NewGuid().ToString("N"));
        var service = new ProjectService();
        try
        {
            var result = service.CreateNew(root, "Machine");
            var device = DriverCatalog.CreateDefault("Simulation", "SIM");
            result.Project!.Devices.Add(device);
            result.Project.TagTables[0].Tags.Add(new Tag { Name = "Start", Device = "SIM" });
            var renamed = service.RenameDevice(device, "SIM_Main");
            Assert.True(renamed.Success);
            Assert.Equal("SIM_Main", result.Project.TagTables[0].Tags[^1].Device);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void DeleteDevice_IsBlockedWhenTagsUseIt()
    {
        string root = Path.Combine(Path.GetTempPath(), "dbi-device-" + Guid.NewGuid().ToString("N"));
        var service = new ProjectService();
        try
        {
            var result = service.CreateNew(root, "Machine");
            var device = DriverCatalog.CreateDefault("Simulation", "SIM");
            result.Project!.Devices.Add(device);
            result.Project.TagTables[0].Tags.Add(new Tag { Name = "Start", Device = "SIM" });
            var deleted = service.DeleteDevice(device);
            Assert.False(deleted.Success);
            Assert.Contains("Start", deleted.Issues[0].Message);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
