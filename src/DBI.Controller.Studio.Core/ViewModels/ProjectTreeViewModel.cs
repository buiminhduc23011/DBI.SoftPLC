using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Studio.Core.Models;

namespace DBI.Controller.Studio.Core.ViewModels;

public enum ProjectNodeKind
{
    Root,
    Folder,
    DeviceConfiguration,
    OnlineDiagnostics,
    GeneratedFile,
    Block,
    TagTable,
    Device,
    WatchTable,
    ForceTable
}

public sealed class MenuActionViewModel
{
    public MenuActionViewModel(string title, ICommand command, object? parameter = null, bool isEnabled = true)
    {
        Title = title;
        Command = command;
        Parameter = parameter;
        IsEnabled = isEnabled;
    }

    public string Title { get; }
    public ICommand Command { get; }
    public object? Parameter { get; }
    public bool IsEnabled { get; }
}

/// <summary>Một node trên cây project.</summary>
public partial class ProjectNode : ObservableObject
{
    public ProjectNode(ProjectNodeKind kind, string title, object? payload = null)
    {
        Kind = kind;
        _title = title;
        Payload = payload;
    }

    public ProjectNodeKind Kind { get; }

    /// <summary>Đối tượng model tương ứng: <c>CodeBlock</c>, <c>TagTable</c>, <c>DeviceConfig</c>...</summary>
    public object? Payload { get; }

    public ObservableCollection<ProjectNode> Children { get; } = new();
    public ObservableCollection<MenuActionViewModel> ContextMenuItems { get; } = new();

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Ký hiệu hiển thị trước tên node. Giữ đơn giản, không cần bộ icon riêng.</summary>
    public string Glyph => Kind switch
    {
        ProjectNodeKind.Root => "▣",
        ProjectNodeKind.Folder => "▸",
        ProjectNodeKind.DeviceConfiguration => "⚙",
        ProjectNodeKind.OnlineDiagnostics => "📊",
        ProjectNodeKind.GeneratedFile => "📄",
        ProjectNodeKind.Block => "◆",
        ProjectNodeKind.TagTable => "▦",
        ProjectNodeKind.Device => "⬢",
        ProjectNodeKind.WatchTable => "◉",
        ProjectNodeKind.ForceTable => "🔒",
        _ => "•"
    };
}

/// <summary>
/// Cây project bên trái — cấu trúc quen thuộc với kỹ sư PLC: Program Blocks / PLC Tags / Devices.
/// </summary>
/// <remarks>phase-05 bổ sung thêm menu ngữ cảnh, đổi tên, xoá, tạo khối từ mẫu.</remarks>
public partial class ProjectTreeViewModel : PaneViewModelBase
{
    public ProjectTreeViewModel() : base("ProjectTree", "Project") { }

    private readonly Dictionary<string, bool> _expandedState = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<ProjectNode> Roots { get; } = new();

    [ObservableProperty]
    private ProjectNode? _selectedNode;

    /// <summary>Phát khi người dùng mở một node (double-click). Shell quyết định mở tài liệu nào.</summary>
    public event EventHandler<ProjectNode>? NodeActivated;

    public void RaiseNodeActivated(ProjectNode node) => NodeActivated?.Invoke(this, node);

    /// <summary>Dựng lại cây từ project. Gọi khi mở project hoặc sau khi thêm/xoá khối.</summary>
    public void Load(DbiProject? project)
    {
        CaptureExpansionState();
        Roots.Clear();

        if (project is null)
        {
            Title = "Project";
            return;
        }

        Title = project.Name;

        var root = new ProjectNode(ProjectNodeKind.Root, project.Name, project);
        ApplyExpansionState(root, "Root");

        root.Children.Add(CreateFixedNode(ProjectNodeKind.DeviceConfiguration, "Device Configuration", key: "DeviceConfiguration"));
        root.Children.Add(CreateFixedNode(ProjectNodeKind.OnlineDiagnostics, "Online & Diagnostics", key: "OnlineDiagnostics"));

        var blocks = CreateFixedNode(ProjectNodeKind.Folder, "Program Blocks", key: "ProgramBlocks");
        foreach (var block in project.Blocks)
            blocks.Children.Add(CreateNode(ProjectNodeKind.Block, block.Name, block, $"Block:{block.FileName}"));

        var tags = CreateFixedNode(ProjectNodeKind.Folder, "PLC Tags", key: "Tags");
        foreach (var table in project.TagTables)
            tags.Children.Add(CreateNode(ProjectNodeKind.TagTable, table.Name, table, $"TagTable:{table.Name}"));

        var generated = CreateFixedNode(ProjectNodeKind.Folder, "Generated", key: "Generated");
        generated.Children.Add(CreateNode(ProjectNodeKind.GeneratedFile, "IO.g.cs", payload: null, "Generated:IO.g.cs"));

        var watches = CreateFixedNode(ProjectNodeKind.Folder, "Watch & Force Tables", key: "Watches");
        watches.Children.Add(CreateFixedNode(ProjectNodeKind.ForceTable, "Force Table", key: "ForceTable"));
        foreach (var watch in project.WatchTables)
            watches.Children.Add(CreateNode(ProjectNodeKind.WatchTable, watch.Name, watch, $"Watch:{watch.Name}"));

        var devices = CreateFixedNode(ProjectNodeKind.Folder, "Devices", key: "Devices");
        foreach (var device in project.Devices)
            devices.Children.Add(CreateNode(ProjectNodeKind.Device, device.Name, device, $"Device:{device.Name}"));

        root.Children.Add(blocks);
        root.Children.Add(tags);
        root.Children.Add(generated);
        root.Children.Add(watches);
        root.Children.Add(devices);

        Roots.Add(root);
    }

