using System.Windows;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;
using DBI.Controller.Studio.Core.ViewModels;
using DBI.Controller.Studio.Services;

namespace DBI.Controller.Studio;

/// <summary>
/// Điểm ráp nối phụ thuộc của Studio.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Bật để chạy Studio hoàn toàn không cần Runtime — dùng khi thiết kế UI.
    /// Chạy: <c>DBI.Controller.Studio.exe --fake-runtime</c>
    /// </summary>
    private const string FakeRuntimeSwitch = "--fake-runtime";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Client tạo TRÊN UI thread: nó bắt SynchronizationContext ngay lúc khởi tạo, nhờ đó mọi
        // event tự về đúng thread và ViewModel không phải tự Dispatcher.BeginInvoke.
        IRuntimeClient runtime = e.Args.Contains(FakeRuntimeSwitch, StringComparer.OrdinalIgnoreCase)
            ? new FakeRuntimeClient()
            : new NamedPipeRuntimeClient();

        var shell = new ShellViewModel(
            new ProjectService(),
            runtime,
            new UserPrompt(),
            new ThemeService());

        var window = new MainWindow(shell);
        MainWindow = window;
        window.Show();
    }
}
