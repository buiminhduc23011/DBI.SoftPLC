# 🎨 TECHNICAL DESIGN: DBI.Studio

**Ngày tạo:** 2026-07-26 | **Version:** 1.0
**Dựa trên:** [BRIEF-STUDIO.md](BRIEF-STUDIO.md) · [plan.md](../plans/260726-2316-dbi-studio-tia-ide/plan.md) · [studio_spec.md](specs/studio_spec.md)
**Thiết kế Runtime nền tảng:** [DESIGN.md](DESIGN.md)

> Tài liệu này trả lời **LÀM THẾ NÀO**. Còn **LÀM CÁI GÌ** nằm ở `plan.md`.

---

## 1. KIẾN TRÚC TỔNG QUAN

### 1.1. Bức tranh lớn

```
        MÁY TÍNH KỸ SƯ                          TỦ ĐIỆN / EDGE DEVICE
┌────────────────────────────┐         ┌──────────────────────────────────┐
│  DBI.Studio (net10-win)    │         │  DBI.Runtime (net8, exe)         │
│                            │         │                                  │
│  ┌──────────────────────┐  │ Deploy  │  ┌────────────────────────────┐  │
│  │ Tag Table            │  │────────►│  │ ScanEngine  ⏱ 20ms         │  │
│  │   ↓ sinh IO.g.cs     │  │  (dll)  │  │  1.đọc → 2.chạy → 3.ghi   │  │
│  │ Blocks/*.cs          │  │         │  └────────────────────────────┘  │
│  │   ↓ Roslyn compile   │  │◄────────│  ┌────────────────────────────┐  │
│  │ 📦 UserProgram.dll   │  │ giá trị │  │ MemoryImage                │  │
│  └──────────────────────┘  │   tag   │  └────────────────────────────┘  │
│                            │         │  ┌────────────────────────────┐  │
│  ⚠️ KHÔNG giữ object nào   │         │  │ Drivers → thiết bị thật    │  │
│     của Runtime            │         │  └────────────────────────────┘  │
└────────────────────────────┘         └──────────────────────────────────┘
         ▲                                          ▲
         └──── cùng tham chiếu ───┐   ┌─────────────┘
                    ┌─────────────▼───▼──────────────┐
                    │  DBI.Controller.Protocol       │
                    │  DTO + CommandType (net8.0)    │
                    └────────────────────────────────┘
```

**Giải thích bằng lời thường:** Studio là *bàn làm việc của kỹ sư* — soạn thảo, biên dịch, gửi đi. Runtime là *cái PLC* — nhận chương trình, chạy vòng lặp 20ms không nghỉ. Hai bên nói chuyện qua "bộ đàm" với các câu lệnh thống nhất sẵn (`DBI.Controller.Protocol`).

### 1.2. Vì sao tách hai tiến trình

Nếu chạy chung một tiến trình, mỗi lần .NET dọn rác (**GC**) sẽ đóng băng **toàn bộ** tiến trình — kể cả luồng scan có `ThreadPriority.Highest`. WPF + AvalonEdit + Roslyn sinh rác liên tục, nên chu kỳ 20ms sẽ trượt thường xuyên. Chi tiết: [ADR-001](../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-001-runtime-ipc-boundary.md).

### 1.3. Bản đồ project sau khi xong

```
src/
├── DBI.Controller.Core         net8.0   IMemoryImage, IDriver, MemorySnapshot, IForceLayer
├── DBI.Controller.SDK          net8.0   ControllerProgram, IOContainer (partial), Primitives
├── DBI.Controller.Protocol     net8.0   ➕ MỚI — DTO, CommandType, framing
├── DBI.Controller.Runtime      net8.0   RuntimeHost, IpcServer, ScanEngine, IProgramSwapper
├── DBI.Controller.Diagnostics  net8.0
├── DBI.Controller.Testing      net8.0
└── DBI.Controller.Studio       net10-win  WPF — KHÔNG tham chiếu Runtime
```

⚠️ Sau phase-03, `DBI.Controller.Studio.csproj` **bỏ** `ProjectReference` tới `DBI.Controller.Runtime`. Nếu solution vẫn build xanh thì ranh giới là thật; nếu không thì nó chỉ là hình thức.

---

## 2. THIẾT KẾ DỮ LIỆU

### 2.1. Quan hệ giữa các thực thể

```
┌─────────────────────────────────────────┐
│  📁 DbiProject                          │
│  ├── name, description, schemaVersion   │
│  └── runtime: RuntimeTarget             │
└───┬──────────────┬─────────────┬────────┘
    │ 1 → nhiều    │ 1 → nhiều   │ 1 → nhiều
    ▼              ▼             ▼
┌──────────┐  ┌──────────┐  ┌──────────────┐
│🔌 Device │  │🏷️ TagTable│  │📦 CodeBlock  │
│ name     │  │ name      │  │ name         │
│ driverTy │  └─────┬─────┘  │ kind         │
│ settings │        │1→nhiều │ fileName     │
└────▲─────┘        ▼        └──────┬───────┘
     │        ┌───────────┐         │ 1 ↔ 1
     │  nhiều │ 🏷️ Tag     │         ▼
     └────────┤ name      │   📄 Blocks/X.cs
     tag → 1  │ dataType  │      (file thật)
     device   │ direction │
              │ device ───┘
              │ address   │
              └─────┬─────┘
                    │ tất cả tag
                    ▼
              📄 Generated/IO.g.cs   ← Studio sinh, KHÔNG sửa tay
```

**Đọc sơ đồ:** một project có nhiều thiết bị, nhiều bảng tag, nhiều khối code. Mỗi tag thuộc **một** bảng và trỏ tới **một** thiết bị (trừ tag `Memory` không thuộc thiết bị nào). Mỗi khối code ứng với **một** file `.cs` thật trên đĩa. Toàn bộ tag của mọi bảng gộp lại sinh ra **một** file `IO.g.cs`.

