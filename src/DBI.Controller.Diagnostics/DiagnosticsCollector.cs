namespace DBI.Controller.Diagnostics;

public class DiagnosticsMetrics
{
    public long CycleCount { get; set; }
    public double LastScanTimeMs { get; set; }
    public double MaxScanTimeMs { get; set; }
    public double JitterMs { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsageMb { get; set; }
    public string Status { get; set; } = "RUNNING";
}

public class DiagnosticsCollector
{
    public event EventHandler<DiagnosticsMetrics>? MetricsUpdated;

    public void ReportMetrics(DiagnosticsMetrics metrics)
    {
        MetricsUpdated?.Invoke(this, metrics);
    }
}
