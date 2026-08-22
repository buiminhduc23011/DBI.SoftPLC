using System.Collections.Immutable;
using DBI.Controller.Studio.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynPad.Roslyn;

namespace DBI.Controller.Studio.Core.Services.CodeAnalysis;

public sealed record StudioSourceFile(string AbsolutePath, string Text);

public sealed class StudioRoslynWorkspace
{
    public static StudioRoslynWorkspace? Current { get; private set; }

    private readonly object _gate = new();
    private readonly Dictionary<string, string> _documents = new(StringComparer.OrdinalIgnoreCase);
    private Workspace? _workspace;
    private DbiProject? _project;
    private ProjectId? _projectId;

    public StudioRoslynWorkspace(RoslynHost? host = null)
    {
        Host = host;
        Current = this;
    }

    public RoslynHost? Host { get; private set; }

    public void InitializeHost(RoslynHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (_gate) Host ??= host;
    }

    public DbiProject? CurrentProject
    {
        get { lock (_gate) return _project; }
    }

    public Workspace? CurrentWorkspace
    {
        get { lock (_gate) return _workspace; }
    }

    public event EventHandler? WorkspaceChanged;

    public void OpenProject(DbiProject? project)
    {
        lock (_gate)
        {
            _project = project;
            _documents.Clear();

            if (project is null)
            {
                _workspace = null;
                _projectId = null;
                RaiseChanged();
                return;
            }

            _projectId = ProjectId.CreateNewId(project.Name);

            foreach (var file in EnumerateProjectFiles(project))
                _documents[file.AbsolutePath] = file.Text;

            foreach (var file in EnumerateSdkSourceFiles())
                _documents[file.AbsolutePath] = file.Text;

            RebuildWorkspace();
        }
    }

    public void UpdateDocument(string path, string text)
    {
        lock (_gate)
        {
            if (_project is null) return;

            _documents[Normalize(path)] = text;
            RebuildWorkspace();
        }
    }

    public void RegenerateIoDocument(string generated)
    {
        lock (_gate)
        {
            if (_project is null) return;

            string generatedPath = Path.Combine(_project.ProjectDirectory, ProjectService.GeneratedFolder, IoCodeGenerator.GeneratedFileName);
            _documents[Normalize(generatedPath)] = generated;
            RebuildWorkspace();
        }
    }

