using DBI.Controller.Core.Models;

namespace DBI.Controller.Protocol;

public static class ProtocolConstants
{
    /// <summary>Tăng khi phá tương thích. Handshake từ chối nếu hai bên lệch.</summary>
    public const int ProtocolVersion = 1;

    public const string DefaultPipeName = "DBI.Runtime";
    public const int DefaultTcpPort = 5580;

    /// <summary>Kênh push khi Runtime gặp lỗi chương trình người dùng.</summary>
    public const string FaultChannel = "fault";

    /// <summary>Kênh push giá trị tag đã đăng ký qua <see cref="CommandType.SubscribeTags"/>.</summary>
    public const string TagChannel = "tags";

    /// <summary>Chặn payload quá lớn — 64MB đủ cho assembly người dùng, đủ nhỏ để không cạn RAM.</summary>
    public const int MaxMessageBytes = 64 * 1024 * 1024;
}

public enum RuntimeState
{
    /// <summary>Chưa có chương trình nào được nạp.</summary>
    NoProgram,

    /// <summary>Có chương trình, chưa chạy.</summary>
    Stopped,

    Running,

    /// <summary>Logic người dùng ném lỗi. Output đã xả về safe state. Cần <see cref="CommandType.Reset"/>.</summary>
    Faulted
}

public enum CommandType
{
    Handshake,
    GetStatus,
    Deploy,
    Start,
    Stop,
    Reset,
    SubscribeTags,
    UnsubscribeTags,
    ForceTag,
    GetForceList,
    GetDeviceStates,
    TestDeviceConnection,
    WriteTag
}

public record IpcRequest(string RequestId, CommandType Type, string? PayloadJson);

public record IpcResponse(string RequestId, bool Ok, string? PayloadJson, string? Error)
{
    public static IpcResponse Success(string requestId, string? payloadJson = null) =>
        new(requestId, true, payloadJson, null);

    public static IpcResponse Failure(string requestId, string error) =>
        new(requestId, false, null, error);
}

/// <summary>Server → client, không cần request tương ứng.</summary>
public record IpcPush(string Channel, string PayloadJson);

// ── Payloads ─────────────────────────────────────────────────────────────────────

public record HandshakeRequest(string StudioVersion, int ProtocolVersion);

public record HandshakeResponse(string RuntimeVersion, int ProtocolVersion, RuntimeState State);

public record StatusResponse(
    RuntimeState State,
    long CycleCount,
    double LastScanMs,
    double MaxScanMs,
    double JitterMs,
    string? FaultMessage);

public record DeployRequest(
    byte[] AssemblyBytes,
    List<TagRoute> TagRoutes,
    List<DeviceSpec> Devices,
    SwapMode Mode = SwapMode.ColdRestart);

/// <summary>ADR-004 — v1 chỉ hỗ trợ <see cref="ColdRestart"/>.</summary>
public enum SwapMode { ColdRestart, HotReload }

public record DeployResponse(bool Ok, string? Error, string? ErrorCode = null);

public static class ErrorCodes
{
    public const string HotReloadUnsupported = "E_HOTRELOAD_UNSUPPORTED";
    public const string ProtocolMismatch = "E_PROTOCOL_MISMATCH";
    public const string NoProgramEntryPoint = "E_NO_PROGRAM_ENTRYPOINT";
    public const string AssemblyLoadFailed = "E_ASSEMBLY_LOAD_FAILED";
    public const string UnknownDriver = "E_UNKNOWN_DRIVER";
    public const string AlreadyConnected = "E_ALREADY_CONNECTED";
}

public record SubscribeTagsRequest(List<string> TagNames);

public record TagValueUpdate(string TagName, string ValueJson, long TimestampMs);

public record TagValueBatch(List<TagValueUpdate> Updates);

public record DeviceStateInfo(string DriverId, string State, string? LastError);

public record DeviceStatesResponse(List<DeviceStateInfo> Devices);

/// <summary>
/// Thử kết nối một thiết bị <b>riêng lẻ</b> — Runtime dựng driver tạm từ spec, nối rồi ngắt ngay,
/// không đụng vào bộ driver đang chạy. Dùng cho nút Test Connection ở Device Configuration.
/// </summary>
public record TestConnectionRequest(DeviceSpec Device);

public record TestConnectionResponse(bool Ok, string? Error);

/// <summary>
/// Ghi MỘT lần vào tag từ Watch Table (phase-10) — khác <see cref="ForceTagRequest"/> ở chỗ
/// Force giữ giá trị liên tục, còn ghi thường logic có thể ghi đè ở chu kỳ sau.
/// </summary>
public record WriteTagRequest(string TagName, string ValueJson);

public record WriteTagResponse(bool Ok, string? Error);

public record FaultNotification(string Message, string? StackTrace, long OccurredAtMs);

// ── Force I/O (phase-11) ─────────────────────────────────────────────────────────

public record ForceTagRequest(string TagName, string ValueJson, bool Enable);

public record ForceInfo(string TagName, string ValueJson);

public record ForceListResponse(List<ForceInfo> Forces);
