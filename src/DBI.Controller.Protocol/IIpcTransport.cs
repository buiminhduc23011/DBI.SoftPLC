namespace DBI.Controller.Protocol;

/// <summary>
/// Kênh liên lạc Studio → Runtime. Trừu tượng hoá sẵn để phase-13+ cắm gRPC/TCP
/// mà không đụng lớp trên.
/// </summary>
public interface IIpcTransport : IAsyncDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<IpcResponse> SendAsync(IpcRequest request, CancellationToken cancellationToken = default);

    /// <summary>Luồng bản tin server chủ động đẩy lên (tag values, fault).</summary>
    IAsyncEnumerable<IpcPush> ReceivePushAsync(CancellationToken cancellationToken = default);

    event EventHandler<string>? Disconnected;
}
