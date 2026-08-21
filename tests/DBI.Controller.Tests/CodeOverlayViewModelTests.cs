using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;
using DBI.Controller.Studio.Core.ViewModels;

namespace DBI.Controller.Tests;

/// <summary>Phase-13 — overlay điều phối đúng vòng đời subscribe theo file và vùng nhìn thấy.</summary>
public sealed class CodeOverlayViewModelTests
{
    [Fact]
    public void UpdateSource_AnalyzesIoReferences()
    {
        var (overlay, runtime) = Create();
        // Regex IO.\w cũng khớp trong comment — hạn chế đã chấp nhận của phương án lùi
        // (phase-13 Task 13.2). Test ghi nhận đúng hành vi đó.
        overlay.UpdateSource("if (IO.StartButton)\n    IO.ConveyorRun = true;\n// IO.OnlyInComment");

        overlay.Monitoring = true;

        Assert.Contains("StartButton", runtime.SubscribedTags);
        Assert.Contains("ConveyorRun", runtime.SubscribedTags);
        Assert.Equal(3, runtime.SubscribedTags.Count);
    }

    [Fact]
    public void MonitoringOff_ClearsMarkersAndUnsubscribes()
    {
        var (overlay, runtime) = Create();
        overlay.UpdateSource("IO.StartButton");
        overlay.Monitoring = true;
        Assert.NotEmpty(runtime.SubscribedTags);

        overlay.Monitoring = false;

        Assert.Empty(runtime.SubscribedTags);
        Assert.Empty(overlay.Markers);
    }

    [Fact]
    public void UpdateVisibleRange_BuildsMarkersForVisibleLines()
    {
        var (overlay, runtime) = Create();
        overlay.UpdateSource("IO.A\nIO.B\nIO.C");
        overlay.Monitoring = true;

        runtime.SetTagValue("A", true);
        runtime.SetTagValue("B", false);
        runtime.SetTagValue("C", true);

        overlay.UpdateVisibleRange(1, 2); // chỉ dòng 1–2

        Assert.Equal(2, overlay.Markers.Count);
        Assert.Equal(1, overlay.Markers[0].Line);
        Assert.Equal("TRUE", overlay.Markers[0].DisplayValue);
        Assert.Equal(2, overlay.Markers[1].Line);
    }

    private static (CodeOverlayViewModel Overlay, FakeRuntimeClient Runtime) Create()
    {
        var runtime = new FakeRuntimeClient();
        return (new CodeOverlayViewModel(new LiveCodeOverlayService(), runtime), runtime);
    }
}
