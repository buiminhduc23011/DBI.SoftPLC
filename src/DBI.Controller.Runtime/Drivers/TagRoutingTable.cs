using DBI.Controller.Core.Models;

namespace DBI.Controller.Runtime.Drivers;

/// <summary>
/// Bảng định tuyến tag → thiết bị (B-5).
/// </summary>
/// <remarks>
/// Đây là mắt xích còn thiếu giữa Tag Table của Studio và driver. Trước đây
/// <c>DriverManager</c> đưa <b>cả</b> memory image cho <b>mọi</b> driver và mỗi driver tự đoán key
/// nào là của mình — nghĩa là hai thiết bị có tag trùng tên sẽ giẫm lên nhau mà không ai biết.
/// </remarks>
public class TagRoutingTable
{
    private static readonly IReadOnlyList<TagRoute> Empty = Array.Empty<TagRoute>();

    private Dictionary<string, IReadOnlyList<TagRoute>> _byDevice =
        new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<TagRoute> _all = Empty;

    /// <summary>Toàn bộ route đang nạp, kể cả tag <c>Memory</c>.</summary>
    public IReadOnlyList<TagRoute> All => Volatile.Read(ref _all);

    /// <summary>
    /// Thay toàn bộ bảng bằng một lượt gán tham chiếu — deploy giữa chừng không để ai
    /// nhìn thấy bảng nửa cũ nửa mới.
    /// </summary>
    public void Load(IEnumerable<TagRoute> routes)
    {
        var all = routes.ToList();

        var byDevice = all
            .Where(r => r.Direction != TagDirection.Memory && !string.IsNullOrWhiteSpace(r.Device))
            .GroupBy(r => r.Device, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TagRoute>)g.ToList(),
                StringComparer.OrdinalIgnoreCase);

        Volatile.Write(ref _byDevice, byDevice);
        Volatile.Write(ref _all, all);
    }

    public void Clear() => Load(Empty);

    /// <summary>Route của riêng một thiết bị. Thiết bị không có tag nào → danh sách rỗng.</summary>
    public IReadOnlyList<TagRoute> GetRoutesForDevice(string deviceName) =>
        Volatile.Read(ref _byDevice).TryGetValue(deviceName, out var routes) ? routes : Empty;

    /// <summary>Tên mọi tag đang khai báo — cho monitoring (phase-10) biết tag nào hợp lệ.</summary>
    public IReadOnlyCollection<string> TagNames =>
        All.Select(r => r.TagName).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
