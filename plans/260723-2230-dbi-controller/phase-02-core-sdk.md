# Phase 02: Core Interfaces & SDK Primitives
Status: ✅ Complete  
Dependencies: Phase 01  

## Objective
Xây dựng lớp **Core Interfaces** (`IIOProvider`, `IMemoryImage`, `IDriver`) và **SDK Base Classes** (`ControllerProgram`, Timers, Edge Detectors, Counters) để người dùng viết logic điều khiển C#.

## Requirements
### Functional
- [x] Define `IDriver`: Interface giao tiếp cho các Driver (Read/Write I/O).
- [x] Define `IMemoryImage`: Interface quản lý Snapshot Input/Output Data Image trong bộ nhớ.
- [x] Define base class `ControllerProgram`:
  - `virtual void OnStart()`: Gọi 1 lần khi Runtime khởi động.
  - `abstract void Execute()`: Gọi lặp lại mỗi Scan Cycle.
  - `virtual void OnStop()`: Gọi 1 lần khi Runtime dừng.
- [x] Xây dựng các hàm Automation chuẩn (Industrial Standard Primitives):
  - **Timers:** `Ton` (Timer On Delay), `Tof` (Timer Off Delay), `Tp` (Pulse Timer).
  - **Edge Detectors:** `RisingEdge` (R_TRIG), `FallingEdge` (F_TRIG).
  - **Counters:** `CounterUp` (CTU).
- [x] Xây dựng `IOContainer` Dynamic Object cho phép truy cập `IO.StartButton` hoặc `IO["StartButton"]`.

### Non-Functional
- [x] Tốc độ truy cập `IO` object và Timers phải đạt hiệu năng cao (< 100ns/op).
- [x] Viết XML Documentation chi tiết cho tất cả SDK Primitives.

## Implementation Steps
1. [x] Thêm các interfaces vào `DBI.Controller.Core`:
   - `IDriver.cs`, `IMemoryImage.cs`, `ConnectionState.cs`.
2. [x] Xây dựng Memory Image Snapshots trong Core:
   - `MemorySnapshot.cs` (Double buffer lock-free).
3. [x] Thêm SDK Base Classes vào `DBI.Controller.SDK`:
   - `ControllerProgram.cs`.
   - `Primitives/Ton.cs`, `Tof.cs`, `Tp.cs`.
   - `Primitives/RisingEdge.cs` (R_TRIG & F_TRIG).
   - `Primitives/Counter.cs`.
4. [x] Xây dựng `IOContainer` cho phép map linh hoạt property name -> IO Tag.

## Files to Create/Modify
- `src/DBI.Controller.Core/Interfaces/IDriver.cs`
- `src/DBI.Controller.Core/Interfaces/IMemoryImage.cs`
- `src/DBI.Controller.Core/Models/MemorySnapshot.cs`
- `src/DBI.Controller.SDK/ControllerProgram.cs`
- `src/DBI.Controller.SDK/IO/IOContainer.cs`
- `src/DBI.Controller.SDK/Primitives/Ton.cs`
- `src/DBI.Controller.SDK/Primitives/Tof.cs`
- `src/DBI.Controller.SDK/Primitives/Tp.cs`
- `src/DBI.Controller.SDK/Primitives/RisingEdge.cs`
- `src/DBI.Controller.SDK/Primitives/Counter.cs`

## Test Criteria
- [x] Compilation `dotnet build` đạt 0 Warnings, 0 Errors.
- [x] Base class `ControllerProgram` cho phép inherit và override sạch sẽ.

---
Next Phase: [phase-03-runtime-engine.md](file:///c:/Users/ducbu/Documents/GitHub/DBI.SoftPLC/plans/260723-2230-dbi-controller/phase-03-runtime-engine.md)
