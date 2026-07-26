using System.Collections.Concurrent;
using DBI.Controller.Core.Interfaces;

namespace DBI.Controller.Core.Models;

/// <summary>
/// Triển khai Lock-free Double-buffered Memory Snapshot Image cho Soft PLC Engine.
/// </summary>
/// <remarks>
/// <para><b>Input</b> — double-buffer thật: driver ghi vào <c>*Write</c>, logic đọc <c>*Read</c>.
/// <see cref="SwapInputBuffers"/> hoán đổi <i>tham chiếu</i> (O(1), không cấp phát), nên giá trị
/// driver ghi giữa chu kỳ không bao giờ lọt vào snapshot đang thực thi.</para>
///
/// <para>⚠️ <b>Ràng buộc của mô hình double-buffer:</b> driver phải ghi <i>đủ</i> mọi tag nó quản lý
/// ở <i>mỗi</i> chu kỳ. Buffer ghi sau khi hoán đổi chứa dữ liệu của 2 chu kỳ trước; tag nào driver
/// bỏ không ghi sẽ đọc ra giá trị cũ đó. Đây là ngữ nghĩa process-image kinh điển của PLC.</para>
///
/// <para><b>Output</b> — <i>không</i> hoán đổi tham chiếu, và đây là chủ ý. Output là trạng thái
/// <i>giữ</i> (retentive) của chương trình: <c>if (start) IO.Motor = true;</c> phải còn <c>true</c> ở
/// chu kỳ sau khi không ai ghi lại. Hoán đổi tham chiếu sẽ trả logic về buffer của 2 chu kỳ trước và
/// phá vỡ latch — một lỗi an toàn máy móc. Vì vậy <see cref="SwapOutputBuffers"/> công bố (publish)
/// OutputState sang OutputBuffer. Đây là O(n) nhưng ghi vào dictionary có sẵn, <b>không cấp phát</b>,
/// nên không tạo áp lực GC lên scan thread.</para>
/// </remarks>
public class MemorySnapshot : IMemoryImage
{
    // ── INPUT: double-buffer, hoán đổi tham chiếu ────────────────────────────────
    private ConcurrentDictionary<string, bool> _inputWriteBool = NewMap<bool>();
    private ConcurrentDictionary<string, bool> _inputReadBool = NewMap<bool>();

    private ConcurrentDictionary<string, int> _inputWriteInt = NewMap<int>();
    private ConcurrentDictionary<string, int> _inputReadInt = NewMap<int>();

    private ConcurrentDictionary<string, float> _inputWriteFloat = NewMap<float>();
    private ConcurrentDictionary<string, float> _inputReadFloat = NewMap<float>();

    // ── OUTPUT: State (logic ghi/đọc lại) → publish → Buffer (driver đọc) ────────
    private readonly ConcurrentDictionary<string, bool> _outputStateBool = NewMap<bool>();
    private readonly ConcurrentDictionary<string, bool> _outputBufferBool = NewMap<bool>();

    private readonly ConcurrentDictionary<string, int> _outputStateInt = NewMap<int>();
    private readonly ConcurrentDictionary<string, int> _outputBufferInt = NewMap<int>();

    private readonly ConcurrentDictionary<string, float> _outputStateFloat = NewMap<float>();
    private readonly ConcurrentDictionary<string, float> _outputBufferFloat = NewMap<float>();

    private static ConcurrentDictionary<string, T> NewMap<T>() => new(StringComparer.OrdinalIgnoreCase);

    // ── Chương trình người dùng ──────────────────────────────────────────────────

    public bool GetBool(string key)
    {
        if (Volatile.Read(ref _inputReadBool).TryGetValue(key, out var val))
            return val;

        return _outputStateBool.TryGetValue(key, out val) && val;
    }

    public void SetBool(string key, bool value) => _outputStateBool[key] = value;

    public int GetInt(string key)
    {
        if (Volatile.Read(ref _inputReadInt).TryGetValue(key, out var val))
            return val;

        return _outputStateInt.TryGetValue(key, out val) ? val : 0;
    }

    public void SetInt(string key, int value) => _outputStateInt[key] = value;

    public float GetFloat(string key)
    {
        if (Volatile.Read(ref _inputReadFloat).TryGetValue(key, out var val))
            return val;

        return _outputStateFloat.TryGetValue(key, out val) ? val : 0f;
    }

    public void SetFloat(string key, float value) => _outputStateFloat[key] = value;

    // ── Driver ───────────────────────────────────────────────────────────────────

