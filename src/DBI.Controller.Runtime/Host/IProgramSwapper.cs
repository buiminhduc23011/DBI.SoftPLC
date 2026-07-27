using DBI.Controller.Core.Interfaces;
using DBI.Controller.Protocol;
using DBI.Controller.Runtime.Loading;

namespace DBI.Controller.Runtime.Host;

public record SwapResult(bool Ok, IControllerProgramContract? Program, string? Error = null, string? ErrorCode = null)
{
    public static SwapResult Failure(string error, string errorCode) => new(false, null, error, errorCode);
}

/// <summary>
/// Thay chương trình đang chạy. ADR-004: v1 chỉ Cold Restart, nhưng để sẵn đường cho Hot Reload
/// bằng cách đi qua interface này thay vì gọi thẳng <see cref="UserProgramLoader"/>.
/// </summary>
public interface IProgramSwapper
{
    Task<SwapResult> SwapAsync(byte[] assemblyBytes, SwapMode mode, IMemoryImage memoryImage, CancellationToken ct = default);

    /// <summary>Gỡ chương trình đang nạp. Gọi <c>OnStop()</c> rồi unload AssemblyLoadContext.</summary>
    void Unload();
}

/// <summary>
/// Cold Restart: gỡ hẳn chương trình cũ, ghi DLL mới ra đĩa, nạp lại từ đầu.
/// </summary>
public class ColdRestartSwapper : IProgramSwapper
{
    private readonly UserProgramLoader _loader = new();
    private readonly string _stagingDirectory;

    /// <param name="stagingDirectory">
    /// Nơi ghi DLL trước khi nạp. Mỗi lần deploy ghi một tên mới — AssemblyLoadContext còn giữ khoá
    /// tệp cũ một lúc sau khi unload, ghi đè cùng tên sẽ dính <c>IOException</c>.
    /// </param>
    public ColdRestartSwapper(string? stagingDirectory = null)
    {
        _stagingDirectory = stagingDirectory ?? Path.Combine(Path.GetTempPath(), "DBI.Runtime", "staging");
        Directory.CreateDirectory(_stagingDirectory);
    }

    public Task<SwapResult> SwapAsync(
        byte[] assemblyBytes,
        SwapMode mode,
        IMemoryImage memoryImage,
        CancellationToken ct = default)
    {
        if (mode == SwapMode.HotReload)
        {
            return Task.FromResult(SwapResult.Failure(
                "Runtime này chưa hỗ trợ Hot Reload. Dùng Cold Restart: Runtime sẽ dừng, nạp " +
                "chương trình mới rồi chạy lại.",
                ErrorCodes.HotReloadUnsupported));
        }

        if (assemblyBytes.Length == 0)
        {
            return Task.FromResult(SwapResult.Failure(
                "Assembly rỗng — không có gì để nạp.", ErrorCodes.AssemblyLoadFailed));
        }

        _loader.UnloadProgram();

        string dllPath = Path.Combine(_stagingDirectory, $"UserProgram.{Guid.NewGuid():N}.dll");

        try
        {
            File.WriteAllBytes(dllPath, assemblyBytes);
            CleanupStaleAssemblies(keep: dllPath);

            var program = _loader.LoadProgramFromAssembly(dllPath, memoryImage);
            return Task.FromResult(new SwapResult(true, program));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(SwapResult.Failure(ex.Message, ErrorCodes.NoProgramEntryPoint));
        }
        catch (Exception ex)
        {
            return Task.FromResult(SwapResult.Failure(
                $"Nạp assembly thất bại: {ex.Message}", ErrorCodes.AssemblyLoadFailed));
        }
    }

    public void Unload() => _loader.UnloadProgram();

    /// <summary>Xoá DLL của các lần deploy trước. Tệp nào còn bị khoá thì bỏ qua, lần sau dọn tiếp.</summary>
    private void CleanupStaleAssemblies(string keep)
    {
        foreach (string file in Directory.EnumerateFiles(_stagingDirectory, "UserProgram.*.dll"))
        {
            if (string.Equals(file, keep, StringComparison.OrdinalIgnoreCase)) continue;

            try { File.Delete(file); }
            catch (IOException) { /* AssemblyLoadContext còn giữ khoá */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
