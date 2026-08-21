using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.ViewModels;

namespace DBI.Controller.Tests;

public sealed class TaskCardsViewModelTests
{
    [Fact]
    public void InsertInstruction_RaisesSnippetRequest()
    {
        var cards = new TaskCardsViewModel();
        TaskCardItem? requested = null;
        cards.SnippetRequested += (_, item) => requested = item;

        cards.InsertInstructionCommand.Execute(cards.Instructions.First(i => i.Name == "Ton"));

        Assert.NotNull(requested);
        Assert.Contains("Ton", requested!.Snippet);
    }

    [Fact]
    public void CodeEditor_InsertsSnippetAtCaret()
    {
        var editor = new CodeEditorViewModel(
            new CodeBlock { Name = "Main", FileName = "Blocks/Main.cs" },
            Path.Combine(Path.GetTempPath(), "Main.cs"),
            "abc\ndef");
        editor.CaretLine = 2;
        editor.CaretColumn = 2;

        editor.InsertAtCaret("X");

        Assert.Equal("abc\ndXef", editor.Text);
    }

    [Fact]
    public void LoadProject_ListsTagsWithMapStatus()
    {
        var cards = new TaskCardsViewModel();
        var project = new DbiProject { Name = "Machine" };
        project.TagTables.Add(new TagTable());
        project.TagTables[0].Tags.Add(new Tag { Name = "StartButton", Device = "Sim", Address = "Sim_0" });
        project.TagTables[0].Tags.Add(new Tag { Name = "InternalFlag", Direction = TagDirection.Memory });

        cards.LoadProject(project);

        Assert.Equal(2, cards.DeviceTags.Count);
        Assert.True(cards.DeviceTags.Single(t => t.Name == "StartButton").IsMapped);
        Assert.False(cards.DeviceTags.Single(t => t.Name == "InternalFlag").IsMapped);
    }

    [Fact]
    public void InsertDeviceTag_RaisesTagDragRequest()
    {
        var cards = new TaskCardsViewModel();
        DeviceTagItem? requested = null;
        cards.TagDragRequested += (_, item) => requested = item;
        var tag = new DeviceTagItem("StartButton", "Bool", "Sim", "Sim_0", IsMapped: true);

        cards.InsertDeviceTagCommand.Execute(tag);

        Assert.Same(tag, requested);
    }
}
