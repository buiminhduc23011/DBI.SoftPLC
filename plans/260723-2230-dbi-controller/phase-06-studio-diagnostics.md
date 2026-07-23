# Phase 06: Diagnostics & Studio Configuration GUI
Status: 0% ⬜ Pending  
Dependencies: Phase 05  

## Objective
Xây dựng module **Diagnostics** (Loggers, Scan Time metrics, Performance Counters) và ứng dụng GUI **DBI.Controller.Studio** cho phép người dùng cấu hình Devices, Drag-drop Mapping Tag, và Monitor/Force I/O Realtime.

## Requirements
### Functional
- [ ] **Diagnostics Engine (`DBI.Controller.Diagnostics`):**
  - Ghi log theo chuẩn `ILogger` / Serilog (File log, Console log, SignalR stream).
  - Thu thập metrics realtime: Scan Time (ms), CPU usage (%), Memory usage (MB), Exception counts, Driver Connection Status.
- [ ] **DBI Studio UI (`DBI.Controller.Studio`):**
  - **Device Configuration:** Thêm/sửa/xóa Devices (PLC1, FactoryIO, Modbus, Robot...).
  - **Drag & Drop Tag Mapping:** Kéo thả liên kết giữa Tag phần cứng và Variable `IO` của C# Logic.
  - **Live Monitor & Force I/O:** Hiển thị giá trị I/O realtime và cho phép toggle/force giá trị để debug khẩn cấp.

## Implementation Steps
1. [ ] Code `DiagnosticsCollector.cs` trong `src/DBI.Controller.Diagnostics`.
2. [ ] Thiết lập IPC / gRPC server trong `Runtime` để gửi diagnostics data sang Studio.
3. [ ] Khởi tạo project UI `src/DBI.Controller.Studio` (WPF hoặc Avalonia UI).
4. [ ] Thiết kế các màn hình: Dashboard Monitor, Device Manager, Tag Mapper, Force I/O.

## Files to Create/Modify
- `src/DBI.Controller.Diagnostics/DiagnosticsCollector.cs`
- `src/DBI.Controller.Diagnostics/Models/ScanMetrics.cs`
- `src/DBI.Controller.Studio/DBI.Controller.Studio.csproj`
- `src/DBI.Controller.Studio/Views/MainWindow.axaml` (hoặc `.xaml`)
- `src/DBI.Controller.Studio/ViewModels/TagMapperViewModel.cs`

## Test Criteria
- [ ] Studio kết nối được với Runtime qua gRPC/IPC và stream dữ liệu Scan Time realtime.
- [ ] Drag-drop Tag mapping lưu thành file config JSON và Runtime load lại không bị lỗi.

---
Done all Phases.
