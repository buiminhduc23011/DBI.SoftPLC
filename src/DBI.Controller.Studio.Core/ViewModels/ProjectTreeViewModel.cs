using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DBI.Controller.Studio.Core.Models;

namespace DBI.Controller.Studio.Core.ViewModels;

public enum ProjectNodeKind { Root, Folder, Block, TagTable, Device, WatchTable }

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
        ProjectNodeKind.Block => "◆",
        ProjectNodeKind.TagTable => "▦",
        ProjectNodeKind.Device => "⬢",
        ProjectNodeKind.WatchTable => "◉",
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

    public ObservableCollection<ProjectNode> Roots { get; } = new();

    [ObservableProperty]
    private ProjectNode? _selectedNode;

    /// <summary>Phát khi người dùng mở một node (double-click). Shell quyết định mở tài liệu nào.</summary>
    public event EventHandler<ProjectNode>? NodeActivated;

    public void RaiseNodeActivated(ProjectNode node) => NodeActivated?.Invoke(this, node);

    /// <summary>Dựng lại cây từ project. Gọi khi mở project hoặc sau khi thêm/xoá khối.</summary>
    public void Load(DbiProject? project)
    {
        Roots.Clear();

        if (project is null)
        {
            Title = "Project";
            return;
        }

        Title = project.Name;

        var root = new ProjectNode(ProjectNodeKind.Root, project.Name, project);

        var blocks = new ProjectNode(ProjectNodeKind.Folder, "Program Blocks");
        foreach (var block in project.Blocks)
            blocks.Children.Add(new ProjectNode(ProjectNodeKind.Block, block.Name, block));

        var tags = new ProjectNode(ProjectNodeKind.Folder, "PLC Tags");
        foreach (var table in project.TagTables)
            tags.Children.Add(new ProjectNode(ProjectNodeKind.TagTable, table.Name, table));

        var devices = new ProjectNode(ProjectNodeKind.Folder, "Devices");
        foreach (var device in project.Devices)
            devices.Children.Add(new ProjectNode(ProjectNodeKind.Device, device.Name, device));

        var watches = new ProjectNode(ProjectNodeKind.Folder, "Watch Tables");
        foreach (var watch in project.WatchTables)
            watches.Children.Add(new ProjectNode(ProjectNodeKind.WatchTable, watch.Name, watch));

        root.Children.Add(blocks);
        root.Children.Add(tags);
        root.Children.Add(devices);
        root.Children.Add(watches);

        Roots.Add(root);
    }

    /// <summary>Thuộc tính của node đang chọn, để Inspector hiển thị ở tab Properties.</summary>
    public static IEnumerable<PropertyRow> DescribeNode(ProjectNode node) => node.Payload switch
    {
        CodeBlock block => new[]
        {
            new PropertyRow("Tên", block.Name),
            new PropertyRow("Loại khối", block.Kind.ToString()),
            new PropertyRow("Tệp", block.FileName),
            new PropertyRow("Ghi chú", block.Comment, isEditable: true)
        },
        TagTable table => new[]
        {
            new PropertyRow("Tên bảng", table.Name),
            new PropertyRow("Số tag", table.Tags.Count.ToString())
        },
        DeviceConfig device => new[]
        {
            new PropertyRow("Tên thiết bị", device.Name),
            new PropertyRow("Loại driver", device.DriverType),
            new PropertyRow("Tham số", string.Join(", ", device.Settings.Select(s => $"{s.Key}={s.Value}")))
        },
        WatchTable watch => new[]
        {
            new PropertyRow("Tên bảng", watch.Name),
            new PropertyRow("Số tag theo dõi", watch.TagNames.Count.ToString())
        },
        DbiProject project => new[]
        {
            new PropertyRow("Tên project", project.Name),
            new PropertyRow("Mô tả", project.Description, isEditable: true),
            new PropertyRow("Schema", project.SchemaVersion),
            new PropertyRow("Chu kỳ quét", $"{project.Runtime.ScanIntervalMs} ms"),
            new PropertyRow("Tự chạy sau mất điện", project.Runtime.AutoStart ? "Có" : "Không")
        },
        _ => new[] { new PropertyRow("Tên", node.Title) }
    };
}

/// <summary>
/// Bảng thẻ tác vụ bên phải. phase-12 làm nội dung thật (kéo-thả tag vào khối).
/// </summary>
public partial class TaskCardsViewModel : PaneViewModelBase
{
    public TaskCardsViewModel() : base("TaskCards", "Toolbox") { }

    [ObservableProperty]
    private string _placeholder = "Thẻ tác vụ và kéo-thả tag sẽ có ở phase-12.";
}
