# 💡 BRIEF: DBI.Studio — TIA Portal-style IDE cho C# Soft PLC

**Ngày tạo:** 2026-07-26
**Phạm vi:** `src/DBI.Controller.Studio` (WPF, net10.0-windows)
**Trạng thái:** Brainstorm hoàn tất — sẵn sàng cho `/awf-plan`
**BRIEF cha:** [BRIEF.md](BRIEF.md) — Studio là hạng mục Phase 2 của DBI.Controller

---

## 1. VẤN ĐỀ CẦN GIẢI QUYẾT

DBI.Controller Runtime đã có nền tảng (ScanEngine, DriverManager, SafetyCatch, SDK Primitives), nhưng **không có công cụ kỹ thuật (engineering tool)** để dùng nó. Hiện trạng Studio:

| Thành phần | Thực tế |
|---|---|
| [MainWindow.xaml](../src/DBI.Controller.Studio/MainWindow.xaml) | 296 dòng, 3 `TabItem` phẳng |
| [MainViewModel.cs](../src/DBI.Controller.Studio/ViewModels/MainViewModel.cs) | 188 dòng, **100% dữ liệu hardcode** (5 device giả, 4 tag giả) |
| Code editor | AvalonEdit giữ **duy nhất 1 string** `CSharpCode` |
| Khái niệm "Project" | ❌ Không tồn tại. Không mở/lưu/quản lý file |
| [RoslynCompilerService.cs](../src/DBI.Controller.Studio/Services/RoslynCompilerService.cs) | 62 dòng, compile 1 source → `byte[]`, **chưa nối vào Runtime** |
| [LiveMonitoringService.cs](../src/DBI.Controller.Studio/Services/LiveMonitoringService.cs) | 51 dòng, sinh số liệu giả |

**Kết luận thẳng:** Studio hiện tại là UI mockup đẹp nhưng rỗng ruột. Đây là công việc **xây mới**, không phải "hoàn thiện". Điều đó là lợi thế — được chọn kiến trúc đúng ngay từ đầu.

Ba nhu cầu người dùng nêu:
1. **Cây project** — quản lý nhiều khối code (Main, Function, Function Block, Data Block)
2. **Gán tag kiểu TIA Portal** — bảng tag là nguồn sự thật, không phải sửa code khi đổi địa chỉ
3. **Hoàn thiện UI/UX** — phong cách TIA Portal

---

## 2. GIẢI PHÁP ĐỀ XUẤT

**DBI.Studio = TIA Portal cho kỹ sư C#.**

Giữ nguyên **mô hình tư duy** (mental model) của TIA Portal — cây project, tag table, khối chương trình, chế độ giám sát "kính" — nhưng thay ngôn ngữ IEC 61131-3 (LAD/FBD/SCL) bằng **C# thuần với IntelliSense đầy đủ**.

> Điểm bán hàng: *"TIA Portal mà bạn quen — nhưng viết bằng C#, có IntelliSense, có Unit Test, có Git."*

---

## 3. ĐỐI TƯỢNG SỬ DỤNG

| Nhóm | Kỳ vọng khi mở Studio lần đầu |
|---|---|
| **Kỹ sư PLC chuyển sang C#** | Thấy cây project quen thuộc → tự tin ngay. Không cần học lại quy trình engineering |
| **Kỹ sư phần mềm C# vào tự động hóa** | Thấy IntelliSense, thấy file `.cs` thật → làm việc như trong Visual Studio |
| **System Integrator** | Đổi thiết bị/địa chỉ chỉ sửa Tag Table, không đụng logic, không recompile thủ công |

---

## 4. NGHIÊN CỨU: TIA PORTAL V20 — BỘ KHUNG CẦN SAO CHÉP

### 4.1. Bốn vùng cố định của Project View

TIA Portal Project view gồm: **Project tree** (trái) · **Working area** (giữa) · **Inspector window** (dưới) · **Task cards** (phải), cộng **Details window** hiển thị nội dung của node đang chọn.

- **Project tree** — cây phân cấp toàn bộ dự án tự động hóa
- **Working area** — vùng chính, mở nhiều editor dạng tab
- **Inspector window** — 3 tab cố định: **Properties** (thuộc tính đối tượng đang chọn), **Information** (thông tin & cảnh báo), **Diagnostics** (sự kiện chẩn đoán hệ thống)
- **Task cards** — thanh phải, nội dung thay đổi theo đối tượng đang chỉnh sửa

### 4.2. Cấu trúc cây project chuẩn TIA

