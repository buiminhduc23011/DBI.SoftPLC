using CommunityToolkit.Mvvm.ComponentModel;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;

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

    /// <summary>
    /// Live overlay (phase-13) — gắn sau khi dựng, vì cần <see cref="LiveCodeOverlayService"/>
    /// và runtime client từ shell.
    /// </summary>
    public CodeOverlayViewModel? Overlay { get; set; }

    [ObservableProperty]
    private string _text;

    /// <summary>Dòng/cột con trỏ, hiện ở thanh trạng thái.</summary>
    [ObservableProperty]
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;

    public static string ContentIdFor(CodeBlock block) =>
        string.IsNullOrWhiteSpace(block.FileName) ? "Block:" + block.Name : "Block:" + block.FileName;

    partial void OnTextChanged(string value)
    {
        IsDirty = !string.Equals(value, _savedText, StringComparison.Ordinal);
        Overlay?.UpdateSource(value); // overlay phân tích lại khi file đổi (Task 13.2)
    }

    /// <summary>
    /// Chèn <paramref name="snippet"/> tại vị trí caret và đặt pending-caret cho tầng WPF
    /// (editor consume qua <see cref="TryTakePendingCaretOffset"/> để đưa caret tới cuối đoạn chèn).
    /// </summary>
    public void InsertAtCaret(string snippet)
    {
        if (string.IsNullOrEmpty(snippet)) return;

        int start = TextPositionMath.PositionToOffset(Text, CaretLine, CaretColumn);
        int end = start + snippet.Length;

        SetPendingCaretOffset(end);
        Text = Text.Insert(start, snippet);

        // Cập nhật luôn trên VM để ai đọc CaretLine/Column ngay sau chèn không phải chờ editor.
        var (line, column) = TextPositionMath.OffsetToPosition(Text, end);
        CaretLine = line;
        CaretColumn = column;
    }

    /// <summary>Con trỏ đích chờ tầng WPF consume — consume-once, ghi đè khi chèn liên tiếp.</summary>
    private int? _pendingCaretOffset;

    /// <summary>
    /// Lấy (và xoá) offset con trỏ đích của lần chèn/reload gần nhất.
    /// Trả về false nếu không có pending hoặc đã bị consume — semantics consume-once.
    /// </summary>
    public bool TryTakePendingCaretOffset(out int offset)
    {
        if (_pendingCaretOffset is { } value)
        {
            offset = value;
            _pendingCaretOffset = null;
            return true;
        }

        offset = 0;
        return false;
    }

    private void SetPendingCaretOffset(int offset) => _pendingCaretOffset = offset;

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

    /// <summary>
    /// Tăng 1 mỗi lần reload — kể cả khi nội dung trùng (ObservableProperty không raise
    /// PropertyChanged khi giá trị bằng nhau, nên tầng WPF cần tín hiệu riêng để refresh
    /// editor và consume pending caret).
    /// </summary>
    [ObservableProperty]
    private int _pendingRefreshTicket;

    /// <summary>Nạp lại từ đĩa, bỏ mọi thay đổi chưa lưu.</summary>
    public async Task ReloadAsync()
    {
        if (!File.Exists(_absolutePath)) return;

        _savedText = await File.ReadAllTextAsync(_absolutePath).ConfigureAwait(false);

        SetPendingCaretOffset(_savedText.Length); // caret clamp cuối văn bản cho tầng WPF
        Text = _savedText;
        IsDirty = false;
        PendingRefreshTicket++;
    }
}
