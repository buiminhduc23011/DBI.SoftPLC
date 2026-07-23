using DBI.Controller.SDK;
using DBI.Controller.SDK.Primitives;

namespace Sample.Conveyor;

public class ConveyorProgram : ControllerProgram
{
    // Timer trễ 1000ms (1 giây) trước khi dừng băng tải khi thấy hàng
    private readonly Ton _stopDelay = new(ptMs: 1000);

    public override void OnStart()
    {
        Console.WriteLine("🚀 [Sample.Conveyor] Program initialized.");
    }

    public override void Execute()
    {
        // 1. Nhấn nút StartButton -> Khởi động Băng tải
        if (IO.StartButton)
        {
            IO.ConveyorRun = true;
        }

        // 2. Nhấn nút StopButton -> Dừng Băng tải lập tức
        if (IO.StopButton)
        {
            IO.ConveyorRun = false;
        }

        // 3. Sensor phát hiện hàng -> Timer đếm 1000ms rồi ngắt băng tải
        _stopDelay.In = IO.SensorProduct;
        if (_stopDelay.Q)
        {
            IO.ConveyorRun = false;
        }
    }

    public override void OnStop()
    {
        Console.WriteLine("🛑 [Sample.Conveyor] Program stopped.");
    }
}
