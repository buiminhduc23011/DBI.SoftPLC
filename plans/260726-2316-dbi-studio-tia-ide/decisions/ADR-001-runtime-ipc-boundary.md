# ADR-001: Runtime chạy tiến trình riêng, giao tiếp qua IPC

**Date:** 2026-07-26 | **Status:** ✅ Accepted

## Context

Studio hiện `ProjectReference` thẳng vào `DBI.Controller.Runtime` ([DBI.Controller.Studio.csproj:24](../../../src/DBI.Controller.Studio/DBI.Controller.Studio.csproj)). Nếu chạy `ScanEngine` in-process trong Studio:

1. **GC pause là toàn tiến trình.** WPF rendering + AvalonEdit + Roslyn workspace sinh rác liên tục. Một lần Gen2 GC dừng luôn cả `ScanEngine` thread dù nó đã có `ThreadPriority.Highest`. Chu kỳ 20ms sẽ trượt — đây chính là rủi ro #1 nêu trong [BRIEF.md §7](../../../docs/BRIEF.md).
2. **Studio crash = máy dừng.** Không chấp nhận được trong môi trường sản xuất.
3. **Không tắt được Studio khi máy đang chạy.** Trái với mô hình PLC thật.
4. **Không deploy được từ xa.** Studio ở laptop, Runtime ở edge device trong tủ điện — mô hình phổ biến nhất.

Ba phương án được cân nhắc:
- **A.** In-process, worker thread riêng
- **B.** Tiến trình riêng + IPC ngay từ đầu
- **C.** Hybrid — định nghĩa `IRuntimeConnection`, làm in-process trước, remote sau

## Decision

Chọn **B — tách hẳn hai tiến trình, thiết kế IPC ngay từ đầu.**

- `DBI.Controller.Runtime` trở thành **host thật** (hiện là stub — xem B-4 trong [plan.md](../plan.md)), chạy IPC server.
- Studio là **client thuần**, không giữ tham chiếu object nào của Runtime.
- Contract nằm ở project mới `DBI.Controller.Protocol` (net8.0) — cả hai bên cùng tham chiếu.
- v1 dùng **NamedPipe** (chỉ local). Transport được trừu tượng hoá để phase sau cắm gRPC/TCP cho remote.

```
┌─────────────────┐   NamedPipe    ┌──────────────────┐
│  DBI.Studio     │  JSON frames   │  DBI.Runtime     │
│  (net10-win)    │ ◄────────────► │  (net8.0, exe)   │
│                 │                │                  │
│  IRuntimeClient │                │  IpcServer       │
└─────────────────┘                │  ScanEngine      │
         │                         │  DriverManager   │
         └── ProjectReference ──┐  └──────────────────┘
                                │           │
                    ┌───────────▼───────────▼──┐
                    │  DBI.Controller.Protocol  │
                    │  (DTO + command enum)     │
                    └───────────────────────────┘
```

## Consequences

### Tích cực
- Determinism của scan cycle được bảo vệ khỏi GC/rendering của UI
- Studio crash không dừng máy; tắt Studio máy vẫn chạy
- Mở đường cho remote deploy lên edge device mà **không phải làm lại**
- Ranh giới rõ ràng buộc phải thiết kế API sạch, dễ test (mock `IRuntimeClient`)

### Tiêu cực / chi phí
- **Tốn thêm ~1 tuần** thiết kế protocol trước khi thấy màn hình nào chạy — đây là chi phí đã được người dùng chấp nhận có ý thức
- Debug khó hơn: phải attach 2 tiến trình
- Monitoring có độ trễ IPC; không đọc trực tiếp object được nữa
- Phát sinh việc quản lý vòng đời: Studio phải biết khởi động / dò tìm / kết nối lại Runtime

### Việc bắt buộc kéo theo
- Tạo project mới `src/DBI.Controller.Protocol`
- **Bỏ** `ProjectReference` Studio → Runtime sau phase-02
- User assembly phải target `net8.0` để Runtime (`net8.0`) load được — Studio `net10.0-windows` chỉ biên dịch chứ không thực thi nó

## Phương án bị loại

**A (in-process)** — loại vì phá determinism, mâu thuẫn trực tiếp với giá trị cốt lõi của sản phẩm ("Soft PLC có chu kỳ tất định").

**C (hybrid)** — đây là khuyến nghị ban đầu vì cho kết quả nhìn thấy sớm hơn. Người dùng loại vì không muốn làm hai lần và chấp nhận trả trước chi phí thiết kế.
