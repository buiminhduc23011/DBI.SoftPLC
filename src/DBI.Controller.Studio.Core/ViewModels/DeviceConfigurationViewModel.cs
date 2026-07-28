using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
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
    public ObservableCollection<DeviceSettingViewModel> Settings { get; } = new();
    public IReadOnlyList<DriverDescriptor> DriverCatalog => DriverCatalogService;
    private static IReadOnlyList<DriverDescriptor> DriverCatalogService => Services.Devices.DriverCatalog.All;

    [ObservableProperty] private DeviceConfig? _selectedDevice;
    [ObservableProperty] private string _newDeviceName = "Device_1";
    [ObservableProperty] private DriverDescriptor? _selectedDriver;

    partial void OnSelectedDeviceChanged(DeviceConfig? value)
    {
        SelectedDriver = value is null ? null : DriverCatalogService.FirstOrDefault(d => d.DriverType == value.DriverType);
        Settings.Clear();
        if (value is null || SelectedDriver is null) return;
        foreach (var setting in SelectedDriver.Settings)
            Settings.Add(new DeviceSettingViewModel(value, setting));
    }

    [RelayCommand]
    private void AddDevice()
    {
        var descriptor = SelectedDriver ?? DriverCatalogService.FirstOrDefault();
        if (descriptor is null || !Regex.IsMatch(NewDeviceName, "^[A-Za-z_][A-Za-z0-9_]*$") ||
            _project.Devices.Any(d => d.Name.Equals(NewDeviceName, StringComparison.OrdinalIgnoreCase))) return;

        if (_projects.AddDevice(NewDeviceName, descriptor.DriverType).Success)
        {
            Refresh();
            SelectedDevice = Devices.LastOrDefault();
            IsDirty = true;
        }
    }

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
        SelectedDriver ??= DriverCatalogService.FirstOrDefault();
    }

    public override Task SaveAsync()
    {
        _projects.Save();
        IsDirty = false;
        return Task.CompletedTask;
    }
}

public sealed class DeviceSettingViewModel : ObservableObject
{
    private readonly DeviceConfig _device;
    private readonly string _key;
    public DeviceSettingViewModel(DeviceConfig device, DriverSetting setting)
    {
        _device = device; _key = setting.Key;
        Label = setting.Label;
        Value = device.Settings.TryGetValue(setting.Key, out var value) ? value : setting.DefaultValue;
    }
    public string Label { get; }
    public string Value
    {
        get => _device.Settings.TryGetValue(_key, out var value) ? value : "";
        set { if (Value == value) return; _device.Settings[_key] = value; OnPropertyChanged(); }
    }
}
