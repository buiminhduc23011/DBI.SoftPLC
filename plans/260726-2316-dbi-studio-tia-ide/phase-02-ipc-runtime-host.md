# Phase 02 — IPC Contract & Runtime Host

**Status:** ✅ Done (2026-07-27) | **Ưu tiên:** 🔴 Đường găng | **Phụ thuộc:** phase-00 | **Nội dung BRIEF:** #16a + B-4, B-5
**ADR:** [ADR-001](decisions/ADR-001-runtime-ipc-boundary.md)

> Đây là phase nặng nhất và không có kết quả nhìn thấy được trên UI. `DBI.Controller.Runtime` hiện là **stub** — `Program.Main` tạo `ScanEngine` rồi... chờ Enter. Không gọi `SetProgram()`, không gọi `Start()`. Phase này biến nó thành host thật.

---

## Mục tiêu

1. Tạo project contract `DBI.Controller.Protocol` dùng chung
2. Biến Runtime thành host thật: nạp assembly, chạy scan, phục vụ IPC
3. Thêm bảng định tuyến tag→device (B-5)

---

## Task 02.1 — Project mới `src/DBI.Controller.Protocol`

`net8.0`, **không** phụ thuộc vào Runtime hay Studio. Cả hai bên cùng tham chiếu.

```csharp
public enum RuntimeState { Stopped, Running, Faulted, NoProgram }

public enum CommandType {
    Handshake, GetStatus, Deploy, Start, Stop, Reset,
    SubscribeTags, UnsubscribeTags, ForceTag, GetForceList, GetDeviceStates
}

public record IpcRequest(string  RequestId, CommandType Type, string? PayloadJson);
public record IpcResponse(string RequestId, bool Ok, string? PayloadJson, string? Error);
public record IpcPush(string Channel, string PayloadJson);   // server → client, không cần request

// Payloads
public record HandshakeRequest(string StudioVersion, int ProtocolVersion);
public record HandshakeResponse(string RuntimeVersion, int ProtocolVersion, RuntimeState State);

public record StatusResponse(
    RuntimeState State, long CycleCount,
    double LastScanMs, double MaxScanMs, double JitterMs,
    string? FaultMessage);

public record DeployRequest(
    byte[] AssemblyBytes,
    List<TagRoute> TagRoutes,
    List<DeviceSpec> Devices);

public record TagRoute(string TagName, string DataType, string Direction, string Device, string Address);
public record DeviceSpec(string Name, string DriverType, Dictionary<string,string> Settings);

public record TagValueUpdate(string TagName, string ValueJson, long TimestampMs);
public record DeviceStateInfo(string DriverId, string State, string? LastError);
```

**Protocol version = 1.** Handshake từ chối nếu major lệch.

## Task 02.2 — Khung transport

```csharp
public interface IIpcTransport : IAsyncDisposable {
    Task<IpcResponse> SendAsync(IpcRequest req, CancellationToken ct);
    IAsyncEnumerable<IpcPush> ReceivePushAsync(CancellationToken ct);
    bool IsConnected { get; }
    event EventHandler<string>? Disconnected;
}
```

**v1:** `NamedPipeTransport` — `NamedPipeServerStream` / `NamedPipeClientStream`.
Framing: `[4-byte big-endian length][UTF-8 JSON]`. **Không dùng `StreamReader.ReadLine`** — payload chứa `byte[]` base64 rất dài và có thể chứa newline.

Trừu tượng hoá sẵn để phase-13+ cắm gRPC/TCP mà không đụng lớp trên.

## Task 02.3 — `RuntimeHost` (Runtime side)

**File:** `src/DBI.Controller.Runtime/Host/RuntimeHost.cs`

Gom vòng đời hiện đang rời rạc:

