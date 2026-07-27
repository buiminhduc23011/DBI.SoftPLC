using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;
using DBI.Controller.Studio.Core.ViewModels;

namespace DBI.Controller.Tests;

/// <summary>Hộp thoại giả — trả lời sẵn, ghi lại đã hỏi những gì.</summary>
internal sealed class ScriptedPrompt : IUserPrompt
{
    public string? ProjectToOpen { get; set; }
    public NewProjectRequest? NewProject { get; set; }
    public NewBlockRequest? NewBlock { get; set; }
    public string? TextAnswer { get; set; }
    public bool ConfirmAnswer { get; set; } = true;

    public List<string> Errors { get; } = new();
    public int ConfirmCount { get; private set; }

    public string? AskProjectToOpen() => ProjectToOpen;
    public NewProjectRequest? AskNewProjectLocation() => NewProject;
    public string? AskSaveProjectAs(string suggestedName) => null;
    public NewBlockRequest? AskNewBlock(bool canCreateMain) => NewBlock;
    public string? AskText(string title, string prompt, string initialValue) => TextAnswer;

    public bool Confirm(string message, string title)
    {
        ConfirmCount++;
        return ConfirmAnswer;
    }

    public void ShowError(string message, string title) => Errors.Add(message);
    public void ShowInformation(string message, string title) { }
}

internal sealed class FakeThemeSwitcher : IThemeSwitcher
{
    public bool IsDark { get; private set; }
    public int ApplyCount { get; private set; }

    public void Apply(bool dark)
    {
        IsDark = dark;
        ApplyCount++;
    }
}

internal sealed class FakeLayoutPersistence : ILayoutPersistence
{
    public string? SavedTo { get; private set; }
    public string? RestoredFrom { get; private set; }
    public int ResetCount { get; private set; }

    public void Save(string layoutFilePath) => SavedTo = layoutFilePath;

    public bool Restore(string layoutFilePath)
    {
        RestoredFrom = layoutFilePath;
        return File.Exists(layoutFilePath);
    }

    public void ResetToDefault() => ResetCount++;
}

public class ShellViewModelTests
{
    private static (ShellViewModel Shell, ScriptedPrompt Prompt, FakeRuntimeClient Runtime) NewShell(
        TempWorkspace temp, FakeLayoutPersistence? layout = null)
    {
        var prompt = new ScriptedPrompt();
        var runtime = new FakeRuntimeClient();

        var shell = new ShellViewModel(
            new ProjectService(new RecentProjectsService(temp.SettingsDirectory)),
            runtime,
            prompt,
            new IoCodeGenerator(),
            new FakeThemeSwitcher(),
            layout);

        return (shell, prompt, runtime);
    }

    // ── Vòng đời project ─────────────────────────────────────────────────────────

    [Fact]
    public void TaoProjectMoi_DungCayVaCapNhatThanhTrangThai()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        Assert.NotNull(shell.Project);
        Assert.Equal("MyMachine", shell.StatusBar.ProjectName);

