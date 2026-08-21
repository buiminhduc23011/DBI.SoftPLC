# Plan: DBI.Studio — TIA Portal-style IDE cho C# Soft PLC

**Created:** 2026-07-26 23:16 | **Version:** 1.0 | **Status:** 🟡 In Progress
**Nguồn:** [BRIEF-STUDIO.md](../../docs/BRIEF-STUDIO.md) | **BRIEF cha:** [BRIEF.md](../../docs/BRIEF.md)
**Spec:** [docs/specs/studio_spec.md](../../docs/specs/studio_spec.md)

---

## PRD Summary

### What

> Studio cho phép kỹ sư tạo project, khai báo tag trong bảng, viết nhiều khối logic C#, biên dịch và nạp xuống Runtime chạy ở tiến trình riêng — với trải nghiệm engineering quen thuộc như TIA Portal.

### User Stories

| As a... | I want to... | So that... | Priority |
|---------|-------------|------------|----------|
| Kỹ sư PLC | Thấy cây project quen thuộc (Program Blocks / PLC Tags / Devices) | Không phải học lại quy trình engineering | 🚀 MVP |
| Kỹ sư PLC | Khai báo tag trong bảng rồi mới dùng trong code | Đổi địa chỉ phần cứng không phải sửa logic | 🚀 MVP |
| Kỹ sư C# | Gõ `IO.` và được gợi ý đúng danh sách tag đã khai báo | Không sai chính tả tag, làm nhanh | 🎁 P2 |
| Kỹ sư C# | Chia logic thành nhiều file khối riêng | Code dài vẫn quản lý được, Git diff đọc được | 🚀 MVP |
| Kỹ sư vận hành | Tắt Studio mà máy vẫn chạy | Bảo trì Studio không dừng sản xuất | 🚀 MVP |
| Kỹ sư vận hành | Xem giá trị tag real-time trong bảng Watch | Debug được mà không cần dừng máy | 🎁 P2 |
| Kỹ sư vận hành | Cưỡng bức (force) giá trị I/O | Test cơ cấu chấp hành khi chưa có tín hiệu thật | 🎁 P2 |
| Kỹ sư bảo trì | Nhìn thấy giá trị biến ngay cạnh dòng code | Hiểu logic đang chạy tới đâu | 🎁 P2 |
| System Integrator | Deploy được lên Runtime chạy trên máy khác | Studio ở laptop, Runtime ở edge device tủ điện | 🎁 P2 |

### Constraints

| # | Ràng buộc | Nguồn |
|---|---|---|
| C-1 | Scan cycle mục tiêu **20ms**, jitter phải nhỏ. Studio **không được** làm ảnh hưởng determinism | [BRIEF.md §7](../../docs/BRIEF.md) |
| C-2 | Runtime chạy **tiến trình riêng** — quyết định đã chốt, xem [ADR-001](decisions/ADR-001-runtime-ipc-boundary.md) | User |
| C-3 | Tag Table là **nguồn sự thật duy nhất**, code sinh ra từ bảng, xem [ADR-002](decisions/ADR-002-tag-source-of-truth.md) | User |
| C-4 | Tag trỏ **trực tiếp** tới `(Device, Address)`, không có tầng `%I0.0` trung gian | User |
| C-5 | Protocol driver **bắt buộc** dùng core client từ `DBI.Drivers`, không tự viết | [BRIEF.md §5](../../docs/BRIEF.md) |
| C-6 | Studio: WPF `net10.0-windows`. Mọi project còn lại: `net10.0` | `Directory.Build.props` |
| C-7 | Project trên đĩa phải **Git-friendly**: `.cs` là file thật, config là JSON | [BRIEF-STUDIO.md §4.4](../../docs/BRIEF-STUDIO.md) |

### Out of Scope (v1.0)

- ❌ Portal View (màn hình khởi động dạng thẻ lớn của TIA)
- ❌ Cross-reference "tag này dùng ở đâu"
- ❌ Compare online/offline
- ❌ Import từ file dự án TIA / CSV
- ❌ Undo/Redo cấp project
- ❌ Multi-PLC trong một project (chỉ 1 Runtime target)
- ❌ Multi-language UI
- ❌ Source Generator thay codegen thủ công
- ❌ Ladder / FBD / SCL editor — **chỉ C#**

---

## 🚨 Phát hiện nền tảng (phải sửa trước, không thương lượng)

Đọc code Runtime phát hiện **5 vấn đề chặn**. Tất cả nằm ở `phase-00` và `phase-02`.