    public async Task<IReadOnlyList<CompileDiagnostic>> GetCurrentDiagnosticsAsync()
    {
        lock (_gate)
        {
            if (_workspace is null) return Array.Empty<CompileDiagnostic>();
        }

        return await CompileCurrentProjectAsync().ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CompileDiagnostic>> GetDiagnosticsAsync(string path)
    {
        lock (_gate)
        {
            if (_workspace is null) return Array.Empty<CompileDiagnostic>();
        }

        var diagnostics = await CompileCurrentProjectAsync().ConfigureAwait(false);
        string normalized = Normalize(path);
        return diagnostics.Where(d => string.Equals(Normalize(d.FilePath), normalized, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public void ApplyToWorkspace(Workspace workspace)
    {
        if (workspace is null) throw new ArgumentNullException(nameof(workspace));

        lock (_gate)
        {
            if (_project is null || _projectId is null) return;

            var files = EnumerateFilesWithGenerated();
            var docInfos = files.Select(file => DocumentInfo.Create(
                    DocumentId.CreateNewId(_projectId, Path.GetFileName(file.AbsolutePath)),
                    Path.GetFileName(file.AbsolutePath),
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(file.Text), VersionStamp.Create())),
                    filePath: file.AbsolutePath))
                .ToImmutableArray();

            var projInfo = ProjectInfo.Create(
                _projectId,
                VersionStamp.Create(),
                _project.Name,
                _project.Name,
                LanguageNames.CSharp,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
                documents: docInfos,
                metadataReferences: BuildMetadataReferences());

            var solution = workspace.CurrentSolution;
            foreach (var projectId in solution.ProjectIds)
                solution = solution.RemoveProject(projectId);

            workspace.TryApplyChanges(solution.AddProject(projInfo));
        }
    }

    public void AttachProjectDocuments(RoslynWorkspace workspace, DocumentId editorDocumentId)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        lock (_gate)
        {
            if (_project is null) return;

            var document = workspace.CurrentSolution.GetDocument(editorDocumentId)
                ?? throw new InvalidOperationException("Editor document không còn thuộc Roslyn project.");
            var project = document.Project;
            var solution = workspace.CurrentSolution;
            string editorPath = Normalize(document.FilePath ?? string.Empty);

            foreach (var file in EnumerateFilesWithGenerated())
            {
                if (string.Equals(Normalize(file.AbsolutePath), editorPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (project.Documents.Any(existing =>
                        string.Equals(Normalize(existing.FilePath ?? string.Empty), Normalize(file.AbsolutePath), StringComparison.OrdinalIgnoreCase)))
                    continue;

                solution = solution.AddDocument(DocumentInfo.Create(
                    DocumentId.CreateNewId(project.Id, Path.GetFileName(file.AbsolutePath)),
                    Path.GetFileName(file.AbsolutePath),
                    loader: TextLoader.From(TextAndVersion.Create(SourceText.From(file.Text), VersionStamp.Create())),
                    filePath: file.AbsolutePath));
            }

            workspace.TryApplyChanges(solution);
        }
    }

    private void RebuildWorkspace()
    {
        if (_project is null || _projectId is null)
        {
            _workspace = null;
            RaiseChanged();
            return;
        }

        _workspace = new AdhocWorkspace();
        ApplyToWorkspace(_workspace);
        RaiseChanged();
    }

    private async Task<IReadOnlyList<CompileDiagnostic>> CompileCurrentProjectAsync()
    {
        if (_workspace is null) return Array.Empty<CompileDiagnostic>();

        var project = _workspace.CurrentSolution.Projects.FirstOrDefault();
        if (project is null) return Array.Empty<CompileDiagnostic>();

        var compilation = await project.GetCompilationAsync().ConfigureAwait(false);
        if (compilation is null) return Array.Empty<CompileDiagnostic>();

        return compilation.GetDiagnostics()
            .Where(d => d.Severity != DiagnosticSeverity.Hidden)
            .Select(MapDiagnostic)
            .ToList();
    }

    private IEnumerable<StudioSourceFile> EnumerateProjectFiles(DbiProject project)
    {
        foreach (var block in project.Blocks)
        {
            string absolutePath = Path.GetFullPath(Path.Combine(project.ProjectDirectory, block.FileName.Replace('/', Path.DirectorySeparatorChar)));
            yield return new StudioSourceFile(absolutePath, File.Exists(absolutePath) ? File.ReadAllText(absolutePath) : string.Empty);
        }

        string generatedPath = Path.GetFullPath(Path.Combine(project.ProjectDirectory, ProjectService.GeneratedFolder, IoCodeGenerator.GeneratedFileName));
        if (File.Exists(generatedPath))
            yield return new StudioSourceFile(generatedPath, File.ReadAllText(generatedPath));
    }

    private IEnumerable<StudioSourceFile> EnumerateFilesWithGenerated()
    {
        foreach (var doc in _documents)
            yield return new StudioSourceFile(doc.Key, doc.Value);
    }

    private static IEnumerable<StudioSourceFile> EnumerateSdkSourceFiles()
    {
        yield return new StudioSourceFile(
            "global_usings.g.cs",
            """
            global using global::System;
            global using global::System.Collections.Generic;
            global using global::System.IO;
            global using global::System.Linq;
            global using global::System.Threading;
            global using global::System.Threading.Tasks;
            """);

        string root = FindSdkSourceRoot();
        string[] files =
        {
            "ControllerProgram.cs",
            Path.Combine("IO", "IOContainer.cs"),
            Path.Combine("Primitives", "Counter.cs"),
            Path.Combine("Primitives", "RisingEdge.cs"),
            Path.Combine("Primitives", "Tof.cs"),
            Path.Combine("Primitives", "Ton.cs"),
            Path.Combine("Primitives", "Tp.cs")
        };

        foreach (string relative in files)
        {
            string path = Path.Combine(root, relative);
            if (File.Exists(path))
                yield return new StudioSourceFile(path, File.ReadAllText(path));
        }
    }

    private static string FindSdkSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "DBI.Controller.SDK");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Không tìm thấy source DBI.Controller.SDK cho IntelliSense.");
    }

    private static IEnumerable<MetadataReference> BuildMetadataReferences()
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            typeof(object).Assembly.Location,
            typeof(Console).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            typeof(CodeBlock).Assembly.Location
        };

        string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            {
                // Không nạp lại DBI.Controller.SDK.dll vì mã nguồn SDK đã được nạp trực tiếp qua EnumerateSdkSourceFiles
                if (Path.GetFileName(path).Equals("DBI.Controller.SDK.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                references.Add(path);
            }
        }

        return references
            .Where(File.Exists)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();
    }

    private static CompileDiagnostic MapDiagnostic(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        string filePath = string.IsNullOrWhiteSpace(span.Path) ? "(generated)" : span.Path.Replace('\\', '/');

        return new CompileDiagnostic(
            diagnostic.Id,
            diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => CompileDiagnosticSeverity.Error,
                DiagnosticSeverity.Warning => CompileDiagnosticSeverity.Warning,
                _ => CompileDiagnosticSeverity.Info
            },
            diagnostic.GetMessage(),
            filePath,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1);
    }

    private static string Normalize(string path) => Path.GetFullPath(path).Replace('\\', '/');

    private void RaiseChanged() => WorkspaceChanged?.Invoke(this, EventArgs.Empty);
}