```csharp
public class RuntimeHost {
    private readonly MemorySnapshot     _memory;
    private readonly DriverManager      _driverManager;
    private readonly SafetyCatchManager _safety;
    private readonly ScanEngine         _engine;
    private readonly UserProgramLoader  _loader;

    public RuntimeState State { get; private set; } = RuntimeState.NoProgram;

    public Task<bool> DeployAsync(DeployRequest req);  // Stop → Unload → ghi dll tmp → Load → cấu hình driver → Start
    public void  Start();
    public void  Stop();
    public void  Reset();                              // xoá fault của SafetyCatch
    public StatusResponse GetStatus();
}
```

**Sửa B-4:** `Program.Main` hiện **không bao giờ** gọi `SetProgram()` hay `engine.Start()`. Viết lại:

```csharp
static async Task Main(string[] args) {
    var host   = new RuntimeHost();
    var server = new IpcServer(host, pipeName: args.PipeNameOrDefault("DBI.Runtime"));
    await server.RunAsync(CancellationToken.None);   // chờ tín hiệu dừng, không chờ Console.ReadLine
}
```

Hỗ trợ tham số: `--pipe <name>`, `--port <n>`, `--headless`.

## Task 02.4 — Định tuyến tag → device (B-5)

**Đây là mắt xích còn thiếu giữa Tag Table và driver.** Hiện `DriverManager.ReadInputsAsync` đưa **cả** memory image cho **mọi** driver, mỗi driver tự đoán key nào của mình.

Thêm:

```csharp
public class TagRoutingTable {
    // device → danh sách tag nó chịu trách nhiệm
    public IReadOnlyList<TagRoute> GetRoutesForDevice(string deviceName);
    public void Load(IEnumerable<TagRoute> routes);
}
```

`DriverManager.ReadInputsAsync` chỉ đưa cho mỗi driver **phần route của nó**:

```csharp
Task ReadInputsAsync(IMemoryImage memory, IReadOnlyList<TagRoute> myRoutes, CancellationToken ct);
```

⚠️ Đây là **breaking change** trên `IDriver` ([IDriver.cs:14-15](../../src/DBI.Controller.Core/Interfaces/IDriver.cs)). Phải cập nhật cả 5 driver trong `Drivers/`. Nhớ Constraint C-5: driver chỉ được bọc core client từ `DBI.Drivers`, không tự viết giao thức.

## Task 02.5 — `IpcServer`

- Nhận 1 client tại một thời điểm (v1). Client thứ hai: từ chối kèm lý do rõ ràng.
- Dispatch `CommandType` → method của `RuntimeHost`
- **Push loop riêng** cho `SubscribeTags`: 100ms đọc `_memory.SnapshotAll()`, chỉ gửi tag đã đăng ký **và có thay đổi giá trị**
- ⚠️ **Push loop tuyệt đối không chạy trên scan thread.** Chạy trên `Task` riêng, đọc snapshot đã chốt. Đây chính là lý do tồn tại của ADR-001 — không được phá nó ngay tại đây.
- Client ngắt kết nối: giữ nguyên trạng thái máy đang chạy, chờ kết nối mới

## Task 02.6 — `IProgramSwapper` (ADR-004)

`RuntimeHost` **không** gọi `UserProgramLoader` trực tiếp — đi qua interface để sau này cắm Hot Reload vào mà không phá gì:

```csharp
public interface IProgramSwapper {
    Task<SwapResult> SwapAsync(byte[] assembly, SwapMode mode, CancellationToken ct);
}
public enum SwapMode { ColdRestart, HotReload }
```

v1 chỉ có `ColdRestartSwapper`. Nhận `SwapMode.HotReload` → trả `E_HOTRELOAD_UNSUPPORTED`.

⚠️ **Thứ tự Stop bắt buộc:** `ClearAllOutputs()` → `SwapOutputBuffers()` → `WriteOutputsAsync()` **rồi mới** unload. Phải xả output xuống **thiết bị thật** trước — nếu không, băng tải vẫn quay trong lúc nạp chương trình mới.

## Task 02.7 — Persistence & Autostart (ADR-005)

