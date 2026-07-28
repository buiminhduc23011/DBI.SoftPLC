using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;

namespace DBI.Controller.Studio.Core.ViewModels;

/// <summary>
/// Vùng tài liệu ở giữa: mở, chuyển, đóng tab.
/// </summary>
/// <remarks>
/// Tách khỏi <see cref="ShellViewModel"/> vì đây là một trách nhiệm riêng — shell lo vòng đời
/// project và bố cục, chỗ này lo tab nào đang mở và có gì chưa lưu.
/// </remarks>
public partial class DocumentHostViewModel : ObservableObject
{
    private readonly IUserPrompt _prompt;

    public DocumentHostViewModel(IUserPrompt prompt) =>
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));

    public ObservableCollection<DocumentViewModelBase> Documents { get; } = new();

    [ObservableProperty]
    private DocumentViewModelBase? _activeDocument;

    public bool HasUnsavedChanges => Documents.Any(d => d.IsDirty);

    /// <summary>Phát khi cần ghi chú vào Inspector — shell nối vào.</summary>
    public event EventHandler<(string Message, IssueSeverity Severity)>? Notice;

    /// <summary>Mở khối trong tab mới, hoặc chuyển sang tab đã mở sẵn.</summary>
    public CodeEditorViewModel OpenBlock(CodeBlock block, string projectDirectory)
    {
        string contentId = CodeEditorViewModel.ContentIdFor(block);

        if (Documents.FirstOrDefault(d => d.ContentId == contentId) is CodeEditorViewModel opened)
        {
            ActiveDocument = opened;
            return opened;
        }

        string absolutePath = Path.Combine(
            projectDirectory, block.FileName.Replace('/', Path.DirectorySeparatorChar));

        bool exists = File.Exists(absolutePath);

        if (!exists)
        {
            Notice?.Invoke(this, (
                $"Khối '{block.Name}' khai báo tệp '{block.FileName}' nhưng tệp không tồn tại. " +
                "Tab mở ra rỗng — lưu lại sẽ tạo tệp mới.",
                IssueSeverity.Warning));
        }

        var editor = new CodeEditorViewModel(block, absolutePath, exists ? File.ReadAllText(absolutePath) : "");

        Documents.Add(editor);
        ActiveDocument = editor;

        return editor;
    }

    public TagTableViewModel OpenTagTable(DbiProject project, TagTable table, IoCodeGenerator generator)
    {
        string contentId = TagTableViewModel.ContentIdFor(table);

        if (Documents.FirstOrDefault(d => d.ContentId == contentId) is TagTableViewModel opened)
        {
            ActiveDocument = opened;
            return opened;
        }

        var editor = new TagTableViewModel(project, table, generator);
        Documents.Add(editor);
        ActiveDocument = editor;
        return editor;
    }

    public GeneratedCodeViewModel OpenGeneratedCode(string absolutePath)
    {
        if (Documents.FirstOrDefault(d => d.ContentId == GeneratedCodeViewModel.GeneratedContentId)
            is GeneratedCodeViewModel opened)
        {
            ActiveDocument = opened;
            return opened;
        }

        string text = File.Exists(absolutePath) ? File.ReadAllText(absolutePath) : string.Empty;
        var editor = new GeneratedCodeViewModel(absolutePath, text);
        Documents.Add(editor);
        ActiveDocument = editor;
        return editor;
    }

    public DeviceConfigurationViewModel OpenDeviceConfiguration(DbiProject project, ProjectService projects)
    {
        if (Documents.FirstOrDefault(d => d.ContentId == "DeviceConfiguration") is DeviceConfigurationViewModel opened)
        {
            ActiveDocument = opened;
            opened.Refresh();
            return opened;
        }

        var document = new DeviceConfigurationViewModel(project, projects);
        Documents.Add(document);
        ActiveDocument = document;
        return document;
    }

    public WatchTableViewModel OpenWatchTable(DbiProject project, WatchTable table, IRuntimeClient runtime, ProjectService projects)
    {
        if (Documents.FirstOrDefault(d => d.ContentId == $"Watch:{table.Name}") is WatchTableViewModel opened)
        { ActiveDocument = opened; return opened; }
        var document = new WatchTableViewModel(project, table, runtime, projects);
        Documents.Add(document); ActiveDocument = document; return document;
    }

    /// <summary>Đóng tab. Hỏi lại nếu còn thay đổi chưa lưu.</summary>
    /// <returns><c>false</c> nếu người dùng huỷ.</returns>
    public bool Close(DocumentViewModelBase? document)
    {
        if (document is null || !document.CanClose) return false;

        if (document.IsDirty &&
            !_prompt.Confirm($"'{document.Title}' có thay đổi chưa lưu. Đóng và bỏ thay đổi?", "Đóng tab"))
        {
            return false;
        }

        Documents.Remove(document);

        if (ReferenceEquals(ActiveDocument, document))
            ActiveDocument = Documents.LastOrDefault();

        return true;
    }

    public void CloseAll()
    {
        Documents.Clear();
        ActiveDocument = null;
    }

    public async Task SaveDirtyAsync()
    {
        foreach (var document in Documents.Where(d => d.IsDirty).ToList())
            await document.SaveAsync();
    }

    public DocumentViewModelBase? Find(string contentId) =>
        Documents.FirstOrDefault(d => d.ContentId == contentId);

    public CodeEditorViewModel? Find(CodeBlock block) =>
        Documents.OfType<CodeEditorViewModel>()
            .FirstOrDefault(d => ReferenceEquals(d.Block, block) ||
                                 string.Equals(d.Block.FileName, block.FileName, StringComparison.OrdinalIgnoreCase));
}
