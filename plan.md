# Đại tu UI DBI.Studio — sửa hỏng Toolbox + hệ token Visual Studio

## Target
Sửa 3 bug khiến Toolbox "không dùng được" khi chèn vào code editor (sync VM→editor, caret thật, phím tắt chết), rồi thay bộ theme mỏng 13 màu bằng hệ token Visual Studio đầy đủ (~30 key) style nốt mọi control còn mặc định, xoá toàn bộ màu cứng — để Studio nhìn và vận hành như một IDE Visual Studio thật ở cả Light lẫn Dark.

## Source Of Truth
- Plan schema: `loop-plan/v1`
- Request/report: `docs/BRIEF.md` (brainstorm 2026-08-21) — user chọn "đại tu một thể", bám sát màu VS thật
- Baseline commit: `d84d269`
- Plan path: `plan.md`

## Current State
- Completed: Toolbox đã có ô lọc + nhóm sập/mở + drag-drop (commit `d84d269`, đang uncommitted trong worktree); kéo-thả TagTable/WatchTable hoạt động; build xanh 0 warning/0 error; test 258/260 pass (2 test jitter `ScanEngineMetricsTests` flaky khi CPU tải cao — rerun sau khi CPU nhàn rỗi thì 8/8 pass; đây là test timing của Runtime, KHÔNG liên quan UI).
- Remaining: 3 bug Toolbox (T-1 sync một chiều, T-2 caret không cập nhật, T-3 phím tắt quảng cáo nhưng không bind); mở rộng token; style ComboBox/ContextMenu/ScrollBar/ToolTip/TreeViewItem expander; xoá màu cứng (`#33C72C3B` MainWindow.xaml:318, `DodgerBlue` RoslynCodeEditorBehaviors.cs:310, `#005A9E` TagDragDropBehaviors.cs:168); key `MutedColor` bị VM tham chiếu (ProjectTreeViewModel.cs:74/139, DeviceConfigurationViewModel.cs:29/55, WatchTableViewModel.cs:56) nhưng **không tồn tại** trong cả Light.xaml lẫn Dark.xaml → converter rơi về Transparent.
- Worktree: dirty — các thay đổi Toolbox UI (ProjectTreeViewModel.cs, MainWindow.xaml, Shared.xaml, 2 converter mới, session_log) là công việc dở của user, PHẢI giữ nguyên và xây trên đó. Các file docs xoá là chủ ý của user (đã xác nhận).

## Codebase Evidence
- Current flow (T-1): Toolbox click/double-click → `TaskCardsViewModel.InsertInstructionCommand`/`InsertDeviceTagCommand` (ProjectTreeViewModel.cs:423-434) → `ShellViewModel.InsertSnippet`/`InsertTagReference` (ShellViewModel.cs:110-133) → `CodeEditorViewModel.InsertAtCaret` (CodeEditorViewModel.cs:57-66) đổi `_text` → `OnTextChanged` raise. Nhưng editor→VM chỉ đi một chiều: `RoslynCodeEditorBehaviors.OnTextChanged` (RoslynCodeEditorBehaviors.cs:231-256) đọc `editor.Text` ghi ngược `model.Text`; **không có handler nào gán `editor.Text` từ VM** (khác với AvalonEdit thường, nơi `BindableTextEditor.cs` làm sync hai chiều cho `GeneratedCodeViewModel`). Kết quả: chèn chỉ đổi bộ nhớ, màn hình không đổi.
- Current flow (T-2): `CaretLine`/`CaretColumn` khởi tạo 1/1 (CodeEditorViewModel.cs:42-46); grep toàn repo không thấy chỗ nào gán lại từ editor → `InsertAtCaret` luôn tính offset đầu dòng 1. Phép `CaretColumn += snippet.Length` cũng sai với snippet nhiều dòng.
- Current flow (T-3): Window.InputBindings chỉ có Ctrl+Shift+B và F5 (MainWindow.xaml:18-21). Placeholder ghi "Search Toolbox (Ctrl+T)" (MainWindow.xaml:118), tooltip Save ghi "(Ctrl+S)" (MainWindow.xaml:452) — không bind nào tương ứng.
- Existing tests: `tests/DBI.Controller.Tests/TaskCardsViewModelTests.cs` (chèn tại caret dòng 2 cột 2 qua VM), `TagDragDropMappingTests.cs`, `StudioRoslynWorkspaceTests.cs` (UpdateDocument rebuild IntelliSense), `CodeOverlayViewModelTests.cs`, `ShellViewModelTests.cs` (ScriptedPrompt/FakeThemeSwitcher pattern).
- Pattern to follow: attached-property behavior `BindableTextEditor.cs` (sync hai chiều TextEditor.Text ↔ VM với cờ chống vọng + giữ caret) — áp đúng mô hình này cho `RoslynCodeEditorBehaviors`; brush-via-resource-key `ResourceKeyToBrushConverter.cs` + `StatusColorKey` string key (VM không giữ mã màu); test VM thuần không STA (`TaskCardsViewModelTests`) vì test project là net10.0 thuần, không WPF.
- Known constraint (đã xác minh): `ResourceKeyToBrushConverter`/`MappedToBrushConverter` trả về **instance Brush cụ thể** tại thời điểm Convert chạy — KHÔNG tự cập nhật khi ThemeService swap MergedDictionaries (khác `{DynamicResource}`). Binding qua converter trên cây project/watch/device sẽ giữ màu theme cũ sau toggle live. Phase 2 có contract riêng xử lý.
- Known bug hiện hữu (phase 1 xử lý): Toolbox item là `Button` với Command — double-click gọi command 2 lần → chèn snippet/tag HAI lần. Drag bắt đầu từ PreviewMouseLeftButtonDown + MouseMove ngưỡng 4px nên drag không bắn Click, nhưng double-click thì có 2 lần Click.
- Known race hiện tại (phase 1 xử lý): `RoslynCodeEditorBehaviors.InitializeEditorAsync` là fire-and-forget, khởi tạo Roslyn từ snapshot `model.Text` lúc Loaded; nếu text/caret đổi trong lúc init chạy thì kết quả init lỗi thời. Subscription PropertyChanged và debounce timer hiện chưa được detach ở Unloaded cho đường mới thêm.
- Pattern fallback: bảng màu tham chiếu lấy từ Visual Studio thực tế (dark: chrome `#1F1F1F`/editor `#1E1E1E`, selection `#062B61`/`#094771`, accent `#007ACC`/`#0F6CBD`; light: editor `#F5F5F5`, chrome `#EEEEF2`, selection `#007ACC`) vì không có mẫu token nào khác trong repo.
- Verified gap: chèn snippet/tag hiện KHÔNG hiện lên editor (một chiều), luôn vào vị trí đầu file (caret ảo 1/1), phím tắt quảng cáo không tồn tại, `MutedColor` tra ra Transparent, 3 chỗ màu cứng lệch theme.