> 🚨 Task này có hệ quả an toàn. Đọc [ADR-005](decisions/ADR-005-runtime-autostart.md) trước khi code.

Deploy thành công → lưu xuống `%PROGRAMDATA%/DBI.Runtime/last-deploy/`:

```
last-deploy/
├── program.dll
├── program.sha256
└── manifest.json     { tagRoutes, devices, deployedAt, lastCleanState, autoStart }
```

`lastCleanState` ghi **mỗi khi đổi trạng thái**, không phải lúc tắt — mất điện thì không kịp ghi.

Trình tự khởi động:

```
đọc last-deploy/
 ├─ không có / sha256 sai   → NoProgram
 ├─ có file .norun          → Stopped   ⚠️ CHỐT CHẶN 2 (phanh tay bảo trì)
 ├─ lastCleanState=Faulted  → Stopped   ⚠️ CHỐT CHẶN 1 (tránh vòng lặp fault)
 └─ lastCleanState=Running  → Load → Connect → OnStart() → Start
```

## Task 02.8 — Kênh `fault` có cấu trúc

[SafetyCatchManager.cs:16-19](../../src/DBI.Controller.Runtime/Safety/SafetyCatchManager.cs) hiện chỉ `Console.WriteLine` — Studio chạy tiến trình riêng nên **không thấy gì**.

Chuyển sang phát sự kiện `FaultOccurred { message, stackTrace, occurredAt }` → `IpcServer` push kênh `fault`. Giữ log console cho chế độ headless.

Đồng thời: khi fault → **xoá toàn bộ force** (chuẩn bị sẵn cho phase-11).

## Task 02.9 — Sửa metric của `ScanEngine`

**File:** [src/DBI.Controller.Runtime/Engine/ScanEngine.cs](../../src/DBI.Controller.Runtime/Engine/ScanEngine.cs)

| Vấn đề | Sửa |
|---|---|
| `Metrics.JitterMs = Math.Abs(elapsed - ScanIntervalMs)` — đó là *thời gian thực thi lệch bao nhiêu*, không phải jitter | Jitter = độ lệch **chu kỳ thực tế** giữa hai lần bắt đầu scan liên tiếp |
| `Thread.Sleep(ScanIntervalMs - (int)elapsed)` — ép kiểu `int` cắt cụt phần thập phân | Dùng deadline tuyệt đối + `SpinWait` cho phần dư dưới 1ms |
| `Metrics` là class mutable, đọc từ thread khác → torn read | Trả snapshot bất biến (`record`) cho `GetStatus` |

---

## ⚠️ Ba điều phát hiện khi triển khai

### 1. Jitter thật là **6ms**, không phải lỗi công thức — Windows chặn ở độ phân giải timer

Sửa công thức jitter xong, đo ra vẫn ~6ms. Nguyên nhân không nằm ở code: Windows chạy timer hệ thống ở **~15.6ms**, nên `Thread.Sleep(18)` có thể ngủ tới 31ms. Chu kỳ 20ms mà ngủ 31ms là jitter 11ms.

Thêm [`HighResolutionTimerScope`](../../src/DBI.Controller.Runtime/Engine/HighResolutionTimerScope.cs) — gọi `timeBeginPeriod(1)` suốt thời gian scan chạy, hạ ngay khi dừng. Đo lại: **jitter trung bình < 1ms**. Ngoài Windows là no-op vì `nanosleep` đã đủ mịn.

### 2. `TagRoute` / `DeviceSpec` / `TagDataType` đặt ở **Core**, không phải Protocol

Plan để chúng trong `DBI.Controller.Protocol`. Nhưng `IDriver` (ở Core) cần `TagRoute` trong chữ ký, mà bắt Core tham chiếu Protocol là lộn ngược tầng. Đặt ở `Core/Models/TagRoute.cs`, Protocol tham chiếu Core. Protocol vẫn **không** biết gì về Runtime/Studio — đúng tinh thần ADR-001, có test canh.

### 3. Hợp đồng ngầm: `camelCase` là bắt buộc, không phải thẩm mỹ

