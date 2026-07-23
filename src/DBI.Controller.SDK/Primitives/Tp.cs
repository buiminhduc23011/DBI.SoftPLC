namespace DBI.Controller.SDK.Primitives;

/// <summary>
/// Pulse Timer (TP): Phát xung đầu ra Q có độ rộng PtMs (milisecond) bất kể tín hiệu đầu vào IN duy trì bao lâu.
/// </summary>
public class Tp
{
    private bool _in;
    private bool _running;
    private long _startTimeMs;

    public int PtMs { get; set; }
    public int EtMs { get; private set; }
    public bool Q { get; private set; }

    public bool In
    {
        get => _in;
        set => Update(value);
    }

    public Tp(int ptMs = 0)
    {
        PtMs = ptMs;
    }

    public bool Update(bool inSignal)
    {
        if (inSignal && !_running && !_in)
        {
            _running = true;
            _startTimeMs = Environment.TickCount64;
            Q = true;
            EtMs = 0;
        }

        _in = inSignal;

        if (_running)
        {
            long elapsed = Environment.TickCount64 - _startTimeMs;
            EtMs = (int)Math.Min(elapsed, PtMs);
            if (EtMs >= PtMs)
            {
                _running = false;
                Q = false;
            }
        }

        return Q;
    }
}
