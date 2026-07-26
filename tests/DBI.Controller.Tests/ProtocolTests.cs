using System.Text;
using DBI.Controller.Core.Models;
using DBI.Controller.Protocol;

namespace DBI.Controller.Tests;

public class ProtocolTests
{
    [Fact]
    public void Protocol_KhongThamChieuRuntimeHayStudio()
    {
        // Nếu contract lỡ tham chiếu Runtime thì ranh giới tiến trình của ADR-001 chỉ còn hình thức.
        var referenced = typeof(IpcRequest).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        Assert.DoesNotContain("DBI.Controller.Runtime", referenced);
        Assert.DoesNotContain("DBI.Controller.Studio", referenced);
        Assert.DoesNotContain("DBI.Controller.Studio.Core", referenced);
    }

    // ── Framing ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Framing_GhiRoiDocLai_GiuNguyenNoiDung()
    {
        using var stream = new MemoryStream();

        await IpcFraming.WriteMessageAsync(stream, "xin chào");
        stream.Position = 0;

        Assert.Equal("xin chào", await IpcFraming.ReadMessageAsync(stream));
    }

    [Fact]
    public async Task Framing_NhieuBanTinLienTiep_KhongLanNhau()
    {
        using var stream = new MemoryStream();

        await IpcFraming.WriteMessageAsync(stream, "một");
        await IpcFraming.WriteMessageAsync(stream, "hai");
        await IpcFraming.WriteMessageAsync(stream, "ba");
        stream.Position = 0;

        Assert.Equal("một", await IpcFraming.ReadMessageAsync(stream));
        Assert.Equal("hai", await IpcFraming.ReadMessageAsync(stream));
        Assert.Equal("ba", await IpcFraming.ReadMessageAsync(stream));
        Assert.Null(await IpcFraming.ReadMessageAsync(stream));
    }

    [Fact]
    public async Task Framing_PayloadLonHon1MB_VanNguyenVen()
    {
        // Deploy gửi assembly thật — dễ dàng vượt 1MB sau khi mã hoá base64.
        var request = new IpcRequest(
            "req-1", CommandType.Deploy,
            ProtocolJson.Serialize(new DeployRequest(
                new byte[2 * 1024 * 1024],
                new List<TagRoute>(),
                new List<DeviceSpec>())));

        string json = ProtocolJson.Serialize(request);
        Assert.True(Encoding.UTF8.GetByteCount(json) > 1024 * 1024);

        using var stream = new MemoryStream();
        await IpcFraming.WriteMessageAsync(stream, json);
        stream.Position = 0;

        string? roundTrip = await IpcFraming.ReadMessageAsync(stream);
        var decoded = ProtocolJson.Deserialize<IpcRequest>(roundTrip);
        var payload = ProtocolJson.Deserialize<DeployRequest>(decoded!.PayloadJson);

        Assert.Equal(2 * 1024 * 1024, payload!.AssemblyBytes.Length);
    }

    [Fact]
    public async Task Framing_PayloadChuaXuongDong_VanDocDungMotBanTin()
    {
        // Lý do không dùng StreamReader.ReadLine: base64 xuống dòng sẽ cắt bản tin làm đôi.
        string payload = "dòng một\ndòng hai\r\ndòng ba";

        using var stream = new MemoryStream();
        await IpcFraming.WriteMessageAsync(stream, payload);
        stream.Position = 0;

        Assert.Equal(payload, await IpcFraming.ReadMessageAsync(stream));
    }

    [Fact]
    public async Task Framing_StreamRong_TraNull()
    {
        using var stream = new MemoryStream();
        Assert.Null(await IpcFraming.ReadMessageAsync(stream));
    }

    [Fact]
    public async Task Framing_DoDaiBaoLon_NemLoiThayViCapPhatHetRAM()
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0x7F, 0xFF, 0xFF, 0xFF });    // ~2GB
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => IpcFraming.ReadMessageAsync(stream));
    }

    [Fact]
    public async Task Framing_DutGiuaChung_NemEndOfStream()
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 0, 0, 0, 100 });               // hứa 100 byte
        stream.Write(new byte[10]);                              // chỉ gửi 10
        stream.Position = 0;

        await Assert.ThrowsAsync<EndOfStreamException>(() => IpcFraming.ReadMessageAsync(stream));
    }

    // ── Serialize ────────────────────────────────────────────────────────────────

    [Fact]
    public void Json_EnumGhiBangTen_LogDocDuoc()
    {
        string json = ProtocolJson.Serialize(new IpcRequest("r1", CommandType.Deploy, null));

        Assert.Contains("\"Deploy\"", json);
        Assert.DoesNotContain("\"type\":2", json);
    }

    /// <summary>
    /// Client phân biệt response với push bằng cách dò trường <c>requestId</c> trên
    /// <c>JsonDocument</c> — phép dò đó PHÂN BIỆT hoa/thường. Nếu chính sách đặt tên đổi, mọi
    /// response biến thành push và mọi lệnh treo vô hạn, không có exception nào để lần ra.
    /// </summary>
    [Fact]
    public void Json_ResponseCoTruongRequestId_PushThiKhong()
    {
        string response = ProtocolJson.Serialize(IpcResponse.Success("r1"));
        string push = ProtocolJson.Serialize(new IpcPush("tags", "{}"));

        using var responseDoc = System.Text.Json.JsonDocument.Parse(response);
        using var pushDoc = System.Text.Json.JsonDocument.Parse(push);

        Assert.True(responseDoc.RootElement.TryGetProperty(ProtocolJson.RequestIdProperty, out _));
        Assert.False(pushDoc.RootElement.TryGetProperty(ProtocolJson.RequestIdProperty, out _));

        // Request cũng mang requestId — dùng chung khoá này cho cả hai chiều.
        using var requestDoc = System.Text.Json.JsonDocument.Parse(
            ProtocolJson.Serialize(new IpcRequest("r1", CommandType.GetStatus, null)));

        Assert.True(requestDoc.RootElement.TryGetProperty(ProtocolJson.RequestIdProperty, out _));
    }

    [Fact]
    public void Json_TagRouteRoundTrip_GiuNguyenKieuVaChieu()
    {
        var original = new TagRoute("Temperature", TagDataType.Real, TagDirection.Input, "PLC_1", "40001");

        var decoded = ProtocolJson.Deserialize<TagRoute>(ProtocolJson.Serialize(original));

        Assert.Equal(original, decoded);
    }
}
