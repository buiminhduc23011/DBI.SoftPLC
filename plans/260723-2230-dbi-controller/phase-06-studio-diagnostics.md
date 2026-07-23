# Phase 06: Diagnostics & Studio Configuration GUI (WPF .NET 8)
Status: ✅ Complete  
Dependencies: Phase 05  

## Objective
Xây dựng module **Diagnostics** (Loggers, Scan Time metrics, Performance Counters) và ứng dụng GUI **DBI.Controller.Studio** sử dụng **WPF (.NET 8)** cho phép cấu hình Devices, Drag-drop Tag Mapping, tích hợp **Built-in C# Code Editor** (Roslyn Compiler + Hot Reload 1-Click) và **TIA Portal-Style Live Code Debugging** ("Glasses / Monitoring Mode").

## Requirements
### Functional
- [x] **Diagnostics Engine (`DBI.Controller.Diagnostics`):**
  - Ghi log và thu thập metrics realtime: Scan Time (ms), Jitter, CPU usage (%), Memory usage (MB), Exception counts, Driver Connection Status.
- [x] **DBI Studio UI (`DBI.Controller.Studio` - WPF .NET 8):**
  - **Device Configuration Window:** Thêm/sửa/xóa Devices (PLC Siemens, FactoryIO, Modbus TCP, Robot...).
  - **Drag & Drop Tag Mapping:** Kéo thả liên kết giữa Tag phần cứng và Variable `IO` của C# Logic.
  - **Live Monitor & Force I/O:** Hiển thị giá trị I/O realtime và cho phép toggle/force giá trị để debug khẩn cấp.
  - **Built-in C# Code Editor:** 
    - Tích hợp `AvalonEdit` hỗ trợ C# Syntax Highlighting.
    - Tích hợp **Roslyn C# Compiler API**: Nút **1-Click "Save & Deploy"** tự biên dịch in-memory và Hot Reload code mới vào Runtime không cần ngắt máy.
  - **TIA Portal-Style Live Code Debugging ("Glasses Mode"):**
    - Chế độ **"Go Online / Monitoring"**: Hiển thị giá trị live (`TRUE`/`FALSE` xanh lá) trực tiếp cạnh từng dòng lệnh C#.

## Implementation Steps
1. [x] Code `DiagnosticsCollector.cs` trong `src/DBI.Controller.Diagnostics`.
2. [x] Khởi tạo project UI `src/DBI.Controller.Studio/DBI.Controller.Studio.csproj` (**WPF .NET 8**).
3. [x] Khai báo các màn hình WPF XAML: `MainWindow.xaml`, `DeviceManagerView`, `TagMapperView`, `CodeEditorView`.
4. [x] Tích hợp `AvalonEdit` + `Microsoft.CodeAnalysis.CSharp` (Roslyn) làm C# Code Editor & Compiler Engine (`RoslynCompilerService.cs`).
5. [x] Implement Live Value Overlay Engine (`LiveMonitoringService.cs`) cho TIA Portal-style Glasses Mode.

## Files to Create/Modify
- `src/DBI.Controller.Diagnostics/DiagnosticsCollector.cs`
- `src/DBI.Controller.Studio/DBI.Controller.Studio.csproj` (WPF .NET 8)
- `src/DBI.Controller.Studio/MainWindow.xaml`
- `src/DBI.Controller.Studio/MainWindow.xaml.cs`
- `src/DBI.Controller.Studio/ViewModels/MainViewModel.cs`
- `src/DBI.Controller.Studio/Services/RoslynCompilerService.cs`
- `src/DBI.Controller.Studio/Services/LiveMonitoringService.cs`

## Test Criteria
- [x] Studio (WPF) build thành công 100% (0 Errors).
- [x] Code C# in-memory được Roslyn biên dịch thành công.
- [x] TIA Portal-Style Glasses Mode hoạt động chính xác.

---
Done all 6 Phases 100%!

---
Done all Phases.
