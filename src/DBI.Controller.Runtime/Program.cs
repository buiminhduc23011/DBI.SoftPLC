using DBI.Controller.Protocol;
using DBI.Controller.Runtime.Host;
using DBI.Controller.Runtime.Ipc;

namespace DBI.Controller.Runtime;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var options = RuntimeOptions.Parse(args);

        if (options.ShowHelp)
        {
            Console.WriteLine(RuntimeOptions.HelpText);
            return 0;
        }

        PrintBanner(options);

        using var host = new RuntimeHost(scanIntervalMs: options.ScanIntervalMs);
        await using var server = new IpcServer(host, options.PipeName);

        using var shutdown = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;       // tự dọn dẹp, không để OS giết ngang khi output còn đang bật
            shutdown.Cancel();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => shutdown.Cancel();

        // ADR-005 — dựng lại lần deploy gần nhất. Hai chốt chặn an toàn nằm trong DeploymentStore.
        bool autoStarted = await host.TryRestoreLastDeploymentAsync(shutdown.Token);

        Console.WriteLine(autoStarted
            ? "[+] Đã nạp lại chương trình lần trước và tự chạy."
            : $"[+] Trạng thái khởi động: {host.State}. Chờ Studio kết nối qua pipe '{options.PipeName}'.");

        try
        {
            await server.RunAsync(shutdown.Token);
        }
        catch (OperationCanceledException)
        {
            // Dừng theo yêu cầu.
        }

        Console.WriteLine("[-] Runtime đã dừng, output đã xả về trạng thái an toàn.");
        return 0;
    }

    private static void PrintBanner(RuntimeOptions options)
    {
        if (options.Headless) return;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("        DBI.CONTROLLER SOFT PLC RUNTIME v1.0     ");
        Console.WriteLine($"        Scan {options.ScanIntervalMs}ms · pipe '{options.PipeName}'");
        Console.WriteLine("=================================================");
        Console.ResetColor();
    }
}

internal record RuntimeOptions(string PipeName, int Port, int ScanIntervalMs, bool Headless, bool ShowHelp)
{
    public const string HelpText = """
        DBI.Controller Soft PLC Runtime

          --pipe <tên>       Tên NamedPipe để Studio kết nối (mặc định: DBI.Runtime)
          --port <số>        Cổng TCP cho kết nối từ xa (dành cho phase-13+)
          --scan <ms>        Chu kỳ quét, milli giây (mặc định: 20)
          --headless         Không in banner, chỉ ghi log
          --help             Hiện trợ giúp này

        Phanh tay bảo trì: tạo tệp .norun trong thư mục last-deploy để Runtime
        luôn khởi động ở trạng thái dừng, kể cả khi project bật AutoStart.
        """;

    public static RuntimeOptions Parse(string[] args)
    {
        string pipeName = ProtocolConstants.DefaultPipeName;
        int port = ProtocolConstants.DefaultTcpPort;
        int scanIntervalMs = 20;
        bool headless = false;
        bool showHelp = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--pipe" when i + 1 < args.Length:
                    pipeName = args[++i];
                    break;
                case "--port" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out int parsedPort)) port = parsedPort;
                    break;
                case "--scan" when i + 1 < args.Length:
                    if (int.TryParse(args[++i], out int parsedScan) && parsedScan > 0) scanIntervalMs = parsedScan;
                    break;
                case "--headless":
                    headless = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
            }
        }

        return new RuntimeOptions(pipeName, port, scanIntervalMs, headless, showHelp);
    }
}
