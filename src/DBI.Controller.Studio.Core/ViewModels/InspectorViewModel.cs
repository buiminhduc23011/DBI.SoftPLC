using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Studio.Core.Services;

namespace DBI.Controller.Studio.Core.ViewModels;

/// <summary>Một dòng log ở tab Information / Diagnostics.</summary>
/// <param name="Severity">Quyết định màu hiển thị.</param>
/// <param name="Message">Nội dung cho kỹ sư đọc.</param>
/// <param name="At">Thời điểm ghi.</param>
public record LogEntry(IssueSeverity Severity, string Message, DateTimeOffset At)
{
    public string BrushKey => Severity == IssueSeverity.Error ? "DangerColor" : "WarningColor";
    public string TimeText => At.ToLocalTime().ToString("HH:mm:ss");
}

/// <summary>Một thuộc tính của node đang chọn, hiện ở tab Properties.</summary>
public partial class PropertyRow : ObservableObject
{
    public PropertyRow(string name, string value, bool isEditable = false)
    {
        Name = name;
        _value = value;
        IsEditable = isEditable;
    }

    public string Name { get; }
    public bool IsEditable { get; }

    [ObservableProperty]
    private string _value;
}

/// <summary>
/// Cửa sổ Inspector — ba tab cố định, thay hẳn ô <c>TextBox CompilerOutput</c> cũ.
/// </summary>
public partial class InspectorViewModel : PaneViewModelBase
{
    /// <summary>Giữ log ở mức đủ dùng — không để phiên chạy dài ăn hết RAM.</summary>
    public const int MaxLogEntries = 500;

    public InspectorViewModel() : base("Inspector", "Inspector") { }

    /// <summary>Thuộc tính của node đang chọn ở Project Tree.</summary>
    public ObservableCollection<PropertyRow> Properties { get; } = new();

    /// <summary>Log build, kết quả validation, thông báo deploy.</summary>
    public ObservableCollection<LogEntry> Information { get; } = new();

    /// <summary>Trạng thái Runtime, driver, fault của SafetyCatch.</summary>
    public ObservableCollection<LogEntry> Diagnostics { get; } = new();

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _selectedNodeTitle = "(chưa chọn gì)";

    public void ShowProperties(string nodeTitle, IEnumerable<PropertyRow> rows)
    {
        SelectedNodeTitle = nodeTitle;

        Properties.Clear();
        foreach (var row in rows) Properties.Add(row);
    }

    public void LogInformation(string message, IssueSeverity severity = IssueSeverity.Warning) =>
        Append(Information, new LogEntry(severity, message, DateTimeOffset.Now));

    private readonly object _logLock = new();

    public void LogInformation(IEnumerable<ValidationIssue> issues)
    {
        lock (_logLock)
        {
            foreach (var issue in issues)
                AppendInternal(Information, new LogEntry(issue.Severity, issue.Message, DateTimeOffset.Now));
        }
    }

    public void LogDiagnostic(string message, IssueSeverity severity = IssueSeverity.Warning)
    {
        lock (_logLock)
        {
            AppendInternal(Diagnostics, new LogEntry(severity, message, DateTimeOffset.Now));
        }
    }

    public void SetCodeDiagnostics(IEnumerable<CompileDiagnostic> diagnostics)
    {
        lock (_logLock)
        {
            Diagnostics.Clear();
            foreach (var diagnostic in diagnostics)
            {
                var severity = diagnostic.Severity switch
                {
                    CompileDiagnosticSeverity.Error => IssueSeverity.Error,
                    CompileDiagnosticSeverity.Warning => IssueSeverity.Warning,
                    _ => IssueSeverity.Warning
                };
                AppendInternal(Diagnostics, new LogEntry(
                    severity,
                    $"[{diagnostic.Id}] {diagnostic.Message} ({diagnostic.FilePath}:{diagnostic.Line}:{diagnostic.Column})",
                    DateTimeOffset.Now));
            }
        }
    }

    private void Append(ObservableCollection<LogEntry> target, LogEntry entry)
    {
        lock (_logLock)
        {
            AppendInternal(target, entry);
        }
    }

    private static void AppendInternal(ObservableCollection<LogEntry> target, LogEntry entry)
    {
        target.Add(entry);

        while (target.Count > MaxLogEntries) target.RemoveAt(0);
    }

    [RelayCommand]
    private void ClearInformation()
    {
        lock (_logLock) { Information.Clear(); }
    }

    [RelayCommand]
    private void ClearDiagnostics()
    {
        lock (_logLock) { Diagnostics.Clear(); }
    }
}
