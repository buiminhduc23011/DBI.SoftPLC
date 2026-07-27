using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;

namespace DBI.Controller.Studio.Core.ViewModels;

/// <summary>
/// Vỏ ứng dụng: bố cục, menu, toolbar, vòng đời project.
/// </summary>
/// <remarks>
/// Thay cho <c>MainViewModel</c> cũ (188 dòng trộn theme token, dữ liệu thiết bị, tag mapping,
/// mã nguồn, compiler và monitoring vào một chỗ).
/// </remarks>
public partial class ShellViewModel : ObservableObject
{
    private readonly ProjectService _projects;
    private readonly IUserPrompt _prompt;
    private readonly IThemeSwitcher _theme;
    private readonly SynchronizationContext? _uiContext;

    private ILayoutPersistence? _layout;
    private FileSystemWatcher? _blocksWatcher;

    public ShellViewModel(
        ProjectService projects,
        IRuntimeClient runtime,
        IUserPrompt prompt,
        IThemeSwitcher theme,
        ILayoutPersistence? layout = null)
    {
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _layout = layout;
        _uiContext = SynchronizationContext.Current;

        Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        StatusBar = new StatusBarViewModel(runtime);
        Editors = new DocumentHostViewModel(prompt);
        Panes = new ObservableCollection<PaneViewModelBase> { ProjectTree, Inspector, TaskCards };

        ProjectTree.NodeActivated += (_, node) => OpenNode(node);
        ProjectTree.PropertyChanged += OnProjectTreeSelectionChanged;
        Editors.Notice += (_, n) => Inspector.LogInformation(n.Message, n.Severity);
        Runtime.FaultOccurred += (_, fault) =>
            Inspector.LogDiagnostic($"FAULT: {fault.Message}", IssueSeverity.Error);
    }

    public IRuntimeClient Runtime { get; }
    public ProjectTreeViewModel ProjectTree { get; } = new();
    public InspectorViewModel Inspector { get; } = new();
    public TaskCardsViewModel TaskCards { get; } = new();
    public StatusBarViewModel StatusBar { get; }
    public DocumentHostViewModel Editors { get; }

    public ObservableCollection<PaneViewModelBase> Panes { get; }

    public DbiProject? Project => _projects.Current;

    public bool IsDarkMode => _theme.IsDark;

    public string ThemeToggleText => _theme.IsDark ? "☀️ Light Mode" : "🌙 Dark Mode";

    /// <summary>
    /// Tra <c>ContentId</c> → ViewModel khi AvalonDock khôi phục layout.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>layout.xml</c> chỉ lưu cấu trúc và <c>ContentId</c>, <b>không</b> lưu content. Thiếu
    /// bảng tra này thì panel khôi phục ra rỗng trơn.
    /// </remarks>
    public object? ResolveContent(string? contentId)
    {
        if (string.IsNullOrEmpty(contentId)) return null;

        return Panes.FirstOrDefault(p => p.ContentId == contentId)
            ?? (object?)Editors.Find(contentId);
    }

    // ── Vòng đời project ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void NewProject()
    {
        var request = _prompt.AskNewProjectLocation();
        if (request is null) return;

        ApplyLoadResult(
            _projects.CreateNew(request.Directory, request.Name), $"Đã tạo project '{request.Name}'.");
    }

    [RelayCommand]
    private void OpenProject()
    {
        string? path = _prompt.AskProjectToOpen();
        if (path is null) return;

        ApplyLoadResult(
            _projects.Open(path), $"Đã mở project '{Path.GetFileNameWithoutExtension(path)}'.");
    }

    private void ApplyLoadResult(ProjectLoadResult result, string successMessage)
    {
        if (!result.Success)
        {
            _prompt.ShowError(
                string.Join("\n", result.Issues.Select(i => i.Message)), "Không mở được project");
            Inspector.LogInformation(result.Issues);
            return;
        }

        Editors.CloseAll();
        ProjectTree.Load(result.Project);
        ConfigureProjectTree();
        StatusBar.ProjectName = result.Project!.Name;

        Inspector.LogInformation(successMessage);
        Inspector.LogInformation(result.Issues);

        OnPropertyChanged(nameof(Project));
        RestoreLayout();
        StartWatchingProject();
    }

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        if (Project is null) return;

        await Editors.SaveDirtyAsync();
        _projects.Save();
        SaveLayout();