    public void ConfigureMenus(
        ICommand addBlockCommand,
        ICommand addTagTableCommand,
        ICommand openNodeCommand,
        ICommand renameBlockCommand,
        ICommand deleteBlockCommand,
        ICommand setMainBlockCommand,
        ICommand renameTagTableCommand,
        ICommand deleteTagTableCommand)
    {
        foreach (ProjectNode root in Roots)
        {
            ConfigureMenusRecursive(
                root,
                addBlockCommand,
                addTagTableCommand,
                openNodeCommand,
                renameBlockCommand,
                deleteBlockCommand,
                setMainBlockCommand,
                renameTagTableCommand,
                deleteTagTableCommand);
        }
    }

    /// <summary>Thuộc tính của node đang chọn, để Inspector hiển thị ở tab Properties.</summary>
    public static IEnumerable<PropertyRow> DescribeNode(ProjectNode node) => node.Payload switch
    {
        CodeBlock block => new[]
        {
            new PropertyRow("Name", block.Name),
            new PropertyRow("Block type", block.Kind.ToString()),
            new PropertyRow("File", block.FileName),
            new PropertyRow("Comment", block.Comment, isEditable: true)
        },
        TagTable table => new[]
        {
            new PropertyRow("Table name", table.Name),
            new PropertyRow("Tag count", table.Tags.Count.ToString())
        },
        DeviceConfig device => new[]
        {
            new PropertyRow("Device name", device.Name),
            new PropertyRow("Driver type", device.DriverType),
            new PropertyRow("Settings", string.Join(", ", device.Settings.Select(s => $"{s.Key}={s.Value}")))
        },
        WatchTable watch => new[]
        {
            new PropertyRow("Table name", watch.Name),
            new PropertyRow("Watched tags", watch.TagNames.Count.ToString())
        },
        DbiProject project => new[]
        {
            new PropertyRow("Project name", project.Name),
            new PropertyRow("Description", project.Description, isEditable: true),
            new PropertyRow("Schema", project.SchemaVersion),
            new PropertyRow("Scan interval", $"{project.Runtime.ScanIntervalMs} ms"),
            new PropertyRow("Auto-start after restart", project.Runtime.AutoStart ? "Yes" : "No")
        },
        _ => new[] { new PropertyRow("Tên", node.Title) }
    };

