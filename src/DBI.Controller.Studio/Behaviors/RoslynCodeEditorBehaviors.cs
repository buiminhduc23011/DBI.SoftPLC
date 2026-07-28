using System.IO;
using System.Windows;
using System.Windows.Threading;
using DBI.Controller.Studio.Core.Services.CodeAnalysis;
using DBI.Controller.Studio.Core.ViewModels;
using Microsoft.CodeAnalysis;
using RoslynPad.Editor;
using RoslynPad.Roslyn;

namespace DBI.Controller.Studio.Behaviors;

public static class RoslynCodeEditorBehaviors
{
    public static readonly DependencyProperty EnableRoslynProperty =
        DependencyProperty.RegisterAttached(
            "EnableRoslyn",
            typeof(bool),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(false, OnEnableRoslynChanged));

    public static bool GetEnableRoslyn(DependencyObject target) =>
        (bool)target.GetValue(EnableRoslynProperty);

    public static void SetEnableRoslyn(DependencyObject target, bool value) =>
        target.SetValue(EnableRoslynProperty, value);

    private static readonly DependencyProperty TimerProperty =
        DependencyProperty.RegisterAttached(
            "Timer",
            typeof(DispatcherTimer),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(null));

    private static readonly DependencyProperty LoadedProperty =
        DependencyProperty.RegisterAttached(
            "Loaded",
            typeof(bool),
            typeof(RoslynCodeEditorBehaviors),
            new PropertyMetadata(false));

    private static void OnEnableRoslynChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RoslynCodeEditor editor) return;

        if ((bool)e.NewValue)
        {
            editor.Loaded -= OnLoaded;
            editor.Loaded += OnLoaded;
            editor.Unloaded -= OnUnloaded;
            editor.Unloaded += OnUnloaded;
        }
        else
        {
            editor.Loaded -= OnLoaded;
            editor.Unloaded -= OnUnloaded;
            editor.TextChanged -= OnTextChanged;
        }
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;
        if (GetLoaded(editor)) return;

        SetLoaded(editor, true);

        if (editor.DataContext is not CodeEditorViewModel model) return;
        if (StudioRoslynWorkspace.Current is null) return;

        editor.TextChanged -= OnTextChanged;
        editor.TextChanged += OnTextChanged;

        _ = InitializeEditorAsync(editor, model, StudioRoslynWorkspace.Current);
    }

    private static async Task InitializeEditorAsync(
        RoslynCodeEditor editor,
        CodeEditorViewModel model,
        StudioRoslynWorkspace roslynWorkspace)
    {
        try
        {
            var host = roslynWorkspace.Host;
            if (host is null) return;

        var projectDirectory = Path.GetDirectoryName(model.AbsolutePath) ?? Environment.CurrentDirectory;
        var documentId = await editor.InitializeAsync(
            host,
            new ClassificationHighlightColors(),
            projectDirectory,
            model.Text,
            SourceCodeKind.Regular);

        var document = host.GetDocument(documentId);
        if (document?.Project.Solution.Workspace is RoslynWorkspace workspace)
            roslynWorkspace.AttachProjectDocuments(workspace, documentId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Roslyn editor initialization failed: {ex}");
        }
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;
        editor.TextChanged -= OnTextChanged;
        SetLoaded(editor, false);
    }

    private static void OnTextChanged(object? sender, EventArgs e)
    {
        if (sender is not RoslynCodeEditor editor) return;
        if (editor.DataContext is not CodeEditorViewModel model) return;
        if (StudioRoslynWorkspace.Current is null) return;

        model.Text = editor.Text;

        var timer = (DispatcherTimer?)editor.GetValue(TimerProperty);
        if (timer is null)
        {
            timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                StudioRoslynWorkspace.Current.UpdateDocument(model.AbsolutePath, model.Text);
            };
            editor.SetValue(TimerProperty, timer);
        }

        timer.Stop();
        timer.Start();
    }

    private static bool GetLoaded(DependencyObject target) => (bool)target.GetValue(LoadedProperty);
    private static void SetLoaded(DependencyObject target, bool value) => target.SetValue(LoadedProperty, value);
}
