# Phase 12 — Task Cards & Drag-drop Tag Mapping

**Status:** ⬜ Pending | **Phụ thuộc:** phase-04, phase-06, phase-09 | **Nội dung BRIEF:** #11, #12

> Vùng bên phải của TIA Portal. Đây là chỗ hoàn thiện "cảm giác TIA Portal" và trả nợ cam kết **"Studio Drag-Drop Mapping"** đã ghi trong [BRIEF.md §4.3](../../docs/BRIEF.md).

---

## Task 12.1 — Task Card: Instructions (Toolbox)

Nội dung đọc bằng reflection từ `DBI.Controller.SDK.Primitives`:

```
▾ Timers
   ⏱ Ton   — On-delay timer
   ⏱ Tof   — Off-delay timer
   ⏱ Tp    — Pulse timer
▾ Counters
   🔢 CTU  — Count up
   🔢 CTD  — Count down
▾ Edge Detection
   ↗ RisingEdge
   ↘ FallingEdge
▾ Math
   ➗ Scale — Chuyển đổi thang đo
```

Kéo vào editor → chèn snippet đúng vị trí con trỏ:

```csharp
// Thả Ton vào →
private readonly Ton _timer1 = new(1000);   // ← chèn vào vùng field của class
...
_timer1.In = /* điều kiện */;               // ← chèn tại con trỏ
if (_timer1.Q) { }
```

Chèn field vào đúng chỗ cần phân tích cú pháp — dùng Roslyn syntax tree nếu phase-08 thành công, không thì chèn ngay tại con trỏ và để người dùng tự dời.

Double-click cũng chèn (không phải ai cũng thích kéo-thả).

## Task 12.2 — Task Card: Device Tags

Liệt kê tag **có sẵn trên phần cứng** theo từng device — nguồn để kéo sang Tag Table:

```
▾ 🔌 FactoryIO_3D           🟢
     Input_0    Bool   ○ chưa map
     Input_1    Bool   ● StartButton
     Input_2    Bool   ○ chưa map
     Output_0   Bool   ● ConveyorRun
▾ 🔌 Modbus_IO_Module        🔴
     40001      Real   ○ chưa map
     40002      Real   ○ chưa map
```

Nguồn dữ liệu:
- **Driver có discovery** (FactoryIO, OPC UA): hỏi Runtime lấy danh sách thật
- **Driver không có** (Modbus, Delta): người dùng khai vùng địa chỉ trong Device Config (ví dụ `40001–40020`), Studio sinh danh sách

Đánh dấu ● đã map / ○ chưa map — nhìn phát biết còn thiếu gì.

## Task 12.3 — Drag-drop mapping

| Thao tác | Kết quả |
|---|---|
| Kéo device tag → **dòng trống** Tag Table | Tạo tag mới, tự điền Device + Address + Type, tên gợi ý = `Device_Address` |
| Kéo device tag → **ô Address** của tag có sẵn | Gán lại địa chỉ cho tag đó |
| Kéo device tag → **Watch Table** | Thêm vào watch nếu đã có tag map tới nó |
| Kéo tag từ Tag Table → **Watch Table** | Thêm vào watch |
| Kéo tag từ Tag Table → **code editor** | Chèn `IO.TenTag` tại con trỏ |

Phản hồi thị giác khi kéo: đường viền xanh `#005A9E` ở vùng thả hợp lệ, con trỏ ⃠ ở vùng không hợp lệ.

**Kiểm tra kiểu:** kéo tag `Real` vào ô của tag `Bool` → chặn, tooltip *"Không khớp kiểu: Real ≠ Bool"*.

## Task 12.4 — Task Card đổi theo ngữ cảnh

Giống TIA — nội dung task card phụ thuộc document đang mở:

| Document đang mở | Task cards |
|---|---|
| Code editor | Instructions · Device Tags |
| Tag Table | Device Tags · Data Types |
| Device Config | Driver Catalog |
| Watch / Force Table | Tag Browser |

---

## Definition of Done

- [ ] Task Card Instructions liệt kê đủ primitive từ SDK bằng reflection
- [ ] Kéo primitive vào editor chèn đúng snippet, biên dịch được ngay
- [ ] Double-click primitive cũng chèn được
- [ ] Task Card Device Tags hiển thị theo device, đánh dấu ●/○ đúng
- [ ] Driver có discovery lấy được danh sách tag thật từ Runtime
- [ ] Driver không có discovery: khai vùng địa chỉ trong Device Config → sinh danh sách đúng
- [ ] Cả 5 thao tác kéo-thả ở bảng Task 12.3 hoạt động
- [ ] Kéo sai kiểu bị chặn, có tooltip giải thích
- [ ] Phản hồi thị giác rõ ràng khi kéo
- [ ] Task card đổi theo document đang active
- [ ] Kéo-thả tạo tag mới → `IO.g.cs` sinh lại, IntelliSense thấy tag mới ngay
- [ ] Undo được sau khi kéo-thả nhầm (ít nhất ở cấp Tag Table)
