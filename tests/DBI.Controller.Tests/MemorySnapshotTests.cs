using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Controller.SDK.IO;

namespace DBI.Controller.Tests;

/// <summary>
/// Chốt lại ba lỗi nền tảng phase-00 đã gỡ: B-1 (dynamic IO), B-2 (driver ép kiểu),
/// B-3 (int/float không double-buffer).
/// </summary>
public class MemorySnapshotTests
{
    // ── B-3: double-buffer đủ cả ba kiểu ─────────────────────────────────────────

    [Theory]
    [InlineData("Bool")]
    [InlineData("Int")]
    [InlineData("Float")]
    public void RawInputGhiGiuaChuKy_KhongAnhHuongSnapshotDangThucThi(string kind)
    {
        IMemoryImage mem = new MemorySnapshot();

        WriteRawInput(mem, "Sensor", kind, first: true);
        mem.SwapInputBuffers();

        AssertInput(mem, "Sensor", kind, expectFirst: true);

        // Driver ghi tiếp giữa chu kỳ — logic đang chạy phải KHÔNG thấy giá trị mới.
        WriteRawInput(mem, "Sensor", kind, first: false);
        AssertInput(mem, "Sensor", kind, expectFirst: true);

        // Chỉ sau khi hoán đổi buffer mới thấy.
        mem.SwapInputBuffers();
        AssertInput(mem, "Sensor", kind, expectFirst: false);
    }

    private static void WriteRawInput(IMemoryImage mem, string key, string kind, bool first)
    {
        switch (kind)
        {
            case "Bool": mem.SetRawInput(key, first); break;
            case "Int": mem.SetRawInput(key, first ? 11 : 22); break;
            case "Float": mem.SetRawInput(key, first ? 1.5f : 2.5f); break;
        }
    }

    private static void AssertInput(IMemoryImage mem, string key, string kind, bool expectFirst)
    {
        switch (kind)
        {
            case "Bool": Assert.Equal(expectFirst, mem.GetBool(key)); break;
            case "Int": Assert.Equal(expectFirst ? 11 : 22, mem.GetInt(key)); break;
            case "Float": Assert.Equal(expectFirst ? 1.5f : 2.5f, mem.GetFloat(key)); break;
        }
    }

    [Fact]
    public void SwapInputBuffers_HoanDoiThamChieu_KhongCapPhatBoNhoMoi()
    {
        IMemoryImage mem = new MemorySnapshot();

        // Nếu swap là copy O(n) thì buffer ghi vẫn giữ giá trị cũ sau khi hoán đổi.
        // Với hoán đổi tham chiếu, buffer ghi trở thành buffer đọc của chu kỳ trước.
        mem.SetRawInput("A", true);
        mem.SwapInputBuffers();
        Assert.True(mem.GetBool("A"));

        // Chu kỳ tiếp theo driver không ghi gì -> đọc ra giá trị của 2 chu kỳ trước (chưa có) = false.
        mem.SwapInputBuffers();
        Assert.False(mem.GetBool("A"));
    }

    // ── Output là trạng thái giữ (latch) — không được hoán đổi tham chiếu ────────

    [Fact]
    public void Output_GiuGiaTriQuaNhieuChuKy_KhiKhongAiGhiLai()
    {
        IMemoryImage mem = new MemorySnapshot();

        mem.SetBool("Motor", true);
        mem.SetInt("Speed", 750);
        mem.SetFloat("Setpoint", 42.5f);
        mem.SwapOutputBuffers();

        // 5 chu kỳ sau, không ai ghi lại — giá trị phải còn nguyên ở cả hai phía.
        for (int i = 0; i < 5; i++)
            mem.SwapOutputBuffers();

        Assert.True(mem.GetBool("Motor"));
        Assert.Equal(750, mem.GetInt("Speed"));
        Assert.Equal(42.5f, mem.GetFloat("Setpoint"));

        Assert.True(mem.GetRawOutputBool("Motor"));
        Assert.Equal(750, mem.GetRawOutputInt("Speed"));
        Assert.Equal(42.5f, mem.GetRawOutputFloat("Setpoint"));
    }

