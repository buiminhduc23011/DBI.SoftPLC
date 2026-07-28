using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Devices;

namespace DBI.Controller.Studio.Core.ViewModels;

public partial class DeviceConfigurationViewModel : DocumentViewModelBase
{
    private readonly DbiProject _project;
    private readonly ProjectService _projects;

    public DeviceConfigurationViewModel(DbiProject project, ProjectService projects)
        : base("DeviceConfiguration", "Devices")
    {
        _project = project;
        _projects = projects;
        Refresh();
    }

    public ObservableCollection<DeviceConfig> Devices { get; } = new();
    public IReadOnlyList<DriverDescriptor> DriverCatalog => DriverCatalogService;
    private static IReadOnlyList<DriverDescriptor> DriverCatalogService => Services.Devices.DriverCatalog.All;

    [ObservableProperty] private DeviceConfig? _selectedDevice;

    [RelayCommand]
    private void AddSimulation()
    {
        string name = "Simulation";
        int suffix = 1;
        while (_project.Devices.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"Simulation_{suffix++}";
        if (_projects.AddDevice(name, "Simulation").Success) { Refresh(); IsDirty = true; SelectedDevice = Devices.LastOrDefault(); }
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedDevice is null) return;
        if (_projects.DeleteDevice(SelectedDevice).Success) { Refresh(); IsDirty = true; }
    }

    public void Refresh()
    {
        Devices.Clear();
        foreach (var device in _project.Devices) Devices.Add(device);
    }

    public override Task SaveAsync()
    {
        _projects.Save();
        IsDirty = false;
        return Task.CompletedTask;
    }
}
