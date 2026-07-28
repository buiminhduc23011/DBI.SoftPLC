using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using DBI.Controller.Protocol;
using DBI.Controller.Runtime.Host;
using DBI.Controller.Runtime.Safety;

namespace DBI.Controller.Runtime.Ipc;

/// <summary>
/// Phục vụ lệnh của Studio qua NamedPipe.
/// </summary>
/// <remarks>
/// <para>v1 nhận <b>một</b> client tại một thời điểm. Client thứ hai bị từ chối kèm lý do rõ ràng
/// thay vì treo im lặng.</para>
/// <para>⚠️ Vòng push monitoring chạy trên <see cref="Task"/> riêng, <b>tuyệt đối không</b> trên
/// scan thread. Nó chỉ đọc ảnh chụp đã chốt. Phá điểm này là phá luôn lý do tồn tại của ADR-001.</para>
/// </remarks>
public sealed class IpcServer : IAsyncDisposable
{
    /// <summary>Chu kỳ đẩy giá trị tag lên Studio. 100ms đủ mượt mắt người, xa chu kỳ scan 20ms.</summary>
    public int PushIntervalMs { get; set; } = 100;

    private readonly RuntimeHost _host;
    private readonly string _pipeName;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly HashSet<string> _subscribedTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastPushedValues = new(StringComparer.OrdinalIgnoreCase);

    private NamedPipeServerStream? _activeClient;
    private int _clientConnected;

    public IpcServer(RuntimeHost host, string pipeName = ProtocolConstants.DefaultPipeName)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pipeName = pipeName;

