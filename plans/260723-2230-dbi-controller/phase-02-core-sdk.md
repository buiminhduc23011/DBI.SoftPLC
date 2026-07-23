# Phase 02: Core Interfaces & SDK Primitives
Status: 0% ⬜ Pending  
Dependencies: Phase 01  

## Objective
Xây dựng lớp **Core Interfaces** (`IIOProvider`, `IMemoryImage`, `IDriver`) và **SDK Base Classes** (`ControllerProgram`, Timers, Edge Detectors, Counters) để người dùng viết logic điều khiển C#.

## Requirements
### Functional
- [ ] Define `IIOProvider`: Interface giao tiếp cho các Driver (Read/Write I/O).
- [ ] Define `IMemoryImage`: Interface quản lý Snapshot Input/Output Data Image trong bộ nhớ.
- [ ] Define base class `ControllerProgram`:
  - `virtual void OnStart()`: Gọi 1 lần khi Runtime khởi động.
  - `abstract void Execute()`: Gọi lặp lại mỗi Scan Cycle.
  - `virtual void OnStop()`: Gọi 1 lần khi Runtime dừng.
- [ ] Xây dựng các hàm Automation chuẩn (Industrial Standard Primitives):
  - **Timers:** `Ton` (Timer On Delay), `Tof` (Timer Off Delay), `Tp` (Pulse Timer).
  - **Edge Detectors:** `RisingEdge` (R_TRIG), `FallingEdge` (F_TRIG).
  - **Counters:** `CTU` (Count Up), `CTD` (Count Down).
- [ ] Xây dựng Dynamic/Reflective `IO` Container Model hoặc Dynamic Dynamic Proxy cho `IO.StartButton`.

### Non-Functional
- [ ] Tốc độ truy cập `IO` object và Timers phải đạt hiệu năng cao (< 100ns/op).
- [ ] Viết XML Documentation chi tiết cho tất cả SDK Primitives.

## Implementation Steps
1. [ ] Thêm các interfaces vào `DBI.Controller.Core`:
   - `IDriver.cs`, `IIOProvider.cs`, `IMemoryImage.cs`, `IIOMapping.cs`.
2. [ ] Xây dựng Memory Image Snapshots trong Core:
   - `InputMemoryImage.cs`, `OutputMemoryImage.cs`.
3. [ ] Thêm SDK Base Classes vào `DBI.Controller.SDK`:
   - `ControllerProgram.cs`.
   - `AutomationPrimitives/Ton.cs`, `Tof.cs`, `Tp.cs`.
   - `AutomationPrimitives/RisingEdge.cs`, `FallingEdge.cs`.
   - `AutomationPrimitives/Counter.cs`.
4. [ ] Xây dựng `IOContainer` cho phép map linh hoạt property name -> IO Tag.

## Files to Create/Modify
- `src/DBI.Controller.Core/Interfaces/IDriver.cs`
- `src/DBI.Controller.Core/Interfaces/IIOProvider.cs`
- `src/DBI.Controller.Core/Interfaces/IMemoryImage.cs`
- `src/DBI.Controller.Core/Models/MemorySnapshot.cs`
- `src/DBI.Controller.SDK/ControllerProgram.cs`
- `src/DBI.Controller.SDK/Primitives/Ton.cs`
- `src/DBI.Controller.SDK/Primitives/Tof.cs`
- `src/DBI.Controller.SDK/Primitives/RisingEdge.cs`
- `src/DBI.Controller.SDK/Primitives/Counter.cs`

## Test Criteria
- [ ] Unit Test `Ton`, `Tof`, `RisingEdge` hoạt động chính xác đúng thời điểm milisecond.
- [ ] Base class `ControllerProgram` cho phép inherit và override sạch sẽ.

---
Next Phase: [phase-03-runtime-engine.md](file:///c:/Users/ducbu/Documents/GitHub/DBI.SoftPLC/plans/260723-2230-dbi-controller/phase-03-runtime-engine.md)