## Scope
- In scope:
  1. Sync hai chiều VM→`RoslynCodeEditor` (giữ caret, chống vọng, không phá IntelliSense — phải gọi `StudioRoslynWorkspace.UpdateDocument` theo đường hiện có).
  2. Caret thật: đẩy `TextArea.Caret.Location`/offset lên VM mỗi khi di chuyển; sửa `InsertAtCaret` cho snippet nhiều dòng (caret cuối đoạn chèn, tính lại line/column).
  3. KeyBinding Ctrl+T focus ô lọc Toolbox; Ctrl+S gọi `SaveAllCommand`.
  4. Mở rộng Light/Dark thành ~30 key theo chuẩn VS; thêm `MutedColor` (fix bug tra Transparent).
  5. Style implicit cho ComboBox (+dropdown popup), ContextMenu/MenuItem (kèm submenu), ScrollBar/ScrollViewer, ToolTip, TreeViewItem expander arrow, CheckBox/RadioButton.
  6. Thay mọi mã hex ngoài Themes/: force-row bg, drop-highlight border, overlay marker text, `Firebrick` ForceSafetyWindow header.
- Out of scope: title bar tuỳ biến (WindowChrome), icon cây project (Phase C của brief — làm đợt sau; dialog code-created THUỘC phạm vi phase 2 ở mức gán style tường minh, xem behavioral contract); mọi logic Runtime/IPC/Safety; B-6 driver; hiệu năng scan.

## Invariants
- Không phá chu kỳ scan 20ms / IPC camelCase / force-safety flow — change thuần UI layer (DBI.Controller.Studio + Studio.Core VM).
- Build `dotnet build DBI.Controller.slnx --nologo`: 0 Warning, 0 Error sau mỗi phase.
- Test suite: 258 pass hiện tại không được giảm; 2 test jitter được coi flaky-môi-trường (CPU tải cao), rerun riêng phải pass — nếu fail do code thì đó là regression thật.
- ADR-003: theme phải là ResourceDictionary + DynamicResource (spike đã chứng minh binding string không theme được AvalonDock).
- Roslyn ghim 5.3.0 — không đụng version.
- Giữ nguyên thay đổi uncommitted của user (Toolbox filter/groups, converters mới) — xây trên chúng, không revert.
- ViewModel (Studio.Core) không được biết WPF — mọi Brush vẫn trao đổi qua resource-key chuỗi.

## Global Gates
| Gate | Command | Expected result |
|---|---|---|
| Build | `dotnet build DBI.Controller.slnx --nologo` | 0 Warning, 0 Error |
| Full tests | `dotnet test DBI.Controller.slnx --nologo` | ≥258 passed; nếu 2 jitter fail → rerun riêng `dotnet test DBI.Controller.slnx --nologo --filter "FullyQualifiedName~ScanEngineMetricsTests"` phải 8/8 pass |
| Theme key parity | kiểm tra thủ công: mọi `DynamicResource X` trong .xaml và mọi resource-key chuỗi trong .cs phải tồn tại trong CẢ Light.xaml và Dark.xaml | 0 key thiếu |

## Execution Rules
- Execute phases in order unless a phase explicitly declares no dependency.
- For each phase: revalidate the cited code evidence, write or update contract tests first, confirm the intended failure when feasible, evaluate whether the tests reject naive shortcuts, implement the full behavioral contract using project patterns, run focused tests, run all phase gates, review the uncommitted diff, fix findings, and commit.
- Green tests alone do not complete a phase. The production diff must satisfy every implementation obligation and invariant for the full described input/state class.
- Never commit with failing gates or an unapproved implementation review.
- Preserve unrelated user changes; never reset or revert them. (Uncommitted Toolbox UI của user thuộc phase 1 scope — commit kèm như một phần của phase đó.)
- Mark a phase complete in this plan in the same commit as that phase's implementation.
- Continue automatically to the next incomplete phase. Stop only for a destructive/irreversible decision, missing credentials/infrastructure, irreconcilable requirements, or repeated failures with no new evidence.

