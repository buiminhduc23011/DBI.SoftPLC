namespace DBI.Controller.SDK.Primitives;

/// <summary>
/// Rising Edge Detector (R_TRIG): Bắt xung cạnh lên của tín hiệu. Trả về TRUE chỉ duy nhất trong 1 chu kỳ Scan Cycle khi tín hiệu chuyển từ FALSE -> TRUE.
/// </summary>
public class RisingEdge
{
    private bool _previousSignal;

    public bool Q { get; private set; }

    public bool Update(bool currentSignal)
    {
        Q = currentSignal && !_previousSignal;
        _previousSignal = currentSignal;
        return Q;
    }
}

/// <summary>
/// Falling Edge Detector (F_TRIG): Bắt xung cạnh xuống của tín hiệu. Trả về TRUE chỉ duy nhất trong 1 chu kỳ Scan Cycle khi tín hiệu chuyển từ TRUE -> FALSE.
/// </summary>
public class FallingEdge
{
    private bool _previousSignal;

    public bool Q { get; private set; }

    public bool Update(bool currentSignal)
    {
        Q = !currentSignal && _previousSignal;
        _previousSignal = currentSignal;
        return Q;
    }
}
