# Phase 08 — Roslyn IntelliSense & Error List

**Status:** ⬜ Pending | **Phụ thuộc:** phase-00 (spike!), phase-06, phase-07 | **Nội dung BRIEF:** #9, #10

> ✅ **Spike đã xong 2026-07-27: DÙNG ĐƯỢC, 10/10 PASS** — [reports/spike-roslynpad.md](reports/spike-roslynpad.md).
> Chạy phương án đầy đủ. Task 08.6 (phương án lùi) **không cần dùng**, giữ lại làm tài liệu.
>
> Đây là điểm bán hàng lớn nhất so với TIA Portal — TIA không có IntelliSense thật.

## ⚠️ Ba cạm bẫy spike đã phát hiện — đọc trước khi code

| # | Cạm bẫy | Hậu quả nếu bỏ qua |
|---|---|---|
| 1 | `RoslynHost` tạo trước khi WPF `Application` tồn tại | `ArgumentNullException (context)` — VS Threading cần `SynchronizationContext` |
| 2 | Dùng `host.AddRelatedDocument` cho project nhiều file | `InvalidOperationException`. API này dành cho scripting (1 file = 1 project) |
| 3 | **`RoslynCodeEditor.InitializeAsync` tạo project CÔ LẬP 1 file** | IntelliSense **sai âm thầm**: báo lỗi ở chỗ đúng, bỏ qua chỗ sai |

Cạm bẫy #3 nguy hiểm nhất vì nó *trông như đang chạy*. Bằng chứng bằng hình trong báo cáo spike:

| Token | Editor mặc định | Sau khi nối đúng |
|---|---|---|
| `IOContainer` | 🔴 gạch đỏ (CS0246) | ✅ sạch |
| `IO.StartButtonn` *(cố tình sai)* | ✅ không gạch — **sai!** | 🔴 gạch đỏ (CS1061) |

---

## Task 08.1 — `StudioWorkspace`

**File:** `src/DBI.Controller.Studio/Services/CodeAnalysis/StudioWorkspace.cs`

```csharp
public class StudioWorkspace {
    public void OpenProject(DbiProject project);          // CreateWorkspace + ProjectInfo nhiều DocumentInfo
    public void UpdateDocument(string path, string text); // gõ phím → cập nhật
    public void RegenerateIoDocument(string generated);   // Tag Table đổi → DỰNG LẠI project
    public Task<IReadOnlyList<CompileDiagnostic>> GetDiagnosticsAsync(string path);
}
```

