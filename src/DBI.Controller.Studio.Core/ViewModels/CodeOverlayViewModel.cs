using System.Collections.Concurrent;
using CommunityToolkit.Mvvm.ComponentModel;
using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;

namespace DBI.Controller.Studio.Core.ViewModels;

/// <summary>Một marker ⟨giá trị⟩ hiển thị cuối dòng code khi monitoring bật.</summary>
public readonly record struct OverlayMarker(int Line, string TagName, string DisplayValue);

/// <summary>
/// Live code overlay cho một tab editor (phase-13): phân tích <c>IO.tag</c> trong nguồn,
/// nhận giá trị push từ Runtime, cung cấp marker cho vùng nhìn thấy.
/// </summary>
/// <remarks>
/// Task 13.4 chống nghẽn: giá trị push đổ vào buffer, chỉ rót theo yêu cầu vẽ lại
/// (throttle do View lo — ≤10Hz). Subscribe chỉ những tag trong file (Task 13.3).
/// </remarks>
public sealed partial class CodeOverlayViewModel : ObservableObject, IDisposable
{
    private readonly LiveCodeOverlayService _overlay;
    private readonly IRuntimeClient _runtime;
    private readonly ConcurrentDictionary<string, byte> _subscribed = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CodeTagReference> _references = Array.Empty<CodeTagReference>();
    private bool _disposed;

    public CodeOverlayViewModel(LiveCodeOverlayService overlay, IRuntimeClient runtime)
    {
        _overlay = overlay;
        _runtime = runtime;
        _runtime.TagValueChanged += OnTagValueChanged;
    }

    /// <summary>Monitoring bật/tắt. Bật → subscribe tag trong file; tắt → dọn sạch.</summary>
    [ObservableProperty]
    private bool _monitoring;

    partial void OnMonitoringChanged(bool value)
    {
        if (_disposed) return;

        if (value)
        {
            ResubscribeAsync();
        }
        else
        {
            UnsubscribeAll();
            _overlay.Clear();
            Markers = Array.Empty<OverlayMarker>();
        }
    }

    /// <summary>Marker hiện tại cho vùng nhìn thấy — View đọc để vẽ.</summary>
    public IReadOnlyList<OverlayMarker> Markers { get; private set; } = Array.Empty<OverlayMarker>();

    /// <summary>Phát khi marker đổi — View vẽ lại đúng lúc.</summary>
    public event EventHandler? MarkersChanged;

    /// <summary>Phân tích lại nguồn khi file đổi (Task 13.2). Regex IO.\w — phương án lùi được chấp nhận.</summary>
    public void UpdateSource(string source)
    {
        if (_disposed) return;
        _references = _overlay.Analyze(source);
        if (Monitoring) ResubscribeAsync();
    }

    /// <summary>Cập nhật marker cho vùng nhìn thấy (Task 13.3 — chỉ subscribe/vẽ vùng thấy).</summary>
    public void UpdateVisibleRange(int firstLine, int lastLine)
    {
        if (_disposed || !Monitoring) return;

        var markers = _overlay.BuildVisibleMarkers(_references, firstLine, lastLine);
        Markers = markers
            .Select(m => new OverlayMarker(m.Line, m.TagName, m.DisplayValue))
            .ToList();
        MarkersChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void ResubscribeAsync()
    {
        var names = _references.Select(r => r.TagName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) return;

        await _runtime.SubscribeTagsAsync(names);

        foreach (var name in names)
            _subscribed.TryAdd(name, 0);
    }

    private void UnsubscribeAll()
    {
        var names = _subscribed.Keys.ToList();
        _subscribed.Clear();
        if (names.Count > 0) _ = _runtime.UnsubscribeTagsAsync(names);
    }

    private void OnTagValueChanged(object? sender, TagValueUpdate update)
    {
        if (!Monitoring) return;
        _overlay.Apply(update); // buffer — View rót qua UpdateVisibleRange ≤10Hz
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Monitoring = false;
        _runtime.TagValueChanged -= OnTagValueChanged;
        GC.SuppressFinalize(this);
    }
}