| # | Vấn đề | File | Hệ quả |
|---|---|---|---|
| **B-1** | `IOContainer : DynamicObject`, `TryGetMember` **luôn** gọi `GetBool` | [IOContainer.cs:33-37](../../src/DBI.Controller.SDK/IO/IOContainer.cs) | IntelliSense bất khả thi; sai chính tả tag không bị bắt; đọc tag Real trả về `bool` |
| **B-2** ⚠️ | `SetRawInputBool` chỉ có trên `MemorySnapshot`, **không có** trên `IMemoryImage` → cả 5 driver phải **ép kiểu xuống**, và **im lặng không làm gì** khi ép kiểu hỏng | [IMemoryImage.cs](../../src/DBI.Controller.Core/Interfaces/IMemoryImage.cs) vs [FactoryIODriver.cs:55-56](../../Drivers/DBI.Controller.Driver.FactoryIO/FactoryIODriver.cs) | Hôm nay **vẫn chạy đúng**. Nhưng bất kỳ `IMemoryImage` nào khác `MemorySnapshot` (ví dụ decorator Force của phase-11) sẽ làm **toàn bộ driver ngừng hoạt động trong im lặng** — không exception, không log, máy đứng yên |
| **B-6** 🆕 | Cả 5 driver **chỉ hỗ trợ `bool`** — 0 lần dùng Int/Float | `Drivers/*/` (đã grep xác nhận) | Tag Table (phase-06) cho khai `Int`/`Real`, nhưng không driver nào đọc/ghi được → tag `Real` **luôn trả 0 trong im lặng** |
| **B-3** | `_intValues` / `_floatValues` **không** double-buffer | [MemorySnapshot.cs:17-18](../../src/DBI.Controller.Core/Models/MemorySnapshot.cs) | Tag `Int`/`Real` bỏ qua cơ chế snapshot → race giữa driver thread và logic thread |
| **B-4** | `Program.Main` **không bao giờ** gọi `SetProgram()` hay `engine.Start()` | [Program.cs:24-32](../../src/DBI.Controller.Runtime/Program.cs) | Runtime host là stub, chưa từng chạy scan cycle nào |
| **B-5** | Không có bảng định tuyến tag→device. `DriverManager` đưa cả memory image cho **mọi** driver | [DriverManager.cs:25-31](../../src/DBI.Controller.Runtime/Drivers/DriverManager.cs) | Tag Table (Device+Address) không có chỗ để cắm vào |

> ⚠️ **Về B-2 — đọc kỹ để khỏi đi tìm nhầm bug.** Driver **không** gọi `SetBool` nhầm chỗ. Chúng ép kiểu `memoryImage is not MemorySnapshot snapshot` rồi gọi đúng `SetRawInputBool`. Vấn đề là **nhánh thất bại**:
>
> ```csharp
> if (State != Connected || _modbusMaster == null || memoryImage is not MemorySnapshot snapshot)
>     return Task.CompletedTask;      // ⚠️ im lặng bỏ qua — không lỗi, không log
> ```
>
> Đây là **quả mìn hẹn giờ cho phase-11**: nếu Force layer làm dạng decorator bọc `IMemoryImage`, mọi driver sẽ ngừng đọc/ghi mà không báo gì.

Phụ (không chặn, sửa tiện tay ở phase-00):
- `SwapInputBuffers` copy từng entry O(n) mỗi scan — không "lock-free swap" như comment mô tả
- `ScanEngine` dùng `Thread.Sleep((int))` — cắt cụt phần thập phân, gây jitter
- `Metrics.JitterMs = |elapsed - interval|` — công thức sai; jitter là **độ lệch chu kỳ**, không phải thời gian thực thi

---

## ⚙️ Điều kiện tiên quyết môi trường

**Kiểm tra trước khi bắt đầu bất kỳ phase nào.**

### Repo anh em `DBI.Drivers` — bắt buộc có

4 driver project tham chiếu ra **ngoài** repo này:

```xml
<!-- Drivers/DBI.Controller.Driver.Modbus/*.csproj -->
<ProjectReference Include="..\..\..\DBI.Drivers\DBI.Drivers.Modbus\DBI.Drivers.Modbus.csproj" />
```

| Driver | Cần |
|---|---|
| Modbus, FactoryIO | `DBI.Drivers.Modbus` |
| Delta | `DBI.Drivers.Delta.PLC` |
| Omron | `DBI.Drivers.Omron` |
| Simulation | — (không cần) |

Bố cục thư mục bắt buộc:

```
GitHub/
├── DBI.SoftPLC/     ← repo này
└── DBI.Drivers/     ← repo anh em, PHẢI clone cạnh
```

Thiếu → `dotnet restore` **fail ngay**, không phải lỗi code.

```powershell
# kiểm tra nhanh
Test-Path ..\DBI.Drivers\DBI.Drivers.Modbus\DBI.Drivers.Modbus.csproj
```

### 📦 Đóng gói: driver DLL đi kèm **Runtime**, không phải Studio

