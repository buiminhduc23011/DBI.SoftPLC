# Phase 07 — Multi-file Compile & Deploy 🏁

**Status:** ⬜ Pending | **Phụ thuộc:** phase-03, phase-05, phase-06 | **Nội dung BRIEF:** #3

> 🏁 **RANH GIỚI MVP.** Hết phase này Studio là engineering tool dùng được thật: tạo project → khai báo tag → viết nhiều khối C# → compile → deploy → máy chạy. Dừng ở đây vẫn có sản phẩm.

---

## Task 07.1 — Nâng cấp `RoslynCompilerService`

[RoslynCompilerService.cs](../../src/DBI.Controller.Studio/Services/RoslynCompilerService.cs) hiện 62 dòng, compile **một** source string.

```csharp
public record CompileRequest(
    IReadOnlyList<SourceFile> Sources,      // Blocks/*.cs + Generated/IO.g.cs
    string AssemblyName,
    OptimizationLevel Optimization);

public record SourceFile(string Path, string Text);

public record CompileResult(
    bool Success,
    byte[]? AssemblyBytes,
    byte[]? PdbBytes,                        // cần cho stack trace có số dòng
    IReadOnlyList<CompileDiagnostic> Diagnostics);

public record CompileDiagnostic(
    string Id, DiagnosticSeverity Severity, string Message,
    string FilePath, int Line, int Column);
```

**Cấu hình bắt buộc:**

| Mục | Giá trị | Lý do |
|---|---|---|
| Target | `net8.0` | Runtime host là `net8.0` (Constraint C-6). Studio `net10.0-windows` chỉ *biên dịch*, không *thực thi* |
| `OutputKind` | `DynamicallyLinkedLibrary` | |
| `LangVersion` | `latest` | khớp `Directory.Build.props` |
| PDB | `DebugInformationFormat.PortablePdb`, embed | stack trace có số dòng khi máy fault |
| References | `DBI.Controller.Core`, `DBI.Controller.SDK`, + BCL trametadata | |

**Trap cần tránh:** không dùng `typeof(object).Assembly.Location` để lấy reference BCL — Studio chạy net10, sẽ nhặt nhầm BCL net10 vào assembly target net8. Dùng reference assemblies của `net8.0` (`Microsoft.NETCore.App.Ref`) hoặc `Basic.Reference.Assemblies` package.

## Task 07.2 — Pipeline Build

```
1. Lưu mọi document đang dirty
2. Validate project (phase-01 + phase-06)         → dừng nếu có 🔴 Error
3. Sinh Generated/IO.g.cs                          (phase-06)
4. Gom Blocks/*.cs + Generated/IO.g.cs
5. Roslyn CSharpCompilation.Emit()
6. Diagnostics → Inspector → Information
7. Thành công → giữ byte[] trong bộ nhớ, bật nút Deploy
```

Chạy trên background thread, có progress. **Không** treo UI.

## Task 07.3 — Pipeline Deploy

```
1. Build (nếu chưa build hoặc project đã dirty)
2. Kết nối Runtime nếu chưa (RuntimeProcessLauncher, phase-03)
3. Export TagRoute (phase-06) + DeviceSpec (từ Project.Devices)
4. client.DeployAsync(assembly, SwapMode.ColdRestart, routes, devices)
5. Runtime: Stop → xả output xuống thiết bị → Unload → Load → cấu hình driver → Start
6. Runtime lưu last-deploy/ xuống đĩa (ADR-005)
7. Poll status tới khi Running hoặc Faulted
8. Báo kết quả ở Inspector → Diagnostics
```

`SwapMode` luôn là `ColdRestart` ở v1 ([ADR-004](decisions/ADR-004-deploy-cold-restart.md)). Trường này có sẵn trong contract để sau này thêm Hot Reload không phải đổi giao thức.

**Cảnh báo an toàn khi máy đang chạy:**
```
┌─────────────────────────────────────────────┐
│ ⚠️  Runtime đang RUN                         │
│                                             │
│ Deploy sẽ DỪNG máy, nạp chương trình mới    │
│ rồi khởi động lại.                          │
│                                             │
│ Output sẽ về trạng thái an toàn (OFF)       │
│ trong quá trình chuyển đổi.                 │
│                                             │
│        [ Huỷ ]   [ Deploy ]                 │
└─────────────────────────────────────────────┘
```

## Task 07.4 — Error List

DataGrid ở Inspector → **Information**:

```
┌───┬────────┬─────────────────────────────────────┬──────────────┬──────┐
│   │ Code   │ Description                         │ File         │ Line │
├───┼────────┼─────────────────────────────────────┼──────────────┼──────┤
│ 🔴│ CS0103 │ Tên 'IO.StartButtonn' không tồn tại │ Conveyor.cs  │  23  │
│ 🟡│ CS0168 │ Biến '_temp' khai báo nhưng không dùng│ Main.cs      │  15  │
└───┴────────┴─────────────────────────────────────┴──────────────┴──────┘
```

Double-click → mở file, nhảy tới dòng, highlight.

Ánh xạ `FilePath` từ Roslyn về đúng `Blocks/*.cs` (dùng `SourceText.From(text, path:)` khi tạo `SyntaxTree`).

## Task 07.5 — Nút toolbar

| Nút | Phím tắt | Điều kiện bật |
|---|---|---|
| 🔨 Compile | `Ctrl+Shift+B` | Có project mở |
| 🚀 Deploy | `F5` | Build thành công |
| ▶️ Start | — | Đã kết nối + có program |
| ⏹️ Stop | — | State = Running |
| 🔄 Reset | — | State = Faulted |

Xoá nút giả `BtnDeploy_Click` hiện tại ở [MainWindow.xaml.cs](../../src/DBI.Controller.Studio/MainWindow.xaml.cs).

---

## Definition of Done

- [x] Compile nhiều file `.cs` thành một assembly
- [x] Assembly target **`net10.0`**, Runtime `net10.0` nạp được
- [x] Không lẫn reference BCL của net10 vào assembly net10
- [x] PDB embed; fault trong code người dùng cho stack trace có số dòng đúng
- [x] Diagnostics ánh xạ đúng file + số dòng; double-click nhảy đúng chỗ
- [x] Build chạy background, UI không treo
- [x] Deploy end-to-end: Studio → Runtime → scan chạy → status `Running`, `CycleCount` tăng
- [x] Cảnh báo an toàn hiện khi deploy lúc `Running`
- [x] Sau deploy thành công, Studio thông báo rõ: "Máy sẽ TỰ CHẠY LẠI sau khi mất điện"
- [x] Status bar hiện biểu tượng ⚡ khi `autoStart = true`
- [x] Deploy code lỗi → Runtime giữ chương trình cũ, không rơi vào trạng thái không xác định
- [x] Deploy code ném exception lúc `Execute()` → SafetyCatch bắt, output về safe state, Studio hiện `Faulted` + stack trace
- [x] Phím tắt `Ctrl+Shift+B` / `F5` hoạt động
- [ ] 🏁 **Kịch bản nghiệm thu MVP:** người dùng mới, chưa đọc tài liệu, tạo được project băng tải chạy trên Factory I/O **trong vòng 10 phút**
