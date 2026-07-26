# Phase 01 — Project Model & Persistence

**Status:** ⬜ Pending | **Phụ thuộc:** phase-00 | **Nội dung BRIEF:** #1

> Studio hiện **không có khái niệm "project"** — code là một string trong ViewModel, device và tag là dữ liệu hardcode. Phase này tạo mô hình dữ liệu và khả năng lưu/mở.

---

## Mục tiêu

Định nghĩa mô hình project, schema `.dbiproj`, và các thao tác New / Open / Save / Save As / Recent.

---

## ⚠️ Lệch so với plan khi triển khai — project mới `DBI.Controller.Studio.Core`

Plan đặt model ở `src/DBI.Controller.Studio/Models/`. Nhưng `DBI.Controller.Studio` là **`net10.0-windows`** (WPF), còn test project là `net10.0` — **net10.0 không tham chiếu được net10.0-windows**, nên DoD "toàn bộ 6 luật validation có unit test" sẽ không thực hiện được.

Tách phần lõi không phụ thuộc WPF ra project mới:

| Project | TFM | Chứa |
|---|---|---|
| `src/DBI.Controller.Studio.Core` | `net8.0` | Models, `ProjectService`, `ProjectValidator`, `RecentProjectsService`, `BlockTemplates` |
| `src/DBI.Controller.Studio` | `net10.0-windows` | Chỉ WPF: View, ViewModel, theme |

Ngoài việc test được, cách này còn ép ranh giới đúng hướng: **logic project không được biết gì về WPF**. Phase-06 (codegen) và phase-07 (compile) sẽ nằm cùng chỗ này.

## Task 01.1 — Mô hình dữ liệu

**Thư mục:** `src/DBI.Controller.Studio.Core/Models/` *(xem mục lệch ở trên)*

```csharp
public class DbiProject {
    public string        SchemaVersion { get; set; } = "1.0";
    public string        Name          { get; set; } = "";
    public string        Description   { get; set; } = "";
    public RuntimeTarget Runtime       { get; set; } = new();
    public List<DeviceConfig>  Devices     { get; set; } = new();
    public List<TagTable>      TagTables   { get; set; } = new();
    public List<CodeBlock>     Blocks      { get; set; } = new();
    public List<WatchTable>    WatchTables { get; set; } = new();

    [JsonIgnore] public string ProjectFilePath { get; set; } = "";  // không serialize
    [JsonIgnore] public bool   IsDirty         { get; set; }
}

public class RuntimeTarget {
    public string TransportType { get; set; } = "NamedPipe";  // NamedPipe | Tcp
    public string PipeName      { get; set; } = "DBI.Runtime";
    public string Host          { get; set; } = "127.0.0.1";
    public int    Port          { get; set; } = 5580;
    public int    ScanIntervalMs{ get; set; } = 20;
    public bool   AutoStart     { get; set; } = true;   // ⚠️ ADR-005 — máy tự chạy sau mất điện
}

public class WatchTable {
    public string       Name     { get; set; } = "Watch table_1";
    public List<string> TagNames { get; set; } = new();
}

public class DeviceConfig {
    public string Name        { get; set; } = "";   // "FactoryIO_3D"
    public string DriverType  { get; set; } = "";   // "DBI.Controller.Driver.FactoryIO"
    public Dictionary<string, string> Settings { get; set; } = new();  // ip, port, slaveId...
}

public class TagTable {
    public string     Name { get; set; } = "Default Tag Table";
    public List<Tag>  Tags { get; set; } = new();
}

public class Tag {
    public string       Name      { get; set; } = "";   // C# identifier hợp lệ
    public TagDataType  DataType  { get; set; }         // Bool | Int | Real
    public TagDirection Direction { get; set; }         // Input | Output | Memory
    public string       Device    { get; set; } = "";   // "" nếu Direction=Memory
    public string       Address   { get; set; } = "";   // "Input_0" | "40001" | "D100"
    public string       Comment   { get; set; } = "";
}

public enum TagDataType  { Bool, Int, Real }
public enum TagDirection { Input, Output, Memory }

public class CodeBlock {
    public string    Name     { get; set; } = "";       // "Conveyor"
    public BlockKind Kind     { get; set; }             // Main | FunctionBlock | Function | DataBlock
    public string    FileName { get; set; } = "";       // "Blocks/Conveyor.cs"
    public string    Comment  { get; set; } = "";
}

public enum BlockKind { Main, FunctionBlock, Function, DataBlock }
```

