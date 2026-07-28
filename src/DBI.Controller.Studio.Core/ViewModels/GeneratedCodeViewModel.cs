namespace DBI.Controller.Studio.Core.ViewModels;

public sealed partial class GeneratedCodeViewModel : DocumentViewModelBase
{
    public const string GeneratedContentId = "Generated:IO.g.cs";

    public GeneratedCodeViewModel(string absolutePath, string text)
        : base(GeneratedContentId, "IO.g.cs")
    {
        AbsolutePath = absolutePath;
        _text = text;
    }

    public string AbsolutePath { get; }
    public string BannerMessage => "Generated from the Tag Table. Edit tags in the table, not here.";

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _text;

    public override bool CanClose => true;

    public Task ReloadAsync()
    {
        if (File.Exists(AbsolutePath))
            Text = File.ReadAllText(AbsolutePath);

        IsDirty = false;
        return Task.CompletedTask;
    }
}
