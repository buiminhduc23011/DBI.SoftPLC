using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Drivers.Modbus.TCP;

namespace DBI.Controller.Driver.FactoryIO;

/// <summary>
/// Factory I/O 3D Simulator Driver Adapter — kết nối qua Modbus TCP Server của Factory I/O,
/// dùng <c>ModbusTCPMaster</c> từ <c>DBI.Drivers.Modbus</c>.
/// </summary>
/// <remarks>
/// Địa chỉ tag là số thứ tự điểm I/O của Factory I/O: <c>0</c>, <c>1</c>, <c>Input_0</c>,
/// <c>Output_3</c> — phần chữ chỉ để đọc cho dễ, chỉ phần số có ý nghĩa.
/// </remarks>
public class FactoryIODriver : IDriver
{
    public const string DriverTypeId = "DBI.Controller.Driver.FactoryIO";

    private ModbusTCPMaster? _modbusMaster;

    public string DriverId { get; }
    public string IpAddress { get; set; }
    public int Port { get; set; }
    public byte SlaveId { get; set; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? LastError { get; private set; }

    public FactoryIODriver(
        string driverId = "FACTORY_IO_DRIVER",
        string ipAddress = "127.0.0.1",
        int port = 502,
        byte slaveId = 1)
    {
        DriverId = driverId;
        IpAddress = ipAddress;
        Port = port;
        SlaveId = slaveId;
    }

    public static FactoryIODriver FromSpec(DeviceSpec spec) => new(
        spec.Name,
        spec.Get("ip", "127.0.0.1"),
        spec.GetInt("port", 502),
        (byte)spec.GetInt("slaveId", 1));

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            State = ConnectionState.Connecting;
            _modbusMaster = new ModbusTCPMaster(IpAddress, Port);
            _modbusMaster.Connect();
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
        if (State != ConnectionState.Connected || _modbusMaster == null)
            return Task.CompletedTask;

        foreach (var route in routes)
        {
            if (route.Direction != TagDirection.Input) continue;
            if (!EnsureBool(route)) continue;
            if (!TryParseAddress(route, out ushort address)) continue;

            try
            {
                bool[] inputs = _modbusMaster.ReadDiscreteInputs(SlaveId, address, 1);
                if (inputs.Length > 0) memoryImage.SetRawInput(route.TagName, inputs[0]);
            }
            catch (Exception ex)
            {
                State = ConnectionState.Faulted;
                LastError = $"Đọc tag '{route.TagName}' (địa chỉ {address}) lỗi: {ex.Message}";
            }
        }

        return Task.CompletedTask;
    }

    public Task WriteOutputsAsync(
        IMemoryImage memoryImage,
        IReadOnlyList<TagRoute> routes,
        CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected || _modbusMaster == null)
            return Task.CompletedTask;

        foreach (var route in routes)
        {
            if (route.Direction != TagDirection.Output) continue;
            if (!EnsureBool(route)) continue;
            if (!TryParseAddress(route, out ushort address)) continue;

            try
            {
                _modbusMaster.WriteSingleCoil(SlaveId, address, memoryImage.GetRawOutputBool(route.TagName));
            }
            catch (Exception ex)
            {
                State = ConnectionState.Faulted;
                LastError = $"Ghi tag '{route.TagName}' (địa chỉ {address}) lỗi: {ex.Message}";
            }
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _modbusMaster?.Disconnect();
        _modbusMaster?.Dispose();
        State = ConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    /// <summary>
    /// B-6: driver này mới hỗ trợ <c>Bool</c>. Báo lỗi rõ thay vì im lặng trả 0 — task 09.0 bổ sung Int/Real.
    /// </summary>
    private bool EnsureBool(TagRoute route)
    {
        if (route.DataType == TagDataType.Bool) return true;

        LastError = $"Tag '{route.TagName}' kiểu {route.DataType}: driver Factory I/O hiện chỉ hỗ trợ Bool.";
        return false;
    }

    /// <summary>Lấy phần số trong địa chỉ: <c>Input_7</c> → 7, <c>7</c> → 7.</summary>
    private bool TryParseAddress(TagRoute route, out ushort address)
    {
        string digits = new(route.Address.Where(char.IsAsciiDigit).ToArray());

        if (ushort.TryParse(digits, out address)) return true;

        LastError = $"Tag '{route.TagName}': địa chỉ '{route.Address}' không đọc được thành số.";
        return false;
    }
}
