# DBI.SoftPLC — Hướng dẫn cho AI agent

**Soft PLC Runtime viết bằng C#/.NET** — thay Ladder/ST bằng C# thuần, giữ chu kỳ scan tất định 20ms.

---

## 🚦 Bắt đầu phiên làm việc mới — đọc theo thứ tự này

1. **`.claude/rules/session.json`** — đang làm tới đâu
2. **`plans/260726-2316-dbi-studio-tia-ide/plan.md`** — kế hoạch đang chạy (DBI.Studio)
3. Phase file tương ứng `phase-XX-*.md` — chỉ đọc phase đang làm, đừng nạp hết
4. `.claude/rules/session_log.txt` — 20 dòng cuối

Tài liệu nền (chỉ đọc khi cần): [docs/BRIEF.md](docs/BRIEF.md) · [docs/DESIGN.md](docs/DESIGN.md) · [docs/BRIEF-STUDIO.md](docs/BRIEF-STUDIO.md) · [docs/DESIGN-STUDIO.md](docs/DESIGN-STUDIO.md)

---

## ⚙️ Điều kiện tiên quyết — kiểm tra TRƯỚC khi code

```powershell
# Repo anh em DBI.Drivers PHẢI nằm cạnh repo này
Test-Path ..\DBI.Drivers\DBI.Drivers.Modbus\DBI.Drivers.Modbus.csproj   # phải True

dotnet build DBI.Controller.slnx --nologo    # baseline: 0 Warning, 0 Error
dotnet test  DBI.Controller.slnx --nologo
```

```
GitHub/
├── DBI.SoftPLC/     ← repo này
└── DBI.Drivers/     ← BẮT BUỘC clone cạnh, 4 driver tham chiếu ra đây
```

Thiếu `DBI.Drivers` → restore fail. Đó là lỗi môi trường, không phải lỗi code.

---

## Cấu trúc

Mọi project là **net10.0**, riêng `DBI.Controller.Studio` là `net10.0-windows` (WPF).

| Project | Vai trò |
|---|---|
| `DBI.Controller.Core` | `IMemoryImage`, `IDriver`, `MemorySnapshot`, `TagRoute`, `DeviceSpec` |
| `DBI.Controller.SDK` | `ControllerProgram`, `IOContainer`, Primitives (Ton/Tof/Tp/CTU/CTD/Edge) |
| `DBI.Controller.Protocol` | Contract IPC Studio ↔ Runtime. **Không** tham chiếu Runtime/Studio |
| `DBI.Controller.Runtime` | `RuntimeHost`, `IpcServer`, `ScanEngine`, `DriverManager`, `SafetyCatch` |
| `DBI.Controller.Studio.Core` | Mô hình project, `ProjectService`, validation — **không** phụ thuộc WPF |
| `DBI.Controller.Studio` | WPF IDE kiểu TIA Portal |
| `Drivers/*` | Adapter bọc `DBI.Drivers` |

---

## Quy tắc bất di bất dịch

1. **Driver chỉ được bọc core client từ `DBI.Drivers`.** Cần giao thức mới → viết ở repo `DBI.Drivers` trước, **không** viết giao thức trong repo này.
2. **Không làm gì phá chu kỳ scan 20ms.** Không chạy việc nặng trên scan thread. Không để UI/GC ảnh hưởng Runtime.
3. **Runtime chạy tiến trình riêng** (ADR-001). Studio **không** được `ProjectReference` tới `DBI.Controller.Runtime` sau phase-03.
4. **Đóng gói:** driver DLL đi kèm **Runtime**, không phải Studio.
5. **Roslyn ghim 5.3.0** — `RoslynPad` phụ thuộc đúng version này. Nâng lên sẽ sinh cảnh báo NU1608 và nạp assembly lệch nhau.

---

## ⚠️ Bẫy đã biết trong code hiện tại

| # | Bẫy | Trạng thái |
|---|---|---|
| B-1 | `IOContainer : DynamicObject`, `TryGetMember` **luôn** gọi `GetBool` | ✅ phase-00 gỡ. `IOContainer` là `partial class`, phase-06 sinh property mạnh kiểu. |
| B-2 | 5 driver ép kiểu `is not MemorySnapshot` rồi **im lặng return** khi hỏng | ✅ phase-00 gỡ. API thô lên `IMemoryImage`; test quét metadata canh không tái phát. |
| B-3 | `_intValues`/`_floatValues` **không** double-buffer | ✅ phase-00 gỡ. |
| B-4 | `Program.Main` **không bao giờ** gọi `SetProgram()`/`Start()` | ✅ phase-02 gỡ. `RuntimeHost` + `IpcServer`. |
| B-5 | Không có bảng định tuyến tag→device | ✅ phase-02 gỡ. `TagRoutingTable`, mỗi driver chỉ nhận route của mình. |
| B-6 | 4 driver phần cứng **chỉ hỗ trợ `bool`** | 🟡 Còn lại. Nhưng **hết im lặng**: gặp tag Int/Real thì ghi `LastError` rõ ràng. Task 09.0 xử lý. `SimulationDriver` đã đủ 3 kiểu. |

### Ba điều dễ vấp, đã có test canh

1. **Output không được hoán đổi tham chiếu.** Chỉ input double-buffer bằng `Interlocked.Exchange`. Output là trạng thái *giữ* (latch) — hoán đổi sẽ làm băng tải tự tắt sau một chu kỳ.
2. **`ProtocolJson` phải là camelCase.** Client phân biệt response với push bằng cách dò `requestId` trên `JsonDocument`, phép dò đó phân biệt hoa/thường. Đổi chính sách đặt tên là **mọi lệnh treo vô hạn, không exception**.
3. **`HighResolutionTimerScope` phải bọc vòng scan.** Không có nó, `Thread.Sleep` bị chặn ở độ phân giải timer ~15.6ms của Windows và jitter lên ~6ms.

---

## An toàn máy móc — không thoả hiệp

Đây là phần mềm điều khiển máy công nghiệp. Hai chỗ có thể gây tai nạn vật lý:

- **Force I/O** (phase-11) — ghi đè tín hiệu thật. Dialog xác nhận có checkbox bắt buộc, **không** có "đừng hỏi lại". Fault → xoá sạch force.
- **Autostart** (ADR-005) — máy tự chạy sau mất điện. Hai chốt chặn: không tự chạy sau Fault; tệp phanh tay `.norun`.

Khi sửa hai vùng này: **an toàn thắng tiện lợi**, luôn luôn.

---

## Lệnh hay dùng

```powershell
dotnet build DBI.Controller.slnx --nologo
dotnet test  DBI.Controller.slnx --nologo
dotnet run --project src\DBI.Controller.Studio
dotnet run --project src\DBI.Controller.Runtime
```
