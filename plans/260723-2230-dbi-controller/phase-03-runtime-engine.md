# Phase 03: Runtime Scan Engine & Memory Image
Status: 0% ⬜ Pending  
Dependencies: Phase 02  

## Objective
Phát triển **Runtime Engine Core**: Vòng lặp Scan Cycle chuẩn xác (Precision Scan Loop), cơ chế Swap Snapshot Memory Image, Loader nạp User DLL bằng `AssemblyLoadContext`, và Safety Exception Catching.

## Requirements
### Functional
- [ ] **Scan Engine Loop (Scan Cycle 20ms / 10ms):**
  1. `Read Inputs` từ Drivers -> ghi vào `InputImage`.
  2. `Execute User Logic` (`ControllerProgram.Execute()`).
  3. `Write Outputs` từ `OutputImage` -> đẩy sang Drivers.
  4. Sleep/Wait cho hết chu kỳ Scan Cycle.
- [ ] **Lock-free Memory Image Swapping:**
  - Đảm bảo trong suốt quá trình `Execute()`, dữ liệu I/O không bị thay đổi giữa chừng (Snapshot Consistency).
- [ ] **DLL Hot Loader (`PluginLoadContext`):**
  - Đọc và nạp User DLL động từ thư mục chỉ định.
  - Hỗ trợ Unload DLL (`IsCollectible = true`).
- [ ] **Safety Exception Handler:**
  - Nếu User Code xảy ra Unhandled Exception trong `Execute()`, Runtime nhảy về Safe State (Set toàn bộ Output = `False`/`0`), dừng Scan Loop và ghi log khẩn cấp.

### Non-Functional
- [ ] **Scan Time Monitoring:** Đo thời gian Scan Time của từng chu kỳ (Execution Time, Total Scan Time, Max Scan Time, Jitter).
- [ ] **High Precision Timer:** Dùng Win32 `timeBeginPeriod` / `PeriodicTimer` hoặc `Stopwatch` loop để đảm bảo Scan Interval ổn định.

## Implementation Steps
1. [ ] Xây dựng `ScanEngine.cs` trong `DBI.Controller.Runtime`:
   - High Precision Loop `ExecuteCycleAsync`.
2. [ ] Xây dựng `MemoryImageManager.cs` chịu trách nhiệm Double/Triple Buffer Snapshot.
3. [ ] Xây dựng `UserProgramLoader.cs` kế thừa `AssemblyLoadContext` để nạp User DLL.
4. [ ] Xây dựng `DriverManager.cs` để quản lý các Driver đăng ký vào Runtime.
5. [ ] Xây dựng `SafetyCatchManager.cs` làm Safe-State Fallback khi gặp sự cố crash.

## Files to Create/Modify
- `src/DBI.Controller.Runtime/Engine/ScanEngine.cs`
- `src/DBI.Controller.Runtime/Engine/MemoryImageManager.cs`
- `src/DBI.Controller.Runtime/Loading/UserProgramLoader.cs`
- `src/DBI.Controller.Runtime/Drivers/DriverManager.cs`
- `src/DBI.Controller.Runtime/Safety/SafetyCatchManager.cs`
- `src/DBI.Controller.Runtime/Program.cs` (Host entry point)

## Test Criteria
- [ ] Scan Loop chạy ổn định ở chu kỳ 20ms với Jitter < 2ms trên PC thông thường.
- [ ] Khi DLL ném Exception, Runtime ngắt Scan Loop an toàn và không crash ứng dụng Host.

---
Next Phase: [phase-04-drivers.md](file:///c:/Users/ducbu/Documents/GitHub/DBI.SoftPLC/plans/260723-2230-dbi-controller/phase-04-drivers.md)
