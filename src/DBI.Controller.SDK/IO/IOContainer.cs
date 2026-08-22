#nullable enable
using System;
using DBI.Controller.Core.Interfaces;

namespace DBI.Controller.SDK.IO;

/// <summary>
/// Cổng truy cập I/O của chương trình điều khiển.
/// </summary>
/// <remarks>
/// <para><c>partial</c> là chủ ý: phase-06 sinh <c>Generated/IO.g.cs</c> từ Tag Table, bổ sung các
/// property mạnh kiểu (<c>IO.StartButton</c>) vào chính class này. Nhờ vậy gõ sai tên tag là lỗi biên
/// dịch, và IntelliSense gợi ý được đúng danh sách tag đã khai báo.</para>
///
/// <para>Trước đây class này kế thừa <c>DynamicObject</c>: <c>TryGetMember</c> luôn gọi <c>GetBool</c>
/// và luôn trả <c>true</c>, nên tag sai chính tả im lặng trả <c>false</c> và tag <c>Real</c> đọc ra
/// <c>bool</c>. Đã gỡ bỏ.</para>
/// </remarks>
public partial class IOContainer
{
    private readonly IMemoryImage _memoryImage;

    public IOContainer(IMemoryImage memoryImage)
    {
        _memoryImage = memoryImage ?? throw new ArgumentNullException(nameof(memoryImage));
    }

    /// <summary>
    /// Truy cập tag <c>Bool</c> theo tên. Dùng khi chưa có Tag Table sinh property mạnh kiểu.
    /// </summary>
    public bool this[string key]
    {
        get => _memoryImage.GetBool(key);
        set => _memoryImage.SetBool(key, value);
    }

    public bool GetBool(string key) => _memoryImage.GetBool(key);
    public void SetBool(string key, bool value) => _memoryImage.SetBool(key, value);

    public int GetInt(string key) => _memoryImage.GetInt(key);
    public void SetInt(string key, int value) => _memoryImage.SetInt(key, value);

    public float GetFloat(string key) => _memoryImage.GetFloat(key);
    public void SetFloat(string key, float value) => _memoryImage.SetFloat(key, value);
}
