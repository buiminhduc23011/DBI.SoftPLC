using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;

namespace DBI.Controller.Studio.Core.Services.Runtime;

/// <summary>
/// Nối <see cref="NamedPipeTransport"/> vào <see cref="IRuntimeClient"/>: poll trạng thái,
/// nhận push, và tự kết nối lại khi Runtime biến mất.
/// </summary>
public sealed class NamedPipeRuntimeClient : IRuntimeClient
{
    /// <summary>Backoff khi nối lại. Dừng ở 10s — nối lại mãi mà không dồn dập.</summary>
    private static readonly TimeSpan[] ReconnectBackoff =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    };

    private readonly SynchronizationContext? _uiContext;
    private readonly Func<string, string, IIpcTransport> _transportFactory;
    private readonly object _gate = new();

    private IIpcTransport? _transport;
    private CancellationTokenSource? _sessionCts;
    private Task? _statusLoop;
    private Task? _deviceStatesLoop;
    private Task? _pushLoop;
    private Task? _reconnectLoop;

    private RuntimeConnectionTarget _target = new();
    private RuntimeClientState _state = RuntimeClientState.Disconnected;
    private int _disposed;

    /// <param name="transportFactory">
    /// Nhận (pipeName, host) và trả về transport. Mặc định là NamedPipe thật; test tiêm bản giả.
    /// </param>
    public NamedPipeRuntimeClient(Func<string, string, IIpcTransport>? transportFactory = null)
    {
        // Bắt SynchronizationContext ngay lúc khởi tạo. Ở Studio, client được tạo trên UI thread
        // nên mọi event tự động về đúng thread — ViewModel không phải tự Dispatcher.BeginInvoke.
        _uiContext = SynchronizationContext.Current;
        _transportFactory = transportFactory ?? ((pipe, host) => new NamedPipeTransport(pipe, host));
    }

    /// <summary>Chu kỳ poll trạng thái.</summary>
    public int StatusPollIntervalMs { get; set; } = 500;

    /// <summary>Chu kỳ poll trạng thái thiết bị (phase-09 Task 09.4).</summary>
    public int DeviceStatesPollIntervalMs { get; set; } = 1000;

    /// <summary>Bật tự kết nối lại. Tắt trong test để không có task chạy nền ngoài ý muốn.</summary>
    public bool AutoReconnect { get; set; } = true;

    public string StudioVersion { get; set; } = "1.0.0";

    public RuntimeClientState State
    {
        get { lock (_gate) return _state; }
    }

    public StatusResponse? LastStatus { get; private set; }

    public event EventHandler<StatusResponse>? StatusUpdated;
    public event EventHandler<IReadOnlyList<DeviceStateInfo>>? DeviceStatesChanged;
    public event EventHandler<TagValueUpdate>? TagValueChanged;
    public event EventHandler<FaultNotification>? FaultOccurred;
    public event EventHandler<string>? ConnectionLost;
    public event EventHandler<RuntimeClientState>? StateChanged;

    // ── Kết nối ──────────────────────────────────────────────────────────────────

    public async Task<bool> ConnectAsync(RuntimeConnectionTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        _target = target;
        await TearDownSessionAsync().ConfigureAwait(false);

        SetState(RuntimeClientState.Connecting);

        try
        {
            var transport = _transportFactory(target.PipeName, target.Host);
            await transport.ConnectAsync(cancellationToken).ConfigureAwait(false);

            var handshake = await HandshakeAsync(transport, cancellationToken).ConfigureAwait(false);

            if (handshake is null)
            {
                await transport.DisposeAsync().ConfigureAwait(false);
                SetState(RuntimeClientState.Disconnected);
                return false;
            }

            _transport = transport;
            _sessionCts = new CancellationTokenSource();

            _statusLoop = Task.Run(() => StatusLoopAsync(_sessionCts.Token), CancellationToken.None);
            _deviceStatesLoop = Task.Run(() => DeviceStatesLoopAsync(_sessionCts.Token), CancellationToken.None);
            _pushLoop = Task.Run(() => PushLoopAsync(_sessionCts.Token), CancellationToken.None);

            SetState(RuntimeClientState.Connected);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            SetState(RuntimeClientState.Disconnected);
            return false;
        }
    }

    /// <summary>Từ chối nếu protocol lệch — nói rõ lý do thay vì để lỗi lạ xuất hiện lúc deploy.</summary>
    private async Task<HandshakeResponse?> HandshakeAsync(IIpcTransport transport, CancellationToken ct)
    {
        var response = await transport.SendAsync(
            NewRequest(CommandType.Handshake,
                new HandshakeRequest(StudioVersion, ProtocolConstants.ProtocolVersion)),
            ct).ConfigureAwait(false);

        if (!response.Ok)
        {
            RaiseConnectionLost(response.Error ?? "Handshake bị từ chối.");
            return null;
        }

        return ProtocolJson.Deserialize<HandshakeResponse>(response.PayloadJson);
    }

    public async Task DisconnectAsync()
    {
        AutoReconnect = false;
        await TearDownSessionAsync().ConfigureAwait(false);
        SetState(RuntimeClientState.Disconnected);
    }

    // ── Lệnh ─────────────────────────────────────────────────────────────────────

    public async Task<DeployResult> DeployAsync(
        byte[] assembly,
        IEnumerable<TagRoute> routes,
        IEnumerable<DeviceSpec> devices,
        CancellationToken cancellationToken = default)
    {
        var payload = new DeployRequest(assembly, routes.ToList(), devices.ToList());

        try
        {
            var response = await SendAsync(NewRequest(CommandType.Deploy, payload), cancellationToken)
                .ConfigureAwait(false);

            return response.Ok ? DeployResult.Succeeded : new DeployResult(false, response.Error);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // Mất kết nối giữa lúc deploy: trả lỗi rõ ràng chứ không treo UI mãi mãi.
            return new DeployResult(false, $"Mất kết nối tới Runtime giữa lúc nạp chương trình: {ex.Message}");
        }
    }

    public Task StartAsync(CancellationToken ct = default) => SendCommandAsync(CommandType.Start, ct);
    public Task StopAsync(CancellationToken ct = default) => SendCommandAsync(CommandType.Stop, ct);
    public Task ResetAsync(CancellationToken ct = default) => SendCommandAsync(CommandType.Reset, ct);

    private async Task SendCommandAsync(CommandType type, CancellationToken ct)
    {
        var response = await SendAsync(NewRequest(type), ct).ConfigureAwait(false);

        if (!response.Ok)
            throw new InvalidOperationException(response.Error ?? $"Lệnh {type} thất bại.");

        var status = ProtocolJson.Deserialize<StatusResponse>(response.PayloadJson);
        if (status is not null) PublishStatus(status);
    }

    public Task SubscribeTagsAsync(IEnumerable<string> tagNames, CancellationToken ct = default) =>
        SendAsync(NewRequest(CommandType.SubscribeTags, new SubscribeTagsRequest(tagNames.ToList())), ct);

    public Task UnsubscribeTagsAsync(IEnumerable<string> tagNames, CancellationToken ct = default) =>
        SendAsync(NewRequest(CommandType.UnsubscribeTags, new SubscribeTagsRequest(tagNames.ToList())), ct);

    public async Task<bool> ForceTagAsync(string tagName, object value, bool enable, CancellationToken ct = default)
    {
        var payload = new ForceTagRequest(tagName, ProtocolJson.Serialize(value), enable);
        var response = await SendAsync(NewRequest(CommandType.ForceTag, payload), ct).ConfigureAwait(false);

        return response.Ok;
    }

    public async Task<IReadOnlyList<ForceInfo>> GetForcesAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(NewRequest(CommandType.GetForceList), ct).ConfigureAwait(false);
        return response.Ok
            ? ProtocolJson.Deserialize<ForceListResponse>(response.PayloadJson)?.Forces ?? []
            : [];
    }

    public async Task<IReadOnlyList<DeviceStateInfo>> GetDeviceStatesAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(NewRequest(CommandType.GetDeviceStates), ct).ConfigureAwait(false);

        if (!response.Ok) return Array.Empty<DeviceStateInfo>();

        return ProtocolJson.Deserialize<DeviceStatesResponse>(response.PayloadJson)?.Devices
            ?? (IReadOnlyList<DeviceStateInfo>)Array.Empty<DeviceStateInfo>();
    }

    public async Task<TestConnectionResponse> TestDeviceConnectionAsync(DeviceSpec device, CancellationToken ct = default)
    {
        var response = await SendAsync(
            NewRequest(CommandType.TestDeviceConnection, new TestConnectionRequest(device)), ct).ConfigureAwait(false);

        if (!response.Ok)
            return new TestConnectionResponse(false, response.Error ?? "Test kết nối thất bại.");

        return ProtocolJson.Deserialize<TestConnectionResponse>(response.PayloadJson)
            ?? new TestConnectionResponse(false, "Runtime trả về payload không đọc được.");
    }

    public async Task<WriteTagResponse> WriteTagAsync(string tagName, object value, CancellationToken ct = default)
    {
        var response = await SendAsync(
            NewRequest(CommandType.WriteTag, new WriteTagRequest(tagName, ProtocolJson.Serialize(value))), ct)
            .ConfigureAwait(false);

        return response.Ok
            ? new WriteTagResponse(true, null)
            : new WriteTagResponse(false, response.Error ?? "Ghi tag thất bại.");
    }

    private static IpcRequest NewRequest(CommandType type, object? payload = null) =>
        new(Guid.NewGuid().ToString("N"), type, payload is null ? null : ProtocolJson.Serialize(payload));

    private Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken ct)
    {
        var transport = _transport
            ?? throw new InvalidOperationException("Chưa kết nối tới Runtime.");

        return transport.SendAsync(request, ct);
    }

    // ── Vòng nền ─────────────────────────────────────────────────────────────────

    private async Task StatusLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await SendAsync(NewRequest(CommandType.GetStatus), ct).ConfigureAwait(false);

                if (response.Ok)
                {
                    var status = ProtocolJson.Deserialize<StatusResponse>(response.PayloadJson);
                    if (status is not null) PublishStatus(status);
                }

                await Task.Delay(StatusPollIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                HandleConnectionDropped($"Mất kết nối tới Runtime: {ex.Message}");
                return;
            }
        }
    }

    private async Task DeviceStatesLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await SendAsync(NewRequest(CommandType.GetDeviceStates), ct).ConfigureAwait(false);

                if (response.Ok)
                {
                    var states = ProtocolJson.Deserialize<DeviceStatesResponse>(response.PayloadJson)?.Devices;
                    if (states is not null) RaiseDeviceStates(states);
                }

                await Task.Delay(DeviceStatesPollIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Trạng thái thiết bị là tiện ích theo dõi — mất một vòng thì bỏ qua,
                // vòng StatusLoopAsync mới là bên phát hiện đứt kết nối.
            }
        }
    }

    private void RaiseDeviceStates(IReadOnlyList<DeviceStateInfo> states)
    {
        if (_uiContext is not null) _uiContext.Post(_ => DeviceStatesChanged?.Invoke(this, states), null);
        else DeviceStatesChanged?.Invoke(this, states);
    }

    private async Task PushLoopAsync(CancellationToken ct)
    {
        var transport = _transport;
        if (transport is null) return;

        try
        {
            await foreach (var push in transport.ReceivePushAsync(ct).ConfigureAwait(false))
            {
                switch (push.Channel)
                {
                    case ProtocolConstants.TagChannel:
                        var batch = ProtocolJson.Deserialize<TagValueBatch>(push.PayloadJson);
                        foreach (var update in batch?.Updates ?? new List<TagValueUpdate>())
                            Raise(TagValueChanged, update);
                        break;

                    case ProtocolConstants.FaultChannel:
                        var fault = ProtocolJson.Deserialize<FaultNotification>(push.PayloadJson);
                        if (fault is not null) Raise(FaultOccurred, fault);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Phiên đã đóng.
        }
        catch (Exception ex)
        {
            HandleConnectionDropped($"Mất luồng dữ liệu từ Runtime: {ex.Message}");
        }
    }

    private void PublishStatus(StatusResponse status)
    {
        LastStatus = status;
        Raise(StatusUpdated, status);
    }

    // ── Mất kết nối và nối lại ───────────────────────────────────────────────────

    private void HandleConnectionDropped(string reason)
    {
        lock (_gate)
        {
            if (_state is RuntimeClientState.Disconnected or RuntimeClientState.Reconnecting) return;
        }

        RaiseConnectionLost(reason);

        if (!AutoReconnect)
        {
            SetState(RuntimeClientState.Disconnected);
            return;
        }

        SetState(RuntimeClientState.Reconnecting);
        _reconnectLoop = Task.Run(ReconnectLoopAsync, CancellationToken.None);
    }

    private async Task ReconnectLoopAsync()
    {
        for (int attempt = 0; AutoReconnect && Volatile.Read(ref _disposed) == 0; attempt++)
        {
            var delay = ReconnectBackoff[Math.Min(attempt, ReconnectBackoff.Length - 1)];
            await Task.Delay(delay).ConfigureAwait(false);

            if (!AutoReconnect || Volatile.Read(ref _disposed) == 1) return;

            // Giữ nguyên AutoReconnect: ConnectAsync không được tự tắt nó.
            if (await ConnectAsync(_target).ConfigureAwait(false)) return;

            SetState(RuntimeClientState.Reconnecting);
        }
    }

    private async Task TearDownSessionAsync()
    {
        CancellationTokenSource? cts;
        IIpcTransport? transport;
        Task?[] loops;

        lock (_gate)
        {
            cts = _sessionCts;
            transport = _transport;
            loops = new[] { _statusLoop, _pushLoop };

            _sessionCts = null;
            _transport = null;
            _statusLoop = null;
            _pushLoop = null;
        }

        cts?.Cancel();

        foreach (var loop in loops)
        {
            if (loop is null) continue;

            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        if (transport is not null) await transport.DisposeAsync().ConfigureAwait(false);
        cts?.Dispose();
    }

    // ── Raise event trên UI thread ───────────────────────────────────────────────

    private void SetState(RuntimeClientState state)
    {
        lock (_gate)
        {
            if (_state == state) return;
            _state = state;
        }

        Raise(StateChanged, state);
    }

    private void RaiseConnectionLost(string reason) => Raise(ConnectionLost, reason);

    /// <summary>
    /// Đẩy event về context bắt được lúc khởi tạo. Không có context (test, console) thì gọi thẳng.
    /// </summary>
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        AutoReconnect = false;
        await TearDownSessionAsync().ConfigureAwait(false);

        if (_reconnectLoop is not null)
        {
            try { await _reconnectLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        SetState(RuntimeClientState.Disconnected);
    }
}
