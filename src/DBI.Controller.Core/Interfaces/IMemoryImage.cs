namespace DBI.Controller.Core.Interfaces;

/// <summary>
/// Quản lý Snapshot Data Image cho I/O trong bộ nhớ (Lock-free double buffering).
/// </summary>
public interface IMemoryImage
{
    bool GetBool(string key);
    void SetBool(string key, bool value);

    int GetInt(string key);
    void SetInt(string key, int value);

    float GetFloat(string key);
    void SetFloat(string key, float value);

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
