using System.Text.Json;
using DBI.Controller.Studio.Core.Models;

namespace DBI.Controller.Studio.Core.Services;

/// <summary>Kết quả mở/tạo project. <see cref="Project"/> là <c>null</c> khi thất bại.</summary>
public record ProjectLoadResult(DbiProject? Project, IReadOnlyList<ValidationIssue> Issues)
{
    public bool Success => Project is not null;

    public static ProjectLoadResult Failed(string message) =>
        new(null, new[] { new ValidationIssue(IssueSeverity.Error, message) });
}

/// <summary>
/// Tạo / mở / lưu project trên đĩa.
/// </summary>
/// <remarks>
/// Bố cục Git-friendly (Constraint C-7):
/// <code>
/// MyMachine/
/// ├── MyMachine.dbiproj     ← JSON có indent
/// ├── Blocks/*.cs           ← file .cs thật
/// ├── Generated/IO.g.cs     ← phase-06 sinh
/// └── .dbistudio/           ← layout AvalonDock, không commit
/// </code>
/// </remarks>
public class ProjectService
{
    public const string ProjectFileExtension = ".dbiproj";
    public const string BlocksFolder = "Blocks";
    public const string GeneratedFolder = "Generated";
    public const string StudioFolder = ".dbistudio";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // Không escape ký tự Unicode — comment tiếng Việt phải đọc được trong Git diff.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly RecentProjectsService _recent;

    public ProjectService(RecentProjectsService? recentProjects = null)
    {
        _recent = recentProjects ?? new RecentProjectsService();
    }

    public DbiProject? Current { get; private set; }

    public RecentProjectsService RecentProjects => _recent;

    /// <summary>Phát khi project đang mở thay đổi (mở, tạo, đóng).</summary>
    public event EventHandler<DbiProject?>? CurrentChanged;

    // ── New ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tạo cây thư mục project mới kèm <c>Blocks/Main.cs</c> và <c>.gitignore</c>.
    /// </summary>
    /// <param name="directory">Thư mục gốc của project. Tạo mới nếu chưa có.</param>
    /// <param name="name">Tên project — cũng là tên tệp <c>.dbiproj</c>.</param>
    public ProjectLoadResult CreateNew(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return ProjectLoadResult.Failed("Tên project không được để trống.");

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return ProjectLoadResult.Failed($"Tên project '{name}' chứa ký tự không dùng được cho tên tệp.");

        string projectPath = Path.Combine(directory, name + ProjectFileExtension);

        if (File.Exists(projectPath))
            return ProjectLoadResult.Failed($"Đã có project tại '{projectPath}'.");

        Directory.CreateDirectory(Path.Combine(directory, BlocksFolder));
        Directory.CreateDirectory(Path.Combine(directory, GeneratedFolder));

        var project = new DbiProject
        {
            Name = name,
            ProjectFilePath = projectPath,
            Blocks =
            {
                new CodeBlock
                {
                    Name = "Main",
                    Kind = BlockKind.Main,
                    FileName = $"{BlocksFolder}/Main.cs",
                    Comment = "Khối chính — Runtime gọi mỗi chu kỳ quét"
                }
            },
            TagTables = { new TagTable() }
        };

        File.WriteAllText(Path.Combine(directory, BlocksFolder, "Main.cs"), BlockTemplates.Main());

        string gitIgnorePath = Path.Combine(directory, ".gitignore");
        if (!File.Exists(gitIgnorePath))
            File.WriteAllText(gitIgnorePath, BlockTemplates.GitIgnore);

        WriteProjectFile(project);

        SetCurrent(project);
        _recent.Add(projectPath);

        return new ProjectLoadResult(project, Array.Empty<ValidationIssue>());
    }

    // ── Open ─────────────────────────────────────────────────────────────────────

    public ProjectLoadResult Open(string projectFilePath)
    {
        if (!File.Exists(projectFilePath))
            return ProjectLoadResult.Failed($"Không tìm thấy tệp project: '{projectFilePath}'.");

        DbiProject? project;
        try
        {
            project = JsonSerializer.Deserialize<DbiProject>(File.ReadAllText(projectFilePath), JsonOptions);
        }
        catch (JsonException ex)
        {
            return ProjectLoadResult.Failed($"Tệp project hỏng, không đọc được JSON: {ex.Message}");
        }

        if (project is null)
            return ProjectLoadResult.Failed("Tệp project rỗng.");

        var schemaIssue = CheckSchemaVersion(project.SchemaVersion);
        if (schemaIssue is not null)
            return new ProjectLoadResult(null, new[] { schemaIssue });

        project.ProjectFilePath = Path.GetFullPath(projectFilePath);
        project.IsDirty = false;

        var issues = new List<ValidationIssue>();
        issues.AddRange(CheckBlockFiles(project));
        issues.AddRange(ProjectValidator.Validate(project));

        SetCurrent(project);
        _recent.Add(project.ProjectFilePath);

        return new ProjectLoadResult(project, issues);
    }