        _host.FaultOccurred += OnFaultOccurred;
    }

    public string PipeName => _pipeName;

    public bool HasClient => Volatile.Read(ref _clientConnected) == 1;

    public async Task RunAsync(CancellationToken ct)
    {
        var pushLoop = Task.Run(() => PushLoopAsync(ct), CancellationToken.None);
        Task serving = Task.CompletedTask;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // maxNumberOfServerInstances = 2: một chỗ phục vụ client hiện tại, một chỗ để
                // client thứ hai kết nối được và NGHE được lời từ chối.
                var pipe = new NamedPipeServerStream(
                    _pipeName, PipeDirection.InOut, maxNumberOfServerInstances: 2,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                try
                {
                    await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    break;
                }

                if (Interlocked.CompareExchange(ref _clientConnected, 1, 0) == 1)
                {
                    await RejectSecondClientAsync(pipe, ct).ConfigureAwait(false);
                    continue;
                }

                _activeClient = pipe;

                // Phục vụ trên task riêng: nếu await ngay ở đây thì vòng accept đứng lại, không tạo
                // instance pipe mới, và client thứ hai treo vô hạn thay vì nhận được lời từ chối.
                serving = Task.Run(() => ServeClientAsync(pipe, ct), CancellationToken.None);
            }
        }
        finally
        {
            await WhenAllIgnoringCancellation(serving, pushLoop).ConfigureAwait(false);
        }
    }

    private static async Task WhenAllIgnoringCancellation(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Đang tắt theo yêu cầu.
        }
    }

    private async Task RejectSecondClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            var refusal = IpcResponse.Failure(
                "connection",
                "Runtime đang phục vụ một Studio khác. Đóng phiên kia rồi kết nối lại.");

            await IpcFraming.WriteMessageAsync(pipe, ProtocolJson.Serialize(refusal), ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Client đã bỏ đi trước khi nghe lời từ chối — không sao.
        }
        finally
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && pipe.IsConnected)
            {
                string? json = await IpcFraming.ReadMessageAsync(pipe, ct).ConfigureAwait(false);
                if (json is null) break;

                var request = ProtocolJson.Deserialize<IpcRequest>(json);
                if (request is null) continue;

                var response = await HandleAsync(request, ct).ConfigureAwait(false);
                await SendAsync(ProtocolJson.Serialize(response), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Runtime đang tắt.
        }
        catch (Exception ex)
        {
            // Studio bị kill giữa chừng là chuyện bình thường — máy phải chạy tiếp, chỉ ghi log.
            Console.Error.WriteLine($"[ipc] Mất kết nối với Studio: {ex.Message}");
        }
        finally
        {
            _activeClient = null;
            Volatile.Write(ref _clientConnected, 0);

            lock (_subscribedTags)
            {
                _subscribedTags.Clear();
                _lastPushedValues.Clear();
            }

            await pipe.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ── Dispatch ─────────────────────────────────────────────────────────────────

    private async Task<IpcResponse> HandleAsync(IpcRequest request, CancellationToken ct)
    {
        try
        {
            return request.Type switch
            {
                CommandType.Handshake => Handshake(request),
                CommandType.GetStatus => Ok(request, _host.GetStatus()),
                CommandType.Deploy => await DeployAsync(request, ct).ConfigureAwait(false),
                CommandType.Start => StartCommand(request),
                CommandType.Stop => StopCommand(request),
                CommandType.Reset => ResetCommand(request),
                CommandType.SubscribeTags => SubscribeTags(request),
                CommandType.UnsubscribeTags => UnsubscribeTags(request),
                CommandType.GetDeviceStates => Ok(request, _host.GetDeviceStates()),
                CommandType.ForceTag => ForceTag(request),
                CommandType.GetForceList => Ok(request, new ForceListResponse(_host.GetForces().Select(f => new ForceInfo(f.Key, ProtocolJson.Serialize(f.Value))).ToList())),
                _ => IpcResponse.Failure(request.RequestId, $"Lệnh '{request.Type}' chưa hỗ trợ.")
            };
        }
        catch (Exception ex)
        {
            return IpcResponse.Failure(request.RequestId, ex.Message);
        }
    }

    private static IpcResponse Ok<T>(IpcRequest request, T payload) =>
        IpcResponse.Success(request.RequestId, ProtocolJson.Serialize(payload));

    private IpcResponse Handshake(IpcRequest request)
    {
        var handshake = ProtocolJson.Deserialize<HandshakeRequest>(request.PayloadJson);

        if (handshake is null)
            return IpcResponse.Failure(request.RequestId, "Handshake thiếu payload.");

        if (handshake.ProtocolVersion != ProtocolConstants.ProtocolVersion)
        {
            return IpcResponse.Failure(request.RequestId,
                $"[{ErrorCodes.ProtocolMismatch}] Studio dùng protocol v{handshake.ProtocolVersion}, " +
                $"Runtime dùng v{ProtocolConstants.ProtocolVersion}. Cập nhật cho khớp phiên bản.");
        }

        string runtimeVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        return Ok(request, new HandshakeResponse(
            runtimeVersion, ProtocolConstants.ProtocolVersion, _host.State));
    }

    private async Task<IpcResponse> DeployAsync(IpcRequest request, CancellationToken ct)
    {
        var deploy = ProtocolJson.Deserialize<DeployRequest>(request.PayloadJson);

        if (deploy is null)
            return IpcResponse.Failure(request.RequestId, "Deploy thiếu payload.");

        var result = await _host.DeployAsync(deploy, ct).ConfigureAwait(false);

        return result.Ok
            ? Ok(request, result)
            : IpcResponse.Failure(request.RequestId, FormatError(result.Error, result.ErrorCode));
    }

    private static string FormatError(string? error, string? code) =>
        string.IsNullOrEmpty(code) ? error ?? "Lỗi không rõ." : $"[{code}] {error}";

    private IpcResponse StartCommand(IpcRequest request)
    {
        _host.Start();
        return Ok(request, _host.GetStatus());
    }

    private IpcResponse StopCommand(IpcRequest request)
    {
        _host.Stop();
        return Ok(request, _host.GetStatus());
    }

    private IpcResponse ResetCommand(IpcRequest request)
    {
        _host.Reset();
        return Ok(request, _host.GetStatus());
    }

    private IpcResponse ForceTag(IpcRequest request)
    {
        var force = ProtocolJson.Deserialize<ForceTagRequest>(request.PayloadJson);
        if (force is null) return IpcResponse.Failure(request.RequestId, "ForceTag thiếu payload.");
        object? value = JsonSerializer.Deserialize<object>(force.ValueJson);
        if (value is JsonElement element)
            value = element.ValueKind switch { JsonValueKind.True or JsonValueKind.False => element.GetBoolean(), JsonValueKind.Number when element.TryGetInt32(out var i) => i, JsonValueKind.Number => element.GetSingle(), _ => null };
        if (value is null) return IpcResponse.Failure(request.RequestId, "Giá trị force không hợp lệ.");
        _host.SetForce(force.TagName, value, force.Enable);
        return Ok(request, new { ok = true });
    }

    private IpcResponse SubscribeTags(IpcRequest request)
    {
        var subscribe = ProtocolJson.Deserialize<SubscribeTagsRequest>(request.PayloadJson);

        if (subscribe is null)
            return IpcResponse.Failure(request.RequestId, "SubscribeTags thiếu payload.");

        lock (_subscribedTags)
        {
            foreach (string tag in subscribe.TagNames)
            {
                _subscribedTags.Add(tag);

                // Xoá giá trị đã nhớ để lần push đầu luôn gửi trạng thái hiện tại —
                // nếu không, tag đăng ký lại mà giá trị không đổi sẽ không bao giờ được gửi.
                _lastPushedValues.Remove(tag);
            }
        }

        return IpcResponse.Success(request.RequestId);
    }

    private IpcResponse UnsubscribeTags(IpcRequest request)
    {
        var unsubscribe = ProtocolJson.Deserialize<SubscribeTagsRequest>(request.PayloadJson);

        if (unsubscribe is not null)
        {
            lock (_subscribedTags)
            {
                foreach (string tag in unsubscribe.TagNames)
                {
                    _subscribedTags.Remove(tag);
                    _lastPushedValues.Remove(tag);
                }
            }
        }

        return IpcResponse.Success(request.RequestId);
    }

    // ── Push ─────────────────────────────────────────────────────────────────────

    private async Task PushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PushIntervalMs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!HasClient) continue;

            var changed = CollectChangedTags();
            if (changed.Count == 0) continue;

            try
            {
                await PushAsync(ProtocolConstants.TagChannel, new TagValueBatch(changed), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // Client vừa ngắt — vòng phục vụ sẽ dọn, ở đây bỏ qua.
            }
        }
    }

    /// <summary>Chỉ gửi tag đã đăng ký <b>và</b> có giá trị thay đổi — băng thông IPC không phải vô hạn.</summary>
    private List<TagValueUpdate> CollectChangedTags()
    {
        var snapshot = _host.ReadTags();
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var changed = new List<TagValueUpdate>();

        lock (_subscribedTags)
        {
            foreach (string tagName in _subscribedTags)
            {
                if (!snapshot.TryGetValue(tagName, out var value)) continue;

                string valueJson = ProtocolJson.Serialize(value);

                if (_lastPushedValues.TryGetValue(tagName, out var previous) && previous == valueJson)
                    continue;

                _lastPushedValues[tagName] = valueJson;
                changed.Add(new TagValueUpdate(tagName, valueJson, timestamp));
            }
        }

        return changed;
    }

    private void OnFaultOccurred(object? sender, FaultEvent fault)
    {
        var notification = new FaultNotification(
            fault.Message, fault.StackTrace, fault.OccurredAt.ToUnixTimeMilliseconds());

        // Fire-and-forget: chuỗi xử lý an toàn đang chạy, không được chặn nó vì chuyện gửi tin.
        _ = PushAsync(ProtocolConstants.FaultChannel, notification, CancellationToken.None);
    }

    private async Task PushAsync<T>(string channel, T payload, CancellationToken ct)
    {
        var pipe = _activeClient;
        if (pipe is null || !pipe.IsConnected) return;

        var push = new IpcPush(channel, ProtocolJson.Serialize(payload));
        await SendAsync(ProtocolJson.Serialize(push), ct).ConfigureAwait(false);
    }

    /// <summary>Mọi lần ghi đi qua đây — response và push chen nhau sẽ làm hỏng khung bản tin.</summary>
    private async Task SendAsync(string json, CancellationToken ct)
    {
        var pipe = _activeClient;
        if (pipe is null || !pipe.IsConnected) return;

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await IpcFraming.WriteMessageAsync(pipe, json, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _host.FaultOccurred -= OnFaultOccurred;
        _activeClient?.Dispose();
        _writeLock.Dispose();

        return ValueTask.CompletedTask;
    }
}