### 2.2. Vì sao `IO.g.cs` phải là code sinh ra

Trước đây `IOContainer` là `DynamicObject` — nghĩa là gõ `IO.` gì cũng được, C# không kiểm tra. Gõ nhầm `IO.StartButtonn` thì chương trình vẫn biên dịch, chạy trả `false` âm thầm, nút Start không bao giờ tác dụng và **không có thông báo lỗi nào**.

Sinh code thật ra thì trình biên dịch mới kiểm tra được:

```
Tag Table                          IO.g.cs sinh ra
┌──────────────┬──────┬────────┐
│ StartButton  │ Bool │ Input  │  →  public bool StartButton => GetBool("StartButton");
│ ConveyorRun  │ Bool │ Output │  →  public bool ConveyorRun { get =>...; set =>...; }
│ Temperature  │ Real │ Input  │  →  public float Temperature => GetFloat("Temperature");
└──────────────┴──────┴────────┘
                                     ↑ Input không có set → ghi vào là lỗi biên dịch
```

Chi tiết: [ADR-002](../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-002-tag-source-of-truth.md).

### 2.3. Schema `.dbiproj`

```jsonc
{
  "schemaVersion": "1.0",
  "name": "MyMachine",
  "description": "Băng tải phân loại",

  "runtime": {
    "transportType": "NamedPipe",      // NamedPipe | Tcp
    "pipeName": "DBI.Runtime",
    "host": "127.0.0.1",
    "port": 5580,
    "scanIntervalMs": 20,
    "autoStart": true                   // ADR-005 — máy tự chạy sau mất điện
  },

  "devices": [{
    "name": "FactoryIO_3D",
    "driverType": "DBI.Controller.Driver.FactoryIO",
    "settings": { "host": "127.0.0.1", "port": "502" },
    "addressRanges": ["Input_0-Input_7", "Output_0-Output_7"]   // cho Task Card Device Tags
  }],

  "tagTables": [{
    "name": "Default Tag Table",
    "tags": [{
      "name": "StartButton",            // C# identifier hợp lệ, duy nhất TOÀN project
      "dataType": "Bool",               // Bool | Int | Real
      "direction": "Input",             // Input | Output | Memory
      "device": "FactoryIO_3D",         // "" nếu direction = Memory
      "address": "Input_0",             // "" nếu direction = Memory
      "comment": "Nút start tủ điện"
    }]
  }],

  "blocks": [
    { "name": "Main",     "kind": "Main",          "fileName": "Blocks/Main.cs",     "comment": "" },
    { "name": "Conveyor", "kind": "FunctionBlock", "fileName": "Blocks/Conveyor.cs", "comment": "" }
  ],

  "watchTables": [
    { "name": "Watch table_1", "tagNames": ["StartButton", "ConveyorRun"] }
  ]
}
```

**Ràng buộc toàn vẹn:**

| # | Luật | Mức |
|---|---|---|
| I-1 | `tag.name` duy nhất **toàn project** (so sánh không phân biệt hoa/thường — `MemorySnapshot` dùng `OrdinalIgnoreCase`) | 🔴 |
| I-2 | `tag.name` là C# identifier hợp lệ, không phải từ khoá | 🔴 |
| I-3 | `direction != Memory` → `device` phải có trong `devices[]`, `address` không rỗng | 🔴 |
| I-4 | Đúng **một** block `kind = Main` | 🔴 |
| I-5 | `block.name` duy nhất; `block.fileName` tồn tại trên đĩa | 🔴 |
| I-6 | `device.name` duy nhất, là C# identifier hợp lệ | 🔴 |
| I-7 | Hai tag cùng `(device, address)` | 🟡 |
| I-8 | `watchTables[].tagNames` phải tồn tại trong tag tables | 🟡 |

### 2.4. Ảnh bộ nhớ trong Runtime (sau khi sửa B-2, B-3)

Hiện tại `bool` có double-buffer nhưng `int`/`float` thì **không** — chúng dùng chung một dictionary, nên driver và logic dẫm chân nhau. Sửa lại cho đồng nhất:

```
                    THIẾT BỊ THẬT
                          │ driver đọc về
                          ▼
   ┌──────────────────────────────────────────┐
   │ InputBuffer      bool[] int[] float[]    │  ← driver ghi vào đây
   └──────────────────┬───────────────────────┘
                      │ SwapInputBuffers()  ← hoán đổi THAM CHIẾU, không copy
                      ▼
   ┌──────────────────────────────────────────┐
   │ InputSnapshot    bool[] int[] float[]    │  ← logic đọc, đứng yên suốt chu kỳ
   └──────────────────┬───────────────────────┘
                      │  🔒 ForceLayer phủ lên (phase-11)
                      ▼
              ControllerProgram.Execute()
                      │
                      ▼
   ┌──────────────────────────────────────────┐
   │ OutputSnapshot   bool[] int[] float[]    │  ← logic ghi vào
   └──────────────────┬───────────────────────┘
                      │ SwapOutputBuffers()
                      ▼
   ┌──────────────────────────────────────────┐
   │ OutputBuffer     bool[] int[] float[]    │  ← driver đọc để gửi xuống
   └──────────────────┬───────────────────────┘
                      │ 🔒 ForceLayer phủ lên
                      ▼
                    THIẾT BỊ THẬT
```

**Vì sao cần hai lớp?** Nếu logic đọc trực tiếp từ chỗ driver đang ghi, thì giữa dòng lệnh thứ nhất và thứ hai của chương trình, giá trị `IO.StartButton` có thể đổi. Logic sẽ thấy tín hiệu **không nhất quán trong cùng một chu kỳ**. Chốt ảnh lại ở đầu chu kỳ thì cả chu kỳ nhìn thấy cùng một bức ảnh — đúng nguyên lý PLC.

