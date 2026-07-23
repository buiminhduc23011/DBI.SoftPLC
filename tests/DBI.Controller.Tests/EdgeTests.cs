using DBI.Controller.SDK.Primitives;
using Xunit;

namespace DBI.Controller.Tests;

public class EdgeTests
{
    [Fact]
    public void RisingEdge_ShouldTriggerForOnlyOneCycle()
    {
        var edge = new RisingEdge();

        Assert.False(edge.Update(false));
        Assert.True(edge.Update(true));   // Cycle 1: False -> True (Rising edge!)
        Assert.False(edge.Update(true));  // Cycle 2: Remains True (No edge)
        Assert.False(edge.Update(false)); // Cycle 3: True -> False (No rising edge)
    }

    [Fact]
    public void FallingEdge_ShouldTriggerForOnlyOneCycle()
    {
        var edge = new FallingEdge();

        edge.Update(true);                 // Setup True state
        Assert.True(edge.Update(false));  // Cycle 1: True -> False (Falling edge!)
        Assert.False(edge.Update(false)); // Cycle 2: Remains False (No edge)
    }
}
