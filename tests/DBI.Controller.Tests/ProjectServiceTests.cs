using System.Text.Json;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;

namespace DBI.Controller.Tests;

/// <summary>Thư mục tạm tự dọn — mỗi test một thư mục riêng, không đụng nhau.</summary>
public sealed class TempWorkspace : IDisposable
{
    public string Root { get; }
    public string SettingsDirectory { get; }

    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "dbi-studio-tests", Guid.NewGuid().ToString("N"));
        SettingsDirectory = Path.Combine(Root, "_settings");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(SettingsDirectory);
    }

    /// <summary>Ghép đường dẫn tương đối với gốc thư mục tạm.</summary>
    public string At(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    public ProjectService NewService() => new(new RecentProjectsService(SettingsDirectory));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { /* thư mục tạm còn khoá — kệ, OS dọn sau */ }
    }
}

public class ProjectServiceTests
{
    // ── New ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateNew_SinhCayThuMucDayDu()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();

        var result = service.CreateNew(temp.Root, "MyMachine");

        Assert.True(result.Success);
        Assert.True(File.Exists(temp.At("MyMachine.dbiproj")));
        Assert.True(File.Exists(temp.At("Blocks", "Main.cs")));
        Assert.True(File.Exists(temp.At(".gitignore")));
        Assert.True(Directory.Exists(temp.At("Generated")));
    }

    [Fact]
    public void CreateNew_ProjectMoiCoDungMotKhoiMain()
    {
        using var temp = new TempWorkspace();
        var result = temp.NewService().CreateNew(temp.Root, "MyMachine");

        var main = Assert.Single(result.Project!.Blocks);
        Assert.Equal(BlockKind.Main, main.Kind);
        Assert.Equal("Blocks/Main.cs", main.FileName);
    }

    [Fact]
    public void CreateNew_MainCs_KeThuaControllerProgram_VaCoDuBaHam()
    {
        using var temp = new TempWorkspace();
        temp.NewService().CreateNew(temp.Root, "MyMachine");

        string source = File.ReadAllText(temp.At("Blocks", "Main.cs"));

        Assert.Contains("using DBI.Controller.SDK;", source);
        Assert.Contains("namespace UserProgram;", source);
        Assert.Contains(": ControllerProgram", source);
        Assert.Contains("public override void OnStart()", source);
        Assert.Contains("public override void Execute()", source);
        Assert.Contains("public override void OnStop()", source);
    }

    [Fact]
    public void CreateNew_GitIgnore_BoQuaThuMucLayoutVaBuild()
    {
        using var temp = new TempWorkspace();
        temp.NewService().CreateNew(temp.Root, "MyMachine");

        string gitignore = File.ReadAllText(temp.At(".gitignore"));

        Assert.Contains(".dbistudio/", gitignore);
        Assert.Contains("bin/", gitignore);
        Assert.Contains("obj/", gitignore);
    }

    [Fact]
    public void CreateNew_TrungTen_TraLoiTuChoi()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();

        service.CreateNew(temp.Root, "MyMachine");
        var second = service.CreateNew(temp.Root, "MyMachine");

        Assert.False(second.Success);
        Assert.Contains(second.Issues, i => i.Severity == IssueSeverity.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad/name")]
    public void CreateNew_TenKhongHopLe_TraLoiTuChoi(string name)
    {
        using var temp = new TempWorkspace();

        var result = temp.NewService().CreateNew(temp.Root, name);

        Assert.False(result.Success);
    }

    // ── Round-trip ───────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_LuuRoiMoLai_GiuNguyenToanBoDuLieu()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();

        var created = service.CreateNew(temp.Root, "MyMachine").Project!;
        created.Description = "Băng tải phân loại — dây chuyền 3";
        created.Runtime.ScanIntervalMs = 25;
        created.Runtime.AutoStart = false;
        created.Devices.Add(new DeviceConfig
        {
            Name = "FactoryIO_3D",
            DriverType = "DBI.Controller.Driver.FactoryIO",
            Settings = { ["ip"] = "127.0.0.1", ["port"] = "502" }
        });
        created.TagTables[0].Tags.Add(new Tag
        {
            Name = "StartButton",
            DataType = TagDataType.Bool,
            Direction = TagDirection.Input,
            Device = "FactoryIO_3D",
            Address = "Input_0",
            Comment = "Nút khởi động — thường mở"
        });
        created.TagTables[0].Tags.Add(new Tag
        {
            Name = "Temperature",
            DataType = TagDataType.Real,
            Direction = TagDirection.Input,
            Device = "FactoryIO_3D",
            Address = "40001"
        });
        created.WatchTables.Add(new WatchTable { Name = "Khởi động", TagNames = { "StartButton" } });

        service.Save();
        service.Close();

        var reopened = temp.NewService().Open(temp.At("MyMachine.dbiproj"));

        Assert.True(reopened.Success);
        AssertDeepEqual(created, reopened.Project!);
    }

    private static void AssertDeepEqual(DbiProject expected, DbiProject actual)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        Assert.Equal(JsonSerializer.Serialize(expected, options), JsonSerializer.Serialize(actual, options));
    }

    [Fact]
    public void FileDbiproj_CoIndent_GitDiffDocDuoc()
    {
        using var temp = new TempWorkspace();
        temp.NewService().CreateNew(temp.Root, "MyMachine");

        string json = File.ReadAllText(temp.At("MyMachine.dbiproj"));

        Assert.Contains("\n", json);
        Assert.Contains("  \"name\": \"MyMachine\"", json);
        // Enum ghi bằng tên, không phải số — số thì diff vô nghĩa với người đọc.
        Assert.Contains("\"kind\": \"Main\"", json);
    }

    [Fact]
    public void FileDbiproj_KhongEscapeTiengViet()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();

        var project = service.CreateNew(temp.Root, "MyMachine").Project!;
        project.Description = "Băng tải phân loại";
        service.Save();

        string json = File.ReadAllText(temp.At("MyMachine.dbiproj"));

        Assert.Contains("Băng tải phân loại", json);
        Assert.DoesNotContain("\\u", json);
    }

    // ── Schema version ───────────────────────────────────────────────────────────

    [Fact]
    public void Open_SchemaVersionTuongLai_TuChoiKemThongBaoRo()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();
        service.CreateNew(temp.Root, "MyMachine");
        service.Close();

        string path = temp.At("MyMachine.dbiproj");
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"1.0\"", "\"2.0\""));

        var result = temp.NewService().Open(path);

        Assert.False(result.Success);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Contains("2.0", issue.Message);
        Assert.Contains("cập nhật", issue.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_FileJsonHong_TraLoiKhongCrash()
    {
        using var temp = new TempWorkspace();
        string path = temp.At("Broken.dbiproj");
        File.WriteAllText(path, "{ đây không phải JSON");

        var result = temp.NewService().Open(path);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Open_FileKhongTonTai_TraLoiKhongCrash()
    {
        using var temp = new TempWorkspace();

        var result = temp.NewService().Open(temp.At("KhongCo.dbiproj"));

        Assert.False(result.Success);
    }

    // ── Đối chiếu block ↔ file .cs ───────────────────────────────────────────────

    [Fact]
    public void Open_BlockKhaiBaoNhungThieuFileCs_SinhWarningKhongCrash()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();
        service.CreateNew(temp.Root, "MyMachine");
        service.Close();

        File.Delete(temp.At("Blocks", "Main.cs"));

        var result = temp.NewService().Open(temp.At("MyMachine.dbiproj"));

        Assert.True(result.Success);
        Assert.Contains(result.Issues, i =>
            i.Severity == IssueSeverity.Warning && i.Target == "Main" && i.Message.Contains("không tồn tại"));
    }

    [Fact]
    public void Open_FileCsMoCoi_SinhWarningHoiThemVaoProject()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();
        service.CreateNew(temp.Root, "MyMachine");
        service.Close();

        File.WriteAllText(temp.At("Blocks", "Conveyor.cs"), "// khối viết tay chưa khai vào project");

        var result = temp.NewService().Open(temp.At("MyMachine.dbiproj"));

        Assert.True(result.Success);
        Assert.Contains(result.Issues, i =>
            i.Severity == IssueSeverity.Warning && i.Target == "Conveyor");
    }

    // ── Save atomic ──────────────────────────────────────────────────────────────

    [Fact]
    public void Save_KhongDeLaiFileTam()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();
        service.CreateNew(temp.Root, "MyMachine");

        service.Save();
        service.Save();

        Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp"));
    }

    [Fact]
    public void Save_GhiDeFileCu_NoiDungCuKhongBiCatDoDang()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();
        var project = service.CreateNew(temp.Root, "MyMachine").Project!;

        // Nội dung lần 2 dài hơn hẳn lần 1 — ghi đè tại chỗ mà lỗi sẽ để lại đuôi rác.
        project.Description = new string('X', 5000);
        service.Save();

        string json = File.ReadAllText(temp.At("MyMachine.dbiproj"));
        var reparsed = JsonSerializer.Deserialize<DbiProject>(json);

        Assert.NotNull(reparsed);
        Assert.Equal(5000, reparsed!.Description.Length);
    }

    [Fact]
    public void Save_KhiChuaMoProject_NemLoiRoRang()
    {
        using var temp = new TempWorkspace();

        var ex = Assert.Throws<InvalidOperationException>(() => temp.NewService().Save());
        Assert.Contains("project", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_XoaCoIsDirty()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();
        var project = service.CreateNew(temp.Root, "MyMachine").Project!;

        project.IsDirty = true;
        service.Save();

        Assert.False(project.IsDirty);
    }

    // ── SaveAs ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SaveAs_CopyToanBoCayThuMucSangViTriMoi()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();
        service.CreateNew(temp.Root, "MyMachine");

        string newDir = temp.At("copy");
        service.SaveAs(Path.Combine(newDir, "MyMachineV2.dbiproj"));

        Assert.True(File.Exists(Path.Combine(newDir, "MyMachineV2.dbiproj")));
        Assert.True(File.Exists(Path.Combine(newDir, "Blocks", "Main.cs")));

        // Bản gốc còn nguyên.
        Assert.True(File.Exists(temp.At("MyMachine.dbiproj")));

        // Project đang mở đã trỏ sang vị trí mới.
        Assert.Equal("MyMachineV2", service.Current!.Name);
        Assert.Equal(Path.GetFullPath(Path.Combine(newDir, "MyMachineV2.dbiproj")), service.Current.ProjectFilePath);
    }

    // ── Close ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Close_XoaProjectDangMo_VaPhatSuKien()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();
        service.CreateNew(temp.Root, "MyMachine");

        int events = 0;
        service.CurrentChanged += (_, _) => events++;

        service.Close();

        Assert.Null(service.Current);
        Assert.Equal(1, events);
    }

    // ── Recent projects ──────────────────────────────────────────────────────────

    [Fact]
    public void Recent_MoiNhatDungDau_VaKhongTrungLap()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();

        service.CreateNew(temp.At("a"), "A");
        service.CreateNew(temp.At("b"), "B");
        service.Open(temp.At("a", "A.dbiproj"));

        var recent = service.RecentProjects.Items;

        Assert.Equal(2, recent.Count);
        Assert.EndsWith("A.dbiproj", recent[0]);
        Assert.EndsWith("B.dbiproj", recent[1]);
    }

    [Fact]
    public void Recent_GioiHan10Muc()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();

        for (int i = 0; i < 15; i++)
            service.CreateNew(temp.At($"p{i}"), $"P{i}");

        Assert.Equal(RecentProjectsService.MaxEntries, service.RecentProjects.Items.Count);
        Assert.EndsWith("P14.dbiproj", service.RecentProjects.Items[0]);
    }

    [Fact]
    public void Recent_LocBoProjectDaBiXoa()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();

        service.CreateNew(temp.At("a"), "A");
        service.CreateNew(temp.At("b"), "B");

        File.Delete(temp.At("a", "A.dbiproj"));

        var recent = service.RecentProjects.Items;

        Assert.Single(recent);
        Assert.EndsWith("B.dbiproj", recent[0]);
    }

    [Fact]
    public void Recent_FileHong_KhongChanMoStudio()
    {
        using var temp = new TempWorkspace();
        File.WriteAllText(Path.Combine(temp.SettingsDirectory, "recent.json"), "không phải JSON");

        var recent = new RecentProjectsService(temp.SettingsDirectory);

        Assert.Empty(recent.Items);
    }
}