**Định tuyến tag → thiết bị (sửa B-5):**

```
TagRoutingTable
┌──────────────┬───────────────┬──────────┬───────────┐
│ Tag          │ Device        │ Address  │ Direction │
├──────────────┼───────────────┼──────────┼───────────┤
│ StartButton  │ FactoryIO_3D  │ Input_0  │ Input     │
│ ConveyorRun  │ FactoryIO_3D  │ Output_0 │ Output    │
│ Temperature  │ Modbus_IO     │ 40001    │ Input     │
└──────────────┴───────────────┴──────────┴───────────┘
         │
         ├─ FactoryIO_3D nhận  → [StartButton, ConveyorRun]
         └─ Modbus_IO nhận     → [Temperature]
```

Trước đây `DriverManager` đưa **cả** ảnh bộ nhớ cho **mọi** driver, mỗi driver tự đoán key nào của mình. Giờ mỗi driver chỉ nhận đúng phần của nó.

⚠️ Đây là **breaking change** trên `IDriver` — phải sửa cả 5 driver trong `Drivers/`.

---

## 3. API CONTRACTS (IPC Studio ↔ Runtime)

**Transport:** NamedPipe · **Framing:** `[4-byte big-endian length][UTF-8 JSON]` · **Protocol version:** 1
**Serialization:** `System.Text.Json`, tên tag dạng **chuỗi** (xem §3.4 về giới hạn quy mô)

### 3.1. Bảng lệnh

| Command | Request | Response | Trạng thái yêu cầu | Mã lỗi | Phase |
|---|---|---|---|---|---|
| `Handshake` | `{studioVersion, protocolVersion}` | `{runtimeVersion, protocolVersion, state}` | bất kỳ | `E_PROTO_MISMATCH` | 02 |
| `GetStatus` | — | `StatusResponse` | đã handshake | — | 02 |
| `Deploy` | `{assemblyBytes, swapMode, tagRoutes[], devices[]}` | `{ok, error?, deployedAt}` | đã handshake | `E_COMPILE`, `E_NO_PROGRAM_CLASS`, `E_DRIVER_INIT`, `E_HOTRELOAD_UNSUPPORTED` | 02, 07 |
| `Start` | — | `{ok, error?}` | `Stopped` | `E_NO_PROGRAM`, `E_FAULTED` | 02 |
| `Stop` | — | `{ok, error?}` | `Running` | — | 02 |
| `Reset` | — | `{ok, error?}` | `Faulted` | — | 02 |
| `SubscribeTags` | `{tagNames[]}` | `{ok, unknownTags[]}` | đã handshake | — | 02, 10 |
| `UnsubscribeTags` | `{tagNames[]}` | `{ok}` | đã handshake | — | 02, 10 |
| `ForceTag` | `{tag, valueJson, enable}` | `{ok, error?}` | đã handshake | `E_TAG_UNKNOWN`, `E_TYPE_MISMATCH` | 11 |
| `GetForceList` | — | `{forces[]}` | đã handshake | — | 11 |
| `GetDeviceStates` | — | `{[{driverId, state, lastError?}]}` | đã handshake | — | 09 |

### 3.2. Kênh push (Runtime → Studio, không cần hỏi)

| Channel | Payload | Tần suất | Điều kiện gửi |
|---|---|---|---|
| `tag` | `{tagName, valueJson, timestampMs}` | 100ms | Đã subscribe **và** giá trị đổi |
| `status` | `StatusResponse` | 500ms | Luôn khi đã kết nối |
| `fault` | `{message, stackTrace, occurredAt}` | tức thì | SafetyCatch kích hoạt |
| `deviceState` | `{driverId, state, lastError?}` | tức thì | Trạng thái driver đổi |

> `fault` là kênh mới — hiện `SafetyCatchManager` chỉ `Console.WriteLine` ([SafetyCatchManager.cs:16-19](../src/DBI.Controller.Runtime/Safety/SafetyCatchManager.cs)), Studio không thấy gì. Phải chuyển sang phát sự kiện có cấu trúc.

### 3.3. Máy trạng thái của Runtime

```
                    ┌─────────────┐
     khởi động ────►│  NoProgram  │
     (không có dll) └──────┬──────┘
                           │ Deploy ok
                           ▼
     Stop  ┌────────────────────────────┐  Start
   ┌───────┤         Stopped            │◄────────┐
   │       └────────────┬───────────────┘         │
   │                    │ Start                   │ Reset
   │                    ▼                         │
   │       ┌────────────────────────────┐         │
   └───────┤         Running            │         │
           └────────────┬───────────────┘         │
                        │ exception trong Execute()│
                        ▼                         │
           ┌────────────────────────────┐         │
           │         Faulted            ├─────────┘
           │  ⚠️ output = 0, force xoá  │
           └────────────────────────────┘
```

**Quy tắc chuyển trạng thái:**
- `Deploy` ở trạng thái nào cũng được → luôn về `Stopped` rồi tự `Start` (cold restart, [ADR-004](../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-004-deploy-cold-restart.md))
- Vào `Faulted`: `ClearAllOutputs()` → `SwapOutputBuffers()` → `WriteOutputsAsync()` → **xoá toàn bộ force**
- `Reset` chỉ đi từ `Faulted` → `Stopped`, **không** tự chạy lại
- `RuntimeHost` ghi `lastCleanState` xuống đĩa **mỗi lần đổi trạng thái**, không phải lúc tắt (mất điện thì không kịp ghi)

### 3.4. ⚠️ Giới hạn quy mô của việc dùng tên tag dạng chuỗi

Quyết định dùng tên tag dạng chuỗi (thay vì số thứ tự) đổi lại sự đơn giản và dễ debug. Cần nói rõ giới hạn:

