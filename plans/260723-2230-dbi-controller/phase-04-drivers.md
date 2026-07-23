# Phase 04: DBI.Drivers Adapter Integration & Protocol Drivers
Status: ✅ Complete  
Dependencies: Phase 03  

## Objective
Tích hợp toàn bộ hệ thống Protocol Drivers từ **`DBI.Drivers`** (`C:\Users\ducbu\Documents\GitHub\DBI.Drivers`) vào Soft PLC Engine thông qua Adapter Pattern (`IDriver`).
- **Quy tắc bắt buộc:** 100% Protocol Drivers (Modbus, Delta PLC, Omron PLC...) đều phải sử dụng core client từ `DBI.Drivers`. Nếu cần bổ sung giao thức mới, core client phải được viết trong `DBI.Drivers` trước.

## Driver Suite Matrix
1. **`SimulationDriver`:** Giả lập I/O trong bộ nhớ để phục vụ Unit Test và Debug không cần thiết bị thật.
2. **`ModbusDriver` (DBI.Drivers.Modbus):** Adapter bọc `DBI.Drivers.Modbus` (Modbus TCP/RTU/ASCII Client).
3. **`DeltaPlcDriver` (DBI.Drivers.Delta.PLC):** Adapter bọc `DBI.Drivers.Delta.PLC` (Delta PLC D/M/X/Y/S/C/T Registers).
4. **`OmronPlcDriver` (DBI.Drivers.Omron):** Adapter bọc `DBI.Drivers.Omron` (Omron FINS / HostLink Protocol).
5. **`FactoryIODriver`:** Adapter điều khiển mô phỏng 3D máy móc Factory I/O (qua Modbus TCP của DBI.Drivers.Modbus).

## Requirements
### Functional
- [x] Tham chiếu trực tiếp các project từ `..\DBI.Drivers\`:
  - `DBI.Drivers.Modbus`
  - `DBI.Drivers.Delta.PLC`
  - `DBI.Drivers.Omron`
- [x] Implement `IDriver` interface cho từng Driver Adapter.
- [x] Tích hợp chế độ `AutoReconnect`, `ReconnectInterval`, `MaxRetry` của `DBI.Drivers`.
- [x] Map linh hoạt các loại Register (D, M, X, Y, Coil, HoldingRegister...) vào `MemorySnapshot`.

### Non-Functional
- [x] Driver hoạt động Asynchronous / Background Worker thread để không làm nghẽn Scan Engine Loop (20ms).
- [x] Quản lý trạng thái kết nối `ConnectionState` (`Connected`, `Connecting`, `Disconnected`, `Faulted`).

## Implementation Steps
1. [x] Link `DBI.Drivers.Modbus`, `DBI.Drivers.Delta.PLC`, `DBI.Drivers.Omron` vào Solution `DBI.Controller.slnx`.
2. [x] Code `SimulationDriver` trong `Drivers/DBI.Controller.Driver.Simulation`.
3. [x] Code `ModbusDriverAdapter` trong `Drivers/DBI.Controller.Driver.Modbus` (Adapter bọc `DBI.Drivers.Modbus.TCP.ModbusTCPMaster`).
4. [x] Code `DeltaPlcDriverAdapter` trong `Drivers/DBI.Controller.Driver.Delta` (Adapter bọc `DBI.Drivers.Delta.PLC.DeltaClient`).
5. [x] Code `OmronPlcDriverAdapter` trong `Drivers/DBI.Controller.Driver.Omron` (Adapter bọc `DBI.Drivers.Omron.OmronClient`).
6. [x] Code `FactoryIODriver` trong `Drivers/DBI.Controller.Driver.FactoryIO`.

## Files to Create/Modify
- `Drivers/DBI.Controller.Driver.Simulation/SimulationDriver.cs`
- `Drivers/DBI.Controller.Driver.Modbus/ModbusDriverAdapter.cs`
- `Drivers/DBI.Controller.Driver.Delta/DeltaPlcDriverAdapter.cs`
- `Drivers/DBI.Controller.Driver.Omron/OmronPlcDriverAdapter.cs`
- `Drivers/DBI.Controller.Driver.FactoryIO/FactoryIODriver.cs`

## Test Criteria
- [x] Build thành công 100% khi link với `DBI.Drivers` (0 Warnings, 0 Errors).
- [x] Đọc/ghi I/O thành công qua `ModbusTCPMaster`, `DeltaClient`, `OmronClient`.
- [x] `SimulationDriver` phản hồi tức thì với tốc độ < 1ms.

---
Next Phase: [phase-05-samples-testing.md](file:///c:/Users/ducbu/Documents/GitHub/DBI.SoftPLC/plans/260723-2230-dbi-controller/phase-05-samples-testing.md)
