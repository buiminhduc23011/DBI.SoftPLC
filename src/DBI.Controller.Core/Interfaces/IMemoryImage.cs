namespace DBI.Controller.Core.Interfaces;

/// <summary>
/// Quản lý Snapshot Data Image cho I/O trong bộ nhớ (Lock-free double buffering).
/// </summary>
public interface IMemoryImage
{
    // ── API cho chương trình người dùng (đọc Snapshot, ghi Output State) ────────

    bool GetBool(string key);
    void SetBool(string key, bool value);

    int GetInt(string key);
    void SetInt(string key, int value);

    float GetFloat(string key);
    void SetFloat(string key, float value);

    // ── API thô cho Driver ─────────────────────────────────────────────────────
    // Nằm trên interface (không phải trên MemorySnapshot) để driver KHÔNG phải ép
    // kiểu xuống lớp cụ thể. Trước đây driver ép kiểu rồi im lặng return khi hỏng,
    // nghĩa là mọi decorator IMemoryImage (ví dụ Force layer) sẽ làm driver chết lặng.

    /// <summary>Driver nạp giá trị Input thô vào InputBuffer.</summary>
    void SetRawInput(string key, bool value);

    /// <inheritdoc cref="SetRawInput(string, bool)"/>
    void SetRawInput(string key, int value);

    /// <inheritdoc cref="SetRawInput(string, bool)"/>
    void SetRawInput(string key, float value);

    /// <summary>Driver lấy Output đã chốt từ OutputBuffer để gửi xuống phần cứng.</summary>
    bool GetRawOutputBool(string key);

    /// <inheritdoc cref="GetRawOutputBool(string)"/>
    int GetRawOutputInt(string key);

    /// <inheritdoc cref="GetRawOutputBool(string)"/>
    float GetRawOutputFloat(string key);

    /// <summary>
    /// Liệt kê toàn bộ Output đã chốt (cả bool/int/float) cho driver ghi hàng loạt.
    /// </summary>
    IReadOnlyDictionary<string, object> GetRawOutputs();

    /// <summary>
    /// Liệt kê toàn bộ tag đang thấy được (Input Snapshot + Output State) cho monitoring.
    /// Không dùng trên scan thread — có cấp phát bộ nhớ.
    /// </summary>
    IReadOnlyDictionary<string, object> SnapshotAll();

    // ── Vòng đời chu kỳ quét ───────────────────────────────────────────────────

    /// <summary>
    /// Đổi buffer Snapshot giữa Input Driver Buffer và Input Execution Snapshot.
    /// </summary>
    void SwapInputBuffers();

    /// <summary>
    /// Đổi buffer Snapshot giữa Output Execution Snapshot và Output Driver Buffer.
    /// </summary>
    void SwapOutputBuffers();

    /// <summary>
    /// Xóa sạch tất cả Output về Safe State (False / 0).
    /// </summary>
    void ClearAllOutputs();
}