    public void SetRawInput(string key, bool value) => Volatile.Read(ref _inputWriteBool)[key] = value;
    public void SetRawInput(string key, int value) => Volatile.Read(ref _inputWriteInt)[key] = value;
    public void SetRawInput(string key, float value) => Volatile.Read(ref _inputWriteFloat)[key] = value;

    public bool GetRawOutputBool(string key) => _outputBufferBool.TryGetValue(key, out var val) && val;
    public int GetRawOutputInt(string key) => _outputBufferInt.TryGetValue(key, out var val) ? val : 0;
    public float GetRawOutputFloat(string key) => _outputBufferFloat.TryGetValue(key, out var val) ? val : 0f;

    public IReadOnlyDictionary<string, object> GetRawOutputs()
    {
        var all = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in _outputBufferBool) all[kvp.Key] = kvp.Value;
        foreach (var kvp in _outputBufferInt) all[kvp.Key] = kvp.Value;
        foreach (var kvp in _outputBufferFloat) all[kvp.Key] = kvp.Value;

        return all;
    }

    public IReadOnlyDictionary<string, object> SnapshotAll()
    {
        var all = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // Output trước, Input sau — Input Snapshot thắng, đúng thứ tự ưu tiên của GetBool/GetInt/GetFloat.
        foreach (var kvp in _outputStateBool) all[kvp.Key] = kvp.Value;
        foreach (var kvp in _outputStateInt) all[kvp.Key] = kvp.Value;
        foreach (var kvp in _outputStateFloat) all[kvp.Key] = kvp.Value;

        foreach (var kvp in Volatile.Read(ref _inputReadBool)) all[kvp.Key] = kvp.Value;
        foreach (var kvp in Volatile.Read(ref _inputReadInt)) all[kvp.Key] = kvp.Value;
        foreach (var kvp in Volatile.Read(ref _inputReadFloat)) all[kvp.Key] = kvp.Value;

        return all;
    }

    // ── Vòng đời chu kỳ quét ─────────────────────────────────────────────────────

    public void SwapInputBuffers()
    {
        SwapInputBuffer(ref _inputWriteBool, ref _inputReadBool);
        SwapInputBuffer(ref _inputWriteInt, ref _inputReadInt);
        SwapInputBuffer(ref _inputWriteFloat, ref _inputReadFloat);
    }

    /// <summary>
    /// Hoán đổi tham chiếu Write ↔ Read. Thứ tự quan trọng: chuyển hướng driver sang buffer tái sử dụng
    /// TRƯỚC, rồi mới công bố buffer vừa ghi cho logic — để không có khoảnh khắc nào driver và logic
    /// cùng trỏ vào một dictionary.
    /// </summary>
    private static void SwapInputBuffer<T>(
        ref ConcurrentDictionary<string, T> write,
        ref ConcurrentDictionary<string, T> read)
    {
        var recycled = Volatile.Read(ref read);
        var fresh = Interlocked.Exchange(ref write, recycled);
        Volatile.Write(ref read, fresh);
    }

    public void SwapOutputBuffers()
    {
        PublishOutputs(_outputStateBool, _outputBufferBool);
        PublishOutputs(_outputStateInt, _outputBufferInt);
        PublishOutputs(_outputStateFloat, _outputBufferFloat);
    }

    private static void PublishOutputs<T>(
        ConcurrentDictionary<string, T> state,
        ConcurrentDictionary<string, T> buffer)
    {
        foreach (var kvp in state)
            buffer[kvp.Key] = kvp.Value;
    }

    public void ClearAllOutputs()
    {
        ResetToSafeState(_outputStateBool, false);
        ResetToSafeState(_outputBufferBool, false);
        ResetToSafeState(_outputStateInt, 0);
        ResetToSafeState(_outputBufferInt, 0);
        ResetToSafeState(_outputStateFloat, 0f);
        ResetToSafeState(_outputBufferFloat, 0f);
    }

    private static void ResetToSafeState<T>(ConcurrentDictionary<string, T> map, T safeValue)
    {
        foreach (var key in map.Keys)
            map[key] = safeValue;
    }

    // ── Tương thích ngược ────────────────────────────────────────────────────────

    [Obsolete("Dùng SetRawInput(key, value) trên IMemoryImage — driver không cần ép kiểu nữa.")]
    public void SetRawInputBool(string key, bool value) => SetRawInput(key, value);

    [Obsolete("Dùng GetRawOutputs() trên IMemoryImage — driver không cần ép kiểu nữa.")]
    public IReadOnlyDictionary<string, bool> GetAllOutputs() => _outputBufferBool;
}
