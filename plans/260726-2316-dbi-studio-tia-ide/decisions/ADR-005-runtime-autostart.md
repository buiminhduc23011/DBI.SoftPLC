# ADR-005: Runtime tự khởi động lại chương trình sau khi restart

**Date:** 2026-07-26 | **Status:** ✅ Accepted
**⚠️ Quyết định có hệ quả an toàn — đọc kỹ mục "Yêu cầu bắt buộc kèm theo".**

## Context

Runtime chạy tiến trình riêng (ADR-001) trên máy tính tủ điện. Sau mất điện / reboot Windows / Windows Update tự khởi động lại — máy có tự chạy tiếp không?

Không có đáp án đúng chung:
- **Dây chuyền liên tục** (lò nung, bơm tuần hoàn, hệ thống làm mát): dừng lâu gây hỏng sản phẩm hoặc hỏng thiết bị. Cần tự chạy.
- **Máy có người thao tác** (máy đóng gói, robot gắp): tự chạy khi có người đang thò tay vào là tai nạn.

PLC công nghiệp thật giải quyết bằng **công tắc cứng RUN/STOP trên thân PLC** — phần mềm không tự quyết được.

## Decision

**Runtime tự khởi động lại chương trình cuối cùng.**

```
Khởi động Runtime
    │
    ├─ Đọc %PROGRAMDATA%/DBI.Runtime/last-deploy/
    │     ├─ program.dll
    │     ├─ manifest.json   (tagRoutes, devices, deployedAt, lastCleanState)
    │     └─ program.sha256
    │
    ├─ ❌ Không có / checksum sai  → state = NoProgram, dừng
    ├─ ❌ lastCleanState = Faulted → state = Stopped  ⚠️ CHỐT CHẶN 1
    ├─ ❌ Có file .norun          → state = Stopped  ⚠️ CHỐT CHẶN 2
    └─ ✅ lastCleanState = Running → Load → Connect drivers → Start
```

### ⚠️ Chốt chặn 1 — Không tự chạy sau khi Fault

Nếu lần dừng trước là do `SafetyCatchManager` bắt exception, Runtime khởi động ở trạng thái `Stopped`, **không** tự chạy.

Lý do: chương trình đã chứng minh là lỗi. Tự chạy lại sẽ fault tiếp → restart → fault → vòng lặp vô tận, mỗi vòng lại giật output một lần. Phải có người xem xét.

`RuntimeHost` ghi `lastCleanState` xuống đĩa **mỗi khi đổi trạng thái**, không phải lúc tắt (mất điện thì không kịp ghi).

### ⚠️ Chốt chặn 2 — Tệp phanh tay

Đặt tệp rỗng tên `.norun` vào thư mục `last-deploy/` → Runtime khởi động ở `Stopped` bất kể mọi thứ.

Dành cho kỹ thuật viên bảo trì: trước khi thò tay vào máy, tạo tệp này. Không cần Studio, không cần biết lập trình — chỉ cần tạo một file rỗng.

### Studio phải hiển thị rõ

| Vị trí | Hiển thị |
|---|---|
| Device Config / Project properties | `☑ Tự khởi động sau khi mất điện` — **hiện trạng thái, không ẩn** |
| Status bar | Biểu tượng ⚡ khi autostart đang bật |
| Sau deploy | Thông báo: *"Chương trình đã lưu. Máy sẽ TỰ CHẠY LẠI sau khi mất điện."* |

## Consequences

### Tích cực
- Dây chuyền liên tục phục hồi mà không cần người trực
- Đúng mô hình PLC công nghiệp ở chế độ RUN
- Runtime không cần Studio để hoạt động — củng cố ADR-001

### Tiêu cực / rủi ro
- 🔴 **Máy có thể tự chuyển động khi không có ai giám sát.** Đây là rủi ro thật, không phải rủi ro lý thuyết.
- Chương trình sai logic (nhưng không ném exception) sẽ tự chạy lại — hai chốt chặn không bắt được loại này
- Thêm trạng thái lưu trên đĩa cần đồng bộ và test kỹ

### 🚨 Yêu cầu bắt buộc kèm theo

**Phần mềm không được là lớp bảo vệ duy nhất.** Hệ thống dùng autostart **bắt buộc** phải có:

1. **Mạch E-stop cứng** — cắt nguồn cơ cấu chấp hành bằng phần cứng, không đi qua phần mềm
2. **Contactor an toàn** — cần tác động chủ ý của người để cấp lại nguồn động lực sau mất điện
3. **Đánh giá rủi ro** theo tiêu chuẩn áp dụng cho loại máy (ISO 13849 / IEC 62061)

Ba mục này **nằm ngoài phạm vi phần mềm** nhưng là điều kiện để quyết định này an toàn. Ghi vào `docs/STUDIO-USER-GUIDE.md` ở phase-13 dưới dạng cảnh báo nổi bật.

## Phương án bị loại

**Không tự chạy** — an toàn nhất, là khuyến nghị ban đầu. Loại vì dây chuyền liên tục sẽ dừng cho tới khi có người tới.

**Cấu hình được, mặc định tắt** — linh hoạt nhất. Loại vì người dùng chọn hành vi nhất quán. *Có thể xem lại nếu sau này cần hỗ trợ cả hai loại máy trên cùng một nền tảng.*
