using DBI.Controller.Core.Models;

namespace DBI.Controller.Tests;

public sealed class ForceLayerTests
{
    [Fact]
    public void ForceOverridesInputAndRawOutputForAllTypes()
    {
        var memory = new MemorySnapshot();
        memory.SetRawInput("Input", false); memory.SetRawInput("Count", 1); memory.SetRawInput("Temperature", 1.5f);
        memory.SwapInputBuffers(); memory.SwapOutputBuffers();
        memory.SetForce("Input", true); memory.SetForce("Count", 42); memory.SetForce("Temperature", 72.5f);
        Assert.True(memory.GetBool("Input")); Assert.Equal(42, memory.GetInt("Count")); Assert.Equal(72.5f, memory.GetFloat("Temperature"));
        Assert.Equal(3, memory.GetAllForces().Count);
        memory.ClearAllForces(); Assert.Empty(memory.GetAllForces());
    }
}
