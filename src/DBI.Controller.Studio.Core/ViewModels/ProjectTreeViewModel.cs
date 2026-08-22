using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Core.Models;
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

    /// <summary>Ký hiệu trạng thái online của device node (phase-09): ● nối, ◐ đang nối, ○ ngắt, ✕ lỗi.</summary>
    [ObservableProperty]
    private string _statusGlyph = "";

    /// <summary>Khóa màu trong theme cho <see cref="StatusGlyph"/> — VM không giữ mã màu.</summary>
    [ObservableProperty]
    private string _statusColorKey = "MutedColor";

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

    /// <summary>
    /// Cập nhật ký hiệu trạng thái trên các node device (phase-09 Task 09.4).
    /// Gọi từ vòng poll <c>DeviceStatesChanged</c> của Shell.
    /// </summary>
    public void UpdateDeviceStatuses(IReadOnlyList<Protocol.DeviceStateInfo> states)
    {
        foreach (ProjectNode root in Roots) UpdateDeviceStatusesRecursive(root, states);
    }

    private static void UpdateDeviceStatusesRecursive(ProjectNode node, IReadOnlyList<Protocol.DeviceStateInfo> states)
    {
        if (node.Kind == ProjectNodeKind.Device)
        {
            var state = states.FirstOrDefault(s => s.DriverId.Equals(node.Title, StringComparison.OrdinalIgnoreCase));
            node.StatusGlyph = state?.State switch
            {
                nameof(ConnectionState.Connected) => " ●",
                nameof(ConnectionState.Connecting) => " ◐",
                nameof(ConnectionState.Disconnected) => " ○",
                nameof(ConnectionState.Faulted) => " ✕",
                _ => ""
            };
            node.StatusColorKey = state?.State switch
            {
                nameof(ConnectionState.Connected) => "SuccessColor",
                nameof(ConnectionState.Connecting) => "WarningColor",
                nameof(ConnectionState.Disconnected) => "MutedColor",
                _ => "DangerColor"
            };
        }

        foreach (ProjectNode child in node.Children)
            UpdateDeviceStatusesRecursive(child, states);
    }

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
        ICommand deleteTagTableCommand,
        ICommand addWatchTableCommand,
        ICommand renameWatchTableCommand,
        ICommand deleteWatchTableCommand)
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
                deleteTagTableCommand,
                addWatchTableCommand,
                renameWatchTableCommand,
                deleteWatchTableCommand);
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
        ICommand deleteTagTableCommand,
        ICommand addWatchTableCommand,
        ICommand renameWatchTableCommand,
        ICommand deleteWatchTableCommand)
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
        else if (node.Kind == ProjectNodeKind.Folder && node.Title == "Watch & Force Tables")
        {
            node.ContextMenuItems.Add(new MenuActionViewModel("Add new watch table…", addWatchTableCommand, node));
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
        else if (node.Kind == ProjectNodeKind.WatchTable)
        {
            node.ContextMenuItems.Add(new MenuActionViewModel("Open", openNodeCommand, node));
            node.ContextMenuItems.Add(new MenuActionViewModel("Rename", renameWatchTableCommand, node));
            node.ContextMenuItems.Add(new MenuActionViewModel("Delete", deleteWatchTableCommand, node));
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
                deleteTagTableCommand,
                addWatchTableCommand,
                renameWatchTableCommand,
                deleteWatchTableCommand);
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
/// Bảng thẻ tác vụ bên phải, kiểu Solution Explorer của Visual Studio:
/// ô lọc trên đầu, mục xếp theo nhóm có tiêu đề bấm để đóng/mở.
/// Đổi nội dung theo document đang active (Task 12.4):
/// code editor → Instructions · Device Tags; bảng khác → chỉ Device Tags.
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
        RefreshFiltered();
    }

    public IReadOnlyList<TaskCardItem> Instructions { get; }

    /// <summary>Tag của project theo device — nguồn cho card Device Tags (Task 12.2).</summary>
    public IReadOnlyList<DeviceTagItem> DeviceTags { get; private set; } = Array.Empty<DeviceTagItem>();

    /// <summary>Nhóm hiển thị sau khi lọc — mỗi nhóm một device, sập mở được như vùng Toolbox VS.</summary>
    public IReadOnlyList<ToolboxGroup> Groups { get; private set; } = Array.Empty<ToolboxGroup>();

    /// <summary>Từ khoá lọc — khớp không phân biệt hoa/thường trên tên tag và device.</summary>
    [ObservableProperty]
    private string _filterText = "";

    /// <summary>Số tag đang hiện / tổng số — dòng trạng thái nhỏ dưới ô lọc kiểu VS.</summary>
    public string FilterSummary => Groups.Count == 0
        ? "Không có mục nào khớp"
        : $"{Groups.Sum(g => g.Items.Count)} of {DeviceTags.Count + Instructions.Count} items";

    /// <summary>Document đang active là code editor không — quyết định card nào hiện.</summary>
    [ObservableProperty]
    private bool _showInstructions = true;

    partial void OnShowInstructionsChanged(bool value) => RefreshFiltered();

    partial void OnFilterTextChanged(string value) => RefreshFiltered();

    /// <summary>Kéo/thả/double-click một tag từ Toolbox.</summary>
    public event EventHandler<TaskCardItem>? SnippetRequested;
    public event EventHandler<DeviceTagItem>? TagDragRequested;

    [RelayCommand]
    private void InsertInstruction(TaskCardItem? item)
    {
        if (item is not null) SnippetRequested?.Invoke(this, item);
    }

    /// <summary>Double-click device tag cũng chèn như kéo-thả (Task 12.1).</summary>
    [RelayCommand]
    private void InsertDeviceTag(DeviceTagItem? item)
    {
        if (item is not null) TagDragRequested?.Invoke(this, item);
    }

    /// <summary>Nạp tag theo device từ project — gọi khi mở project hoặc tag table đổi.</summary>
    public void LoadProject(DbiProject? project)
    {
        DeviceTags = project is null
            ? Array.Empty<DeviceTagItem>()
            : project.AllTags()
                .Select(t => new DeviceTagItem(
                    t.Name,
                    t.DataType.ToString(),
                    t.Device,
                    t.Address,
                    IsMapped: !string.IsNullOrWhiteSpace(t.Device) && t.Direction != Models.TagDirection.Memory && !string.IsNullOrWhiteSpace(t.Address)))
                .ToList();
        OnPropertyChanged(nameof(DeviceTags));
        RefreshFiltered();
    }

    /// <summary>Xếp lại nhóm theo từ khoá lọc: Instructions đứng đầu, sau đó là từng device.</summary>
    private void RefreshFiltered()
    {
        var filter = FilterText.Trim();
        var groups = new List<ToolboxGroup>();

        if (ShowInstructions)
        {
            groups.Add(new ToolboxGroup("Instructions", filter.Length == 0
                ? Instructions
                : Instructions.Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList()));
        }

        foreach (var group in DeviceTags
                     .Where(t => filter.Length == 0 ||
                                 t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                 t.Device.Contains(filter, StringComparison.OrdinalIgnoreCase))
                     .GroupBy(t => string.IsNullOrEmpty(t.Device) ? "(unassigned)" : t.Device)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            groups.Add(new ToolboxGroup(group.Key, group.ToList()));
        }

        Groups = groups;
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(FilterSummary));
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
}

/// <summary>Một dòng trong card Device Tags: tag đã khai trong Tag Table kèm trạng thái map.</summary>
/// <param name="IsMapped">● đã trỏ tới device+address; ○ chưa hoàn chỉnh.</param>
public sealed record DeviceTagItem(
    string Name,
    string DataType,
    string Device,
    string Address,
    bool IsMapped)
{
    public string MapGlyph => IsMapped ? "●" : "○";
    public string Display => $"{Name} · {DataType} · {(string.IsNullOrEmpty(Device) ? "—" : Device)}{(string.IsNullOrEmpty(Address) ? "" : "/" + Address)}";
}

public sealed record TaskCardItem(string Name, string Snippet);

/// <summary>
/// Một nhóm mục trong Toolbox kiểu VS: tiêu đề bấm để sập/mở, đếm số mục ở lề phải.
/// </summary>
public partial class ToolboxGroup : ObservableObject
{
    public ToolboxGroup(string title, IReadOnlyList<object> items)
    {
        Title = title;
        Items = items;
    }

    public string Title { get; }
    public IReadOnlyList<object> Items { get; }

    [ObservableProperty]
    private bool _isExpanded = true;
}
