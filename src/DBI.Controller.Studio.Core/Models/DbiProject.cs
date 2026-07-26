using System.Text.Json.Serialization;

namespace DBI.Controller.Studio.Core.Models;

/// <summary>
/// Mô hình một project của DBI.Studio — nội dung của tệp <c>*.dbiproj</c>.
/// </summary>
/// <remarks>
/// <c>.dbiproj</c> chỉ chứa <b>metadata + tag + device</b>. Nội dung code nằm ở file <c>.cs</c> thật
/// trong <c>Blocks/</c> để Git diff đọc được (Constraint C-7).
/// </remarks>
public class DbiProject
{
    /// <summary>Version schema Studio hiện hỗ trợ. Tăng major khi phá tương thích.</summary>
    public const string CurrentSchemaVersion = "1.0";

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("runtime")]
    public RuntimeTarget Runtime { get; set; } = new();

    [JsonPropertyName("devices")]
    public List<DeviceConfig> Devices { get; set; } = new();

    [JsonPropertyName("tagTables")]
    public List<TagTable> TagTables { get; set; } = new();

    [JsonPropertyName("blocks")]
    public List<CodeBlock> Blocks { get; set; } = new();

    [JsonPropertyName("watchTables")]
    public List<WatchTable> WatchTables { get; set; } = new();

    /// <summary>Đường dẫn tuyệt đối tới tệp <c>.dbiproj</c>. Không serialize.</summary>
    [JsonIgnore]
    public string ProjectFilePath { get; set; } = "";

    [JsonIgnore]
    public bool IsDirty { get; set; }

    /// <summary>Thư mục gốc của project (nơi chứa <c>Blocks/</c>, <c>Generated/</c>).</summary>
    [JsonIgnore]
    public string ProjectDirectory =>
        string.IsNullOrEmpty(ProjectFilePath) ? "" : Path.GetDirectoryName(ProjectFilePath) ?? "";

    /// <summary>Mọi tag của mọi bảng tag, gộp lại.</summary>
    public IEnumerable<Tag> AllTags() => TagTables.SelectMany(t => t.Tags);
}

/// <summary>Runtime mà project này deploy xuống.</summary>
public class RuntimeTarget
{
    [JsonPropertyName("transportType")]
    public string TransportType { get; set; } = "NamedPipe";   // NamedPipe | Tcp

    [JsonPropertyName("pipeName")]
    public string PipeName { get; set; } = "DBI.Runtime";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "127.0.0.1";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 5580;

    [JsonPropertyName("scanIntervalMs")]
    public int ScanIntervalMs { get; set; } = 20;

    /// <summary>
    /// ⚠️ ADR-005 — máy tự chạy lại sau mất điện. Có hai chốt chặn ở Runtime:
    /// không tự chạy sau Fault, và tệp phanh tay <c>.norun</c>.
    /// </summary>
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; } = true;
}

/// <summary>Một thiết bị phần cứng (hoặc giả lập) mà tag trỏ tới.</summary>
public class DeviceConfig
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";               // "FactoryIO_3D"

    [JsonPropertyName("driverType")]
    public string DriverType { get; set; } = "";         // "DBI.Controller.Driver.FactoryIO"

    [JsonPropertyName("settings")]
    public Dictionary<string, string> Settings { get; set; } = new();   // ip, port, slaveId...
}

/// <summary>Một bảng tag. Tag Table là nguồn sự thật duy nhất (ADR-002).</summary>
public class TagTable
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Default Tag Table";

    [JsonPropertyName("tags")]
    public List<Tag> Tags { get; set; } = new();
}

public class Tag
{
    /// <summary>Phải là C# identifier hợp lệ — phase-06 sinh property cùng tên trong <c>IO.g.cs</c>.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("dataType")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TagDataType DataType { get; set; } = TagDataType.Bool;

    [JsonPropertyName("direction")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TagDirection Direction { get; set; } = TagDirection.Input;

    /// <summary>Rỗng khi <see cref="Direction"/> = <see cref="TagDirection.Memory"/>.</summary>
    [JsonPropertyName("device")]
    public string Device { get; set; } = "";

    /// <summary>Địa chỉ thô trên thiết bị: <c>Input_0</c> | <c>40001</c> | <c>D100</c>.</summary>
    [JsonPropertyName("address")]
    public string Address { get; set; } = "";

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = "";
}

public enum TagDataType { Bool, Int, Real }

public enum TagDirection { Input, Output, Memory }

/// <summary>Một khối logic — tương ứng một file <c>.cs</c> thật trong <c>Blocks/</c>.</summary>
public class CodeBlock
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";               // "Conveyor"

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BlockKind Kind { get; set; } = BlockKind.FunctionBlock;

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";           // "Blocks/Conveyor.cs" — đường dẫn tương đối

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = "";
}

public enum BlockKind { Main, FunctionBlock, Function, DataBlock }

/// <summary>Bảng theo dõi giá trị tag lúc chạy (phase-10).</summary>
public class WatchTable
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Watch table_1";

    [JsonPropertyName("tagNames")]
    public List<string> TagNames { get; set; } = new();
}
