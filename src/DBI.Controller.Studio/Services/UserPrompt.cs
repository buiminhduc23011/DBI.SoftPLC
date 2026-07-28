using System.IO;
using System.Windows;
using System.Windows.Controls;
using DBI.Controller.Studio.Core.Services;
using Microsoft.Win32;
using DBI.Controller.Studio.Core.Models;

namespace DBI.Controller.Studio.Services;

/// <summary>Hộp thoại thật của WPF cho <see cref="IUserPrompt"/>.</summary>
public class UserPrompt : IUserPrompt
{
    private const string ProjectFilter = "DBI Studio Project (*.dbiproj)|*.dbiproj|All files (*.*)|*.*";

    public string? AskProjectToOpen()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open project",
            Filter = ProjectFilter,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public NewProjectRequest? AskNewProjectLocation()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Create new project",
            Filter = ProjectFilter,
            FileName = "MyMachine.dbiproj",
            OverwritePrompt = false
        };

        if (dialog.ShowDialog() != true) return null;

        string directory = Path.GetDirectoryName(dialog.FileName) ?? "";
        string name = Path.GetFileNameWithoutExtension(dialog.FileName);

        return new NewProjectRequest(directory, name);
    }

    public string? AskSaveProjectAs(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save project as",
            Filter = ProjectFilter,
            FileName = suggestedName + ".dbiproj"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public NewBlockRequest? AskNewBlock(bool canCreateMain)
    {
        var dialog = new BlockPromptWindow(canCreateMain) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public string? AskText(string title, string prompt, string initialValue)
    {
        var dialog = new TextPromptWindow(title, prompt, initialValue) { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInformation(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}

internal sealed class TextPromptWindow : Window
{
    private readonly TextBox _textBox;

    public TextPromptWindow(string title, string prompt, string initialValue)
    {
        Title = title;
        Width = 420;
        Height = 170;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _textBox = new TextBox { Margin = new Thickness(0, 8, 0, 12), Text = initialValue };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = prompt },
                _textBox,
                CreateButtons(OnOk)
            }
        };
    }

    public string? Result { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = _textBox.Text.Trim();
        DialogResult = true;
    }

    internal static UIElement CreateButtons(RoutedEventHandler okHandler)
    {
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += okHandler;

        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { ok, cancel }
        };
    }
}

internal sealed class BlockPromptWindow : Window
{
    private readonly TextBox _nameBox = new() { Margin = new Thickness(0, 8, 0, 12), Text = "Conveyor" };
    private readonly TextBox _commentBox = new() { Margin = new Thickness(0, 8, 0, 12) };
    private readonly Dictionary<BlockKind, RadioButton> _choices;

    public BlockPromptWindow(bool canCreateMain)
    {
        Title = "Add new block";
        Width = 460;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _choices = new Dictionary<BlockKind, RadioButton>
        {
            [BlockKind.Main] = new RadioButton
            {
                Content = "Main — cycle entry point",
                IsEnabled = canCreateMain,
                Margin = new Thickness(0, 2, 0, 2)
            },
            [BlockKind.FunctionBlock] = new RadioButton
            {
                Content = "Function Block — stateful",
                IsChecked = true,
                Margin = new Thickness(0, 2, 0, 2)
            },
            [BlockKind.Function] = new RadioButton
            {
                Content = "Function — stateless",
                Margin = new Thickness(0, 2, 0, 2)
            },
            [BlockKind.DataBlock] = new RadioButton
            {
                Content = "Data Block — data only",
                Margin = new Thickness(0, 2, 0, 2)
            }
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Name:" });
        panel.Children.Add(_nameBox);
        panel.Children.Add(new TextBlock { Text = "Block type:" });
        foreach (var choice in _choices.Values) panel.Children.Add(choice);
        panel.Children.Add(new TextBlock { Text = "Comment:" });
        panel.Children.Add(_commentBox);
        panel.Children.Add(TextPromptWindow.CreateButtons(OnOk));

        Content = panel;
    }

    public NewBlockRequest? Result { get; private set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var selected = _choices.First(kv => kv.Value.IsChecked == true).Key;
        Result = new NewBlockRequest(_nameBox.Text.Trim(), selected, _commentBox.Text.Trim());
        DialogResult = true;
    }
}
