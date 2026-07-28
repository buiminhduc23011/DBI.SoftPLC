using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;

namespace DBI.Controller.Studio.Core.ViewModels;

public partial class ShellViewModel
{
    private byte[]? _lastCompiledAssembly;
    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            RefreshCommandState();
        }
    }

    public bool CanCompile => Project is not null && !IsBusy;
    public bool CanDeploy => Project is not null && !IsBusy && _lastCompiledAssembly is not null;
    public bool CanStart => !IsBusy && Runtime.LastStatus?.State is RuntimeState.Stopped;
    public bool CanStop => !IsBusy && Runtime.LastStatus?.State is RuntimeState.Running;
    public bool CanReset => !IsBusy && Runtime.LastStatus?.State is RuntimeState.Faulted;

    [RelayCommand]
    private async Task CompileAsync()
    {
        await CompileProjectAsync(force: true).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task DeployAsync()
    {
        if (Project is null) return;

        if (Runtime.LastStatus?.State == RuntimeState.Running &&
            !_prompt.Confirm(
                "Runtime is RUNNING.\n\nDeploy will stop the machine, load the new program, then restart it.\n" +
                "Outputs will enter a safe state during the transition.\n\nContinue deploy?",
                "Deploy while running"))
        {
            return;
        }

        if (!await CompileProjectAsync(force: _lastCompiledAssembly is null).ConfigureAwait(false))
            return;

        IsBusy = true;

        try
        {
            var target = GetRuntimeTarget(Project);
            var launch = await _launcher.EnsureRunningAsync(target).ConfigureAwait(false);

            if (launch.Outcome == LaunchOutcome.Failed)
            {
                Inspector.LogDiagnostic(launch.Message ?? "Failed to start Runtime.", IssueSeverity.Error);
                return;
            }

            if (launch.Message is not null)
                Inspector.LogDiagnostic(launch.Message, IssueSeverity.Warning);

            if (!await Runtime.ConnectAsync(target).ConfigureAwait(false))
            {
                Inspector.LogDiagnostic("Could not connect to Runtime.", IssueSeverity.Error);
                return;
            }

            var deploy = await Runtime.DeployAsync(
                _lastCompiledAssembly!,
                _generator.ExportRoutes(Project),
                ExportDevices(Project)).ConfigureAwait(false);

            if (!deploy.Ok)
            {
                Inspector.LogDiagnostic($"Deploy failed: {deploy.Error}", IssueSeverity.Error);
                return;
            }

            Inspector.LogDiagnostic("Deploy succeeded. Runtime accepted the new program.", IssueSeverity.Warning);

            if (Project.Runtime.AutoStart)
            {
                Inspector.LogDiagnostic(
                    "The machine will AUTO-START after a power loss when Runtime restarts.",
                    IssueSeverity.Warning);
            }
        }
        finally
        {
            IsBusy = false;
            RefreshCommandState();
        }
    }

    [RelayCommand]
    private async Task StartRuntimeAsync()
    {
        if (Project is null) return;
        IsBusy = true;
        try
        {
            if (await Runtime.ConnectAsync(GetRuntimeTarget(Project)).ConfigureAwait(false))
            {
                await Runtime.StartAsync().ConfigureAwait(false);
                Inspector.LogDiagnostic("Start command sent to Runtime.", IssueSeverity.Warning);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StopRuntimeAsync()
    {
        IsBusy = true;
        try
        {
            await Runtime.StopAsync().ConfigureAwait(false);
                Inspector.LogDiagnostic("Stop command sent to Runtime.", IssueSeverity.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetRuntimeAsync()
    {
        IsBusy = true;
        try
        {
            await Runtime.ResetAsync().ConfigureAwait(false);
                Inspector.LogDiagnostic("Reset command sent to Runtime.", IssueSeverity.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> CompileProjectAsync(bool force)
    {
        if (Project is null) return false;
        if (!force && _lastCompiledAssembly is not null && !Editors.HasUnsavedChanges && !Project.IsDirty)
            return true;

        IsBusy = true;

        try
        {
            await SaveAllAsync().ConfigureAwait(false);

            var issues = ValidateForBuild(Project);
            Inspector.ClearInformationCommand.Execute(null);
            Inspector.LogInformation(issues);

            if (issues.Any(i => i.Severity == IssueSeverity.Error))
            {
                _lastCompiledAssembly = null;
                RefreshCommandState();
                return false;
            }

            var request = new CompileRequest(
                CollectProjectSources(Project),
                SanitizeAssemblyName(Project.Name),
                CompileOptimizationLevel.Debug);

            var result = await _compiler.CompileAsync(request).ConfigureAwait(false);
            LogCompileDiagnostics(result.Diagnostics);

            if (!result.Success || result.AssemblyBytes is null)
            {
                _lastCompiledAssembly = null;
                Inspector.LogInformation("Build failed. Check Diagnostics for details.", IssueSeverity.Error);
                RefreshCommandState();
                return false;
            }

            _lastCompiledAssembly = result.AssemblyBytes;
            Inspector.LogInformation(
                $"Build thành công: {request.Sources.Count} file -> {_lastCompiledAssembly.Length:N0} bytes.",
                IssueSeverity.Warning);
            RefreshCommandState();
            return true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private IReadOnlyList<ValidationIssue> ValidateForBuild(DbiProject project)
    {
        var issues = new List<ValidationIssue>(ProjectValidator.Validate(project));

        foreach (var block in project.Blocks)
        {
            string absolutePath = Path.Combine(
                project.ProjectDirectory,
                block.FileName.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(absolutePath))
            {
                issues.Add(new ValidationIssue(
                    IssueSeverity.Error,
                    $"Khối '{block.Name}' đang trỏ tới file không tồn tại: '{block.FileName}'.",
                    block.Name));
            }
        }

        return issues;
    }

    private IReadOnlyList<CompileSourceFile> CollectProjectSources(DbiProject project)
    {
        EnsureGeneratedCode(project);

        var sources = new List<CompileSourceFile>();
        foreach (var block in project.Blocks)
        {
            string absolutePath = Path.Combine(
                project.ProjectDirectory,
                block.FileName.Replace('/', Path.DirectorySeparatorChar));
            sources.Add(new CompileSourceFile(block.FileName, File.ReadAllText(absolutePath)));
        }

        string generatedPath = _generator.GetGeneratedFilePath(project);
        sources.Add(new CompileSourceFile(
            $"{ProjectService.GeneratedFolder}/{IoCodeGenerator.GeneratedFileName}",
            File.ReadAllText(generatedPath)));

        return sources;
    }

    private void LogCompileDiagnostics(IReadOnlyList<CompileDiagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            Inspector.LogInformation(
                $"[{diagnostic.Id}] {diagnostic.Message} ({diagnostic.FilePath}:{diagnostic.Line}:{diagnostic.Column})",
                diagnostic.Severity == CompileDiagnosticSeverity.Error ? IssueSeverity.Error : IssueSeverity.Warning);
        }
    }

    private IReadOnlyList<DeviceSpec> ExportDevices(DbiProject project) =>
        project.Devices
            .Select(d => new DeviceSpec(d.Name, d.DriverType, new Dictionary<string, string>(d.Settings)))
            .ToList();

    private static RuntimeConnectionTarget GetRuntimeTarget(DbiProject project) =>
        new(project.Runtime.PipeName, project.Runtime.Host, project.Runtime.Port);

    private static string SanitizeAssemblyName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private void ResetBuildState()
    {
        _lastCompiledAssembly = null;
        RefreshCommandState();
    }

    private void RefreshCommandState()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanCompile));
        OnPropertyChanged(nameof(CanDeploy));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanReset));
    }
}
