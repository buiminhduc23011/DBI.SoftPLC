using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;

namespace DBI.Controller.Studio.Core.ViewModels;

public sealed partial class WatchRowViewModel : ObservableObject
{
    public WatchRowViewModel(string name) => Name = name;
    public string Name { get; }
    [ObservableProperty] private string _valueText = "—";
    [ObservableProperty] private DateTimeOffset? _updatedAt;
}

public partial class WatchTableViewModel : DocumentViewModelBase
{
    private readonly WatchTable _table;
    private readonly IRuntimeClient _runtime;
    private readonly ProjectService _projects;
    private bool _subscribed;

    public WatchTableViewModel(DbiProject project, WatchTable table, IRuntimeClient runtime, ProjectService projects)
        : base($"Watch:{table.Name}", table.Name)
    {
        _table = table; _runtime = runtime; _projects = projects;
        foreach (var name in table.TagNames) Rows.Add(new WatchRowViewModel(name));
        _runtime.TagValueChanged += OnTagValueChanged;
    }

    public ObservableCollection<WatchRowViewModel> Rows { get; } = new();
    [ObservableProperty] private bool _monitoring;

    partial void OnMonitoringChanged(bool value)
    {
        if (value) _ = SubscribeAsync(); else _ = UnsubscribeAsync();
    }

    [RelayCommand]
    private void ToggleMonitoring() => Monitoring = !Monitoring;

    [RelayCommand]
    private void AddTag()
    {
        if (_table.TagNames.Count == 0) return;
        var name = _table.TagNames.FirstOrDefault(n => Rows.All(r => !r.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));
        if (name is null) return;
        Rows.Add(new WatchRowViewModel(name)); IsDirty = true;
    }

    [RelayCommand]
    private void RemoveSelected(WatchRowViewModel? row)
    {
        if (row is null) return;
        Rows.Remove(row); _table.TagNames.Remove(row.Name); IsDirty = true;
    }

    private async Task SubscribeAsync() { _subscribed = true; await _runtime.SubscribeTagsAsync(Rows.Select(r => r.Name)); }
    private async Task UnsubscribeAsync() { if (!_subscribed) return; _subscribed = false; await _runtime.UnsubscribeTagsAsync(Rows.Select(r => r.Name)); }

    private void OnTagValueChanged(object? sender, TagValueUpdate update)
    {
        var row = Rows.FirstOrDefault(r => r.Name.Equals(update.TagName, StringComparison.OrdinalIgnoreCase));
        if (row is null) return;
        try { row.ValueText = FormatValue(update.ValueJson); } catch { row.ValueText = update.ValueJson; }
        row.UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(update.TimestampMs);
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
}
