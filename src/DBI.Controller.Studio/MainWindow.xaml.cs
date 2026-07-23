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

    private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ToggleTheme();

        // Update AvalonEdit Colors based on Theme
        if (ViewModel.IsDarkMode)
        {
            CodeEditor.Background = System.Windows.Media.Brushes.Transparent;
            CodeEditor.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#F1F1F1")!;
        }
        else
        {
            CodeEditor.Background = System.Windows.Media.Brushes.Transparent;
            CodeEditor.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#1A1A1A")!;
        }
    }
}