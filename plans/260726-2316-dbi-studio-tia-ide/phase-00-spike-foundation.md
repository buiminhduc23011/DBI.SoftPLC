# Phase 00 — Spike & Foundation Fixes

**Status:** ✅ Done (2026-07-27) | **Ưu tiên:** 🔴 CHẶN TẤT CẢ | **Phụ thuộc:** — | **Nội dung BRIEF:** #0

> Phase này không tạo ra tính năng nhìn thấy được. Nó gỡ 3 lỗi nền tảng và trả lời 2 câu hỏi kỹ thuật còn treo. Làm sai ở đây thì mọi phase sau đều phải làm lại.

---

## Mục tiêu

1. Gỡ `DynamicObject` khỏi `IOContainer` (B-1) → mở đường cho ADR-002 và IntelliSense
2. Sửa `IMemoryImage` để driver ghi input đúng chỗ (B-2)
3. Double-buffer cho `Int`/`Float` (B-3)
4. **Spike:** `RoslynPad.Editor` có chạy được trên net10.0-windows + Roslyn 5.6.0 không?
5. **Spike:** `Dirkster.AvalonDock` có theme được về phong cách industrial phẳng không?

---

## Task 00.0 — Kiểm tra môi trường (làm đầu tiên, 1 phút)

```powershell
# 1. Repo anh em DBI.Drivers phải nằm cạnh DBI.SoftPLC
Test-Path ..\DBI.Drivers\DBI.Drivers.Modbus\DBI.Drivers.Modbus.csproj      # phải True

# 2. Baseline phải xanh TRƯỚC khi sửa gì
dotnet build DBI.Controller.slnx --nologo                                   # 0 Warning, 0 Error
dotnet test  DBI.Controller.slnx --nologo                                   # tất cả pass
```

Nếu bước 1 sai → dừng, clone `DBI.Drivers` cạnh repo này. Đây là lỗi môi trường, không phải lỗi code.
Nếu bước 2 sai → dừng, ghi lại trạng thái. Không được bắt đầu phase-00 từ nền đã hỏng.

**Baseline xác nhận 2026-07-27:** build 0 warning / 0 error ✅

## Task 00.1 — Gỡ `DynamicObject` khỏi `IOContainer`

**File:** [src/DBI.Controller.SDK/IO/IOContainer.cs](../../src/DBI.Controller.SDK/IO/IOContainer.cs)

```csharp
// TRƯỚC
public class IOContainer : DynamicObject {
    public override bool TryGetMember(GetMemberBinder b, out object? r) {
        r = _memoryImage.GetBool(b.Name);   // ⚠️ luôn bool
        return true;                         // ⚠️ luôn pass
    }
    public override bool TrySetMember(...) { ... }
}

// SAU
public partial class IOContainer {
    private readonly IMemoryImage _memoryImage;
    public IOContainer(IMemoryImage m) => _memoryImage = m ?? throw new ArgumentNullException(nameof(m));

    // API nội bộ cho IO.g.cs gọi — giữ nguyên chữ ký
    public bool  GetBool(string k)  => _memoryImage.GetBool(k);
    public void  SetBool(string k, bool v)  => _memoryImage.SetBool(k, v);
    public int   GetInt(string k)   => _memoryImage.GetInt(k);
    public void  SetInt(string k, int v)    => _memoryImage.SetInt(k, v);
    public float GetFloat(string k) => _memoryImage.GetFloat(k);
    public void  SetFloat(string k, float v)=> _memoryImage.SetFloat(k, v);
    public bool this[string k] { get => GetBool(k); set => SetBool(k, v: value); }
}
```

- ❌ Xoá `using System.Dynamic;`, xoá `TryGetMember` / `TrySetMember`
- ✅ Thêm `partial` — phần property strongly-typed sẽ do phase-06 sinh

## Task 00.2 — Sửa `ControllerProgram.IO`

**File:** [src/DBI.Controller.SDK/ControllerProgram.cs](../../src/DBI.Controller.SDK/ControllerProgram.cs)

```csharp
// TRƯỚC: public dynamic IO => ...
// SAU:
public IOContainer IO => _io ?? throw new InvalidOperationException(
    "Chương trình chưa được khởi tạo với Memory Image từ Runtime.");
```

