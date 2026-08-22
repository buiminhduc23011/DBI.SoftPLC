using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DBI.Controller.Studio.Behaviors;

/// <summary>
/// Chặn Click thứ hai của double-click: Button vẫn bắn command mỗi lần click, nên
/// double-click trên Toolbox item = chèn HAI lần. Gắn attached property này thì cú nhấp
/// thứ hai trong cửa sổ double-click (<c>e.ClickCount == 2</c>) bị đánh dấu Handled ở
/// Preview — command chỉ chạy đúng một lần. Drag không bị ảnh hưởng (TagDragDropBehaviors
/// đã có ngưỡng 4px và bắt đầu từ PreviewMouseLeftButtonDown riêng).
/// </summary>
public static class SingleClickCommandBehavior
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(SingleClickCommandBehavior),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button) return;

        if ((bool)e.NewValue)
        {
            button.PreviewMouseLeftButtonDown -= OnPreviewMouseDown;
            button.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
        }
        else
        {
            button.PreviewMouseLeftButtonDown -= OnPreviewMouseDown;
        }
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // ClickCount == 2 là cú nhấp thứ hai của double-click → nuốt trước khi Button kịp bắn Click.
        if (e.ClickCount == 2) e.Handled = true;
    }
}
