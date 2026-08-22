# 💡 BRIEF: Đại tu UI DBI.Studio — chuẩn Visual Studio

Created: 2026-08-21
Nguồn: /awf-brainstorm — khảo sát code thật, build baseline xanh (0 warning/0 error).

---

## 1. Vấn đề

DBI.Studio đã đủ tính năng (phase 00–13) nhưng lớp giao diện tụt lại so với phần lõi:

1. **Toolbox "không dùng được"** — người dùng click/double-click/kéo tag vào code editor
   và **không thấy gì xảy ra trên editor**, dù log báo "Inserted…".
2. **Theme chưa đồng bộ** — trong Dark mode vẫn có control bật lên trắng lóa kiểu WinForms.
3. **Chưa hiện đại** — title bar trắng hệ điều hành, cây project không icon, dialog lỗi
   kiểu MessageBox hệ thống.

## 2. Nguyên nhân gốc (đã xác minh bằng đọc code)

### Bug nhóm Toolbox — 3 bug thật, không phải cảm tính

| # | Bug | Bằng chứng |
|---|-----|-----------|
| T-1 | **Luồng dữ liệu editor một chiều.** ViewModel đổi `Text` nhưng không có gì đẩy ngược vào `RoslynCodeEditor` — chữ chỉ đổi trong bộ nhớ, lần gõ phím sau đè mất. Người dùng thấy log "Inserted…" nhưng editor không hiện gì. | `InsertAtCaret` ghi VM ([CodeEditorViewModel.cs:57](../src/DBI.Controller.Studio.Core/ViewModels/CodeEditorViewModel.cs)); `RoslynCodeEditorBehaviors.OnTextChanged` chỉ sync editor→VM ([RoslynCodeEditorBehaviors.cs:237](../src/DBI.Controller.Studio/Behaviors/RoslynCodeEditorBehaviors.cs#L237)) |
| T-2 | **Con trỏ không bao giờ được cập nhật.** `CaretLine`/`CaretColumn` mặc định 1/1, không có chỗ nào gán lại từ editor thật → mọi lần chèn nhắm đầu dòng 1. Kèm sai cộng cột với snippet nhiều dòng (`CaretColumn += snippet.Length`). | [CodeEditorViewModel.cs:42-66](../src/DBI.Controller.Studio.Core/ViewModels/CodeEditorViewModel.cs) |
| T-3 | **Phím tắt quảng cáo nhưng không tồn tại.** Ô lọc ghi "Search Toolbox (Ctrl+T)", nút Save ghi "(Ctrl+S)" — app chỉ bind Ctrl+Shift+B và F5. | [MainWindow.xaml:18-21](../src/DBI.Controller.Studio/MainWindow.xaml), [MainWindow.xaml:118](../src/DBI.Controller.Studio/MainWindow.xaml) |

Kéo-thả vào Tag Table / Watch Table hoạt động bình thường (đi đường nghiệp vụ riêng qua
`TagDragDropBehaviors.ResolveHandler`) — hỏng nằm ở nhánh code editor.

### Nhóm Theme

- **Control chưa style**: ComboBox + dropdown, ContextMenu + submenu MenuItem, ScrollBar,
  ToolTip, TreeView expander arrow → dùng template mặc định của Windows, trắng lóa trong Dark.
- **Màu cứng ngoài bảng theme**: nền đỏ dòng force `#33C72C3B` (MainWindow.xaml:318),
  marker overlay `Brushes.DodgerBlue` (RoslynCodeEditorBehaviors.cs:310), viền drop `#005A9E`
  (TagDragDropBehaviors). Đổi Light sẽ sai tông.
- **Bộ token mỏng** (13 màu) so với chuẩn VS — thiếu disabled, hover input, tab-active,
  splitter… nên style phải chữa lửa bằng opacity.
- **Lệch nội bộ**: thiết kế gốc "flat, không bo góc" nhưng style mới bo 3–4px tuỳ nơi;
  comment App.xaml nói "nạp Light mặc định" trong khi thực tế nạp Dark.

### Nhóm Hiện đại hoá

- Title bar trắng HĐH (VS dùng title bar tối tuỳ biến).
- Cây project không icon theo loại node (OB/FC/FB/DB/device).
- Lỗi báo bằng MessageBox hệ thống thay vì dialog cùng tông.

## 3. Người dùng

Kỹ sư tự động hoá công nghiệp — quen Visual Studio và TIA Portal. Họ đánh giá độ tin cậy
của tool **bằng chất lượng IDE**: control lệch tông = tool amateur = không dám chạy máy thật.

## 4. Market Research

Không cần nghiên cứu thị trường ngoài — chuẩn tham chiếu là chính Visual Studio:
- **Dark**: editor `#1E1E1E`, chrome `#2D2D30`, selection `#094771`, accent xanh VS blue.
- **Light**: editor `#F5F5F5`, chrome `#EEEEF2`, selection `#007ACC`.
- Bộ token đầy đủ của VS gồm ~40 key; DBI.Studio đang có 13.

## 5. Features

### 🚀 MVP — Phase A: Sửa hỏng (bug thật)

1. **T-1**: Sync hai chiều VM→editor cho `RoslynCodeEditor`. Khi VM đổi Text phải render lại
   trong editor mà không mất vị trí con trỏ, không bắn vòng lặp TextChanged, không phá
   IntelliSense (Roslyn document phải cập nhật theo).
2. **T-2**: Đẩy vị trí caret thật từ editor lên VM mỗi khi caret di chuyển; sửa phép tính
   chèn với snippet nhiều dòng (chèn xong đặt caret cuối đoạn chèn).
3. **T-3**: Thêm KeyBinding Ctrl+T (focus ô lọc Toolbox) và Ctrl+S (Save all); hoặc bỏ nhãn
   phím tắt nếu không làm — không được quảng cáo phím chết.

### 🚀 MVP — Phase B: Hệ token Visual Studio đầy đủ

4. Mở rộng token từ 13 → ~35-40 key theo bảng màu VS thật (cả Light lẫn Dark): thêm
   disabled fg/bg, hover/pressed, input border/focus, tab active/inactive, splitter,
   scrollbar, tooltip bg/fg, menu hover, status bar…
5. Style nốt các control còn mặc định: ComboBox (+dropdown), ContextMenu/MenuItem submenu,
   ScrollBar/ScrollViewer, ToolTip, TreeViewItem expander, CheckBox/RadioButton nếu dùng.
6. Thay toàn bộ màu cứng bằng token mới (force row, overlay marker, drop highlight).
7. Chốt một phong cách bo góc duy nhất (đề xuất: 2px đồng nhất, bỏ các chỗ 3-4px).

### 🎁 Phase C: Hiện đại hoá vỏ

8. Title bar tối tuỳ biến (WindowChrome) — icon app + tên project + nút min/max/close
   theo theme.
9. Icon cây project theo loại node (giấy MDL2 có sẵn trong repo).
10. Dialog xác nhận force/thoát dùng chung tông theme thay MessageBox hệ thống.

### 💭 Backlog (ghi nhận, chưa hẹn kỳ)

- Nút chuyển 3 chế độ Light/Dark/Theo-Windows.
- Animation chuyển theme mượt (fade brush).
- High-DPI rà soát riêng màn hình scale 150%.

## 6. Complexity & Risks

| Việc | Độ khó | Rủi ro |
|------|--------|--------|
| T-1 sync hai chiều RoslynCodeEditor | 🔴 Hard | Phải cập nhật cả Roslyn document (IntelliSense) khi set text từ ngoài; chống loop TextChanged; giữ caret. Spike ngắn trước khi làm. |
| T-2 caret tracking | 🟢 Easy | Gắn event `TextArea.Caret.PositionChanged`, marshal về VM. |
| T-3 keybinding | 🟢 Easy | Focus ô lọc cần `FocusManager`; Ctrl+S xung đột mặc định WPF? — kiểm tra. |
| Token mở rộng | 🟡 Medium | Cơ học nhưng phải quét hết XAML thay brush; test cả 2 theme. |
| Style ComboBox/ContextMenu/ScrollBar | 🟡 Medium | Template WPF dài, dễ sót trigger; copy mẫu chuẩn rồi tinh gọn. |
| Title bar tuỳ biến | 🟡 Medium | WindowChrome + xử lý maximize padding, snap, double-click; AvalonDock phải sống chung. |
| ⚠️ Không được phá | — | Chu kỳ scan 20ms, IPC camelCase, force-safety flow, ADR-001. UI-only change nhưng build phải giữ 0 warning. |

## 7. Next → /awf-plan

Đầu vào cho plan:
- Phase A (bug) → phase file riêng, DoD: chèn snippet/tag hiện ngay trên editor đúng vị trí
  con trỏ; Ctrl+T/Ctrl+S hoạt động.
- Phase B (token) → phase file riêng, DoD: quét XAML không còn hex ngoài Themes/*.xaml;
  cả 2 theme chụp màn hình đối chiếu.
- Phase C (vỏ) → phase file riêng, DoD: title bar tối cả 2 theme, icon cây project.
- Giữ nguyên mọi logic Runtime/IPC/Safety — chỉ đụng Studio + Studio.Core (phần VM).
