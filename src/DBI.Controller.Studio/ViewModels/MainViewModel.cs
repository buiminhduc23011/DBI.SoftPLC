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
}

public class MainViewModel : INotifyPropertyChanged
{
    private string _activeTab = "Editor";
    private bool _isOnline;
    private string _cSharpCode = @"using DBI.Controller.SDK;
using DBI.Controller.SDK.Primitives;

namespace UserProgram;

public class ConveyorLogic : ControllerProgram
{
    private readonly Ton _delayStop = new(1000);

    public override void Execute()
    {
        // 1. Nhấn nút Start -> Chạy băng tải
        if (IO.StartButton)
            IO.ConveyorRun = true;

        // 2. Nhấn nút Stop -> Dừng băng tải
        if (IO.StopButton)
            IO.ConveyorRun = false;

        // 3. Sensor thấy hàng -> Trễ 1000ms rồi dừng băng tải
        _delayStop.In = IO.SensorProduct;
        if (_delayStop.Q)
            IO.ConveyorRun = false;
    }
}";
    private string _compilerOutput = "Ready to Compile & Deploy.";
    private double _scanTimeMs = 1.2;
    private double _maxScanTimeMs = 2.4;
    private double _jitterMs = 0.1;

    public ObservableCollection<DeviceItem> Devices { get; } = new()
    {
        new DeviceItem { Name = "PLC_Siemens_S7", Type = "Siemens S7-1200", ConnectionInfo = "192.168.1.10", Status = "Connected" },
        new DeviceItem { Name = "Modbus_IO_Module", Type = "Modbus TCP", ConnectionInfo = "192.168.1.20:502", Status = "Connected" },
        new DeviceItem { Name = "FactoryIO_3D", Type = "Factory I/O", ConnectionInfo = "127.0.0.1:502", Status = "Connected" }
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

    public void CompileAndDeploy()
    {
        CompilerOutput = "⏳ Compiling C# source code via Roslyn...";
        var result = CompilerService.CompileSource(CSharpCode);

        if (result.Success)
        {
            CompilerOutput = $"✅ COMPILATION SUCCEEDED!\n[+] Built Assembly size: {result.AssemblyBytes?.Length} bytes.\n[+] Hot Reload deployed to DBI SoftPLC Runtime.";
        }
        else
        {
            CompilerOutput = $"❌ COMPILATION FAILED:\n" + string.Join("\n", result.Errors);
        }
    }

    public void ToggleOnline()
    {
        IsOnline = !IsOnline;
        if (IsOnline)
        {
            MonitoringService.GoOnline();
            CompilerOutput = "👓 TIA Portal-Style Live Glasses Mode Activated! Realtime inline value overlay stream enabled.";
        }
        else
        {
            MonitoringService.GoOffline();
            CompilerOutput = "⚪ Disconnected from Live Glasses Mode.";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