| Số tag đang monitor | Băng thông @10Hz | Đánh giá |
|---|---|---|
| 50 | ~19 KB/s | ✅ Thoải mái |
| 200 | ~76 KB/s | ✅ Ổn |
| 500 | ~190 KB/s | 🟡 Bắt đầu nặng cho UI thread |
| 1000+ | ~380 KB/s | 🔴 Cần đổi sang số thứ tự |

**Ba biện pháp giảm tải, không cần đổi giao thức:**
1. Chỉ push tag **đã subscribe** — không bao giờ gửi toàn bộ
2. Chỉ push khi giá trị **thật sự đổi**
3. Live overlay (phase-13) chỉ subscribe tag **trong vùng màn hình đang nhìn thấy**

Nếu sau này chạm ngưỡng 1000 tag, đổi sang số thứ tự chỉ động vào lớp transport — DTO đã tách riêng nên UI không phải sửa.

---

## 4. SƠ ĐỒ TUẦN TỰ (SEQUENCE)

### 4.1. Deploy — từ lúc bấm nút tới lúc máy chạy

```
Người dùng   Studio          Compiler      IpcClient   IpcServer   RuntimeHost   Drivers
    │           │                │             │           │            │           │
    │─ F5 ─────►│                │             │           │            │           │
    │           │─ lưu file dirty                          │            │           │
    │           │─ validate project (I-1…I-8)              │            │           │
    │           │   ❌ có lỗi 🔴 → dừng, hiện Error List   │            │           │
    │           │─ sinh IO.g.cs ─►│             │           │            │           │
    │           │─ compile ──────►│             │           │            │           │
    │           │◄─ dll + pdb ────│             │           │            │           │
    │           │                              │           │            │           │
    │◄═ ⚠️ "Máy đang RUN, deploy sẽ DỪNG máy" ═│           │            │           │
    │─ xác nhận ►│                              │           │            │           │
    │           │─ Deploy(dll, routes, devices)►│──────────►│            │           │
    │           │                              │           │─ Stop ────►│           │
    │           │                              │           │            │─ output=0 ►│
    │           │                              │           │            │─ ghi xuống►│
    │           │                              │           │            │─ Unload    │
    │           │                              │           │            │─ Load dll  │
    │           │                              │           │            │─ nạp routes│
    │           │                              │           │            │─ Connect ─►│
    │           │                              │           │            │─ OnStart() │
    │           │                              │           │            │─ Start     │
    │           │◄─ {ok, deployedAt} ──────────│◄──────────│            │           │
    │           │─ lưu last-deploy/ xuống đĩa (ADR-005)    │            │           │
    │◄─ "Deployed. Máy sẽ TỰ CHẠY sau mất điện" ─          │            │           │
    │           │                              │           │            │           │
    │           │◄════ push status 500ms ══════│◄══════════│◄───────────│           │
```

**Điểm quan trọng:** thứ tự khi Stop là `ClearAllOutputs()` → `SwapOutputBuffers()` → `WriteOutputsAsync()` **rồi mới** unload. Phải xả output xuống **thiết bị thật** trước, không chỉ xoá trong bộ nhớ — nếu không, băng tải vẫn quay trong lúc nạp chương trình mới.

### 4.2. Chu kỳ scan (có Force)

```
   ┌──────────────────── mỗi 20ms ────────────────────┐
   │                                                   │
   │  1. DriverManager.ReadInputsAsync(myRoutes)      │  ← chỉ route của từng driver
   │  2. SwapInputBuffers()                           │  ← hoán đổi tham chiếu
   │  3. 🔒 ForceLayer.ApplyToInputs()                │  ← force ghi đè input
   │  4. program.Execute()          ⚠️ try/catch      │
   │  5. SwapOutputBuffers()                          │
   │  6. 🔒 ForceLayer.ApplyToOutputs()               │  ← force thắng logic
   │  7. DriverManager.WriteOutputsAsync(myRoutes)    │
   │  8. cập nhật Metrics                             │
   │  9. chờ tới deadline tuyệt đối                   │  ← không dùng Thread.Sleep(int)
   │                                                   │
   └───────────────────────────────────────────────────┘
              │
              │ nếu Execute() ném exception
              ▼
   ┌───────────────────────────────────────────────────┐
   │  SafetyCatch:                                     │
   │   ClearAllOutputs → Swap → ghi xuống thiết bị     │
   │   xoá TOÀN BỘ force                               │
   │   ghi lastCleanState = Faulted xuống đĩa          │
   │   push kênh `fault` lên Studio                    │
   │   state = Faulted, dừng vòng lặp                  │
   └───────────────────────────────────────────────────┘
```

**Sửa metric (hiện đang sai):**

| Hiện tại | Vấn đề | Sửa |
|---|---|---|
| `JitterMs = \|elapsed − 20\|` | Đó là *thời gian thực thi lệch*, không phải jitter | Jitter = độ lệch giữa hai **thời điểm bắt đầu** chu kỳ liên tiếp |
| `Thread.Sleep(20 − (int)elapsed)` | Ép `int` cắt cụt thập phân → sai tới 1ms mỗi chu kỳ | Deadline tuyệt đối + `SpinWait` cho phần dư < 1ms |
| `Metrics` là class mutable | Thread khác đọc → torn read | Trả `record` bất biến |

### 4.3. Runtime khởi động lại sau mất điện (ADR-005)

```
   Có điện → Windows boot → DBI.Runtime.exe khởi động
        │
        ├─ đọc %PROGRAMDATA%/DBI.Runtime/last-deploy/
        │
        ├─ ❌ không có file / sha256 sai   → NoProgram, dừng
        │
        ├─ ❌ tồn tại file `.norun`         → Stopped   ⚠️ CHỐT CHẶN 2 (phanh tay)
        │      (kỹ thuật viên tạo trước khi thò tay vào máy)
        │
        ├─ ❌ lastCleanState = Faulted      → Stopped   ⚠️ CHỐT CHẶN 1
        │      (chương trình đã chứng minh là lỗi — không lặp vô tận)
        │
        └─ ✅ lastCleanState = Running
               → Load dll → nạp routes → Connect drivers → OnStart() → Start
               → 🏭 MÁY TỰ CHẠY
```

