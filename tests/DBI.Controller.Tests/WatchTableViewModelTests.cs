using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;
using DBI.Controller.Studio.Core.ViewModels;

namespace DBI.Controller.Tests;

public sealed class WatchTableViewModelTests
{
    [Fact]
    public async Task Monitoring_SubscriptionUpdatesOnlyWatchedRows()
    {
        string root = Path.Combine(Path.GetTempPath(), "dbi-watch-" + Guid.NewGuid().ToString("N"));
        var projects = new ProjectService();
        try
        {
            var loaded = projects.CreateNew(root, "Machine");
            var project = loaded.Project!;
            project.WatchTables.Add(new WatchTable { Name = "Watch", TagNames = { "StartButton" } });
            var runtime = new FakeRuntimeClient();
            var view = new WatchTableViewModel(project, project.WatchTables[0], runtime, projects);

            view.ToggleMonitoringCommand.Execute(null);
            runtime.SetTagValue("StartButton", true);
            runtime.SetTagValue("Other", true);
            view.FlushPending(); // rót thủ công — không phụ thuộc timer 100ms

            Assert.Equal("TRUE", view.Rows.Single().ValueText);
            Assert.True(view.Monitoring);
            await view.SaveAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void AddSelectedTag_AddsRowAndUpdatesDropdown()
    {
        var (view, _, _) = CreateView(extraTag: "StopButton");
        view.SelectedTagName = view.AvailableTags.First();

        view.AddSelectedTagCommand.Execute(null);

        Assert.Equal(2, view.Rows.Count);
        Assert.True(view.IsDirty);
    }

    [Fact]
    public async Task ModifyValue_WritesToRuntime()
    {
        var (view, runtime, _) = CreateView(direction: TagDirection.Output);
        var row = view.Rows.Single();

        row.ModifyText = "true";
        await view.ModifyValueCommand.ExecuteAsync(row);

        Assert.Equal(("StartButton", true), runtime.LastWrite);
    }

    [Fact]
    public async Task ModifyValue_InputTagIsBlocked()
    {
        var (view, runtime, _) = CreateView(direction: TagDirection.Input);
        var row = view.Rows.Single();

        row.ModifyText = "true";
        await view.ModifyValueCommand.ExecuteAsync(row);

        Assert.Null(runtime.LastWrite); // bị chặn trước khi tới Runtime
    }

    [Fact]
    public void ConnectionLost_RowsGoStale_KeepLastValue_ReconnectResubscribes()
    {
        var (view, runtime, _) = CreateView();
        runtime.ConnectAsync(new RuntimeConnectionTarget());
        view.ToggleMonitoringCommand.Execute(null);
        runtime.SetTagValue("StartButton", true);
        view.FlushPending();

        runtime.SimulateConnectionLost();
        Assert.All(view.Rows, r => Assert.True(r.IsStale));
        Assert.Equal("TRUE", view.Rows.Single().ValueText); // giữ giá trị cuối, không xoá về 0

        runtime.ConnectAsync(new RuntimeConnectionTarget());
        Assert.False(view.Rows.Single().IsStale); // nối lại → hết stale
        Assert.Contains("StartButton", runtime.SubscribedTags); // tự subscribe lại
    }

    [Fact]
    public async Task RefreshForces_MarksLockedRowsMatchingRuntimeForces()
    {
        var (view, runtime, _) = CreateView(extraTag: "StopButton");
        view.SelectedTagName = "StopButton";
        view.AddSelectedTagCommand.Execute(null);

        await runtime.ForceTagAsync("StartButton", true, enable: true);

        await view.RefreshForcesAsync();

        Assert.True(view.Rows.Single(r => r.Name == "StartButton").IsForced);
        Assert.False(view.Rows.Single(r => r.Name == "StopButton").IsForced);
    }

    [Fact]
    public async Task RefreshForces_ClearsLockWhenForceRemoved()
    {
        var (view, runtime, _) = CreateView();
        await runtime.ForceTagAsync("StartButton", true, enable: true);
        await view.RefreshForcesAsync();
        Assert.True(view.Rows.Single().IsForced);

        await runtime.ForceTagAsync("StartButton", true, enable: false);
        await view.RefreshForcesAsync();

        Assert.False(view.Rows.Single().IsForced);
    }

    private static (WatchTableViewModel View, FakeRuntimeClient Runtime, ProjectService Projects) CreateView(
        TagDirection direction = TagDirection.Input, string? extraTag = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "dbi-watch-" + Guid.NewGuid().ToString("N"));
        var projects = new ProjectService();
        var loaded = projects.CreateNew(root, "Machine");
        var project = loaded.Project!;
        project.Devices.Add(new DeviceConfig { Name = "Sim", DriverType = "Simulation" });
        project.TagTables[0].Tags.Add(new Tag { Name = "StartButton", DataType = TagDataType.Bool, Direction = direction, Device = "Sim", Address = "Sim_0" });
        if (extraTag is not null)
            project.TagTables[0].Tags.Add(new Tag { Name = extraTag, DataType = TagDataType.Bool, Direction = TagDirection.Memory, Device = "Sim", Address = "Sim_1" });
        project.WatchTables.Add(new WatchTable { Name = "Watch", TagNames = { "StartButton" } });

        var runtime = new FakeRuntimeClient();
        var view = new WatchTableViewModel(project, project.WatchTables[0], runtime, projects);
        return (view, runtime, projects);
    }
}
