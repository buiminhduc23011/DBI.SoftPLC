using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;

namespace DBI.Controller.Studio.Core.ViewModels;

/// <summary>Một dòng trong Watch Table — kèm kiểu/chiều của tag để khoá Modify cho Input.</summary>
public sealed partial class WatchRowViewModel : ObservableObject
{
    public WatchRowViewModel(string name, TagDataType dataType = TagDataType.Bool, TagDirection direction = TagDirection.Memory)
    {
        Name = name;
        DataType = dataType;
        Direction = direction;
    }

    public string Name { get; }
    public TagDataType DataType { get; }
    public TagDirection Direction { get; }

    /// <summary>Task 10.4 — tag Input do driver ghi đè mỗi chu kỳ nên không sửa được.</summary>
    public bool CanModify => Direction is TagDirection.Output or TagDirection.Memory;
    public string ModifyToolTip => CanModify
        ? "Ghi một lần vào tag."
        : "Tag Input không sửa được: driver ghi đè giá trị mỗi chu kỳ (20ms). Dùng Force Table nếu cần ép giá trị.";

    [ObservableProperty] private string _valueText = "—";
    [ObservableProperty] private DateTimeOffset? _updatedAt;

    /// <summary>Khóa màu của giá trị: Bool TRUE xanh / FALSE đỏ, mất kết nối thì xám.</summary>
    [ObservableProperty] private string _valueBrushKey = "TextPrimary";

    /// <summary>Nháy nền vàng 300ms khi giá trị đổi (Task 10.3).</summary>
    [ObservableProperty] private bool _isFlashing;

    /// <summary>Mất kết nối: giữ nguyên giá trị cuối, chỉ xám + ⚠️ — KHÔNG xoá về 0.</summary>
    [ObservableProperty] private bool _isStale;

    public string ModifyText { get; set; } = "";

    public void ApplyStale(bool stale)
    {
        if (IsStale == stale) return;
        IsStale = stale;
        ValueBrushKey = stale ? "MutedColor" : DefaultBrushKey();
    }

    private string DefaultBrushKey() => DataType switch
    {
        TagDataType.Bool when ValueText == "TRUE" => "SuccessColor",
        TagDataType.Bool => "DangerColor",
        _ => "TextPrimary"
    };

    public void SetValueText(string text)
    {
        ValueText = text;
        ValueBrushKey = DefaultBrushKey();
    }
}

/// <summary>
/// Watch Table — theo dõi giá trị tag real-time qua kênh push của Runtime.
/// </summary>
/// <remarks>
/// Task 10.5 chống nghẽn UI: message push KHÔNG đụng vào row ngay mà đổ vào buffer
/// <see cref="_pending"/>; vòng quét 100ms mới rót xuống một lượt. 200 tag × 10Hz thành
/// ~10 lần cập nhật/giây thay vì 2000 lần raise PropertyChanged.
/// </remarks>
public partial class WatchTableViewModel : DocumentViewModelBase, IDisposable
{
    private readonly DbiProject _project;
    private readonly WatchTable _table;
    private readonly IRuntimeClient _runtime;
    private readonly ProjectService _projects;
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private readonly ConcurrentDictionary<string, TagValueUpdate> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _flushTimer;
    private bool _subscribed;
    private int _disposed;

    public WatchTableViewModel(DbiProject project, WatchTable table, IRuntimeClient runtime, ProjectService projects)
        : base($"Watch:{table.Name}", table.Name)
    {
        _project = project; _table = table; _runtime = runtime; _projects = projects;

        foreach (var name in table.TagNames)
        {
            var tag = project.AllTags().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Rows.Add(new WatchRowViewModel(name, tag?.DataType ?? TagDataType.Bool, tag?.Direction ?? TagDirection.Memory));
        }

        _runtime.TagValueChanged += OnTagValueChanged;
        _runtime.StateChanged += OnRuntimeStateChanged;
        // Có UI context thì rót trên UI thread; không có (test/headless) thì rót luôn trên
        // thread timer — chỉ set property nên WPF tự marshal được.
        _flushTimer = new Timer(
            _ =>
            {
                if (_uiContext is not null) _uiContext.Post(__ => FlushPending(), null);
                else FlushPending();
            },
            null, 100, 100);
    }

    public ObservableCollection<WatchRowViewModel> Rows { get; } = new();
    [ObservableProperty] private bool _monitoring;

