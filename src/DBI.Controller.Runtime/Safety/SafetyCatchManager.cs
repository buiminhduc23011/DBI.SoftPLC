using DBI.Controller.Core.Interfaces;
using DBI.Controller.Runtime.Drivers;

namespace DBI.Controller.Runtime.Safety;

public class SafetyCatchManager
{
    public bool IsFaulted { get; private set; }
    public Exception? LastException { get; private set; }

    public async Task HandleUnhandledExceptionAsync(Exception ex, IMemoryImage memoryImage, DriverManager driverManager)
    {
        IsFaulted = true;
        LastException = ex;

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n🚨 EMERGENCY SAFETY TRIGGERED: User logic crash!");
        Console.WriteLine($"Lỗi: {ex.GetType().Name} - {ex.Message}");
        Console.ResetColor();

        // 1. Force Clear all Outputs in Memory Image to Safe State (False / 0)
        memoryImage.ClearAllOutputs();
        memoryImage.SwapOutputBuffers();

        // 2. Emergency Write Outputs to Physical Devices
        try
        {
            await driverManager.WriteOutputsAsync(memoryImage);
            Console.WriteLine("⚠️ Đã xả toàn bộ Output về 0 (Safe State) xuống thiết bị thành công.");
        }
        catch (Exception driverEx)
        {
            Console.WriteLine($"❌ Lỗi ghi khẩn cấp Output xuống Driver: {driverEx.Message}");
        }
    }

    public void ResetFault()
    {
        IsFaulted = false;
        LastException = null;
    }
}