Sau [ADR-001](decisions/ADR-001-runtime-ipc-boundary.md), **Runtime mới là bên nạp driver** — Studio chỉ gửi `DeviceSpec` qua IPC. Khi đóng gói:

| Gói | Chứa |
|---|---|
| **DBI.Runtime** | `DBI.Controller.Driver.*.dll` + `DBI.Drivers.*.dll` |
| **DBI.Studio** | ❌ **không** cần driver DLL |

Đây là điểm dễ nhầm: trước ADR-001 Studio `ProjectReference` thẳng vào `Driver.Simulation`, sau phase-03 thì bỏ.

### Baseline đã xác nhận (2026-07-27)

```
dotnet build DBI.Controller.slnx  →  0 Warning, 0 Error  ✅
```

---

## IPC Contract (Studio ↔ Runtime)

**Transport:** NamedPipe (local) / gRPC over TCP (remote — phase-13+)
**Serialization:** `System.Text.Json` (v1), có thể đổi sang MessagePack nếu monitoring quá tải
**Chi tiết:** [ADR-001](decisions/ADR-001-runtime-ipc-boundary.md)

| Command | Request | Response | Ghi chú |
|---|---|---|---|
| `Handshake` | `{ studioVersion, protocolVersion }` | `{ runtimeVersion, protocolVersion, state }` | Từ chối nếu protocol lệch major |
| `GetStatus` | — | `{ state, cycleCount, lastScanMs, maxScanMs, jitterMs, faultMessage? }` | Poll 500ms |
| `Deploy` | `{ assemblyBytes, tagMapJson, deviceConfigJson }` | `{ ok, error? }` | Runtime tự `Stop → Load → Start` |
| `Start` / `Stop` / `Reset` | — | `{ ok, error? }` | `Reset` xóa fault của SafetyCatch |
| `SubscribeTags` | `{ tagNames[] }` | stream `{ tag, value, ts }` | Push 100ms, chỉ gửi tag đã đăng ký |
| `ForceTag` | `{ tag, value, enable }` | `{ ok }` | phase-11 |
| `GetForceList` | — | `{ forces[] }` | phase-11 |
| `GetDeviceStates` | — | `{ [{ driverId, state, lastError? }] }` | phase-09 |

**Nguyên tắc:** Studio **không bao giờ** giữ tham chiếu trực tiếp tới object của Runtime. Mọi thứ đi qua contract này.

⚠️ Sau phase-02, `DBI.Controller.Studio.csproj` phải **bỏ** `ProjectReference` tới `DBI.Controller.Runtime` — chỉ giữ tham chiếu tới project contract chung.

---

## Định dạng Project trên đĩa

```
MyMachine/
├── MyMachine.dbiproj        ← JSON (xem schema ở phase-01)
├── Blocks/
│   ├── Main.cs
│   ├── Conveyor.cs
│   └── RecipeData.cs
├── Generated/
│   └── IO.g.cs              ← sinh tự động, có header cảnh báo
└── .dbistudio/
    └── layout.xml           ← layout AvalonDock (không commit)
```

---

## Tech Stack

| Lớp | Công nghệ | Trạng thái |
|---|---|---|
| UI Shell | WPF + `Dirkster.AvalonDock` | ➕ thêm mới |
| MVVM | `CommunityToolkit.Mvvm` | ➕ thêm mới |
| Code Editor | `AvalonEdit` 6.3.1 | ✅ có sẵn |
| IntelliSense | `RoslynPad.Editor.Windows` 5.0.0 | ✅ **spike PASS 10/10** |
| Compiler | `Microsoft.CodeAnalysis.CSharp` **5.3.0** | ⚠️ **hạ từ 5.6.0** — xem [spike](reports/spike-roslynpad.md) |
| IPC | `System.IO.Pipes` + `System.Text.Json` | ➕ thêm mới |
| Project file | `System.Text.Json` | ✅ built-in |
| Test | xUnit | ✅ có sẵn |

---

## Phases

**🏁 Ranh giới MVP = hết phase-07.** Dừng ở đó vẫn có engineering tool dùng được thật.

