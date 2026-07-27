using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;

namespace DBI.Controller.Studio.Core.Services.Runtime;

public enum RuntimeClientState
{
    Disconnected,
    Connecting,
    Connected,

    /// <summary>Mất kết nối, đang thử nối lại theo backoff.</summary>
    Reconnecting
}

public record DeployResult(bool Ok, string? Error)
{
    public static readonly DeployResult Succeeded = new(true, null);
}

/// <summary>
/// Phía Studio của cầu IPC.
/// </summary>
/// <remarks>
/// <para>Studio <b>không bao giờ</b> giữ tham chiếu trực tiếp tới object của Runtime — mọi thứ đi
/// qua contract này (ADR-001).</para>
///
/// <para>Mọi event đều raise trên <see cref="SynchronizationContext"/> bắt được lúc khởi tạo. Ở
/// Studio đó là UI thread, nên ViewModel bind thẳng được mà không phải tự gọi
/// <c>Dispatcher.BeginInvoke</c>.</para>
/// </remarks>
public interface IRuntimeClient : IAsyncDisposable
{
    RuntimeClientState State { get; }

    /// <summary>Trạng thái Runtime lần cuối biết được. <c>null</c> khi chưa từng kết nối.</summary>
    StatusResponse? LastStatus { get; }

    Task<bool> ConnectAsync(RuntimeConnectionTarget target, CancellationToken cancellationToken = default);
    Task DisconnectAsync();

    Task<DeployResult> DeployAsync(
        byte[] assembly,
        IEnumerable<TagRoute> routes,
        IEnumerable<DeviceSpec> devices,
        CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);

    Task SubscribeTagsAsync(IEnumerable<string> tagNames, CancellationToken cancellationToken = default);
    Task UnsubscribeTagsAsync(IEnumerable<string> tagNames, CancellationToken cancellationToken = default);

    /// <summary>phase-11.</summary>
    Task<bool> ForceTagAsync(string tagName, object value, bool enable, CancellationToken cancellationToken = default);

    /// <summary>phase-09.</summary>
    Task<IReadOnlyList<DeviceStateInfo>> GetDeviceStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Poll 500ms khi đã kết nối.</summary>
    event EventHandler<StatusResponse>? StatusUpdated;

    /// <summary>Runtime đẩy lên, chỉ tag đã đăng ký và chỉ khi giá trị đổi.</summary>
    event EventHandler<TagValueUpdate>? TagValueChanged;

    event EventHandler<FaultNotification>? FaultOccurred;

    event EventHandler<string>? ConnectionLost;

    event EventHandler<RuntimeClientState>? StateChanged;
}

/// <summary>Nơi cần kết nối tới. Ánh xạ từ <c>RuntimeTarget</c> của project.</summary>
/// <param name="PipeName">Tên NamedPipe khi kết nối cục bộ.</param>
/// <param name="Host">Máy chứa Runtime. <c>.</c> hoặc <c>127.0.0.1</c> nghĩa là cục bộ.</param>
/// <param name="Port">Cổng TCP, dành cho phase-13+.</param>
public record RuntimeConnectionTarget(
    string PipeName = ProtocolConstants.DefaultPipeName,
    string Host = ".",
    int Port = ProtocolConstants.DefaultTcpPort)
{
    /// <summary>
    /// Runtime nằm cùng máy hay không. Quyết định Studio có được phép tự khởi động Runtime không —
    /// không bao giờ tự khởi động tiến trình trên máy người khác.
    /// </summary>
    public bool IsLocal =>
        Host is "." or "localhost" or "127.0.0.1" or "::1" ||
        string.Equals(Host, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
}