```
PLC_1 [CPU 1214C]
├── Device configuration
├── Online & diagnostics
├── Program blocks
│   ├── Main [OB1]          ← điểm vào chu kỳ
│   ├── Conveyor [FB1]      ← có state, đi kèm Instance DB
│   ├── ScaleValue [FC1]    ← không state
│   └── Recipe_DB [DB2]     ← dữ liệu
├── PLC tags
│   └── Default tag table   ← Name | DataType | Address | Comment
└── Watch and force tables
```

### 4.3. Quy tắc kỹ thuật TIA đáng học

- **OB1 giữ mỏng** — chỉ gọi FB/FC mô tả luồng chương trình, không chứa logic chi tiết
- **Tag table chỉ chứa I/O** vùng nhớ I và Q; dữ liệu nội bộ nằm trong DB
- **Mỗi subroutine nên là một FB** được gọi từ Main OB; nên tạo một tag table riêng cho mỗi subroutine
- **FB luôn có Instance DB** lưu state và biến static qua các chu kỳ scan

### 4.4. Điểm YẾU của TIA mà DBI.Studio phải thắng

Nghiên cứu trung thực — không chỉ sao chép mà phải hơn:

| Điểm yếu TIA Portal | DBI.Studio khắc phục |
|---|---|
| Không có IntelliSense thật (SCL editor rất yếu) | Roslyn IntelliSense đầy đủ |
| Project là binary blob, Git diff vô nghĩa | `.cs` + JSON — Git diff đọc được |
| Không Unit Test được | xUnit chạy thẳng trên khối logic |
| Khởi động chậm, nặng hàng GB | WPF gọn nhẹ |
| Đóng kín, đắt đỏ | Mở, C# thuần |

---

## 5. QUYẾT ĐỊNH KIẾN TRÚC (đã chốt)

### ✅ QĐ-1: Tag Table là nguồn sự thật duy nhất → sinh code

Khai báo tag trong bảng → Studio sinh `IO.g.cs`. Code chỉ dùng được tag đã khai báo.

```
Tag Table (UI)
┌──────────────┬────────┬─────────────┬──────────────┐
│ Name         │ Type   │ Device      │ Address      │
├──────────────┼────────┼─────────────┼──────────────┤
│ StartButton  │ Bool   │ FactoryIO   │ Input_0      │
│ ConveyorRun  │ Bool   │ FactoryIO   │ Output_0     │
│ Temperature  │ Real   │ Modbus_IO   │ 40001        │
└──────────────┴────────┴─────────────┴──────────────┘
        ↓ generate
// IO.g.cs (auto-generated — DO NOT EDIT)
public partial class IOContainer {
    public bool  StartButton => GetBool("StartButton");
    public bool  ConveyorRun { get => GetBool("ConveyorRun");
                               set => SetBool("ConveyorRun", value); }
    public float Temperature => GetFloat("Temperature");
}
```

**Lợi ích:** đổi địa chỉ phần cứng không đụng code logic · sai chính tả tag = lỗi compile · IntelliSense gợi ý đúng tag · kiểu dữ liệu được kiểm tra.

### ✅ QĐ-2: Cây project Hybrid — template TIA, nội dung C# tự do

Cây có nhóm chuẩn TIA. "Add new block" cho chọn loại → sinh file `.cs` từ template đúng chuẩn. Nhưng **bên trong file là C# thuần, không ràng buộc gì thêm**.

```
📁 ConveyorProject
 ├─ ⚙️  Device Configuration
 ├─ 📊 Online & Diagnostics
 ├─ 📦 Program Blocks
 │   ├─ ▶️  Main.cs            [Cyclic]
 │   ├─ 🔷 Conveyor.cs         [FB]
 │   ├─ 🔶 ScaleValue.cs       [FC]
 │   └─ 🗄️  RecipeData.cs       [DB]
 ├─ 🏷️  PLC Tags
 │   ├─ Default Tag Table
 │   └─ Conveyor Tags
 ├─ 👁️  Watch & Force Tables
 └─ 🔌 Devices
```

Ánh xạ khái niệm:

| TIA Portal | DBI.Studio | Ràng buộc |
|---|---|---|
| `Main [OB1]` | `class Main : ControllerProgram` | Bắt buộc 1 và chỉ 1 |
| `Function Block [FB]` | `class X` có field state | Template gợi ý, không ép |
| `Function [FC]` | `static class` / method thuần | Template gợi ý, không ép |
| `Data Block [DB]` | `record` / POCO | Template gợi ý, không ép |

