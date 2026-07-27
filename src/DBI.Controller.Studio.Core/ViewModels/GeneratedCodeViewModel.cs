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
    public string BannerMessage => "File sinh tự động từ Tag Table. Sửa tag trong bảng, không sửa ở đây.";

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
