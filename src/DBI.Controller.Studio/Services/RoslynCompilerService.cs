using System.IO;
using System.Reflection;
using DBI.Controller.Core.Interfaces;
using DBI.Controller.SDK;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DBI.Controller.Studio.Services;

public class CompilationResult
{
    public bool Success { get; set; }
    public byte[]? AssemblyBytes { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class RoslynCompilerService
{
    public CompilationResult CompileSource(string sourceCode, string assemblyName = "UserDynamicProgram")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

        // Referenced Assemblies required to compile ControllerProgram logic
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(typeof(IMemoryImage).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ControllerProgram).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            $"{assemblyName}_{Guid.NewGuid():N}",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}")
                .ToList();

            return new CompilationResult
            {
                Success = false,
                Errors = errors
            };
        }

        return new CompilationResult
        {
            Success = true,
            AssemblyBytes = ms.ToArray()
        };
    }
}