### ✅ QĐ-3: AvalonDock — dựng lại 4 vùng TIA

```
┌───────────────────────────────────────────────────┐
│ ☰ File Edit  │ 🚀Deploy  👓Monitor  ⏱1.2ms      │
├──────────┬────────────────────────────┬───────────┤
│ Project  │  Main.cs × │ Tags ×       │ Toolbox   │
│  tree    │ ┌────────────────────────┐ │ ───────── │
│          │ │ 1 public override     │ │ ▸Timers   │
│ 📦Blocks │ │ 2   void Execute(){   │ │  Ton Tof  │
│  ▶Main   │ │ 3     IO.Run = true;  │ │ ▸Counters │
│  🔷Conv  │ │       └─ TRUE  👓     │ │  CTU CTD  │
│ 🏷️Tags   │ └────────────────────────┘ │ ▸Edge     │
├──────────┴────────────────────────────┴───────────┤
│ Properties │ Info │ Diagnostics  ← Inspector      │
├───────────────────────────────────────────────────┤
│ RUN │ Scan 1.2ms │ Jitter 0.1ms │ 5 devices OK    │
└───────────────────────────────────────────────────┘
```

Panel kéo-thả, thu gọn, float, lưu/khôi phục layout.

### ✅ QĐ-4: Roslyn IntelliSense đầy đủ (RoslynPad.Editor)

Auto-complete `IO.<tag>` · signature help · gạch đỏ lỗi real-time · hover tooltip.

---

## 6. 🚨 PHÁT HIỆN CHẶN: `IOContainer` là `DynamicObject`

**Đây là rủi ro nghiêm trọng nhất, phải xử lý trước mọi việc khác.**

[IOContainer.cs](../src/DBI.Controller.SDK/IO/IOContainer.cs) hiện kế thừa `DynamicObject`:

```csharp
public class IOContainer : DynamicObject {
    public override bool TryGetMember(GetMemberBinder binder, out object? result) {
        result = _memoryImage.GetBool(binder.Name);   // ⚠️ LUÔN trả về bool
        return true;
    }
}
```

Ba hệ quả trực tiếp:

1. **IntelliSense bất khả thi.** `dynamic` không có metadata compile-time → Roslyn không gợi ý được gì. QĐ-4 chết ngay.
2. **Sai chính tả không bị phát hiện.** `IO.StartButtonn` compile pass, chạy trả `false` âm thầm — đây là loại lỗi nguy hiểm nhất trong điều khiển máy móc.
3. **Bug thật đang tồn tại:** `TryGetMember` luôn gọi `GetBool` bất kể kiểu. Đọc `IO.Temperature` (Real) sẽ trả về `bool`, không phải `float`.

**Giải pháp bắt buộc:** chuyển `IOContainer` thành `partial class` thường (bỏ `DynamicObject`), phần thuộc tính strongly-typed do Tag Table sinh ra vào `IO.g.cs`. Giữ `GetBool/SetBool/indexer` làm API nội bộ cho code sinh gọi.

**Đây là task số 0 — không làm cái này thì QĐ-1 và QĐ-4 đều không thể triển khai.**

---

## 7. TÍNH NĂNG & ĐỘ PHỨC TẠP

### 🚀 MVP — Giai đoạn 1 (nền tảng)

| # | Tính năng | Độ khó | Ghi chú |
|---|---|---|---|
| 0 | **Refactor `IOContainer`** bỏ `DynamicObject` → `partial class` | 🟢 Dễ | **Chặn tất cả** — làm đầu tiên |
| 1 | **Project Model + Persistence** — `.dbiproj` (JSON) + thư mục `.cs` | 🟢 Dễ | New/Open/Save/Save As, MRU list |
| 2 | **Project Tree (TreeView)** — nhóm chuẩn TIA, context menu Add/Rename/Delete | 🟡 Vừa | Icon theo loại block |
| 3 | **Multi-file Roslyn compile** — gộp tất cả `.cs` + `IO.g.cs` → 1 assembly | 🟡 Vừa | Nâng cấp `RoslynCompilerService` |
| 4 | **Tag Table editor** — DataGrid Name/Type/Device/Address/Comment, validate trùng tên | 🟡 Vừa | Nhiều bảng tag |
| 5 | **IO Code Generator** — Tag Table → `IO.g.cs` | 🟢 Dễ | Chạy khi save tag / trước compile |
| 6 | **AvalonDock shell 4 vùng** — refactor `MainWindow` | 🟡 Vừa | Theme khớp phong cách công nghiệp phẳng hiện tại |
| 7 | **Inspector window** — Properties / Info / Diagnostics | 🟢 Dễ | Thay `TextBox` log hiện tại |
| 8 | **MVVM đúng chuẩn** — tách `MainViewModel` 188 dòng, dùng `CommunityToolkit.Mvvm` | 🟡 Vừa | Bỏ `Click=` code-behind, dùng `ICommand` |