> 🚨 **Yêu cầu bắt buộc:** hệ thống dùng autostart phải có **mạch E-stop cứng** và **contactor an toàn** bên ngoài phần mềm. Phần mềm không được là lớp bảo vệ duy nhất. Xem [ADR-005](../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-005-runtime-autostart.md).

### 4.4. Monitoring (Watch Table)

```
Studio                          IpcServer                RuntimeHost
  │                                 │                         │
  │─ SubscribeTags([A,B,C]) ───────►│                         │
  │◄─ {ok, unknownTags:[]} ─────────│                         │
  │                                 │                         │
  │                                 │  ┌─ Task riêng, 100ms ─┐│
  │                                 │  │  SnapshotAll()      ││ ⚠️ KHÔNG chạy
  │                                 │  │  so với lần trước   ││   trên scan thread
  │                                 │  │  chỉ lấy tag đổi    ││
  │◄══ push `tag` [{B, 72.4}] ══════│  └─────────────────────┘│
  │                                 │                         │
  │─ gộp buffer 100ms rồi mới       │                         │
  │  raise PropertyChanged một lượt │                         │
  │                                 │                         │
  │─ đóng tab → UnsubscribeTags ───►│                         │
```

**Hai chỗ dễ sai:**
1. Push loop **không được** chạy trên scan thread — làm vậy là phá chính lý do tồn tại của ADR-001
2. Studio **không được** raise `PropertyChanged` cho mỗi message — 200 tag × 10Hz = 2000 lần/giây sẽ làm treo UI. Phải gộp buffer.

---

## 5. DANH SÁCH MÀN HÌNH

### 5.1. Shell chính

```
┌───────────────────────────────────────────────────────────┐
│ File  Edit  Project  Online  View  Help                   │
│ 🆕 📂 💾 │ 🔨Compile 🚀Deploy │ ▶Start ⏹Stop 🔄Reset │ 👓 │
├──────────┬────────────────────────────────┬───────────────┤
│ Project  │  Main.cs ×  │ Tags ×  │ Watch ×│  Task Cards   │
│  tree    │                                │  ▾ Toolbox    │
│          │  vùng làm việc                 │    Ton Tof Tp │
│  260px   │  (nhiều document tab)          │  ▾ Device Tags│
│          │                                │    240px      │
├──────────┴────────────────────────────────┴───────────────┤
│ Properties │ Information │ Diagnostics       (180px)      │
├───────────────────────────────────────────────────────────┤
│ 🟢RUN │Scan 1.2ms│Jitter 0.1ms│⚡Auto│⚠️3 FORCE│MyMachine │
└───────────────────────────────────────────────────────────┘
```

### 5.2. Bảng màn hình

| # | Màn hình | Vùng | Mục đích | Thao tác chính | Phase |
|---|---|---|---|---|---|
| S-01 | Project Tree | Trái | Điều hướng toàn project | Double-click mở · chuột phải thêm/xoá/đổi tên | 05 |
| S-02 | Code Editor | Giữa | Soạn thảo C# | Gõ code · IntelliSense · overlay giá trị | 04, 08, 13 |
| S-03 | Tag Table | Giữa | Khai báo tag | Sửa inline · thêm/xoá · kéo-thả từ Device Tags | 06 |
| S-04 | Device Config | Giữa | Cấu hình thiết bị | CRUD · Test Connection | 09 |
| S-05 | Watch Table | Giữa | Xem giá trị real-time | Thêm tag · Modify value | 10 |
| S-06 | Force Table | Giữa | Cưỡng bức I/O | Force · Clear · Clear All | 11 |
| S-07 | Inspector → Properties | Dưới | Thuộc tính node đang chọn | Sửa tên, comment | 04 |
| S-08 | Inspector → Information | Dưới | Lỗi biên dịch, validation | Double-click nhảy tới dòng | 04, 07 |
| S-09 | Inspector → Diagnostics | Dưới | Trạng thái Runtime, driver, fault | Xem stack trace | 04, 09 |
| S-10 | Task Card → Toolbox | Phải | Chèn primitive | Kéo-thả · double-click | 12 |
| S-11 | Task Card → Device Tags | Phải | Tag có sẵn trên phần cứng | Kéo sang Tag Table | 12 |
| S-12 | Status Bar | Dưới cùng | Trạng thái tổng thể | — | 04 |

### 5.3. Hộp thoại

| # | Hộp thoại | Khi nào | Điểm cần chú ý | Phase |
|---|---|---|---|---|
| D-01 | New Project | File → New | Chọn thư mục, tên | 01 |
| D-02 | Add New Block | Chuột phải Program Blocks | `Main` vô hiệu nếu đã có | 05 |
| D-03 | Add Device | Chuột phải Devices | Form tham số sinh động theo driver + Test Connection | 09 |
| D-04 | ⚠️ Deploy khi Running | Bấm Deploy lúc máy chạy | Nói rõ **output sẽ về 0** | 07 |
| D-05 | ⚠️ Xác nhận Force | Tạo force | Checkbox bắt buộc, **không** có "đừng hỏi lại" | 11 |
| D-06 | ⚠️ Đóng Studio khi còn Force | Đóng app | 3 lựa chọn: xoá force / giữ / huỷ | 11 |
| D-07 | ⚠️ Đóng Studio khi Runtime Running | Đóng app | Báo rõ máy vẫn chạy | 03 |
| D-08 | Xác nhận xoá | Xoá block/tag/device | Liệt kê thứ sẽ hỏng theo | 05, 09 |

