# Phase 13 — Live Code Overlay & Integration

**Status:** 🟡 Core done; AvalonEdit/Factory I/O acceptance pending | **Phụ thuộc:** phase-08, phase-10 | **Nội dung BRIEF:** #13

> 🎯 Đây là "chế độ kính" (monitoring glasses) của TIA Portal — thứ gây ấn tượng mạnh nhất khi demo.
>
> ⚠️ **Đây cũng là phase có thể cắt.** Phase-10 (Watch Table) đã cho 80% giá trị debug. Nếu ngân sách cạn, cắt phase này không mất tính năng cốt lõi nào.

---

## Task 13.1 — Overlay giá trị inline

```csharp
 1  public override void Execute()
 2  {
 3      if (IO.StartButton)              ⟨TRUE⟩
 4          IO.ConveyorRun = true;       ⟨TRUE⟩
 5
 6      if (IO.StopButton)               ⟨FALSE⟩
 7          IO.ConveyorRun = false;
 8
 9      _delayStop.In = IO.SensorProduct;⟨FALSE⟩
10      if (_delayStop.Q)                ⟨FALSE⟩  ⏱ 0/1000ms
11          IO.ConveyorRun = false;
12  }
```

Kỹ thuật AvalonEdit:
- `IVisualLineTransformer` hoặc `VisualLineElementGenerator` chèn phần tử cuối dòng
- Nền dòng có tag active: xanh nhạt (nhánh đang chạy) — giống TIA tô sáng nhánh đúng

## Task 13.2 — Phân tích vị trí tag trong code

Cần biết dòng nào có tag nào. Hai cách:

| Cách | Điều kiện | Chất lượng |
|---|---|---|
| Roslyn syntax walker tìm `MemberAccessExpression` trên `IO` | phase-08 thành công | Chính xác |
| Regex `IO\.(\w+)` | Phương án lùi | Đủ dùng, sai trong comment/string |

Kết quả: `Dictionary<int line, List<string tagName>>` — cập nhật khi file đổi.

## Task 13.3 — Nối dữ liệu

- Bật monitoring → auto `SubscribeTags` **chỉ những tag xuất hiện trong file đang mở**
- Cuộn màn hình → subscribe/unsubscribe theo vùng nhìn thấy (giảm tải)
- Đóng tab / tắt monitoring → unsubscribe hết

## Task 13.4 — Hiệu năng

Đây là rủi ro chính. Yêu cầu:

| Chỉ tiêu | Ngưỡng |
|---|---|
| Frame rate khi monitoring | ≥ 50fps |
| Tần suất vẽ lại | ≤ 10Hz (không vẽ mỗi message) |
| Độ trễ gõ phím khi bật monitoring | < 80ms |
| CPU Studio | < 20% |

Kỹ thuật: gộp update theo timer 100ms, chỉ vẽ lại dòng trong vùng nhìn thấy, dùng `TextView.Redraw(segment)` thay vì redraw cả file.

## Task 13.5 — Kiểm thử tích hợp toàn hệ thống

Kịch bản end-to-end trên Factory I/O thật:

```
1. Tạo project mới "Conveyor"
2. Thêm device FactoryIO_3D (127.0.0.1:502)
3. Kéo 4 device tag sang Tag Table → StartButton, StopButton, SensorProduct, ConveyorRun
4. Thêm Function Block "ConveyorLogic"
5. Kéo Ton từ Toolbox vào
6. Viết logic (dùng IntelliSense cho IO.*)
7. Gọi từ Main
8. Compile → Deploy
9. Bật monitoring → thấy giá trị chạy inline
10. Bấm nút trong Factory I/O → băng tải chạy
11. Force ConveyorRun = FALSE → băng tải dừng dù logic vẫn TRUE
12. Clear force → băng tải chạy lại
13. Đóng Studio → máy VẪN CHẠY
14. Mở lại Studio → nối lại, thấy đúng trạng thái
```

## Task 13.6 — Tài liệu & bàn giao

- `docs/STUDIO-USER-GUIDE.md` — hướng dẫn theo kịch bản 13.5, có ảnh chụp màn hình
- Cập nhật `README.md`
- Sample project `Samples/Sample.Conveyor.Studio/` mở được ngay

---

## Definition of Done

### Checkpoint 2026-07-28

- [x] Core overlay service maps `IO.<tag>` references to source lines and stores latest pushed values.
- [x] Overlay mapping regression tests pass.
- [x] Initial Studio user guide added.
- [ ] AvalonEdit inline rendering, visible-range subscription and Factory I/O acceptance remain.

- [ ] Overlay hiện giá trị đúng bên phải dòng code
- [ ] Chỉ subscribe tag trong vùng nhìn thấy
- [ ] Tắt monitoring → overlay biến mất sạch, unsubscribe hết
- [ ] Sửa code khi đang monitoring không làm sai lệch overlay
- [ ] **≥ 50fps** khi monitoring file 200 dòng có 30 tag
- [ ] Độ trễ gõ phím < 80ms khi bật monitoring
- [ ] Overlay hoạt động cả khi phase-08 dùng phương án lùi (regex)
- [ ] **Kịch bản 13.5 chạy trọn vẹn 14 bước trên Factory I/O thật**
- [ ] Bước 13 xác nhận: đóng Studio máy vẫn chạy (nghiệm thu ADR-001)
- [ ] `docs/STUDIO-USER-GUIDE.md` hoàn chỉnh, có ảnh
- [ ] Sample project mở được và deploy được ngay
- [ ] Toàn bộ DoD của phase 00–12 đã tick
- [ ] `dotnet build` + `dotnet test` toàn solution xanh
