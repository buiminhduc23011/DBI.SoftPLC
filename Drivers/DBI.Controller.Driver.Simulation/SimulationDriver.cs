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
    private readonly ConcurrentDictionary<string, bool> _inputsBool = New<bool>();
    private readonly ConcurrentDictionary<string, int> _inputsInt = New<int>();
    private readonly ConcurrentDictionary<string, float> _inputsFloat = New<float>();

    private readonly ConcurrentDictionary<string, object> _outputs = New<object>();

    private static ConcurrentDictionary<string, T> New<T>() => new(StringComparer.OrdinalIgnoreCase);

    public string DriverId => "SIMULATION_DRIVER";
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Connected;
        return Task.CompletedTask;
    }

    public Task ReadInputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default)
    {
        foreach (var kvp in _inputsBool) memoryImage.SetRawInput(kvp.Key, kvp.Value);
        foreach (var kvp in _inputsInt) memoryImage.SetRawInput(kvp.Key, kvp.Value);
        foreach (var kvp in _inputsFloat) memoryImage.SetRawInput(kvp.Key, kvp.Value);

        return Task.CompletedTask;
    }

    public Task WriteOutputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default)
    {
        foreach (var kvp in memoryImage.GetRawOutputs())
            _outputs[kvp.Key] = kvp.Value;

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
}
