using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;

namespace DBI.Controller.Studio.Core.ViewModels;

public sealed partial class ForceRowViewModel : ObservableObject
{
    public ForceRowViewModel(ForceInfo force)
    {
        TagName = force.TagName;
        ValueText = force.ValueJson;
    }

    public string TagName { get; }
    [ObservableProperty] private string _valueText;
}

/// <summary>Runtime force state. Force is deliberately not persisted in the project file.</summary>
public sealed partial class ForceTableViewModel : DocumentViewModelBase
{
    private readonly IRuntimeClient _runtime;
    private readonly IUserPrompt _prompt;

    public ForceTableViewModel(IRuntimeClient runtime, IUserPrompt prompt)
        : base("ForceTable", "Force Table")
    {
        _runtime = runtime;
        _prompt = prompt;
    }

    public ObservableCollection<ForceRowViewModel> Rows { get; } = new();
    public string Banner => Rows.Count == 0 ? "No active forces" : $"{Rows.Count} TAG ĐANG BỊ FORCE";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Rows.Clear();
        foreach (var force in await _runtime.GetForcesAsync())
            Rows.Add(new ForceRowViewModel(force));
        OnPropertyChanged(nameof(Banner));
    }

    [RelayCommand]
    private async Task ClearAsync(ForceRowViewModel? row)
    {
        if (row is null) return;
        await _runtime.ForceTagAsync(row.TagName, Parse(row.ValueText), enable: false);
        Rows.Remove(row);
        OnPropertyChanged(nameof(Banner));
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (Rows.Count == 0 || !_prompt.Confirm(
                $"{Rows.Count} tag đang bị force. Xoá toàn bộ force?", "Cảnh báo an toàn")) return;

        foreach (var row in Rows.ToList())
            await _runtime.ForceTagAsync(row.TagName, Parse(row.ValueText), enable: false);
        Rows.Clear();
        OnPropertyChanged(nameof(Banner));
    }

    private static object Parse(string value)
    {
        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.True or JsonValueKind.False => doc.RootElement.GetBoolean(),
                JsonValueKind.Number when doc.RootElement.TryGetInt32(out var i) => i,
                JsonValueKind.Number => doc.RootElement.GetSingle(),
                _ => value
            };
        }
        catch { return value; }
    }
}
