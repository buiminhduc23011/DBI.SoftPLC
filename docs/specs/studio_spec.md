# Spec: DBI.Studio — TIA Portal-style IDE cho C# Soft PLC

**Version:** 1.0 | **Date:** 2026-07-26 | **Status:** Draft
**Plan:** [plans/260726-2316-dbi-studio-tia-ide/plan.md](../../plans/260726-2316-dbi-studio-tia-ide/plan.md)
**Brief:** [BRIEF-STUDIO.md](../BRIEF-STUDIO.md)

---

## 1. Executive Summary

DBI.Studio là công cụ engineering cho nền tảng DBI.SoftPLC — giữ nguyên mô hình tư duy của Siemens TIA Portal (cây project, tag table, khối chương trình, chế độ giám sát) nhưng thay ngôn ngữ IEC 61131-3 bằng **C# thuần có IntelliSense**.

Hiện trạng: Studio là UI mockup 3 tab với dữ liệu hardcode, không có khái niệm project. Kế hoạch này xây lại từ nền, chia 14 phase, ranh giới MVP ở phase-07.

**Ba quyết định kiến trúc định hình toàn bộ:**

| # | Quyết định | Hệ quả lớn nhất |
|---|---|---|
| [ADR-001](../../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-001-runtime-ipc-boundary.md) | Runtime chạy tiến trình riêng, IPC | Scan cycle không bị GC của UI làm giật; Studio tắt máy vẫn chạy |
| [ADR-002](../../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-002-tag-source-of-truth.md) | Tag Table sinh `IO.g.cs` | IntelliSense khả thi; sai tên tag = lỗi compile |
| [ADR-003](../../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-003-shell-avalondock.md) | AvalonDock 4 vùng | Xem code + tag đồng thời, đúng cảm giác TIA |

---

## 2. User Stories

| ID | As a... | I want to... | So that... | Priority | Phase |
|---|---|---|---|---|---|
| US-01 | Kỹ sư PLC | Thấy cây project quen thuộc | Không học lại quy trình engineering | 🚀 | 05 |
| US-02 | Kỹ sư PLC | Khai báo tag trong bảng trước khi dùng | Đổi địa chỉ không phải sửa logic | 🚀 | 06 |
| US-03 | Kỹ sư C# | Gõ `IO.` được gợi ý tag | Không sai chính tả, làm nhanh | 🎁 | 08 |
| US-04 | Kỹ sư C# | Chia logic thành nhiều file khối | Code dài vẫn quản lý được | 🚀 | 05, 07 |
| US-05 | Kỹ sư vận hành | Tắt Studio mà máy vẫn chạy | Bảo trì Studio không dừng sản xuất | 🚀 | 02, 03 |
| US-06 | Kỹ sư vận hành | Xem giá trị tag real-time | Debug không cần dừng máy | 🎁 | 10 |
| US-07 | Kỹ sư vận hành | Cưỡng bức giá trị I/O | Test cơ cấu khi chưa có tín hiệu thật | 🎁 | 11 |
| US-08 | Kỹ sư bảo trì | Thấy giá trị ngay cạnh dòng code | Hiểu logic đang chạy tới đâu | 🎁 | 13 |
| US-09 | System Integrator | Deploy lên Runtime máy khác | Studio ở laptop, Runtime ở tủ điện | 🎁 | 03 |
| US-10 | Kỹ sư | Kéo device tag sang Tag Table | Map nhanh, không gõ tay địa chỉ | 🎁 | 12 |

---

## 3. IPC Contract (Studio ↔ Runtime)

**Transport:** NamedPipe · **Framing:** `[4-byte BE length][UTF-8 JSON]` · **Protocol version:** 1

| Command | Request | Response | Auth | Phase |
|---|---|---|---|---|
| `Handshake` | `{studioVersion, protocolVersion}` | `{runtimeVersion, protocolVersion, state}` | — | 02 |
| `GetStatus` | — | `{state, cycleCount, lastScanMs, maxScanMs, jitterMs, faultMessage?}` | — | 02 |
| `Deploy` | `{assemblyBytes, tagRoutes[], devices[]}` | `{ok, error?}` | — | 02, 07 |
| `Start` | — | `{ok, error?}` | — | 02 |
| `Stop` | — | `{ok, error?}` | — | 02 |
| `Reset` | — | `{ok, error?}` | — | 02 |
| `SubscribeTags` | `{tagNames[]}` | `{ok}` → push stream | — | 02, 10 |
| `UnsubscribeTags` | `{tagNames[]}` | `{ok}` | — | 02, 10 |
| `ForceTag` | `{tag, value, enable}` | `{ok}` | — | 11 |
| `GetForceList` | — | `{forces[]}` | — | 11 |
| `GetDeviceStates` | — | `{[{driverId, state, lastError?}]}` | — | 09 |

**Push channel:** `TagValueUpdate {tagName, valueJson, timestampMs}` — 100ms, chỉ tag đã subscribe **và** có thay đổi.

> Auth: v1 chỉ hỗ trợ NamedPipe local, dựa vào ACL của Windows. Remote (TCP/gRPC) sẽ cần cơ chế xác thực — ngoài phạm vi v1.0.

---

