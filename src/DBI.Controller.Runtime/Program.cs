using DBI.Controller.Core.Models;
using DBI.Controller.Runtime.Drivers;
using DBI.Controller.Runtime.Engine;
using DBI.Controller.Runtime.Safety;

namespace DBI.Controller.Runtime;

internal class Program
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("        DBI.CONTROLLER SOFT PLC RUNTIME v1.0     ");
        Console.WriteLine("        Architecture: Decoupled C# Automation   ");
        Console.WriteLine("=================================================");
        Console.ResetColor();

        var memoryImage = new MemorySnapshot();
        var driverManager = new DriverManager();
        var safetyCatch = new SafetyCatchManager();

        var engine = new ScanEngine(memoryImage, driverManager, safetyCatch)
        {
            ScanIntervalMs = 20
        };

        Console.WriteLine("\n[+] Initialized Soft PLC Runtime Engine (20ms Scan Cycle).");
        Console.WriteLine("[+] Ready for Plugin Assembly Loading & Driver Connection.");
        Console.WriteLine("\nNhấn Enter để dừng Runtime...");
        Console.ReadLine();

        if (engine.IsRunning)
        {
            engine.Stop();
        }
        Console.WriteLine("[-] Soft PLC Runtime has stopped gracefully.");
    }
}
