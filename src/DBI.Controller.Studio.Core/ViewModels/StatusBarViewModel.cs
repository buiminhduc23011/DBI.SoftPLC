using CommunityToolkit.Mvvm.ComponentModel;
using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Services.Runtime;

namespace DBI.Controller.Studio.Core.ViewModels;

/// <summary>
/// Thanh trạng thái dưới cùng — phản ánh trạng thái <b>thật</b> của Runtime, không phải chuỗi
/// hardcode.
/// </summary>
/// <remarks>
/// Người vận hành liếc thanh này để biết máy đang chạy hay đứng, nên nó phải nói đúng. Bốn trạng
/// thái, bốn màu, không mập mờ.
/// </remarks>
public partial class StatusBarViewModel : ObservableObject, IDisposable
{
    private readonly IRuntimeClient _runtime;
    private bool _disposed;

    public StatusBarViewModel(IRuntimeClient runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

        _runtime.StatusUpdated += OnStatusUpdated;
        _runtime.ConnectionLost += OnConnectionLost;
        _runtime.StateChanged += OnClientStateChanged;

        if (_runtime.LastStatus is not null) Apply(_runtime.LastStatus);
    }

    [ObservableProperty]
    private RuntimeState _runtimeState = RuntimeState.NoProgram;

    /// <summary>Nhãn ngắn, viết hoa, đọc được từ xa.</summary>
    [ObservableProperty]
    private string _stateLabel = "NO PROGRAM";

    /// <summary>
    /// Khoá brush trong ResourceDictionary. View dùng <c>{DynamicResource}</c> theo khoá này nên
    /// đổi theme là màu tự đổi theo.
    /// </summary>
    [ObservableProperty]
    private string _stateBrushKey = "WarningColor";

    [ObservableProperty]
    private string? _faultMessage;

    [ObservableProperty]
    private long _cycleCount;

    [ObservableProperty]
    private double _scanTimeMs;

    [ObservableProperty]
    private double _maxScanTimeMs;

    [ObservableProperty]
    private double _jitterMs;

    [ObservableProperty]
    private string _projectName = "(chưa mở project)";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoStartIndicator))]
    private bool _autoStartEnabled = true;

    public string AutoStartIndicator => AutoStartEnabled ? "⚡ AUTOSTART" : "MANUAL";

    /// <summary>Dòng tóm tắt cho phần bên phải thanh trạng thái.</summary>
    public string MetricsSummary =>
        IsConnected
            ? $"Scan {ScanTimeMs:F1}ms · Max {MaxScanTimeMs:F1}ms · Jitter {JitterMs:F2}ms · Chu kỳ {CycleCount:N0}"
            : "Scan -- · Jitter --";

    private void OnStatusUpdated(object? sender, StatusResponse status) => Apply(status);

    private void Apply(StatusResponse status)
    {
        IsConnected = true;
        RuntimeState = status.State;
        CycleCount = status.CycleCount;
        ScanTimeMs = status.LastScanMs;
        MaxScanTimeMs = status.MaxScanMs;
        JitterMs = status.JitterMs;
        FaultMessage = status.FaultMessage;

        (StateLabel, StateBrushKey) = Describe(status.State);

        OnPropertyChanged(nameof(MetricsSummary));
    }

    /// <summary>Bảng tra trạng thái → (nhãn, màu). Một chỗ duy nhất, để View không tự bịa màu.</summary>
    public static (string Label, string BrushKey) Describe(RuntimeState state) => state switch
    {
        RuntimeState.Running => ("RUN", "SuccessColor"),
        RuntimeState.Stopped => ("OFFLINE", "IdleColor"),
        RuntimeState.Faulted => ("FAULT", "DangerColor"),
        _ => ("NO PROGRAM", "WarningColor")
    };

    private void OnConnectionLost(object? sender, string reason)
    {
        IsConnected = false;
        StateLabel = "MẤT KẾT NỐI";
        StateBrushKey = "DangerColor";
        FaultMessage = reason;

        OnPropertyChanged(nameof(MetricsSummary));
    }

    private void OnClientStateChanged(object? sender, RuntimeClientState state)
    {
        if (state is RuntimeClientState.Connected) return;

        IsConnected = false;

        if (state == RuntimeClientState.Reconnecting)
        {
            StateLabel = "ĐANG NỐI LẠI";
            StateBrushKey = "WarningColor";
        }

        OnPropertyChanged(nameof(MetricsSummary));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _runtime.StatusUpdated -= OnStatusUpdated;
        _runtime.ConnectionLost -= OnConnectionLost;
        _runtime.StateChanged -= OnClientStateChanged;

        GC.SuppressFinalize(this);
    }
}