- Giữ `IOStore` làm alias tương thích ngược, đánh dấu `[Obsolete("Dùng IO")]`

## Task 00.3 — Bổ sung `IMemoryImage` (B-2)

**File:** [src/DBI.Controller.Core/Interfaces/IMemoryImage.cs](../../src/DBI.Controller.Core/Interfaces/IMemoryImage.cs)

> ⚠️ **Đọc kỹ trước khi sửa — đừng đi tìm nhầm bug.**
>
> Driver hiện **KHÔNG** gọi `SetBool` nhầm chỗ. Chúng ép kiểu xuống rồi gọi đúng hàm, và **đang chạy đúng**:
>
> ```csharp
> // FactoryIODriver.cs:55-63 — cả 5 driver đều theo mẫu này
> if (State != Connected || _modbusMaster == null || memoryImage is not MemorySnapshot snapshot)
>     return Task.CompletedTask;      // ⚠️ VẤN ĐỀ NẰM Ở ĐÂY: im lặng bỏ qua
> ...
> snapshot.SetRawInputBool(map.TagName, inputs[0]);
> ```
>
> Vấn đề thật là **nhánh thất bại im lặng**: không exception, không log. Nếu phase-11 làm Force layer dạng decorator bọc `IMemoryImage`, mọi driver sẽ ngừng đọc/ghi mà máy không báo gì — băng tải đứng yên, kỹ sư không biết tại sao.

Đưa API thô lên interface để driver **không phải ép kiểu nữa**:

```csharp
// Driver ghi input thô vào InputBuffer
void SetRawInput(string key, bool value);
void SetRawInput(string key, int value);
void SetRawInput(string key, float value);

// Driver đọc output đã chốt từ OutputBuffer để gửi xuống phần cứng
bool  GetRawOutputBool(string key);
int   GetRawOutputInt(string key);
float GetRawOutputFloat(string key);

// Liệt kê toàn bộ tag cho monitoring (phase-10)
IReadOnlyDictionary<string, object> SnapshotAll();
```

**Cập nhật cả 5 driver** — bỏ hẳn `is not MemorySnapshot snapshot`, dùng thẳng `IMemoryImage`:

```csharp
// TRƯỚC
if (State != Connected || _modbusMaster == null || memoryImage is not MemorySnapshot snapshot)
    return Task.CompletedTask;
snapshot.SetRawInputBool(map.TagName, inputs[0]);

// SAU
if (State != Connected || _modbusMaster == null)
    return Task.CompletedTask;
memoryImage.SetRawInput(map.TagName, inputs[0]);
```

| File | Dòng cần sửa |
|---|---|
| [FactoryIODriver.cs](../../Drivers/DBI.Controller.Driver.FactoryIO/FactoryIODriver.cs) | 55, 63, 76, 83 |
| [ModbusDriverAdapter.cs](../../Drivers/DBI.Controller.Driver.Modbus/ModbusDriverAdapter.cs) | 65, 75, 80, 94, 103 |
| [DeltaPlcDriverAdapter.cs](../../Drivers/DBI.Controller.Driver.Delta/DeltaPlcDriverAdapter.cs) | 58, 68, 73, 78, 92, 101, 106 |
| [OmronPlcDriverAdapter.cs](../../Drivers/DBI.Controller.Driver.Omron/OmronPlcDriverAdapter.cs) | 56, 66, 71, 76, 90, 97 |
| [SimulationDriver.cs](../../Drivers/DBI.Controller.Driver.Simulation/SimulationDriver.cs) | 27 |

**Kiểm chứng đã sửa hết:**
```powershell
Select-String -Path "Drivers\*\*.cs" -Pattern "is not MemorySnapshot"   # phải ra RỖNG
```

## Task 00.4 — Double-buffer cho Int / Float (B-3)

**File:** [src/DBI.Controller.Core/Models/MemorySnapshot.cs](../../src/DBI.Controller.Core/Models/MemorySnapshot.cs)

Hiện `_intValues` / `_floatValues` là **một** dictionary duy nhất, bỏ qua cơ chế snapshot → race giữa driver thread và logic thread.