        var root = Assert.Single(shell.ProjectTree.Roots);
        Assert.Equal("MyMachine", root.Title);
        Assert.Equal(7, root.Children.Count);   // Device Config / Diagnostics / Blocks / Tags / Generated / Watch / Devices
        Assert.Contains(root.Children, c => c.Title == "Program Blocks" && c.Children.Count == 1);
    }

    [Fact]
    public void ThemKhoiMoi_SinhTepVaMoDungTab()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        prompt.NewBlock = new NewBlockRequest("Conveyor", BlockKind.FunctionBlock, "Logic bang tai");
        shell.AddBlockCommand.Execute(null);

        Assert.Contains(shell.Project!.Blocks, b => b.Name == "Conveyor" && b.Kind == BlockKind.FunctionBlock);
        Assert.True(File.Exists(temp.At("Blocks", "Conveyor.cs")));
        Assert.Contains("class Conveyor", File.ReadAllText(temp.At("Blocks", "Conveyor.cs")));
        Assert.Contains(shell.Editors.Documents.OfType<CodeEditorViewModel>(), d => d.Block.Name == "Conveyor");
    }

    [Fact]
    public void KhongTaoMainThuHai()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        prompt.NewBlock = new NewBlockRequest("Main2", BlockKind.Main, "khong hop le");
        shell.AddBlockCommand.Execute(null);

        Assert.Single(prompt.Errors);
        Assert.DoesNotContain(shell.Project!.Blocks, b => b.Name == "Main2");
    }

    [Fact]
    public void DoiTenKhoi_DoiCaTepClassVaDbiproj()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        prompt.NewBlock = new NewBlockRequest("Conveyor", BlockKind.FunctionBlock, "");
        shell.AddBlockCommand.Execute(null);

        var blockNode = shell.ProjectTree.Roots[0].Children
            .Single(c => c.Title == "Program Blocks").Children.Single(c => c.Title == "Conveyor");

        prompt.TextAnswer = "Line1";
        shell.RenameBlockCommand.Execute(blockNode);

        Assert.DoesNotContain(shell.Project!.Blocks, b => b.Name == "Conveyor");
        var renamed = Assert.Single(shell.Project.Blocks, b => b.Name == "Line1");
        Assert.Equal("Blocks/Line1.cs", renamed.FileName);
        Assert.True(File.Exists(temp.At("Blocks", "Line1.cs")));
        Assert.False(File.Exists(temp.At("Blocks", "Conveyor.cs")));
        Assert.Contains("class Line1", File.ReadAllText(temp.At("Blocks", "Line1.cs")));
    }

    [Fact]
    public void XoaKhoi_XoaDungTep()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        prompt.NewBlock = new NewBlockRequest("Conveyor", BlockKind.FunctionBlock, "");
        shell.AddBlockCommand.Execute(null);

        var blockNode = shell.ProjectTree.Roots[0].Children
            .Single(c => c.Title == "Program Blocks").Children.Single(c => c.Title == "Conveyor");

        shell.DeleteBlockCommand.Execute(blockNode);

        Assert.DoesNotContain(shell.Project!.Blocks, b => b.Name == "Conveyor");
        Assert.False(File.Exists(temp.At("Blocks", "Conveyor.cs")));
    }

    [Fact]
    public void DatKhoiKhacLamMain_HaMainCuXuongFunctionBlock()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);
        prompt.NewBlock = new NewBlockRequest("Conveyor", BlockKind.FunctionBlock, "");
        shell.AddBlockCommand.Execute(null);

        var blockNode = shell.ProjectTree.Roots[0].Children
            .Single(c => c.Title == "Program Blocks").Children.Single(c => c.Title == "Conveyor");

        shell.SetMainBlockCommand.Execute(blockNode);

        Assert.Equal(BlockKind.Main, shell.Project!.Blocks.Single(b => b.Name == "Conveyor").Kind);
        Assert.Equal(BlockKind.FunctionBlock, shell.Project.Blocks.Single(b => b.Name == "Main").Kind);
    }

    [Fact]
    public void MoProjectSchemaTuongLai_HienLoiRoRangKhongCrash()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);
        shell.CloseProjectCommand.Execute(null);

        string path = temp.At("MyMachine.dbiproj");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"1.0\"", "\"9.0\""));

        prompt.ProjectToOpen = path;
        shell.OpenProjectCommand.Execute(null);

        Assert.Null(shell.Project);
        Assert.Single(prompt.Errors);
        Assert.Contains("9.0", prompt.Errors[0]);
    }

    [Fact]
    public void DongProject_ConThayDoiChuaLuu_HoiLaiTruoc()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        var editor = shell.Editors.OpenBlock(shell.Project!.Blocks[0], shell.Project.ProjectDirectory);
        editor.Text += "\n// sửa";

        prompt.ConfirmAnswer = false;
        shell.CloseProjectCommand.Execute(null);

        Assert.Equal(1, prompt.ConfirmCount);
        Assert.NotNull(shell.Project);           // người dùng huỷ -> project vẫn mở

        prompt.ConfirmAnswer = true;
        shell.CloseProjectCommand.Execute(null);

        Assert.Null(shell.Project);
        Assert.Empty(shell.Editors.Documents);
    }

    // ── Tài liệu ─────────────────────────────────────────────────────────────────

    [Fact]
    public void MoNhieuTabVaDongTungCai()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        var project = shell.Project!;
        File.WriteAllText(temp.At("Blocks", "Conveyor.cs"), "// conveyor");
        project.Blocks.Add(new CodeBlock { Name = "Conveyor", FileName = "Blocks/Conveyor.cs" });

        shell.Editors.OpenBlock(project.Blocks[0], project.ProjectDirectory);
        var second = shell.Editors.OpenBlock(project.Blocks[1], project.ProjectDirectory);

        Assert.Equal(2, shell.Editors.Documents.Count);
        Assert.Same(second, shell.Editors.ActiveDocument);

        shell.CloseDocumentCommand.Execute(second);

        Assert.Single(shell.Editors.Documents);
        Assert.Equal("Main", shell.Editors.ActiveDocument!.BaseTitle);
    }

    [Fact]
    public void MoLaiKhoiDaMo_ChuyenSangTabCu_KhongTaoTabTrung()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        var block = shell.Project!.Blocks[0];
        var first = shell.Editors.OpenBlock(block, shell.Project.ProjectDirectory);
        var again = shell.Editors.OpenBlock(block, shell.Project.ProjectDirectory);

        Assert.Same(first, again);
        Assert.Single(shell.Editors.Documents);
    }

    [Fact]
    public async Task SuaRoiLuu_XoaDauSaoTrenTieuDeTab()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        var editor = shell.Editors.OpenBlock(shell.Project!.Blocks[0], shell.Project.ProjectDirectory);
        Assert.Equal("Main", editor.Title);

        editor.Text += "\n// sửa";
        Assert.True(editor.IsDirty);
        Assert.Equal("Main *", editor.Title);

        await shell.SaveAllCommand.ExecuteAsync(null);

        Assert.False(editor.IsDirty);
        Assert.Equal("Main", editor.Title);
        Assert.Contains("// sửa", File.ReadAllText(temp.At("Blocks", "Main.cs")));
    }

    [Fact]
    public void MoKhoiThieuTepCs_GhiCanhBaoVaoInspector_KhongCrash()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        File.Delete(temp.At("Blocks", "Main.cs"));
        shell.Editors.OpenBlock(shell.Project!.Blocks[0], shell.Project.ProjectDirectory);

        Assert.Contains(shell.Inspector.Information, e => e.Message.Contains("không tồn tại"));
    }

    // ── Inspector ────────────────────────────────────────────────────────────────

    [Fact]
    public void ChonNodeTrenCay_InspectorHienThuocTinhTuongUng()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        var blockNode = shell.ProjectTree.Roots[0].Children
            .Single(c => c.Title == "Program Blocks").Children[0];

        shell.ProjectTree.SelectedNode = blockNode;

        Assert.Equal("Main", shell.Inspector.SelectedNodeTitle);
        Assert.Contains(shell.Inspector.Properties, p => p.Name == "Tệp" && p.Value == "Blocks/Main.cs");
        Assert.Contains(shell.Inspector.Properties, p => p.Name == "Loại khối" && p.Value == "Main");
    }

    [Fact]
    public void Inspector_GioiHanSoDongLog()
    {
        var inspector = new InspectorViewModel();

        for (int i = 0; i < InspectorViewModel.MaxLogEntries + 50; i++)
            inspector.LogInformation($"dòng {i}");

        Assert.Equal(InspectorViewModel.MaxLogEntries, inspector.Information.Count);
        Assert.Equal("dòng 549", inspector.Information[^1].Message);
    }

    [Fact]
    public void Fault_TuRuntime_GhiVaoTabDiagnostics()
    {
        using var temp = new TempWorkspace();
        var (shell, _, runtime) = NewShell(temp);

        runtime.SimulateFault("Chia cho 0 ở khối Conveyor");

        Assert.Contains(shell.Inspector.Diagnostics,
            e => e.Severity == IssueSeverity.Error && e.Message.Contains("Chia cho 0"));
    }

    // ── Theme và layout ──────────────────────────────────────────────────────────

    [Fact]
    public void DoiTheme_QuaIThemeSwitcher_KhongPhaiDoiChuoiMauTrenViewModel()
    {
        using var temp = new TempWorkspace();
        var theme = new FakeThemeSwitcher();

        var shell = new ShellViewModel(
            new ProjectService(new RecentProjectsService(temp.SettingsDirectory)),
            new FakeRuntimeClient(), new ScriptedPrompt(), new IoCodeGenerator(), theme);

        Assert.False(shell.IsDarkMode);

        shell.ToggleThemeCommand.Execute(null);

        Assert.True(theme.IsDark);
        Assert.True(shell.IsDarkMode);
        Assert.Contains("Light", shell.ThemeToggleText);
    }

    [Fact]
    public void ResetLayout_GoiXuongLopLuuBoCuc()
    {
        using var temp = new TempWorkspace();
        var layout = new FakeLayoutPersistence();
        var (shell, _, _) = NewShell(temp, layout);

        shell.ResetLayoutCommand.Execute(null);

        Assert.Equal(1, layout.ResetCount);
        Assert.Contains(shell.Inspector.Information, e => e.Message.Contains("mặc định"));
    }

    [Fact]
    public void Layout_LuuVaoThuMucDbistudioCuaProject()
    {
        using var temp = new TempWorkspace();
        var layout = new FakeLayoutPersistence();
        var (shell, prompt, _) = NewShell(temp, layout);

        Assert.Null(shell.LayoutFilePath);   // chưa mở project thì chưa có chỗ lưu

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        shell.SaveLayout();

        Assert.Equal(Path.Combine(temp.Root, ProjectService.StudioFolder, "layout.xml"), layout.SavedTo);
        Assert.True(Directory.Exists(Path.GetDirectoryName(layout.SavedTo!)!));
    }

    // ── Bảng tra ContentId → ViewModel ───────────────────────────────────────────

    [Fact]
    public void ResolveContent_TraDungViewModelChoTungContentId()
    {
        using var temp = new TempWorkspace();
        var (shell, prompt, _) = NewShell(temp);

        prompt.NewProject = new NewProjectRequest(temp.Root, "MyMachine");
        shell.NewProjectCommand.Execute(null);

        var editor = shell.Editors.OpenBlock(shell.Project!.Blocks[0], shell.Project.ProjectDirectory);

        Assert.Same(shell.ProjectTree, shell.ResolveContent("ProjectTree"));
        Assert.Same(shell.Inspector, shell.ResolveContent("Inspector"));
        Assert.Same(shell.TaskCards, shell.ResolveContent("TaskCards"));
        Assert.Same(editor, shell.ResolveContent(editor.ContentId));

        // ContentId lạ -> null, để lớp bố cục bỏ qua thay vì dựng panel rỗng.
        Assert.Null(shell.ResolveContent("KhongCoPanelNay"));
        Assert.Null(shell.ResolveContent(null));
    }

    [Fact]
    public void ContentId_CuaTabMaNguon_OnDinhGiuaCacPhien()
    {
        var block = new CodeBlock { Name = "Conveyor", FileName = "Blocks/Conveyor.cs" };

        Assert.Equal("Block:Blocks/Conveyor.cs", CodeEditorViewModel.ContentIdFor(block));

        // Hai khối trùng tên ở thư mục khác nhau phải ra ContentId khác nhau.
        var other = new CodeBlock { Name = "Conveyor", FileName = "Blocks/Line2/Conveyor.cs" };
        Assert.NotEqual(CodeEditorViewModel.ContentIdFor(block), CodeEditorViewModel.ContentIdFor(other));
    }

    // ── Cảnh báo khi đóng Studio ─────────────────────────────────────────────────

    [Fact]
    public async Task DongStudioLucMayDangChay_CoCanhBaoRangKhongDungMay()
    {
        using var temp = new TempWorkspace();
        var (shell, _, runtime) = NewShell(temp);

        Assert.Null(shell.ClosingWarning);

        await runtime.ConnectAsync(new RuntimeConnectionTarget());
        await runtime.DeployAsync(
            Array.Empty<byte>(), SampleAssembly.ConveyorRoutes(), SampleAssembly.SimulationDevice());

        Assert.Equal(RuntimeState.Running, shell.StatusBar.RuntimeState);
        Assert.NotNull(shell.ClosingWarning);
        Assert.Contains("không dừng máy", shell.ClosingWarning, StringComparison.OrdinalIgnoreCase);
    }
}
