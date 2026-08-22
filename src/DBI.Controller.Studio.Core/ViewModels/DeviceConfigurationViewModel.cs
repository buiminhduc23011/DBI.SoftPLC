using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Devices;
using DBI.Controller.Studio.Core.Services.Runtime;

namespace DBI.Controller.Studio.Core.ViewModels;

/// <summary>Một dòng trong bảng Devices — bọc <see cref="DeviceConfig"/> kèm trạng thái online.</summary>
public sealed partial class DeviceRowViewModel : ObservableObject
{
    public DeviceRowViewModel(DeviceConfig device) => Device = device;

    public DeviceConfig Device { get; }

    public string Name => Device.Name;
    public string DriverType => Device.DriverType;
    public string SettingsSummary => $"{Device.Settings.Count} settings";

    [ObservableProperty] private string _statusText = "—";
    [ObservableProperty] private string _statusDetail = "";

    /// <summary>Khóa màu tra trong theme: SuccessColor / WarningColor / MutedColor / DangerColor.</summary>
    [ObservableProperty] private string _statusColorKey = "MutedColor";

    public void UpdateStatus(DeviceStateInfo? state)
    {
        if (state is null)
        {
            StatusText = "?";
            StatusDetail = "Chưa nhận được trạng thái từ Runtime.";
            StatusColorKey = "MutedColor";
            return;
        }

        StatusText = state.State switch
        {
            nameof(ConnectionState.Connected) => "OK",
            nameof(ConnectionState.Connecting) => "Connecting",
            nameof(ConnectionState.Disconnected) => "Offline",
            nameof(ConnectionState.Faulted) => "Fault",
            _ => state.State
        };

        StatusDetail = state.LastError ?? "";
        StatusColorKey = state.State switch
        {
            nameof(ConnectionState.Connected) => "SuccessColor",
            nameof(ConnectionState.Connecting) => "WarningColor",
            nameof(ConnectionState.Disconnected) => "MutedColor",
            _ => "DangerColor"
        };
    }
}

public partial class DeviceConfigurationViewModel : DocumentViewModelBase, IDisposable
{
    private readonly DbiProject _project;
    private readonly ProjectService _projects;
    private readonly IRuntimeClient _runtime;
    private readonly IUserPrompt _prompt;

    public DeviceConfigurationViewModel(
        DbiProject project,
        ProjectService projects,
        IRuntimeClient runtime,
        IUserPrompt prompt)
        : base("DeviceConfiguration", "Devices")
    {
        _project = project;
        _projects = projects;
        _runtime = runtime;
        _prompt = prompt;

        Refresh();
        _selectedDriver = DriverCatalogService.FirstOrDefault();
        _runtime.DeviceStatesChanged += OnDeviceStatesChanged;
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();
    public ObservableCollection<DeviceSettingViewModel> Settings { get; } = new();
    public IReadOnlyList<DriverDescriptor> DriverCatalog => DriverCatalogService;
    private static IReadOnlyList<DriverDescriptor> DriverCatalogService => Services.Devices.DriverCatalog.All;

    [ObservableProperty] private DeviceRowViewModel? _selectedRow;
    [ObservableProperty] private string _newDeviceName = "Device_1";
    [ObservableProperty] private DriverDescriptor? _selectedDriver;
    [ObservableProperty] private bool _isTestingConnection;

    public DeviceConfig? SelectedDevice => SelectedRow?.Device;

    partial void OnSelectedRowChanged(DeviceRowViewModel? value)
    {
        var device = value?.Device;
        SelectedDriver = device is null ? null : DriverCatalogService.FirstOrDefault(d => d.DriverType == device.DriverType);
        Settings.Clear();
        if (device is null || SelectedDriver is null) return;
        foreach (var setting in SelectedDriver.Settings)
            Settings.Add(new DeviceSettingViewModel(device, setting));
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
            SelectedRow = Devices.LastOrDefault();
            IsDirty = true;
        }
    }

    [RelayCommand]
    private void AddSimulation()
    {
        string name = "Simulation";
        int suffix = 1;
        while (_project.Devices.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"Simulation_{suffix++}";
        if (_projects.AddDevice(name, "Simulation").Success) { Refresh(); IsDirty = true; SelectedRow = Devices.LastOrDefault(); }
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedDevice is null) return;
        if (_projects.DeleteDevice(SelectedDevice).Success) { Refresh(); IsDirty = true; }
    }

    /// <summary>
    /// Task 09.3 — Test Connection gửi lệnh thử qua Runtime. Runtime dựng driver tạm từ spec,
    /// nối rồi ngắt ngay; bộ driver đang chạy không bị ảnh hưởng.
    /// </summary>
    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var device = SelectedDevice;
        if (device is null || IsTestingConnection) return;

        IsTestingConnection = true;
        try
        {
            var spec = new DeviceSpec(device.Name, device.DriverType, new Dictionary<string, string>(device.Settings));
            var result = await _runtime.TestDeviceConnectionAsync(spec).ConfigureAwait(true);

            if (result.Ok) _prompt.ShowInformation($"Kết nối tới '{device.Name}' thành công.", "Test Connection");
            else _prompt.ShowError($"Kết nối tới '{device.Name}' thất bại:\n{result.Error}", "Test Connection");
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    private void OnDeviceStatesChanged(object? sender, IReadOnlyList<DeviceStateInfo> states)
    {
        foreach (var row in Devices)
        {
            var state = states.FirstOrDefault(s => s.DriverId.Equals(row.Name, StringComparison.OrdinalIgnoreCase));
            row.UpdateStatus(state);
        }
    }

    public void Refresh()
    {
        Devices.Clear();
        foreach (var device in _project.Devices) Devices.Add(new DeviceRowViewModel(device));
        SelectedDriver ??= DriverCatalogService.FirstOrDefault();

        // Lấy trạng thái lần cuối client biết được để bảng không trống lúc mới mở.
        _ = RefreshStatusesFromRuntimeAsync();
    }

    private async Task RefreshStatusesFromRuntimeAsync()
    {
        if (_runtime.State != RuntimeClientState.Connected) return;
        try
        {
            var states = await _runtime.GetDeviceStatesAsync().ConfigureAwait(true);
            OnDeviceStatesChanged(this, states);
        }
        catch { /* chưa nối được thì cứ hiện "—" */ }
    }

    public override Task SaveAsync()
    {
        _projects.Save();
        IsDirty = false;
        return Task.CompletedTask;
    }

    public void Dispose() => _runtime.DeviceStatesChanged -= OnDeviceStatesChanged;
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
