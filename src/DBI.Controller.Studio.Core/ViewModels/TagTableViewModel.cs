using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DBI.Controller.Studio.Core.Models;
using DBI.Controller.Studio.Core.Services;

namespace DBI.Controller.Studio.Core.ViewModels;

public sealed class TagRowViewModel : ObservableObject, INotifyDataErrorInfo
{
    private readonly DbiProject _project;
    private readonly Tag _tag;
    private readonly Action _changed;
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public TagRowViewModel(DbiProject project, Tag tag, Action changed)
    {
        _project = project;
        _tag = tag;
        _changed = changed;
    }

    public Tag Tag => _tag;

    public string Name
    {
        get => _tag.Name;
        set
        {
            if (_tag.Name == value) return;
            _tag.Name = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public TagDataType DataType
    {
        get => _tag.DataType;
        set
        {
            if (_tag.DataType == value) return;
            _tag.DataType = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public TagDirection Direction
    {
        get => _tag.Direction;
        set
        {
            if (_tag.Direction == value) return;
            _tag.Direction = value;
            if (value == TagDirection.Memory)
            {
                _tag.Device = string.Empty;
                _tag.Address = string.Empty;
                OnPropertyChanged(nameof(Device));
                OnPropertyChanged(nameof(Address));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDeviceEnabled));
            _changed();
        }
    }

    public string Device
    {
        get => _tag.Device;
        set
        {
            if (_tag.Device == value) return;
            _tag.Device = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public string Address
    {
        get => _tag.Address;
        set
        {
            if (_tag.Address == value) return;
            _tag.Address = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public string Comment
    {
        get => _tag.Comment;
        set
        {
            if (_tag.Comment == value) return;
            _tag.Comment = value;
            OnPropertyChanged();
            _changed();
        }
    }

    public bool IsDeviceEnabled => Direction != TagDirection.Memory;
    public bool HasErrors => _errors.Count > 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return _errors.Values.SelectMany(e => e).ToList();

        return _errors.TryGetValue(propertyName, out var list) ? list : Array.Empty<string>();
    }

    public void ApplyErrors(TagFieldErrors errors)
    {
        ReplaceErrors(nameof(Name), errors.NameErrors);
        ReplaceErrors(nameof(Device), errors.DeviceErrors);
        ReplaceErrors(nameof(Address), errors.AddressErrors);
    }

    private void ReplaceErrors(string propertyName, IReadOnlyList<string> messages)
    {
        if (messages.Count == 0) _errors.Remove(propertyName);
        else _errors[propertyName] = messages.ToList();

        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnPropertyChanged(nameof(HasErrors));
    }
}

public sealed partial class TagTableViewModel : DocumentViewModelBase
{
    private readonly DbiProject _project;
    private readonly TagTable _table;
    private readonly IoCodeGenerator _generator;

    public TagTableViewModel(DbiProject project, TagTable table, IoCodeGenerator generator)
        : base(ContentIdFor(table), table.Name)
    {
        _project = project;
        _table = table;
        _generator = generator;

        Rows = new ObservableCollection<TagRowViewModel>(
            table.Tags.Select(t => new TagRowViewModel(project, t, OnRowsChanged)));

        AvailableDevices = new ObservableCollection<string>(_project.Devices.Select(d => d.Name));
        Revalidate();
    }

    public ObservableCollection<TagRowViewModel> Rows { get; }
    public ObservableCollection<string> AvailableDevices { get; }
    public IReadOnlyList<TagDataType> DataTypes { get; } = Enum.GetValues<TagDataType>();
    public IReadOnlyList<TagDirection> Directions { get; } = Enum.GetValues<TagDirection>();

    [ObservableProperty]
    private TagRowViewModel? _selectedRow;

    public event EventHandler<IReadOnlyList<ValidationIssue>>? ValidationIssuesChanged;
    public event EventHandler<CodeGenerationResult>? GeneratedCodeChanged;

    public static string ContentIdFor(TagTable table) => "TagTable:" + table.Name;

    [RelayCommand]
    private void AddRow()
    {
        var tag = new Tag
        {
            Name = "NewTag",
            DataType = TagDataType.Bool,
            Direction = TagDirection.Input
        };

        _table.Tags.Add(tag);
        var row = new TagRowViewModel(_project, tag, OnRowsChanged);
        Rows.Add(row);
        SelectedRow = row;
        OnRowsChanged();
    }

    [RelayCommand]
    private void DeleteRow()
    {
        if (SelectedRow is null) return;

        _table.Tags.Remove(SelectedRow.Tag);
        Rows.Remove(SelectedRow);
        SelectedRow = Rows.LastOrDefault();
        OnRowsChanged();
    }

    public override Task SaveAsync()
    {
        var result = _generator.WriteIfChanged(_project);
        GeneratedCodeChanged?.Invoke(this, result);
        IsDirty = false;
        return Task.CompletedTask;
    }

    private void OnRowsChanged()
    {
        _project.IsDirty = true;
        IsDirty = true;
        Revalidate();
    }

    private void Revalidate()
    {
        foreach (var row in Rows)
            row.ApplyErrors(TagValidationService.ValidateFieldErrors(_project, row.Tag));

        ValidationIssuesChanged?.Invoke(this, TagValidationService.ValidateProject(_project));
    }
}
