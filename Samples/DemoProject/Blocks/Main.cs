using DBI.Controller.SDK;

namespace UserProgram;

public class Main : ControllerProgram
{
    public override void Execute()
    {
        // Start: nút nhấn giữ băng tải chạy tới khi Stop
        if (IO.StartButton && !IO.StopButton)
            IO.ConveyorRun = true;
        if (IO.StopButton)
            IO.ConveyorRun = false;
    }
}
