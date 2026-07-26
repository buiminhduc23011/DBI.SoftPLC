using DBI.Controller.Core.Models;
using DBI.Controller.Driver.Delta;
using DBI.Controller.Driver.Modbus;
using DBI.Controller.Driver.Omron;
using DBI.Controller.Driver.Simulation;
using DBI.Controller.Runtime.Drivers;

namespace DBI.Controller.Tests;

/// <summary>
/// B-5 — bảng định tuyến tag → thiết bị. Trước đây <c>DriverManager</c> đưa cả memory image cho
/// mọi driver và mỗi driver tự đoán key nào của mình.
/// </summary>
public class TagRoutingTests
{
    private static TagRoute Input(string tag, string device, string address = "0") =>
        new(tag, TagDataType.Bool, TagDirection.Input, device, address);

    private static TagRoute Output(string tag, string device, string address = "0") =>
        new(tag, TagDataType.Bool, TagDirection.Output, device, address);

    [Fact]
    public void RoutingTable_TraDungRouteCuaTungThietBi()
    {
        var table = new TagRoutingTable();
        table.Load(new[]
        {
            Input("StartButton", "PLC_A"),
            Input("StopButton", "PLC_A"),
            Output("Motor", "PLC_B"),
        });

        Assert.Equal(2, table.GetRoutesForDevice("PLC_A").Count);
        Assert.Single(table.GetRoutesForDevice("PLC_B"));
        Assert.Empty(table.GetRoutesForDevice("PLC_C"));
    }

    [Fact]
    public void RoutingTable_TenThietBiKhongPhanBietHoaThuong()
    {
        var table = new TagRoutingTable();
        table.Load(new[] { Input("StartButton", "PLC_A") });

        Assert.Single(table.GetRoutesForDevice("plc_a"));
    }

    [Fact]
    public void RoutingTable_TagMemoryKhongThuocThietBiNao()
    {
        var table = new TagRoutingTable();
        table.Load(new[]
        {
            Input("StartButton", "PLC_A"),
            new TagRoute("InternalFlag", TagDataType.Bool, TagDirection.Memory, "", ""),
        });

        Assert.Single(table.GetRoutesForDevice("PLC_A"));
        Assert.Equal(2, table.All.Count);
        Assert.Contains("InternalFlag", table.TagNames);
    }

    [Fact]
    public void RoutingTable_LoadLai_ThayHanBangCu()
    {
        var table = new TagRoutingTable();
        table.Load(new[] { Input("Cũ", "PLC_A") });
        table.Load(new[] { Input("Mới", "PLC_B") });

        Assert.Empty(table.GetRoutesForDevice("PLC_A"));
        Assert.Single(table.GetRoutesForDevice("PLC_B"));
    }

    // ── Cách ly giữa các driver ──────────────────────────────────────────────────

    [Fact]
    public async Task Driver_ChiNhanRouteCuaChinhNo_KhongThayTagCuaDriverKhac()
    {
        var memory = new MemorySnapshot();

        var driverA = new SimulationDriver("PLC_A");
        var driverB = new SimulationDriver("PLC_B");

        var manager = new DriverManager();
        manager.RegisterDriver(driverA);
        manager.RegisterDriver(driverB);

        manager.RoutingTable.Load(new[]
        {
            Output("MotorA", "PLC_A"),
            Output("MotorB", "PLC_B"),
        });

        await driverA.ConnectAsync();
        await driverB.ConnectAsync();

        memory.SetBool("MotorA", true);
        memory.SetBool("MotorB", true);
        memory.SwapOutputBuffers();

        await manager.WriteOutputsAsync(memory);

        Assert.Equal(new[] { "MotorA" }, driverA.WrittenTagNames);
        Assert.Equal(new[] { "MotorB" }, driverB.WrittenTagNames);
    }

    [Fact]
    public async Task Driver_HaiThietBiTagTrungTen_MoiBenChiThayTagCuaMinh()
    {
        var memory = new MemorySnapshot();

        var driverA = new SimulationDriver("PLC_A");
        var driverB = new SimulationDriver("PLC_B");

        var manager = new DriverManager();
        manager.RegisterDriver(driverA);
        manager.RegisterDriver(driverB);

        // Chỉ PLC_A giữ tag "Motor". Trước B-5, cả hai driver đều ghi tag này xuống phần cứng.
        manager.RoutingTable.Load(new[] { Output("Motor", "PLC_A") });

        await driverA.ConnectAsync();
        await driverB.ConnectAsync();

        memory.SetBool("Motor", true);
        memory.SwapOutputBuffers();

        await manager.WriteOutputsAsync(memory);

        Assert.Contains("Motor", driverA.WrittenTagNames);
        Assert.Empty(driverB.WrittenTagNames);
    }

    [Fact]
    public async Task Driver_ChiDocInputCuaMinh()
    {
        var memory = new MemorySnapshot();

        var driverA = new SimulationDriver("PLC_A");
        driverA.SetInputBool("SensorA", true);
        driverA.SetInputBool("SensorB", true);   // không thuộc route của A

        var manager = new DriverManager();
        manager.RegisterDriver(driverA);
        manager.RoutingTable.Load(new[] { Input("SensorA", "PLC_A") });

        await driverA.ConnectAsync();
        await manager.ReadInputsAsync(memory);
        memory.SwapInputBuffers();

        Assert.True(memory.GetBool("SensorA"));
        Assert.False(memory.GetBool("SensorB"));
    }

