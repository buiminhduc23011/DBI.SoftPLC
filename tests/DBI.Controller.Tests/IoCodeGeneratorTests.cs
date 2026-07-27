using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;

namespace DBI.Controller.Tests;

public class IoCodeGeneratorTests
{
    [Fact]
    public void Generate_SinhDungAccessorChoChinToHop()
    {
        var generator = new IoCodeGenerator();
        var project = NewProjectWithNineTags();

        string text = generator.Generate(project);

        Assert.Contains("public bool InBool => GetBool(\"InBool\");", text);
        Assert.Contains("public int InInt => GetInt(\"InInt\");", text);
        Assert.Contains("public float InReal => GetFloat(\"InReal\");", text);

        Assert.Contains("public bool OutBool { get => GetBool(\"OutBool\"); set => SetBool(\"OutBool\", value); }", text);
        Assert.Contains("public int OutInt { get => GetInt(\"OutInt\"); set => SetInt(\"OutInt\", value); }", text);
        Assert.Contains("public float OutReal { get => GetFloat(\"OutReal\"); set => SetFloat(\"OutReal\", value); }", text);

        Assert.Contains("public bool MemBool { get => GetBool(\"MemBool\"); set => SetBool(\"MemBool\", value); }", text);
        Assert.Contains("public int MemInt { get => GetInt(\"MemInt\"); set => SetInt(\"MemInt\", value); }", text);
        Assert.Contains("public float MemReal { get => GetFloat(\"MemReal\"); set => SetFloat(\"MemReal\", value); }", text);
    }

    [Fact]
    public void Generate_InputKhongCoSetter()
    {
        var generator = new IoCodeGenerator();
        var project = NewProjectWithNineTags();

        string text = generator.Generate(project);

        Assert.Contains("public bool InBool => GetBool(\"InBool\");", text);
        Assert.DoesNotContain("InBool { get => GetBool(\"InBool\"); set =>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_CommentThanhXmlDocVaEscapeDung()
    {
        var generator = new IoCodeGenerator();
        var project = NewProjectWithNineTags();
        project.TagTables[0].Tags[0].Comment = "Nút <Start> & an toàn";

        string text = generator.Generate(project);

        Assert.Contains("/// <summary>Nút &lt;Start&gt; &amp; an toàn</summary>", text);
    }

    [Fact]
    public void WriteIfChanged_LanHaiKhongGhiLai()
    {
        using var temp = new TempWorkspace();
        var service = temp.NewService();
        var project = service.CreateNew(temp.Root, "MyMachine").Project!;
        project.TagTables[0].Tags.Add(new Tag { Name = "MotorRun", Direction = TagDirection.Output });

        var generator = new IoCodeGenerator();
        var first = generator.WriteIfChanged(project);
        var second = generator.WriteIfChanged(project);

        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.True(File.Exists(first.FilePath));
    }

    [Fact]
    public void ExportRoutes_BoQuaTagMemory()
    {
        var generator = new IoCodeGenerator();
        var project = NewProjectWithNineTags();

        var routes = generator.ExportRoutes(project);

        Assert.DoesNotContain(routes, r => r.Direction == DBI.Controller.Core.Models.TagDirection.Memory);
        Assert.Equal(6, routes.Count);
    }

    [Fact]
    public void DoiAddress_KhongLamThayDoiIoG()
    {
        var generator = new IoCodeGenerator();
        var project = NewProjectWithNineTags();

        string before = generator.Generate(project);
        project.TagTables[0].Tags.First(t => t.Name == "OutBool").Address = "Output_99";
        string after = generator.Generate(project);

        Assert.Equal(before, after);
    }

    [Fact]
    public void DoiTenTag_LamThayDoiIoG()
    {
        var generator = new IoCodeGenerator();
        var project = NewProjectWithNineTags();

        string before = generator.Generate(project);
        project.TagTables[0].Tags.First(t => t.Name == "OutBool").Name = "ConveyorRun";
        string after = generator.Generate(project);

        Assert.NotEqual(before, after);
        Assert.Contains("ConveyorRun", after);
        Assert.DoesNotContain("OutBool", after);
    }

    private static DbiProject NewProjectWithNineTags()
    {
        var project = new DbiProject
        {
            Name = "MyMachine",
            ProjectFilePath = Path.Combine(Path.GetTempPath(), "MyMachine.dbiproj"),
            Devices =
            {
                new DeviceConfig { Name = "FactoryIO_3D", DriverType = "Simulation" }
            },
            TagTables =
            {
                new TagTable
                {
                    Tags =
                    {
                        new Tag { Name = "InBool", DataType = TagDataType.Bool, Direction = TagDirection.Input, Device = "FactoryIO_3D", Address = "Input_0" },
                        new Tag { Name = "InInt", DataType = TagDataType.Int, Direction = TagDirection.Input, Device = "FactoryIO_3D", Address = "Input_1" },
                        new Tag { Name = "InReal", DataType = TagDataType.Real, Direction = TagDirection.Input, Device = "FactoryIO_3D", Address = "Input_2" },
                        new Tag { Name = "OutBool", DataType = TagDataType.Bool, Direction = TagDirection.Output, Device = "FactoryIO_3D", Address = "Output_0" },
                        new Tag { Name = "OutInt", DataType = TagDataType.Int, Direction = TagDirection.Output, Device = "FactoryIO_3D", Address = "Output_1" },
                        new Tag { Name = "OutReal", DataType = TagDataType.Real, Direction = TagDirection.Output, Device = "FactoryIO_3D", Address = "Output_2" },
                        new Tag { Name = "MemBool", DataType = TagDataType.Bool, Direction = TagDirection.Memory },
                        new Tag { Name = "MemInt", DataType = TagDataType.Int, Direction = TagDirection.Memory },
                        new Tag { Name = "MemReal", DataType = TagDataType.Real, Direction = TagDirection.Memory }
                    }
                }
            }
        };

        return project;
    }
}
