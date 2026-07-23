using DBI.Controller.Core.Interfaces;

namespace DBI.Controller.Runtime.Drivers;

public class DriverManager
{
    private readonly List<IDriver> _drivers = new();

    public IReadOnlyList<IDriver> Drivers => _drivers.AsReadOnly();

    public void RegisterDriver(IDriver driver)
    {
        if (driver == null) throw new ArgumentNullException(nameof(driver));
        _drivers.Add(driver);
    }

    public async Task ConnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var driver in _drivers)
        {
            await driver.ConnectAsync(cancellationToken);
        }
    }

    public async Task ReadInputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default)
    {
        foreach (var driver in _drivers)
        {
            await driver.ReadInputsAsync(memoryImage, cancellationToken);
        }
    }

    public async Task WriteOutputsAsync(IMemoryImage memoryImage, CancellationToken cancellationToken = default)
    {
        foreach (var driver in _drivers)
        {
            await driver.WriteOutputsAsync(memoryImage, cancellationToken);
        }
    }

    public async Task DisconnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var driver in _drivers)
        {
            await driver.DisconnectAsync(cancellationToken);
        }
    }
}