    // ── Phân giải địa chỉ của driver phần cứng ───────────────────────────────────

    [Theory]
    [InlineData("Coil:10", ModbusRegisterType.Coil, 10)]
    [InlineData("DI:5", ModbusRegisterType.DiscreteInput, 5)]
    [InlineData("HR:40001", ModbusRegisterType.HoldingRegister, 40001)]
    [InlineData("IR:3", ModbusRegisterType.InputRegister, 3)]
    [InlineData("7", ModbusRegisterType.DiscreteInput, 7)]      // Input mặc định DiscreteInput
    public void Modbus_PhanGiaiDiaChi(string address, ModbusRegisterType expectedType, int expectedAddress)
    {
        var driver = new ModbusDriverAdapter();
        var route = new TagRoute("T", TagDataType.Bool, TagDirection.Input, "PLC", address);

        Assert.True(driver.TryParseAddress(route, out var type, out ushort parsed));
        Assert.Equal(expectedType, type);
        Assert.Equal(expectedAddress, parsed);
    }

    [Fact]
    public void Modbus_DiaChiSai_BaoLoiRoRangThayViImLang()
    {
        var driver = new ModbusDriverAdapter();
        var route = new TagRoute("BăngTải", TagDataType.Bool, TagDirection.Input, "PLC", "không-phải-số");

        Assert.False(driver.TryParseAddress(route, out _, out _));
        Assert.Contains("BăngTải", driver.LastError);
    }

    [Theory]
    [InlineData("X0", 'X', 0)]
    [InlineData("Y5", 'Y', 5)]
    [InlineData("M100", 'M', 100)]
    [InlineData("m100", 'M', 100)]
    public void Delta_PhanGiaiDiaChi(string address, char expectedArea, int expectedAddress)
    {
        var driver = new DeltaPlcDriverAdapter();
        var route = new TagRoute("T", TagDataType.Bool, TagDirection.Input, "PLC", address);

        Assert.True(driver.TryParseAddress(route, out char area, out int parsed));
        Assert.Equal(expectedArea, area);
        Assert.Equal(expectedAddress, parsed);
    }

    [Theory]
    [InlineData("CIO100", "CIO", 100)]
    [InlineData("WR5", "WR", 5)]
    [InlineData("hr10", "HR", 10)]
    public void Omron_PhanGiaiDiaChi(string address, string expectedArea, int expectedAddress)
    {
        var driver = new OmronPlcDriverAdapter();
        var route = new TagRoute("T", TagDataType.Bool, TagDirection.Input, "PLC", address);

        Assert.True(driver.TryParseAddress(route, out string area, out int parsed));
        Assert.Equal(expectedArea, area);
        Assert.Equal(expectedAddress, parsed);
    }

    [Fact]
    public void Omron_DiaChiSai_BaoLoiRoRang()
    {
        var driver = new OmronPlcDriverAdapter();
        var route = new TagRoute("T", TagDataType.Bool, TagDirection.Input, "PLC", "DM100");

        Assert.False(driver.TryParseAddress(route, out _, out _));
        Assert.Contains("CIO100", driver.LastError);
    }

    // ── DriverFactory ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("DBI.Controller.Driver.Simulation")]
    [InlineData("Simulation")]
    [InlineData("DBI.Controller.Driver.Modbus")]
    [InlineData("Modbus")]
    [InlineData("FactoryIO")]
    [InlineData("Delta")]
    [InlineData("Omron")]
    public void DriverFactory_DungDuocNamLoaiDriver(string driverType)
    {
        var factory = new DriverFactory();
        var spec = new DeviceSpec("PLC_1", driverType, new Dictionary<string, string>());

        var driver = factory.Create(spec);

        // DriverId phải bằng tên thiết bị — TagRoutingTable tra route theo đúng tên đó.
        Assert.Equal("PLC_1", driver.DriverId);
    }

    [Fact]
    public void DriverFactory_LoaiLa_NemLoiKemDanhSachHoTro()
    {
        var factory = new DriverFactory();
        var spec = new DeviceSpec("PLC_1", "Siemens_S7", new Dictionary<string, string>());

        var ex = Assert.Throws<UnknownDriverException>(() => factory.Create(spec));

        Assert.Contains("Siemens_S7", ex.Message);
        Assert.Contains("Modbus", ex.Message);
    }

    [Fact]
    public void DriverFactory_DocThamSoKetNoiTuDeviceSpec()
    {
        var factory = new DriverFactory();
        var spec = new DeviceSpec("PLC_1", "Modbus", new Dictionary<string, string>
        {
            ["ip"] = "192.168.1.50",
            ["port"] = "5020",
            ["slaveId"] = "3"
        });

        var driver = (ModbusDriverAdapter)factory.Create(spec);

        Assert.Equal("192.168.1.50", driver.IpAddress);
        Assert.Equal(5020, driver.Port);
        Assert.Equal(3, driver.SlaveId);
    }
}