---

## 6. HÀNH TRÌNH NGƯỜI DÙNG

### 6.1. Kỹ sư lần đầu — từ zero tới máy chạy (mục tiêu ≤ 10 phút)

```
1️⃣ Mở Studio ──► 2️⃣ File → New Project "Conveyor"
                        │
                        ▼
3️⃣ Chuột phải Devices → Add Device
      chọn FactoryIO · 127.0.0.1:502 · [Test Connection] ✅
                        │
                        ▼
4️⃣ Mở Default Tag Table
      kéo Input_0, Input_1, Output_0 từ Task Card sang bảng
      đổi tên → StartButton, StopButton, ConveyorRun
                        │  (Studio âm thầm sinh IO.g.cs)
                        ▼
5️⃣ Mở Main.cs → gõ "IO." → 💡 gợi ý hiện 3 tag vừa khai
      viết logic 5 dòng
                        │
                        ▼
6️⃣ Ctrl+Shift+B ──► ✅ Build thành công
                        │
                        ▼
7️⃣ F5 ──► Studio tự khởi động Runtime → Deploy → 🟢 RUN
                        │
                        ▼
8️⃣ Bấm nút trong Factory I/O ──► 🏭 băng tải chạy
```

### 6.2. Debug khi máy chạy sai

```
Máy không chạy như mong đợi
        │
        ├─► Mở Watch Table, thêm các tag liên quan
        │        │
        │        ├─ StartButton = FALSE?  ──► lỗi phần cứng / sai địa chỉ
        │        └─ StartButton = TRUE nhưng ConveyorRun = FALSE ──► lỗi logic
        │
        ├─► Bật 👓 trên Main.cs ──► thấy giá trị ngay cạnh dòng code
        │        └─ nhìn ra nhánh if nào không vào được
        │
        └─► Nghi ngờ cơ cấu chấp hành hỏng?
                 └─► Force ConveyorRun = TRUE
                          ├─ băng tải quay  ──► cơ cấu OK, lỗi ở logic
                          └─ không quay     ──► lỗi phần cứng
                     ⚠️ Clear force ngay sau khi test xong
```

### 6.3. Sửa chương trình đang chạy sản xuất

```
Cần sửa logic ──► Mở project, sửa code
                        │
                        ▼
              Ctrl+Shift+B kiểm tra biên dịch
                        │
                        ▼
              F5 ──► ⚠️ "Máy đang RUN. Deploy sẽ DỪNG máy,
                          output về 0 trong ~300ms."
                        │
                   ┌────┴────┐
              [Huỷ]│         │[Deploy]
                   │         ▼
                   │    Dừng → nạp → chạy lại
                   │         │
                   │         ▼
                   │    Kiểm tra bằng Watch Table
                   ▼
        Đợi tới ca nghỉ rồi deploy
```

### 6.4. Mất điện rồi có điện lại

```
⚡ Mất điện ──► Runtime tắt đột ngột (lastCleanState đã ghi từ trước = Running)
                        │
⚡ Có điện ──► Windows boot ──► DBI.Runtime.exe tự khởi động
                        │
                        ├─ có file .norun?      ──► ⏸ Stopped, chờ người
                        ├─ lastCleanState=Faulted ──► ⏸ Stopped, chờ người
                        └─ lastCleanState=Running ──► 🏭 TỰ CHẠY LẠI
                                    │
                        Kỹ sư mở Studio ──► kết nối ──► thấy 🟢 RUN
```

---

## 7. TIÊU CHÍ NGHIỆM THU

### F-01 — Project Tree (phase-05)

```
✅ Cơ bản
   □ Cây hiện đủ nhóm: Device Config, Online & Diagnostics,
     Program Blocks, PLC Tags, Watch & Force, Devices
   □ Nhóm rỗng vẫn hiện (giống TIA — để người dùng biết chỗ thêm)
   □ Double-click block mở đúng tab, mở lại không tạo tab trùng
   □ Icon phân biệt Main ▶️ / FB 🔷 / FC 🔶 / DB 🗄️

✅ Nâng cao
   □ Add block sinh file .cs biên dịch được ngay (cả 4 loại)
   □ Không tạo được Main thứ hai
   □ Rename đổi cả tên file, tên class, và .dbiproj
   □ FileSystemWatcher bắt được sửa đổi ngoài Studio

✅ Trải nghiệm
   □ Trạng thái mở rộng được lưu và khôi phục
   □ Project 20 block thao tác không giật
```

### F-02 — Tag Table & Code Generator (phase-06)

```
✅ Cơ bản
   □ Sửa inline được cả 6 cột
   □ Dropdown Device nạp từ danh sách thật
   □ Direction = Memory thì ô Device/Address bị vô hiệu hoá

✅ Nâng cao
   □ 8 luật toàn vẹn I-1…I-8 chạy real-time, ô sai viền đỏ + tooltip
   □ IO.g.cs sinh đúng cho 9 tổ hợp (3 kiểu × 3 direction)
   □ Tag Input KHÔNG có setter
   □ Comment thành XML doc comment
   □ IO.g.cs chỉ ghi lại khi nội dung thật sự đổi

✅ Trải nghiệm
   □ IO.g.cs read-only trong editor, có banner + nút "Đi tới Tag Table"
   □ Đổi Address KHÔNG làm IO.g.cs đổi  ← lời hứa cốt lõi của sản phẩm
```

### F-03 — Compile & Deploy (phase-07)

