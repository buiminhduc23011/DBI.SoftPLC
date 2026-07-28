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

            Assert.Equal("TRUE", view.Rows.Single().ValueText);
            Assert.True(view.Monitoring);
            await view.SaveAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
