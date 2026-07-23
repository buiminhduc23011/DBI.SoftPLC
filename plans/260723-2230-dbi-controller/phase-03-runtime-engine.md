# Phase 03: Runtime Scan Engine & Memory Image
Status: ✅ Complete  
Dependencies: Phase 02  

## Objective
Phát triển **Runtime Engine Core**: Vòng lặp Scan Cycle chuẩn xác (Precision Scan Loop), cơ chế Swap Snapshot Memory Image, Loader nạp User DLL bằng `AssemblyLoadContext`, và Safety Exception Catching.

## Requirements
### Functional
- [x] **Scan Engine Loop (Scan Cycle 20ms):**
  1. `Read Inputs` từ Drivers -> ghi vào `InputSnapshot`.
  2. `Execute User Logic` (`ControllerProgram.Execute()`).
  3. `Write Outputs` từ `OutputSnapshot` -> đẩy sang Drivers.
  4. High Precision Sleep/Wait cho hết chu kỳ Scan Cycle 20ms.
- [x] **Lock-free Memory Image Swapping:**
  - Đảm bảo trong suốt quá trình `Execute()`, dữ liệu I/O không bị thay đổi giữa chừng (Snapshot Consistency).
- [x] **DLL Hot Loader (`PluginLoadContext`):**
  - Đọc và nạp User DLL động từ thư mục chỉ định.
  - Hỗ trợ Unload DLL (`IsCollectible = true`).
- [x] **Safety Exception Handler:**
  - Nếu User Code xảy ra Unhandled Exception trong `Execute()`, Runtime nhảy về Safe State (Set toàn bộ Output = `False`/`0`), dừng Scan Loop và ghi log khẩn cấp.

### Non-Functional
- [x] **Scan Time Monitoring:** Đo thời gian Scan Time của từng chu kỳ (Execution Time, Max Scan Time, Jitter).
- [x] **High Precision Timer:** Dùng `Stopwatch` & `ThreadPriority.Highest` để đảm bảo Scan Interval ổn định.

## Implementation Steps
1. [x] Xây dựng `ScanEngine.cs` trong `DBI.Controller.Runtime` với High Precision Loop.
2. [x] Xây dựng `UserProgramLoader.cs` kế thừa `AssemblyLoadContext` để nạp/hủy User DLL.
3. [x] Xây dựng `DriverManager.cs` để quản lý các Driver đăng ký vào Runtime.
4. [x] Xây dựng `SafetyCatchManager.cs` làm Safe-State Fallback khi gặp sự cố crash.

## Files to Create/Modify
- `src/DBI.Controller.Runtime/Engine/ScanEngine.cs`
- `src/DBI.Controller.Runtime/Loading/UserProgramLoader.cs`
- `src/DBI.Controller.Runtime/Drivers/DriverManager.cs`
- `src/DBI.Controller.Runtime/Safety/SafetyCatchManager.cs`
- `src/DBI.Controller.Runtime/Program.cs` (Host entry point)

## Test Criteria
- [x] Compilation `dotnet build` đạt 0 Warnings, 0 Errors.
- [x] Khi DLL ném Exception, Runtime ngắt Scan Loop an toàn và đưa toàn bộ Output về Safe State 0.

---
Next Phase: [phase-04-drivers.md](file:///c:/Users/ducbu/Documents/GitHub/DBI.SoftPLC/plans/260723-2230-dbi-controller/phase-04-drivers.md)