Nhân bản đúng mô hình của `bool`:

| Kiểu | Cần có |
|---|---|
| `bool` | `_inputBuffer`, `_inputSnapshot`, `_outputSnapshot`, `_outputBuffer` ✅ đã có |
| `int` | 4 dictionary tương tự ➕ thêm |
| `float` | 4 dictionary tương tự ➕ thêm |

`SwapInputBuffers()` / `SwapOutputBuffers()` phải swap cả 3 kiểu.

**Tối ưu kèm theo:** hiện swap là vòng lặp copy O(n) mỗi scan, không phải "lock-free swap" như comment. Đổi sang **hoán đổi tham chiếu** (`Interlocked.Exchange` trên field dictionary) — đúng như tên hàm và tên class mô tả.

### ⚠️ Lệch so với plan khi triển khai — CHỈ input hoán đổi tham chiếu

Áp hoán đổi tham chiếu cho **output** sẽ **phá latch** và đó là lỗi an toàn máy móc, không phải chuyện hiệu năng:

```csharp
if (IO["StartButton"]) IO["ConveyorRun"] = true;   // chu kỳ N ghi true
// chu kỳ N+1 không ai ghi lại -> phải vẫn là true
```

Output là **trạng thái giữ (retentive)** của chương trình. Nếu hoán đổi tham chiếu, chu kỳ N+1 logic sẽ đọc lại buffer của 2 chu kỳ trước → băng tải tự tắt. Test `ConveyorProgram_StartAndStopFlow` bắt được ngay tình huống này.

| Hướng | Cơ chế | Lý do |
|---|---|---|
| **Input** | `Interlocked.Exchange` hoán đổi tham chiếu — O(1), không cấp phát | Driver ghi đủ mọi tag mỗi chu kỳ → double-buffer kinh điển |
| **Output** | Publish `OutputState → OutputBuffer`, O(n) **nhưng ghi vào dictionary có sẵn, không cấp phát** | Giữ latch. n ≤ ~500 tag → vài µs trong chu kỳ 20ms, không đáng kể |

Ràng buộc của mô hình input double-buffer đã ghi rõ trong XML doc của `MemorySnapshot`: **driver phải ghi đủ mọi tag nó quản lý ở mỗi chu kỳ**, vì buffer ghi sau khi hoán đổi chứa dữ liệu của 2 chu kỳ trước.

## ✅ Task 00.5 — SPIKE: RoslynPad.Editor — **XONG 2026-07-27**

**Kết quả:** ✅ **DÙNG ĐƯỢC — 10/10 PASS.** Báo cáo: [reports/spike-roslynpad.md](reports/spike-roslynpad.md)

Phase-08 chạy phương án đầy đủ, **không** cần phương án lùi. Nhưng spike phát sinh 5 việc bắt buộc — xem Task 00.8.

## ✅ Task 00.6 — SPIKE: AvalonDock theming — **XONG 2026-07-27**

**Kết quả:** ✅ **DÙNG ĐƯỢC — 7/7 PASS.** Báo cáo: [reports/spike-avalondock.md](reports/spike-avalondock.md)

Rủi ro theming không xảy ra. Theme VS2013 đè brush được về bảng màu flat industrial, cả Light lẫn Dark, đổi được lúc runtime. Layout save/restore, float/dock, auto-hide đều chạy.

## Task 00.8 — Việc phát sinh từ spike (bắt buộc)

### 00.8a — Ghim version Roslyn về 5.3.0

`RoslynPad.Roslyn` 5.0.0 phụ thuộc Roslyn ở **đúng** 5.3.0. Studio đang ghim 5.6.0 → NuGet nâng một phần, để lại một phần, sinh **9 cảnh báo NU1608** và assembly nạp lệch version:

```
Microsoft.CodeAnalysis            5.6.0   ← bị nâng
Microsoft.CodeAnalysis.Features   5.3.0   ← giữ nguyên
```

Cấu hình trộn *vẫn chạy đúng* trong spike, nhưng đó là may mắn — Roslyn ship theo bộ khớp chặt, một bản vá sau này là hỏng lúc chạy chứ không phải lúc build.

