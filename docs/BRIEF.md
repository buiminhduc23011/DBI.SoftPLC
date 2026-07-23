# 💡 BRIEF: DBI.Controller (Soft PLC Runtime in C#/.NET)

**Ngày tạo:** 2026-07-23  
**Dự án:** DBI.Controller / DBI.SoftPLC  
**Trạng thái:** Draft / Brainstorm Completed  

---

## 1. VẤN ĐỀ CẦN GIẢI QUYẾT

Lập trình PLC truyền thống (dùng Ladder Logic, Structured Text trên Siemens TIA Portal, Mitsubishi GX Works, Beckhoff TwinCAT):
- **Phụ thuộc chặt vào địa chỉ phần cứng:** Code dính liền với địa chỉ I/O (`M100`, `Q0.0`, `DB20.DBX0.0`, `IW50`). Khi đổi PLC hoặc thiết bị phải sửa toàn bộ code.
- **Thiếu sinh thái phần mềm hiện đại:** Không hỗ trợ Unit Testing chuẩn, không có Dependency Injection (DI), không có Hot Reload, không tích hợp dễ dàng với CI/CD, Git hay Logging/Diagnostics chuẩn phần mềm.
- **Đường cong học tập cao:** Khó thu hút các kỹ sư phần mềm C#/.NET tham gia vào lĩnh vực tự động hóa công nghiệp (OT/Automation).

---

## 2. GIẢI PHÁP ĐỀ XUẤT

**DBI.Controller** - Một **Soft PLC Runtime** viết bằng C#/.NET, đóng vai trò như một **Automation Runtime** chạy trên PC/Edge Device.

- **Triết lý Decoupling:** 
  $$\text{Logic} \longrightarrow \text{IO Object (Abstract)} \longrightarrow \text{Runtime} \longrightarrow \text{Driver} \longrightarrow \text{Device}$$
- Logic điều khiển hoàn toàn độc lập với Driver & Device. Người dùng viết logic với Object C# thuần (`IO.StartButton`, `IO.Conveyor`).
- Runtime chịu trách nhiệm toàn bộ hạ tầng: Scan Engine, Memory Mapping, Driver Loading, Threading, Diagnostics, Logging, Safety Catch.
- Lấy cảm hứng từ **ASP.NET Core Host**: Runtime đóng vai trò Host quản lý vòng đời ứng dụng; Logic người dùng biên dịch ra DLL độc lập; Driver là các Plugin cắm rút linh hoạt.

---

## 3. ĐỐI TƯỢNG SỬ DỤNG

- **Kỹ sư Tự Động Hóa / OT Engineer:** Muốn sử dụng C# để viết các thuật toán điều khiển phức tạp, xử lý dữ liệu, tích hợp hệ thống nhanh chóng.
- **Kỹ sư Phần Mềm / Software Engineer:** Có thể lập trình điều khiển máy móc mà không cần học các ngôn ngữ PLC cổ điển.
- **Nhà tích hợp hệ thống (System Integrator):** Cần kết nối máy móc với Factory I/O, PLC, OPC UA, Modbus, AGV, Camera, Robot trên cùng một nền tảng C# thống nhất.

---

## 4. NGHIÊN CỨU THỊ TRƯỜNG & ĐIỂM KHÁC BIỆT

### Đối thủ & Sản phẩm tương đương:
| Nền tảng | Điểm mạnh | Điểm yếu |
| :--- | :--- | :--- |
| **TwinCAT 3 / CODESYS** | Real-time cứng tốt, tiêu chuẩn công nghiệp | Đắt đỏ, đóng kín, ngôn ngữ IEC 61131-3 cổ điển, Unit Test kém |
| **Node-RED** | Trực quan, nhiều node tích hợp | Dựa trên Event-driven JavaScript, không phù hợp cho Deterministic Scan Cycle |
| **DotNetPLC / Custom C# Scripts** | Dùng C# | Thường là script đơn lẻ, thiếu Memory Image, thiếu Driver Abstraction & Dynamic Mapping |

### Điểm khác biệt độc đáo của DBI.Controller:
1. **Pure C# / .NET 8+ Native:** Tận dụng tối đa hiệu năng của .NET hiện đại.
2. **True Decoupled Architecture:** Logic C# hoàn toàn không chứa địa chỉ IP, địa chỉ Coil/Register hay NodeId.
3. **Studio Drag-Drop Mapping:** Cấu hình liên kết giữa Tag phần cứng và Variable C# bằng Studio trực quan mà không cần sửa/recompile code logic.
4. **Dev Experience Đỉnh Cao:** Full Unit Test Support, Dependency Injection, Hot Reload (không ngắt Runtime), Logging & Diagnostics chuyên nghiệp.

---

## 5. PHÂN CHIA NĂNG LỰC & TÍNH NĂNG (FEATURE SCOPE)

