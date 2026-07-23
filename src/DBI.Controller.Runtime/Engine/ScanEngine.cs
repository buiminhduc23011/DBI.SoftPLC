using System.Diagnostics;
using DBI.Controller.Core.Interfaces;
using DBI.Controller.Core.Models;
using DBI.Controller.Runtime.Drivers;
using DBI.Controller.Runtime.Safety;
using DBI.Controller.SDK;

namespace DBI.Controller.Runtime.Engine;

public class ScanMetrics
{
    public long CycleCount { get; set; }
    public double LastScanTimeMs { get; set; }
    public double MaxScanTimeMs { get; set; }
    public double JitterMs { get; set; }
}

public class ScanEngine
{
    private readonly IMemoryImage _memoryImage;
    private readonly DriverManager _driverManager;
    private readonly SafetyCatchManager _safetyCatchManager;
    private ControllerProgram? _program;
    private bool _isRunning;
    private Thread? _engineThread;

    public int ScanIntervalMs { get; set; } = 20;
    public ScanMetrics Metrics { get; } = new();
    public bool IsRunning => _isRunning;

    public ScanEngine(IMemoryImage memoryImage, DriverManager driverManager, SafetyCatchManager safetyCatchManager)
    {
        _memoryImage = memoryImage ?? throw new ArgumentNullException(nameof(memoryImage));
        _driverManager = driverManager ?? throw new ArgumentNullException(nameof(driverManager));
        _safetyCatchManager = safetyCatchManager ?? throw new ArgumentNullException(nameof(safetyCatchManager));
    }

    public void SetProgram(ControllerProgram program)
    {
        _program = program;
    }

    public void Start()
    {
        if (_isRunning) return;
        if (_program == null) throw new InvalidOperationException("Chưa thiết lập ControllerProgram cho ScanEngine.");

        _isRunning = true;
        _engineThread = new Thread(RunLoop)
        {
            Name = "DBI_SoftPLC_ScanEngine",
            Priority = ThreadPriority.Highest,
            IsBackground = true
        };
        _engineThread.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        _engineThread?.Join(1000);
    }

    private void RunLoop()
    {
        var stopwatch = new Stopwatch();

        while (_isRunning && !_safetyCatchManager.IsFaulted)
        {
            stopwatch.Restart();

            try
            {
                // 1. Sync Read Inputs from Drivers into Memory InputBuffer
                _driverManager.ReadInputsAsync(_memoryImage).GetAwaiter().GetResult();

                // 2. Swap Input Buffers (InputBuffer -> InputSnapshot)
                _memoryImage.SwapInputBuffers();

                // 3. Execute User Logic (ControllerProgram.Execute())
                _program?.Execute();

                // 4. Swap Output Buffers (OutputSnapshot -> OutputBuffer)
                _memoryImage.SwapOutputBuffers();

                // 5. Sync Write Outputs from Memory Snapshot to Drivers
                _driverManager.WriteOutputsAsync(_memoryImage).GetAwaiter().GetResult();

                Metrics.CycleCount++;
            }
            catch (Exception ex)
            {
                _safetyCatchManager.HandleUnhandledExceptionAsync(ex, _memoryImage, _driverManager).GetAwaiter().GetResult();
                _isRunning = false;
                break;
            }

            stopwatch.Stop();
            double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

            Metrics.LastScanTimeMs = elapsedMs;
            Metrics.MaxScanTimeMs = Math.Max(Metrics.MaxScanTimeMs, elapsedMs);
            Metrics.JitterMs = Math.Abs(elapsedMs - ScanIntervalMs);

            // Precision timing wait for remaining frame duration
            int sleepTimeMs = ScanIntervalMs - (int)elapsedMs;
            if (sleepTimeMs > 0)
            {
                Thread.Sleep(sleepTimeMs);
            }
        }
    }
}
