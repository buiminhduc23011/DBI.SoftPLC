using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Drivers.Omron;

namespace DBI.Controller.Driver.Omron;

public class OmronTagConfig
{
    public string TagName { get; set; } = string.Empty;
    public string Area { get; set; } = "CIO"; // CIO, WR, HR
    public int Address { get; set; }
}

/// <summary>
/// Driver Adapter bọc OmronClient từ DBI.Drivers.Omron (FINS Protocol).
/// </summary>
public class OmronPlcDriverAdapter : IDriver
{
    private OmronClient? _client;

    public string DriverId { get; }
    public string IpAddress { get; set; }
    public int Port { get; set; }
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public List<OmronTagConfig> InputMappings { get; } = new();
    public List<OmronTagConfig> OutputMappings { get; } = new();

    public OmronPlcDriverAdapter(string driverId = "OMRON_PLC_DRIVER", string ipAddress = "192.168.1.10", int port = 9600)
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
            _client = new OmronClient(IpAddress, Port);
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
                if (map.Area.Equals("CIO", StringComparison.OrdinalIgnoreCase))
                {
                    bool[] vals = _client.ReadCIO(map.Address, 1);
                    if (vals.Length > 0) memoryImage.SetRawInput(map.TagName, vals[0]);
                }
                else if (map.Area.Equals("WR", StringComparison.OrdinalIgnoreCase))
                {
                    bool[] vals = _client.ReadWR(map.Address, 1);
                    if (vals.Length > 0) memoryImage.SetRawInput(map.TagName, vals[0]);
                }
                else if (map.Area.Equals("HR", StringComparison.OrdinalIgnoreCase))
                {
                    bool[] vals = _client.ReadHR(map.Address, 1);
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
                bool val = memoryImage.GetRawOutputBool(map.TagName);
                if (map.Area.Equals("CIO", StringComparison.OrdinalIgnoreCase))
                {
                    _client.WriteCIO(map.Address, new[] { val });
                }
                else if (map.Area.Equals("WR", StringComparison.OrdinalIgnoreCase))
                {
                    _client.WriteWR(map.Address, new[] { val });
                }
                else if (map.Area.Equals("HR", StringComparison.OrdinalIgnoreCase))
                {
                    _client.WriteHR(map.Address, new[] { val });
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