## Task 01.2 — Bố cục trên đĩa

```
MyMachine/
├── MyMachine.dbiproj        ← JSON, indent, Git diff đọc được
├── Blocks/*.cs              ← file .cs thật, người dùng viết
├── Generated/IO.g.cs        ← phase-06 sinh
└── .dbistudio/layout.xml    ← layout AvalonDock, KHÔNG commit
```

**`.dbiproj` chỉ chứa metadata + tag + device. Nội dung code nằm trong file `.cs` thật** (Constraint C-7).
Studio ghi kèm `.gitignore` mẫu khi tạo project mới:
```
.dbistudio/
bin/
obj/
```

## Task 01.3 — `ProjectService`

**File:** `src/DBI.Controller.Studio/Services/ProjectService.cs`

| Method | Hành vi |
|---|---|
| `CreateNew(path, name)` | Tạo cây thư mục, `.dbiproj`, `Blocks/Main.cs` từ template, `.gitignore` |
| `Open(path)` | Đọc JSON, kiểm tra `SchemaVersion`, xác minh mọi file trong `Blocks` tồn tại |
| `Save()` | Ghi `.dbiproj` + mọi block đang dirty. **Ghi atomic**: tmp file → `File.Replace` |
| `SaveAs(path)` | Copy toàn bộ cây sang vị trí mới |
| `Close()` | Hỏi lưu nếu `IsDirty` |
| `RecentProjects` | 10 mục gần nhất, lưu ở `%APPDATA%/DBI.Studio/recent.json` |

**Xử lý lỗi bắt buộc:**
- Schema version cao hơn Studio → từ chối mở, báo rõ ràng
- Block khai trong `.dbiproj` nhưng file `.cs` không có → cảnh báo, cho phép "xoá khỏi project" hoặc "tạo lại từ template"
- File `.cs` có trong `Blocks/` nhưng không khai trong `.dbiproj` → hỏi "thêm vào project?"

## Task 01.4 — Validation

| Luật | Thông báo |
|---|---|
| `Tag.Name` phải là C# identifier hợp lệ | "Tên tag phải bắt đầu bằng chữ cái hoặc `_`" |
| `Tag.Name` không trùng (kể cả khác hoa/thường — `MemorySnapshot` dùng `OrdinalIgnoreCase`) | "Đã có tag tên này" |
| `Tag.Name` không phải từ khoá C# | "`class` là từ khoá C#, không dùng làm tên tag được" |
| `Direction != Memory` → `Device` phải tồn tại trong `Devices` | "Thiết bị `X` chưa được khai báo" |
| Đúng **một** block `Kind = Main` | "Project phải có đúng một khối Main" |
| `CodeBlock.Name` không trùng | "Đã có khối tên này" |

Validation trả về `List<ValidationIssue>` (Severity: Error/Warning) — hiển thị ở Inspector tab **Information** (phase-04).

---

## Definition of Done

- [x] Model classes đầy đủ, có `[JsonPropertyName]` rõ ràng; enum serialize bằng **tên** (`JsonStringEnumConverter`) để Git diff đọc được
- [x] `ProjectService` làm được New / Open / Save / SaveAs / Close / Recent
- [x] Save là **atomic** — ghi tệp tạm → `stream.Flush(flushToDisk: true)` → `File.Replace`
- [x] Round-trip test: tạo project → save → load → so sánh deep-equal
- [x] Test: mở `.dbiproj` schema version tương lai → từ chối kèm thông báo rõ
- [x] Test: block khai báo nhưng thiếu file `.cs` → sinh warning, không crash
- [x] Test: file `.cs` mồ côi trong `Blocks/` → warning "thêm vào project?"
- [x] Toàn bộ 6 luật validation có unit test (`ProjectValidatorTests`, 22 case)
- [x] Project mới tạo có sẵn `Blocks/Main.cs` biên dịch được
- [x] `.gitignore` được sinh kèm project mới
- [x] JSON có indent, Git diff đọc được bằng mắt; **không escape Unicode** → comment tiếng Việt đọc được
- [x] `dotnet test`: **82/82 pass**, build 0 warning / 0 error
