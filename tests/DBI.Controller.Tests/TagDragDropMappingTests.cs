using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;
using DBI.Controller.Studio.Core.Services.Runtime;
using DBI.Controller.Studio.Core.ViewModels;

namespace DBI.Controller.Tests;

/// <summary>Phase-12 Task 12.3 — nghiệp vụ kéo-thả device tag vào Tag Table và Watch Table.</summary>
public sealed class TagDragDropMappingTests
{
    // ── Kéo device tag → dòng trống Tag Table: tạo tag mới, tự điền Device/Address/Type ──

    [Fact]
    public void MapFromDevice_EmptyRow_CreatesTagWithSuggestedName()
    {
        var table = CreateTable();
        var item = new DeviceTagItem("Input_1", "Bool", "FactoryIO", "Input_1", IsMapped: true);

        string? rejection = table.MapFromDevice(item);

        Assert.Null(rejection);
        Assert.Equal(2, table.Rows.Count); // StartButton có sẵn + tag mới tạo
        var tag = table.Rows.Single(r => r.Name == "FactoryIO_Input_1");
        Assert.Equal(TagDataType.Bool, tag.DataType);
        Assert.Equal("FactoryIO", tag.Device);
        Assert.Equal("Input_1", tag.Address);
    }

    // ── Kéo device tag → ô của tag có sẵn: gán lại Device/Address ──

    [Fact]
    public void MapFromDevice_SelectedRow_RetargetsDeviceAndAddress()
    {
        var table = CreateTable();
        table.SelectedRow = table.Rows.Single(); // StartButton đang Bool
        var item = new DeviceTagItem("SensorX", "Bool", "Modbus_IO", "40001", IsMapped: true);

        string? rejection = table.MapFromDevice(item);

        Assert.Null(rejection);
        Assert.Equal("StartButton", table.SelectedRow.Name); // tên giữ nguyên
        Assert.Equal("Modbus_IO", table.SelectedRow.Device);
        Assert.Equal("40001", table.SelectedRow.Address);
    }

    // ── Kiểm tra kiểu: kéo Real vào ô Bool → chặn kèm thông điệp ──

    [Fact]
    public void MapFromDevice_TypeMismatch_IsRejectedWithReason()
    {
        var table = CreateTable();
        table.SelectedRow = table.Rows.Single(); // Bool
        var item = new DeviceTagItem("Temperature", "Real", "Modbus_IO", "40002", IsMapped: true);

        string? rejection = table.MapFromDevice(item);

        Assert.NotNull(rejection);
        Assert.Contains("Real", rejection);
        Assert.Equal("", table.SelectedRow.Device); // không bị ghi đè
    }

    [Fact]
    public void MapFromDevice_DuplicateSuggestedName_IsRejected()
    {
        var table = CreateTable();
        var first = new DeviceTagItem("Any", "Bool", "Sim", "Sim_0", IsMapped: true);

        string? rejection = table.MapFromDevice(first); // tạo tag tên gợi ý "Sim_Sim_0"

        Assert.Null(rejection);
        table.SelectedRow = null; // thả vào vùng trống, không phải lên dòng vừa tạo
        var second = new DeviceTagItem("Other", "Bool", "Sim", "Sim_0", IsMapped: true);
        string? secondRejection = table.MapFromDevice(second);

        Assert.NotNull(secondRejection); // trùng tên tag vừa tạo
    }

    // ── Kéo device tag → Watch Table ──

    [Fact]
    public void AddDeviceTag_MappedTag_AddsWatchRow()
    {
        var (view, runtime, projects) = CreateWatchView();
        var item = new DeviceTagItem("StopButton", "Bool", "Sim", "Sim_1", IsMapped: true);

        string? rejection = view.AddDeviceTag(item);

        Assert.Null(rejection);
        Assert.Equal(2, view.Rows.Count);
        Assert.True(view.IsDirty);
    }

    [Fact]
    public void AddDeviceTag_UnmappedTag_IsRejected()
    {
        var (view, _, _) = CreateWatchView();
        var item = new DeviceTagItem("Ghost", "Bool", "", "", IsMapped: false);

        string? rejection = view.AddDeviceTag(item);

        Assert.NotNull(rejection);
        Assert.Single(view.Rows); // không thêm gì
    }

    [Fact]
    public void AddDeviceTag_AlreadyWatched_IsRejected()
    {
        var (view, _, _) = CreateWatchView();
        var item = new DeviceTagItem("StartButton", "Bool", "Sim", "Sim_0", IsMapped: true);

        string? rejection = view.AddDeviceTag(item);

        Assert.NotNull(rejection);
        Assert.Single(view.Rows);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static TagTableViewModel CreateTable()
    {
        var project = new DbiProject { Name = "Machine" };
        project.TagTables.Add(new TagTable());
        project.TagTables[0].Tags.Add(new Tag { Name = "StartButton", DataType = TagDataType.Bool, Direction = TagDirection.Input });
        return new TagTableViewModel(project, project.TagTables[0], new IoCodeGenerator());
    }

    private static (WatchTableViewModel View, FakeRuntimeClient Runtime, ProjectService Projects) CreateWatchView()
    {
        string root = Path.Combine(Path.GetTempPath(), "dbi-dragdrop-" + Guid.NewGuid().ToString("N"));
        var projects = new ProjectService();
        var loaded = projects.CreateNew(root, "Machine");
        var project = loaded.Project!;
        project.Devices.Add(new DeviceConfig { Name = "Sim", DriverType = "Simulation" });
        project.TagTables[0].Tags.Add(new Tag { Name = "StartButton", DataType = TagDataType.Bool, Direction = TagDirection.Input, Device = "Sim", Address = "Sim_0" });
        project.TagTables[0].Tags.Add(new Tag { Name = "StopButton", DataType = TagDataType.Bool, Direction = TagDirection.Input, Device = "Sim", Address = "Sim_1" });
        project.WatchTables.Add(new WatchTable { Name = "Watch", TagNames = { "StartButton" } });

        var runtime = new FakeRuntimeClient();
        var view = new WatchTableViewModel(project, project.WatchTables[0], runtime, projects);
        return (view, runtime, projects);
    }
}
