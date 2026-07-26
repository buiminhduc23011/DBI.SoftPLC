using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Drivers.Modbus.TCP;

namespace DBI.Controller.Driver.Modbus;

public class ModbusTagConfig
{
    public string TagName { get; set; } = string.Empty;
    public byte SlaveId { get; set; } = 1;
    public ushort Address { get; set; }
    public ModbusRegisterType RegisterType { get; set; } = ModbusRegisterType.Coil;
}

public enum ModbusRegisterType
{
    Coil,
    DiscreteInput,
    HoldingRegister,
    InputRegister
}

/// <summary>
/// Driver Adapter bọc ModbusTCPMaster từ DBI.Drivers.Modbus.
/// </summary>
public class ModbusDriverAdapter : IDriver
{
    private ModbusTCPMaster? _modbusMaster;

    public string DriverId { get; }
    public string IpAddress { get; set; }
    public int Port { get; set; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public List<ModbusTagConfig> InputMappings { get; } = new();
    public List<ModbusTagConfig> OutputMappings { get; } = new();

    public ModbusDriverAdapter(string driverId = "MODBUS_DRIVER", string ipAddress = "127.0.0.1", int port = 502)
    {
        DriverId = driverId;
        IpAddress = ipAddress;
        Port = port;
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            State = ConnectionState.Connecting;
            _modbusMaster = new ModbusTCPMaster(IpAddress, Port);
            _modbusMaster.Connect();
            State = ConnectionState.Connected;
        }
        catch
        {
            State = ConnectionState.Faulted;
            throw;
        }

        return Task.CompletedTask;
    }

    public Task ReadInputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected || _modbusMaster == null)
            return Task.CompletedTask;

        foreach (var map in InputMappings)
        {
            try
            {
                if (map.RegisterType == ModbusRegisterType.DiscreteInput)
                {
                    bool[] inputs = _modbusMaster.ReadDiscreteInputs(map.SlaveId, map.Address, 1);
                    if (inputs.Length > 0) memoryImage.SetRawInput(map.TagName, inputs[0]);
                }
                else if (map.RegisterType == ModbusRegisterType.Coil)
                {
                    bool[] coils = _modbusMaster.ReadCoils(map.SlaveId, map.Address, 1);
                    if (coils.Length > 0) memoryImage.SetRawInput(map.TagName, coils[0]);
                }
            }
            catch
            {
                State = ConnectionState.Faulted;
            }
        }

        return Task.CompletedTask;
    }

    public Task WriteOutputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default)
    {
        if (State != ConnectionState.Connected || _modbusMaster == null)
            return Task.CompletedTask;

        foreach (var map in OutputMappings)
        {
            try
            {
                if (map.RegisterType == ModbusRegisterType.Coil)
                {
                    bool val = memoryImage.GetRawOutputBool(map.TagName);
                    _modbusMaster.WriteSingleCoil(map.SlaveId, map.Address, val);
                }
            }
            catch
            {
                State = ConnectionState.Faulted;
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
}