Client phân biệt response với push bằng cách dò trường `requestId` trên `JsonDocument` — phép dò đó **phân biệt hoa/thường**. Lúc đầu `ProtocolJson` ghi PascalCase nên mọi response bị coi là push và **mọi lệnh treo vô hạn, không exception nào để lần ra**. Đã ghim `PropertyNamingPolicy = CamelCase` kèm hằng `ProtocolJson.RequestIdProperty` và test canh.

---

## Definition of Done

- [x] `DBI.Controller.Protocol` build được, **không** tham chiếu Runtime/Studio (có test đọc `GetReferencedAssemblies`)
- [x] `NamedPipeTransport` framing bằng length-prefix, test với payload > 1MB; kèm test payload chứa `\n` và test độ dài báo láo
- [x] Handshake từ chối protocol version lệch, kèm thông báo rõ
- [x] `Program.Main` viết lại — **thật sự** gọi `SetProgram()` và `Start()` (B-4 chết); có `--pipe`, `--port`, `--scan`, `--headless`, `--help`
- [x] Deploy end-to-end: gửi `byte[]` assembly → Runtime nạp → scan chạy → `GetStatus` trả `Running` và `CycleCount` tăng
- [x] `TagRoutingTable` hoạt động; cả 5 driver cập nhật theo chữ ký `IDriver` mới
- [x] Test: driver chỉ nhận route của chính nó, không thấy tag của driver khác — kể cả khi **hai thiết bị có tag trùng tên**
- [x] `SubscribeTags` push đúng tag, chỉ khi giá trị đổi
- [x] **Test determinism:** push loop bật + client poll status — jitter trung bình < 2ms, không chu kỳ nào > 40ms. Mặc định chạy 5s cho nhanh; đặt `DBI_DETERMINISM_SECONDS=60` để chạy đủ 60 giây như bàn giao
- [x] Client ngắt đột ngột → Runtime vẫn chạy, `CycleCount` tiếp tục tăng, nhận lại kết nối mới được
- [x] Client thứ hai bị từ chối kèm lý do rõ ràng, không treo im lặng
- [x] Deploy assembly lỗi (không có class kế thừa `ControllerProgram`) → `E_NO_PROGRAM_ENTRYPOINT`; assembly rác → `E_ASSEMBLY_LOAD_FAILED`; driver lạ → `E_UNKNOWN_DRIVER`. Runtime không crash
- [x] Jitter và scan time đo đúng công thức, có test; `ScanMetrics` là `record` bất biến (hết torn read)
- [x] `RuntimeHost` đi qua `IProgramSwapper`, không gọi `UserProgramLoader` trực tiếp
- [x] `SwapMode.HotReload` trả `E_HOTRELOAD_UNSUPPORTED` kèm thông báo rõ *(TC-D12)*
- [x] Stop xả output xuống **thiết bị thật** trước khi unload; có test khẳng định output đã về 0 **trước** lúc nạp chương trình mới *(TC-D01)*
- [x] Deploy lưu `last-deploy/` kèm sha256; checksum sai → `NoProgram` *(TC-D08)*
- [x] `lastCleanState` ghi mỗi lần đổi trạng thái, không phải lúc tắt
- [x] ⚠️ **Chốt chặn 1:** `lastCleanState = Faulted` → khởi động ở `Stopped` *(TC-D05)*
- [x] ⚠️ **Chốt chặn 2:** có file `.norun` → khởi động ở `Stopped` *(TC-D06)*
- [x] `lastCleanState = Running` + không `.norun` + checksum đúng → tự chạy *(TC-D07)*
- [x] Fault push kênh `fault` có cấu trúc lên Studio, không chỉ `Console.WriteLine`
- [x] Fault xoá force **trước** khi xả output (xả xong mà force còn thì force kéo output lên lại) *(TC-D03)*
- [x] `dotnet test` toàn bộ pass — **162/162**, build 0 warning / 0 error
