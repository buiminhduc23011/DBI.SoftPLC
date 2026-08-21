using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;
using DBI.Controller.Studio.Core.ViewModels;

namespace DBI.Controller.Tests;

/// <summary>
/// Test luồng an toàn của Force Table: tạo force phải qua xác nhận, huỷ xác nhận thì
/// không force. Đây là vùng an toàn máy móc — không thoả hiệp.
/// </summary>
public sealed class ForceTableViewModelTests
{
    [Fact]
    public async Task ApplyForce_Confirmed_SendsForceToRuntime()
    {
        var (view, runtime, prompt) = CreateView();
        view.SelectedTagName = "StartButton";
        view.NewValueText = "true";

        await view.ApplyForceCommand.ExecuteAsync(null);

        Assert.Equal(1, prompt.ForceSafetyCount); // LUÔN hỏi — không có "đừng hỏi lại"
        Assert.True(await runtime.GetForcesAsync().ContinueWith(t => t.Result.Any(f => f.TagName == "StartButton")));
    }

    [Fact]
    public async Task ApplyForce_Cancelled_DoesNotForce()
    {
        var (view, runtime, prompt) = CreateView();
        prompt.ConfirmAnswer = false;
        view.SelectedTagName = "StartButton";
        view.NewValueText = "true";

        await view.ApplyForceCommand.ExecuteAsync(null);

        Assert.Equal(1, prompt.ForceSafetyCount);
        Assert.Empty(await runtime.GetForcesAsync());
    }

    [Fact]
    public async Task ApplyForce_InvalidValue_ShowsErrorWithoutAsking()
    {
        var (view, runtime, prompt) = CreateView();
        view.SelectedTagName = "StartButton";
        view.NewValueText = "không phải bool";

        await view.ApplyForceCommand.ExecuteAsync(null);

        Assert.Equal(0, prompt.ForceSafetyCount);
        Assert.NotEmpty(prompt.Errors);
        Assert.Empty(await runtime.GetForcesAsync());
    }

    [Fact]
    public async Task ClearAll_RemovesAllForces()
    {
        var (view, runtime, _) = CreateView();
        view.SelectedTagName = "StartButton";
        view.NewValueText = "true";
        await view.ApplyForceCommand.ExecuteAsync(null);
        view.SelectedTagName = "StopButton";
        view.NewValueText = "false";
        await view.ApplyForceCommand.ExecuteAsync(null);

        await view.ClearAllCommand.ExecuteAsync(null);

        Assert.Empty(await runtime.GetForcesAsync());
        Assert.False(view.HasForces);
    }

    private static (ForceTableViewModel View, FakeRuntimeClient Runtime, ScriptedPrompt Prompt) CreateView()
    {
        var project = new DbiProject { Name = "Machine" };
        project.TagTables.Add(new TagTable());
        project.TagTables[0].Tags.Add(new Tag { Name = "StartButton", DataType = TagDataType.Bool });
        project.TagTables[0].Tags.Add(new Tag { Name = "StopButton", DataType = TagDataType.Bool });

        var runtime = new FakeRuntimeClient();
        var prompt = new ScriptedPrompt();
        var view = new ForceTableViewModel(runtime, prompt, project);
        return (view, runtime, prompt);
    }
}
