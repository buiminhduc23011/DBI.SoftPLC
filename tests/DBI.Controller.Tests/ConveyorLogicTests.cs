using DBI.Controller.Testing;
using Sample.Conveyor;
using Xunit;

namespace DBI.Controller.Tests;

public class ConveyorLogicTests
{
    [Fact]
    public void ConveyorProgram_StartAndStopFlow_ShouldWorkCorrectly()
    {
        var program = new ConveyorProgram();
        var host = new TestHost(program, scanIntervalMs: 20);

        // Initial State
        host.Step(1);
        Assert.False(host.GetOutputBool("ConveyorRun"));

        // 1. Press StartButton -> ConveyorRun should turn TRUE
        host.SetInputBool("StartButton", true);
        host.Step(1);
        Assert.True(host.GetOutputBool("ConveyorRun"));

        // Release StartButton -> ConveyorRun remains TRUE
        host.SetInputBool("StartButton", false);
        host.Step(1);
        Assert.True(host.GetOutputBool("ConveyorRun"));

        // 2. Press StopButton -> ConveyorRun should turn FALSE
        host.SetInputBool("StopButton", true);
        host.Step(1);
        Assert.False(host.GetOutputBool("ConveyorRun"));
    }

    [Fact]
    public void ConveyorProgram_SensorProductTimer_ShouldStopConveyorAfterDelay()
    {
        var program = new ConveyorProgram();
        var host = new TestHost(program, scanIntervalMs: 20);

        // Start conveyor
        host.SetInputBool("StartButton", true);
        host.Step(1);
        host.SetInputBool("StartButton", false);
        Assert.True(host.GetOutputBool("ConveyorRun"));

        // Trigger SensorProduct (1000ms delay timer)
        host.SetInputBool("SensorProduct", true);

        // Step for 500ms (25 cycles) -> Conveyor still running
        host.Step(25);
        Assert.True(host.GetOutputBool("ConveyorRun"));

        // Wait for timer completion (1100ms total)
        Thread.Sleep(1100);
        host.Step(5);

        // Conveyor should stop automatically after delay!
        Assert.False(host.GetOutputBool("ConveyorRun"));
    }
}
