using CommunityToolkit.Mvvm.ComponentModel;

namespace DBI.Controller.Studio.Core.ViewModels;

/// <summary>
/// Lớp cơ sở cho mọi panel neo hai bên (Project Tree, Inspector, Task Cards).
/// </summary>
/// <remarks>
/// ⚠️ <see cref="ContentId"/> phải <b>ổn định giữa các phiên</b>. AvalonDock lưu vào
/// <c>layout.xml</c> đúng chuỗi này và khi khôi phục sẽ hỏi ngược lại
/// "ContentId này là ViewModel nào?". Đổi chuỗi là layout cũ khôi phục ra panel rỗng.
/// </remarks>
public abstract partial class PaneViewModelBase : ObservableObject
{
    protected PaneViewModelBase(string contentId, string title)
    {
        ContentId = contentId;
        Title = title;
    }

    public string ContentId { get; }

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>Panel neo không đóng được — chỉ ẩn. Đóng hẳn thì người dùng không biết mở lại kiểu gì.</summary>
    public virtual bool CanClose => false;
}

/// <summary>
/// Lớp cơ sở cho tài liệu mở ở vùng giữa (mã nguồn, bảng tag, bảng watch).
/// </summary>
public abstract partial class DocumentViewModelBase : ObservableObject
{
    private string _baseTitle;

    protected DocumentViewModelBase(string contentId, string title)
    {
        ContentId = contentId;
        _baseTitle = title;
        _title = title;
    }

    public string ContentId { get; }

    /// <summary>
    /// Nhãn hiện trên tab, đã kèm dấu <c>*</c> khi chưa lưu.
    /// </summary>
    /// <remarks>
    /// Gộp dấu <c>*</c> vào đây thay vì để riêng một property: AvalonDock dùng <b>một</b> style cho
    /// cả tài liệu lẫn panel neo, mà panel neo là kiểu của AvalonDock nên chỉ có sẵn <c>Title</c>.
    /// </remarks>
    [ObservableProperty]
    private string _title;

    /// <summary>Tên gốc, không có dấu <c>*</c>.</summary>
    public string BaseTitle
    {
        get => _baseTitle;
        set
        {
            if (_baseTitle == value) return;

            _baseTitle = value;
            OnPropertyChanged();
            RefreshTitle();
        }
    }

    [ObservableProperty]
    private bool _isActive;

    /// <summary>Có thay đổi chưa lưu.</summary>
    [ObservableProperty]
    private bool _isDirty;

    public virtual bool CanClose => true;

    partial void OnIsDirtyChanged(bool value) => RefreshTitle();

    private void RefreshTitle() => Title = IsDirty ? _baseTitle + " *" : _baseTitle;

    /// <summary>Ghi nội dung xuống đĩa. Mặc định không làm gì.</summary>
    public virtual Task SaveAsync() => Task.CompletedTask;
}