| Phase | Tên | Nội dung BRIEF | Status | Progress |
|-------|-----|---|--------|----------|
| 00 | [Spike & Foundation Fixes](phase-00-spike-foundation.md) | #0 + B-1→B-3 | ✅ Done | **100%** |
| 01 | [Project Model & Persistence](phase-01-project-model.md) | #1 | ✅ Done | **100%** |
| 02 | [IPC Contract & Runtime Host](phase-02-ipc-runtime-host.md) | #16a + B-4, B-5 | ✅ Done | **100%** |
| 03 | [Studio Runtime Client](phase-03-studio-runtime-client.md) | #16b | ✅ Done | **100%** |
| 04 | [AvalonDock Shell & MVVM](phase-04-shell-mvvm.md) | #6, #7, #8 | ✅ Done | **100%** |
| 05 | [Project Tree & Block Templates](phase-05-project-tree.md) | #2 | ✅ Done | **100%** |
| 06 | [Tag Table & Code Generator](phase-06-tag-table-codegen.md) | #4, #5 | ✅ Done | **100%** |
| 07 | [Multi-file Compile & Deploy](phase-07-compile-deploy.md) 🏁 | #3 | ✅ Done | 100% |
| 08 | [Roslyn IntelliSense & Error List](phase-08-intellisense.md) | #9, #10 | ✅ Done | 100% |
| 09 | [Device Config & Tag Routing](phase-09-device-config.md) | #17 | ✅ Done | 100% |
| 10 | [Watch Table & Live Monitoring](phase-10-watch-table.md) | #14 | ✅ Done (chưa nghiệm thu tải) | **95%** |
| 11 | [Force I/O](phase-11-force-io.md) | #15 | ✅ Done (chưa nghiệm thu phần cứng) | **95%** |
| 12 | [Task Cards & Drag-drop Mapping](phase-12-taskcards-dragdrop.md) | #11, #12 | 🟡 Cards + click-chèn xong; drag-drop chưa làm | 75% |
| 13 | [Live Code Overlay & Integration](phase-13-live-overlay.md) | #13 | 🟡 Overlay + margin xong; nghiệm thu hiệu năng chưa chạy | 80% |

**Đường găng (critical path):** `00 → 01 → 02 → 03 → 07`. Phase 04/05/06 chạy song song được với 02/03 nếu có 2 người.

---

## Architecture Decisions

| ADR | Tiêu đề | Status |
|---|---|---|
| [ADR-001](decisions/ADR-001-runtime-ipc-boundary.md) | Runtime chạy tiến trình riêng, giao tiếp qua IPC | ✅ Accepted |
| [ADR-002](decisions/ADR-002-tag-source-of-truth.md) | Tag Table là nguồn sự thật, sinh `IO.g.cs` | ✅ Accepted |
| [ADR-003](decisions/ADR-003-shell-avalondock.md) | AvalonDock 4 vùng thay TabControl | ✅ Accepted |
| [ADR-004](decisions/ADR-004-deploy-cold-restart.md) | Deploy dùng Cold Restart, để đường cho Hot Reload | ✅ Accepted |
| [ADR-005](decisions/ADR-005-runtime-autostart.md) | ⚠️ Runtime tự chạy lại sau restart — **có hệ quả an toàn** | ✅ Accepted |

**Thiết kế chi tiết:** [docs/DESIGN-STUDIO.md](../../docs/DESIGN-STUDIO.md) — sơ đồ tuần tự, máy trạng thái, 40 test case.

---

## Rủi ro & phương án lùi

| Rủi ro | Mức | Phương án lùi |
|---|---|---|
| ~~`RoslynPad.Editor` xung đột net10.0 / Roslyn 5.6.0~~ | ✅ **ĐÃ GỠ** | [Spike 2026-07-27](reports/spike-roslynpad.md): 10/10 PASS. Điều kiện: ghim Roslyn **5.3.0** |
| ~~AvalonDock theme đè phong cách industrial phẳng~~ | ✅ **ĐÃ GỠ** | [Spike 2026-07-27](reports/spike-avalondock.md): 7/7 PASS, theme được cả Light lẫn Dark |
| Monitoring qua IPC trễ / tốn băng thông | 🟡 | Chỉ push tag đã `SubscribeTags`; hạ tần suất 100ms→250ms; đổi JSON→MessagePack |
| Live overlay AvalonEdit quá khó | 🟡 | phase-10 (Watch Table) đã cho 80% giá trị debug — phase-13 có thể cắt |
| ~~Cross-targeting net8.0 ↔ net10.0 khi load assembly~~ | ✅ **KHÔNG XẢY RA** | phase-02 phát hiện `Directory.Build.props` khai `net8.0` nhưng **mọi project đều đè thành `net10.0`** — tài liệu nói một đằng, code chạy một nẻo. Đã sửa props cho khớp thực tế. Không có cross-targeting nào cả |
| **MỚI:** `RoslynCodeEditor` tạo project cô lập 1 file → IntelliSense sai âm thầm | 🟡 | Phải nối editor vào workspace của project — [spike §Phát hiện 4](reports/spike-roslynpad.md). Có task riêng ở phase-08 |

---

## Next

1. `/awf-design` — thiết kế chi tiết class diagram + JSON schema (khuyến nghị)
2. `/awf-visualize` — dựng mockup UI trước
3. `/awf-code phase-00` — code luôn
