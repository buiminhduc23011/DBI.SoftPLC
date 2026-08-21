using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DBI.Controller.Studio.Core.Services.CodeAnalysis;
using DBI.Controller.Studio.Core.ViewModels;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.CodeAnalysis;
using RoslynPad.Editor;
using RoslynPad.Roslyn;

namespace DBI.Controller.Studio.Behaviors;

public static class RoslynCodeEditorBehaviors
{
    public static readonly DependencyProperty EnableRoslynProperty =
        DependencyProperty.RegisterAttached(
            "EnableRoslyn",
            typeof(bool),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(false, OnEnableRoslynChanged));

    public static bool GetEnableRoslyn(DependencyObject target) =>
        (bool)target.GetValue(EnableRoslynProperty);

    public static void SetEnableRoslyn(DependencyObject target, bool value) =>
        target.SetValue(EnableRoslynProperty, value);

    /// <summary>Bật live overlay ⟨giá trị⟩ cuối dòng (phase-13). View lo throttle ≤10Hz.</summary>
    public static readonly DependencyProperty EnableOverlayProperty =
        DependencyProperty.RegisterAttached(
            "EnableOverlay",
            typeof(bool),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(false, OnEnableOverlayChanged));

    public static bool GetEnableOverlay(DependencyObject target) =>
        (bool)target.GetValue(EnableOverlayProperty);

    public static void SetEnableOverlay(DependencyObject target, bool value) =>
        target.SetValue(EnableOverlayProperty, value);

    private static readonly DependencyProperty OverlayModelProperty =
        DependencyProperty.RegisterAttached(
            "OverlayModel",
            typeof(CodeOverlayViewModel),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(null));

    private static readonly DependencyProperty OverlayTimerProperty =
        DependencyProperty.RegisterAttached(
            "OverlayTimer",
            typeof(DispatcherTimer),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(null));

    private static readonly DependencyProperty OverlayMarginProperty =
        DependencyProperty.RegisterAttached(
            "OverlayMargin",
            typeof(AbstractMargin),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(null, OnOverlayMarginChanged));

