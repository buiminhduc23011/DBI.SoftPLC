using System.Collections.Concurrent;
using DBI.Controller.Core.Interfaces;

namespace DBI.Controller.Core.Models;

/// <summary>
/// Triển khai Lock-free Double-buffered Memory Snapshot Image cho Soft PLC Engine.
/// </summary>
public class MemorySnapshot : IMemoryImage
{
    private readonly ConcurrentDictionary<string, bool> _inputBuffer = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _inputSnapshot = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, bool> _outputSnapshot = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _outputBuffer = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, int> _intValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, float> _floatValues = new(StringComparer.OrdinalIgnoreCase);

    public bool GetBool(string key)
    {
        if (_inputSnapshot.TryGetValue(key, out var val))
            return val;

        if (_outputSnapshot.TryGetValue(key, out val))
            return val;

        return false;
    }

    public void SetBool(string key, bool value)
    {
        _outputSnapshot[key] = value;
    }

    public int GetInt(string key)
    {
        return _intValues.TryGetValue(key, out var val) ? val : 0;
    }

    public void SetInt(string key, int value)
    {
        _intValues[key] = value;
    }

    public float GetFloat(string key)
    {
        return _floatValues.TryGetValue(key, out var val) ? val : 0f;
    }

    public void SetFloat(string key, float value)
    {
        _floatValues[key] = value;
    }

    public void SwapInputBuffers()
    {
        foreach (var kvp in _inputBuffer)
        {
            _inputSnapshot[kvp.Key] = kvp.Value;
        }
    }

    public void SwapOutputBuffers()
    {
        foreach (var kvp in _outputSnapshot)
        {
            _outputBuffer[kvp.Key] = kvp.Value;
        }
    }

    public void ClearAllOutputs()
    {
        foreach (var key in _outputSnapshot.Keys)
        {
            _outputSnapshot[key] = false;
        }
        foreach (var key in _outputBuffer.Keys)
        {
            _outputBuffer[key] = false;
        }
    }

    /// <summary>
    /// Cho phép Driver nạp dữ liệu Input thô vào InputBuffer.
    /// </summary>
    public void SetRawInputBool(string key, bool value)
    {
        _inputBuffer[key] = value;
    }

    /// <summary>
    /// Cho phép Driver lấy dữ liệu Output đã chốt từ OutputBuffer để gửi xuống phần cứng.
    /// </summary>
    public bool GetRawOutputBool(string key)
    {
        return _outputBuffer.TryGetValue(key, out var val) && val;
    }

    public IReadOnlyDictionary<string, bool> GetAllOutputs()
    {
        return _outputSnapshot;
    }
}
