using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DBI.Controller.Studio.Core.ViewModels;

namespace DBI.Controller.Studio.Behaviors;

/// <summary>
/// Nối <see cref="TreeView"/> vào ViewModel mà không cần code-behind.
/// </summary>
/// <remarks>
/// <c>TreeView.SelectedItem</c> chỉ đọc, và mở node bằng double-click vốn là sự kiện — cả hai đều
/// buộc phải viết code-behind nếu không có lớp này.
/// </remarks>
public static class ProjectTreeBehaviors
{
    // ── Chọn node ────────────────────────────────────────────────────────────────

    public static readonly DependencyProperty SelectedNodeProperty =
        DependencyProperty.RegisterAttached(
            "SelectedNode",
            typeof(ProjectNode),
            typeof(ProjectTreeBehaviors),
            new FrameworkPropertyMetadata(
                null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedNodeAttached));

    public static ProjectNode? GetSelectedNode(DependencyObject target) =>
        (ProjectNode?)target.GetValue(SelectedNodeProperty);

    public static void SetSelectedNode(DependencyObject target, ProjectNode? value) =>
        target.SetValue(SelectedNodeProperty, value);

    private static void OnSelectedNodeAttached(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView tree) return;

        tree.SelectedItemChanged -= OnSelectedItemChanged;
        tree.SelectedItemChanged += OnSelectedItemChanged;
    }

    private static void OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (sender is TreeView tree && e.NewValue is ProjectNode node)
            SetSelectedNode(tree, node);
    }

    // ── Mở node bằng double-click ────────────────────────────────────────────────

    public static readonly DependencyProperty ActivateTargetProperty =
        DependencyProperty.RegisterAttached(
            "ActivateTarget",
            typeof(ProjectTreeViewModel),
            typeof(ProjectTreeBehaviors),
            new PropertyMetadata(null, OnActivateTargetAttached));

    public static ProjectTreeViewModel? GetActivateTarget(DependencyObject target) =>
        (ProjectTreeViewModel?)target.GetValue(ActivateTargetProperty);

    public static void SetActivateTarget(DependencyObject target, ProjectTreeViewModel? value) =>
        target.SetValue(ActivateTargetProperty, value);

    private static void OnActivateTargetAttached(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeView tree) return;

        tree.MouseDoubleClick -= OnMouseDoubleClick;
        tree.MouseDoubleClick += OnMouseDoubleClick;
    }

    private static void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeView tree) return;
        if (GetActivateTarget(tree) is not { } viewModel) return;
        if (tree.SelectedItem is not ProjectNode node) return;

        viewModel.RaiseNodeActivated(node);
        e.Handled = true;
    }
}
