# Phase 03 — Studio Runtime Client

**Status:** ✅ Done (2026-07-27) | **Phụ thuộc:** phase-02 | **Nội dung BRIEF:** #16b
**ADR:** [ADR-001](decisions/ADR-001-runtime-ipc-boundary.md)

> Phía Studio của cầu IPC. Kết thúc phase này, Studio **cắt đứt** phụ thuộc trực tiếp vào Runtime.

---

## Task 03.1 — `IRuntimeClient`

**File:** `src/DBI.Controller.Studio/Services/Runtime/IRuntimeClient.cs`

```csharp
public interface IRuntimeClient : IAsyncDisposable {
    ConnectionState State { get; }

    Task<bool> ConnectAsync(RuntimeTarget target, CancellationToken ct);
    Task DisconnectAsync();

    Task<DeployResult>  DeployAsync(byte[] assembly, IEnumerable<TagRoute> routes,
                                    IEnumerable<DeviceSpec> devices, CancellationToken ct);
    Task StartAsync();
    Task StopAsync();
    Task ResetAsync();

    Task SubscribeTagsAsync(IEnumerable<string> tagNames);
    Task ForceTagAsync(string tag, object value, bool enable);          // phase-11
    Task<IReadOnlyList<DeviceStateInfo>> GetDeviceStatesAsync();        // phase-09

    event EventHandler<StatusResponse>?   StatusUpdated;     // 500ms
    event EventHandler<TagValueUpdate>?   TagValueChanged;   // push
    event EventHandler<string>?           ConnectionLost;
}
```

Toàn bộ event **marshal về UI thread** trước khi raise (`Dispatcher.BeginInvoke`) — ViewModel không tự lo việc này.

## Task 03.2 — `NamedPipeRuntimeClient`

- Nối `NamedPipeTransport` với `IRuntimeClient`
- **Poll status 500ms** khi đã kết nối
- **Tự kết nối lại**: backoff 1s → 2s → 5s → 10s, tối đa 10s
- Mất kết nối: raise `ConnectionLost`, chuyển UI về offline, **không** xoá dữ liệu project

## Task 03.3 — `RuntimeProcessLauncher`

Studio phải khởi động được Runtime local:

| Tình huống | Xử lý |
|---|---|
| Chưa có Runtime nào chạy | `Process.Start` với `--pipe <name>`, chờ pipe sẵn sàng tối đa 10s |
| Đã có Runtime chạy sẵn | Kết nối thẳng, **không** khởi động thêm |
| Target là remote | Không tự khởi động, chỉ kết nối |
| Studio đóng | **Không** giết Runtime — máy phải tiếp tục chạy (chính là lý do của ADR-001) |

Cảnh báo người dùng khi đóng Studio lúc Runtime đang `Running`: *"Runtime vẫn đang chạy máy. Đóng Studio sẽ không dừng máy."*

## Task 03.4 — `FakeRuntimeClient` (cho test & phát triển UI)

Bản giả in-memory: deploy luôn thành công, sinh giá trị tag giả, giả lập lỗi kết nối theo yêu cầu.
Cho phép phase-04/05/06 phát triển UI **song song** mà không cần Runtime chạy thật.

Thay thế [LiveMonitoringService.cs](../../src/DBI.Controller.Studio/Services/LiveMonitoringService.cs) hiện tại (51 dòng sinh số liệu giả rời rạc).

## Task 03.5 — ✂️ Cắt phụ thuộc

**File:** [src/DBI.Controller.Studio/DBI.Controller.Studio.csproj](../../src/DBI.Controller.Studio/DBI.Controller.Studio.csproj)

```xml
<!-- XOÁ -->
<ProjectReference Include="..\DBI.Controller.Runtime\DBI.Controller.Runtime.csproj" />
<ProjectReference Include="..\..\Drivers\DBI.Controller.Driver.Simulation\...csproj" />

<!-- THÊM -->
<ProjectReference Include="..\DBI.Controller.Protocol\DBI.Controller.Protocol.csproj" />
```

Giữ `Core` + `SDK` (cần để biên dịch code người dùng) và `Diagnostics`.

**Đây là DoD quan trọng nhất của phase.** Nếu Studio vẫn build được khi đã xoá reference tới Runtime, ranh giới kiến trúc là thật.

---

## ⚠️ Lệch so với plan — client nằm ở `Studio.Core`, marshal bằng `SynchronizationContext`

Plan đặt client ở `src/DBI.Controller.Studio/Services/Runtime/`. Nhưng project đó là `net10.0-windows`, test project không tham chiếu được — mà đây là phase có DoD nặng về hành vi (nối lại, mất kết nối giữa lúc deploy, marshal event). Đặt ở `DBI.Controller.Studio.Core`.

Kéo theo: không dùng được `Dispatcher.BeginInvoke`. Thay bằng **bắt `SynchronizationContext.Current` lúc khởi tạo client** rồi `Post` mọi event về đó. Ở Studio, client được tạo trên UI thread nên `SynchronizationContext.Current` chính là `DispatcherSynchronizationContext` — kết quả y hệt, mà lại test được và không kéo WPF vào tầng lõi.

Test dùng một `SynchronizationContext` giả có thread pump riêng, ghi lại thread nào thật sự chạy callback.

---

## Definition of Done

- [x] `IRuntimeClient` + `NamedPipeRuntimeClient` hoạt động end-to-end với Runtime của phase-02 (deploy, start/stop, poll status, nhận push tag, đọc trạng thái thiết bị)
- [x] **`DBI.Controller.Studio.csproj` KHÔNG còn `ProjectReference` tới `DBI.Controller.Runtime`** — cũng bỏ luôn `Driver.Simulation`. Solution build xanh. Có test đọc thẳng tệp `.csproj` canh
- [x] Không còn `using DBI.Controller.Runtime.*` nào trong Studio; `Studio.Core` cũng không tham chiếu Runtime (test đọc `GetReferencedAssemblies`)
- [x] Tự kết nối lại hoạt động: giết Runtime → client sang `Reconnecting` → Runtime lên lại đúng pipe → client tự nối lại. Backoff 1s → 2s → 5s → 10s
- [x] `RuntimeProcessLauncher` không khởi động trùng khi đã có Runtime chạy; target ở máy khác thì **không bao giờ** tự khởi động
- [x] Đóng Studio **không** giết Runtime — có test khẳng định lớp launcher không hề có method Kill/Terminate/Shutdown; hằng `ClosingWhileRunningWarning` cho cảnh báo
- [x] Mọi event của client raise trên UI thread — test bằng `SynchronizationContext` giả có thread riêng, đối chiếu `ManagedThreadId`
- [x] `FakeRuntimeClient` đủ dùng để chạy Studio không cần Runtime: deploy, subscribe, giả lập fault, giả lập mất kết nối, Stop xả output về 0
- [x] Test: mất kết nối giữa lúc deploy → trả `DeployResult` báo lỗi rõ ràng, không treo
- [x] `LiveMonitoringService` cũ đã bị xoá — có test canh không quay lại
- [x] `dotnet test`: **187/187 pass**, build 0 warning / 0 error