**Kiểm chứng MVP:** *"Nếu Studio chỉ có 9 mục trên — tạo project, thêm khối, khai báo tag, compile ra DLL chạy được trên Runtime — có dùng được không?"* → **Có.** Đó là một engineering tool hoàn chỉnh tối thiểu.

### 🎁 Phase 2 — Trải nghiệm

| # | Tính năng | Độ khó | Ghi chú |
|---|---|---|---|
| 9 | **Roslyn IntelliSense** (RoslynPad.Editor) | 🔴 Khó | Rủi ro tích hợp cao, xem §8 |
| 10 | **Error List real-time** — gạch đỏ + double-click nhảy tới dòng | 🟡 Vừa | Phụ thuộc #9 hoặc chạy độc lập sau compile |
| 11 | **Drag-drop tag mapping** — kéo device tag từ Task card thả vào Tag Table | 🟡 Vừa | Đúng cam kết trong BRIEF gốc §4.3 |
| 12 | **Task Cards / Toolbox** — `Ton`, `Tof`, `Tp`, `CTU`, `CTD`, `RisingEdge` → kéo thả chèn snippet | 🟢 Dễ | Đọc từ SDK Primitives |
| 13 | **Live Monitoring overlay ("kính")** — hiện giá trị inline cạnh dòng code | 🔴 Khó | Custom rendering trong AvalonEdit |
| 14 | **Watch Table** — chọn tag theo dõi real-time dạng bảng | 🟢 Dễ | Dễ hơn #13 nhiều, làm trước |
| 15 | **Force I/O** — cưỡng bức giá trị để debug | 🟡 Vừa | Cần Runtime hỗ trợ force layer |
| 16 | **Nối Studio ↔ Runtime thật** — thay `LiveMonitoringService` giả bằng kênh thật | 🟡 Vừa | Xem rủi ro §8.3 |
| 17 | **Device Configuration editor** — thay data hardcode bằng CRUD thật | 🟢 Dễ | |

### 💭 Backlog

- Portal View (màn hình khởi động dạng thẻ lớn như TIA)
- Cross-reference — "tag này được dùng ở khối nào, dòng nào"
- Compare online/offline (so sánh project với PLC đang chạy)
- Import tag từ CSV / file dự án TIA
- Undo/Redo toàn cục cấp project
- Multi-language UI (VI/EN)
- Source Generator thay cho codegen thủ công (đã ghi trong BRIEF gốc §5 Backlog)

---

## 8. RỦI RO KỸ THUẬT

### 8.1. 🔴 `IOContainer` là `DynamicObject` — CHẶN
Đã phân tích §6. **Giảm thiểu:** làm task #0 trước tiên; kiểm tra toàn bộ code hiện dùng `dynamic IO` (`ControllerProgram`, `Sample.Conveyor`, tests) và cập nhật đồng bộ.

### 8.2. 🔴 RoslynPad.Editor vs net10.0-windows + Roslyn 5.6.0
Studio đang dùng `Microsoft.CodeAnalysis.CSharp` **5.6.0** trên **net10.0-windows**. RoslynPad.Editor thường pin một version Roslyd cụ thể → nguy cơ xung đột assembly.
**Giảm thiểu:** làm **spike 1 ngày** dựng project WPF trống thử tích hợp trước khi cam kết. Nếu hỏng → lùi về phương án trung gian (completion tự viết đọc từ Tag Table + Error List từ diagnostics sau compile), vẫn đạt ~70% giá trị.

### 8.3. 🟡 Ranh giới Studio ↔ Runtime chưa xác định
Studio hiện `ProjectReference` thẳng vào `DBI.Controller.Runtime`. Chạy Runtime **in-process** trong UI thread sẽ phá vỡ tính deterministic của scan cycle 20ms — chính là rủi ro #1 trong [BRIEF.md §7](BRIEF.md).
**Cần quyết ở `/awf-plan`:** in-process worker thread riêng (đơn giản, rủi ro jitter) **hay** tiến trình Runtime riêng + IPC gRPC/NamedPipe (đúng đắn, phức tạp hơn).

