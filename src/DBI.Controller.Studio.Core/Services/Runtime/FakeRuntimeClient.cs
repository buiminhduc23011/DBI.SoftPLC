using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;

namespace DBI.Controller.Studio.Core.Services.Runtime;

/// <summary>
/// Bản giả in-memory của <see cref="IRuntimeClient"/>.
/// </summary>
/// <remarks>
/// Cho phép phát triển và test UI mà không cần Runtime chạy thật — phase-04/05/06 làm song song
/// được. Thay cho <c>LiveMonitoringService</c> cũ (51 dòng sinh số liệu giả rời rạc, không nối
/// với bất kỳ contract nào).
/// </remarks>
public sealed class FakeRuntimeClient : IRuntimeClient
{
    private readonly SynchronizationContext? _uiContext;
    private readonly Dictionary<string, object> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _subscribed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _forces = new(StringComparer.OrdinalIgnoreCase);

    private RuntimeClientState _state = RuntimeClientState.Disconnected;
    private RuntimeState _runtimeState = RuntimeState.NoProgram;
    private long _cycleCount;
    private string? _faultMessage;

    public FakeRuntimeClient() => _uiContext = SynchronizationContext.Current;

    // ── Điều khiển hành vi giả lập từ test / UI ──────────────────────────────────

    /// <summary>Đặt <c>false</c> để <see cref="ConnectAsync"/> thất bại như khi chưa bật Runtime.</summary>
    public bool CanConnect { get; set; } = true;

    /// <summary>Lỗi mà <see cref="DeployAsync"/> trả về. <c>null</c> nghĩa là deploy thành công.</summary>
    public string? DeployError { get; set; }

    public List<DeviceStateInfo> DeviceStates { get; } = new();

    public IReadOnlyCollection<string> SubscribedTags => _subscribed;

    public RuntimeClientState State => _state;

    public StatusResponse? LastStatus { get; private set; }

    public event EventHandler<StatusResponse>? StatusUpdated;
    public event EventHandler<TagValueUpdate>? TagValueChanged;
    public event EventHandler<FaultNotification>? FaultOccurred;
    public event EventHandler<string>? ConnectionLost;
    public event EventHandler<RuntimeClientState>? StateChanged;

    public Task<bool> ConnectAsync(RuntimeConnectionTarget target, CancellationToken ct = default)
    {
        if (!CanConnect)
        {
            SetState(RuntimeClientState.Disconnected);
            return Task.FromResult(false);
        }

        SetState(RuntimeClientState.Connected);
        PublishStatus();

        return Task.FromResult(true);
    }

    public Task DisconnectAsync()
    {
        SetState(RuntimeClientState.Disconnected);
        return Task.CompletedTask;
    }

    public Task<DeployResult> DeployAsync(
        byte[] assembly,
        IEnumerable<TagRoute> routes,
        IEnumerable<DeviceSpec> devices,
        CancellationToken ct = default)
    {
        if (DeployError is not null)
            return Task.FromResult(new DeployResult(false, DeployError));

        // Tag khai trong route xuất hiện ngay với giá trị mặc định, để Watch Table có gì mà hiện.
        foreach (var route in routes)
        {
            _tags[route.TagName] = route.DataType switch
            {
                TagDataType.Int => 0,
                TagDataType.Real => 0f,
                _ => false
            };
        }

        DeviceStates.Clear();
        foreach (var device in devices)
            DeviceStates.Add(new DeviceStateInfo(device.Name, nameof(ConnectionState.Connected), null));

        _runtimeState = RuntimeState.Running;
        _cycleCount = 0;
        _faultMessage = null;

        PublishStatus();
        return Task.FromResult(DeployResult.Succeeded);
    }