    private static void OnEnableOverlayChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RoslynCodeEditor editor) return;

        if ((bool)e.NewValue)
        {
            editor.Loaded -= OnOverlayLoaded;
            editor.Loaded += OnOverlayLoaded;
            editor.Unloaded -= OnOverlayUnloaded;
            editor.Unloaded += OnOverlayUnloaded;
        }
        else
        {
            editor.Loaded -= OnOverlayLoaded;
            editor.Unloaded -= OnOverlayUnloaded;
        }
    }

    private static void OnOverlayLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;
        if (editor.DataContext is not CodeEditorViewModel model || model.Overlay is null) return;

        editor.SetValue(OverlayModelProperty, model.Overlay);
        model.Overlay.MarkersChanged += (_, _) => ApplyMarkers(editor, model.Overlay);

        // Vẽ lại theo vùng nhìn thấy mỗi 100ms khi monitoring bật — Task 13.4 (≤10Hz).
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        timer.Tick += (_, _) =>
        {
            if (!model.Overlay.Monitoring) return;
            RefreshVisibleRange(editor, model.Overlay);
            ApplyMarkers(editor, model.Overlay);
        };
        timer.Start();
        editor.SetValue(OverlayTimerProperty, timer);
    }

    private static void OnOverlayUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;

        (editor.GetValue(OverlayTimerProperty) as DispatcherTimer)?.Stop();
        editor.SetValue(OverlayTimerProperty, null);
    }

    private static void RefreshVisibleRange(RoslynCodeEditor editor, CodeOverlayViewModel overlay)
    {
        try
        {
            int first = Math.Max(1, editor.TextArea.TextView.VisualLines.Count > 0
                ? editor.TextArea.TextView.VisualLines.First().FirstDocumentLine.LineNumber : 1);
            int last = editor.TextArea.TextView.VisualLines.Count > 0
                ? editor.TextArea.TextView.VisualLines.Last().LastDocumentLine.LineNumber : first;
            overlay.UpdateVisibleRange(first, last);
        }
        catch { /* visual lines đang dựng lại — kỳ vẽ sau thử tiếp */ }
    }

    private static void ApplyMarkers(RoslynCodeEditor editor, CodeOverlayViewModel overlay)
    {
        if (editor.GetValue(OverlayMarginProperty) is not OverlayMarkerMargin margin) return;
        margin.Update(overlay.Markers);
    }

    /// <summary>Gắn margin hiển thị marker vào TextArea lần đầu loaded.</summary>
    private static void OnOverlayMarginChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RoslynCodeEditor editor) return;

        if (e.OldValue is AbstractMargin old) editor.TextArea.LeftMargins.Remove(old);

        if (e.NewValue is AbstractMargin margin && !editor.TextArea.LeftMargins.Contains(margin))
        {
            // TextArea.LeftMargins tự gán margin.TextView — không set tay trước khi add.
            editor.TextArea.LeftMargins.Add(margin);
        }
    }

    private static readonly DependencyProperty TimerProperty =
        DependencyProperty.RegisterAttached(
            "Timer",
            typeof(DispatcherTimer),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(null));

    private static readonly DependencyProperty LoadedProperty =
        DependencyProperty.RegisterAttached(
            "Loaded",
            typeof(bool),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(false));

    private static void OnEnableRoslynChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RoslynCodeEditor editor) return;

        if ((bool)e.NewValue)
        {
            editor.Loaded -= OnLoaded;
            editor.Loaded += OnLoaded;
            editor.Unloaded -= OnUnloaded;
            editor.Unloaded += OnUnloaded;
        }
        else
        {
            editor.Loaded -= OnLoaded;
            editor.Unloaded -= OnUnloaded;
            editor.TextChanged -= OnTextChanged;
        }
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;
        if (GetLoaded(editor)) return;

        SetLoaded(editor, true);

        if (editor.DataContext is not CodeEditorViewModel model) return;
        if (StudioRoslynWorkspace.Current is null) return;

        editor.TextChanged -= OnTextChanged;
        editor.TextChanged += OnTextChanged;

        _ = InitializeEditorAsync(editor, model, StudioRoslynWorkspace.Current);
    }

    private static async Task InitializeEditorAsync(
        RoslynCodeEditor editor,
        CodeEditorViewModel model,
        StudioRoslynWorkspace roslynWorkspace)
    {
        try
        {
            var host = roslynWorkspace.Host;
            if (host is null) return;

        var projectDirectory = Path.GetDirectoryName(model.AbsolutePath) ?? Environment.CurrentDirectory;
        var documentId = await editor.InitializeAsync(
            host,
            new ClassificationHighlightColors(),
            projectDirectory,
            model.Text,
            SourceCodeKind.Regular);

        var document = host.GetDocument(documentId);
        if (document?.Project.Solution.Workspace is RoslynWorkspace workspace)
            roslynWorkspace.AttachProjectDocuments(workspace, documentId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Roslyn editor initialization failed: {ex}");
        }
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;
        editor.TextChanged -= OnTextChanged;
        SetLoaded(editor, false);
    }

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;
        if (editor.DataContext is not CodeEditorViewModel model) return;
        if (StudioRoslynWorkspace.Current is null) return;

        model.Text = editor.Text;

        var timer = (DispatcherTimer?)editor.GetValue(TimerProperty);
        if (timer is null)
        {
            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                StudioRoslynWorkspace.Current.UpdateDocument(model.AbsolutePath, model.Text);
            };
            editor.SetValue(TimerProperty, timer);
        }

        timer.Stop();
        timer.Start();
    }

    private static bool GetLoaded(DependencyObject target) => (bool)target.GetValue(LoadedProperty);
    private static void SetLoaded(DependencyObject target, bool value) => target.SetValue(LoadedProperty, value);
}

/// <summary>
/// Margin trái hiển thị marker ⟨giá trị⟩ của live overlay (phase-13 Task 13.1). Vẽ theo
/// dòng trong vùng nhìn thấy — không đụng vào văn bản nên gõ phím không bị chậm.
/// </summary>
internal sealed class OverlayMarkerMargin : AbstractMargin
{
    private IReadOnlyList<OverlayMarker> _markers = Array.Empty<OverlayMarker>();
    private readonly Typeface _typeface = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public void Update(IReadOnlyList<OverlayMarker> markers)
    {
        _markers = markers;
        InvalidateVisual();
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;

    protected override Size MeasureOverride(Size availableSize)
    {
        // Cột marker rộng cố định — đủ chỗ cho ⟨FALSE⟩ và số.
        return new Size(72, 0);
    }

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        base.OnTextViewChanged(oldTextView, newTextView);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (TextView is null || _markers.Count == 0) return;
        if (!TextView.VisualLinesValid) return;

        foreach (var line in TextView.VisualLines)
        {
            var marker = _markers.FirstOrDefault(m => m.Line == line.FirstDocumentLine.LineNumber);
            if (marker == default) continue;

            double y = line.GetTextLineVisualYPosition(
                line.TextLines[0], VisualYPosition.LineMiddle);

            var text = new FormattedText(
                $"⟨{marker.DisplayValue}⟩",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                _typeface,
                11,
                System.Windows.Media.Brushes.DodgerBlue,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            drawingContext.DrawText(text, new Point(4, y - text.Height / 2));
        }
    }
}
