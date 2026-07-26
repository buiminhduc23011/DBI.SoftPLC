# Phase 05 — Project Tree & Block Templates

**Status:** ⬜ Pending | **Phụ thuộc:** phase-01, phase-04 | **Nội dung BRIEF:** #2

> Yêu cầu số 1 của người dùng: *"Cây project (Code nhiều khối function, main,...)"*

---

## Task 05.1 — Cấu trúc cây

```
📁 MyMachine
 ├─ ⚙️  Device Configuration          → mở tab Device Config (phase-09)
 ├─ 📊 Online & Diagnostics           → mở tab Diagnostics
 ├─ 📦 Program Blocks
 │   ├─ ▶️  Main            [Main]     → mở code editor
 │   ├─ 🔷 Conveyor        [FB]
 │   ├─ 🔶 ScaleValue      [FC]
 │   └─ 🗄️  RecipeData      [DB]
 ├─ 🏷️  PLC Tags
 │   ├─ Default Tag Table             → mở tab Tag Table (phase-06)
 │   └─ Conveyor Tags
 ├─ 👁️  Watch & Force Tables          → phase-10, 11
 └─ 🔌 Devices
     ├─ FactoryIO_3D                  🟢
     └─ Modbus_IO_Module              🔴
```

Nhóm cố định (Program Blocks / PLC Tags / ...) luôn hiện dù rỗng — giống TIA, giúp người dùng biết chỗ để thêm.

## Task 05.2 — `ProjectTreeViewModel`

```csharp
public abstract class TreeNodeViewModel {
    public string Title { get; }
    public string Icon  { get; }
    public ObservableCollection<TreeNodeViewModel> Children { get; }
    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
    public virtual void OnDoubleClick() { }        // mở document tương ứng
    public virtual IEnumerable<MenuItemVm> ContextMenu => [];
}
```

Node cụ thể: `ProjectRootNode`, `FolderNode`, `BlockNode`, `TagTableNode`, `DeviceNode`, `WatchTableNode`.

`HierarchicalDataTemplate` chọn template theo kiểu node.

## Task 05.3 — Context menu

| Node | Menu |
|---|---|
| **Program Blocks** | Add new block… |
| **Block** | Open · Rename · Delete · Properties · Set as Main *(chỉ nếu Kind=Main hợp lệ)* |
| **PLC Tags** | Add new tag table |
| **Tag Table** | Open · Rename · Delete *(chặn xoá Default)* |
| **Devices** | Add device… |
| **Device** | Edit · Delete · Connect / Disconnect |

## Task 05.4 — Dialog "Add new block"

```
┌──────────────────────────────────────────┐
│  Add new block                           │
├──────────────────────────────────────────┤
│  Name:  [ Conveyor              ]        │
│                                          │
│  ○ ▶️  Main            — điểm vào chu kỳ  │
│  ● 🔷 Function Block  — có nhớ trạng thái │
│  ○ 🔶 Function        — không nhớ         │
│  ○ 🗄️  Data Block      — chỉ chứa dữ liệu │
│                                          │
│  Comment: [ Logic băng tải chính     ]   │
│                                          │
│           [ Cancel ]  [ OK ]             │
└──────────────────────────────────────────┘
```

- `Main` **bị vô hiệu hoá** nếu project đã có Main
- Tên phải là C# identifier hợp lệ, không trùng (dùng lại validator của phase-01)

## Task 05.5 — Template khối

**Thư mục:** `src/DBI.Controller.Studio/Templates/`

**Main:**
```csharp
using DBI.Controller.SDK;

namespace UserProgram;

public class {{Name}} : ControllerProgram
{
    // Khai báo instance của các Function Block ở đây
    // private readonly Conveyor _conveyor = new();

    public override void OnStart() { }

    public override void Execute()
    {
        // Giữ Main mỏng — chỉ gọi FB/FC mô tả luồng chương trình
        // _conveyor.Execute(IO);
    }

    public override void OnStop() { }
}
```

**Function Block** (state giữ qua các chu kỳ = field của class, tương đương Instance DB):
```csharp
using DBI.Controller.SDK;
using DBI.Controller.SDK.Primitives;

namespace UserProgram;

public class {{Name}}
{
    private readonly Ton _delay = new(1000);

    public void Execute(IOContainer IO)
    {
        // Logic có nhớ trạng thái
    }
}
```

**Function** (không nhớ):
```csharp
namespace UserProgram;

public static class {{Name}}
{
    public static float Scale(float raw, float inMin, float inMax, float outMin, float outMax)
        => outMin + (raw - inMin) * (outMax - outMin) / (inMax - inMin);
}
```

**Data Block:**
```csharp
namespace UserProgram;

public class {{Name}}
{
    public float Setpoint { get; set; }
    public int   BatchSize { get; set; }
}
```

> Template chỉ là **điểm khởi đầu**. Bên trong file là C# thuần, Studio không ép ràng buộc gì thêm (ADR — quyết định Hybrid).

## Task 05.6 — Đồng bộ file ↔ cây

| Sự kiện | Hành vi |
|---|---|
| Rename node | Đổi tên file `.cs` **và** tên class bên trong; cập nhật `.dbiproj` |
| Delete node | Hỏi xác nhận, xoá file, cập nhật `.dbiproj` |
| File bị sửa/xoá ngoài Studio | `FileSystemWatcher` → hỏi reload / khôi phục |
| File `.cs` lạ trong `Blocks/` | Hỏi "thêm vào project?" |

Rename class: dùng thay thế chuỗi đơn giản ở v1; sau phase-08 (có Roslyn workspace) nâng cấp thành rename ngữ nghĩa thật.

---

## Definition of Done

- [ ] Cây hiển thị đủ nhóm chuẩn TIA, có icon phân biệt loại khối
- [ ] Double-click block mở đúng document tab; mở lại không tạo tab trùng
- [ ] Context menu đủ chức năng theo bảng Task 05.3
- [ ] Add new block sinh file `.cs` từ template, **biên dịch được ngay** (test cho cả 4 loại)
- [ ] Không tạo được block Main thứ hai
- [ ] Không tạo được block trùng tên / tên không hợp lệ C#
- [ ] Rename đổi cả tên file, tên class và `.dbiproj`
- [ ] Delete hỏi xác nhận và xoá đúng file
- [ ] `FileSystemWatcher` phát hiện sửa đổi ngoài Studio
- [ ] Trạng thái mở rộng của cây được lưu và khôi phục
- [ ] Chọn node cập nhật tab Properties của Inspector
- [ ] Test: project có 20 block vẫn thao tác mượt