### 8.4. 🟡 Xung đột TargetFramework
`Directory.Build.props` = `net8.0`, Studio override = `net10.0-windows`. Các project khác vẫn net8.0.
**Giảm thiểu:** chuẩn hóa toàn solution hoặc xác nhận cross-targeting hoạt động khi Studio load assembly do Runtime sinh.

### 8.5. 🟡 AvalonDock theming
Package (`Dirkster.AvalonDock`) mang theme mặc định VS2013/Metro, khả năng đè lên phong cách "flat industrial 2px border" vừa được thiết kế ở 2 commit gần nhất.
**Giảm thiểu:** dùng `AvalonDock.Themes.VS2013` làm nền rồi override brush, hoặc viết theme riêng khớp `WindowBg/CardBg/BorderColor` sẵn có.

### 8.6. 🟡 Live monitoring overlay là bài toán rendering khó
Hiển thị giá trị inline cạnh dòng code cần custom `IBackgroundRenderer`/`VisualLineElementGenerator` của AvalonEdit, đồng thời cập nhật ở tần suất cao mà không giật UI.
**Giảm thiểu:** làm **Watch Table (#14)** trước — đạt 80% giá trị debug với 20% công sức. Overlay để sau.

### 8.7. 🟢 `MainViewModel` monolithic
188 dòng đã trộn lẫn: theme tokens, dữ liệu device, tag, code, compiler, monitoring. Thêm project tree + tag table vào đây sẽ không quản lý nổi.
**Giảm thiểu:** tách ViewModel theo panel ngay ở task #8, đưa theme tokens ra `ResourceDictionary` thay vì binding string.

---

## 9. ƯỚC TÍNH TỔNG

| Giai đoạn | Khối lượng | Kết quả bàn giao |
|---|---|---|
| **MVP (#0–#8)** | ~2–3 tuần | Engineering tool dùng được: tạo project → khai báo tag → viết nhiều khối C# → compile → deploy |
| **Phase 2 (#9–#17)** | ~2–4 tuần | Trải nghiệm ngang TIA Portal: IntelliSense, monitoring, watch/force, drag-drop |

**Độ phức tạp tổng thể:** 🟡 Khá cao — nhưng chia được thành các bước độc lập, mỗi bước đều cho ra sản phẩm chạy được.

---

## 10. BƯỚC TIẾP THEO

Chạy **`/awf-plan`** để:
1. Chốt ranh giới Studio ↔ Runtime (§8.3) — quyết định kiến trúc quan trọng nhất còn treo
2. Thiết kế schema `.dbiproj` chi tiết
3. Thiết kế class diagram cho Project Model / Tag Model / Code Generator
4. Lên spike plan cho RoslynPad.Editor (§8.2)
5. Chia task cụ thể theo thứ tự #0 → #8

---

## Nguồn nghiên cứu

- [Function and structure of the project tree — TIA Portal V20](https://docs.tia.siemens.cloud/r/en-us/v20/introduction-to-the-tia-portal/user-interface-and-operation/layout-of-the-user-interface/project-tree/function-and-structure-of-the-project-tree)
- [Project view — TIA Portal V20](https://docs.tia.siemens.cloud/r/en-us/v20/introduction-to-the-tia-portal/user-interface-and-operation/layout-of-the-user-interface/project-view)
- [Inspector window — TIA Portal V20](https://docs.tia.siemens.cloud/r/en-us/v20/introduction-to-the-tia-portal/user-interface-and-operation/layout-of-the-user-interface/inspector-window)
- [Task cards — TIA Portal V21](https://docs.tia.siemens.cloud/r/en-us/v21/introduction-to-the-tia-portal/user-interface-and-operation/layout-of-the-user-interface/task-cards)
- [TIA Portal FB/FC/OB Program Structure Best Practices](https://industrialmonitordirect.com/blogs/knowledgebase/tia-portal-fb-fc-ob-program-structure-best-practices)
- [Siemens TIA Portal basics: project tree, devices, and tags](https://www.plcprogramming.co.za/brands/siemens/tia-portal-basics)
- [A Programming Style guide for TIA PORTAL](https://www.linkedin.com/pulse/programming-style-guide-tia-portal-ahmed-abd-alrahiem)
- [TIA Portal — Inspector's window (PLCspace)](https://sklep-plcspace.pl/en/blog/tia-portal-inspector-window/)