```diff
- <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.6.0" />
+ <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="5.3.0" />
```

Đặt ở `Directory.Build.props` để `RoslynCompilerService` (phase-07) và `StudioWorkspace` (phase-08) **luôn dùng chung version** — lệch nhau khiến IntelliSense và Compile báo lỗi khác nhau.

### 00.8b — Chuẩn bị cho phase-04: theme phải là `ResourceDictionary`

Spike xác nhận: theme token dạng **string binding trên ViewModel** (cách hiện tại) **không** theme được AvalonDock. Bắt buộc chuyển sang `ResourceDictionary` + `DynamicResource`. Đây là điều kiện cần, không phải tuỳ chọn.

## Task 00.7 — Cập nhật code phụ thuộc

Tìm và sửa mọi nơi dùng `dynamic IO`:

- `Samples/Sample.Conveyor/`
- `tests/DBI.Controller.Tests/`
- `src/DBI.Controller.Testing/`
- Chuỗi mẫu trong [MainViewModel.cs:30-54](../../src/DBI.Controller.Studio/ViewModels/MainViewModel.cs)

Tạm thời dùng `IOStore["TagName"]` cho tới khi phase-06 sinh được `IO.g.cs`.

---

## Definition of Done

- [x] Task 00.0: `DBI.Drivers` có mặt; `dotnet build` + `dotnet test` xanh **trước khi** bắt đầu
- [x] `IOContainer` không còn kế thừa `DynamicObject`, là `partial class`
- [x] `ControllerProgram.IO` trả về `IOContainer`, không phải `dynamic` (`IOStore` giữ làm alias `[Obsolete]`)
- [x] `IMemoryImage` có `SetRawInput` (3 overload), `GetRawOutput*` (3), `SnapshotAll()`, `GetRawOutputs()`
- [x] **Cả 5 driver bỏ hẳn ép kiểu** — `Select-String "MemorySnapshot" Drivers\*\*.cs` ra **rỗng**
- [x] Test: đưa cho driver một `IMemoryImage` **không phải** `MemorySnapshot` → vẫn đọc/ghi đúng — `DriverMemoryImageTests`, có cả decorator `ForcingMemoryImage` mô phỏng Force layer phase-11
- [x] Test chốt chặn bổ sung: quét bảng TypeRef trong metadata của cả 5 assembly driver, khẳng định không assembly nào còn nhắc tới `MemorySnapshot`
- [x] `MemorySnapshot` double-buffer đầy đủ cho `bool` / `int` / `float`
- [x] Swap buffer **input** dùng hoán đổi tham chiếu, không copy từng entry — ⚠️ **output cố ý giữ publish-copy** để không phá latch, xem mục "Lệch so với plan" ở Task 00.4
- [x] Unit test chứng minh: ghi input giữa chu kỳ **không** ảnh hưởng snapshot đang thực thi (cả 3 kiểu)
- [x] Unit test chứng minh: đọc tag `float` trả về `float`, không phải `bool` (bug B-1 đã chết)
- [x] Unit test chứng minh output giữ latch qua nhiều chu kỳ, và `ClearAllOutputs` đưa cả 3 kiểu về safe state
- [x] `dotnet build` toàn solution xanh (0 warning / 0 error); `dotnet test` **25/25 pass**
- [x] ✅ `reports/spike-roslynpad.md` — **DÙNG ĐƯỢC, 10/10 PASS**
- [x] ✅ `reports/spike-avalondock.md` — **DÙNG ĐƯỢC, 7/7 PASS**, có ảnh Light + Dark
- [x] ✅ `plan.md` đã cập nhật: 2 rủi ro 🔴 gỡ bỏ, 1 rủi ro 🟡 mới phát hiện
- [x] `Microsoft.CodeAnalysis.CSharp` hạ **5.6.0 → 5.3.0**, khai báo ở `Directory.Build.props` (`$(DbiRoslynVersion)`)
- [x] `dotnet restore` toàn solution: **0 cảnh báo NU1608**
- [x] Không còn chỗ nào trong solution dùng `dynamic` để truy cập IO