**Khởi tạo `RoslynHost` trong `Application.Startup`, không phải `Main()` hay static ctor** (cạm bẫy #1).

Cách dựng project — đã kiểm chứng ở spike:

```csharp
var ws     = host.CreateWorkspace();
var projId = ProjectId.CreateNewId("UserProgram");

var docInfos = files.Select(f => DocumentInfo.Create(
    DocumentId.CreateNewId(projId, f.Name), f.Name,
    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(f.Text), VersionStamp.Create())),
    filePath: Path.Combine(projectDir, f.Name))).ToImmutableArray();

var projInfo = ProjectInfo.Create(projId, VersionStamp.Create(), "UserProgram", "UserProgram",
    LanguageNames.CSharp,
    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
    parseOptions:       host.ParseOptions,
    documents:          docInfos,
    metadataReferences: host.DefaultReferences);

ws.SetCurrentSolution(ws.CurrentSolution.AddProject(projInfo));
```

⚠️ **Dùng chung bộ reference với `RoslynCompilerService`** (phase-07). Lệch nhau → IntelliSense và Compile báo lỗi khác nhau, người dùng mất niềm tin.

## Task 08.1b — 🆕 Nối `RoslynCodeEditor` vào workspace của project

> **Task này do spike phát hiện. Bỏ qua thì IntelliSense chạy sai âm thầm** (cạm bẫy #3).

`InitializeAsync` tự tạo project riêng chỉ chứa văn bản truyền vào. Phải bổ sung các file còn lại vào **cùng project đó**:

```csharp
var docId = await editor.InitializeAsync(host, colors, dir, text, SourceCodeKind.Regular);

var edDoc = host.GetDocument(docId)!;
var edWs  = (RoslynWorkspace)edDoc.Project.Solution.Workspace;
var sol   = edWs.CurrentSolution;
foreach (var f in otherProjectFiles)          // Blocks/*.cs + Generated/IO.g.cs
    sol = sol.AddDocument(DocumentInfo.Create(
        DocumentId.CreateNewId(edDoc.Project.Id, f.Name), f.Name,
        loader: TextLoader.From(TextAndVersion.Create(SourceText.From(f.Text), VersionStamp.Create()))));
edWs.SetCurrentSolution(sol);
```

Cách thay thế sạch hơn — điểm mở rộng chính thức của RoslynPad (chưa thử ở spike):

```csharp
// CreatingDocumentEventArgs.DocumentId có setter
editor.CreatingDocument += (s, e) => { e.DocumentId = CreateDocumentInMyProject(e.TextContainer); };
```

**Nhiều tab mở cùng lúc:** mỗi `RoslynCodeEditor` có workspace riêng (`RoslynWorkspace.OpenDocumentId` là số ít). Cần quyết ở lúc code: mỗi editor một workspace chứa đủ project, hay dùng `CreatingDocument` để dùng chung một workspace.

## Task 08.1c — 🆕 Tag Table đổi → dựng lại project, KHÔNG patch document

Spike đo được: sau `WithDocumentText` + `SetCurrentSolution`, **compilation thấy tag mới nhưng completion vẫn trả danh sách cũ**. Nguyên nhân: `SetCurrentSolution` không phát workspace change event → cache của completion service không bị vô hiệu hoá. `TryApplyChanges` cũng vô dụng (trả `true` nhưng không đổi gì).

→ Khi sinh lại `IO.g.cs`, **dựng lại project trong workspace**. Với vài chục file thì chi phí không đáng kể.

## Task 08.2 — Tích hợp `RoslynPad.Editor`

Thay `avalonEdit:TextEditor` bằng `RoslynPad.Editor.RoslynCodeEditor`. Spike xác nhận có sẵn: syntax highlighting · line numbers · code folding · brace completion · squiggles.

| Tính năng | Mô tả |
|---|---|
| Completion | `Ctrl+Space` và tự động sau `.` |
| Signature help | Gợi ý tham số khi gõ `(` |
| Quick info | Hover hiện kiểu + XML doc comment |
| Squiggles | Gạch đỏ error / xanh warning real-time |
| Go to definition | `F12` — nhảy sang block khác trong project |

## Task 08.3 — Trải nghiệm `IO.`

Đây là thứ người dùng cảm nhận rõ nhất:

```
IO.Sta│
     ┌──────────────────────────────────┐
     │ 🏷️ StartButton    bool  (Input)  │
     │    Nút start tủ điện              │
     │    FactoryIO_3D / Input_0         │
     ├──────────────────────────────────┤
     │ 🏷️ StationReady   bool  (Input)  │
     │ 🏷️ StatusWord     int   (Memory) │
     └──────────────────────────────────┘
```

Có được là nhờ ADR-002 — `IO.g.cs` sinh property thật với XML doc comment chứa device + address (phase-06 Task 06.3).

**Test then chốt:** gõ `IO.StartButtonn` (thừa chữ n) phải gạch đỏ **ngay lập tức**, không đợi compile. Đây là bug B-1 đã bị giết hoàn toàn.

## Task 08.4 — Error List real-time

Mở rộng Error List của phase-07: diagnostics cập nhật khi gõ (debounce 500ms), không cần bấm Compile.

Phân biệt: 🔴 Error · 🟡 Warning · 🔵 Info. Lọc theo mức và theo file.

## Task 08.5 — Rename ngữ nghĩa

Có Roslyn workspace rồi thì nâng cấp rename của phase-05 (Task 05.6) từ thay thế chuỗi lên rename thật:
- Đổi tên block → đổi tên class + mọi chỗ tham chiếu
- Đổi tên tag → cảnh báo trước những chỗ trong code sẽ hỏng, có tuỳ chọn tự sửa

## Task 08.6 — 🔻 PHƯƠNG ÁN LÙI (nếu spike thất bại)

Nếu `RoslynPad.Editor` không dùng được, tự làm trên `AvalonEdit`:

| Tính năng | Cách làm | % giá trị |
|---|---|---|
| Completion cho `IO.` | Tự viết `CompletionWindow` của AvalonEdit, nguồn dữ liệu = Tag Table (không cần Roslyn) | 40% |
| Completion cho SDK Primitives | Danh sách tĩnh từ reflection `DBI.Controller.SDK` | 10% |
| Error List | Từ `RoslynCompilerService.Diagnostics` sau khi Compile (không real-time) | 15% |
| Squiggles | Từ cùng nguồn, vẽ sau mỗi lần Compile | 5% |
| **Tổng** | | **~70%** |

Bỏ: signature help, quick info, go-to-definition, rename ngữ nghĩa.

**Quan trọng:** phần `IO.` completion — thứ người dùng dùng nhiều nhất — vẫn giữ được **mà không cần Roslyn**, vì Tag Table đã là nguồn sự thật (ADR-002). Đây chính là lý do quyết định ADR-002 đáng giá kể cả khi spike hỏng.

---

## Definition of Done

> Spike đã PASS → dùng danh sách này. Phần "phương án lùi" bên dưới giữ lại làm tài liệu.

- [ ] `RoslynPad.Editor` tích hợp, không xung đột assembly (`dotnet restore` 0 cảnh báo NU1608)
- [ ] `RoslynHost` khởi tạo trong `Application.Startup`, không phải `Main()`
- [ ] 🆕 Editor nối vào workspace của project — **test hồi quy:** mở `Conveyor.cs`, gõ `IO.SaiChinhTa` → gạch đỏ **ở đúng token đó**, và `IOContainer` **không** bị gạch
- [ ] 🆕 Sửa Tag Table → dựng lại project → completion thấy tag mới, không cần restart
- [ ] Completion hoạt động cho `IO.`, SDK primitives, và class trong project
- [ ] Gõ `IO.StartButtonn` gạch đỏ ngay, không cần Compile
- [ ] Hover tag hiện kiểu + comment + device/address
- [ ] `F12` nhảy được sang block khác
- [ ] Sửa Tag Table → completion cập nhật ngay, không cần restart
- [ ] Error List real-time, debounce 500ms
- [ ] Rename block đổi mọi tham chiếu
- [ ] Diagnostics của IntelliSense **khớp** với diagnostics của Compile
- [ ] Project 20 block: gõ phím không giật (< 50ms độ trễ)

~~**Nếu spike thất bại (phương án lùi):**~~ — không dùng, spike đã PASS 10/10.