    public Task StartAsync(CancellationToken ct = default)
    {
        _runtimeState = RuntimeState.Running;
        PublishStatus();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _runtimeState = RuntimeState.Stopped;

        // Dừng máy là xả output — giả lập cũng phải phản ánh đúng để UI không nói dối.
        foreach (string key in _tags.Keys.ToList())
            SetTagValue(key, _tags[key] switch { int => 0, float => 0f, _ => (object)false });

        PublishStatus();
        return Task.CompletedTask;
    }

    public Task ResetAsync(CancellationToken ct = default)
    {
        _faultMessage = null;
        _runtimeState = RuntimeState.Stopped;
        PublishStatus();
        return Task.CompletedTask;
    }

    public Task SubscribeTagsAsync(IEnumerable<string> tagNames, CancellationToken ct = default)
    {
        foreach (string tag in tagNames)
        {
            _subscribed.Add(tag);

            if (_tags.TryGetValue(tag, out var value))
                Raise(TagValueChanged, NewUpdate(tag, value));
        }

        return Task.CompletedTask;
    }

    public Task UnsubscribeTagsAsync(IEnumerable<string> tagNames, CancellationToken ct = default)
    {
        foreach (string tag in tagNames) _subscribed.Remove(tag);
        return Task.CompletedTask;
    }

    public Task<bool> ForceTagAsync(string tagName, object value, bool enable, CancellationToken ct = default)
    {
        if (enable) { _forces[tagName] = value; SetTagValue(tagName, value); }
        else _forces.Remove(tagName);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<ForceInfo>> GetForcesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ForceInfo>>(_forces.Select(f => new ForceInfo(f.Key, ProtocolJson.Serialize(f.Value))).ToList());

    public Task<IReadOnlyList<DeviceStateInfo>> GetDeviceStatesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeviceStateInfo>>(DeviceStates.ToList());

    // ── Điều khiển giả lập ───────────────────────────────────────────────────────

    /// <summary>Đổi giá trị một tag. Chỉ tag đã đăng ký mới phát event, giống Runtime thật.</summary>
    public void SetTagValue(string tagName, object value)
    {
        bool changed = !_tags.TryGetValue(tagName, out var previous) || !Equals(previous, value);
        _tags[tagName] = value;

        if (changed && _subscribed.Contains(tagName))
            Raise(TagValueChanged, NewUpdate(tagName, value));
    }

    /// <summary>Giả lập logic người dùng ném lỗi.</summary>
    public void SimulateFault(string message)
    {
        _faultMessage = message;
        _runtimeState = RuntimeState.Faulted;

        Raise(FaultOccurred, new FaultNotification(
            message, "   at UserProgram.Main.Execute()", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        PublishStatus();
    }

    /// <summary>Giả lập Runtime biến mất (mất điện, bị kill).</summary>
    public void SimulateConnectionLost(string reason = "Runtime không phản hồi.")
    {
        SetState(RuntimeClientState.Disconnected);
        Raise(ConnectionLost, reason);
    }

    /// <summary>Tăng số chu kỳ và phát status, để UI có gì nhúc nhích.</summary>
    public void AdvanceCycles(long count = 1)
    {
        _cycleCount += count;
        PublishStatus();
    }

    private TagValueUpdate NewUpdate(string tagName, object value) =>
        new(tagName, ProtocolJson.Serialize(value), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    private void PublishStatus()
    {
        LastStatus = new StatusResponse(_runtimeState, _cycleCount, 1.2, 2.4, 0.1, _faultMessage);
        Raise(StatusUpdated, LastStatus);
    }

    private void SetState(RuntimeClientState state)
    {
        if (_state == state) return;

        _state = state;
        Raise(StateChanged, state);
    }

    private void Raise<T>(EventHandler<T>? handler, T args)
    {
        if (handler is null) return;

        if (_uiContext is null || _uiContext == SynchronizationContext.Current)
        {
            handler(this, args);
            return;
        }

        _uiContext.Post(_ => handler(this, args), null);
    }

    public ValueTask DisposeAsync()
    {
        SetState(RuntimeClientState.Disconnected);
        return ValueTask.CompletedTask;
    }
}
