using System.IO;
using AvalonDock;
using AvalonDock.Layout.Serialization;
using DBI.Controller.Studio.Core.Services;

namespace DBI.Controller.Studio.Services;

/// <summary>
/// Lưu và khôi phục bố cục AvalonDock ra <c>.dbistudio/layout.xml</c>.
/// </summary>
/// <remarks>
/// ⚠️ <c>layout.xml</c> chỉ lưu <b>cấu trúc</b> và <c>ContentId</c>, không lưu content. Không gắn
/// <see cref="XmlLayoutSerializer.LayoutSerializationCallback"/> thì mọi panel khôi phục ra rỗng
/// trơn — đúng cái bẫy spike đã ghi lại.
/// </remarks>
public class DockLayoutService : ILayoutPersistence
{
    private readonly DockingManager _dockingManager;
    private readonly Func<string?, object?> _resolveContent;
    private readonly string _defaultLayoutXml;

    public DockLayoutService(DockingManager dockingManager, Func<string?, object?> resolveContent)
    {
        _dockingManager = dockingManager ?? throw new ArgumentNullException(nameof(dockingManager));
        _resolveContent = resolveContent ?? throw new ArgumentNullException(nameof(resolveContent));

        // Chụp bố cục lúc khởi động làm mốc "mặc định" cho View → Reset Layout.
        _defaultLayoutXml = Serialize();
    }

    public void Save(string layoutFilePath)
    {
        string? directory = Path.GetDirectoryName(layoutFilePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllText(layoutFilePath, Serialize());
    }

    public bool Restore(string layoutFilePath)
    {
        if (!File.Exists(layoutFilePath)) return false;

        try
        {
            Deserialize(File.ReadAllText(layoutFilePath));
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            // layout.xml của phiên bản cũ hoặc hỏng không được chặn mở project — về mặc định là xong.
            ResetToDefault();
            return false;
        }
    }

    public void ResetToDefault() => Deserialize(_defaultLayoutXml);

    private string Serialize()
    {
        using var writer = new StringWriter();

        new XmlLayoutSerializer(_dockingManager).Serialize(writer);

        return writer.ToString();
    }

    private void Deserialize(string layoutXml)
    {
        if (string.IsNullOrWhiteSpace(layoutXml)) return;

        var serializer = new XmlLayoutSerializer(_dockingManager);

        serializer.LayoutSerializationCallback += (_, e) =>
        {
            e.Content = _resolveContent(e.Model.ContentId);

            // ContentId lạ (panel của bản Studio khác) — bỏ qua thay vì dựng panel rỗng.
            if (e.Content is null) e.Cancel = true;
        };

        using var reader = new StringReader(layoutXml);
        serializer.Deserialize(reader);
    }
}
