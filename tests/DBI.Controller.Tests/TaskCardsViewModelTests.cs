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
}
