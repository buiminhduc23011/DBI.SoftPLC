using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Devices;
using DBI.Controller.Studio.Core.Services.Runtime;
using DBI.Controller.Studio.Core.ViewModels;

namespace DBI.Controller.Tests;

/// <summary>
/// Device Configuration (phase-09): dòng device nhận trạng thái từ vòng poll,
/// Test Connection báo kết quả đúng qua hộp thoại.
/// </summary>
public sealed class DeviceConfigurationViewModelTests
{
    private static (ProjectService Service, DbiProject Project) NewProject()
    {
        var service = new ProjectService();
        string root = Path.Combine(Path.GetTempPath(), "dbi-devicevm-" + Guid.NewGuid().ToString("N"));
        service.CreateNew(root, "Machine");
        return (service, service.Current!);
    }

    [Fact]
    public async Task DeviceStatesChanged_CapNhatTrangThaiTungDong()
    {
        var (service, project) = NewProject();
        try
        {
            project.Devices.Add(DriverCatalog.CreateDefault("Simulation", "SIM"));
            project.Devices.Add(DriverCatalog.CreateDefault("Modbus", "PLC_1"));

            await using var runtime = new FakeRuntimeClient();
            using var vm = new DeviceConfigurationViewModel(project, service, runtime, new ScriptedPrompt());

            runtime.RaiseDeviceStates(new List<DeviceStateInfo>
            {
                new("SIM", nameof(ConnectionState.Connected), null),
                new("PLC_1", nameof(ConnectionState.Faulted), "Connection refused")
            });

            Assert.Equal("OK", vm.Devices[0].StatusText);
            Assert.Equal("SuccessColor", vm.Devices[0].StatusColorKey);

            Assert.Equal("Fault", vm.Devices[1].StatusText);
            Assert.Equal("DangerColor", vm.Devices[1].StatusColorKey);
            Assert.Contains("refused", vm.Devices[1].StatusDetail);
        }
        finally { Cleanup(service); }
    }

    [Fact]
    public async Task TestConnection_ThanhCong_BaoQuaHopThoaiThongTin()
    {
        var (service, project) = NewProject();
        try
        {
            project.Devices.Add(DriverCatalog.CreateDefault("Simulation", "SIM"));

            var prompt = new ScriptedPrompt();
            using var vm = new DeviceConfigurationViewModel(
                project, service, new FakeRuntimeClient(), prompt);
            vm.Refresh();
            vm.SelectedRow = vm.Devices.Single();

            await vm.TestConnectionCommand.ExecuteAsync(null);

            Assert.False(vm.IsTestingConnection);
        }
        finally { Cleanup(service); }
    }

    [Fact]
    public async Task TestConnection_ThatBai_HienLoiCuaDriver()
    {
        var (service, project) = NewProject();
        try
        {
            project.Devices.Add(DriverCatalog.CreateDefault("Modbus", "PLC_1"));

            var prompt = new ScriptedPrompt();
            using var vm = new DeviceConfigurationViewModel(
                project, service, new FakeRuntimeClient { TestConnectionError = "Connection refused" }, prompt);
            vm.Refresh();
            vm.SelectedRow = vm.Devices.Single();

            await vm.TestConnectionCommand.ExecuteAsync(null);

            var error = Assert.Single(prompt.Errors);
            Assert.Contains("Connection refused", error);
        }
        finally { Cleanup(service); }
    }

    private static void Cleanup(ProjectService service)
    {
        if (Directory.Exists(service.Current!.ProjectDirectory))
            Directory.Delete(service.Current.ProjectDirectory, true);
    }
}