## 4. Data Model

### `.dbiproj` (JSON)

```json
{
  "schemaVersion": "1.0",
  "name": "MyMachine",
  "description": "Băng tải phân loại",
  "runtime": {
    "transportType": "NamedPipe",
    "pipeName": "DBI.Runtime",
    "scanIntervalMs": 20
  },
  "devices": [
    { "name": "FactoryIO_3D", "driverType": "DBI.Controller.Driver.FactoryIO",
      "settings": { "host": "127.0.0.1", "port": "502" } }
  ],
  "tagTables": [
    { "name": "Default Tag Table", "tags": [
      { "name": "StartButton", "dataType": "Bool", "direction": "Input",
        "device": "FactoryIO_3D", "address": "Input_0", "comment": "Nút start tủ điện" }
    ]}
  ],
  "blocks": [
    { "name": "Main",     "kind": "Main",          "fileName": "Blocks/Main.cs" },
    { "name": "Conveyor", "kind": "FunctionBlock", "fileName": "Blocks/Conveyor.cs" }
  ]
}
```

### Enum

| Enum | Giá trị |
|---|---|
| `TagDataType` | `Bool` · `Int` · `Real` |
| `TagDirection` | `Input` (read-only) · `Output` (rw) · `Memory` (rw, không thuộc device) |
| `BlockKind` | `Main` · `FunctionBlock` · `Function` · `DataBlock` |
| `RuntimeState` | `Stopped` · `Running` · `Faulted` · `NoProgram` |

### Quy tắc sinh `IO.g.cs`

| Direction | Accessor | DataType | Method |
|---|---|---|---|
| `Input` | chỉ `get` | `Bool` | `GetBool` / `SetBool` |
| `Output` | `get` + `set` | `Int` | `GetInt` / `SetInt` |
| `Memory` | `get` + `set` | `Real` | `GetFloat` / `SetFloat` |

---

## 5. Tech Stack

| Lớp | Công nghệ | Trạng thái |
|---|---|---|
| UI Shell | WPF `net10.0-windows` + `Dirkster.AvalonDock` | ➕ |
| MVVM | `CommunityToolkit.Mvvm` | ➕ |
| Editor | `AvalonEdit` 6.3.1 | ✅ |
| IntelliSense | `RoslynPad.Editor.Windows` | ⚠️ spike |
| Compiler | `Microsoft.CodeAnalysis.CSharp` 5.6.0 | ✅ |
| IPC | `System.IO.Pipes` + `System.Text.Json` | ➕ |
| Runtime host | `net8.0` console exe | ✅ (hiện là stub) |
| Test | xUnit | ✅ |

**Ràng buộc framework:** assembly người dùng phải target `net8.0` để Runtime (`net8.0`) nạp được. Studio (`net10.0-windows`) chỉ *biên dịch*, không *thực thi* nó.

---

## 6. Build Checklist

| # | Hạng mục | Phase | ✓ |
|---|---|---|---|
| 1 | Gỡ `DynamicObject` khỏi `IOContainer` | 00 | ⬜ |
| 2 | Sửa `IMemoryImage` cho driver ghi input | 00 | ⬜ |
| 3 | Double-buffer Int/Float | 00 | ⬜ |
| 4 | Spike RoslynPad + AvalonDock | 00 | ⬜ |
| 5 | Project model + `.dbiproj` | 01 | ⬜ |
| 6 | `DBI.Controller.Protocol` | 02 | ⬜ |
| 7 | Runtime host thật (gọi `Start()`) | 02 | ⬜ |
| 8 | `TagRoutingTable` | 02 | ⬜ |
| 9 | `IRuntimeClient` + cắt ProjectReference | 03 | ⬜ |
| 10 | AvalonDock shell + tách ViewModel + theme ResourceDictionary | 04 | ⬜ |
| 11 | Project Tree + block templates | 05 | ⬜ |
| 12 | Tag Table + `IoCodeGenerator` | 06 | ⬜ |
| 13 | Multi-file compile + deploy | 07 | 🏁 MVP |
| 14 | IntelliSense + Error List | 08 | ⬜ |
| 15 | Device Config CRUD | 09 | ⬜ |
| 16 | Watch Table | 10 | ⬜ |
| 17 | Force I/O + chỉ báo an toàn | 11 | ⬜ |
| 18 | Task Cards + drag-drop | 12 | ⬜ |
| 19 | Live overlay + E2E Factory I/O | 13 | ⬜ |

---

## 7. Out of Scope (v1.0)

Portal View · Cross-reference · Compare online/offline · Import TIA/CSV · Undo/Redo cấp project · Multi-PLC · Multi-language UI · Source Generator · Ladder/FBD/SCL editor

---

## 8. Acceptance Criteria

**MVP (hết phase-07):**
> Người dùng mới, chưa đọc tài liệu, tạo được project băng tải chạy trên Factory I/O **trong 10 phút**.

**v1.0 (hết phase-13):**
> Kịch bản 14 bước ở [phase-13](../../plans/260726-2316-dbi-studio-tia-ide/phase-13-live-overlay.md) chạy trọn vẹn trên Factory I/O thật — bao gồm bước 13: *đóng Studio, máy vẫn chạy*.
