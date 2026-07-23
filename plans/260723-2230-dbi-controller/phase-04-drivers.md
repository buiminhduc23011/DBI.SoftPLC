# Phase 04: MVP Protocol Drivers Implementation
Status: 0% ⬜ Pending  
Dependencies: Phase 03  

## Objective
Phát triển 3 Protocol Drivers đầu tiên cho hệ sinh thái DBI.Controller:
1. **SimulationDriver:** Giả lập I/O trong bộ nhớ để phục vụ Unit Test và Debug không cần thiết bị thật.
2. **ModbusDriver:** Kết nối chuẩn công nghiệp Modbus TCP Client.
3. **FactoryIODriver:** Tích hợp trực tiếp với phần mềm mô phỏng 3D Factory I/O.

## Requirements
### Functional
- [ ] Implement `IDriver` interface cho 3 Driver.
- [ ] **SimulationDriver:**
  - Cho phép set/get value của Digital/Analog Tag trực tiếp từ code hoặc C# Test.
- [ ] **ModbusDriver (Modbus TCP):**
  - Read Coils, Read Discrete Inputs, Read Holding Registers, Read Input Registers.
  - Write Single/Multiple Coils, Write Single/Multiple Registers.
  - Auto-reconnect khi đứt kết nối mạng.
- [ ] **FactoryIODriver:**
  - Giao tiếp với Factory I/O SDK (hoặc Modbus TCP bridge sang Factory I/O).
  - Sync trạng thái Sensor và Actuator 3D realtime.

### Non-Functional
- [ ] Driver hoạt động Asynchronous / Background Worker thread để không làm nghẽn Scan Engine Loop.
- [ ] Quản lý trạng thái kết nối `ConnectionState` (`Connected`, `Disconnected`, `Faulted`).

## Implementation Steps
1. [ ] Code `SimulationDriver` trong `Drivers/DBI.Controller.Driver.Simulation`.
2. [ ] Code `ModbusDriver` trong `Drivers/DBI.Controller.Driver.Modbus` (sử dụng NModbus hoặc FluentModbus).
3. [ ] Code `FactoryIODriver` trong `Drivers/DBI.Controller.Driver.FactoryIO`.
4. [ ] Viết Config Schema cho từng Driver (IP, Port, SlaveID, Tag Address List).

## Files to Create/Modify
- `Drivers/DBI.Controller.Driver.Simulation/SimulationDriver.cs`
- `Drivers/DBI.Controller.Driver.Modbus/ModbusDriver.cs`
- `Drivers/DBI.Controller.Driver.Modbus/Models/ModbusTagConfig.cs`
- `Drivers/DBI.Controller.Driver.FactoryIO/FactoryIODriver.cs`

## Test Criteria
- [ ] `SimulationDriver` phản hồi tức thì với tốc độ < 1ms.
- [ ] `ModbusDriver` giao tiếp đọc/ghi thành công với Modbus Simulator (ví dụ: Modbus Pal / An扉Modbus).
- [ ] `FactoryIODriver` điều khiển được băng tải và đọc được sensor từ Factory I/O.

---
Next Phase: [phase-05-samples-testing.md](file:///c:/Users/ducbu/Documents/GitHub/DBI.SoftPLC/plans/260723-2230-dbi-controller/phase-05-samples-testing.md)
