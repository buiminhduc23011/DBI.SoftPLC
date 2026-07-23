# Phase 05: Testing Harness & Sample Projects
Status: 0% ⬜ Pending  
Dependencies: Phase 04  

## Objective
Thấu suốt luồng hoạt động end-to-end bằng cách xây dựng **Testing Suite** (`DBI.Controller.Testing`) và dự án **Sample Conveyor Program** mô phỏng hệ thống điều khiển băng tải công nghiệp.

## Requirements
### Functional
- [ ] **Test Harness (`DBI.Controller.Testing`):**
  - Cung cấp `TestHost` cho phép nạp User DLL, chạy `Execute()` thủ công hoặc theo timer, và Assert I/O state cực kỳ dễ dàng.
- [ ] **Sample.Conveyor Project (`Samples/Sample.Conveyor`):**
  - Viết logic điều khiển Băng tải:
    - Khi `StartButton` = TRUE -> `Conveyor`Run = TRUE.
    - Khi `StopButton` = TRUE -> `Conveyor`Run = FALSE.
    - Khi `Sensor` phát hiện hàng dừng sau 3 giây (`Ton` timer delay) -> Tự động dừng băng tải.
- [ ] **Unit Tests & Integration Tests:**
  - Viết Unit Test cho Timers, Counters, Scan Loop, Simulation Driver.

## Implementation Steps
1. [ ] Code `TestHost.cs` trong `src/DBI.Controller.Testing`.
2. [ ] Tạo project `Samples/Sample.Conveyor/ConveyorProgram.cs` kế thừa `ControllerProgram`.
3. [ ] Tạo `tests/DBI.Controller.Tests/` chứa toàn bộ Unit Tests & Integration Tests (xUnit).
4. [ ] Chạy `dotnet test` nghiệm thu toàn bộ hệ thống.

## Files to Create/Modify
- `src/DBI.Controller.Testing/TestHost.cs`
- `Samples/Sample.Conveyor/ConveyorProgram.cs`
- `Samples/Sample.Conveyor/Sample.Conveyor.csproj`
- `tests/DBI.Controller.Tests/ScanEngineTests.cs`
- `tests/DBI.Controller.Tests/TimerTests.cs`
- `tests/DBI.Controller.Tests/ConveyorLogicTests.cs`

## Test Criteria
- [ ] Chạy `dotnet test` đạt 100% test cases PASSED.
- [ ] `ConveyorProgram` chạy thành công trên `SimulationDriver` lẫn `FactoryIODriver`.

---
Next Phase: [phase-06-studio-diagnostics.md](file:///c:/Users/ducbu/Documents/GitHub/DBI.SoftPLC/plans/260723-2230-dbi-controller/phase-06-studio-diagnostics.md)
