using System.Windows;
using ICSharpCode.AvalonEdit;

namespace DBI.Controller.Studio.Behaviors;

/// <summary>
/// Cho phép bind hai chiều nội dung của <see cref="TextEditor"/>.
/// </summary>
/// <remarks>
/// <c>TextEditor.Text</c> không phải DependencyProperty nên không bind thẳng được — nếu không có
/// lớp này thì buộc phải nối bằng code-behind, mà DoD phase-04 yêu cầu
/// <c>MainWindow.xaml.cs</c> chỉ còn <c>InitializeComponent()</c>.
/// </remarks>
public static class BindableTextEditor
{
    private static readonly DependencyProperty IsSyncingProperty =
        DependencyProperty.RegisterAttached(
            "IsSyncing", typeof(bool), typeof(BindableTextEditor), new PropertyMetadata(false));

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(BindableTextEditor),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTextChanged));

    public static string GetText(DependencyObject target) => (string)target.GetValue(TextProperty);

    public static void SetText(DependencyObject target, string value) => target.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextEditor editor) return;

        Attach(editor);

        // Cờ chống vọng: gán editor.Text sẽ bắn TextChanged, mà handler lại gán ngược property này.
        if ((bool)editor.GetValue(IsSyncingProperty)) return;

        string incoming = e.NewValue as string ?? string.Empty;
        if (editor.Text == incoming) return;

        editor.SetValue(IsSyncingProperty, true);
        try
        {
            // Giữ vị trí con trỏ: gán Text làm nó nhảy về đầu, người đang gõ sẽ mất chỗ.
            int caret = Math.Min(editor.CaretOffset, incoming.Length);
            editor.Text = incoming;
            editor.CaretOffset = caret;
        }
        finally
        {
            editor.SetValue(IsSyncingProperty, false);
        }
    }

    private static void Attach(TextEditor editor)
    {
        editor.TextChanged -= OnEditorTextChanged;
        editor.TextChanged += OnEditorTextChanged;
    }

    private static void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (sender is not TextEditor editor) return;
        if ((bool)editor.GetValue(IsSyncingProperty)) return;

        editor.SetValue(IsSyncingProperty, true);
        try
        {
            SetText(editor, editor.Text);
        }
        finally
        {
            editor.SetValue(IsSyncingProperty, false);
        }
    }
}
