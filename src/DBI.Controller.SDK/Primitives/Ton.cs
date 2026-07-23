namespace DBI.Controller.SDK.Primitives;

/// <summary>
/// Timer On-Delay (TON): Trễ tín hiệu bật ngõ ra Q sau thời gian PtMs (milisecond).
/// </summary>
public class Ton
{
    private bool _in;
    private long _startTimeMs;

    public int PtMs { get; set; }
    public int EtMs { get; private set; }
    public bool Q { get; private set; }

    public bool In
    {
        get => _in;
        set => Update(value);
    }

    public Ton(int ptMs = 0)
    {
        PtMs = ptMs;
    }

    public bool Update(bool inSignal, int deltaTimeMs = 20)
    {
        if (!inSignal)
        {
            _in = false;
            EtMs = 0;
            Q = false;
            return false;
        }

        if (!_in)
        {
            _in = true;
            _startTimeMs = Environment.TickCount64;
            EtMs = 0;
            Q = false;
        }
        else
        {
            long elapsed = Environment.TickCount64 - _startTimeMs;
            EtMs = (int)Math.Min(elapsed, PtMs);
            if (EtMs >= PtMs)
            {
                Q = true;
            }
        }

        return Q;
    }
}
