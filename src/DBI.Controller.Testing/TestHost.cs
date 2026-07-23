using DBI.Controller.Core.Models;
using DBI.Controller.Driver.Simulation;
using DBI.Controller.Runtime.Drivers;
using DBI.Controller.Runtime.Engine;
using DBI.Controller.Runtime.Safety;
using DBI.Controller.SDK;

namespace DBI.Controller.Testing;

public class TestHost
{
    public MemorySnapshot MemoryImage { get; }
    public SimulationDriver SimulationDriver { get; }
    public DriverManager DriverManager { get; }
    public SafetyCatchManager SafetyCatchManager { get; }
    public ScanEngine ScanEngine { get; }
    public ControllerProgram Program { get; }

    public TestHost(ControllerProgram program, int scanIntervalMs = 20)
    {
        Program = program ?? throw new ArgumentNullException(nameof(program));
        MemoryImage = new MemorySnapshot();
        SimulationDriver = new SimulationDriver();
        DriverManager = new DriverManager();
        DriverManager.RegisterDriver(SimulationDriver);
        SafetyCatchManager = new SafetyCatchManager();

        ScanEngine = new ScanEngine(MemoryImage, DriverManager, SafetyCatchManager)
        {
            ScanIntervalMs = scanIntervalMs
        };

        Program.Initialize(MemoryImage);
        Program.OnStart();
        ScanEngine.SetProgram(Program);
    }

    /// <summary>
    /// Giả lập chạy một số lượng chu kỳ Scan Cycle nhất định.
    /// </summary>
    public void Step(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            SimulationDriver.ReadInputsAsync(MemoryImage).GetAwaiter().GetResult();
            MemoryImage.SwapInputBuffers();

            Program.Execute();

            MemoryImage.SwapOutputBuffers();
            SimulationDriver.WriteOutputsAsync(MemoryImage).GetAwaiter().GetResult();
        }
    }

    public void SetInputBool(string key, bool value)
    {
        SimulationDriver.SetInputBool(key, value);
    }

    public bool GetOutputBool(string key)
    {
        return SimulationDriver.GetOutputBool(key);
    }
}
