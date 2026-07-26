# Phase 03 — Studio Runtime Client

**Status:** ⬜ Pending | **Phụ thuộc:** phase-02 | **Nội dung BRIEF:** #16b
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

## Definition of Done

- [ ] `IRuntimeClient` + `NamedPipeRuntimeClient` hoạt động end-to-end với Runtime của phase-02
- [ ] **`DBI.Controller.Studio.csproj` KHÔNG còn `ProjectReference` tới `DBI.Controller.Runtime`** — solution vẫn build xanh
- [ ] Không còn `using DBI.Controller.Runtime.*` nào trong Studio
- [ ] Tự kết nối lại hoạt động: kill Runtime → Studio báo offline → khởi động lại Runtime → Studio tự nối lại
- [ ] `RuntimeProcessLauncher` không khởi động trùng khi đã có Runtime chạy
- [ ] Đóng Studio **không** giết Runtime; có cảnh báo khi Runtime đang `Running`
- [ ] Mọi event của client raise trên UI thread (test bằng `Dispatcher.CheckAccess`)
- [ ] `FakeRuntimeClient` đủ dùng để chạy Studio không cần Runtime
- [ ] Test: mất kết nối giữa lúc deploy → không treo UI, báo lỗi rõ ràng
- [ ] `LiveMonitoringService` cũ đã bị xoá