    [Fact]
    public void Output_ChuaSwap_DriverChuaThayGiaTriMoi()
    {
        IMemoryImage mem = new MemorySnapshot();

        mem.SetBool("Motor", true);
        Assert.False(mem.GetRawOutputBool("Motor"));   // chưa chốt

        mem.SwapOutputBuffers();
        Assert.True(mem.GetRawOutputBool("Motor"));    // đã chốt
    }

    [Fact]
    public void ClearAllOutputs_DuaCaBaKieuVeSafeState()
    {
        IMemoryImage mem = new MemorySnapshot();

        mem.SetBool("Motor", true);
        mem.SetInt("Speed", 750);
        mem.SetFloat("Setpoint", 42.5f);
        mem.SwapOutputBuffers();

        mem.ClearAllOutputs();

        Assert.False(mem.GetBool("Motor"));
        Assert.Equal(0, mem.GetInt("Speed"));
        Assert.Equal(0f, mem.GetFloat("Setpoint"));

        Assert.False(mem.GetRawOutputBool("Motor"));
        Assert.Equal(0, mem.GetRawOutputInt("Speed"));
        Assert.Equal(0f, mem.GetRawOutputFloat("Setpoint"));
    }

    // ── B-1: tag Real đọc ra float, không phải bool ──────────────────────────────

    [Fact]
    public void TagFloat_DocRaFloat_KhongPhaiBool()
    {
        IMemoryImage mem = new MemorySnapshot();
        var io = new IOContainer(mem);

        mem.SetRawInput("Temperature", 36.6f);
        mem.SwapInputBuffers();

        float value = io.GetFloat("Temperature");

        Assert.Equal(36.6f, value);
        Assert.IsType<float>(value);

        // Tag Bool và tag Real cùng tên khác nhau là hai không gian riêng —
        // đọc nhầm kiểu không còn im lặng trả về false như thời DynamicObject.
        Assert.False(io.GetBool("Temperature"));
    }

    [Fact]
    public void TagInt_DocRaInt_QuaIOContainer()
    {
        IMemoryImage mem = new MemorySnapshot();
        var io = new IOContainer(mem);

        io.SetInt("Counter", 1234);
        Assert.Equal(1234, io.GetInt("Counter"));

        io.SetFloat("Rate", -0.25f);
        Assert.Equal(-0.25f, io.GetFloat("Rate"));
    }

    // ── SnapshotAll cho monitoring (phase-10) ────────────────────────────────────

    [Fact]
    public void SnapshotAll_LietKeCaInputVaOutput_DungKieu()
    {
        IMemoryImage mem = new MemorySnapshot();

        mem.SetRawInput("StartButton", true);
        mem.SetRawInput("PartCount", 7);
        mem.SetRawInput("Temperature", 36.6f);
        mem.SwapInputBuffers();

        mem.SetBool("Motor", true);
        mem.SetInt("Speed", 750);

        var all = mem.SnapshotAll();

        Assert.Equal(true, all["StartButton"]);
        Assert.Equal(7, all["PartCount"]);
        Assert.Equal(36.6f, all["Temperature"]);
        Assert.Equal(true, all["Motor"]);
        Assert.Equal(750, all["Speed"]);
    }

    [Fact]
    public void GetRawOutputs_LietKeOutputDaChot_DuCaBaKieu()
    {
        IMemoryImage mem = new MemorySnapshot();

        mem.SetBool("Motor", true);
        mem.SetInt("Speed", 750);
        mem.SetFloat("Setpoint", 42.5f);
        mem.SwapOutputBuffers();

        var outputs = mem.GetRawOutputs();

        Assert.Equal(true, outputs["Motor"]);
        Assert.Equal(750, outputs["Speed"]);
        Assert.Equal(42.5f, outputs["Setpoint"]);
    }

    [Fact]
    public void TenTag_KhongPhanBietHoaThuong()
    {
        IMemoryImage mem = new MemorySnapshot();

        mem.SetRawInput("StartButton", true);
        mem.SwapInputBuffers();

        Assert.True(mem.GetBool("startbutton"));
        Assert.True(mem.GetBool("STARTBUTTON"));
    }
}
