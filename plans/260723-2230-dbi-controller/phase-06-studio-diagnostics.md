# Phase 06: Diagnostics & Studio Configuration GUI (WPF .NET 8)
Status: 0% ⬜ Pending  
Dependencies: Phase 05  

## Objective
Xây dựng module **Diagnostics** (Loggers, Scan Time metrics, Performance Counters) và ứng dụng GUI **DBI.Controller.Studio** sử dụng **WPF (.NET 8)** cho phép cấu hình Devices, Drag-drop Tag Mapping, tích hợp **Built-in C# Code Editor** (Roslyn Compiler + Hot Reload 1-Click) và **TIA Portal-Style Live Code Debugging** ("Glasses / Monitoring Mode").

## Requirements
### Functional
- [ ] **Diagnostics Engine (`DBI.Controller.Diagnostics`):**
  - Ghi log theo chuẩn `ILogger` / Serilog (File log, Console log, SignalR/gRPC stream).
  - Thu thập metrics realtime: Scan Time (ms), Jitter, CPU usage (%), Memory usage (MB), Exception counts, Driver Connection Status.
- [ ] **DBI Studio UI (`DBI.Controller.Studio` - WPF .NET 8):**
  - **Device Configuration Window:** Thêm/sửa/xóa Devices (PLC Siemens, FactoryIO, Modbus TCP, Robot...).
  - **Drag & Drop Tag Mapping:** Kéo thả liên kết giữa Tag phần cứng và Variable `IO` của C# Logic.
  - **Live Monitor & Force I/O:** Hiển thị giá trị I/O realtime và cho phép toggle/force giá trị để debug khẩn cấp.
  - **Built-in C# Code Editor:** 
    - Tích hợp `AvalonEdit` (hoặc `Monaco Editor`) hỗ trợ C# Syntax Highlighting, Auto-complete (IntelliSense).
    - Tích hợp **Roslyn C# Compiler API**: Nút **1-Click "Save & Deploy"** tự biên dịch và Hot Reload code mới vào Runtime không cần ngắt máy.
  - **TIA Portal-Style Live Code Debugging ("Glasses Mode"):**
    - Chế độ **"Go Online / Monitoring"**: Hiển thị giá trị live (`TRUE`/`FALSE` xanh lá, Timer ET counter) trực tiếp cạnh từng dòng lệnh C#.
    - Highlighting đường chạy `if` condition và hỗ trợ C# Breakpoints & Step-by-Step Execution.

## Implementation Steps
1. [ ] Code `DiagnosticsCollector.cs` trong `src/DBI.Controller.Diagnostics`.
2. [ ] Thiết lập IPC / gRPC server trong `Runtime` để truyền nhận stream Diagnostics & Live Code Values.
3. [ ] Khởi tạo project UI `src/DBI.Controller.Studio/DBI.Controller.Studio.csproj` (**WPF .NET 8**).
4. [ ] Khai báo các màn hình WPF XAML: `MainWindow.xaml`, `DeviceManagerView.xaml`, `TagMapperView.xaml`, `CodeEditorView.xaml`, `DiagnosticsView.xaml`.
5. [ ] Tích hợp `AvalonEdit` + `Microsoft.CodeAnalysis.CSharp` (Roslyn) làm C# Code Editor & Compiler Engine.
6. [ ] Implement Live Value Inline Overlay Engine trên WPF Code Editor.

## Files to Create/Modify
- `src/DBI.Controller.Diagnostics/DiagnosticsCollector.cs`
- `src/DBI.Controller.Diagnostics/Models/ScanMetrics.cs`
- `src/DBI.Controller.Studio/DBI.Controller.Studio.csproj` (WPF .NET 8)
- `src/DBI.Controller.Studio/Views/MainWindow.xaml`
- `src/DBI.Controller.Studio/Views/CodeEditorView.xaml`
- `src/DBI.Controller.Studio/ViewModels/CodeEditorViewModel.cs`
- `src/DBI.Controller.Studio/Services/RoslynCompilerService.cs`
- `src/DBI.Controller.Studio/Services/LiveMonitoringService.cs`

## Test Criteria
- [ ] Studio (WPF) kết nối được với Runtime qua gRPC/IPC và stream dữ liệu Scan Time realtime.
- [ ] Viết/sửa code C# trực tiếp trên Studio, bấm "Save & Deploy" -> Roslyn biên dịch thành công & Runtime Hot Reload thành công.
- [ ] Bật chế độ "Go Online" -> Biến C# hiển thị giá trị live `TRUE`/`FALSE` xanh đè lên editor chuẩn TIA Portal.

---
Done all Phases.
