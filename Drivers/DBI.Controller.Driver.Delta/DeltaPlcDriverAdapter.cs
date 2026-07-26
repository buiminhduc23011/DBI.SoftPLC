using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Drivers.Delta.PLC;

namespace DBI.Controller.Driver.Delta;

public class DeltaTagConfig
{
    public string TagName { get; set; } = string.Empty;
    public string RegisterType { get; set; } = "X"; // X, Y, M, D, S, C, T
    public int Address { get; set; }
}

/// <summary>
/// Driver Adapter bọc DeltaClient từ DBI.Drivers.Delta.PLC.
/// </summary>
public class DeltaPlcDriverAdapter : IDriver
{
    private DeltaClient? _client;

    public string DriverId { get; }
    public string IpAddress { get; set; }
    public int Port { get; set; }
    public byte SlaveId { get; set; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public List<DeltaTagConfig> InputMappings { get; } = new();
    public List<DeltaTagConfig> OutputMappings { get; } = new();

    public DeltaPlcDriverAdapter(string driverId = "DELTA_PLC_DRIVER", string ipAddress = "192.168.1.5", int port = 502, byte slaveId = 1)
    {
        DriverId = driverId;
        IpAddress = ipAddress;
        Port = port;
        SlaveId = slaveId;
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            State = ConnectionState.Connecting;
            _client = new DeltaClient(IpAddress, Port, DeltaConnectionType.TcpDVP, SlaveId);
            _client.Connect();
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
        if (State != ConnectionState.Connected || _client == null)
            return Task.CompletedTask;

        foreach (var map in InputMappings)
        {
            try
            {
                if (map.RegisterType.Equals("X", StringComparison.OrdinalIgnoreCase))
                {
                    bool[] vals = _client.ReadX(map.Address, 1);
                    if (vals.Length > 0) memoryImage.SetRawInput(map.TagName, vals[0]);
                }
                else if (map.RegisterType.Equals("M", StringComparison.OrdinalIgnoreCase))
                {
                    bool[] vals = _client.ReadM(map.Address, 1);
                    if (vals.Length > 0) memoryImage.SetRawInput(map.TagName, vals[0]);
                }
                else if (map.RegisterType.Equals("Y", StringComparison.OrdinalIgnoreCase))
                {
                    bool[] vals = _client.ReadY(map.Address, 1);
                    if (vals.Length > 0) memoryImage.SetRawInput(map.TagName, vals[0]);
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
        if (State != ConnectionState.Connected || _client == null)
            return Task.CompletedTask;

        foreach (var map in OutputMappings)
        {
            try
            {
                if (map.RegisterType.Equals("Y", StringComparison.OrdinalIgnoreCase))
                {
                    bool val = memoryImage.GetRawOutputBool(map.TagName);
                    _client.WriteY(map.Address, new[] { val });
                }
                else if (map.RegisterType.Equals("M", StringComparison.OrdinalIgnoreCase))
                {
                    bool val = memoryImage.GetRawOutputBool(map.TagName);
                    _client.WriteM(map.Address, new[] { val });
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
        _client?.Disconnect();
        _client?.Dispose();
        State = ConnectionState.Disconnected;
        return Task.CompletedTask;
    }
}
