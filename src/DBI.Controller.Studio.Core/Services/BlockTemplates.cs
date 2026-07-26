using DBI.Controller.Studio.Core.Models;

namespace DBI.Controller.Studio.Core.Services;

/// <summary>
/// Nội dung mẫu cho khối mới. Mọi mẫu phải biên dịch được ngay mà không cần sửa gì.
/// </summary>
public static class BlockTemplates
{
    /// <summary>Namespace của toàn bộ code người dùng — cố định để phase-07 nạp assembly biết chỗ tìm.</summary>
    public const string UserNamespace = "UserProgram";

    public static string For(BlockKind kind, string blockName) => kind switch
    {
        BlockKind.Main => Main(blockName),
        BlockKind.FunctionBlock => FunctionBlock(blockName),
        BlockKind.Function => Function(blockName),
        BlockKind.DataBlock => DataBlock(blockName),
        _ => FunctionBlock(blockName)
    };

    /// <summary>Khối chính — Runtime gọi <c>Execute()</c> mỗi chu kỳ quét (tương tự OB1).</summary>
    public static string Main(string blockName = "Main") => $$"""
        using DBI.Controller.SDK;
        using DBI.Controller.SDK.Primitives;

        namespace {{UserNamespace}};

        /// <summary>
        /// Khối chính. Runtime gọi Execute() mỗi chu kỳ quét (mặc định 20ms).
        /// </summary>
        public class {{blockName}} : ControllerProgram
        {
            /// <summary>Chạy đúng một lần khi Runtime khởi động.</summary>
            public override void OnStart()
            {
            }

            /// <summary>Vòng lặp quét chính. Không đặt việc nặng hay chờ I/O ở đây.</summary>
            public override void Execute()
            {
                // Ví dụ: IO["ConveyorRun"] = IO["StartButton"];
            }

            /// <summary>Chạy đúng một lần khi Runtime dừng.</summary>
            public override void OnStop()
            {
            }
        }
        """;

    /// <summary>Khối chức năng có trạng thái riêng — giữ timer, counter, cờ nội bộ giữa các chu kỳ.</summary>
    public static string FunctionBlock(string blockName) => $$"""
        using DBI.Controller.SDK.IO;
        using DBI.Controller.SDK.Primitives;

        namespace {{UserNamespace}};

        /// <summary>
        /// Khối chức năng có trạng thái. Khối Main tạo một thể hiện rồi gọi Execute() mỗi chu kỳ.
        /// </summary>
        public class {{blockName}}
        {
            public void Execute(IOContainer IO)
            {
            }
        }
        """;

    /// <summary>Hàm thuần không trạng thái — vào ra rõ ràng, gọi lúc nào cũng cho cùng kết quả.</summary>
    public static string Function(string blockName) => $$"""
        namespace {{UserNamespace}};

        /// <summary>
        /// Hàm thuần, không giữ trạng thái giữa các chu kỳ.
        /// </summary>
        public static class {{blockName}}
        {
            public static int Compute(int input) => input;
        }
        """;

    /// <summary>Khối dữ liệu — hằng số công thức, tham số máy.</summary>
    public static string DataBlock(string blockName) => $$"""
        namespace {{UserNamespace}};

        /// <summary>
        /// Khối dữ liệu: hằng số công thức, tham số máy.
        /// </summary>
        public static class {{blockName}}
        {
            public const int DefaultSpeed = 100;
        }
        """;

    /// <summary>
    /// <c>.gitignore</c> sinh kèm project mới. <c>.dbistudio/</c> chứa layout AvalonDock của
    /// từng máy — không commit.
    /// </summary>
    public const string GitIgnore = """
        # Layout cửa sổ của DBI.Studio — riêng từng máy, không commit
        .dbistudio/

        # Kết quả biên dịch
        bin/
        obj/
        """;
}
