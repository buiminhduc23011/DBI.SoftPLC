using CommunityToolkit.Mvvm.ComponentModel;
using DBI.Controller.Studio.Core.Models;

namespace DBI.Controller.Studio.Core.ViewModels;

/// <summary>
/// Một tab soạn mã — mỗi khối logic một thể hiện.
/// </summary>
/// <remarks>
/// <see cref="PaneViewModelBase.ContentId"/> lấy theo đường dẫn tương đối của tệp: ổn định giữa
/// các phiên nên AvalonDock khôi phục lại đúng tab đang mở, và không đụng nhau khi hai khối
/// trùng tên ở thư mục khác.
/// </remarks>
public partial class CodeEditorViewModel : DocumentViewModelBase
{
    private string _absolutePath;
    private string _savedText;

    public CodeEditorViewModel(CodeBlock block, string absolutePath, string text)
        : base(ContentIdFor(block), block.Name)
    {
        Block = block;
        _absolutePath = absolutePath;
        _savedText = text;
        _text = text;
    }

    public CodeBlock Block { get; }

    public string AbsolutePath => _absolutePath;

    [ObservableProperty]
    private string _text;

    /// <summary>Dòng/cột con trỏ, hiện ở thanh trạng thái.</summary>
    [ObservableProperty]
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;

    public static string ContentIdFor(CodeBlock block) =>
        string.IsNullOrWhiteSpace(block.FileName) ? "Block:" + block.Name : "Block:" + block.FileName;

    partial void OnTextChanged(string value) => IsDirty = !string.Equals(value, _savedText, StringComparison.Ordinal);

    public override async Task SaveAsync()
    {
        string? directory = Path.GetDirectoryName(_absolutePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(_absolutePath, Text).ConfigureAwait(false);

        _savedText = Text;
        IsDirty = false;
    }

    public void Retarget(CodeBlock block, string absolutePath)
    {
        Block.Name = block.Name;
        Block.Kind = block.Kind;
        Block.FileName = block.FileName;
        BaseTitle = block.Name;
        _absolutePath = absolutePath;
    }

    /// <summary>Nạp lại từ đĩa, bỏ mọi thay đổi chưa lưu.</summary>
    public async Task ReloadAsync()
    {
        if (!File.Exists(_absolutePath)) return;

        _savedText = await File.ReadAllTextAsync(_absolutePath).ConfigureAwait(false);
        Text = _savedText;
        IsDirty = false;
    }
}
