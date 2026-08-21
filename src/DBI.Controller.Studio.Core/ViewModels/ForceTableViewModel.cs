using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;

namespace DBI.Controller.Studio.Core.ViewModels;

public sealed partial class ForceRowViewModel : ObservableObject
{
    public ForceRowViewModel(ForceInfo force)
    {
        TagName = force.TagName;
        ValueText = Format(force.ValueJson);
        _forceValueJson = force.ValueJson;
    }

    public string TagName { get; }

    /// <summary>Giá trị force dạng JSON gốc — dùng khi Clear (cần đúng kiểu để gỡ).</summary>
    private readonly string _forceValueJson;

    [ObservableProperty] private string _valueText;

    public string ForceValueJson => _forceValueJson;

    /// <summary>Giá trị hiển thị: TRUE/FALSE cho bool, số thập phân cho số.</summary>
    internal static string Format(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.True => "TRUE",
                JsonValueKind.False => "FALSE",
                JsonValueKind.Number when doc.RootElement.TryGetDouble(out var n) => n.ToString("0.##"),
                _ => json
            };
        }
        catch { return json; }
    }
}

/// <summary>
/// Force Table — trạng thái force của Runtime. Force là trạng thái runtime tạm thời,
/// KHÔNG lưu vào .dbiproj; deploy mới và fault đều xoá sạch.
/// </summary>
/// <remarks>
/// ⚠️ An toàn máy móc: tạo force phải qua hộp thoại xác nhận có checkbox bắt buộc, không có
/// "đừng hỏi lại". Banner đỏ ở status bar hiện khi còn force bất kỳ (Shell lo phần đó).
/// </remarks>
public sealed partial class ForceTableViewModel : DocumentViewModelBase
{
    private readonly IRuntimeClient _runtime;
    private readonly IUserPrompt _prompt;
    private readonly DbiProject? _project;

    public ForceTableViewModel(IRuntimeClient runtime, IUserPrompt prompt, DbiProject? project = null)
        : base("ForceTable", "Force Table")
    {
        _runtime = runtime;
        _prompt = prompt;
        _project = project;
    }

    public ObservableCollection<ForceRowViewModel> Rows { get; } = new();

    /// <summary>Danh sách tag của project — nguồn cho dropdown tạo force.</summary>
    public IReadOnlyList<string> AvailableTags =>
        _project?.AllTags().Select(t => t.Name).ToList() ?? new List<string>();

    [ObservableProperty] private string? _selectedTagName;

    /// <summary>Giá trị muốn force, người dùng nhập dạng chữ ("true", "85.0"...).</summary>
    [ObservableProperty] private string _newValueText = "";

    /// <summary>Banner trong bảng: đếm số tag đang bị force.</summary>
    public string Banner => Rows.Count == 0 ? "No active forces" : $"⚠️ {Rows.Count} TAG ĐANG BỊ FORCE";

    /// <summary>Còn force nào không — Shell dùng để bật/tắt banner đỏ toàn cửa sổ.</summary>
    public bool HasForces => Rows.Count > 0;

    /// <summary>Phát khi tập force đổi — banner status bar và badge cây cần làm mới.</summary>
    public event EventHandler? ForcesChanged;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Rows.Clear();
        foreach (var force in await _runtime.GetForcesAsync())
            Rows.Add(new ForceRowViewModel(force));
        OnPropertyChanged(nameof(Banner));
        OnPropertyChanged(nameof(HasForces));
        ForcesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Tạo force mới — LUÔN đi qua hộp thoại xác nhận an toàn có checkbox bắt buộc
    /// (Task 11.5). Không có tuỳ chọn "đừng hỏi lại".
    /// </summary>
    [RelayCommand]
    private async Task ApplyForceAsync()
    {
        string? tagName = SelectedTagName;
        if (string.IsNullOrWhiteSpace(tagName) || string.IsNullOrWhiteSpace(NewValueText)) return;

        var tag = _project?.AllTags().FirstOrDefault(
            t => t.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase));
        if (tag is null) return;

        object value;
        try
        {
            value = tag.DataType switch
            {
                TagDataType.Bool => ParseBool(NewValueText),
                TagDataType.Int => int.Parse(NewValueText.Trim()),
                TagDataType.Real => float.Parse(NewValueText.Trim()),
                _ => NewValueText
            };
        }
        catch (FormatException)
        {
            _prompt.ShowError($"'{NewValueText}' không đọc được thành {tag.DataType}.", "Force");
            return;
        }

        // Chốt an toàn: xác nhận có checkbox bắt buộc trước khi ghi đè tín hiệu thật.
        if (!_prompt.ConfirmForceSafety(tagName, DescribeValue(tag.DataType), NewValueText))
            return;

        var result = await _runtime.ForceTagAsync(tagName, value, enable: true);
        if (!result)
        {
            _prompt.ShowError($"Không force được tag '{tagName}'.", "Force");
            return;
        }

        NewValueText = "";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ClearAsync(ForceRowViewModel? row)
    {
        if (row is null) return;
        await _runtime.ForceTagAsync(row.TagName, Parse(row.ForceValueJson), enable: false);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (Rows.Count == 0 || !_prompt.Confirm(
                $"{Rows.Count} tags are currently forced. Clear all forces?", "Safety confirmation")) return;

        foreach (var row in Rows.ToList())
            await _runtime.ForceTagAsync(row.TagName, Parse(row.ForceValueJson), enable: false);
        await RefreshAsync();
    }

    private static string DescribeValue(TagDataType dataType) => dataType switch
    {
        TagDataType.Bool => "TRUE/FALSE",
        TagDataType.Int => "số nguyên",
        TagDataType.Real => "số thực",
        _ => "giá trị"
    };

    private static bool ParseBool(string text) => text.Trim().ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => throw new FormatException()
    };

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
