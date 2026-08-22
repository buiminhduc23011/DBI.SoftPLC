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
        using System;
        using DBI.Controller.SDK;
        using DBI.Controller.SDK.IO;
        using DBI.Controller.SDK.Primitives;

        namespace {{UserNamespace}};

        /// <summary>
        /// Khối chương trình chính (Main / OB1).
        /// Runtime sẽ gọi Execute() lặp lại theo mỗi chu kỳ quét (Scan Interval).
        /// </summary>
        public class {{blockName}} : ControllerProgram
        {
            // Khai báo các khối chức năng (Function Blocks), Timer, Counter dùng chung ở đây
            // private readonly Conveyor _conveyor = new();

            /// <summary>Chạy đúng một lần duy nhất khi Runtime khởi động.</summary>
            public override void OnStart()
            {
            }

            /// <summary>Vòng lặp quét chính — thực thi liên tục mỗi chu kỳ.</summary>
            public override void Execute()
            {
                // Ví dụ điều khiển cơ bản:
                // IO["ConveyorRun"] = IO["StartButton"] && !IO["StopButton"];
            }

            /// <summary>Chạy đúng một lần khi Runtime dừng lại.</summary>
            public override void OnStop()
            {
            }
        }
        """;

    /// <summary>Khối chức năng có trạng thái riêng — giữ timer, counter, cờ nội bộ giữa các chu kỳ.</summary>
    public static string FunctionBlock(string blockName) => $$"""
        using System;
        using DBI.Controller.SDK;
        using DBI.Controller.SDK.IO;
        using DBI.Controller.SDK.Primitives;

        namespace {{UserNamespace}};

        /// <summary>
        /// Khối chức năng có trạng thái (Function Block).
        /// Giữ nguyên timer, counter, biến nội bộ giữa các chu kỳ quét.
        /// </summary>
        public class {{blockName}}
        {
            // Khai báo Timer, Counter hoặc trạng thái nội bộ tại đây
            // private readonly Ton _timer = new();
            // private readonly CounterUp _counter = new();

            /// <summary>Hàm thực thi logic của khối trong mỗi chu kỳ quét.</summary>
            public void Execute(IOContainer IO)
            {
                // Viết logic điều khiển tại đây
            }
        }
        """;

    /// <summary>Hàm thuần không trạng thái — vào ra rõ ràng, gọi lúc nào cũng cho cùng kết quả.</summary>
    public static string Function(string blockName) => $$"""
        using System;

        namespace {{UserNamespace}};

        /// <summary>
        /// Hàm tính toán thuần túy (Function) — không giữ trạng thái giữa các chu kỳ.
        /// </summary>
        public static class {{blockName}}
        {
            public static double Scale(double rawValue, double inMin, double inMax, double outMin, double outMax)
            {
                if (Math.Abs(inMax - inMin) < double.Epsilon) return outMin;
                return (rawValue - inMin) / (inMax - inMin) * (outMax - outMin) + outMin;
            }
        }
        """;

    /// <summary>Khối dữ liệu — hằng số công thức, tham số máy.</summary>
    public static string DataBlock(string blockName) => $$"""
        namespace {{UserNamespace}};

        /// <summary>
        /// Khối dữ liệu (Data Block): hằng số công thức, tham số cấu hình máy.
        /// </summary>
        public static class {{blockName}}
        {
            public const int DefaultSpeed = 100;
            public const int MaxTimeoutMs = 5000;
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
