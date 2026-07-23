using System.Collections.Concurrent;
using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;

namespace DBI.Controller.Driver.Simulation;

public class SimulationDriver : IDriver
{
    private readonly ConcurrentDictionary<string, bool> _simulatedInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _simulatedOutputs = new(StringComparer.OrdinalIgnoreCase);

    public string DriverId => "SIMULATION_DRIVER";
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Connected;
        return Task.CompletedTask;
    }

    public Task ReadInputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default)
    {
        if (memoryImage is MemorySnapshot snapshot)
        {
            foreach (var kvp in _simulatedInputs)
            {
                snapshot.SetRawInputBool(kvp.Key, kvp.Value);
            }
        }
        return Task.CompletedTask;
    }

    public Task WriteOutputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default)
    {
        if (memoryImage is MemorySnapshot snapshot)
        {
            foreach (var kvp in snapshot.GetAllOutputs())
            {
                _simulatedOutputs[kvp.Key] = kvp.Value;
            }
        }
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        State = ConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cho phép Test/Debug giả lập giá trị Input tác động lên hệ thống.
    /// </summary>
    public void SetInputBool(string key, bool value)
    {
        _simulatedInputs[key] = value;
    }

    /// <summary>
    /// Cho phép Test/Debug kiểm tra ngõ ra Output đã được ghi từ hệ thống.
    /// </summary>
    public bool GetOutputBool(string key)
    {
        return _simulatedOutputs.TryGetValue(key, out var val) && val;
    }
}
