using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DBI.Controller.Protocol;

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        // Enum ghi bằng tên: log IPC đọc được, và thêm giá trị enum không làm lệch ý nghĩa số cũ.
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

        // camelCase là hợp đồng trên dây, không phải chuyện thẩm mỹ: phía nhận phân biệt response
        // với push bằng cách dò trường "requestId" trên JsonDocument — mà phép dò đó PHÂN BIỆT
        // hoa/thường. Đổi chính sách này là mọi response biến thành push và lệnh treo vô hạn.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Tên trường dùng để phân biệt response với push. Xem ghi chú ở <see cref="Options"/>.</summary>
    public const string RequestIdProperty = "requestId";

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string? json) =>
        string.IsNullOrEmpty(json) ? default : JsonSerializer.Deserialize<T>(json, Options);
}

/// <summary>
/// Đóng/mở khung bản tin trên stream: <c>[4 byte độ dài big-endian][UTF-8 JSON]</c>.
/// </summary>
/// <remarks>
/// ⚠️ <b>Không</b> dùng <c>StreamReader.ReadLine</c>: payload <c>Deploy</c> chứa assembly mã hoá
/// base64 dài hàng MB và có thể chứa ký tự xuống dòng — đọc theo dòng sẽ cắt bản tin làm đôi.
/// </remarks>
public static class IpcFraming
{
    public static async Task WriteMessageAsync(Stream stream, string json, CancellationToken ct = default)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);

        if (payload.Length > ProtocolConstants.MaxMessageBytes)
        {
            throw new InvalidOperationException(
                $"Bản tin {payload.Length:N0} byte vượt giới hạn {ProtocolConstants.MaxMessageBytes:N0} byte.");
        }

        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);

        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Đọc một bản tin. Trả <c>null</c> khi đầu kia đã đóng kết nối sạch sẽ.</summary>
    public static async Task<string?> ReadMessageAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[4];
        if (!await ReadExactlyAsync(stream, header, ct).ConfigureAwait(false))
            return null;

        int length = BinaryPrimitives.ReadInt32BigEndian(header);

        if (length < 0 || length > ProtocolConstants.MaxMessageBytes)
        {
            throw new InvalidDataException(
                $"Độ dài bản tin không hợp lệ: {length}. Kết nối có thể đã lệch khung.");
        }

        if (length == 0) return string.Empty;

        byte[] payload = new byte[length];
        if (!await ReadExactlyAsync(stream, payload, ct).ConfigureAwait(false))
            throw new EndOfStreamException("Kết nối đứt giữa lúc đang đọc bản tin.");

        return Encoding.UTF8.GetString(payload);
    }

    /// <summary>
    /// Đọc đủ <paramref name="buffer"/>. NamedPipe trả về từng phần, không đọc đủ sẽ lệch khung
    /// và mọi bản tin sau đó đều hỏng.
    /// </summary>
    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);

            if (read == 0)
                return offset == 0 ? false : throw new EndOfStreamException("Kết nối đứt giữa chừng.");

            offset += read;
        }

        return true;
    }
}
