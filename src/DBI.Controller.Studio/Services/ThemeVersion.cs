using System.ComponentModel;

namespace DBI.Controller.Studio.Services;

/// <summary>
/// Nguồn "phiên bản theme" cho MultiBinding: tăng <see cref="Version"/> sau mỗi lần
/// <see cref="ThemeService.Apply"/> swap dictionary → WPF đánh giá lại binding → converter
/// tra brush mới từ Application resources. Instance binding qua <c>Source=&#123;x:Static&#125;</c>.
/// </summary>
public sealed class ThemeVersion : INotifyPropertyChanged
{
    public static ThemeVersion Instance { get; } = new();

    private int _version;

    private ThemeVersion() { }

    public int Version => _version;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gọi sau khi theme swap xong — làm mọi MultiBinding gắn Version đánh giá lại.</summary>
    public void RaiseAll()
    {
        _version++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version)));
    }
}
