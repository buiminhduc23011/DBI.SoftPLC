using System.Collections.Concurrent;
using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;

namespace DBI.Controller.Driver.Simulation;

/// <summary>
/// Driver giả lập dùng cho test và chạy khô không cần phần cứng.
/// Hỗ trợ cả ba kiểu tag <c>bool</c> / <c>int</c> / <c>float</c>.
/// </summary>
public class SimulationDriver : IDriver
{
    /// <summary>
    /// Bỏ qua bảng định tuyến và soi toàn bộ tag trong memory image.
    /// </summary>
    /// <remarks>
    /// Chỉ dành cho <c>TestHost</c> chạy logic khi chưa có Tag Table. Mặc định <c>false</c> và
    /// phải bật tường minh: nếu "không có route thì lấy tất" là mặc định, một thiết bị chưa gán tag
    /// nào sẽ âm thầm vơ hết tag của thiết bị khác — đúng cái B-5 vừa sửa.
    /// </remarks>
    public bool MirrorAllTags { get; set; }

    private readonly ConcurrentDictionary<string, bool> _inputsBool = New<bool>();
    private readonly ConcurrentDictionary<string, int> _inputsInt = New<int>();
    private readonly ConcurrentDictionary<string, float> _inputsFloat = New<float>();

    private readonly ConcurrentDictionary<string, object> _outputs = New<object>();

    private static ConcurrentDictionary<string, T> New<T>() => new(StringComparer.OrdinalIgnoreCase);

    public SimulationDriver(string driverId = "SIMULATION_DRIVER") => DriverId = driverId;

    public string DriverId { get; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? LastError => null;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Connected;
        return Task.CompletedTask;
    }

    public Task ReadInputsAsync(
        IMemoryImage memoryImage,
        IReadOnlyList<TagRoute> routes,
        CancellationToken cancellationToken = default)
    {
        if (MirrorAllTags)
        {
            foreach (var kvp in _inputsBool) memoryImage.SetRawInput(kvp.Key, kvp.Value);
            foreach (var kvp in _inputsInt) memoryImage.SetRawInput(kvp.Key, kvp.Value);
            foreach (var kvp in _inputsFloat) memoryImage.SetRawInput(kvp.Key, kvp.Value);

            return Task.CompletedTask;
        }

        foreach (var route in routes)
        {
            if (route.Direction != TagDirection.Input) continue;

            switch (route.DataType)
            {
                case TagDataType.Bool:
                    memoryImage.SetRawInput(route.TagName, _inputsBool.GetValueOrDefault(route.TagName));
                    break;
                case TagDataType.Int:
                    memoryImage.SetRawInput(route.TagName, _inputsInt.GetValueOrDefault(route.TagName));
                    break;
                case TagDataType.Real:
                    memoryImage.SetRawInput(route.TagName, _inputsFloat.GetValueOrDefault(route.TagName));
                    break;
            }
        }

        return Task.CompletedTask;
    }

    public Task WriteOutputsAsync(
        IMemoryImage memoryImage,
        IReadOnlyList<TagRoute> routes,
        CancellationToken cancellationToken = default)
    {
        if (MirrorAllTags)
        {
            foreach (var kvp in memoryImage.GetRawOutputs())
                _outputs[kvp.Key] = kvp.Value;

            return Task.CompletedTask;
        }

        foreach (var route in routes)
        {
            if (route.Direction != TagDirection.Output) continue;

            _outputs[route.TagName] = route.DataType switch
            {
                TagDataType.Int => memoryImage.GetRawOutputInt(route.TagName),
                TagDataType.Real => memoryImage.GetRawOutputFloat(route.TagName),
                _ => memoryImage.GetRawOutputBool(route.TagName)
            };
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    // ── Cho Test/Debug giả lập Input ─────────────────────────────────────────────

    public void SetInputBool(string key, bool value) => _inputsBool[key] = value;
    public void SetInputInt(string key, int value) => _inputsInt[key] = value;
    public void SetInputFloat(string key, float value) => _inputsFloat[key] = value;

    // ── Cho Test/Debug kiểm tra Output đã ghi xuống ──────────────────────────────

    public bool GetOutputBool(string key) => _outputs.TryGetValue(key, out var val) && val is bool b && b;
    public int GetOutputInt(string key) => _outputs.TryGetValue(key, out var val) && val is int i ? i : 0;
    public float GetOutputFloat(string key) => _outputs.TryGetValue(key, out var val) && val is float f ? f : 0f;

    /// <summary>Tên mọi tag driver này đã ghi xuống — cho test khẳng định nó KHÔNG thấy tag của driver khác.</summary>
    public IReadOnlyCollection<string> WrittenTagNames => _outputs.Keys.ToList();
}
