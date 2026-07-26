namespace DBI.Controller.Core.Models;

/// <summary>Kiểu dữ liệu của tag. Ánh xạ thẳng sang ba kiểu của <c>IMemoryImage</c>.</summary>
public enum TagDataType { Bool, Int, Real }

/// <summary>Chiều của tag. <see cref="Memory"/> là biến nội bộ, không gắn thiết bị nào.</summary>
public enum TagDirection { Input, Output, Memory }

/// <summary>
/// Một tag trỏ tới địa chỉ thô trên một thiết bị. Đây là mắt xích giữa Tag Table của Studio
/// và driver — thay cho việc mỗi driver tự đoán key nào là của mình.
/// </summary>
/// <param name="TagName">Tên tag, cũng là key trong <see cref="IMemoryImage"/>.</param>
/// <param name="DataType">Bool | Int | Real.</param>
/// <param name="Direction">Input | Output | Memory.</param>
/// <param name="Device">Tên thiết bị. Rỗng khi <paramref name="Direction"/> = Memory.</param>
/// <param name="Address">Địa chỉ thô theo cách hiểu của driver: <c>Input_0</c>, <c>40001</c>, <c>D100</c>, <c>X0</c>.</param>
public record TagRoute(
    string TagName,
    TagDataType DataType,
    TagDirection Direction,
    string Device,
    string Address);

/// <summary>Cấu hình một thiết bị để Runtime dựng driver tương ứng.</summary>
/// <param name="Name">Tên thiết bị — khớp với <see cref="TagRoute.Device"/>.</param>
/// <param name="DriverType">Định danh loại driver, ví dụ <c>DBI.Controller.Driver.Modbus</c>.</param>
/// <param name="Settings">Tham số kết nối: <c>ip</c>, <c>port</c>, <c>slaveId</c>...</param>
public record DeviceSpec(
    string Name,
    string DriverType,
    Dictionary<string, string> Settings)
{
    public string Get(string key, string fallback = "") =>
        Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    public int GetInt(string key, int fallback) =>
        int.TryParse(Get(key), out var value) ? value : fallback;
}
