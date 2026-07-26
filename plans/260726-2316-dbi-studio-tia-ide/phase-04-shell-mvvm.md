# Phase 04 — AvalonDock Shell & MVVM

**Status:** ⬜ Pending | **Phụ thuộc:** phase-00 (spike), phase-01 | **Nội dung BRIEF:** #6, #7, #8
**ADR:** [ADR-003](decisions/ADR-003-shell-avalondock.md)

> Có thể làm **song song** với phase-02/03 nếu dùng `FakeRuntimeClient`.

---

## Task 04.1 — Tách `MainViewModel`

[MainViewModel.cs](../../src/DBI.Controller.Studio/ViewModels/MainViewModel.cs) hiện 188 dòng trộn lẫn: theme token, dữ liệu device, tag mapping, code, compiler, monitoring. Thêm project tree + tag table vào đây sẽ không quản lý nổi.

Cài `CommunityToolkit.Mvvm`, tách thành:

```
ViewModels/
├── ShellViewModel.cs           # layout, menu, toolbar, status bar, vòng đời project
├── Docking/
│   ├── PaneViewModelBase.cs    # Title, ContentId, IsActive, CanClose
│   ├── ProjectTreeViewModel.cs # phase-05
│   ├── InspectorViewModel.cs   # Properties / Information / Diagnostics
│   └── TaskCardsViewModel.cs   # phase-12
└── Documents/
    ├── DocumentViewModelBase.cs
    ├── CodeEditorViewModel.cs      # một instance / một block đang mở
    ├── TagTableViewModel.cs        # phase-06
    ├── DeviceConfigViewModel.cs    # phase-09
    └── WatchTableViewModel.cs      # phase-10
```

- Bỏ toàn bộ `Click="..."` trong [MainWindow.xaml](../../src/DBI.Controller.Studio/MainWindow.xaml) → `[RelayCommand]`
- Xoá [MainWindow.xaml.cs](../../src/DBI.Controller.Studio/MainWindow.xaml.cs) 48 dòng code-behind

## Task 04.2 — Theme sang `ResourceDictionary`

Theme hiện là **string binding** trên ViewModel (`WindowBg`, `CardBg`, `BorderColor`...) và `OnPropertyChanged` thủ công 7 property mỗi lần đổi theme. Cách này không theme được AvalonDock.

```
Themes/
├── Light.xaml       # SolidColorBrush theo key
├── Dark.xaml
└── Shared.xaml      # Button/DataGrid/TextBlock style — giữ nguyên phong cách flat industrial
```

Đổi theme = swap `MergedDictionaries`. XAML dùng `{DynamicResource WindowBg}` thay `{Binding WindowBg}`.

**Giữ nguyên bảng màu đã thiết kế** ở commit `83518a4` / `5c0a02d` — border 2px, accent `#005A9E`, xanh `#107C41`, đỏ `#C72C3B`.

## Task 04.3 — Shell 4 vùng

```
┌───────────────────────────────────────────────────┐
│ File Edit Project Online View Help                │  ← Menu
│ 🆕 📂 💾 │ 🔨Compile 🚀Deploy │ ▶Start ⏹Stop │ 👓 │  ← Toolbar
├──────────┬────────────────────────────┬───────────┤
│ Project  │  Main.cs × │ Tags ×       │ Toolbox   │
│  tree    │                            │           │
├──────────┴────────────────────────────┴───────────┤
│ Properties │ Information │ Diagnostics            │  ← Inspector
├───────────────────────────────────────────────────┤
│ ⚫OFFLINE │ Scan -- │ Jitter -- │ Project: MyMachine│  ← StatusBar
└───────────────────────────────────────────────────┘
```

| Vùng | AvalonDock element |
|---|---|
| Trái | `LayoutAnchorablePane` (width 260) |
| Giữa | `LayoutDocumentPane` |
| Dưới | `LayoutAnchorablePane` (height 180) |
| Phải | `LayoutAnchorablePane` (width 240) |

Layout lưu/khôi phục từ `.dbistudio/layout.xml`. Menu **View → Reset Layout** trả về mặc định.

> ✅ [Spike 2026-07-27](reports/spike-avalondock.md) xác nhận toàn bộ mục trên chạy được: 4 vùng, theme Light/Dark đổi lúc runtime, layout save/restore (1414 bytes), float/dock, auto-hide. **Dựng bằng code, không cần XAML** — hợp với MVVM.

⚠️ **Khôi phục layout cần callback gắn lại content.** `layout.xml` chỉ lưu cấu trúc + `ContentId`, không lưu content. Thiếu callback thì panel khôi phục ra rỗng:

```csharp
var ser = new XmlLayoutSerializer(dm);
ser.LayoutSerializationCallback += (s, e) => { e.Content = ResolvePane(e.Model.ContentId); };
ser.Deserialize(path);
```

→ `ShellViewModel` phải giữ bảng tra `ContentId` → ViewModel, và mọi pane cần `ContentId` **ổn định giữa các phiên**.

## Task 04.4 — Inspector window (3 tab cố định)

| Tab | Nội dung |
|---|---|
| **Properties** | Thuộc tính của node đang chọn ở Project Tree — tên, loại, đường dẫn file, comment. Sửa được. |
| **Information** | Log build, kết quả validation của phase-01, thông báo deploy |
| **Diagnostics** | Trạng thái Runtime, driver, fault của SafetyCatch, lịch sử scan |

Thay hẳn `TextBox CompilerOutput` hiện tại.

## Task 04.5 — Status bar thật

Nối `IRuntimeClient.StatusUpdated`. Chỉ báo trạng thái theo màu:

| Trạng thái | Màu | Text |
|---|---|---|
| `Stopped` | ⚪ xám | OFFLINE |
| `Running` | 🟢 `#107C41` | RUN |
| `Faulted` | 🔴 `#C72C3B` | FAULT — kèm thông điệp |
| `NoProgram` | 🟡 vàng | NO PROGRAM |

Thay chuỗi hardcode `"...RUNNING | Target: 20ms... | Status: OK"` ở [MainWindow.xaml:292](../../src/DBI.Controller.Studio/MainWindow.xaml).

---

## Definition of Done

- [ ] `Dirkster.AvalonDock` tích hợp, 4 vùng hiển thị đồng thời
- [ ] Layout lưu vào `.dbistudio/layout.xml`, khôi phục đúng khi mở lại project
- [ ] **View → Reset Layout** hoạt động
- [ ] `MainViewModel` 188 dòng đã bị tách; không file ViewModel nào > 200 dòng
- [ ] **Zero** `Click="..."` trong XAML; `MainWindow.xaml.cs` chỉ còn `InitializeComponent()`
- [ ] Theme dùng `ResourceDictionary` + `DynamicResource`; không còn theme token dạng string trên ViewModel — **spike xác nhận đây là điều kiện cần**, string binding không theme được AvalonDock
- [ ] `ShellViewModel` có bảng tra `ContentId` → ViewModel; `LayoutSerializationCallback` gắn lại content đúng
- [ ] Đổi Light↔Dark áp dụng cho **cả** panel AvalonDock (đây là điểm rủi ro chính, phải kiểm bằng mắt)
- [ ] Phong cách flat industrial giữ nguyên — so sánh ảnh chụp trước/sau
- [ ] Inspector 3 tab hoạt động; Properties phản ánh node đang chọn
- [ ] Status bar phản ánh trạng thái thật từ `IRuntimeClient` (dùng `FakeRuntimeClient` để test 4 trạng thái)
- [ ] Mở nhiều document tab cùng lúc, đóng từng cái được
- [ ] App chạy được với `FakeRuntimeClient` khi không có Runtime
