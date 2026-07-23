# Phase 05: Testing Harness & Sample Projects
Status: ✅ Complete  
Dependencies: Phase 04  

## Objective
Thấu suốt luồng hoạt động end-to-end bằng cách xây dựng **Testing Suite** (`DBI.Controller.Testing`) và dự án **Sample Conveyor Program** mô phỏng hệ thống điều khiển băng tải công nghiệp.

## Requirements
### Functional
- [x] **Test Harness (`DBI.Controller.Testing`):**
  - Cung cấp `TestHost` cho phép nạp User DLL, chạy `Execute()` thủ công hoặc theo timer, và Assert I/O state cực kỳ dễ dàng.
- [x] **Sample.Conveyor Project (`Samples/Sample.Conveyor`):**
  - Viết logic điều khiển Băng tải:
    - Khi `StartButton` = TRUE -> `ConveyorRun` = TRUE.
    - Khi `StopButton` = TRUE -> `ConveyorRun` = FALSE.
    - Khi `SensorProduct` phát hiện hàng dừng sau 1 giây (`Ton` timer delay) -> Tự động dừng băng tải.
- [x] **Unit Tests & Integration Tests (`tests/DBI.Controller.Tests`):**
  - Viết Unit Test cho Timers, Counters, Edge Detectors, Scan Loop, Simulation Driver & End-to-End Conveyor Logic.

## Implementation Steps
1. [x] Code `TestHost.cs` trong `src/DBI.Controller.Testing`.
2. [x] Tạo project `Samples/Sample.Conveyor/ConveyorProgram.cs` kế thừa `ControllerProgram`.
3. [x] Tạo `tests/DBI.Controller.Tests/` chứa toàn bộ Unit Tests & Integration Tests (xUnit).
4. [x] Chạy `dotnet test` nghiệm thu toàn bộ hệ thống (Passed 100%).

## Files to Create/Modify
- `src/DBI.Controller.Testing/TestHost.cs`
- `Samples/Sample.Conveyor/ConveyorProgram.cs`
- `Samples/Sample.Conveyor/Sample.Conveyor.csproj`
- `tests/DBI.Controller.Tests/TimerTests.cs`
- `tests/DBI.Controller.Tests/EdgeTests.cs`
- `tests/DBI.Controller.Tests/ConveyorLogicTests.cs`

## Test Criteria
- [x] Chạy `dotnet test` đạt 100% test cases PASSED (5/5 passed).
- [x] `ConveyorProgram` chạy thành công trên `SimulationDriver` lẫn `TestHost`.

---
Next Phase: [phase-06-studio-diagnostics.md](file:///c:/Users/ducbu/Documents/GitHub/DBI.SoftPLC/plans/260723-2230-dbi-controller/phase-06-studio-diagnostics.md)
