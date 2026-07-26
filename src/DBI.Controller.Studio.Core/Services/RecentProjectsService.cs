using System.Text.Json;

namespace DBI.Controller.Studio.Core.Services;

/// <summary>
/// Danh sách project mở gần đây, lưu ở <c>%APPDATA%/DBI.Studio/recent.json</c>.
/// </summary>
public class RecentProjectsService
{
    public const int MaxEntries = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _storePath;
    private List<string>? _cache;

    /// <param name="settingsDirectory">
    /// Thư mục lưu cấu hình. Để <c>null</c> dùng <c>%APPDATA%/DBI.Studio</c>.
    /// Test truyền thư mục tạm để không đụng cấu hình thật của người dùng.
    /// </param>
    public RecentProjectsService(string? settingsDirectory = null)
    {
        settingsDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DBI.Studio");

        _storePath = Path.Combine(settingsDirectory, "recent.json");
    }

    /// <summary>Mới nhất đứng đầu. Mục trỏ tới tệp không còn tồn tại được lọc bỏ.</summary>
    public IReadOnlyList<string> Items
    {
        get
        {
            _cache ??= Load();
            return _cache.Where(File.Exists).ToList();
        }
    }

    public void Add(string projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath)) return;

        string full = Path.GetFullPath(projectFilePath);

        _cache ??= Load();
        _cache.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _cache.Insert(0, full);

        if (_cache.Count > MaxEntries)
            _cache.RemoveRange(MaxEntries, _cache.Count - MaxEntries);

        Persist();
    }

    public void Clear()
    {
        _cache = new List<string>();
        Persist();
    }

    private List<string> Load()
    {
        if (!File.Exists(_storePath)) return new List<string>();

        try
        {
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_storePath)) ?? new List<string>();
        }
        catch (JsonException)
        {
            // recent.json hỏng là chuyện vặt — bắt đầu lại từ danh sách rỗng, không chặn mở Studio.
            return new List<string>();
        }
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        File.WriteAllText(_storePath, JsonSerializer.Serialize(_cache, JsonOptions));
    }
}
