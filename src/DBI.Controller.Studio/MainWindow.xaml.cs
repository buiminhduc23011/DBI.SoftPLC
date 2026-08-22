using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DBI.Controller.Studio.Core.ViewModels;
using DBI.Controller.Studio.Services;

namespace DBI.Controller.Studio;

public partial class MainWindow : Window
{
    /// <summary>Ctrl+T — hiện pane Toolbox (nếu đang ẩn) rồi focus ô lọc. View-layer command: không qua VM.</summary>
    public static readonly RoutedCommand FocusToolboxFilter = new(nameof(FocusToolboxFilter), typeof(MainWindow));

    private readonly ShellViewModel _shell;

    public MainWindow(ShellViewModel shell)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));

        InitializeComponent();

        DataContext = _shell;
        ProjectPane.Content = _shell.ProjectTree;
        InspectorPane.Content = _shell.Inspector;
        ToolboxPane.Content = _shell.TaskCards;

        // DockingManager chỉ tồn tại sau InitializeComponent, nên dịch vụ bố cục phải ráp ở đây.
        // Nó vẫn đi qua interface ILayoutPersistence — ShellViewModel không biết AvalonDock là gì.
        _shell.AttachLayout(new DockLayoutService(Docking, _shell.ResolveContent));
        Docking.Loaded += (_, _) => NormalizeDockChrome();
    }

    /// <summary>
    /// Ctrl+T: <c>ToolboxPane.Show()</c> khi hidden/auto-hidden (KHÔNG đụng
    /// <c>Docking.ActiveContent</c> — nó two-way bind Editors.ActiveDocument), tìm ô lọc trong
    /// visual tree từ Docking, defer focus nếu template chưa render xong.
    /// </summary>
    private void OnFocusToolboxFilter(object sender, ExecutedRoutedEventArgs e)
    {
        if (ToolboxPane.IsHidden || ToolboxPane.IsAutoHidden) ToolboxPane.Show();

        FocusToolboxFilterBox();
    }

    private void FocusToolboxFilterBox()
    {
        var filterBox = Descendants(Docking).OfType<TextBox>().FirstOrDefault(t => t.Name == "ToolboxFilterBox");
        if (filterBox is null)
        {
            // Template của pane chưa render xong (vừa Show) — thử lại ở priority Loaded.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(FocusToolboxFilterBox));
            return;
        }

        filterBox.Focus();
        filterBox.SelectAll();
    }

    private void OnFocusToolboxFilterCanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = true;

    private void NormalizeDockChrome()
    {
        foreach (var element in Descendants(Docking).OfType<FrameworkElement>())
        {
            var typeName = element.GetType().Name;
            if (typeName.Contains("PaneControl", StringComparison.Ordinal) ||
                typeName is "LayoutAnchorableControl" or "LayoutDocumentControl")
            {
                element.Margin = new Thickness(0);
                if (element is Control control) control.Padding = new Thickness(0);
            }
            else if (element is GridSplitter splitter)
            {
                splitter.Margin = new Thickness(0);
                splitter.Width = 1;
                splitter.Height = 1;
            }
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    /// <summary>
    /// Cảnh báo trước khi đóng nếu máy đang chạy — đóng Studio <b>không</b> dừng máy (ADR-001).
    /// Còn force thì hỏi 3 lựa chọn (Task 11.4) — force tiếp tục hiệu lực ở Runtime.
    /// </summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        _shell.SaveLayout();

        if (!_shell.ConfirmCloseWithForcesAsync().GetAwaiter().GetResult())
        {
            e.Cancel = true;
            return;
        }

        if (_shell.ClosingWarning is { } warning &&
            MessageBox.Show(
                warning + "\n\nVẫn đóng Studio?",
                "Runtime đang chạy",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }
}
