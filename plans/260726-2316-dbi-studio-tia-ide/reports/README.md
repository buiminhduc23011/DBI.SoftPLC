# Reports

Nơi lưu kết quả spike, đo đạc và báo cáo nghiệm thu của từng phase.

| File | Sinh ở phase | Nội dung |
|---|---|---|
| `spike-roslynpad.md` | 00 | Kết luận **DÙNG ĐƯỢC / KHÔNG** cho `RoslynPad.Editor` trên net10.0-windows + Roslyn 5.6.0. Quyết định này định đoạt phase-08. |
| `spike-avalondock.md` | 00 | Kết quả theming AvalonDock về phong cách flat industrial, kèm ảnh chụp so sánh |
| `determinism-baseline.md` | 02 | Đo jitter/scan time 60 giây khi có IPC + push loop. Nghiệm thu ADR-001. |
| `mvp-acceptance.md` | 07 | Kịch bản "người dùng mới tạo project băng tải trong 10 phút" |
| `perf-monitoring.md` | 10, 13 | Đo fps / CPU / độ trễ gõ phím khi bật monitoring |
| `integration-e2e.md` | 13 | Kết quả 14 bước kịch bản Factory I/O |

**Quy tắc:** spike thất bại thì phải ghi rõ nguyên nhân và **cập nhật `plan.md`** — không im lặng đi tiếp.
