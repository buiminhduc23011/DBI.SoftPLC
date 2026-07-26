using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DBI.Controller.Protocol;

/// <summary>
/// Transport phía client (Studio) qua NamedPipe.
/// </summary>
/// <remarks>
/// Một luồng đọc duy nhất bơm bản tin từ pipe rồi phân loại: có <c>requestId</c> khớp thì hoàn tất
/// <c>TaskCompletionSource</c> đang chờ, còn lại là push đẩy vào hàng đợi. Nhờ vậy nhiều lệnh gửi
/// song song không giẫm chân nhau, và push đến giữa lúc đang chờ response không bị nuốt mất.
/// </remarks>
public sealed class NamedPipeTransport : IIpcTransport
{
    private readonly string _serverName;
    private readonly string _pipeName;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IpcResponse>> _pending = new();
    private readonly Channel<IpcPush> _pushes = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _readLoopCts;
    private Task? _readLoop;
    private int _disposed;

    public NamedPipeTransport(string pipeName = ProtocolConstants.DefaultPipeName, string serverName = ".")
    {
        _pipeName = pipeName;
        _serverName = serverName;
    }

    public bool IsConnected => _pipe?.IsConnected == true;

    public event EventHandler<string>? Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected) return;

        var pipe = new NamedPipeClientStream(
            _serverName, _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);

        _pipe = pipe;
        _readLoopCts = new CancellationTokenSource();
        _readLoop = Task.Run(() => ReadLoopAsync(_readLoopCts.Token), CancellationToken.None);
    }

    public async Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken cancellationToken = default)
    {
        var pipe = _pipe ?? throw new InvalidOperationException("Chưa kết nối tới Runtime.");

        var completion = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.RequestId] = completion;

        try
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await IpcFraming.WriteMessageAsync(pipe, ProtocolJson.Serialize(request), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }

            using var registration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));

            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(request.RequestId, out _);
        }
    }

    public async IAsyncEnumerable<IpcPush> ReceivePushAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IpcPush? push = await _pushes.TakeAsync(cancellationToken).ConfigureAwait(false);
            if (push is null) yield break;

            yield return push;
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        string reason = "Kết nối tới Runtime đã đóng.";

        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? json = await IpcFraming.ReadMessageAsync(_pipe!, ct).ConfigureAwait(false);
                if (json is null) break;

                Dispatch(json);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            reason = $"Mất kết nối tới Runtime: {ex.Message}";
        }

        FailPending(reason);
        _pushes.Complete();
        Disconnected?.Invoke(this, reason);
    }

    /// <summary>
    /// Phân loại bản tin đến. Response và push khác nhau ở chỗ response có <c>requestId</c>;
    /// đọc thử trường đó thay vì đoán theo thứ tự đến.
    /// </summary>
    private void Dispatch(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.TryGetProperty(ProtocolJson.RequestIdProperty, out var requestId) &&
            requestId.ValueKind == JsonValueKind.String)
        {
            var response = ProtocolJson.Deserialize<IpcResponse>(json);
            if (response is not null && _pending.TryRemove(response.RequestId, out var completion))
                completion.TrySetResult(response);

            return;
        }

        var push = ProtocolJson.Deserialize<IpcPush>(json);
        if (push is not null) _pushes.Add(push);
    }

    private void FailPending(string reason)
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var completion))
                completion.TrySetException(new IOException(reason));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

        _readLoopCts?.Cancel();

        if (_readLoop is not null)
        {
            try { await _readLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* dừng theo yêu cầu */ }
        }

        FailPending("Transport đã đóng.");
        _pushes.Complete();

        _pipe?.Dispose();
        _readLoopCts?.Dispose();
        _writeLock.Dispose();
    }

    /// <summary>Hàng đợi push đơn giản, chờ không tốn CPU và kết thúc sạch khi transport đóng.</summary>
    private sealed class Channel<T> where T : class
    {
        private readonly ConcurrentQueue<T> _items = new();
        private readonly SemaphoreSlim _signal = new(0);
        private volatile bool _completed;

        public void Add(T item)
        {
            _items.Enqueue(item);
            _signal.Release();
        }

        public void Complete()
        {
            _completed = true;
            _signal.Release();
        }

        public async Task<T?> TakeAsync(CancellationToken ct)
        {
            while (true)
            {
                if (_items.TryDequeue(out var item)) return item;
                if (_completed) return null;

                await _signal.WaitAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
