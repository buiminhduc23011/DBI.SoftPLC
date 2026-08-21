using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DBI.Controller.Studio.Core.ViewModels;

namespace DBI.Controller.Studio.Behaviors;

/// <summary>
/// Dây kéo-thả device tag (Task 12.3) mà không cần code-behind:
/// <list type="bullet">
/// <item><b>DragSource</b> — gắn lên nút trong Toolbox: bắt MouseMove khi trái giữ để bắt đầu
/// <c>DragDrop.DoDragDrop</c> với payload <see cref="DeviceTagItem"/>.</item>
/// <item><b>DropTarget</b> — gắn lên DataGrid của Tag/Watch table hoặc code editor: nhận payload,
/// gọi handler của ViewModel tương ứng theo <see cref="DocumentViewModelBase.ContentId"/>.</item>
/// </list>
/// Vùng thả hợp lệ tô viền xanh #005A9E; payload sai loại thì con trỏ ⃠ (Effects.None).
/// </summary>
public static class TagDragDropBehaviors
{
    /// <summary>Payload trao đổi qua DataObject giữa drag source và drop target.</summary>
    public static readonly string DeviceTagFormat = "DBI.DeviceTag";

    // ── Drag source ──────────────────────────────────────────────────────────────

    public static readonly DependencyProperty DragSourceProperty =
        DependencyProperty.RegisterAttached(
            "DragSource",
            typeof(bool),
            typeof(TagDragDropBehaviors),
            new PropertyMetadata(false, OnDragSourceChanged));

    public static bool GetDragSource(DependencyObject target) => (bool)target.GetValue(DragSourceProperty);
    public static void SetDragSource(DependencyObject target, bool value) => target.SetValue(DragSourceProperty, value);

    private static void OnDragSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button) return;

        if ((bool)e.NewValue)
        {
            button.PreviewMouseLeftButtonDown -= OnDragSourceMouseDown;
            button.PreviewMouseLeftButtonDown += OnDragSourceMouseDown;
        }
        else
        {
            button.PreviewMouseLeftButtonDown -= OnDragSourceMouseDown;
        }
    }

    private static void OnDragSourceMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DeviceTagItem item } button) return;

        // Double-click vẫn phải tới được nút (chèn IO.tag) — chỉ bắt đầu kéo khi chuột rời
        // khỏi điểm nhấn quá ngưỡng, không khởi động drag ngay trên click.
        var origin = e.GetPosition(button);
        void OnMove(object s, MouseEventArgs args)
        {
            if ((args.GetPosition(button) - origin).Length < 4) return;

            button.MouseMove -= OnMove;
            var data = new DataObject(DeviceTagFormat, item);
            DragDrop.DoDragDrop(button, data, DragDropEffects.Copy);
        }

        button.MouseMove += OnMove;
        void Unsubscribe(object s, MouseEventArgs args) => button.MouseMove -= OnMove;
        button.MouseLeave += Unsubscribe;
        button.MouseUp += Unsubscribe;
    }

    // ── Drop target ──────────────────────────────────────────────────────────────

    public static readonly DependencyProperty DropTargetProperty =
        DependencyProperty.RegisterAttached(
            "DropTarget",
            typeof(bool),
            typeof(TagDragDropBehaviors),
            new PropertyMetadata(false, OnDropTargetChanged));

    public static bool GetDropTarget(DependencyObject target) => (bool)target.GetValue(DropTargetProperty);
    public static void SetDropTarget(DependencyObject target, bool value) => target.SetValue(DropTargetProperty, value);

    private static void OnDropTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        if ((bool)e.NewValue)
        {
            element.AllowDrop = true;
            element.DragEnter += OnDragOver;
            element.DragOver += OnDragOver;
            element.DragLeave += OnDragLeave;
            element.Drop += OnDrop;
        }
        else
        {
            element.AllowDrop = false;
            element.DragEnter -= OnDragOver;
            element.DragOver -= OnDragOver;
            element.DragLeave -= OnDragLeave;
            element.Drop -= OnDrop;
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DeviceTagFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = ResolveHandler(sender) is null ? DragDropEffects.None : DragDropEffects.Copy;
        SetDropHighlight(sender, true);
        e.Handled = true;
    }

    private static void OnDragLeave(object sender, DragEventArgs e) => SetDropHighlight(sender, false);

    private static void OnDrop(object sender, DragEventArgs e)
    {
        SetDropHighlight(sender, false);
        if (e.Data.GetData(DeviceTagFormat) is not DeviceTagItem item) return;

        string? rejection = ResolveHandler(sender)?.Invoke(item);
        if (rejection is not null)
            MessageBox.Show(rejection, "Không thả được", MessageBoxButton.OK, MessageBoxImage.Warning);

        e.Handled = true;
    }

    /// <summary>
    /// Chọn hành động thả theo ViewModel của document chứa phần tử: TagTable → map vào dòng,
    /// WatchTable → thêm dòng watch, CodeEditor → chèn IO.TenTag. Trả về null nếu không phải đích hợp lệ.
    /// </summary>
    private static Func<DeviceTagItem, string?>? ResolveHandler(object sender)
    {
        var element = sender as FrameworkElement;
        while (element is not null)
        {
            switch (element.DataContext)
            {
                case TagTableViewModel tagTable:
                    return tagTable.MapFromDevice;
                case WatchTableViewModel watch:
                    return watch.AddDeviceTag;
                case CodeEditorViewModel editor:
                    return item =>
                    {
                        editor.InsertAtCaret($"IO.{item.Name}");
                        return null;
                    };
            }
            element = GetVisualParent(element);
        }
        return null;
    }

    private static FrameworkElement? GetVisualParent(FrameworkElement element) =>
        VisualTreeHelper.GetParent(element) as FrameworkElement;

    private static void SetDropHighlight(object sender, bool on)
    {
        if (sender is not Border border) return;
        border.BorderBrush = on ? new SolidColorBrush(Color.FromRgb(0x00, 0x5A, 0x9E)) : Brushes.Transparent;
        border.BorderThickness = on ? new Thickness(2) : border.BorderThickness;
    }
}
