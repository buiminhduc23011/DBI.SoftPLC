# Phase 09 — Device Config & Tag Routing

**Status:** 🟡 Core done; hardware acceptance pending | **Phụ thuộc:** phase-02, phase-05, phase-06 | **Nội dung BRIEF:** #17

> Thay 5 device hardcode ở [MainViewModel.cs:60-67](../../src/DBI.Controller.Studio/ViewModels/MainViewModel.cs) bằng CRUD thật.

---

## 🔴 Task 09.0 — Mở rộng driver sang `Int` / `Real` (B-6)

> **Không có task này thì Tag Table nói dối người dùng.** Kỹ sư khai tag `Temperature` kiểu `Real`, map vào Modbus `40001`, deploy — và **luôn đọc về 0**, không báo lỗi gì. Đúng loại lỗi âm thầm mà [ADR-002](decisions/ADR-002-tag-source-of-truth.md) và phase-00 đang cố tiêu diệt.

**Hiện trạng đã grep xác nhận:** cả 5 driver **chỉ xử lý `bool`** — 0 lần dùng Int/Float.

```csharp
// Mẫu hiện tại: chỉ có coil / discrete input
bool[] inputs = _modbusMaster.ReadDiscreteInputs(1, map.ModbusAddress, 1);
snapshot.SetRawInputBool(map.TagName, inputs[0]);
```

### Việc cần làm

**1. `TagRoute` mang theo kiểu dữ liệu** (đã có `DataType` trong contract phase-02) — driver dựa vào đó chọn hàm đọc/ghi.

**2. Mỗi driver bổ sung nhánh Int/Real:**

| Driver | `Bool` | `Int` | `Real` |
|---|---|---|---|
| Modbus / FactoryIO | Coil / DiscreteInput ✅ | HoldingRegister (1 word) | HoldingRegister (2 word, IEEE-754) |
| Delta | M/X/Y/S ✅ | D register (1 word) | D register (2 word) |
| Omron | CIO/W bit ✅ | D/CIO word | D 2 word |
| Simulation | ✅ | thêm | thêm |

**3. Quyết định endianness cho `Real` 2 word.** Modbus không chuẩn hoá thứ tự word — Factory I/O, Delta, Omron có thể khác nhau. Thêm tuỳ chọn `wordOrder: BigEndian | LittleEndian` vào `DeviceConfig.Settings`, mặc định BigEndian, cho người dùng đổi được.

**4. Constraint C-5 vẫn áp dụng:** chỉ bọc core client từ `DBI.Drivers`. Nếu `DBI.Drivers.Modbus` chưa có hàm đọc HoldingRegister thì **viết ở repo `DBI.Drivers` trước**, không viết giao thức trong repo này.

### Phương án tạm nếu chưa kịp làm

Nếu phải hoãn task này: **Tag Table chỉ cho chọn `Bool`**, ẩn `Int`/`Real` khỏi dropdown (phase-06), kèm tooltip *"Đang phát triển"*. Thà thiếu tính năng còn hơn để người dùng khai tag rồi nhận số 0 âm thầm.

---

## Task 09.1 — Catalog driver

**File:** `src/DBI.Controller.Studio/Services/Devices/DriverCatalog.cs`

Mô tả driver có sẵn + tham số cấu hình của từng loại. **Constraint C-5: mọi driver phải bọc core client từ `DBI.Drivers`, không tự viết giao thức.**

| Driver | Tham số | Địa chỉ mẫu |
|---|---|---|
| `Simulation` | — | `Sim_0` |
| `Modbus` | `host`, `port`, `unitId`, `mode` (TCP/RTU/ASCII) | `40001`, `00001` |
| `Delta.PLC` | `host`, `port`, `station` | `D100`, `M0`, `X0`, `Y0` |
| `Omron` | `host`, `port`, `node` (FINS/HostLink) | `CIO100`, `D200` |
| `FactoryIO` | `host`, `port` | `Input_0`, `Output_0` |

Catalog cung cấp cho UI: danh sách tham số, giá trị mặc định, validator, và **placeholder địa chỉ** dùng ở Tag Table (phase-06 Task 06.1).

## Task 09.2 — Device Config editor

Document tab:

