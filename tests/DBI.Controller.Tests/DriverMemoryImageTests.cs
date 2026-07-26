using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Controller.Driver.Simulation;

namespace DBI.Controller.Tests;

/// <summary>
/// Decorator bọc <see cref="IMemoryImage"/> — mô phỏng đúng hình dạng của Force layer ở phase-11.
/// Đây KHÔNG phải <see cref="MemorySnapshot"/>: trước phase-00 mọi driver nhận object kiểu này
/// sẽ ép kiểu hỏng rồi im lặng return — máy đứng yên mà không exception, không log.
/// </summary>
internal sealed class ForcingMemoryImage : IMemoryImage
{
    private readonly IMemoryImage _inner;
    private readonly Dictionary<string, bool> _forcedBools = new(StringComparer.OrdinalIgnoreCase);

    public ForcingMemoryImage(IMemoryImage inner) => _inner = inner;

    public void Force(string key, bool value) => _forcedBools[key] = value;

    public bool GetBool(string key) => _forcedBools.TryGetValue(key, out var f) ? f : _inner.GetBool(key);
    public void SetBool(string key, bool value) => _inner.SetBool(key, value);

    public int GetInt(string key) => _inner.GetInt(key);
    public void SetInt(string key, int value) => _inner.SetInt(key, value);

    public float GetFloat(string key) => _inner.GetFloat(key);
    public void SetFloat(string key, float value) => _inner.SetFloat(key, value);

    public void SetRawInput(string key, bool value) => _inner.SetRawInput(key, value);
    public void SetRawInput(string key, int value) => _inner.SetRawInput(key, value);
    public void SetRawInput(string key, float value) => _inner.SetRawInput(key, value);

    public bool GetRawOutputBool(string key) =>
        _forcedBools.TryGetValue(key, out var f) ? f : _inner.GetRawOutputBool(key);

    public int GetRawOutputInt(string key) => _inner.GetRawOutputInt(key);
    public float GetRawOutputFloat(string key) => _inner.GetRawOutputFloat(key);

    public IReadOnlyDictionary<string, object> GetRawOutputs()
    {
        var outputs = new Dictionary<string, object>(_inner.GetRawOutputs(), StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _forcedBools) outputs[kvp.Key] = kvp.Value;
        return outputs;
    }

    public IReadOnlyDictionary<string, object> SnapshotAll() => _inner.SnapshotAll();

    public void SwapInputBuffers() => _inner.SwapInputBuffers();
    public void SwapOutputBuffers() => _inner.SwapOutputBuffers();
    public void ClearAllOutputs() => _inner.ClearAllOutputs();
}

/// <summary>
/// B-2 — quả mìn hẹn giờ của phase-11. Driver phải làm việc qua <see cref="IMemoryImage"/>,
/// không được phụ thuộc lớp cụ thể <see cref="MemorySnapshot"/>.
/// </summary>
public class DriverMemoryImageTests
{
    /// <summary>
    /// Các test ở đây kiểm tra tầng <see cref="IMemoryImage"/>, không phải định tuyến — driver
    /// giả lập bật <c>MirrorAllTags</c> nên không cần route.
    /// </summary>
    private static readonly IReadOnlyList<TagRoute> NoRoutes = Array.Empty<TagRoute>();

    [Fact]
    public async Task Driver_NhanIMemoryImageKhongPhaiMemorySnapshot_VanDocInputDung()
    {
        var snapshot = new MemorySnapshot();
        IMemoryImage decorated = new ForcingMemoryImage(snapshot);

        var driver = new SimulationDriver { MirrorAllTags = true };
        await driver.ConnectAsync();
        driver.SetInputBool("StartButton", true);
        driver.SetInputInt("PartCount", 42);
        driver.SetInputFloat("Temperature", 36.6f);

        await driver.ReadInputsAsync(decorated, NoRoutes);
        decorated.SwapInputBuffers();

        Assert.True(decorated.GetBool("StartButton"));
        Assert.Equal(42, decorated.GetInt("PartCount"));
        Assert.Equal(36.6f, decorated.GetFloat("Temperature"));
    }

    [Fact]
    public async Task Driver_NhanIMemoryImageKhongPhaiMemorySnapshot_VanGhiOutputDung()
    {
        var snapshot = new MemorySnapshot();
        IMemoryImage decorated = new ForcingMemoryImage(snapshot);

        var driver = new SimulationDriver { MirrorAllTags = true };
        await driver.ConnectAsync();

        decorated.SetBool("ConveyorRun", true);
        decorated.SetInt("Speed", 750);
        decorated.SetFloat("Setpoint", 42.5f);
        decorated.SwapOutputBuffers();

        await driver.WriteOutputsAsync(decorated, NoRoutes);

        Assert.True(driver.GetOutputBool("ConveyorRun"));
        Assert.Equal(750, driver.GetOutputInt("Speed"));
        Assert.Equal(42.5f, driver.GetOutputFloat("Setpoint"));
    }

    [Fact]
    public async Task ForceLayer_DeGiaTriXuongDuocDriver_MaKhongLamDriverChetLang()
    {
        var snapshot = new MemorySnapshot();
        var forcing = new ForcingMemoryImage(snapshot);

        var driver = new SimulationDriver { MirrorAllTags = true };
        await driver.ConnectAsync();

        // Chương trình muốn tắt, nhưng kỹ sư force bật để test cơ cấu chấp hành.
        forcing.SetBool("ConveyorRun", false);
        forcing.SwapOutputBuffers();
        forcing.Force("ConveyorRun", true);

        await driver.WriteOutputsAsync(forcing, NoRoutes);

        Assert.True(driver.GetOutputBool("ConveyorRun"));
    }

    /// <summary>
    /// Chốt chặn phase-11: không assembly driver nào được nhắc tới lớp cụ thể
    /// <see cref="MemorySnapshot"/>. Đọc thẳng bảng TypeRef trong metadata — ép kiểu
    /// <c>is not MemorySnapshot</c> chắc chắn để lại dấu vết ở đây.
    /// </summary>
    [Theory]
    [MemberData(nameof(DriverAssemblyPaths))]
    public void DriverAssembly_KhongConThamChieuToiMemorySnapshot(string driverName, string assemblyPath)
    {
        Assert.True(File.Exists(assemblyPath), $"Không tìm thấy assembly của {driverName}: {assemblyPath}");

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        var referencedTypes = metadata.TypeReferences
            .Select(handle => metadata.GetString(metadata.GetTypeReference(handle).Name))
            .ToHashSet(StringComparer.Ordinal);

        // Chốt để test không "pass rỗng": driver nào cũng phải tham chiếu IMemoryImage.
        Assert.Contains(nameof(IMemoryImage), referencedTypes);

        Assert.False(
            referencedTypes.Contains(nameof(MemorySnapshot)),
            $"{driverName} vẫn ép kiểu xuống MemorySnapshot — mọi decorator IMemoryImage " +
            "(Force layer phase-11) sẽ làm driver này ngừng đọc/ghi trong im lặng.");
    }

    public static TheoryData<string, string> DriverAssemblyPaths()
    {
        var data = new TheoryData<string, string>();

        foreach (var type in new[]
        {
            typeof(SimulationDriver),
            typeof(Controller.Driver.Modbus.ModbusDriverAdapter),
            typeof(Controller.Driver.FactoryIO.FactoryIODriver),
            typeof(Controller.Driver.Delta.DeltaPlcDriverAdapter),
            typeof(Controller.Driver.Omron.OmronPlcDriverAdapter),
        })
        {
            data.Add(type.Assembly.GetName().Name!, type.Assembly.Location);
        }

        return data;
    }
}
