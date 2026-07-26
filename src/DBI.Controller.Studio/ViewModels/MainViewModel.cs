using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using DBI.Controller.Studio.Services;

namespace DBI.Controller.Studio.ViewModels;

public class DeviceItem
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string ConnectionInfo { get; set; } = string.Empty;
    public string Status { get; set; } = "Connected";
}

public class TagMappingItem
{
    public string HardwareTag { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string CSharpProperty { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = "FALSE";
    public bool IsTrue => CurrentValue == "TRUE";
    public string ValueBadgeColor => IsTrue ? "#107C41" : "#C72C3B";
}

public class MainViewModel : INotifyPropertyChanged
{
    private bool _isOnline;
    private bool _isDarkMode = false; // Default to Clean Professional Industrial Light Theme
    private string _cSharpCode = @"using DBI.Controller.SDK;
using DBI.Controller.SDK.Primitives;

namespace UserProgram;

public class ConveyorLogic : ControllerProgram
{
    private readonly Ton _delayStop = new(1000);

    public override void Execute()
    {
        // 1. Start Conveyor
        if (IO[""StartButton""])
            IO[""ConveyorRun""] = true;

        // 2. Stop Conveyor
        if (IO[""StopButton""])
            IO[""ConveyorRun""] = false;

        // 3. Product Sensor -> 1000ms delay then stop
        _delayStop.In = IO[""SensorProduct""];
        if (_delayStop.Q)
            IO[""ConveyorRun""] = false;
    }
}";
    private string _compilerOutput = "Engine Ready. Roslyn C# Compiler loaded.";
    private double _scanTimeMs = 1.2;
    private double _maxScanTimeMs = 2.4;
    private double _jitterMs = 0.1;

    public ObservableCollection<DeviceItem> Devices { get; } = new()
    {
        new DeviceItem { Name = "PLC_Siemens_S7", Type = "Siemens S7-1200 (DBI.Drivers)", ConnectionInfo = "192.168.1.10", Status = "Connected" },
        new DeviceItem { Name = "Modbus_IO_Module", Type = "Modbus TCP (DBI.Drivers.Modbus)", ConnectionInfo = "192.168.1.20:502", Status = "Connected" },
        new DeviceItem { Name = "Delta_DVP_PLC", Type = "Delta PLC (DBI.Drivers.Delta.PLC)", ConnectionInfo = "192.168.1.5:502", Status = "Connected" },
        new DeviceItem { Name = "Omron_FINS_PLC", Type = "Omron FINS (DBI.Drivers.Omron)", ConnectionInfo = "192.168.1.15:9600", Status = "Connected" },
        new DeviceItem { Name = "FactoryIO_3D", Type = "Factory I/O 3D Simulator", ConnectionInfo = "127.0.0.1:502", Status = "Connected" }
    };

    public ObservableCollection<TagMappingItem> TagMappings { get; } = new()
    {
        new TagMappingItem { HardwareTag = "FactoryIO.Input_0", DeviceName = "FactoryIO_3D", CSharpProperty = "IO.StartButton", CurrentValue = "TRUE" },
        new TagMappingItem { HardwareTag = "FactoryIO.Input_1", DeviceName = "FactoryIO_3D", CSharpProperty = "IO.StopButton", CurrentValue = "FALSE" },
        new TagMappingItem { HardwareTag = "FactoryIO.Input_2", DeviceName = "FactoryIO_3D", CSharpProperty = "IO.SensorProduct", CurrentValue = "FALSE" },
        new TagMappingItem { HardwareTag = "FactoryIO.Output_0", DeviceName = "FactoryIO_3D", CSharpProperty = "IO.ConveyorRun", CurrentValue = "TRUE" }
    };

    public string CSharpCode
    {
        get => _cSharpCode;
        set { _cSharpCode = value; OnPropertyChanged(); }
    }

    public string CompilerOutput
    {
        get => _compilerOutput;
        set { _compilerOutput = value; OnPropertyChanged(); }
    }

    public bool IsDarkMode
    {
        get => _isDarkMode;
        set 
        { 
            _isDarkMode = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(ThemeToggleText));
            OnPropertyChanged(nameof(WindowBg));
            OnPropertyChanged(nameof(CardBg));
            OnPropertyChanged(nameof(TextPrimary));
            OnPropertyChanged(nameof(TextSecondary));
            OnPropertyChanged(nameof(BorderColor));
            OnPropertyChanged(nameof(HeaderBg));
            OnPropertyChanged(nameof(AccentColor));
        }
    }

    public string ThemeToggleText => IsDarkMode ? "☀️ Light Mode" : "🌙 Dark Mode";

    // Industrial Minimalist Color Tokens
    public string WindowBg => IsDarkMode ? "#1E1E1E" : "#F3F3F3";
    public string CardBg => IsDarkMode ? "#252526" : "#FFFFFF";
    public string HeaderBg => IsDarkMode ? "#2D2D30" : "#E8E8E8";
    public string TextPrimary => IsDarkMode ? "#CCCCCC" : "#1A1A1A";
    public string TextSecondary => IsDarkMode ? "#808080" : "#555555";
    public string BorderColor => IsDarkMode ? "#3F3F46" : "#CCCCCC";
    public string AccentColor => IsDarkMode ? "#007ACC" : "#005A9E";

    public bool IsOnline
    {
        get => _isOnline;
        set { _isOnline = value; OnPropertyChanged(); OnPropertyChanged(nameof(OnlineStatusText)); }
    }

    public string OnlineStatusText => IsOnline ? "🟢 ONLINE (Glasses Mode)" : "⚪ OFFLINE";

    public double ScanTimeMs
    {
        get => _scanTimeMs;
        set { _scanTimeMs = value; OnPropertyChanged(); }
    }

    public double MaxScanTimeMs
    {
        get => _maxScanTimeMs;
        set { _maxScanTimeMs = value; OnPropertyChanged(); }
    }

    public double JitterMs
    {
        get => _jitterMs;
        set { _jitterMs = value; OnPropertyChanged(); }
    }

    public RoslynCompilerService CompilerService { get; } = new();
    public LiveMonitoringService MonitoringService { get; } = new();

    public void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
    }

    public void CompileAndDeploy()
    {
        CompilerOutput = "Compiling C# source code via Roslyn Compiler API...";
        var result = CompilerService.CompileSource(CSharpCode);

        if (result.Success)
        {
            CompilerOutput = $"[SUCCESS] Dynamic Assembly built ({result.AssemblyBytes?.Length} bytes).\n[INFO] Hot Reload deployed to DBI SoftPLC Runtime Engine.";
        }
        else
        {
            CompilerOutput = $"[ERROR] COMPILATION FAILED:\n" + string.Join("\n", result.Errors);
        }
    }

    public void ToggleOnline()
    {
        IsOnline = !IsOnline;
        if (IsOnline)
        {
            MonitoringService.GoOnline();
            CompilerOutput = "TIA Portal-Style Live Glasses Mode Activated.";
        }
        else
        {
            MonitoringService.GoOffline();
            CompilerOutput = "Disconnected from Live Glasses Mode.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
