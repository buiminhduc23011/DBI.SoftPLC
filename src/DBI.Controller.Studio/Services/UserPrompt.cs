using System.IO;
using System.Windows;
using DBI.Controller.Studio.Core.Services;
using Microsoft.Win32;

namespace DBI.Controller.Studio.Services;

/// <summary>Hộp thoại thật của WPF cho <see cref="IUserPrompt"/>.</summary>
public class UserPrompt : IUserPrompt
{
    private const string ProjectFilter = "DBI Studio Project (*.dbiproj)|*.dbiproj|Tất cả tệp (*.*)|*.*";

    public string? AskProjectToOpen()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Mở project",
            Filter = ProjectFilter,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public NewProjectRequest? AskNewProjectLocation()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Tạo project mới",
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
            Title = "Lưu project thành",
            Filter = ProjectFilter,
            FileName = suggestedName + ".dbiproj"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowError(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInformation(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
