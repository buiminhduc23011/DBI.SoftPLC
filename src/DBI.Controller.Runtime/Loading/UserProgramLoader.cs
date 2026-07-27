using System.Reflection;
using System.Runtime.Loader;
using DBI.Controller.Core.Interfaces;

namespace DBI.Controller.Runtime.Loading;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}

public class UserProgramLoader
{
    private PluginLoadContext? _loadContext;

    public IControllerProgramContract? CurrentProgram { get; private set; }

    public IControllerProgramContract LoadProgramFromAssembly(string dllPath, IMemoryImage memoryImage)
    {
        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"Khong tim thay file User DLL: {dllPath}");

        _loadContext = new PluginLoadContext(dllPath);
        Assembly assembly = _loadContext.LoadFromAssemblyPath(dllPath);

        Type? programType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IControllerProgramContract).IsAssignableFrom(t) && !t.IsAbstract);

        if (programType == null)
        {
            throw new InvalidOperationException(
                $"Assembly {dllPath} khong chua class nao trien khai {nameof(IControllerProgramContract)}.");
        }

        var instance = (IControllerProgramContract)Activator.CreateInstance(programType)!;
        instance.Initialize(memoryImage);

        // KHONG goi OnStart() o day. Trinh tu dung la Load -> Connect driver -> OnStart -> Start.
        CurrentProgram = instance;
        return instance;
    }

    public void UnloadProgram()
    {
        if (CurrentProgram != null)
        {
            try
            {
                CurrentProgram.OnStop();
            }
            catch
            {
                // Ignored on unload
            }

            CurrentProgram = null;
        }

        if (_loadContext != null)
        {
            _loadContext.Unload();
            _loadContext = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
