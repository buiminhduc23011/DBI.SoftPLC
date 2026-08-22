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

    // ── Sync hai chiều VM ↔ editor ──────────────────────────────────────────────
    // Chiều editor→VM (TextChanged + PositionChanged) attach NGAY khi Loaded — trước khi Roslyn
    // init chạy — để keystrokes và di chuyển caret của user trong thời gian init vẫn chảy về VM.
    // Chiều VM→editor (consumer của PropertyChanged(Text)/pending-caret) chỉ attach SAU khi
    // InitializeEditorAsync hoàn tất, để pending caret không thể bị consume sớm (plan phase-1).

    private static readonly DependencyProperty ModelSubscriptionProperty =
        DependencyProperty.RegisterAttached(
            "ModelSubscription",
            typeof(object),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(null));

    /// <summary>Snapshot model.Text tại thời điểm bắt đầu InitializeEditorAsync — dùng cho quy tắc bảo vệ init-write.</summary>
    private static readonly DependencyProperty InitSnapshotProperty =
        DependencyProperty.RegisterAttached(
            "InitSnapshot",
            typeof(string),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(null));

    /// <summary>Cờ đang-init: TextChanged phát ra do chính init gán editor.Text thì không ghi ngược VM.</summary>
    private static readonly DependencyProperty IsInitializingProperty =
        DependencyProperty.RegisterAttached(
            "IsInitializing",
            typeof(bool),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(false));

    /// <summary>Số thứ tự lần init — continuation của generation cũ phải bỏ (review #7).</summary>
    private static readonly DependencyProperty InitGenerationProperty =
        DependencyProperty.RegisterAttached(
            "InitGeneration",
            typeof(int),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(0));

    /// <summary>Cờ bặt NGAY TRƯỚC khi init/VM→editor gán editor.Text, xoá ngay sau (review #5).</summary>
    private static readonly DependencyProperty IsInitWriteProperty =
        DependencyProperty.RegisterAttached(
            "IsInitWrite",
            typeof(bool),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(false));

    /// <summary>Chiều VM→editor đã attach xong chưa (sau init).</summary>
    private static readonly DependencyProperty IsModelSyncAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsModelSyncAttached",
            typeof(bool),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(false));

    /// <summary>Cờ chống vọng theo mẫu BindableTextEditor — gắn trên editor, chỉ chặn ghi ngược VM.</summary>
    private static readonly DependencyProperty SyncFlagProperty =
        DependencyProperty.RegisterAttached(
            "SyncFlag",
            typeof(bool),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(false));

    /// <summary>Cờ "đã Loaded" của vòng lifecycle Roslyn.</summary>
    private static readonly DependencyProperty LoadedProperty =
        DependencyProperty.RegisterAttached(
            "Loaded",
            typeof(bool),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(false));

    private static bool GetIsInitializing(DependencyObject target) => (bool)target.GetValue(IsInitializingProperty);
    private static void SetIsInitializing(DependencyObject target, bool value) => target.SetValue(IsInitializingProperty, value);
    private static int GetInitGeneration(DependencyObject target) => (int)target.GetValue(InitGenerationProperty);
    private static void SetInitGeneration(DependencyObject target, int value) => target.SetValue(InitGenerationProperty, value);
    private static bool GetIsInitWrite(RoslynCodeEditor editor) => (bool)editor.GetValue(IsInitWriteProperty);
    private static void SetIsInitWrite(RoslynCodeEditor editor, bool value) => editor.SetValue(IsInitWriteProperty, value);
    private static string? GetInitSnapshot(DependencyObject target) => (string?)target.GetValue(InitSnapshotProperty);
    private static void SetInitSnapshot(DependencyObject target, string? value) => target.SetValue(InitSnapshotProperty, value);
    private static bool GetIsModelSyncAttached(DependencyObject target) => (bool)target.GetValue(IsModelSyncAttachedProperty);
    private static void SetIsModelSyncAttached(DependencyObject target, bool value) => target.SetValue(IsModelSyncAttachedProperty, value);

    private static bool GetSyncing(RoslynCodeEditor editor) => (bool)editor.GetValue(SyncFlagProperty);
    private static void SetSyncing(RoslynCodeEditor editor, bool value) => editor.SetValue(SyncFlagProperty, value);

    private static void OnEnableRoslynChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RoslynCodeEditor editor) return;

        editor.Loaded -= OnLoaded;
        editor.Unloaded -= OnUnloaded;
        editor.DataContextChanged -= OnDataContextChanged;

        if ((bool)e.NewValue)
        {
            editor.Loaded += OnLoaded;
            editor.Unloaded += OnUnloaded;
            editor.DataContextChanged += OnDataContextChanged;

            if (editor.IsLoaded && editor.DataContext is CodeEditorViewModel)
            {
                AttachEditor(editor);
            }
        }
        else
        {
            DetachEditor(editor);
            editor.TextChanged -= OnTextChanged;
        }
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;
        DetachEditor(editor);
        if (editor.IsLoaded && editor.DataContext is CodeEditorViewModel)
        {
            AttachEditor(editor);
        }
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;
        if (editor.DataContext is CodeEditorViewModel)
        {
            AttachEditor(editor);
        }
    }

    private static void AttachEditor(RoslynCodeEditor editor)
    {
        if (GetLoaded(editor)) return;
        if (editor.DataContext is not CodeEditorViewModel model) return;

        SetLoaded(editor, true);

        // Chiều editor→VM attach NGAY: keystrokes + caret của user trong lúc Roslyn init
        // vẫn cập nhật VM (plan phase-1 — race #16/#19).
        editor.TextChanged -= OnTextChanged;
        editor.TextChanged += OnTextChanged;
        editor.TextArea.SetValue(EditorProperty, editor);
        editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
        editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        UpdateCaretOnModel(editor, model);

        // Nạp nội dung model.Text vào editor ngay từ đầu
        if (editor.Text != model.Text)
        {
            ApplyModelTextToEditor(editor, model);
        }

        if (StudioRoslynWorkspace.Current is null)
        {
            // Review #10: workspace vắng vẫn phải mở chiều VM→editor — chèn snippet không được
            // phụ thuộc Roslyn. Init thật sẽ chạy khi workspace có mặt qua SaveAll/OpenProject
            // (OpenProject gọi lại từ Shell), còn editor hoạt động độc lập ngay từ bây giờ.
            AttachModelSync(editor, model);
            return;
        }

        SetInitSnapshot(editor, model.Text);
        int generation = GetInitGeneration(editor) + 1;
        SetInitGeneration(editor, generation);
        _ = InitializeEditorAsync(editor, model, StudioRoslynWorkspace.Current, generation);
    }

    private static async Task InitializeEditorAsync(
        RoslynCodeEditor editor,
        CodeEditorViewModel model,
        StudioRoslynWorkspace? roslynWorkspace,
        int generation)
    {
        SetIsInitializing(editor, true);
        try
        {
            // Marker theo SỰ KIỆN consume-once (review #5/#11/#12): gọi InitializeAsync KHÔNG
            // await ngay — phần synchronous prefix gán editor.Text và bắn TextChanged ngay
            // trong lần gọi đó; cờ xoá NGAY sau lời gọi trả về. Cờ vì thế chỉ sống đúng phạm vi
            // synchronous prefix — không thể nuốt keystroke của user đến sau.
            DocumentId? initDocumentId = null;
            var host = roslynWorkspace?.Host;
            if (host is not null)
            {
                var projectDirectory = Path.GetDirectoryName(model.AbsolutePath) ?? Environment.CurrentDirectory;
                SetIsInitWrite(editor, true);
                ValueTask<DocumentId> initTask;
                try
                {
                    initTask = editor.InitializeAsync(
                        host,
                        new ClassificationHighlightColors(),
                        projectDirectory,
                        model.Text,
                        SourceCodeKind.Regular);
                }
                finally
                {
                    // Exception đồng bộ (trước await) cũng phải gỡ cờ — không để marker kẹt.
                    SetIsInitWrite(editor, false);
                }

                initDocumentId = await initTask;
            }

            // Generation cũ (tab unload/reload nhanh tạo init mới) phải bỏ — chỉ init MỚI NHẤT
            // được reconciliation/attach (review #7).
            if (generation != GetInitGeneration(editor)) return;

            if (initDocumentId is not null && roslynWorkspace is not null)
            {
                var initDocument = roslynWorkspace.Host?.GetDocument(initDocumentId);
                if (initDocument?.Project.Solution.Workspace is RoslynWorkspace initWorkspace)
                    roslynWorkspace.AttachProjectDocuments(initWorkspace, initDocumentId);
            }

            // Race #3-review: tab có thể đã bị đóng (Unloaded detach hết) trong lúc await —
            // continuation không được reconciliation/attach lại lên editor đã unload.
            if (!GetLoaded(editor) || !ReferenceEquals(editor.DataContext, model))
                return;

            // Reconciliation sau init (race #18): model.Text là giá trị MỚI NHẤT (đã chứa cả
            // edit trong lúc init nhờ chiều editor→VM sống và quy tắc init-write). Khác snapshot
            // thì áp lên editor, rồi consume pending caret, cuối cùng mới mở chiều VM→editor.
            string latest = model.Text;
            if (!string.Equals(latest, GetInitSnapshot(editor), StringComparison.Ordinal))
                ApplyModelTextToEditor(editor, model);

            ConsumePendingCaret(editor, model);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Roslyn editor initialization failed: {ex}");
        }
        finally
        {
            // Review #8: subscription VM→editor PHẢI mở sau mọi đường — kể cả init fail/host null —
            // nếu không chèn snippet sẽ không bao giờ hiện trên editor. Guard generation + loaded
            // giữ nguyên để không attach nhầm lên editor đã unload hoặc init cũ.
            bool current = generation == GetInitGeneration(editor)
                           && GetLoaded(editor)
                           && ReferenceEquals(editor.DataContext, model);
            if (current)
            {
                AttachModelSync(editor, model);
                SetIsInitializing(editor, false);
                SetInitSnapshot(editor, null);
            }
            else if (generation == GetInitGeneration(editor))
            {
                SetIsInitializing(editor, false);
                SetInitSnapshot(editor, null);
            }
        }
    }

    /// <summary>Mở chiều VM→editor: model đổi Text (chèn snippet/tag, reload) phải render lại trên editor.</summary>
    private static void AttachModelSync(RoslynCodeEditor editor, CodeEditorViewModel model)
    {
        if (GetIsModelSyncAttached(editor)) return;

        System.ComponentModel.PropertyChangedEventHandler handler = (_, args) =>
        {
            // ReloadAsync chạy continuation thread-pool (ConfigureAwait(false)) — property
            // change có thể bắn từ background; đụng editor phải marshal về UI thread.
            if (args.PropertyName != nameof(CodeEditorViewModel.Text) &&
                args.PropertyName != nameof(CodeEditorViewModel.PendingRefreshTicket))
                return;

            var dispatcher = editor.Dispatcher;
            if (dispatcher.CheckAccess())
            {
                ApplyModelTextToEditor(editor, model);
                return;
            }

            dispatcher.BeginInvoke(() =>
            {
                // Editor có thể đã unload giữa chừng — bỏ qua khi đó.
                if (GetLoaded(editor) && ReferenceEquals(editor.DataContext, model))
                    ApplyModelTextToEditor(editor, model);
            });
        };

        model.PropertyChanged += handler;
        editor.SetValue(ModelSubscriptionProperty, handler);
        SetIsModelSyncAttached(editor, true);
    }

    private static void ApplyModelTextToEditor(RoslynCodeEditor editor, CodeEditorViewModel model)
    {
        // Cờ chống vọng theo mẫu BindableTextEditor: chỉ chặn ghi NGƯỢC VM, không chặn
        // debounce UpdateDocument (TextChanged vẫn bắn timer). IsInitWrite đánh dấu chính xác
        // event do đường VM→editor gán text — OnTextChanged nhìn cờ này thay vì suy đoán.
        bool wasSyncing = GetSyncing(editor);
        SetSyncing(editor, true);
        SetIsInitWrite(editor, true);
        try
        {
            string incoming = model.Text ?? string.Empty;
            if (editor.Text != incoming)
            {
                int caret = Math.Min(editor.CaretOffset, incoming.Length);
                editor.Text = incoming;
                editor.CaretOffset = caret; // pending consume ngay sau sẽ đặt lại nếu có chèn
            }
            ConsumePendingCaret(editor, model);
        }
        finally
        {
            SetIsInitWrite(editor, false);
            SetSyncing(editor, wasSyncing);
        }
    }

    private static void ConsumePendingCaret(RoslynCodeEditor editor, CodeEditorViewModel model)
    {
        if (!model.TryTakePendingCaretOffset(out int offset)) return;

        offset = Math.Clamp(offset, 0, editor.Document?.TextLength ?? offset);
        editor.TextArea.Caret.Offset = offset;
        UpdateCaretOnModel(editor, model);
    }

    private static void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (sender is not ICSharpCode.AvalonEdit.Editing.Caret caret) return;
        var area = GetTextAreaOf(caret);
        if (area?.GetValue(EditorProperty) is not RoslynCodeEditor owner) return;
        if (GetSyncing(owner)) return; // đang áp text từ VM — không vòng
        if (owner.DataContext is not CodeEditorViewModel model) return;

        UpdateCaretOnModel(owner, model);
    }

    private static ICSharpCode.AvalonEdit.Editing.TextArea? GetTextAreaOf(ICSharpCode.AvalonEdit.Editing.Caret caret)
    {
        // Caret giữ TextArea ở property internal/public tuỳ version — tra bằng reflection một lần.
        var prop = typeof(ICSharpCode.AvalonEdit.Editing.Caret).GetProperty(
            "TextArea",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return prop?.GetValue(caret) as ICSharpCode.AvalonEdit.Editing.TextArea;
    }

    /// <summary>Editor sở hữu TextArea — gắn lúc attach để handler tĩnh tìm lại nhanh.</summary>
    private static readonly DependencyProperty EditorProperty =
        DependencyProperty.RegisterAttached(
            "Editor",
            typeof(object),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(null));

    /// <summary>Đẩy vị trí caret (1-based) từ editor về VM — nguồn sự thật cho InsertAtCaret.</summary>
    private static void UpdateCaretOnModel(RoslynCodeEditor editor, CodeEditorViewModel model)
    {
        var document = editor.Document;
        if (document is null) return;

        var location = document.GetLocation(Math.Min(editor.CaretOffset, document.TextLength));
        model.CaretLine = location.Line;
        model.CaretColumn = location.Column;
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;
        DetachEditor(editor);
    }

    private static void DetachEditor(RoslynCodeEditor editor)
    {
        editor.TextChanged -= OnTextChanged;
        editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;

        if (editor.DataContext is CodeEditorViewModel model &&
            editor.GetValue(ModelSubscriptionProperty) is System.ComponentModel.PropertyChangedEventHandler handler)
        {
            model.PropertyChanged -= handler;
            editor.SetValue(ModelSubscriptionProperty, null);
        }
        SetIsModelSyncAttached(editor, false);

        if (editor.GetValue(TimerProperty) is DispatcherTimer timer)
        {
            timer.Stop();
            editor.SetValue(TimerProperty, null);
        }

        SetLoaded(editor, false);
    }

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;
        if (editor.DataContext is not CodeEditorViewModel model) return;

        bool suppressModelWrite = false;

        // Quy tắc bảo vệ init-write (#18) theo MARKER-SỰ-KIỆN consume-once (review #5/#11):
        // cờ IsInitWrite được bặt đồng bộ ngay trước InitializeAsync; TextChanged KẾ TIẾP trên
        // UI thread là event do init gán snapshot → chặn ghi VM và CONSUME cờ. Mọi keystroke
        // sau đó thấy cờ đã hết — kể cả edit khôi phục đúng nội dung snapshot cũng không bị
        // chặn nhầm (không suy đoán từ nội dung text).
        if (GetIsInitWrite(editor))
        {
            SetIsInitWrite(editor, false); // consume-once
            suppressModelWrite = true;
        }

        // Cờ chống vọng của ApplyModelTextToEditor — chỉ chặn GHI NGƯỢC VM; debounce
        // UpdateDocument vẫn chạy cho cả thay đổi lập trình.
        if (!suppressModelWrite && !GetSyncing(editor))
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
                StudioRoslynWorkspace.Current?.UpdateDocument(model.AbsolutePath, model.Text);
            };
            editor.SetValue(TimerProperty, timer);
        }

        timer.Stop();
        timer.Start();
    }

    /// <summary>Cờ "đã Loaded" của vòng lifecycle Roslyn.</summary>
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

        // Brush tra từ theme mỗi lần render — đổi theme live đổi màu marker ngay (rẻ, không cache).
        var markerBrush = Application.Current?.TryFindResource("OverlayMarkerBrush") as System.Windows.Media.Brush;
        if (markerBrush is null) return;

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
                markerBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            drawingContext.DrawText(text, new Point(4, y - text.Height / 2));
        }
    }
}
