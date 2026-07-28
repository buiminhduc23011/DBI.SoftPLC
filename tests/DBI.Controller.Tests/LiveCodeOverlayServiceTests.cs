using DBI.Controller.Protocol;
using DBI.Controller.Studio.Core.Services;

namespace DBI.Controller.Tests;

public sealed class LiveCodeOverlayServiceTests
{
    [Fact]
    public void Analyze_MapsIoMembersToSourceLines()
    {
        var overlay = new LiveCodeOverlayService();
        var refs = overlay.Analyze("if (IO.StartButton)\n    IO.ConveyorRun = true;\n// IO.NotCode");
        Assert.Equal(3, refs.Count);
        Assert.Equal((1, "StartButton"), (refs[0].Line, refs[0].TagName));
        Assert.Equal((2, "ConveyorRun"), (refs[1].Line, refs[1].TagName));
    }

    [Fact]
    public void Apply_StoresLatestValueForOverlayLookup()
    {
        var overlay = new LiveCodeOverlayService();
        overlay.Apply(new TagValueUpdate("StartButton", "true", 1));
        Assert.True(overlay.TryGetValue("StartButton", out var value));
        Assert.Equal("true", value.ValueJson);
    }
}
