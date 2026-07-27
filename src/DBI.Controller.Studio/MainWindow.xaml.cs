using System.ComponentModel;
using System.Windows;
using DBI.Controller.Studio.Core.ViewModels;
using DBI.Controller.Studio.Services;

namespace DBI.Controller.Studio;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;

    public MainWindow(ShellViewModel shell)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));

        InitializeComponent();

        DataContext = _shell;

        // DockingManager chỉ tồn tại sau InitializeComponent, nên dịch vụ bố cục phải ráp ở đây.
        // Nó vẫn đi qua interface ILayoutPersistence — ShellViewModel không biết AvalonDock là gì.
        _shell.AttachLayout(new DockLayoutService(Docking, _shell.ResolveContent));
    }

    /// <summary>
    /// Cảnh báo trước khi đóng nếu máy đang chạy — đóng Studio <b>không</b> dừng máy (ADR-001).
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        _shell.SaveLayout();

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