    /// <summary>Danh sách tag của project còn chưa có trong bảng — nguồn cho dropdown thêm tag.</summary>
    public IReadOnlyList<string> AvailableTags =>
        _project.AllTags()
            .Where(t => Rows.All(r => !r.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(t => t.Name)
            .ToList();

    [ObservableProperty] private string? _selectedTagName;

    /// <summary>Dòng đang chọn ở DataGrid — nút Remove thao tác trên dòng này.</summary>
    [ObservableProperty] private WatchRowViewModel? _selectedRow;

    /// <summary>Phát khi tập dòng đổi — dropdown và cây project cần làm mới.</summary>
    public event EventHandler? RowsChanged;

    /// <summary>Phát khi cần ghi chú vào Inspector — DocumentHost nối sang <see cref="DocumentHostViewModel.Notice"/>.</summary>
    public event EventHandler<(string Message, IssueSeverity Severity)>? Notice;

    partial void OnMonitoringChanged(bool value)
    {
        if (value) _ = SubscribeAsync(); else _ = UnsubscribeAsync();
    }

    [RelayCommand]
    private void ToggleMonitoring() => Monitoring = !Monitoring;

    [RelayCommand]
    private void AddSelectedTag()
    {
        string? name = SelectedTagName;
        if (string.IsNullOrWhiteSpace(name)) return;

        var tag = _project.AllTags().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (tag is null) return;

        Rows.Add(new WatchRowViewModel(tag.Name, tag.DataType, tag.Direction));
        IsDirty = true;
        SelectedTagName = null;
        OnPropertyChanged(nameof(AvailableTags));
        RowsChanged?.Invoke(this, EventArgs.Empty);
        if (Monitoring) _ = SubscribeAsync();
    }

    [RelayCommand]
    private void RemoveSelected(WatchRowViewModel? row)
    {
        if (row is null) return;
        Rows.Remove(row); _table.TagNames.Remove(row.Name); IsDirty = true;
        OnPropertyChanged(nameof(AvailableTags));
        RowsChanged?.Invoke(this, EventArgs.Empty);
        if (Monitoring) _ = SubscribeAsync(); // đồng bộ lại tập subscription với tập dòng đang hiện
    }

    /// <summary>Task 10.4 — ghi một lần vào tag. Chỉ Output/Memory; Input bị chặn kèm giải thích.</summary>
    [RelayCommand]
    private async Task ModifyValueAsync(WatchRowViewModel? row)
    {
        if (row is null || !row.CanModify || string.IsNullOrWhiteSpace(row.ModifyText)) return;

        object value;
        try
        {
            value = row.DataType switch
            {
                TagDataType.Bool => ParseBool(row.ModifyText),
                TagDataType.Int => int.Parse(row.ModifyText),
                TagDataType.Real => float.Parse(row.ModifyText),
                _ => row.ModifyText
            };
        }
        catch (FormatException)
        {
            Notice?.Invoke(this, ($"'{row.ModifyText}' không đọc được thành {row.DataType}.", IssueSeverity.Error));
            return;
        }

        var result = await _runtime.WriteTagAsync(row.Name, value).ConfigureAwait(true);

        if (!result.Ok)
            Notice?.Invoke(this, (result.Error ?? "Ghi tag thất bại.", IssueSeverity.Error));
        else
            Notice?.Invoke(this, ($"Đã ghi '{row.Name}' = {row.ModifyText}.", IssueSeverity.Warning));
    }

    private static bool ParseBool(string text) => text.Trim().ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => throw new FormatException()
    };

    // ── Kênh dữ liệu (Task 10.2) ─────────────────────────────────────────────────

    private async Task SubscribeAsync()
    {
        _subscribed = true;
        _table.TagNames.Clear();
        _table.TagNames.AddRange(Rows.Select(r => r.Name));
        await _runtime.SubscribeTagsAsync(Rows.Select(r => r.Name));
    }

    private async Task UnsubscribeAsync()
    {
        if (!_subscribed) return;
        _subscribed = false;
        await _runtime.UnsubscribeTagsAsync(Rows.Select(r => r.Name));
    }

    private void OnTagValueChanged(object? sender, TagValueUpdate update) =>
        _pending[update.TagName] = update; // latest wins — vòng flush lo phần còn lại

    /// <summary>Rót buffer vào UI một lượt mỗi 100ms (Task 10.5). Public để test gọi trực tiếp, không phụ thuộc timing.</summary>
    public void FlushPending()
    {
        if (Volatile.Read(ref _disposed) > 0) return;

        foreach (var kvp in _pending)
        {
            if (!_pending.TryRemove(kvp.Key, out var update)) continue;

            var row = Rows.FirstOrDefault(r => r.Name.Equals(update.TagName, StringComparison.OrdinalIgnoreCase));
            if (row is null) continue;

            try { row.SetValueText(FormatValue(update.ValueJson)); }
            catch { row.SetValueText(update.ValueJson); }
            row.UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(update.TimestampMs);
            row.IsFlashing = true;
        }

        foreach (var row in Rows.Where(r => r.IsFlashing))
            row.IsFlashing = false; // nháy đúng một kỳ flush (~100–200ms, đủ mắt bắt được)
    }

    /// <summary>Mất kết nối → xám + ⚠️, giữ giá trị cuối. Nối lại → tự subscribe lại (DoD phase-10).</summary>
    private void OnRuntimeStateChanged(object? sender, RuntimeClientState state)
    {
        bool online = state == RuntimeClientState.Connected;

        foreach (var row in Rows) row.ApplyStale(!online && state != RuntimeClientState.Connecting);

        if (online && Monitoring) _ = SubscribeAsync();
    }

    private static string FormatValue(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind switch
        {
            JsonValueKind.True => "TRUE",
            JsonValueKind.False => "FALSE",
            JsonValueKind.Number when doc.RootElement.TryGetDouble(out var n) => n.ToString("0.##"),
            JsonValueKind.String => doc.RootElement.GetString() ?? "",
            _ => json
        };
    }

    public override async Task SaveAsync()
    {
        _table.TagNames.Clear(); _table.TagNames.AddRange(Rows.Select(r => r.Name));
        _projects.Save(); IsDirty = false;
        await UnsubscribeAsync();
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _disposed);
        _flushTimer.Dispose();
        _runtime.TagValueChanged -= OnTagValueChanged;
        _runtime.StateChanged -= OnRuntimeStateChanged;
        _ = UnsubscribeAsync();
        GC.SuppressFinalize(this);
    }
}
