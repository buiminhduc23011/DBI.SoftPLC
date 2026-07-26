using DBI.Controller.Core.Models;
using DBI.Controller.Driver.Simulation;
using DBI.Controller.Runtime.Drivers;
using DBI.Controller.Runtime.Engine;
using DBI.Controller.Runtime.Safety;
using DBI.Controller.SDK;

namespace DBI.Controller.Testing;

/// <summary>
/// Chạy một <see cref="ControllerProgram"/> theo từng chu kỳ, không cần Runtime thật hay phần cứng.
/// </summary>
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
        // Chưa có Tag Table thì driver giả lập soi toàn bộ memory image — gọi LoadRoutes() để
        // chuyển sang định tuyến thật.
        SimulationDriver = new SimulationDriver { MirrorAllTags = true };

        DriverManager = new DriverManager();
        DriverManager.RegisterDriver(SimulationDriver);

        SafetyCatchManager = new SafetyCatchManager { WriteToConsole = false };

        ScanEngine = new ScanEngine(MemoryImage, DriverManager, SafetyCatchManager)
        {
            ScanIntervalMs = scanIntervalMs
        };

        Program.Initialize(MemoryImage);
        Program.OnStart();
        ScanEngine.SetProgram(Program);
    }

    /// <summary>
    /// Nạp bảng định tuyến tag → thiết bị. Không gọi thì driver giả lập thấy toàn bộ tag,
    /// đủ dùng cho phần lớn test logic.
    /// </summary>
    public void LoadRoutes(IEnumerable<TagRoute> routes)
    {
        DriverManager.RoutingTable.Load(routes);
        SimulationDriver.MirrorAllTags = false;
    }

    /// <summary>Chạy đúng <paramref name="count"/> chu kỳ quét, tuần tự, không đợi thời gian thực.</summary>
    public void Step(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            DriverManager.ReadInputsAsync(MemoryImage).GetAwaiter().GetResult();
            MemoryImage.SwapInputBuffers();

            Program.Execute();

            MemoryImage.SwapOutputBuffers();
            DriverManager.WriteOutputsAsync(MemoryImage).GetAwaiter().GetResult();
        }
    }

    public void SetInputBool(string key, bool value) => SimulationDriver.SetInputBool(key, value);
    public void SetInputInt(string key, int value) => SimulationDriver.SetInputInt(key, value);
    public void SetInputFloat(string key, float value) => SimulationDriver.SetInputFloat(key, value);

    public bool GetOutputBool(string key) => SimulationDriver.GetOutputBool(key);
    public int GetOutputInt(string key) => SimulationDriver.GetOutputInt(key);
    public float GetOutputFloat(string key) => SimulationDriver.GetOutputFloat(key);
}
