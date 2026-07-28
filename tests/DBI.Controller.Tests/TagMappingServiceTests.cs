using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.ViewModels;

namespace DBI.Controller.Tests;

public sealed class TagMappingServiceTests
{
    [Fact]
    public void AddToTable_SuggestsUniqueNameAndCopiesHardwareMapping()
    {
        var table = new TagTable { Name = "IO" };
        table.Tags.Add(new Tag { Name = "FIO_Input_0" });
        var result = new TagMappingService().AddToTable(table,
            new DeviceTagCandidate("FIO", "Input_0", TagDataType.Bool));

        Assert.True(result.Success);
        Assert.Equal("FIO_Input_0_2", result.CreatedTagName);
        Assert.Equal("Input_0", table.Tags[^1].Address);
    }

    [Fact]
    public void MapToTag_RejectsDifferentDataType()
    {
        var project = new DbiProject();
        var tag = new Tag { Name = "Temperature", DataType = TagDataType.Bool };
        var row = new TagRowViewModel(project, tag, () => { });

        var result = new TagMappingService().MapToTag(row,
            new DeviceTagCandidate("MB", "40001", TagDataType.Real));

        Assert.False(result.Success);
        Assert.Contains("Real", result.Error);
        Assert.Equal("", tag.Address);
    }

    [Fact]
    public void MappingSupportsWatchAndEditorDropTargets()
    {
        var watch = new WatchTable();
        var mapping = new TagMappingService();

        Assert.True(mapping.AddToWatch(watch, "StartButton"));
        Assert.False(mapping.AddToWatch(watch, "StartButton"));
        Assert.Equal("if (IO.StartButton)", TagMappingService.InsertIoReference("if ()", 4, "StartButton"));
    }
}
