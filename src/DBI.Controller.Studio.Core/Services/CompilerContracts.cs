namespace DBI.Controller.Studio.Core.Services;

public enum CompileDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public record CompileSourceFile(string Path, string Text);

public record CompileRequest(
    IReadOnlyList<CompileSourceFile> Sources,
    string AssemblyName,
    CompileOptimizationLevel Optimization);

public record CompileDiagnostic(
    string Id,
    CompileDiagnosticSeverity Severity,
    string Message,
    string FilePath,
    int Line,
    int Column);

public record CompileResult(
    bool Success,
    byte[]? AssemblyBytes,
    byte[]? PdbBytes,
    IReadOnlyList<CompileDiagnostic> Diagnostics);

public interface IProjectCompiler
{
    Task<CompileResult> CompileAsync(CompileRequest request, CancellationToken cancellationToken = default);
}

public enum CompileOptimizationLevel
{
    Debug,
    Release
}
