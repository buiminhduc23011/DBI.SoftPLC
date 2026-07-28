using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Drivers.Delta.PLC;

namespace DBI.Controller.Driver.Delta;

/// <summary>
/// Driver Adapter bọc <c>DeltaClient</c> từ <c>DBI.Drivers.Delta.PLC</c>.
/// </summary>
/// <remarks>
/// Cú pháp địa chỉ theo đúng ký hiệu Delta: <c>X0</c>, <c>Y5</c>, <c>M100</c>.
/// Ghi được vào <c>Y</c> và <c>M</c>; <c>X</c> là ngõ vào vật lý nên chỉ đọc.
/// </remarks>
public class DeltaPlcDriverAdapter : IDriver
{
    public const string DriverTypeId = "DBI.Controller.Driver.Delta";

    private DeltaClient? _client;

    public string DriverId { get; }
    public string IpAddress { get; set; }
    public int Port { get; set; }
    public byte SlaveId { get; set; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? LastError { get; private set; }

    public DeltaPlcDriverAdapter(
        string driverId = "DELTA_PLC_DRIVER",
        string ipAddress = "192.168.1.5",
        int port = 502,
        byte slaveId = 1)
    {
        DriverId = driverId;
        IpAddress = ipAddress;
        Port = port;
        SlaveId = slaveId;
    }

    public static DeltaPlcDriverAdapter FromSpec(DeviceSpec spec) => new(
        spec.Name,
        spec.Get("ip", "192.168.1.5"),
        spec.GetInt("port", 502),
        (byte)spec.GetInt("slaveId", 1));

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            State = ConnectionState.Connecting;
            _client = new DeltaClient(IpAddress, Port, DeltaConnectionType.TcpDVP, SlaveId);
            _client.Connect();
            State = ConnectionState.Connected;
            LastError = null;
        }
        catch (Exception ex)
        {
            State = ConnectionState.Faulted;
            LastError = ex.Message;
            throw;
        }

        return Task.CompletedTask;
    }

    public Task ReadInputsAsync(
        IMemoryImage memoryImage,
        IReadOnlyList<TagRoute> routes,
        CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected || _client == null)
            return Task.CompletedTask;

        foreach (var route in routes)
        {
            if (route.Direction != TagDirection.Input) continue;
            if (!TryParseAddress(route, out char area, out int address)) continue;

            try
            {
                if (route.DataType == TagDataType.Bool)
                {
                    bool[] values = area switch { 'X' => _client.ReadX(address, 1), 'Y' => _client.ReadY(address, 1), 'M' => _client.ReadM(address, 1), _ => Array.Empty<bool>() };
                    if (values.Length > 0) memoryImage.SetRawInput(route.TagName, values[0]);
                }
                else if (area == 'D')
                {
                    if (route.DataType == TagDataType.Int) memoryImage.SetRawInput(route.TagName, _client.ReadDInt(address));
                    else memoryImage.SetRawInput(route.TagName, _client.ReadFloat(address));
                }
                else
                    LastError = $"Tag '{route.TagName}': vùng nhớ '{area}' chưa hỗ trợ đọc Bool.";
            }
            catch (Exception ex)
            {
                State = ConnectionState.Faulted;
                LastError = $"Đọc tag '{route.TagName}' ({route.Address}) lỗi: {ex.Message}";
            }
        }

        return Task.CompletedTask;
    }

    public Task WriteOutputsAsync(
        IMemoryImage memoryImage,
        IReadOnlyList<TagRoute> routes,
        CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected || _client == null)
            return Task.CompletedTask;

        foreach (var route in routes)
        {
            if (route.Direction != TagDirection.Output) continue;
            if (!TryParseAddress(route, out char area, out int address)) continue;

            try
            {
                if (route.DataType == TagDataType.Int && area == 'D') { _client.WriteDInt(address, memoryImage.GetRawOutputInt(route.TagName)); continue; }
                if (route.DataType == TagDataType.Real && area == 'D') { _client.WriteFloat(address, memoryImage.GetRawOutputFloat(route.TagName)); continue; }
                bool value = memoryImage.GetRawOutputBool(route.TagName);
                switch (area)
                {
                    case 'Y': _client.WriteY(address, new[] { value }); break;
                    case 'M': _client.WriteM(address, new[] { value }); break;
                    default:
                        LastError = $"Tag '{route.TagName}': vùng '{area}' chỉ đọc, ghi được vào Y hoặc M.";
                        break;
                }
            }
            catch (Exception ex)
            {
                State = ConnectionState.Faulted;
                LastError = $"Ghi tag '{route.TagName}' ({route.Address}) lỗi: {ex.Message}";
            }
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _client?.Disconnect();
        _client?.Dispose();
        State = ConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    /// <summary>B-6: mới hỗ trợ Bool. Báo lỗi rõ thay vì im lặng trả 0 — task 09.0 bổ sung Int/Real.</summary>
    private bool EnsureBool(TagRoute route)
    {
        if (route.DataType == TagDataType.Bool) return true;

        LastError = $"Tag '{route.TagName}' kiểu {route.DataType}: driver Delta hiện chỉ hỗ trợ Bool.";
        return false;
    }

    /// <summary>
    /// Tách ký hiệu Delta: <c>M100</c> → ('M', 100). Công khai để Studio kiểm tra địa chỉ
    /// ngay lúc kỹ sư gõ vào Tag Table.
    /// </summary>
    public bool TryParseAddress(TagRoute route, out char area, out int address)
    {
        area = '\0';
        address = 0;

        string raw = route.Address.Trim();

        if (raw.Length >= 2 && char.IsAsciiLetter(raw[0]) && int.TryParse(raw[1..], out address))
        {
            area = char.ToUpperInvariant(raw[0]);
            return true;
        }

        LastError = $"Tag '{route.TagName}': địa chỉ '{route.Address}' không đúng ký hiệu Delta " +
                    "(ví dụ X0, Y5, M100).";
        return false;
    }
}