        Inspector.LogInformation("Đã lưu project và mọi khối đang mở.");
    }

    [RelayCommand]
    private void CloseProject()
    {
        if (Project is null) return;

        if (Editors.HasUnsavedChanges &&
            !_prompt.Confirm("Còn khối chưa lưu. Đóng project và bỏ thay đổi?", "Đóng project"))
        {
            return;
        }

        SaveLayout();
        Editors.CloseAll();
        StopWatchingProject();
        _projects.Close();
        ProjectTree.Load(null);
        StatusBar.ProjectName = "(chưa mở project)";

        OnPropertyChanged(nameof(Project));
    }

    // ── Tài liệu ─────────────────────────────────────────────────────────────────

    public void OpenNode(ProjectNode node)
    {
        if (node.Payload is CodeBlock block && Project is not null)
        {
            Editors.OpenBlock(block, Project.ProjectDirectory);
            return;
        }

        if (node.Kind == ProjectNodeKind.OnlineDiagnostics)
            Inspector.LogInformation("Diagnostics sẽ mở thành document riêng ở phase-10.");
        else if (node.Kind == ProjectNodeKind.DeviceConfiguration)
            Inspector.LogInformation("Device Configuration sẽ mở thành document riêng ở phase-09.");
    }

    [RelayCommand]
    private void CloseDocument(DocumentViewModelBase? document) => Editors.Close(document);

    // ── Giao diện ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Apply(!_theme.IsDark);

        OnPropertyChanged(nameof(IsDarkMode));
        OnPropertyChanged(nameof(ThemeToggleText));
    }

    [RelayCommand]
    private void ResetLayout()
    {
        _layout?.ResetToDefault();
        Inspector.LogInformation("Đã đưa bố cục cửa sổ về mặc định.");
    }

    /// <summary>
    /// Ráp dịch vụ bố cục sau khi cửa sổ đã dựng xong.
    /// </summary>
    /// <remarks>
    /// <c>DockingManager</c> chỉ tồn tại sau <c>InitializeComponent()</c> nên không tiêm qua
    /// constructor được. Vẫn đi qua interface — shell không biết AvalonDock là gì.
    /// </remarks>
    public void AttachLayout(ILayoutPersistence layout)
    {
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        RestoreLayout();
    }

    /// <summary>Layout riêng từng máy nên nằm trong <c>.dbistudio/</c> — thư mục không commit.</summary>
    public string? LayoutFilePath => Project is null
        ? null
        : Path.Combine(Project.ProjectDirectory, ProjectService.StudioFolder, "layout.xml");

    public void SaveLayout()
    {
        if (LayoutFilePath is null || _layout is null) return;

        Directory.CreateDirectory(Path.GetDirectoryName(LayoutFilePath)!);
        _layout.Save(LayoutFilePath);
    }

    public void RestoreLayout()
    {
        if (LayoutFilePath is not null) _layout?.Restore(LayoutFilePath);
    }

    /// <summary>
    /// Cảnh báo khi đóng Studio lúc máy đang chạy. <c>null</c> nghĩa là đóng thoải mái.
    /// </summary>
    public string? ClosingWarning =>
        StatusBar.RuntimeState == Protocol.RuntimeState.Running
            ? RuntimeProcessLauncher.ClosingWhileRunningWarning
            : null;

    [RelayCommand]
    private void OpenNodeFromMenu(ProjectNode? node)
    {
        if (node is not null) OpenNode(node);
    }

    [RelayCommand]
    private void AddBlock(ProjectNode? _)
    {
        if (Project is null) return;

        bool canCreateMain = Project.Blocks.All(b => b.Kind != BlockKind.Main);
        var request = _prompt.AskNewBlock(canCreateMain);
        if (request is null) return;

        var result = _projects.AddBlock(request);
        if (!result.Success)
        {
            ReportProjectIssues("Không tạo được khối", result.Issues);
            return;
        }

        ProjectTree.Load(Project);
        ConfigureProjectTree();
        SelectAndOpenBlock(request.Name);
        Inspector.LogInformation($"Đã tạo khối '{request.Name}'.");
    }

    [RelayCommand]
    private void RenameBlock(ProjectNode? node)
    {
        if (Project is null || node?.Payload is not CodeBlock block) return;

        string? newName = _prompt.AskText("Đổi tên khối", "Tên mới:", block.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == block.Name) return;

        var result = _projects.RenameBlock(block, newName);
        if (!result.Success)
        {
            ReportProjectIssues("Không đổi tên được khối", result.Issues);
            return;
        }

        if (Editors.Find(block) is { } opened)
        {
            string absolutePath = Path.Combine(Project.ProjectDirectory, block.FileName.Replace('/', Path.DirectorySeparatorChar));
            opened.Retarget(block, absolutePath);
        }

        ProjectTree.Load(Project);
        ConfigureProjectTree();
        SelectBlock(block);
        Inspector.LogInformation($"Đã đổi tên khối thành '{block.Name}'.");
    }

    [RelayCommand]
    private void DeleteBlock(ProjectNode? node)
    {
        if (Project is null || node?.Payload is not CodeBlock block) return;

        if (!_prompt.Confirm($"Xoá khối '{block.Name}' và tệp '{block.FileName}'?", "Xoá khối"))
            return;

        if (Editors.Find(block) is { } opened)
            Editors.Close(opened);

        _projects.DeleteBlock(block);
        ProjectTree.Load(Project);
        ConfigureProjectTree();
        Inspector.LogInformation($"Đã xoá khối '{block.Name}'.");
    }

    [RelayCommand]
    private void SetMainBlock(ProjectNode? node)
    {
        if (Project is null || node?.Payload is not CodeBlock block || block.Kind == BlockKind.Main) return;

        _projects.SetMainBlock(block);
        ProjectTree.Load(Project);
        ConfigureProjectTree();
        SelectBlock(block);
        Inspector.LogInformation($"Đã đặt '{block.Name}' làm khối Main.");
    }

    private void OnProjectTreeSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ProjectTreeViewModel.SelectedNode)) return;

        if (ProjectTree.SelectedNode is { } node)
            Inspector.ShowProperties(node.Title, ProjectTreeViewModel.DescribeNode(node));
    }

    private void ConfigureProjectTree() =>
        ProjectTree.ConfigureMenus(
            AddBlockCommand,
            OpenNodeFromMenuCommand,
            RenameBlockCommand,
            DeleteBlockCommand,
            SetMainBlockCommand);

    private void ReportProjectIssues(string title, IReadOnlyList<ValidationIssue> issues)
    {
        string message = string.Join("\n", issues.Select(i => i.Message));
        _prompt.ShowError(message, title);
        Inspector.LogInformation(issues);
    }

    private void SelectAndOpenBlock(string blockName)
    {
        if (ProjectTree.Roots.FirstOrDefault() is not { } root) return;

        var node = root.Children
            .FirstOrDefault(c => c.Title == "Program Blocks")?
            .Children.FirstOrDefault(c => c.Title.Equals(blockName, StringComparison.OrdinalIgnoreCase));

        if (node is null) return;

        ProjectTree.SelectedNode = node;
        OpenNode(node);
    }

    private void SelectBlock(CodeBlock block)
    {
        if (ProjectTree.Roots.FirstOrDefault() is not { } root) return;

        ProjectTree.SelectedNode = root.Children
            .FirstOrDefault(c => c.Title == "Program Blocks")?
            .Children.FirstOrDefault(c => ReferenceEquals(c.Payload, block));
    }

    private void StartWatchingProject()
    {
        StopWatchingProject();

        if (Project is null || _uiContext is null) return;

        string blocksPath = Path.Combine(Project.ProjectDirectory, ProjectService.BlocksFolder);
        Directory.CreateDirectory(blocksPath);

        _blocksWatcher = new FileSystemWatcher(blocksPath, "*.cs")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
        };

        _blocksWatcher.Created += (_, e) => PostToUi(() => OnExternalBlockCreated(e.FullPath));
        _blocksWatcher.Changed += (_, e) => PostToUi(() => Inspector.LogInformation(
            $"Tệp khối '{Path.GetFileName(e.FullPath)}' vừa bị sửa ngoài Studio.", IssueSeverity.Warning));
        _blocksWatcher.Deleted += (_, e) => PostToUi(() => Inspector.LogInformation(
            $"Tệp khối '{Path.GetFileName(e.FullPath)}' vừa bị xoá ngoài Studio.", IssueSeverity.Warning));
        _blocksWatcher.Renamed += (_, e) => PostToUi(() => Inspector.LogInformation(
            $"Tệp khối đổi tên ngoài Studio: '{e.OldName}' → '{e.Name}'.", IssueSeverity.Warning));
        _blocksWatcher.EnableRaisingEvents = true;
    }

    private void StopWatchingProject()
    {
        _blocksWatcher?.Dispose();
        _blocksWatcher = null;
    }

    private void OnExternalBlockCreated(string absolutePath)
    {
        if (Project is null || Project.Blocks.Any(b =>
                Path.GetFullPath(Path.Combine(Project.ProjectDirectory, b.FileName.Replace('/', Path.DirectorySeparatorChar)))
                    .Equals(Path.GetFullPath(absolutePath), StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (_prompt.Confirm(
                $"Phát hiện tệp khối mới '{Path.GetFileName(absolutePath)}' trong Blocks/. Thêm vào project?",
                "Thêm khối ngoài Studio"))
        {
            _projects.AttachExistingBlock(absolutePath);
            ProjectTree.Load(Project);
            ConfigureProjectTree();
            Inspector.LogInformation($"Đã thêm '{Path.GetFileNameWithoutExtension(absolutePath)}' vào project.");
            return;
        }

        Inspector.LogInformation(
            $"Phát hiện tệp khối mới '{Path.GetFileName(absolutePath)}' nhưng chưa thêm vào project.",
            IssueSeverity.Warning);
    }

    private void PostToUi(Action action)
    {
        if (_uiContext is null)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }
}
