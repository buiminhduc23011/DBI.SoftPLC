namespace DBI.Controller.SDK.Primitives;

/// <summary>
/// Count Up Counter (CTU): Đếm tăng khi có xung cạnh lên tại ngõ vào CU. Đạt Preset Value (PV) -> Q = TRUE.
/// </summary>
public class CounterUp
{
    private readonly RisingEdge _edge = new();

    public int Value { get; private set; }
    public int PresetValue { get; set; }
    public bool Q => Value >= PresetValue;

    public CounterUp(int presetValue = 0)
    {
        PresetValue = presetValue;
    }

    public void Update(bool cuSignal, bool reset = false)
    {
        if (reset)
        {
            Value = 0;
            return;
        }

        if (_edge.Update(cuSignal))
        {
            Value++;
        }
    }
}
