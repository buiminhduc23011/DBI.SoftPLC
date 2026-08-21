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

    public bool ConfirmForceSafety(string tagName, string dataTypeDescription, string valueText)
    {
        var dialog = new ForceSafetyWindow(tagName, dataTypeDescription, valueText)
            { Owner = Application.Current?.MainWindow };
        return dialog.ShowDialog() == true;
    }

    public CloseWithForcesChoice? AskCloseWithForces(int forceCount)
    {
        var result = MessageBox.Show(
            $"{forceCount} tag vẫn đang bị force và sẽ TIẾP TỤC bị force sau khi đóng Studio.\n\n" +
            "Xoá force trước khi đóng?",
            "Còn tag đang bị force",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => CloseWithForcesChoice.ClearForcesAndClose,
            MessageBoxResult.No => CloseWithForcesChoice.KeepForcesAndClose,
            _ => CloseWithForcesChoice.Cancel
        };
    }

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

    internal static UIElement CreateButtons2(Button primary, Button secondary)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        panel.Children.Add(primary);
        panel.Children.Add(secondary);
        return panel;
    }
}

/// <summary>
/// Cảnh báo an toàn khi tạo force (phase-11 Task 11.5). Nút Force chỉ bật khi đã tick
/// checkbox xác nhận — KHÔNG có tuỳ chọn "đừng hỏi lại". An toàn thắng tiện lợi.
/// </summary>
internal sealed class ForceSafetyWindow : Window
{
    private readonly CheckBox _acknowledge;

    public ForceSafetyWindow(string tagName, string dataTypeDescription, string valueText)
    {
        Title = "⚠️ CẢNH BÁO AN TOÀN";
        Width = 480;
        Height = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var forceButton = new Button { Content = "Force", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };

        _acknowledge = new CheckBox
        {
            Content = "Tôi xác nhận khu vực máy đã an toàn",
            Margin = new Thickness(0, 12, 0, 0)
        };
        _acknowledge.Checked += (_, _) => forceButton.IsEnabled = true;
        _acknowledge.Unchecked += (_, _) => forceButton.IsEnabled = false;

        forceButton.Click += (_, _) => DialogResult = true;

        var cancel = new Button { Content = "Huỷ", Width = 80, IsCancel = true };

        var warning = new TextBlock
        {
            Text = $"Force \"{tagName}\" = {valueText} ({dataTypeDescription}) sẽ ghi đè logic chương trình " +
                   "và điều khiển TRỰC TIẾP thiết bị vật lý.\n\nĐảm bảo khu vực máy an toàn trước khi tiếp tục.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8)
        };

        var header = new TextBlock
        {
            Text = "⚠️ CẢNH BÁO AN TOÀN",
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Foreground = System.Windows.Media.Brushes.Firebrick
        };

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Children = { header, warning, _acknowledge, TextPromptWindow.CreateButtons2(forceButton, cancel) }
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
