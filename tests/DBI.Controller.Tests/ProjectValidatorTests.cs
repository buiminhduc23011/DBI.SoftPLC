using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;

namespace DBI.Controller.Tests;

/// <summary>Sáu luật validation của phase-01, mỗi luật một test.</summary>
public class ProjectValidatorTests
{
    /// <summary>Project hợp lệ tối thiểu — đúng một Main, một device, một tag trỏ đúng device.</summary>
    private static DbiProject ValidProject()
    {
        var project = new DbiProject
        {
            Name = "MyMachine",
            Devices = { new DeviceConfig { Name = "FactoryIO_3D", DriverType = "DBI.Controller.Driver.FactoryIO" } },
            Blocks = { new CodeBlock { Name = "Main", Kind = BlockKind.Main, FileName = "Blocks/Main.cs" } },
            TagTables = { new TagTable() }
        };

        project.TagTables[0].Tags.Add(new Tag
        {
            Name = "StartButton",
            DataType = TagDataType.Bool,
            Direction = TagDirection.Input,
            Device = "FactoryIO_3D",
            Address = "Input_0"
        });

        return project;
    }

    private static Tag Tag(string name, TagDirection direction = TagDirection.Memory, string device = "") =>
        new() { Name = name, Direction = direction, Device = device };

    [Fact]
    public void ProjectHopLe_KhongCoLoiNao()
    {
        Assert.Empty(ProjectValidator.Validate(ValidProject()));
    }

