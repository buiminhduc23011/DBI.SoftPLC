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
    public void InsertAtCaret_MultiLineSnippet_CaretCuoiDoanChen()
    {
        var editor = NewEditor("abc\ndef");
        editor.CaretLine = 2;
        editor.CaretColumn = 2;

        editor.InsertAtCaret("X\nY");

        Assert.Equal("abc\ndX\nYef", editor.Text);
        // Chèn tại offset 5 ('e'), snippet dài 3 → caret-end offset 8 = ngay sau 'Y' (dòng 3 cột 2).
        Assert.Equal(3, editor.CaretLine);
        Assert.Equal(2, editor.CaretColumn);
        // Pending caret cho tầng WPF consume: offset cuối đoạn chèn.
        Assert.True(editor.TryTakePendingCaretOffset(out int pending));
        Assert.Equal("abc\ndX\nY".Length, pending);
    }

    [Fact]
    public void InsertAtCaret_CRLF_Chuẩn()
    {
        var editor = NewEditor("abc\r\ndef");
        editor.CaretLine = 2;
        editor.CaretColumn = 2;

        editor.InsertAtCaret("X");

        // Chèn vào text GỐC (giữ CRLF), không normalize mất \r.
        Assert.Equal("abc\r\ndXef", editor.Text);
        Assert.True(editor.TryTakePendingCaretOffset(out int pending));
        Assert.Equal(7, pending); // offset của 'X' + 1 = sau ký tự chèn
    }

    [Fact]
    public void InsertAtCaret_CaretNgoaiPhamVi_KhongCrash()
    {
        var editor = NewEditor("abc\ndef");

        editor.CaretLine = 99;
        editor.CaretColumn = 99;

        editor.InsertAtCaret("Z");

        // Clamp về cuối file: dòng 2 cột 4 → chèn sau 'f'.
        Assert.Equal("abc\ndefZ", editor.Text);
        Assert.Equal(2, editor.CaretLine);
        Assert.Equal(5, editor.CaretColumn);
    }

    [Fact]
    public async Task Reload_SameContent_VanPhatRefreshSignal()
    {
        string path = Path.Combine(Path.GetTempPath(), "dbi-reload-" + Guid.NewGuid().ToString("N") + ".cs");
        await File.WriteAllTextAsync(path, "khong doi");
        try
        {
            var editor = new CodeEditorViewModel(
                new CodeBlock { Name = "Main", FileName = "Blocks/Main.cs" }, path, "khong doi");

            int tickets = editor.PendingRefreshTicket;
            editor.PendingRefreshTicket.ToString(); // chạm property

            await editor.ReloadAsync();

            // Nội dung GIỐNG NHAU → ObservableProperty(Text) không raise, nhưng ticket vẫn phải tăng
            // để tầng WPF biết cần refresh editor và consume pending caret.
            Assert.Equal("khong doi", editor.Text);
            Assert.True(editor.PendingRefreshTicket > tickets, $"Ticket không tăng: {editor.PendingRefreshTicket} <= {tickets}");
            Assert.True(editor.TryTakePendingCaretOffset(out int pending));
            Assert.Equal("khong doi".Length, pending); // caret clamp cuối văn bản
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static CodeEditorViewModel NewEditor(string text) =>
        new(new CodeBlock { Name = "Main", FileName = "Blocks/Main.cs" },
            Path.Combine(Path.GetTempPath(), "Main.cs"), text);

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

    [Fact]
    public void ShowInstructions_DoiDocKhongCode_NhomHienLai()
    {
        var cards = new TaskCardsViewModel();

        // Code → non-code: nhóm Instructions biến mất
        cards.ShowInstructions = false;
        Assert.DoesNotContain(cards.Groups, g => g.Title == "Instructions");

        // non-code → code: nhóm Instructions quay lại
        cards.ShowInstructions = true;
        Assert.Contains(cards.Groups, g => g.Title == "Instructions");
    }
}