    private void ConfigureMenusRecursive(
        ProjectNode node,
        ICommand addBlockCommand,
        ICommand addTagTableCommand,
        ICommand openNodeCommand,
        ICommand renameBlockCommand,
        ICommand deleteBlockCommand,
        ICommand setMainBlockCommand,
        ICommand renameTagTableCommand,
        ICommand deleteTagTableCommand)
    {
        node.ContextMenuItems.Clear();

        if (node.Kind == ProjectNodeKind.Folder && node.Title == "Program Blocks")
        {
            node.ContextMenuItems.Add(new MenuActionViewModel("Add new block…", addBlockCommand, node));
        }
        else if (node.Kind == ProjectNodeKind.Folder && node.Title == "PLC Tags")
        {
            node.ContextMenuItems.Add(new MenuActionViewModel("Add new tag table…", addTagTableCommand, node));
        }
        else if (node.Kind == ProjectNodeKind.Block)
        {
            bool isMain = node.Payload is CodeBlock { Kind: BlockKind.Main };

            node.ContextMenuItems.Add(new MenuActionViewModel("Open", openNodeCommand, node));
            node.ContextMenuItems.Add(new MenuActionViewModel("Rename", renameBlockCommand, node));
            node.ContextMenuItems.Add(new MenuActionViewModel("Delete", deleteBlockCommand, node));
            node.ContextMenuItems.Add(new MenuActionViewModel("Set as Main", setMainBlockCommand, node, !isMain));
        }
        else if (node.Kind == ProjectNodeKind.TagTable)
        {
            bool isDefault = string.Equals(node.Title, "Default Tag Table", StringComparison.OrdinalIgnoreCase);
            node.ContextMenuItems.Add(new MenuActionViewModel("Open", openNodeCommand, node));
            node.ContextMenuItems.Add(new MenuActionViewModel("Rename", renameTagTableCommand, node));
            node.ContextMenuItems.Add(new MenuActionViewModel("Delete", deleteTagTableCommand, node, !isDefault));
        }
        else if (node.Kind == ProjectNodeKind.GeneratedFile)
        {
            node.ContextMenuItems.Add(new MenuActionViewModel("Open", openNodeCommand, node));
        }

        foreach (ProjectNode child in node.Children)
        {
            ConfigureMenusRecursive(
                child,
                addBlockCommand,
                addTagTableCommand,
                openNodeCommand,
                renameBlockCommand,
                deleteBlockCommand,
                setMainBlockCommand,
                renameTagTableCommand,
                deleteTagTableCommand);
        }
    }

    private ProjectNode CreateFixedNode(ProjectNodeKind kind, string title, string key)
        => CreateNode(kind, title, payload: null, key: key);

    private ProjectNode CreateNode(ProjectNodeKind kind, string title, object? payload, string key)
    {
        var node = new ProjectNode(kind, title, payload);
        ApplyExpansionState(node, key);
        return node;
    }

    private void CaptureExpansionState()
    {
        foreach (ProjectNode root in Roots)
            CaptureRecursive(root);
    }

    private void CaptureRecursive(ProjectNode node)
    {
        _expandedState[BuildKey(node)] = node.IsExpanded;

        foreach (ProjectNode child in node.Children)
            CaptureRecursive(child);
    }

    private void ApplyExpansionState(ProjectNode node, string key)
    {
        if (_expandedState.TryGetValue(key, out bool expanded))
            node.IsExpanded = expanded;
    }

    private static string BuildKey(ProjectNode node) => node.Kind switch
    {
        ProjectNodeKind.Block when node.Payload is CodeBlock block => $"Block:{block.FileName}",
        ProjectNodeKind.TagTable => $"Tag:{node.Title}",
        ProjectNodeKind.Device => $"Device:{node.Title}",
        ProjectNodeKind.WatchTable => $"Watch:{node.Title}",
        _ => node.Title
    };
}

/// <summary>
/// Bảng thẻ tác vụ bên phải. phase-12 làm nội dung thật (kéo-thả tag vào khối).
/// </summary>
public partial class TaskCardsViewModel : PaneViewModelBase
{
    public TaskCardsViewModel() : base("TaskCards", "Toolbox")
    {
        Instructions = Assembly.Load("DBI.Controller.SDK").GetTypes()
            .Where(t => t.IsPublic && t.IsClass && t.Namespace == "DBI.Controller.SDK.Primitives")
            .OrderBy(t => t.Name)
            .Select(t => new TaskCardItem(t.Name, SnippetFor(t.Name)))
            .ToList();
    }

    public IReadOnlyList<TaskCardItem> Instructions { get; }
    public event EventHandler<TaskCardItem>? SnippetRequested;

    [RelayCommand]
    private void InsertInstruction(TaskCardItem? item)
    {
        if (item is not null) SnippetRequested?.Invoke(this, item);
    }

    private static string SnippetFor(string name) => name switch
    {
        "Ton" => "private readonly Ton _timer = new(1000);\n_timer.In = /* condition */;",
        "Tof" => "private readonly Tof _timer = new(1000);\n_timer.In = /* condition */;",
        "Tp" => "private readonly Tp _pulse = new(1000);\n_pulse.In = /* condition */;",
        "Counter" => "var counter = new Counter();",
        "RisingEdge" => "var edge = new RisingEdge();",
        _ => $"var {name.ToLowerInvariant()} = new {name}();"
    };

    [ObservableProperty]
    private string _placeholder = "Thẻ tác vụ và kéo-thả tag sẽ có ở phase-12.";
}

public sealed record TaskCardItem(string Name, string Snippet);
