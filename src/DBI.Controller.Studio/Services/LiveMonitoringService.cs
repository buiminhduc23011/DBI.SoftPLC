using System.Collections.Concurrent;

namespace DBI.Controller.Studio.Services;

public class LiveTagState
{
    public string TagName { get; set; } = string.Empty;
    public string Value { get; set; } = "FALSE";
    public bool IsTrue { get; set; }
}

public class LiveMonitoringService
{
    private readonly ConcurrentDictionary<string, LiveTagState> _liveTags = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<IReadOnlyDictionary<string, LiveTagState>>? LiveStateUpdated;

    public bool IsOnline { get; private set; }

    public void GoOnline()
    {
        IsOnline = true;
    }

    public void GoOffline()
    {
        IsOnline = false;
    }

    public void UpdateTagValue(string tagName, bool value)
    {
        _liveTags[tagName] = new LiveTagState
        {
            TagName = tagName,
            Value = value ? "TRUE" : "FALSE",
            IsTrue = value
        };

        if (IsOnline)
        {
            LiveStateUpdated?.Invoke(this, _liveTags);
        }
    }

    public LiveTagState GetTagState(string tagName)
    {
        return _liveTags.TryGetValue(tagName, out var state)
            ? state
            : new LiveTagState { TagName = tagName, Value = "FALSE", IsTrue = false };
    }
}