### 🚀 MVP (Giai đoạn 1 - Bắt buộc có):
- [ ] **DBI.Controller.SDK:**
  - Base class `ControllerProgram` (`OnStart`, `Execute`, `OnStop`).
  - Core Automation Primitives: Timers (`Ton`, `Tof`, `Tp`), Counters (`CTU`, `CTD`), Edge Detection (`RisingEdge`, `FallingEdge`).
  - Abstract IO Object Model.
- [ ] **DBI.Controller.Runtime:**
  - High-precision Scan Engine (Scan cycle 20ms).
  - Double/Triple Buffering Memory Image (`InputImage` -> `Logic` -> `OutputImage`).
  - Assembly Load Engine (`AssemblyLoadContext`) hỗ trợ nạp User DLL linh hoạt.
  - Driver Manager & Safety Exception Catching (Dừng an toàn khi DLL crash).
- [ ] **Drivers (MVP):**
  - `SimulationDriver`: Giả lập Input/Output trong bộ nhớ để test logic mà không cần phần cứng.
  - `ModbusDriver` (Modbus TCP): Chuẩn giao tiếp phổ biến nhất.
  - `FactoryIODriver`: Tích hợp giả lập 3D máy móc Factory I/O.
- [ ] **Samples & Testing:**
  - Demo Băng tải (Conveyor), Sorting System trên Factory I/O.
  - Unit Test Suite mẫu bằng xUnit.

### 🎁 Phase 2 (Cải tiến & Nâng cao):
- [ ] **DBI.Controller.Studio (Desktop App - WPF/Avalonia hoặc Web App):**
  - Cấu hình danh sách Devices & Drivers.
  - Drag-drop Tag Mapping giữa Device Tag và `IO` Property.
  - Live Diagnostics (Scan time display, Memory usage, Driver Status).
  - Force I/O (Bật/tắt cưỡng bức I/O để debug).
- [ ] **Hot Reload:**
  - Tự động bù DLL mới biên dịch, Swap Assembly không cần Restart Runtime.
- [ ] **Drivers mở rộng:**
  - `OpcUaDriver` (Client OPC UA).
  - `SiemensDriver` (S7 Protocol cho S7-1200/1500).

### 💭 Backlog (Tương lai):
- [ ] Support Linux RT-PREEMPT kernel cho High-Determinism Real-time.
- [ ] MQTT Driver cho IoT Cloud connectivity.
- [ ] Source Generator cho Compile-time Strongly-typed `IO` Generator.

---

## 6. CẤU TRÚC SOLUTION ĐỀ XUẤT

```
DBI.Controller
├── src
│   ├── DBI.Controller.Core           # Interfaces, Memory Image, Abstractions
│   ├── DBI.Controller.SDK            # ControllerProgram, Timers, Edge, StateMachine
│   ├── DBI.Controller.Runtime        # Scan Engine, Scheduler, Host, Safety Catch
│   ├── DBI.Controller.Studio         # UI App cấu hình Devices & Drag-Drop Mapping
│   ├── DBI.Controller.Diagnostics    # Logging, Scan Time metrics, Performance Counters
│   └── DBI.Controller.Testing        # Mock Drivers & Testing utilities
├── Drivers
│   ├── DBI.Controller.Driver.Simulation
│   ├── DBI.Controller.Driver.Modbus
│   ├── DBI.Controller.Driver.FactoryIO
│   ├── DBI.Controller.Driver.OpcUa
│   └── DBI.Controller.Driver.Siemens
├── Samples
│   ├── Sample.Conveyor
│   ├── Sample.Sorting
│   └── Sample.Palletizer
└── docs
    └── BRIEF.md
```

---

## 7. ƯỚC TÍNH SƠ BỘ & RỦI RO KỸ THUẬT

- **Độ phức tạp:** Trung bình - Khá.
- **Rủi ro kỹ thuật:**
  1. **Determinism trên Windows:** Windows không phải OS Real-Time. 
     * *Giải pháp:* Dùng Win32 High Resolution Timers + Thread Priority Highest + Async Worker I/O để tránh làm nghẽn Scan Engine.
  2. **Memory Leak khi Hot Reload:** 
     * *Giải pháp:* Sử dụng collectible `AssemblyLoadContext` và giải phóng triệt để reference cũ.
  3. **Thread Safety trên Memory Image:** 
     * *Giải pháp:* Cơ chế Memory Snapshot Lock-free (Interlocked Swap) tại đầu và cuối Scan Cycle.

---

## 8. BƯỚC TIẾP THEO

- Chạy lệnh **`/plan`** để tiến hành thiết kế chi tiết kiến trúc lớp (Class Diagram, Sequence Diagram), xây dựng PRD chi tiết và phân chia các task code cụ thể.
