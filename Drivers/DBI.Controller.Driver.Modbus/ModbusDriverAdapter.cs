using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Drivers.Modbus.TCP;

namespace DBI.Controller.Driver.Modbus;

public enum ModbusRegisterType
{
    Coil,
    DiscreteInput,
    HoldingRegister,
    InputRegister
}

/// <summary>
/// Driver Adapter bọc <c>ModbusTCPMaster</c> từ <c>DBI.Drivers.Modbus</c>.
/// </summary>
/// <remarks>
/// Cú pháp địa chỉ: <c>&lt;loại&gt;:&lt;số&gt;</c> — ví dụ <c>Coil:10</c>, <c>DI:5</c>,
/// <c>HR:40001</c>, <c>IR:3</c>. Bỏ phần loại thì mặc định theo chiều tag:
/// Input → DiscreteInput, Output → Coil.
/// </remarks>
public class ModbusDriverAdapter : IDriver
{
    public const string DriverTypeId = "DBI.Controller.Driver.Modbus";

    private ModbusTCPMaster? _modbusMaster;

    public string DriverId { get; }
    public string IpAddress { get; set; }
    public int Port { get; set; }
    public byte SlaveId { get; set; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? LastError { get; private set; }

    public ModbusDriverAdapter(
        string driverId = "MODBUS_DRIVER",
        string ipAddress = "127.0.0.1",
        int port = 502,
        byte slaveId = 1)
    {
        DriverId = driverId;
        IpAddress = ipAddress;
        Port = port;
        SlaveId = slaveId;
    }

    public static ModbusDriverAdapter FromSpec(DeviceSpec spec) => new(
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
            if (!TryParseAddress(route, out var registerType, out ushort address)) continue;

            try
            {
                bool[] values = registerType switch
                {
                    ModbusRegisterType.Coil => _modbusMaster.ReadCoils(SlaveId, address, 1),
                    ModbusRegisterType.DiscreteInput => _modbusMaster.ReadDiscreteInputs(SlaveId, address, 1),
                    _ => Array.Empty<bool>()
                };

                if (values.Length > 0)
                {
                    memoryImage.SetRawInput(route.TagName, values[0]);
                }
                else
                {
                    LastError = $"Tag '{route.TagName}': loại thanh ghi {registerType} chưa hỗ trợ đọc Bool.";
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
        if (State != ConnectionState.Connected || _modbusMaster == null)
            return Task.CompletedTask;

        foreach (var route in routes)
        {
            if (route.Direction != TagDirection.Output) continue;
            if (!EnsureBool(route)) continue;
            if (!TryParseAddress(route, out var registerType, out ushort address)) continue;

            if (registerType != ModbusRegisterType.Coil)
            {
                LastError = $"Tag '{route.TagName}': chỉ ghi được vào Coil, không phải {registerType}.";
                continue;
            }

            try
            {
                _modbusMaster.WriteSingleCoil(SlaveId, address, memoryImage.GetRawOutputBool(route.TagName));
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
        _modbusMaster?.Disconnect();
        _modbusMaster?.Dispose();
        State = ConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    /// <summary>B-6: mới hỗ trợ Bool. Báo lỗi rõ thay vì im lặng trả 0 — task 09.0 bổ sung Int/Real.</summary>
    private bool EnsureBool(TagRoute route)
    {
        if (route.DataType == TagDataType.Bool) return true;

        LastError = $"Tag '{route.TagName}' kiểu {route.DataType}: driver Modbus hiện chỉ hỗ trợ Bool.";
        return false;
    }

    /// <summary>
    /// Phân giải cú pháp địa chỉ Modbus. Công khai để Studio kiểm tra địa chỉ ngay lúc kỹ sư gõ
    /// vào Tag Table, thay vì đợi tới lúc chạy mới biết sai.
    /// </summary>
    public bool TryParseAddress(TagRoute route, out ModbusRegisterType registerType, out ushort address)
    {
        registerType = route.Direction == TagDirection.Output
            ? ModbusRegisterType.Coil
            : ModbusRegisterType.DiscreteInput;

        string raw = route.Address.Trim();
        int separator = raw.IndexOf(':');

        if (separator >= 0)
        {
            string prefix = raw[..separator].Trim();
            raw = raw[(separator + 1)..].Trim();

            if (!TryParseRegisterType(prefix, out registerType))
            {
                address = 0;
                LastError = $"Tag '{route.TagName}': loại thanh ghi '{prefix}' không nhận ra " +
                            "(dùng Coil | DI | HR | IR).";
                return false;
            }
        }

        if (ushort.TryParse(raw, out address)) return true;

        LastError = $"Tag '{route.TagName}': địa chỉ '{route.Address}' không đọc được thành số.";
        return false;
    }

    private static bool TryParseRegisterType(string prefix, out ModbusRegisterType registerType)
    {
        switch (prefix.ToUpperInvariant())
        {
            case "COIL": case "C": registerType = ModbusRegisterType.Coil; return true;
            case "DI": case "DISCRETEINPUT": registerType = ModbusRegisterType.DiscreteInput; return true;
            case "HR": case "HOLDINGREGISTER": registerType = ModbusRegisterType.HoldingRegister; return true;
            case "IR": case "INPUTREGISTER": registerType = ModbusRegisterType.InputRegister; return true;
            default: registerType = ModbusRegisterType.Coil; return false;
        }
    }
}