```
✅ Cơ bản
   □ Compile nhiều file .cs thành một assembly
   □ Assembly target net8.0, Runtime net8.0 nạp được
   □ Diagnostics ánh xạ đúng file + số dòng

✅ Nâng cao
   □ Không lẫn reference BCL net10 vào assembly net8
   □ PDB embed → fault cho stack trace có số dòng đúng
   □ Deploy code lỗi → Runtime giữ chương trình cũ, không rơi vào trạng thái lạ
   □ Cảnh báo D-04 hiện khi deploy lúc Running

✅ Trải nghiệm
   □ Build chạy nền, UI không treo (test với 20 block)
   □ Ctrl+Shift+B / F5 hoạt động
   □ 🏁 Người mới tạo được project băng tải chạy trong 10 phút
```

### F-04 — Monitoring (phase-10, 13)

```
✅ Cơ bản
   □ Giá trị cập nhật real-time đúng cả 3 kiểu
   □ Bool hiện badge màu, Int/Real hiện số

✅ Nâng cao
   □ Chỉ subscribe tag đang hiển thị; đóng tab thì unsubscribe
   □ Mất kết nối → xám + ⚠️, KHÔNG xoá về 0
   □ Kết nối lại → tự subscribe lại

✅ Trải nghiệm
   □ Giá trị đổi nháy vàng 300ms
   □ 200 tag @10Hz — UI 60fps, CPU Studio < 15%
   □ Monitoring bật không làm jitter Runtime tăng quá 0.5ms
```

### F-05 — Force I/O (phase-11)

```
✅ Cơ bản
   □ Force áp dụng đúng cả chiều đọc Input và ghi Output
   □ Clear từng cái và Clear All hoạt động

✅ An toàn (bắt buộc)
   □ SafetyCatch kích hoạt → xoá TOÀN BỘ force
   □ Deploy mới → xoá toàn bộ force
   □ Force KHÔNG được ghi vào .dbiproj
   □ Dialog D-05 có checkbox bắt buộc, không có "đừng hỏi lại"
   □ Banner đỏ ở status bar khi có force
   □ D-06 cảnh báo khi đóng Studio còn force

✅ Trải nghiệm
   □ Watch Table đánh dấu 🔒 dòng bị force
   □ Mất kết nối Studio → force vẫn giữ ở Runtime (đúng ngữ nghĩa PLC)
```

---

## 8. TEST CASES (viết TRƯỚC khi code)

### Nhóm A — Nền tảng (phase-00)

| ID | Given | When | Then |
|---|---|---|---|
| TC-A01 | Tag `Temperature` kiểu `Real` = 72.4 | Đọc `IO.Temperature` | Trả về `float` 72.4 — **không** phải `bool` (bug B-1 chết) |
| TC-A02 | `IOContainer` đã bỏ `DynamicObject` | Code gõ `IO.KhongTonTai` | **Lỗi biên dịch**, không phải trả `false` âm thầm |
| TC-A03 | Đang trong `Execute()`, driver ghi `SetRawInput("A", true)` | Đọc `IO.A` trong cùng chu kỳ | Vẫn thấy giá trị **cũ** — snapshot đứng yên |
| TC-A04 | Như TC-A03 nhưng tag kiểu `Int` | Đọc trong cùng chu kỳ | Vẫn thấy giá trị cũ (bug B-3 chết) |
| TC-A05 | Driver nhận `IMemoryImage` | Gọi `SetRawInput` | Ghi vào InputBuffer, **không** phải OutputSnapshot (bug B-2 chết) |

### Nhóm B — Project & Tag (phase-01, 06)

| ID | Given | When | Then |
|---|---|---|---|
| TC-B01 | Project có tag `StartButton` | Thêm tag `startbutton` | 🔴 Lỗi I-1 "Đã có tag tên này" (không phân biệt hoa/thường) |
| TC-B02 | Bảng tag trống | Thêm tag tên `class` | 🔴 Lỗi I-2 "`class` là từ khoá C#" |
| TC-B03 | Tag `Direction = Input` | Code gán `IO.StartButton = true` | **Lỗi biên dịch** — Input không có setter |
| TC-B04 | Tag `ConveyorRun` map tới `Output_0` | Đổi Address thành `Output_5` | `IO.g.cs` **không đổi** — code logic độc lập với địa chỉ |
| TC-B05 | Project đã có block `Main` | Add New Block, chọn Main | Tuỳ chọn Main bị vô hiệu hoá |
| TC-B06 | Project đang lưu | Kill tiến trình giữa lúc Save | `.dbiproj` không hỏng (ghi atomic) |
| TC-B07 | `.dbiproj` có `schemaVersion: "9.0"` | Mở project | Từ chối kèm thông báo rõ, không crash |
| TC-B08 | Device `Modbus_IO` có 12 tag trỏ tới | Xoá device | Bị chặn, liệt kê 12 tag đang dùng |

### Nhóm C — IPC & Runtime (phase-02, 03)

| ID | Given | When | Then |
|---|---|---|---|
| TC-C01 | Studio protocol v1, Runtime v2 | Handshake | Từ chối, `E_PROTO_MISMATCH`, thông báo rõ |
| TC-C02 | Assembly 2 MB | Deploy | Truyền đủ, framing length-prefix hoạt động |
| TC-C03 | Runtime đang `Running` | Kill tiến trình Studio | Runtime **vẫn chạy**, `CycleCount` vẫn tăng |
| TC-C04 | Studio mất kết nối | Khởi động lại Runtime | Studio tự nối lại (backoff 1→2→5→10s) |
| TC-C05 | Runtime chạy 60s, có push loop + poll status | Đo metric | Jitter TB < 2ms, **không** chu kỳ nào > 40ms |
| TC-C06 | 3 driver, mỗi driver vài tag | Scan một chu kỳ | Mỗi driver chỉ nhận route của **chính nó** (B-5 chết) |
| TC-C07 | Deploy assembly không có class kế thừa `ControllerProgram` | Deploy | `E_NO_PROGRAM_CLASS`, Runtime **không** crash |
| TC-C08 | Runtime đang `Running`, đã có 1 client | Client thứ 2 kết nối | Từ chối kèm lý do rõ |

