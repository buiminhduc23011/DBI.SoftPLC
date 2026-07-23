namespace DBI.Controller.SDK.Primitives;

/// <summary>
/// Timer Off-Delay (TOF): Duy trì tín hiệu bật ngõ ra Q thêm thời gian PtMs (milisecond) sau khi ngắt IN.
/// </summary>
public class Tof
{
    private bool _in;
    private long _offTimeMs;

    public int PtMs { get; set; }
    public int EtMs { get; private set; }
    public bool Q { get; private set; }

    public bool In
    {
        get => _in;
        set => Update(value);
    }

    public Tof(int ptMs = 0)
    {
        PtMs = ptMs;
    }

    public bool Update(bool inSignal)
    {
        if (inSignal)
        {
            _in = true;
            Q = true;
            EtMs = 0;
            return true;
        }

        if (_in)
        {
            _in = false;
            _offTimeMs = Environment.TickCount64;
            EtMs = 0;
            Q = true;
        }
        else if (Q)
        {
            long elapsed = Environment.TickCount64 - _offTimeMs;
            EtMs = (int)Math.Min(elapsed, PtMs);
            if (EtMs >= PtMs)
            {
                Q = false;
            }
        }

        return Q;
    }
}
