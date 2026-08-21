# Phase 10 — Watch Table & Live Monitoring

**Status:** ✅ Done (UI + VM hoàn thiện 2026-08-21; chưa chạy nghiệm thu tải/determinism trên máy thật) | **Phụ thuộc:** phase-03, phase-06 | **Nội dung BRIEF:** #14

> Đây là phase cho **80% giá trị debug với 20% công sức** so với live overlay (phase-13). Làm trước, và nếu ngân sách hết thì phase-13 có thể cắt.

---

## Task 10.1 — Watch Table editor

```
Watch table_1                        👓 Monitor: ● ON      [+ Add]  [🗑]
┌──────────────┬────────┬──────────────┬──────────────┬─────────────────┐
│ Name         │ Type   │ Address      │ Monitor value│ Modify value    │
├──────────────┼────────┼──────────────┼──────────────┼─────────────────┤
│ StartButton  │ Bool   │ FIO/Input_0  │ ● TRUE       │                 │
│ ConveyorRun  │ Bool   │ FIO/Output_0 │ ● TRUE       │ [FALSE] [Apply] │
│ Temperature  │ Real   │ MB/40001     │   72.4       │ [      ] [Apply]│
│ CycleCounter │ Int    │ —            │   1 284      │ [      ] [Apply]│
└──────────────┴────────┴──────────────┴──────────────┴─────────────────┘
```

- Thêm tag: dropdown lọc theo tên, hoặc kéo từ Tag Table / Project Tree
- Nhiều watch table, mỗi cái một document tab, lưu trong `.dbiproj`
- Nút 👓 bật/tắt monitoring cho table đang mở

## Task 10.2 — Kênh dữ liệu

Dùng `SubscribeTags` / `TagValueChanged` của `IRuntimeClient` (phase-03).

| Yêu cầu | Giá trị |
|---|---|
| Tần suất push | 100ms |
| Chỉ gửi khi | Giá trị **thay đổi** |
| Chỉ gửi tag | Đã `SubscribeTags` |
| Khi đóng tab | `UnsubscribeTags` — không để rò rỉ subscription |

⚠️ **Không được** subscribe toàn bộ tag của project. Project 500 tag × 10Hz = 5000 msg/s làm nghẽn cả IPC lẫn UI thread.

## Task 10.3 — Hiển thị

| Kiểu | Cách hiện |
|---|---|
| `Bool` | Badge `TRUE` xanh `#107C41` / `FALSE` đỏ `#C72C3B` — dùng lại style hiện có |
| `Int` | Số, nhóm hàng nghìn |
| `Real` | 2 chữ số thập phân, đổi được sang khoa học |

- Giá trị vừa đổi: **nháy nền vàng 300ms** rồi tắt dần — mắt bắt được thay đổi
- Mất kết nối: giá trị chuyển xám + biểu tượng ⚠️, **không** xoá về 0 (xoá về 0 gây hiểu nhầm là tín hiệu thật)

## Task 10.4 — Modify value

Ghi một lần vào tag (khác với Force của phase-11 — Force giữ giá trị liên tục).

- Chỉ cho `Output` và `Memory`. Tag `Input` không sửa được (driver ghi đè mỗi chu kỳ) — hiện tooltip giải thích lý do
- Xác nhận nếu Runtime đang `Running`

## Task 10.5 — Chống nghẽn UI

- Gộp update: buffer 100ms rồi cập nhật một lượt, không raise `PropertyChanged` mỗi message
- Watch table > 100 dòng: bật UI virtualization
- Test với 200 tag đang monitor: UI phải giữ 60fps

---

## Definition of Done

### Checkpoint 2026-07-28

- [x] Watch table mở được thành document tab và hiển thị các dòng tag.
- [x] Monitoring subscribe/unsubscribe đúng các tag trong table.
- [x] Giá trị push được format cho Bool/Int/Real; test FakeRuntime pass `1/1`.
- [x] Modify value, flash/stale lifecycle hoàn thiện 2026-08-21 (5 test VM pass); benchmark tải/determinism chưa chạy.

### Checkpoint 2026-08-21

- [x] Tạo/xoá/đổi tên watch table; lưu trong `.dbiproj` (`ProjectService.AddWatchTable/RenameWatchTable/DeleteWatchTable`, ShellViewModel đóng tab khi đổi tên vì ContentId nhúng tên)
- [x] Thêm tag bằng dropdown (`AvailableTags` + `AddSelectedTagCommand`); kéo-thả từ Tag Table **chưa làm**
- [x] Giá trị cập nhật real-time đúng cho cả 3 kiểu (buffer 100ms, flush một lượt — test `FlushPending`)
- [x] Giá trị đổi nháy vàng 300ms (RowStyle trigger `IsFlashing` → nền WarningColor)
- [x] Chỉ subscribe tag đang hiển thị; đóng tab thì unsubscribe (test VM xác nhận; chưa test bằng log phía Runtime thật)
- [x] Mất kết nối → xám + ⚠️, không xoá về 0 (trigger `IsStale` → Opacity 0.55; test `ConnectionLost_RowsGoStale`)
- [x] Kết nối lại → tự subscribe lại (reconnect lifecycle trong WatchTableViewModel)
- [x] Modify value hoạt động cho Output/Memory; chặn Input kèm tooltip giải thích
- [ ] **Test tải:** 200 tag @ 10Hz — UI 60fps, CPU Studio < 15% *(chưa đo)*
- [ ] **Test determinism:** monitoring bật không làm jitter của Runtime tăng quá 0.5ms (bảo vệ ADR-001) *(chưa đo)*
