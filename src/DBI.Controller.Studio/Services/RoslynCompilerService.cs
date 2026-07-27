using System.IO;
using System.Collections.Immutable;
using System.Reflection;
using DBI.Controller.Core.Interfaces;
using DBI.Controller.Studio.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace DBI.Controller.Studio.Services;

public sealed class RoslynCompilerService : IProjectCompiler
{
    private static readonly string[] SdkSourceFiles =
    {
        "ControllerProgram.cs",
        Path.Combine("IO", "IOContainer.cs"),
        Path.Combine("Primitives", "Counter.cs"),
        Path.Combine("Primitives", "RisingEdge.cs"),
        Path.Combine("Primitives", "Tof.cs"),
        Path.Combine("Primitives", "Ton.cs"),
        Path.Combine("Primitives", "Tp.cs")
    };

    public Task<CompileResult> CompileAsync(CompileRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTrees = request.Sources
            .Select(source => CSharpSyntaxTree.ParseText(
                SourceText.From(source.Text),
                parseOptions,
                path: NormalizePath(source.Path)))
            .Concat(LoadSdkSyntaxTrees(parseOptions))
            .ToList();

        var compilation = CSharpCompilation.Create(
            $"{request.AssemblyName}_{Guid.NewGuid():N}",
            syntaxTrees,
            BuildReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: request.Optimization == CompileOptimizationLevel.Release
                    ? OptimizationLevel.Release
                    : OptimizationLevel.Debug,
                deterministic: true,
                nullableContextOptions: NullableContextOptions.Enable));

        using var assemblyStream = new MemoryStream();
        var emit = compilation.Emit(
            peStream: assemblyStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.Embedded),
            cancellationToken: cancellationToken);

        var diagnostics = emit.Diagnostics
            .Where(d => d.Severity != DiagnosticSeverity.Hidden)
            .Select(MapDiagnostic)
            .ToList();

        return Task.FromResult(new CompileResult(
            emit.Success,
            emit.Success ? assemblyStream.ToArray() : null,
            null,
            diagnostics));
    }

    private static IEnumerable<SyntaxTree> LoadSdkSyntaxTrees(CSharpParseOptions parseOptions)
    {
        string sdkRoot = FindSdkSourceRoot();

        foreach (string relativePath in SdkSourceFiles)
        {
            string absolutePath = Path.Combine(sdkRoot, relativePath);
            string source = File.ReadAllText(absolutePath);

            yield return CSharpSyntaxTree.ParseText(
                SourceText.From(source),
                parseOptions,
                path: NormalizePath($"sdk/{relativePath.Replace('\\', '/')}"));
        }
    }

    private static IEnumerable<MetadataReference> BuildReferences()
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            typeof(object).Assembly.Location,
            typeof(Console).Assembly.Location,
            typeof(Enumerable).Assembly.Location,
            typeof(IMemoryImage).Assembly.Location,
            typeof(ImmutableArray<>).Assembly.Location
        };

        string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator))
                references.Add(path);
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

    private static string FindSdkSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "DBI.Controller.SDK");
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Khong tim thay source cua DBI.Controller.SDK de bien dich user program.");
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
