using System.Diagnostics;
using System.IO.Pipes;

namespace DBI.Controller.Studio.Core.Services.Runtime;

public enum LaunchOutcome
{
    /// <summary>Đã có Runtime chạy sẵn — kết nối thẳng, không khởi động thêm.</summary>
    AlreadyRunning,

    Started,

    /// <summary>Target ở máy khác. Studio không bao giờ tự khởi động tiến trình trên máy người khác.</summary>
    SkippedRemote,

    /// <summary>Không tìm thấy tệp Runtime, hoặc khởi động rồi mà pipe không lên.</summary>
    Failed
}

public record LaunchResult(LaunchOutcome Outcome, string? Message = null, int? ProcessId = null)
{
    public bool CanConnect => Outcome is LaunchOutcome.AlreadyRunning or LaunchOutcome.Started;
}

/// <summary>
/// Khởi động tiến trình Runtime cục bộ khi chưa có cái nào chạy.
/// </summary>
/// <remarks>
/// ⚠️ <b>Studio không bao giờ giết Runtime.</b> Đóng Studio thì máy phải chạy tiếp — đó chính là lý
/// do tồn tại của ADR-001. Lớp này chỉ khởi động, không có đường tắt máy.
/// </remarks>
public class RuntimeProcessLauncher
{
    /// <summary>Tên tệp thực thi của Runtime.</summary>
    public string ExecutableName { get; set; } = "DBI.Controller.Runtime";

    /// <summary>Thư mục tìm tệp thực thi. Mặc định cạnh Studio.</summary>
    public string? SearchDirectory { get; set; }

    public int PipeReadyTimeoutMs { get; set; } = 10_000;

    public bool Headless { get; set; }

    public async Task<LaunchResult> EnsureRunningAsync(
        RuntimeConnectionTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.IsLocal)
            return new LaunchResult(LaunchOutcome.SkippedRemote,
                $"Runtime nằm ở '{target.Host}' — Studio chỉ kết nối, không tự khởi động.");

        if (IsPipeAvailable(target.PipeName))
            return new LaunchResult(LaunchOutcome.AlreadyRunning);

        string? executable = ResolveExecutable();

        if (executable is null)
        {
            return new LaunchResult(LaunchOutcome.Failed,
                $"Không tìm thấy '{ExecutableName}' trong '{SearchDirectory ?? AppContext.BaseDirectory}'. " +
                "Hãy khởi động Runtime bằng tay, hoặc kiểm tra lại cách đóng gói.");
        }

        var process = StartProcess(executable, target);

        if (process is null)
            return new LaunchResult(LaunchOutcome.Failed, $"Khởi động '{executable}' thất bại.");

        bool ready = await WaitForPipeAsync(target.PipeName, cancellationToken).ConfigureAwait(false);

        return ready
            ? new LaunchResult(LaunchOutcome.Started, null, process.Id)
            : new LaunchResult(LaunchOutcome.Failed,
                $"Đã khởi động Runtime nhưng pipe '{target.PipeName}' không sẵn sàng sau " +
                $"{PipeReadyTimeoutMs / 1000}s.", process.Id);
    }

    /// <summary>
    /// Kiểm tra đã có Runtime nào phục vụ pipe này chưa. Nhanh và không phụ thuộc tên tiến trình —
    /// Runtime chạy dạng service hay chạy tay đều nhận ra.
    /// </summary>
    public virtual bool IsPipeAvailable(string pipeName)
    {
        try
        {
            using var probe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            probe.Connect(timeout: 200);

            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (IOException)
        {
            // Pipe có nhưng đang bận — vẫn nghĩa là Runtime đang chạy.
            return true;
        }
    }

    protected virtual string? ResolveExecutable()
    {
        string directory = SearchDirectory ?? AppContext.BaseDirectory;

        foreach (string candidate in new[]
        {
            Path.Combine(directory, ExecutableName + ".exe"),
            Path.Combine(directory, ExecutableName),
            Path.Combine(directory, ExecutableName + ".dll")
        })
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    protected virtual Process? StartProcess(string executable, RuntimeConnectionTarget target)
    {
        var info = new ProcessStartInfo
        {
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = Headless
        };

        if (executable.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            info.FileName = "dotnet";
            info.ArgumentList.Add(executable);
        }
        else
        {
            info.FileName = executable;
        }

        info.ArgumentList.Add("--pipe");
        info.ArgumentList.Add(target.PipeName);

        if (Headless) info.ArgumentList.Add("--headless");

        return Process.Start(info);
    }

    private async Task<bool> WaitForPipeAsync(string pipeName, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(PipeReadyTimeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (ct.IsCancellationRequested) return false;
            if (IsPipeAvailable(pipeName)) return true;

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// Câu cảnh báo hiện khi người dùng đóng Studio lúc Runtime đang chạy máy.
    /// </summary>
    public const string ClosingWhileRunningWarning =
        "Runtime vẫn đang chạy máy. Đóng Studio sẽ không dừng máy.";
}