```
Devices                                              [+ Add Device]
┌──────────────────┬──────────────────┬───────────────────┬─────────┐
│ Name             │ Driver           │ Connection        │ Status  │
├──────────────────┼──────────────────┼───────────────────┼─────────┤
│ FactoryIO_3D     │ FactoryIO        │ 127.0.0.1:502     │ 🟢 OK   │
│ Modbus_IO_Module │ Modbus TCP       │ 192.168.1.20:502  │ 🔴 Lỗi  │
│ Delta_DVP_PLC    │ Delta PLC        │ 192.168.1.5:502   │ ⚪ Chưa │
└──────────────────┴──────────────────┴───────────────────┴─────────┘
```

Chọn device → Inspector → Properties hiện form tham số động sinh từ catalog.

## Task 09.3 — Dialog Add Device

1. Chọn loại driver (list + mô tả)
2. Đặt tên (C# identifier hợp lệ, không trùng)
3. Điền tham số (form sinh từ catalog, có validate)
4. **Test Connection** — gửi lệnh thử qua Runtime, báo kết quả ngay

## Task 09.4 — Trạng thái driver

`IRuntimeClient.GetDeviceStatesAsync()` (đã định nghĩa ở phase-02/03), poll 1s khi online.

| `ConnectionState` | Hiển thị |
|---|---|
| `Connected` | 🟢 OK |
| `Connecting` | 🟡 Đang kết nối |
| `Disconnected` | ⚪ Chưa kết nối |
| `Faulted` | 🔴 Lỗi — kèm `LastError` |

Hiện cả ở node Devices trong Project Tree (phase-05).

## Task 09.5 — Đổi tên device → cập nhật tag

Đổi tên device khi đã có tag trỏ tới:
```
Device "Modbus_IO_Module" đang được 12 tag sử dụng.
Đổi tên sẽ cập nhật cả 12 tag.
                          [ Huỷ ]  [ Đổi tên ]
```

Xoá device khi còn tag trỏ tới → **chặn**, liệt kê tag đang dùng.

---

## Definition of Done

### Checkpoint 2026-07-28

- [x] Driver catalog đã có đủ 5 loại driver, setting mặc định và address placeholder.
- [x] ProjectService có Add/Rename/Delete device; rename cập nhật các tag liên quan và delete bị chặn khi còn usage.
- [x] Regression tests catalog + CRUD: `3/3` pass.
- [x] Modbus, Factory I/O, Delta và Omron adapter build thành công với nhánh Int/Real.
- [x] UI Device Configuration tab được nối vào Project Tree; add Simulation và delete có guard.
- [ ] Dynamic driver form, connection test/status polling và end-to-end hardware test vẫn đang triển khai.
- [x] Dynamic driver form đã sinh theo catalog và validate tên device trước khi tạo.

- [ ] 🔴 **Task 09.0:** cả 5 driver đọc/ghi được `Int` và `Real`, không chỉ `Bool`
- [ ] Test end-to-end: tag `Real` map vào Modbus `40001` → đọc đúng giá trị thật, **không phải 0**
- [ ] `wordOrder` cấu hình được cho `Real` 2 word; mặc định BigEndian
- [ ] Nếu hoãn 09.0 → Tag Table **ẩn** `Int`/`Real`, không để người dùng khai tag vô dụng
- [ ] Catalog đủ 5 driver hiện có, đúng tham số
- [ ] CRUD device hoạt động, ghi vào `.dbiproj`
- [ ] Form tham số sinh động theo loại driver, có validate
- [ ] Test Connection hoạt động thật qua Runtime
- [ ] Trạng thái driver hiển thị đúng 4 mức, poll 1s
- [ ] Trạng thái hiện cả ở Project Tree
- [ ] Đổi tên device cập nhật mọi tag liên quan
- [ ] Không xoá được device đang có tag dùng; báo rõ tag nào
- [ ] Dropdown Device ở Tag Table nạp từ danh sách thật
- [ ] Placeholder địa chỉ ở Tag Table đổi theo loại driver
- [ ] Dữ liệu device hardcode trong `MainViewModel` đã bị xoá sạch
- [ ] Deploy gửi đúng `DeviceSpec`; Runtime khởi tạo đúng driver