### Nhóm D — An toàn (phase-07, 11 + ADR-004, 005)

| ID | Given | When | Then |
|---|---|---|---|
| TC-D01 | Máy `Running`, băng tải quay | Deploy chương trình mới | Output ghi 0 xuống **thiết bị thật** trước khi unload |
| TC-D02 | Code người dùng ném `DivideByZeroException` | Chu kỳ scan chạy tới đó | SafetyCatch bắt · output = 0 · force xoá sạch · state `Faulted` · push kênh `fault` |
| TC-D03 | Đang force 3 tag | Xảy ra fault | **Toàn bộ** force bị xoá |
| TC-D04 | Đang force 3 tag | Deploy chương trình mới | **Toàn bộ** force bị xoá |
| TC-D05 | `lastCleanState = Faulted` | Runtime khởi động lại | State = `Stopped`, **không** tự chạy (chốt chặn 1) |
| TC-D06 | `lastCleanState = Running`, có file `.norun` | Runtime khởi động lại | State = `Stopped` (chốt chặn 2 — phanh tay) |
| TC-D07 | `lastCleanState = Running`, không có `.norun`, checksum đúng | Runtime khởi động lại | Tự Load → Connect → Start (ADR-005) |
| TC-D08 | `program.dll` bị sửa/hỏng | Runtime khởi động lại | Checksum sai → `NoProgram`, không nạp |
| TC-D09 | Đang force `ConveyorRun = FALSE`, logic tính ra `TRUE` | Chu kỳ scan | Driver nhận **FALSE** — force thắng logic |
| TC-D10 | Đang force 3 tag | Đóng Studio | Hiện D-06 với 3 lựa chọn rõ ràng |
| TC-D11 | Runtime `Running` | Đóng Studio | Hiện D-07, Runtime **không** bị giết |
| TC-D12 | v1 Runtime | Deploy với `swapMode = HotReload` | `E_HOTRELOAD_UNSUPPORTED`, thông báo rõ (ADR-004) |

### Nhóm E — Hiệu năng (phase-10, 13)

| ID | Given | When | Then |
|---|---|---|---|
| TC-E01 | 200 tag đang monitor @10Hz | Đo UI | ≥ 60fps, CPU Studio < 15% |
| TC-E02 | Monitoring bật | Đo Runtime 60s | Jitter tăng **không quá 0.5ms** so với baseline |
| TC-E03 | File 200 dòng, 30 tag, overlay bật | Gõ phím | Độ trễ < 80ms, ≥ 50fps |
| TC-E04 | Project 20 block | Compile | UI không treo, có progress |
| TC-E05 | Watch table mở, cuộn màn hình | Theo dõi subscription | Chỉ subscribe tag trong vùng nhìn thấy |

### Nhóm F — IntelliSense (phase-08)

| ID | Given | When | Then |
|---|---|---|---|
| TC-F01 | Tag Table có `StartButton` | Gõ `IO.Sta` | Gợi ý hiện `StartButton` kèm kiểu + comment + device/address |
| TC-F02 | Đang gõ code | Gõ `IO.StartButtonn` | Gạch đỏ **ngay**, không đợi Compile |
| TC-F03 | Thêm tag mới vào Tag Table | Quay lại editor | Gợi ý có tag mới ngay, không cần restart |
| TC-F04 | IntelliSense báo lỗi X | Bấm Compile | Compile báo **cùng** lỗi X (hai nguồn phải khớp) |

---

## 9. THAM CHIẾU QUYẾT ĐỊNH KIẾN TRÚC

| ADR | Tiêu đề | Ảnh hưởng tới |
|---|---|---|
| [ADR-001](../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-001-runtime-ipc-boundary.md) | Runtime tiến trình riêng + IPC | §1, §3, §4.1, TC-C* |
| [ADR-002](../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-002-tag-source-of-truth.md) | Tag Table sinh `IO.g.cs` | §2.2, §2.3, F-02, TC-B* |
| [ADR-003](../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-003-shell-avalondock.md) | AvalonDock 4 vùng | §5 |
| [ADR-004](../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-004-deploy-cold-restart.md) | Cold restart, để đường cho Hot Reload | §4.1, TC-D01, TC-D12 |
| [ADR-005](../plans/260726-2316-dbi-studio-tia-ide/decisions/ADR-005-runtime-autostart.md) | Runtime tự chạy lại sau restart | §4.3, §6.4, TC-D05…D08 |

---

## 10. NHỮNG CHỖ CẦN CẨN THẬN NHẤT

Tổng hợp lại để không ai quên khi bắt tay vào code:

| # | Chỗ | Vì sao nguy hiểm |
|---|---|---|
| 1 | Push loop của IPC | Chạy nhầm trên scan thread là phá tan lý do tồn tại của ADR-001 |
| 2 | Thứ tự khi Stop | Phải xả output xuống **thiết bị thật** trước khi unload, không chỉ xoá bộ nhớ |
| 3 | Reference BCL khi compile | Studio net10 dễ nhặt nhầm BCL net10 vào assembly target net8 |
| 4 | Force + SafetyCatch | Fault mà không xoá force = máy vẫn bị điều khiển bởi giá trị cưỡng bức |
| 5 | Autostart sau Faulted | Không chặn thì thành vòng lặp restart–fault vô tận, mỗi vòng giật output |
| 6 | `PropertyChanged` khi monitoring | Raise mỗi message = treo UI ở 200 tag |
| 7 | `IDriver` breaking change | Sửa 5 driver; nhớ Constraint C-5 (bọc `DBI.Drivers`, không tự viết giao thức) |
| 8 | `AssemblyLoadContext` | Còn sót một reference là không giải phóng được — sẽ đau khi làm Hot Reload sau này |
