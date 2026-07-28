using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services.CodeAnalysis;
using DBI.Controller.Studio.Core.Services;

namespace DBI.Controller.Tests;

public sealed class StudioRoslynWorkspaceTests
{
    [Fact]
    public void OpenProject_BuildsOneWorkspaceContainingAllBlocksAndGeneratedIo()
    {
        using var project = TestProject.Create();
        var workspace = CreateWorkspace();

        workspace.OpenProject(project.Model);

        var documents = workspace.CurrentWorkspace!.CurrentSolution.Projects.Single().Documents;
        Assert.Contains(documents, d => Path.GetFileName(d.FilePath) == "Main.cs" && d.FilePath!.Contains("Blocks", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(documents, d => Path.GetFileName(d.FilePath) == "Conveyor.cs" && d.FilePath!.Contains("Blocks", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(documents, d => Path.GetFileName(d.FilePath) == "IO.g.cs" && d.FilePath!.Contains("Generated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UpdateDocument_ReportsUnknownIoMemberInTheChangedFile()
    {
        using var project = TestProject.Create();
        var workspace = CreateWorkspace();

        workspace.OpenProject(project.Model);
        workspace.UpdateDocument(project.MainPath, "using DBI.Controller.SDK; namespace UserProgram; public class Main : ControllerProgram { public override void Execute() => _ = IO.DoesNotExist; }");

        var diagnostics = await workspace.GetDiagnosticsAsync(project.MainPath);

        Assert.Contains(diagnostics, d => d.Id == "CS1061" && d.Message.Contains("DoesNotExist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegenerateIoDocument_MakesNewTagAvailableWithoutRestart()
    {
        using var project = TestProject.Create();
        var workspace = CreateWorkspace();

        workspace.OpenProject(project.Model);
        workspace.UpdateDocument(project.MainPath, "using DBI.Controller.SDK; namespace UserProgram; public class Main : ControllerProgram { public override void Execute() => _ = IO.StopButton; }");
        project.Model.TagTables[0].Tags.Add(new Tag { Name = "StopButton", Device = "Simulation", Address = "1" });
        workspace.RegenerateIoDocument(new IoCodeGenerator().Generate(project.Model));

        var diagnostics = await workspace.GetDiagnosticsAsync(project.MainPath);

        Assert.DoesNotContain(diagnostics, d => d.Id == "CS1061" && d.Message.Contains("StartButton", StringComparison.Ordinal));
    }

    private static StudioRoslynWorkspace CreateWorkspace()
    {
        return new StudioRoslynWorkspace();
    }

    private sealed class TestProject : IDisposable
    {
        private TestProject(string directory, DbiProject model)
        {
            DirectoryPath = directory;
            Model = model;
            MainPath = Path.Combine(directory, "Blocks", "Main.cs");
        }

        public string DirectoryPath { get; }
        public string MainPath { get; }
        public DbiProject Model { get; }

        public static TestProject Create()
        {
            string directory = Path.Combine(Path.GetTempPath(), "dbi-studio-roslyn-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "Blocks"));
            Directory.CreateDirectory(Path.Combine(directory, "Generated"));

            var model = new DbiProject
            {
                Name = "TestProject",
                ProjectFilePath = Path.Combine(directory, "TestProject.dbiproj"),
                Blocks =
                {
                    new CodeBlock { Name = "Main", Kind = BlockKind.Main, FileName = "Blocks/Main.cs" },
                    new CodeBlock { Name = "Conveyor", Kind = BlockKind.FunctionBlock, FileName = "Blocks/Conveyor.cs" }
                },
                TagTables =
                {
                    new TagTable { Tags = { new Tag { Name = "StartButton", Device = "Simulation", Address = "0" } } }
                }
            };

            File.WriteAllText(Path.Combine(directory, "Blocks", "Main.cs"), "using DBI.Controller.SDK; namespace UserProgram; public class Main : ControllerProgram { public override void Execute() => _ = IO.StartButton; }");
            File.WriteAllText(Path.Combine(directory, "Blocks", "Conveyor.cs"), "namespace UserProgram; public class Conveyor { }");
            File.WriteAllText(Path.Combine(directory, "Generated", "IO.g.cs"), new IoCodeGenerator().Generate(model));
            return new TestProject(directory, model);
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