## Plan Review
- Status: APPROVE (round 8)
- Rounds: 8
- Open issues: None.
- History: R1: 8 findings fixed · R2: 7 fixed · R3: 4 fixed · R4: 2 fixed · R5: 1 fixed (tách hai chiều attach) · R6: 1 fixed (init-write protection) · R7: 1 fixed (PositionChanged attach ngay từ Loaded) · R8: **APPROVE** — "resolves the identified correctness, lifecycle, theming, and verification gaps; executable path to all stated acceptance criteria".

## Phase 1: Sửa luồng chèn Toolbox — sync hai chiều, caret thật, phím tắt
- Status: complete
- Depends on: None
- Goal: Click/double-click/kéo tag từ Toolbox hiển thị ngay trên code editor đúng vị trí con trỏ; Ctrl+T và Ctrl+S hoạt động như nhãn đã quảng cáo.
- Current behavior: (1) `InsertAtCaret` đổi `_text` trên VM nhưng `RoslynCodeEditor` không nghe chiều VM→editor nên màn hình không đổi, lần gõ phím sau đè mất nội dung đã "chèn"; log Inspector báo "Inserted…" sai sự thật. (2) `CaretLine`/`CaretColumn` đứng yên ở 1/1 vì không ai gán — chèn luôn vào offset đầu file. (3) Chỉ có KeyBinding Ctrl+Shift+B/F5 (MainWindow.xaml:18-21) dù UI quảng cáo Ctrl+T/Ctrl+S.
- Code evidence: `CodeEditorViewModel.InsertAtCaret` (src/DBI.Controller.Studio.Core/ViewModels/CodeEditorViewModel.cs:57-66); chiều editor→VM duy nhất tại `RoslynCodeEditorBehaviors.OnTextChanged` (src/DBI.Controller.Studio/Behaviors/RoslynCodeEditorBehaviors.cs:231-256) + debounce 500ms `UpdateDocument` (dòng 239-255); mẫu sync hai chiều đối chiếu: `BindableTextEditor` (src/DBI.Controller.Studio/Behaviors/BindableTextEditor.cs:34-58, cờ IsSyncing + giữ CaretOffset); `StudioRoslynWorkspace.UpdateDocument(path, text)` (src/DBI.Controller.Studio.Core/Services/CodeAnalysis/StudioRoslynWorkspace.cs:75-84) rebuild workspace cho IntelliSense; InputBindings (src/DBI.Controller.Studio/MainWindow.xaml:18-21); placeholder Ctrl+T (MainWindow.xaml:114-122); tooltip Ctrl+S (MainWindow.xaml:451-452); test hiện có `CodeEditor_InsertsSnippetAtCaret` (tests/DBI.Controller.Tests/TaskCardsViewModelTests.cs:22-34) — test này PASS hiện tại vì nó test đúng phần VM hoạt động, gap nằm ở tầng WPF mà test net10.0-thuần không bọc được trực tiếp.
- Pattern to follow: `BindableTextEditor.cs` — attached property TwoWay, cờ chống vọng, giữ caret khi set text từ ngoài. Áp y mô hình vào `RoslynCodeEditorBehaviors` (nơi đã có vòng đời Loaded/Unloaded + Timer attach sẵn).
- Behavioral contract:
  - **Protocol truyền caret tách khỏi Text** (fix finding #2): `InsertAtCaret` KHÔNG chỉ đổi `Text` rồi tự tính lại — VM giữ pending-caret (offset đích sau chèn) trong property nội bộ trước khi gán `Text`; handler VM→editor đọc pending-caret SAU khi set `editor.Text`, đặt `editor.CaretOffset = pending`, rồi xoá pending. Thứ tự bắt buộc: (1) VM set pending-offset, (2) VM set Text → PropertyChanged → handler set editor.Text + CaretOffset từ pending, (3) handler PositionChanged đẩy (line,column) về VM. Hai lần chèn liên tiếp phải hoạt động đúng (pending được ghi đè, không dồn).
  - Khi `CodeEditorViewModel.Text` đổi từ nguồn ngoài editor (chèn snippet/tag, ReloadAsync), editor render lại văn bản mới; với chèn thì caret đặt CUỐI đoạn chèn (qua protocol trên), với ReloadAsync thì caret clamp về cuối văn bản; không bắn vòng lặp TextChanged vô hạn; không mất IntelliSense (set `editor.Text` bắn TextChanged nội bộ của editor → debounce `UpdateDocument` hiện có chạy tiếp — cờ chống vọng CHỈ chặn việc ghi NGƯỢC lại VM, không chặn workspace-update).
  - Khi người dùng gõ/di chuyển caret trong editor, `CaretLine`/`CaretColumn` trên VM cập nhật theo (1-based) — qua `TextArea.Caret.PositionChanged` + `editor.Document.GetLocation`.
  - **CRLF là chuẩn làm việc** (fix finding #3, định nghĩa lại theo #9): helper offset↔(line,column) dùng **offset .NET/AvalonEdit chuẩn — zero-based, `\r\n` chiếm ĐÚNG HAI ký tự offset**, ranh giới dòng nằm sau `\n`. Ví dụ chuẩn: text `"abc\r\ndef\r\nghi"` — offset 4 là ký tự `\n` cuối dòng 1, **dòng 2 bắt đầu tại offset 5**, `OffsetToPosition(text, 5) == (2, 1)`; `PositionToOffset(text, 2, 1) == 5`. `InsertAtCaret` chèn vào text gốc tại offset tính trên text gốc (bỏ bước normalize sai hiện tại). Helper hai chiều: `PositionToOffset(text,line,column)` + `OffsetToPosition(text,offset)` (1-based line/column, zero-based offset), cả hai clamp an toàn.
  - **Double-click chèn ĐÚNG MỘT LẦN** (fix finding #6): lệnh chèn phải idempotent theo cú nhấp — double-click sinh đúng 1 lần chèn; drag-drop không phát sinh thêm lệnh click chèn.
  - **Vòng đời init/unload race-safe, tách hai chiều** (fix findings #4/#13/#16/#18, chốt theo #19): HAI chiều attach khác thời điểm. Chiều editor→VM (`TextChanged` + `PositionChanged` caret tracking) attach NGAY khi Loaded — trước init — để keystrokes lẫn di chuyển caret của user trong thời gian Roslyn init đều cập nhật VM (`CaretLine/CaretColumn` luôn mới, chèn trong lúc init vẫn đúng chỗ). **Quy tắc bảo vệ init-write** (#18): handler TextChanged phân biệt nguồn — TextChanged do init gán `editor.Text` từ snapshot lỗi thời KHÔNG ghi đè `model.Text` khi model.Text đã khác snapshot-bắt-đầu-init (cờ đang-init hoặc so sánh snapshot). Sau await: (1) so snapshot ban đầu vs `model.Text` hiện tại (giá trị mới nhất, không bị đè nhờ quy tắc trên), khác thì áp lên editor, (2) consume `TryTakePendingCaretOffset` đặt caret đích, (3) attach chiều VM→editor còn thiếu (PropertyChanged(Text)/pending consumer). Detach đầy đủ ở Unloaded (cả hai chiều + debounce timer). Race này đưa vào inspection obligation bắt buộc của review.
  - Ctrl+T đặt focus vào TextBox lọc Toolbox; Ctrl+S chạy `SaveAllCommand`.
  - **Cầu nối focus Ctrl+T cụ thể** (bản DUY NHẤT — fix finding #5 round 2, giữ nguyên ở round 3): MainWindow.xaml.cs (nơi đã tham chiếu DockingManager/ToolboxPane hợp lệ) đăng ký RoutedCommand `FocusToolboxFilter` qua CommandBinding trên Window: handler (a) gọi `ToolboxPane.Show()` khi `IsHidden`/`IsAutoHidden` (KHÔNG BAO GIỜ gán `Docking.ActiveContent` — nó đang two-way bind `Editors.ActiveDocument` kiểu DocumentViewModelBase), (b) tìm TextBox tên `ToolboxFilterBox` bằng helper Descendants (MainWindow.xaml.cs:51) đi từ `Docking`, (c) nếu template chưa render xong thì defer qua `Dispatcher.BeginInvoke(DispatcherPriority.Loaded)`, rồi `Focus()` + select-all. KeyBinding Ctrl+T bind RoutedCommand này, Ctrl+S bind SaveAllCommand.
- Tests first:
  - Helper thuần text ở Studio.Core (static class `TextPositionMath`): `PositionToOffset(text,line,column)` + `OffsetToPosition(text,offset)` trên offset .NET chuẩn (`\r\n` = 2 offset). Tests: (a) LF-only nhiều dòng; (b) CRLF nhiều dòng — `"abc\r\ndef\r\nghi"`: `PositionToOffset(text,2,1)==5`, `OffsetToPosition(text,5)==(2,1)`, `OffsetToPosition(text,4)==(1,5)`; (c) clamp offset vượt độ dài, line/column vượt biên; (d) round-trip hai chiều cho cả LF lẫn CRLF.
  - Sửa/mở rộng `TaskCardsViewModelTests`: (a) `InsertAtCaret_MultiLineSnippet_CaretCuoiDoanChen` — VM text "abc\ndef", caret 2/2, chèn "X\nY" kỳ vọng text đúng và caret-end đúng theo helper (dòng 3 cột 2); test này FAIL trên code hiện tại (`CaretColumn += snippet.Length` cho 7). (b) `InsertAtCaret_CRLF_Chuẩn` — text "abc\r\ndef", caret 2/2, chèn "X" kỳ vọng "abc\r\ndXef" và caret đúng. (c) `InsertAtCaret_CaretNgoaiPhamVi_KhongCrash` — CaretLine=99 vượt số dòng vẫn clamp chèn cuối file. (d) `Reload_SameContent_VanPhatRefreshSignal` — reload nội dung giống hệt → ticket/tín hiệu refresh vẫn đổi và pending caret được giữ cho handler consume (fix finding #15; fail trên thiết kế chỉ dựa PropertyChanged(Text)).
  - **Double-click guard ở UI layer, KHÔNG guard timestamp trên VM** (chốt lại theo finding #10): phương án duy nhất — behavior `PreviewMouseLeftButtonDown` kiểm `e.ClickCount == 2` đánh dấu Handled để Click thứ hai không bắn command; test tự động tương ứng không nằm ở VM (VM luôn chèn mỗi lần gọi — đó là contract đúng của command), proof là smoke gate AC-4 bấm double-click thật đếm snippet xuất hiện đúng một lần.
  - KeyBinding/focus bridge: kiểm chứng bằng inspection XAML+code-behind (AC liệt kê); smoke gate thủ công `dotnet run --project src/DBI.Controller.Studio -- --fake-runtime` bấm Ctrl+T/Ctrl+S/chèn/double-click — ghi nhận trong review.
- Anti-shortcut coverage:
  - Hardcode: test multi-line + CRLF với caret giữa file (không phải đầu/cuối) sẽ fail mọi cài đặt chèn-cố-định-vị-trí hoặc normalize-lộn-xộn.
  - Happy-path-only: case `ReloadAsync` (VM nạp text mới từ đĩa) phải render lại editor — nếu sync chỉ xử lý chèn mà không xử lý set-text chung sẽ lộ qua review obligation; test helper bao phủ offset > length (clamp).
  - Chèn đôi: KHÔNG test tự động được ở tầng Core (VM contract là chèn mỗi lần gọi — đúng nghĩa command); proof duy nhất là smoke gate AC-4 double-click thật đếm đúng 1 snippet. Review đối chiếu obligation guard ClickCount có trong diff.
  - Vòng lặp/race: cài thiếu cờ chống vọng sẽ treo UI ngay khi chạy thật; race init lộ khi gõ phím ngay lúc mở tab — review bắt buộc đối chiếu obligations (không test tự động được ở tầng Core, chấp nhận bằng obligation + inspection + smoke gate).
- Implementation obligations:
  - Tạo `TextPositionMath` thuần ở Studio.Core (test được không cần WPF) — cả hai chiều + clamp; `CodeEditorViewModel.InsertAtCaret` dùng nó thay logic tự tính.
  - Protocol pending-caret như behavioral contract: `CodeEditorViewModel` expose **API public WPF-free** `bool TryTakePendingCaretOffset(out int offset)` — semantics: trả true và xoá pending ở lần gọi đầu, các lần gọi sau đến khi có chèn mới trả false (consume-once); `InsertAtCaret`/`ReloadAsync` là nơi duy nhất set pending. Handler WPF ở Studio assembly gọi API public này — không đụng field internal xuyên assembly.
  - **ReloadAsync same-value hole** (fix finding #15): `ObservableProperty` KHÔNG raise PropertyChanged khi giá trị mới bằng cũ — reload file trùng nội dung hiện tại sẽ bỏ ngỏ pending caret và editor không được nhắc refresh. Obligation: `ReloadAsync` (và mọi đường set-pending ngoài InsertAtCaret) PHẢI phát thông báo tường minh kể cả khi text bằng nhau — cách WPF-free: VM expose thêm event nhẹ `RefreshRequested` (raise trong ReloadAsync sau khi set pending, không điều kiện) hoặc property `int PendingRefreshTicket` tăng 1 mỗi lần reload để handler nghe PropertyChanged(Ticket) luôn bắn; handler WPF nghe cả Text lẫn tín hiệu này. Test Core: reload với nội dung GIỐNG NHAU → ticket/tín hiệu vẫn đổi (assert qua event hoặc ticket), pending offset còn nguyên cho handler consume.
  - Thêm chiều VM→editor trong `RoslynCodeEditorBehaviors`: nghe `PropertyChanged` của `model.Text`, khi đổi: bặt cờ chống vọng, `editor.Text = value`, đặt `CaretOffset` từ pending (nếu có) else clamp giữ caret hiện tại, tắt cờ. Cờ chỉ chặn chiều editor→VM, KHÔNG chặn workspace debounce.
  - Reconciliation sau init: sau await InitializeEditorAsync, so sánh snapshot ban đầu với model.Text hiện tại — khác thì áp text mới nhất + pending caret.
  - Detach: Unloaded gỡ PropertyChanged subscription, PositionChanged, TextChanged, stop timer (mở rộng OnUnloaded hiện có RoslynCodeEditorBehaviors.cs:224-229).
  - Double-click guard: chặn Click thứ hai của double-click qua behavior `PreviewMouseLeftButtonDown` kiểm `e.ClickCount == 2` → Handled — đây là phương án DUY NHẤT (không guard timestamp trên VM); drag path không bị ảnh hưởng (drag đã ngưỡng 4px).
  - CommandBinding/RoutedCommand `FocusToolboxFilter` tại MainWindow (code-behind đã có Descendants helper): **đúng API AvalonDock** — `ToolboxPane` là `LayoutAnchorable` (MainWindow.xaml:572), activation bằng `ToolboxPane.Show()` nếu `IsHidden`/`IsAutoHidden`, sau đó tìm TextBox tên `ToolboxFilterBox` trong visual tree của `ToolboxPane.Content`-hosting element bằng helper Descendants từ `Docking`; nếu template chưa render xong thì defer focus qua `Dispatcher.BeginInvoke(DispatcherPriority.Loaded)`. KHÔNG gán gì vào `Docking.ActiveContent` (đã two-way bind với Editors.ActiveDocument kiểu DocumentViewModelBase — đụng vào là phá selection document). KeyBinding Ctrl+T bind RoutedCommand, Ctrl+S bind SaveAllCommand (grep xác nhận chưa xung đột binding nào).
  - Smoke gate `--fake-runtime`: Ctrl+T reveal+focus, Ctrl+S log "Saved", click chèn 1 lần, double-click chèn đúng 1 lần, kéo tag vào TagTable vẫn map bình thường.
- Acceptance criteria:
  - [x] AC-1: Test `InsertAtCaret_MultiLineSnippet_CaretCuoiDoanChen`, `InsertAtCaret_CRLF_Chuẩn` (CRLF + LF), `InsertAtCaret_CaretNgoaiPhamVi_KhongCrash`, `Reload_SameContent_VanPhatRefreshSignal` pass — proven by focused test run (19/19 TaskCards+TextPositionMath).
  - [x] AC-2: Helper hai chiều `TextPositionMath` pass test LF + CRLF + clamp + round-trip — proven by focused test run.
  - [x] AC-3: Inspection: `RoslynCodeEditorBehaviors` có chiều VM→editor với cờ chống vọng theo mẫu BindableTextEditor; pending-caret protocol; reconciliation sau init; detach đầy đủ ở Unloaded; CommandBinding FocusToolboxFilter + KeyBinding Ctrl+T/Ctrl+S trong MainWindow — proven by code inspection in review.
  - [x] AC-4: Smoke gate `--fake-runtime`: Ctrl+T focus được ô lọc, chèn snippet hiện ngay trên editor đúng vị trí, double-click chỉ chèn một lần, drag vào TagTable vẫn hoạt động — proven by manual run ghi nhận trong review.
  - [x] AC-5: Toàn bộ test suite 275/275 pass (≥258), build 0 warning — proven by global gates.
- Focused verification:
  - `dotnet test DBI.Controller.slnx --nologo --filter "FullyQualifiedName~TaskCardsViewModelTests|FullyQualifiedName~TextPositionMath"`
- Phase gates:
  - `dotnet build DBI.Controller.slnx --nologo`
  - `dotnet test DBI.Controller.slnx --nologo` (+ rerun jitter filter nếu 2 test đó fail)
  - Smoke gate `--fake-runtime` theo AC-4
- Review: run `codex-impl-review` against this phase and this plan; verdict must be APPROVE.
- Commit: `feat(studio): toolbox insert hiển thị thật trên editor — sync hai chiều, caret thật, chèn-đúng-một-lần, Ctrl+T/S`

## Phase 2: Hệ token Visual Studio đầy đủ + style control còn thiếu
- Status: pending
- Depends on: Phase 1
- Goal: Bộ token ~30 key giống Visual Studio cho Light/Dark, mọi control còn template mặc định (ComboBox, ContextMenu, ScrollBar, ToolTip, TreeViewItem, CheckBox/RadioButton) được style đồng bộ, không còn mã hex nào ngoài Themes/, key `MutedColor` tồn tại thật.
- Current behavior: Light/Dark chỉ 13 key. `MutedColor` được 3 VM dùng làm StatusColorKey/ValueBrushKey nhưng không định nghĩa → `ResourceKeyToBrushConverter`/`MappedToBrushConverter` rơi về Transparent/Gray — glyph trạng thái device trong cây project và giá trị watch-stale hiện TRẮNG (vô hình) thay vì xám. ComboBox/ContextMenu/ScrollBar/ToolTip/TreeViewItem expander dùng template Windows mặc định → trắng lóa trong Dark. ListBox (Inspector Information/Diagnostics tab, MainWindow.xaml:93,102) và các Button dựng trực tiếp không qua style cũng lộ template mặc định. Dialog dựng bằng code (ForceSafetyWindow, TextPromptWindow, BlockPromptWindow — UserPrompt.cs) dùng control WPF mặc định nên lệch theme. Màu cứng: force-row `#33C72C3B` (MainWindow.xaml:318), overlay marker `Brushes.DodgerBlue` (RoslynCodeEditorBehaviors.cs:310), drop border `Color.FromRgb(0x00,0x5A,0x9E)` (TagDragDropBehaviors.cs:168), dialog an-toàn-force `Brushes.Firebrick` (UserPrompt.cs:208). Ngoài ra binding qua converter trả brush instance CỤ THỂ — sau toggle theme live, glyph cây project / giá trị watch / status device GIỮ màu theme cũ.
- Code evidence: Light.xaml/Dark.xaml (13 key mỗi file); tham chiếu MutedColor: ProjectTreeViewModel.cs:72-74,135-141, DeviceConfigurationViewModel.cs:28-55, WatchTableViewModel.cs:56; converter fallback Brushes.Transparent (ResourceKeyToBrushConverter.cs Convert); ListBox trần: MainWindow.xaml:93,102; dialog code-created: UserPrompt.cs:103-279 (TextPromptWindow/ForceSafetyWindow/BlockPromptWindow); ThemeService.Apply swap MergedDictionaries (ThemeService.cs:40-55); styles hiện hữu Shared.xaml; watch row triggers MainWindow.xaml:310-326; test pattern VM-key: WatchTableViewModelTests, DeviceConfigurationViewModelTests (assert ValueBrushKey/StatusColorKey là CHUỖI key — không cần WPF).
- Pattern to follow: chính các dictionary Light/Dark hiện tại (key đặt tên ngữ nghĩa, comment tiếng Việt, SolidColorBrush phẳng) + implicit style không x:Key trong Shared.xaml cho control phổ quát (như đã làm với Menu/TreeView/TextBox) + DynamicResource cho mọi brush.
- Pattern fallback: bảng màu VS thật (dark: window/chrome `#1F1F1F`, panel `#252526`, editor `#1E1E1E`, hover `#3E3E40`, selection `#094771`, focus-border `#007ACC`, disabled-fg `#656565`, scrollbar-thumb `#424242`/hover `#686868`, tooltip `#1B1B1A`/fg `#F1F1F1`; light: editor `#F5F5F5`, chrome `#EEEEF2`, hover `#DCEBFC`, selection `#BDD8F1`/text-on `#1E1E1E`, focus-border `#007ACC`, disabled-fg `#A2A4A5`, scrollbar `#C1C1C1`/hover `#A6A6A6`, tooltip `#FFFFFE`/border `#000000`) — không có mẫu nội bộ nào đủ chi tiết hơn.
- Behavioral contract:
  - Cả Light.xaml và Dark.xaml định nghĩa ĐỦ và CÙNG bộ key (parity tuyệt đối — thiếu một key là chỗ đó trống lúc runtime, đã ghi trong comment Dark.xaml).
  - Bộ key mới (thêm vào 13 cũ): `DisabledText`, `InputBorderFocus`, `ScrollbarBg`, `ScrollbarThumb`, `ScrollbarThumbHover`, `TooltipBg`, `TooltipFg`, `TooltipBorder`, `MenuHoverBg`, `SubMenuBg`, `SubMenuBorder`, `ForceRowBg`, `OverlayMarkerBrush`, `DropHighlightBrush`, `MutedColor` (alias của IdleColor — giữ tên vì VM đã dùng). Tổng ~28 key.
  - Giá trị dark bám VS dark, light bám VS light (fallback values ở trên); status colors giữ nguyên (Success/Danger/Warning đã nhất quán 2 theme).
  - Control mới được style: ComboBox + ComboBoxItem + popup/border, ContextMenu + MenuItem (kèm Icon/checkable/submenu hover), ScrollBar vertical+horizontal + Thumb + ScrollViewer, ToolTip, TreeViewItem (expander toggle + selection box), CheckBox/RadioButton box, **ListBox + ListBoxItem** (Inspector Information/Diagnostics đang trần — fix finding #7). Tất cả implicit style trong Shared.xaml, brush chỉ qua DynamicResource, hover/pressed/disabled/keyboard-focus đầy đủ.
  - **Dialog dựng bằng code nhận theme** (fix finding #7): ForceSafetyWindow/TextPromptWindow/BlockPromptWindow gắn `Application.Current.Resources` làm nguồn style — cách đơn giản nhất: các Window này set `Resources.MergedDictionaries` merge Shared.xaml lúc ctor, hoặc chuyển sang XAML-based dialog dùng cùng implicit style. Chọn phương án ít xáo động: giữ code-created nhưng gán tường minh Background/Foreground/Button style từ TryFindResource tại ctor (đủ để hết trắng lóa; không yêu cầu full template).
  - **Button dựng trực tiếp dùng style hiện có** (fix finding #7b): rà các Button không gắn FlatButton/ToolBarIconButton trong MainWindow.xaml (VD nút Deploy đã có inline template — giữ; các nút trong dialog code-created dùng FlatButton qua style gán tay) — mọi Button render được phải thuộc một trong: FlatButton, AccentButton, ToolBarIconButton, ToolboxItemButton, hoặc inline template tường minh. Liệt kê vào Dark checklist.
  - **Binding-qua-converter refresh khi toggle theme live** (fix finding #1, chốt bản khả-dịch theo review round 3): invalidate cache KHÔNG đủ (binding one-way không gọi lại Convert). Bản chốt: viết `ThemeBrushConverter : IMultiValueConverter` MỚI (chấp nhận values = [resource-key chuỗi, theme-version int], trả brush tra từ Application.Current.TryFindResource) — converter cũ IValueConverter không dùng cho MultiBinding. Nguồn version: `ThemeVersion : INotifyPropertyChanged` singleton ở Studio assembly, property `int Version`; XAML khai `<MultiBinding Converter="{StaticResource ThemeBrush}"><Binding Path="StatusColorKey"/><Binding Path="Version" Source="{x:Static local:ThemeVersion.Instance}"/></MultiBinding>`. Khi Apply swap xong → `ThemeVersion.RaiseAll()` tăng Version → WPF đánh giá lại MultiBinding → Convert chạy lại tra brush mới. **Inventory đầy đủ mọi binding qua converter** (rà code): ProjectTree StatusGlyph Foreground (MainWindow.xaml:70), DeviceConfiguration Ellipse Fill (:274), WatchTable ValueText Foreground + row triggers, LogEntry Message Foreground (:36), **Toolbox MapGlyph Fill qua `MappedToBrushConverter` (MainWindow.xaml:170)** —MappedToBrushConverter cũng chuyển sang IMultiValueConverter tương tự hoặc được thay bởi ThemeBrushConverter; RaiseAll() gọi trong `ThemeService.Apply` sau swap. Kiểm chứng smoke gate AC-4.
  - Mọi mã hex trong MainWindow.xaml/RoslynCodeEditorBehaviors.cs/TagDragDropBehaviors.cs/UserPrompt.cs thay bằng tra key qua `Application.Current.TryFindResource` (code-behind) hoặc DynamicResource (XAML); force-row/drop/overlay đổi màu theo theme.
  - Overlay marker: `OverlayMarkerMargin` lấy brush lúc render qua TryFindResource("OverlayMarkerBrush") mỗi lần OnRender (rẻ, không cache cứng) — đổi theme live không cần restart.
  - **MutedColor phải resolve ra brush thật** (fix finding #8): parity test đọc giá trị Color của MutedColor trong CẢ HAI dictionary, assert alpha > 0 và không bằng Transparent (màu idle xám ~#7F8B99/#808080 khớp IdleColor hiện có — khai báo như alias cùng giá trị IdleColor).
- Tests first:
  - `ThemeDictionaryParityTests` (test mới, thuần XML parse không cần WPF): load 2 file Themes/Light.xaml, Themes/Dark.xaml từ path repo, assert tập `x:Key` của 2 file BẰNG NHAU và chứa bộ key bắt buộc (liệt kê 28 key). Test này FAIL hiện tại (thiếu 15 key + MutedColor).
  - **MutedColor resolve test** (fix finding #8): cùng parser, parse thuộc tính Color của `MutedColor` ở cả 2 dictionary — assert alpha > 0 và giá trị khác #00FFFFFF/Transparent. Fail hiện tại (key không tồn tại).
  - Hex-free gate: test quét source `.xaml` ngoài Themes/ + các file .cs đã liệt kê **gồm cả MappedToBrushConverter.cs** (fallback Brushes.Gray hiện tại phải thay token — fix finding #14), regex `#[0-9A-Fa-f]{6}`/`Color.FromRgb`/`Brushes.` (chỉ cho phép Brushes.Transparent fallback của ResourceKeyToBrushConverter/SetDropHighlight-null) → 0 match. Fail hiện tại (3+ chỗ). **Ngoài ra quét cả brush-literal tên** (`Foreground="White"`, `Background="Transparent"`...) trong XAML ngoài Themes/: whitelist tối thiểu `White` trên nền accent (AccentButton, Deploy button, force banner — chữ trắng trên nền màu đậm là chủ ý thiết kế) và placeholder `Transparent` của drop-border; mọi literal khác phải thay token.
- Anti-shortcut coverage:
  - Parity test reject cách thêm key chỉ vào 1 theme (bug kinh điển của swap-MergedDictionaries).
  - MutedColor-resolve test reject alias khai rỗng/Transparent — chỉ chứng minh tên tồn tại là không đủ.
  - Hex-free test reject cách "vá nhanh" giữ nguyên DodgerBlue/hex.
  - Converter-refresh: smoke gate toggle theme Light↔Dark 2 lần rồi đối chiếu glyph cây project/giá trị watch đổi màu theo — cài thiếu sẽ lộ màu cũ đứng yên.
- Implementation obligations:
  - Viết đủ 2 dictionary mới hoàn chỉnh (không patch lẻ); giữ comment phong cách hiện có.
  - Style templates: copy cấu trúc template chuẩn WPF (MSDN default templates) rồi tinh gọn flat — mỗi trigger state (normal/hover/pressed/disabled/focused) map đúng token; ScrollBar phải hoạt động cả track lẫn thumb drag.
  - ToolTip style implicit áp toàn app kể cả tooltip của AvalonDock.
  - Force-row: DataTrigger Setter đổi thành `{DynamicResource ForceRowBg}`.
  - Drop highlight: SetDropHighlight tra `TryFindResource("DropHighlightBrush")`; trả về Transparent khi null (an toàn design-time).
  - Kiểm tra bằng mắt cả 2 theme (run app `--fake-runtime`) — screenshot 2 theme lưu `reports/ui-phase2-*.png` làm bằng chứng review (không commit vào src).
- Acceptance criteria:
  - [ ] AC-1: `ThemeDictionaryParityTests` pass: 2 dictionary cùng bộ key, đủ 28 key bắt buộc — proven by focused test.
  - [ ] AC-2: Hex-free source test pass: 0 mã màu cứng trong XAML ngoài Themes/ và trong các file .cs liệt kê — proven by focused test.
  - [ ] AC-3: MutedColor resolve test pass (alpha > 0, không Transparent, cả 2 theme); watch stale/device glyph tra đúng brush xám — proven by focused test + inspection converter path.
  - [ ] AC-4: Converter-refresh hoạt động: smoke gate toggle theme Light↔Dark 2 lần — glyph cây project, giá trị watch, status device đổi theo theme ngay không restart — proven by manual run ghi nhận trong review.
  - [ ] AC-5: ListBox Inspector tab và 3 dialog code-created hết trắng lóa trong Dark (screenshot `reports/ui-phase2-dark-dialogs.png`) — proven by manual inspection.
  - [ ] AC-6: Build 0 warning + full suite ≥258 — proven by phase gates.
- Focused verification:
  - `dotnet test DBI.Controller.slnx --nologo --filter "FullyQualifiedName~ThemeDictionaryParity"`
- Phase gates:
  - `dotnet build DBI.Controller.slnx --nologo`
  - `dotnet test DBI.Controller.slnx --nologo`
  - Smoke gates toggle-theme + screenshot 2 theme (AC-4/AC-5)
- Review: run `codex-impl-review` against this phase and this plan; verdict must be APPROVE.
- Commit: `feat(studio): hệ token Visual Studio đầy đủ — style control thiếu, dialog theme hoá, converter refresh theo theme`

## Completion Criteria
- [ ] Every phase is complete and committed exactly once.
- [ ] Every acceptance criterion is checked.
- [ ] All global gates pass on final HEAD.
- [ ] Final `codex-impl-review` verdict is APPROVE for the complete plan range.
- [ ] Worktree is clean apart from pre-existing unrelated changes (docs đã xoá chủ ý + .claude/settings.json của user).

## Progress Log
| Phase | Status | Commit | Verification | Review |
|---|---|---|---|---|
| 1 | complete | pending (this commit) | focused 19/19 · full suite 275/275 · build 0/0 · jitter 8/8 khi CPU nhàn | APPROVE (round 8) |
| 2 | pending | N/A | pending | pending |
