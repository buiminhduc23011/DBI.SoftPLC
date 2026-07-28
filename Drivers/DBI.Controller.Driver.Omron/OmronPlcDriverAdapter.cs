using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Drivers.Omron;

namespace DBI.Controller.Driver.Omron;

/// <summary>
/// Driver Adapter bọc <c>OmronClient</c> từ <c>DBI.Drivers.Omron</c> (giao thức FINS).
/// </summary>
/// <remarks>Cú pháp địa chỉ theo ký hiệu Omron: <c>CIO100</c>, <c>WR5</c>, <c>HR10</c>.</remarks>
public class OmronPlcDriverAdapter : IDriver
{
    public const string DriverTypeId = "DBI.Controller.Driver.Omron";

    private static readonly string[] KnownAreas = { "CIO", "WR", "HR", "DM" };

    private OmronClient? _client;

    public string DriverId { get; }
    public string IpAddress { get; set; }
    public int Port { get; set; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? LastError { get; private set; }

    public OmronPlcDriverAdapter(
        string driverId = "OMRON_PLC_DRIVER",
        string ipAddress = "192.168.1.10",
        int port = 9600)
    {
        DriverId = driverId;
        IpAddress = ipAddress;
        Port = port;
    }

    public static OmronPlcDriverAdapter FromSpec(DeviceSpec spec) => new(
        spec.Name,
        spec.Get("ip", "192.168.1.10"),
        spec.GetInt("port", 9600));

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            State = ConnectionState.Connecting;
            _client = new OmronClient(IpAddress, Port);
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
            if (!TryParseAddress(route, out string area, out int address)) continue;

            try
            {
                if (route.DataType == TagDataType.Bool)
                {
                    bool[] values = area switch { "CIO" => _client.ReadCIO(address, 1), "WR" => _client.ReadWR(address, 1), "HR" => _client.ReadHR(address, 1), _ => Array.Empty<bool>() };
                    if (values.Length > 0) memoryImage.SetRawInput(route.TagName, values[0]);
                }
                else if (area == "DM")
                {
                    if (route.DataType == TagDataType.Int) memoryImage.SetRawInput(route.TagName, _client.ReadDMInt(address));
                    else memoryImage.SetRawInput(route.TagName, _client.ReadDMFloat(address));
                }
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
            if (!TryParseAddress(route, out string area, out int address)) continue;

            try
            {
                if (route.DataType == TagDataType.Int && area == "DM") { _client.WriteDMInt(address, memoryImage.GetRawOutputInt(route.TagName)); continue; }
                if (route.DataType == TagDataType.Real && area == "DM") { _client.WriteDMFloat(address, memoryImage.GetRawOutputFloat(route.TagName)); continue; }
                bool[] value = { memoryImage.GetRawOutputBool(route.TagName) };
                switch (area)
                {
                    case "CIO": _client.WriteCIO(address, value); break;
                    case "WR": _client.WriteWR(address, value); break;
                    case "HR": _client.WriteHR(address, value); break;
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

        LastError = $"Tag '{route.TagName}' kiểu {route.DataType}: driver Omron hiện chỉ hỗ trợ Bool.";
        return false;
    }

    /// <summary>
    /// Tách ký hiệu Omron: <c>CIO100</c> → ("CIO", 100). Công khai để Studio kiểm tra địa chỉ
    /// ngay lúc kỹ sư gõ vào Tag Table.
    /// </summary>
    public bool TryParseAddress(TagRoute route, out string area, out int address)
    {
        string raw = route.Address.Trim().ToUpperInvariant();

        foreach (string candidate in KnownAreas)
        {
            if (raw.StartsWith(candidate, StringComparison.Ordinal) &&
                int.TryParse(raw[candidate.Length..], out address))
            {
                if (candidate == "DM" && route.DataType == TagDataType.Bool)
                {
                    area = "";
                    LastError = $"Tag '{route.TagName}': DM chỉ dùng cho Int/Real, không phải Bool (địa chỉ hợp lệ ví dụ CIO100).";
                    return false;
                }
                area = candidate;
                return true;
            }
        }

        area = "";
        address = 0;
        LastError = $"Tag '{route.TagName}': địa chỉ '{route.Address}' không đúng ký hiệu Omron " +
                    "(ví dụ CIO100, WR5, HR10).";
        return false;
    }
}
