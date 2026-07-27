namespace DBI.Controller.Studio.Core.Services;

/// <param name="Directory">Thư mục gốc của project mới.</param>
/// <param name="Name">Tên project — cũng là tên tệp <c>.dbiproj</c>.</param>
public record NewProjectRequest(string Directory, string Name);

/// <summary>
/// Hộp thoại và câu hỏi cho người dùng.
/// </summary>
/// <remarks>
/// Tách ra interface để <see cref="ViewModels.ShellViewModel"/> nằm được ở tầng lõi không phụ
/// thuộc WPF — nhờ vậy vòng đời project test được mà không cần dựng cửa sổ thật.
/// </remarks>
public interface IUserPrompt
{
    /// <summary>Chọn tệp <c>.dbiproj</c> để mở. <c>null</c> nghĩa là người dùng huỷ.</summary>
    string? AskProjectToOpen();

    /// <summary>Chọn nơi tạo project mới. <c>null</c> nghĩa là người dùng huỷ.</summary>
    NewProjectRequest? AskNewProjectLocation();

    /// <summary>Chọn nơi lưu bản sao project. <c>null</c> nghĩa là người dùng huỷ.</summary>
    string? AskSaveProjectAs(string suggestedName);

    bool Confirm(string message, string title);

    void ShowError(string message, string title);

    void ShowInformation(string message, string title);
}

/// <summary>Đổi theme bằng cách swap <c>MergedDictionaries</c>, không phải đổi chuỗi màu.</summary>
public interface IThemeSwitcher
{
    bool IsDark { get; }

    void Apply(bool dark);
}

/// <summary>Lưu và khôi phục bố cục cửa sổ của AvalonDock.</summary>
public interface ILayoutPersistence
{
    void Save(string layoutFilePath);

    /// <returns><c>true</c> nếu đã khôi phục được từ tệp.</returns>
    bool Restore(string layoutFilePath);

    void ResetToDefault();
}
