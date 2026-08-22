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

    // ── Block operations ────────────────────────────────────────────────────────

    public ProjectLoadResult AddBlock(NewBlockRequest request)
    {
        if (Current is null)
            throw new InvalidOperationException("Chưa mở project nào.");

        var validation = ValidateNewBlockName(Current, request.Name, request.Kind, currentBlock: null);
        if (validation is not null)
            return new ProjectLoadResult(null, new[] { validation });

        string relativePath = $"{BlocksFolder}/{request.Name}.cs";
        string absolutePath = Path.Combine(Current.ProjectDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(absolutePath))
        {
            return ProjectLoadResult.Failed(
                $"Tệp '{relativePath}' đã tồn tại. Hãy chọn tên khác hoặc đổi tên tệp cũ.");
        }

        var block = new CodeBlock
        {
            Name = request.Name.Trim(),
            Kind = request.Kind,
            FileName = relativePath,
            Comment = request.Comment.Trim()
        };

        Current.Blocks.Add(block);
        Current.IsDirty = true;

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, BlockTemplates.For(block.Kind, block.Name));

        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
    }

    public ProjectLoadResult RenameBlock(CodeBlock block, string newName)
    {
        if (Current is null)
            throw new InvalidOperationException("Chưa mở project nào.");

        ArgumentNullException.ThrowIfNull(block);

        var validation = ValidateNewBlockName(Current, newName, block.Kind, block);
        if (validation is not null)
            return new ProjectLoadResult(null, new[] { validation });

        string oldPath = Path.Combine(Current.ProjectDirectory, block.FileName.Replace('/', Path.DirectorySeparatorChar));
        string newRelativePath = $"{BlocksFolder}/{newName.Trim()}.cs";
        string newPath = Path.Combine(Current.ProjectDirectory, newRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath))
        {
            return ProjectLoadResult.Failed(
                $"Tệp '{newRelativePath}' đã tồn tại. Không thể đổi tên khối thành '{newName}'.");
        }

        string oldName = block.Name;
        block.Name = newName.Trim();
        block.FileName = newRelativePath;
        Current.IsDirty = true;

        if (File.Exists(oldPath))
        {
            string text = File.ReadAllText(oldPath);
            File.WriteAllText(oldPath, RenameClass(text, oldName, newName.Trim()));

            if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                File.Move(oldPath, newPath);
            }
        }

        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
    }

    public void DeleteBlock(CodeBlock block)
    {
        if (Current is null)
            throw new InvalidOperationException("Chưa mở project nào.");

        ArgumentNullException.ThrowIfNull(block);

        string absolutePath = Path.Combine(Current.ProjectDirectory, block.FileName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(absolutePath)) File.Delete(absolutePath);

        Current.Blocks.Remove(block);
        Current.IsDirty = true;
    }

    public ProjectLoadResult SetMainBlock(CodeBlock block)
    {
        if (Current is null)
            throw new InvalidOperationException("Chưa mở project nào.");

        ArgumentNullException.ThrowIfNull(block);

        foreach (var candidate in Current.Blocks)
            candidate.Kind = candidate == block ? BlockKind.Main : candidate.Kind == BlockKind.Main ? BlockKind.FunctionBlock : candidate.Kind;

        Current.IsDirty = true;
        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
    }

    public CodeBlock AttachExistingBlock(string absolutePath)
    {
        if (Current is null)
            throw new InvalidOperationException("Chưa mở project nào.");

        string relativePath = Path.GetRelativePath(Current.ProjectDirectory, absolutePath)
            .Replace(Path.DirectorySeparatorChar, '/');
        string name = Path.GetFileNameWithoutExtension(absolutePath);

        var block = new CodeBlock
        {
            Name = name,
            Kind = name.Equals("Main", StringComparison.OrdinalIgnoreCase) &&
                   Current.Blocks.All(b => b.Kind != BlockKind.Main)
                ? BlockKind.Main
                : BlockKind.FunctionBlock,
            FileName = relativePath
        };

        Current.Blocks.Add(block);
        Current.IsDirty = true;
        return block;
    }

    public ProjectLoadResult AddTagTable(string name)
    {
        if (Current is null)
            throw new InvalidOperationException("Chưa mở project nào.");

        string candidate = name.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return ProjectLoadResult.Failed("Tên tag table không được để trống.");

        if (Current.TagTables.Any(t => t.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            return ProjectLoadResult.Failed($"Đã có tag table tên '{candidate}'.");

        Current.TagTables.Add(new TagTable { Name = candidate });
        Current.IsDirty = true;
        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
    }

    public ProjectLoadResult RenameTagTable(TagTable table, string newName)
    {
        if (Current is null)
            throw new InvalidOperationException("Chưa mở project nào.");

        string candidate = newName.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return ProjectLoadResult.Failed("Tên tag table không được để trống.");

        if (Current.TagTables.Any(t => !ReferenceEquals(t, table) &&
                                       t.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            return ProjectLoadResult.Failed($"Đã có tag table tên '{candidate}'.");

        table.Name = candidate;
        Current.IsDirty = true;
        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
    }

    public ProjectLoadResult DeleteTagTable(TagTable table)
    {
        if (Current is null)
            throw new InvalidOperationException("Chưa mở project nào.");

        if (string.Equals(table.Name, "Default Tag Table", StringComparison.OrdinalIgnoreCase))
            return ProjectLoadResult.Failed("Không xoá được Default Tag Table.");

        Current.TagTables.Remove(table);
        Current.IsDirty = true;
        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
    }

    public ProjectLoadResult AddDevice(string name, string driverType)
    {
        if (Current is null) throw new InvalidOperationException("Chưa mở project nào.");
        if (!ProjectValidator.IsValidCSharpIdentifier(name.Trim()))
            return ProjectLoadResult.Failed($"Tên device '{name}' không hợp lệ.");
        if (Current.Devices.Any(d => d.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return ProjectLoadResult.Failed($"Đã có device tên '{name.Trim()}'.");
        try { Current.Devices.Add(Devices.DriverCatalog.CreateDefault(driverType, name.Trim())); }
        catch (ArgumentException ex) { return ProjectLoadResult.Failed(ex.Message); }
        Current.IsDirty = true;
        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
    }

    public ProjectLoadResult RenameDevice(DeviceConfig device, string newName)
    {
        if (Current is null) throw new InvalidOperationException("Chưa mở project nào.");
        string candidate = newName.Trim();
        if (!ProjectValidator.IsValidCSharpIdentifier(candidate)) return ProjectLoadResult.Failed($"Tên device '{candidate}' không hợp lệ.");
        if (Current.Devices.Any(d => !ReferenceEquals(d, device) && d.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            return ProjectLoadResult.Failed($"Đã có device tên '{candidate}'.");
        foreach (var tag in Current.AllTags().Where(t => t.Device.Equals(device.Name, StringComparison.OrdinalIgnoreCase))) tag.Device = candidate;
        device.Name = candidate;
        Current.IsDirty = true;
        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
    }

    public ProjectLoadResult DeleteDevice(DeviceConfig device)
    {
        if (Current is null) throw new InvalidOperationException("Chưa mở project nào.");
        var usages = Current.AllTags().Where(t => t.Device.Equals(device.Name, StringComparison.OrdinalIgnoreCase)).Select(t => t.Name).ToList();
        if (usages.Count > 0) return ProjectLoadResult.Failed($"Không thể xoá device '{device.Name}'; đang được dùng bởi: {string.Join(", ", usages)}.");
        Current.Devices.Remove(device);
        Current.IsDirty = true;
        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
    }

    // ── Watch tables (phase-10) ──────────────────────────────────────────────────

    public ProjectLoadResult AddWatchTable(string name)
    {
        if (Current is null) throw new InvalidOperationException("Chưa mở project nào.");

        string candidate = name.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return ProjectLoadResult.Failed("Tên watch table không được để trống.");

        if (Current.WatchTables.Any(t => t.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            return ProjectLoadResult.Failed($"Đã có watch table tên '{candidate}'.");

        Current.WatchTables.Add(new WatchTable { Name = candidate });
        Current.IsDirty = true;
        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
    }

    public ProjectLoadResult RenameWatchTable(WatchTable table, string newName)
    {
        if (Current is null) throw new InvalidOperationException("Chưa mở project nào.");

        string candidate = newName.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
            return ProjectLoadResult.Failed("Tên watch table không được để trống.");

        if (Current.WatchTables.Any(t => !ReferenceEquals(t, table) &&
                                         t.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            return ProjectLoadResult.Failed($"Đã có watch table tên '{candidate}'.");

        table.Name = candidate;
        Current.IsDirty = true;
        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
    }

    public ProjectLoadResult DeleteWatchTable(WatchTable table)
    {
        if (Current is null) throw new InvalidOperationException("Chưa mở project nào.");

        Current.WatchTables.Remove(table);
        Current.IsDirty = true;
        return new ProjectLoadResult(Current, Array.Empty<ValidationIssue>());
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

    private static ValidationIssue? ValidateNewBlockName(
        DbiProject project,
        string? name,
        BlockKind kind,
        CodeBlock? currentBlock)
    {
        string candidate = name?.Trim() ?? "";

        if (!ProjectValidator.IsValidCSharpIdentifier(candidate))
        {
            return new ValidationIssue(
                IssueSeverity.Error,
                $"Tên khối '{candidate}' không hợp lệ. Tên phải là identifier C# hợp lệ.",
                candidate);
        }

        if (project.Blocks.Any(b => !ReferenceEquals(b, currentBlock) &&
                                    b.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            return new ValidationIssue(IssueSeverity.Error, $"Đã có khối tên '{candidate}'.", candidate);
        }

        if (kind == BlockKind.Main &&
            project.Blocks.Any(b => !ReferenceEquals(b, currentBlock) && b.Kind == BlockKind.Main))
        {
            return new ValidationIssue(
                IssueSeverity.Error,
                "Project đã có khối Main. Không tạo thêm khối Main thứ hai.",
                candidate);
        }

        return null;
    }

    private static string RenameClass(string text, string oldName, string newName)
    {
        string[] patterns =
        {
            $"class {oldName}",
            $"static class {oldName}"
        };

        foreach (string pattern in patterns)
        {
            if (text.Contains(pattern, StringComparison.Ordinal))
                text = text.Replace(pattern, pattern.Replace(oldName, newName, StringComparison.Ordinal), StringComparison.Ordinal);
        }

        return text;
    }
}
