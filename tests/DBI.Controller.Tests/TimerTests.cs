using DBI.Controller.SDK.Primitives;
using Xunit;

namespace DBI.Controller.Tests;

public class TimerTests
{
    [Fact]
    public void TonTimer_ShouldDelayOutput_Correctly()
    {
        var timer = new Ton(ptMs: 100);

        // Initial state
        Assert.False(timer.Q);

        // Trigger input signal
        timer.Update(inSignal: true);
        Assert.False(timer.Q);

        // Wait 150ms
        Thread.Sleep(150);

        // Update timer cycle
        timer.Update(inSignal: true);
        Assert.True(timer.Q);

        // Reset input signal
        timer.Update(inSignal: false);
        Assert.False(timer.Q);
        Assert.Equal(0, timer.EtMs);
    }
}
