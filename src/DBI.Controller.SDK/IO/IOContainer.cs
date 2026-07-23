using System.Dynamic;
using DBI.Controller.Core.Interfaces;

namespace DBI.Controller.SDK.IO;

/// <summary>
/// Container cho phép truy cập linh hoạt thuộc tính IO dạng Dynamic Object (IO.StartButton) hoặc Indexer (IO["StartButton"]).
/// </summary>
public class IOContainer : DynamicObject
{
    private readonly IMemoryImage _memoryImage;

    public IOContainer(IMemoryImage memoryImage)
    {
        _memoryImage = memoryImage ?? throw new ArgumentNullException(nameof(memoryImage));
    }

    public bool this[string key]
    {
        get => _memoryImage.GetBool(key);
        set => _memoryImage.SetBool(key, value);
    }

    public bool GetBool(string key) => _memoryImage.GetBool(key);
    public void SetBool(string key, bool value) => _memoryImage.SetBool(key, value);

    public int GetInt(string key) => _memoryImage.GetInt(key);
    public void SetInt(string key, int value) => _memoryImage.SetInt(key, value);

    public float GetFloat(string key) => _memoryImage.GetFloat(key);
    public void SetFloat(string key, float value) => _memoryImage.SetFloat(key, value);

    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        result = _memoryImage.GetBool(binder.Name);
        return true;
    }

    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        if (value is bool boolVal)
        {
            _memoryImage.SetBool(binder.Name, boolVal);
            return true;
        }
        if (value is int intVal)
        {
            _memoryImage.SetInt(binder.Name, intVal);
            return true;
        }
        if (value is float floatVal)
        {
            _memoryImage.SetFloat(binder.Name, floatVal);
            return true;
        }

        return false;
    }
}