    // ── Luật 1: tên tag phải là C# identifier hợp lệ ─────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Tag\"1")]
    [InlineData("Tag\n1")]
    public void TenTagKhongHopLe_BaoLoi(string tagName)
    {
        var project = ValidProject();
        project.TagTables[0].Tags.Add(Tag(tagName));

        var issues = ProjectValidator.Validate(project);

        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("không hợp lệ"));
    }

    [Theory]
    [InlineData("StopButton")]
    [InlineData("_internal")]
    [InlineData("Motor1")]
    [InlineData("Tag 1")]
    [InlineData("Start Button")]
    [InlineData("BăngTải")]      // tên có dấu vẫn là identifier C# hợp lệ
    public void TenTagHopLe_KhongBaoLoi(string tagName)
    {
        var project = ValidProject();
        project.TagTables[0].Tags.Add(Tag(tagName));

        Assert.Empty(ProjectValidator.Validate(project));
    }

    // ── Luật 2: tên tag không trùng, kể cả khác hoa/thường ───────────────────────

    [Fact]
    public void TenTagTrung_BaoLoi()
    {
        var project = ValidProject();
        project.TagTables[0].Tags.Add(Tag("Motor"));
        project.TagTables[0].Tags.Add(Tag("Motor"));

        Assert.Contains(ProjectValidator.Validate(project),
            i => i.Severity == IssueSeverity.Error && i.Message.Contains("Đã có tag tên"));
    }

    [Fact]
    public void TenTagTrungKhacHoaThuong_VanBaoLoi()
    {
        // MemorySnapshot dùng OrdinalIgnoreCase — "Motor" và "motor" là cùng một ô nhớ lúc chạy.
        var project = ValidProject();
        project.TagTables[0].Tags.Add(Tag("Motor"));
        project.TagTables[0].Tags.Add(Tag("motor"));

        Assert.Contains(ProjectValidator.Validate(project),
            i => i.Severity == IssueSeverity.Error && i.Message.Contains("Đã có tag tên"));
    }

    [Fact]
    public void TenTagTrungGiuaHaiBangTag_VanBaoLoi()
    {
        var project = ValidProject();
        project.TagTables[0].Tags.Add(Tag("Motor"));
        project.TagTables.Add(new TagTable { Name = "Bảng 2", Tags = { Tag("Motor") } });

        Assert.Contains(ProjectValidator.Validate(project),
            i => i.Severity == IssueSeverity.Error && i.Message.Contains("Đã có tag tên"));
    }

    // ── Luật 3: tên tag không phải từ khoá C# ────────────────────────────────────

    [Theory]
    [InlineData("class")]
    [InlineData("int")]
    [InlineData("return")]
    [InlineData("namespace")]
    public void TenTagLaTuKhoaCSharp_BaoLoi(string keyword)
    {
        var project = ValidProject();
        project.TagTables[0].Tags.Add(Tag(keyword));

        Assert.Contains(ProjectValidator.Validate(project),
            i => i.Severity == IssueSeverity.Error && i.Message.Contains("từ khoá C#"));
    }

    [Theory]
    [InlineData("value")]
    [InlineData("var")]
    [InlineData("async")]
    public void TenTagLaContextualKeyword_VanDungDuoc(string contextual)
    {
        // Contextual keyword dùng làm identifier bình thường trong C# — không được chặn oan.
        var project = ValidProject();
        project.TagTables[0].Tags.Add(Tag(contextual));

        Assert.Empty(ProjectValidator.Validate(project));
    }

    // ── Luật 4: tag Input/Output phải trỏ tới device đã khai ─────────────────────

    [Theory]
    [InlineData(TagDirection.Input)]
    [InlineData(TagDirection.Output)]
    public void TagTroToiDeviceChuaKhaiBao_BaoLoi(TagDirection direction)
    {
        var project = ValidProject();
        project.TagTables[0].Tags.Add(Tag("Motor", direction, device: "Khong_Ton_Tai"));

        Assert.Contains(ProjectValidator.Validate(project),
            i => i.Severity == IssueSeverity.Error && i.Message.Contains("Khong_Ton_Tai"));
    }

    [Theory]
    [InlineData(TagDirection.Input)]
    [InlineData(TagDirection.Output)]
    public void TagInputOutputKhongCoDevice_BaoLoi(TagDirection direction)
    {
        var project = ValidProject();
        project.TagTables[0].Tags.Add(Tag("Motor", direction, device: ""));

        Assert.Contains(ProjectValidator.Validate(project),
            i => i.Severity == IssueSeverity.Error && i.Target == "Motor");
    }

    [Fact]
    public void TagMemoryKhongCanDevice()
    {
        var project = ValidProject();
        project.TagTables[0].Tags.Add(Tag("InternalFlag", TagDirection.Memory));

        Assert.Empty(ProjectValidator.Validate(project));
    }

    [Fact]
    public void TenDeviceKhongPhanBietHoaThuong()
    {
        var project = ValidProject();
        project.TagTables[0].Tags.Add(Tag("Motor", TagDirection.Output, device: "factoryio_3d"));

        Assert.Empty(ProjectValidator.Validate(project));
    }

    // ── Luật 5: đúng một khối Main ───────────────────────────────────────────────

    [Fact]
    public void KhongCoKhoiMain_BaoLoi()
    {
        var project = ValidProject();
        project.Blocks.Clear();

        Assert.Contains(ProjectValidator.Validate(project),
            i => i.Severity == IssueSeverity.Error && i.Message.Contains("đúng một khối Main"));
    }

    [Fact]
    public void CoHaiKhoiMain_BaoLoi()
    {
        var project = ValidProject();
        project.Blocks.Add(new CodeBlock { Name = "Main2", Kind = BlockKind.Main, FileName = "Blocks/Main2.cs" });

        Assert.Contains(ProjectValidator.Validate(project),
            i => i.Severity == IssueSeverity.Error && i.Message.Contains("đúng một khối Main"));
    }

    // ── Luật 6: tên khối không trùng ─────────────────────────────────────────────

    [Fact]
    public void TenKhoiTrung_BaoLoi()
    {
        var project = ValidProject();
        project.Blocks.Add(new CodeBlock { Name = "Conveyor", FileName = "Blocks/Conveyor.cs" });
        project.Blocks.Add(new CodeBlock { Name = "Conveyor", FileName = "Blocks/Conveyor2.cs" });

        Assert.Contains(ProjectValidator.Validate(project),
            i => i.Severity == IssueSeverity.Error && i.Message.Contains("Đã có khối tên"));
    }

    [Fact]
    public void TenKhoiTrungKhacHoaThuong_VanBaoLoi()
    {
        var project = ValidProject();
        project.Blocks.Add(new CodeBlock { Name = "Conveyor", FileName = "Blocks/Conveyor.cs" });
        project.Blocks.Add(new CodeBlock { Name = "conveyor", FileName = "Blocks/conveyor.cs" });

        Assert.Contains(ProjectValidator.Validate(project),
            i => i.Severity == IssueSeverity.Error && i.Message.Contains("Đã có khối tên"));
    }
}
