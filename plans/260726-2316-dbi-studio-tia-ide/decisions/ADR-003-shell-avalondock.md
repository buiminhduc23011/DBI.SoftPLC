# ADR-003: AvalonDock 4 vùng thay TabControl

**Date:** 2026-07-26 | **Status:** ✅ Accepted

## Context

[MainWindow.xaml](../../../src/DBI.Controller.Studio/MainWindow.xaml) hiện là `TabControl` 3 tab phẳng: Code Editor / Tag Mapper / Device Manager. Muốn xem code và tag cùng lúc là không được — phải chuyển tab.

TIA Portal Project view có 4 vùng hiển thị **đồng thời**: Project tree (trái), Working area (giữa, nhiều tab document), Inspector window (dưới — Properties/Information/Diagnostics), Task cards (phải). Đây chính là thứ tạo cảm giác "engineering tool" chứ không phải "app 3 tab".

## Decision

Dùng **`Dirkster.AvalonDock`** dựng lại 4 vùng.

```
┌───────────────────────────────────────────────────┐
│ ☰ File Edit  │ 🚀Deploy  👓Monitor  ⏱1.2ms      │  ← Toolbar
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
│ RUN │ Scan 1.2ms │ Jitter 0.1ms │ 5 devices OK    │  ← StatusBar
└───────────────────────────────────────────────────┘
```

| Vùng | AvalonDock | Nội dung |
|---|---|---|
| Trái | `LayoutAnchorablePane` | Project Tree |
| Giữa | `LayoutDocumentPane` | Code editor, Tag Table, Device Config, Watch Table (mỗi cái 1 document tab) |
| Dưới | `LayoutAnchorablePane` | Inspector: Properties / Information / Diagnostics |
| Phải | `LayoutAnchorablePane` | Task Cards: Toolbox, Device Tags |

Layout lưu vào `.dbistudio/layout.xml` trong thư mục project, **không commit** (thêm vào `.gitignore`).

## Consequences

### Tích cực
- Nhìn code và tag cùng lúc — nhu cầu thực tế hàng ngày
- Panel float/dock/auto-hide, người dùng tự sắp theo màn hình của mình
- Mở nhiều khối code cùng lúc dạng document tab
- Đúng mental model TIA Portal

### Tiêu cực / chi phí
- Phải viết lại toàn bộ `MainWindow.xaml` (296 dòng)
- Thêm dependency ngoài
- **Rủi ro theming:** AvalonDock mang theme VS2013/Metro sẵn, khả năng cao đè lên phong cách "flat industrial 2px border" vừa thiết kế ở commit `83518a4` và `5c0a02d`

### Giảm thiểu rủi ro theming
Dùng `AvalonDock.Themes.VS2013` làm nền rồi override các brush key về đúng token màu đang có trong `MainViewModel` (`WindowBg`, `CardBg`, `HeaderBg`, `BorderColor`, `AccentColor`).

Nhân tiện: các token này hiện là **string binding** trên ViewModel — cách làm sai. Phase-04 chuyển sang `ResourceDictionary` + `DynamicResource`, đổi theme bằng cách swap dictionary. Đây là điều kiện cần để theme AvalonDock hoạt động được.

## Phương án bị loại

**Grid + GridSplitter** — rẻ hơn nhiều, đạt ~80% hình thức, nhưng không float/auto-hide/lưu layout được.

**Giữ TabControl thêm cây bên trái** — an toàn nhất nhưng không giải quyết được yêu cầu "hoàn thiện UI/UX theo phong cách TIA Portal".