    /// <summary>
    /// Từ chối mở project có schema major cao hơn Studio — mở ra sẽ mất dữ liệu của các trường
    /// Studio chưa biết. Major thấp hơn thì đọc được (tương thích ngược).
    /// </summary>
    private static ValidationIssue? CheckSchemaVersion(string? schemaVersion)
    {
        if (!TryParseMajor(schemaVersion, out int fileMajor))
        {
            return new ValidationIssue(IssueSeverity.Error,
                $"Không đọc được schemaVersion '{schemaVersion}'. Tệp có thể đã hỏng.");
        }

        TryParseMajor(DbiProject.CurrentSchemaVersion, out int studioMajor);

        if (fileMajor > studioMajor)
        {
            return new ValidationIssue(IssueSeverity.Error,
                $"Project dùng schema {schemaVersion}, Studio này chỉ hỗ trợ tới " +
                $"{DbiProject.CurrentSchemaVersion}. Hãy cập nhật DBI.Studio rồi mở lại.");
        }

        return null;
    }

    private static bool TryParseMajor(string? version, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(version)) return false;

        string head = version.Split('.')[0];
        return int.TryParse(head, out major);
    }

    /// <summary>
    /// Đối chiếu khối khai trong <c>.dbiproj</c> với file <c>.cs</c> thật trên đĩa — hai chiều.
    /// </summary>
    private static List<ValidationIssue> CheckBlockFiles(DbiProject project)
    {
        var issues = new List<ValidationIssue>();
        string root = project.ProjectDirectory;

        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var block in project.Blocks)
        {
            string full = Path.Combine(root, block.FileName.Replace('/', Path.DirectorySeparatorChar));
            declared.Add(Path.GetFullPath(full));

            if (!File.Exists(full))
            {
                issues.Add(new ValidationIssue(IssueSeverity.Warning,
                    $"Khối '{block.Name}' khai báo tệp '{block.FileName}' nhưng tệp không tồn tại. " +
                    "Hãy xoá khối khỏi project, hoặc tạo lại từ mẫu.", block.Name));
            }
        }

        string blocksDir = Path.Combine(root, BlocksFolder);
        if (Directory.Exists(blocksDir))
        {
            foreach (string file in Directory.EnumerateFiles(blocksDir, "*.cs", SearchOption.TopDirectoryOnly))
            {
                if (declared.Contains(Path.GetFullPath(file))) continue;

                issues.Add(new ValidationIssue(IssueSeverity.Warning,
                    $"Tệp '{BlocksFolder}/{Path.GetFileName(file)}' có trong thư mục nhưng chưa khai " +
                    "trong project. Thêm vào project?", Path.GetFileNameWithoutExtension(file)));
            }
        }

        return issues;
    }

    // ── Save ─────────────────────────────────────────────────────────────────────

    /// <summary>Ghi <c>.dbiproj</c> của project đang mở. Ghi atomic.</summary>
    public void Save()
    {
        if (Current is null)
            throw new InvalidOperationException("Chưa mở project nào.");

        WriteProjectFile(Current);
        Current.IsDirty = false;
        _recent.Add(Current.ProjectFilePath);
    }

    /// <summary>
    /// Lưu project sang vị trí mới, copy toàn bộ <c>Blocks/</c> và <c>Generated/</c>.
    /// Project đang mở chuyển sang trỏ vị trí mới.
    /// </summary>
    public void SaveAs(string newProjectFilePath)
    {
        if (Current is null)
            throw new InvalidOperationException("Chưa mở project nào.");

        string newRoot = Path.GetDirectoryName(Path.GetFullPath(newProjectFilePath))
            ?? throw new ArgumentException("Đường dẫn không hợp lệ.", nameof(newProjectFilePath));

        string oldRoot = Current.ProjectDirectory;

        Directory.CreateDirectory(newRoot);

        foreach (string folder in new[] { BlocksFolder, GeneratedFolder })
        {
            CopyDirectory(Path.Combine(oldRoot, folder), Path.Combine(newRoot, folder));
        }

        string gitIgnore = Path.Combine(oldRoot, ".gitignore");
        if (File.Exists(gitIgnore))
            File.Copy(gitIgnore, Path.Combine(newRoot, ".gitignore"), overwrite: true);

        Current.ProjectFilePath = Path.GetFullPath(newProjectFilePath);
        Current.Name = Path.GetFileNameWithoutExtension(newProjectFilePath);

        WriteProjectFile(Current);
        Current.IsDirty = false;
        _recent.Add(Current.ProjectFilePath);
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(source)) return;

        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// Ghi atomic: ra tệp tạm cùng ổ đĩa, flush xuống đĩa, rồi mới thay thế bằng thao tác
    /// nguyên tử của hệ tệp. Kill tiến trình giữa chừng thì <c>.dbiproj</c> cũ vẫn nguyên vẹn —
    /// không bao giờ để lại tệp ghi dở.
    /// </summary>
    private static void WriteProjectFile(DbiProject project)
    {
        string path = project.ProjectFilePath;
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidOperationException($"Đường dẫn project không hợp lệ: '{path}'.");

        Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(project, JsonOptions);
        string tempPath = Path.Combine(directory, Path.GetFileName(path) + ".tmp");

        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    // ── Close ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Đóng project đang mở. Gọi <see cref="Save"/> trước nếu muốn giữ thay đổi —
    /// lớp UI chịu trách nhiệm hỏi người dùng khi <see cref="DbiProject.IsDirty"/>.
    /// </summary>
    public void Close() => SetCurrent(null);

    private void SetCurrent(DbiProject? project)
    {
        Current = project;
        CurrentChanged?.Invoke(this, project);
    }
}
