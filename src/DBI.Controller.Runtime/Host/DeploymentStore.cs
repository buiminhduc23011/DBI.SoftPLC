using System.Security.Cryptography;
using System.Text.Json;
using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;

namespace DBI.Controller.Runtime.Host;

public record DeploymentManifest(
    List<TagRoute> TagRoutes,
    List<DeviceSpec> Devices,
    DateTimeOffset DeployedAt,
    RuntimeState LastCleanState,
    bool AutoStart);

public record StoredDeployment(byte[] AssemblyBytes, DeploymentManifest Manifest);

/// <summary>
/// Lưu lần deploy gần nhất xuống đĩa để Runtime tự dựng lại sau khi khởi động lại (ADR-005).
/// </summary>
/// <remarks>
/// 🚨 <b>Có hệ quả an toàn.</b> Máy tự chạy lại sau mất điện là tiện, nhưng cũng có nghĩa băng tải
/// có thể quay khi không ai đứng cạnh. Hai chốt chặn:
/// <list type="number">
/// <item><b>Chốt 1</b> — <c>lastCleanState = Faulted</c> thì khởi động ở <c>Stopped</c>: máy vừa
/// hỏng vì logic, tự chạy lại chỉ tạo vòng lặp fault.</item>
/// <item><b>Chốt 2</b> — có tệp <c>.norun</c> thì luôn khởi động ở <c>Stopped</c>: phanh tay cho
/// thợ bảo trì, tạo được bằng tay, không cần Studio.</item>
/// </list>
/// </remarks>
public class DeploymentStore
{
    public const string NoRunFileName = ".norun";

    private const string AssemblyFileName = "program.dll";
    private const string ChecksumFileName = "program.sha256";
    private const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly string _root;

    /// <param name="rootDirectory">
    /// Để <c>null</c> dùng <c>%PROGRAMDATA%/DBI.Runtime/last-deploy</c>. Test truyền thư mục tạm.
    /// </param>
    public DeploymentStore(string? rootDirectory = null)
    {
        _root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "DBI.Runtime", "last-deploy");
    }

    public string RootDirectory => _root;

    /// <summary>⚠️ Chốt chặn 2 — phanh tay bảo trì. Có tệp này thì không bao giờ tự chạy.</summary>
    public bool HasNoRunFile => File.Exists(Path.Combine(_root, NoRunFileName));

    public string NoRunFilePath => Path.Combine(_root, NoRunFileName);

    public void Save(byte[] assemblyBytes, IEnumerable<TagRoute> routes, IEnumerable<DeviceSpec> devices, bool autoStart)
    {
        Directory.CreateDirectory(_root);

        File.WriteAllBytes(Path.Combine(_root, AssemblyFileName), assemblyBytes);
        File.WriteAllText(Path.Combine(_root, ChecksumFileName), Sha256(assemblyBytes));

        WriteManifest(new DeploymentManifest(
            routes.ToList(), devices.ToList(), DateTimeOffset.UtcNow, RuntimeState.Stopped, autoStart));
    }

    /// <summary>
    /// Ghi trạng thái sạch gần nhất.
    /// </summary>
    /// <remarks>
    /// Gọi <b>mỗi khi đổi trạng thái</b>, không phải lúc tắt máy: mất điện thì không có "lúc tắt"
    /// nào để kịp ghi.
    /// </remarks>
    public void UpdateLastCleanState(RuntimeState state)
    {
        var manifest = ReadManifest();
        if (manifest is null) return;

        WriteManifest(manifest with { LastCleanState = state });
    }

    /// <summary>
    /// Đọc lần deploy gần nhất. Trả <c>null</c> khi chưa có, thiếu tệp, hoặc <b>checksum sai</b> —
    /// nạp một DLL đã hỏng xuống máy công nghiệp nguy hiểm hơn nhiều so với việc không chạy gì.
    /// </summary>
    public StoredDeployment? Load()
    {
        string assemblyPath = Path.Combine(_root, AssemblyFileName);
        string checksumPath = Path.Combine(_root, ChecksumFileName);

        if (!File.Exists(assemblyPath) || !File.Exists(checksumPath)) return null;

        var manifest = ReadManifest();
        if (manifest is null) return null;

        byte[] bytes = File.ReadAllBytes(assemblyPath);
        string expected = File.ReadAllText(checksumPath).Trim();

        if (!string.Equals(Sha256(bytes), expected, StringComparison.OrdinalIgnoreCase))
            return null;

        return new StoredDeployment(bytes, manifest);
    }

    /// <summary>
    /// Trạng thái Runtime nên khởi động vào, sau khi qua cả hai chốt chặn an toàn.
    /// </summary>
    public RuntimeState DecideStartupState(StoredDeployment? deployment)
    {
        if (deployment is null) return RuntimeState.NoProgram;       // chưa deploy, hoặc checksum sai
        if (HasNoRunFile) return RuntimeState.Stopped;               // ⚠️ chốt chặn 2
        if (deployment.Manifest.LastCleanState == RuntimeState.Faulted) return RuntimeState.Stopped;  // ⚠️ chốt chặn 1
        if (!deployment.Manifest.AutoStart) return RuntimeState.Stopped;

        return deployment.Manifest.LastCleanState == RuntimeState.Running
            ? RuntimeState.Running
            : RuntimeState.Stopped;
    }

    public void Clear()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private DeploymentManifest? ReadManifest()
    {
        string path = Path.Combine(_root, ManifestFileName);
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<DeploymentManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void WriteManifest(DeploymentManifest manifest)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, ManifestFileName),
            JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
