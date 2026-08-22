namespace DBI.Controller.Studio.Core.Services;

/// <summary>
/// Ánh xạ hai chiều giữa offset ký tự và vị trí (dòng, cột) trên văn bản thuần.
/// </summary>
/// <remarks>
/// Offset là offset .NET/AvalonEdit chuẩn: <b>zero-based</b>, chuỗi <c>"\r\n"</c> chiếm
/// <b>hai</b> ký tự offset, ranh giới dòng nằm sau <c>'\n'</c>. Line/column là 1-based.
/// Mọi giá trị ngoài phạm vi đều clamp an toàn — tầng gọi không cần tự kiểm trước.
/// Thuần string để tầng Core test được không cần WPF.
/// </remarks>
public static class TextPositionMath
{
    /// <summary>Đổi vị trí (1-based) thành offset zero-based; clamp về biên khi vượt phạm vi.</summary>
    public static int PositionToOffset(string text, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || line <= 1 && column <= 1) return 0;

        int currentLine = 1;
        int i = 0;
        while (i < text.Length && currentLine < line)
        {
            if (text[i] == '\n') currentLine++;
            i++;
        }

        // Vượt số dòng → clamp cuối văn bản.
        if (currentLine < line) return text.Length;

        // Đi tiếp theo cột trong dòng hiện tại (dừng tại '\n').
        int columnStart = i;
        int targetColumn = Math.Max(1, column);
        while (i < text.Length && text[i] != '\n' && i - columnStart < targetColumn - 1)
            i++;

        return i;
    }

    /// <summary>Đổi offset zero-based thành (line, column) 1-based; clamp về cuối văn bản khi vượt độ dài.</summary>
    public static (int Line, int Column) OffsetToPosition(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return (1, 1);

        offset = Math.Clamp(offset, 0, text.Length);

        int line = 1;
        int lastLineStart = 0;
        for (int i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lastLineStart = i + 1;
            }
        }

        return (line, offset - lastLineStart + 1);
    }
}
