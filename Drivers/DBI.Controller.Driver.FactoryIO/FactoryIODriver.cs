using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Drivers.Modbus.TCP;

namespace DBI.Controller.Driver.FactoryIO;

public class FactoryIOTagConfig
{
    public string TagName { get; set; } = string.Empty;
    public ushort ModbusAddress { get; set; }
    public bool IsOutput { get; set; }
}

/// <summary>
/// Factory I/O 3D Simulator Driver Adapter (kết nối qua Modbus TCP Server của Factory I/O sử dụng DBI.Drivers.Modbus).
/// </summary>
public class FactoryIODriver : IDriver
{
    private ModbusTCPMaster? _modbusMaster;

    public string DriverId { get; }
    public string IpAddress { get; set; }
    public int Port { get; set; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public List<FactoryIOTagConfig> TagMappings { get; } = new();

    public FactoryIODriver(string driverId = "FACTORY_IO_DRIVER", string ipAddress = "127.0.0.1", int port = 502)
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
        if (State != ConnectionState.Connected || _modbusMaster == null || memoryImage is not MemorySnapshot snapshot)
            return Task.CompletedTask;

        foreach (var map in TagMappings.Where(t => !t.IsOutput))
        {
            try
            {
                bool[] inputs = _modbusMaster.ReadDiscreteInputs(1, map.ModbusAddress, 1);
                if (inputs.Length > 0) snapshot.SetRawInputBool(map.TagName, inputs[0]);
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
        if (State != ConnectionState.Connected || _modbusMaster == null || memoryImage is not MemorySnapshot snapshot)
            return Task.CompletedTask;

        foreach (var map in TagMappings.Where(t => t.IsOutput))
        {
            try
            {
                bool val = snapshot.GetRawOutputBool(map.TagName);
                _modbusMaster.WriteSingleCoil(1, map.ModbusAddress, val);
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
