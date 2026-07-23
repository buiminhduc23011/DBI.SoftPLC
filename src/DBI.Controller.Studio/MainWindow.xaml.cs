using System.Windows;
using DBI.Controller.Studio.ViewModels;

namespace DBI.Controller.Studio;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;

        // Bind AvalonEdit text editor content to ViewModel.CSharpCode
        CodeEditor.Text = ViewModel.CSharpCode;
        CodeEditor.TextChanged += (s, e) =>
        {
            ViewModel.CSharpCode = CodeEditor.Text;
        };
    }

    private void BtnDeploy_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CompileAndDeploy();
    }

    private void BtnToggleOnline_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleOnline();
    }
}