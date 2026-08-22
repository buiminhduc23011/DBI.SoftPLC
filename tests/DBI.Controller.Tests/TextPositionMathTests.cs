using DBI.Controller.Studio.Core.Services;

namespace DBI.Controller.Tests;

/// <summary>
/// Helper offset↔(line,column) phải dùng offset .NET chuẩn: zero-based, "\r\n" chiếm ĐÚNG HAI
/// ký tự offset, ranh giới dòng nằm sau '\n'. Sai một ký tự là chèn lệch vị trí trong file CRLF
/// (chuẩn của editor WPF) — đây là test canh.
/// </summary>
public class TextPositionMathTests
{
    private const string CrlfText = "abc\r\ndef\r\nghi";
    private const string LfText = "abc\ndef\nghi";

    [Fact]
    public void PositionToOffset_CRLF_Dong2BatDauTai5()
    {
        Assert.Equal(5, TextPositionMath.PositionToOffset(CrlfText, 2, 1));
    }

    [Fact]
    public void OffsetToPosition_CRLF_Offset4LaCuoiDong1()
    {
        Assert.Equal((1, 5), TextPositionMath.OffsetToPosition(CrlfText, 4));
        Assert.Equal((2, 1), TextPositionMath.OffsetToPosition(CrlfText, 5));
    }

    [Fact]
    public void PositionToOffset_LF_Dong2BatDauTai4()
    {
        Assert.Equal(4, TextPositionMath.PositionToOffset(LfText, 2, 1));
        Assert.Equal((2, 1), TextPositionMath.OffsetToPosition(LfText, 4));
    }

    [Fact]
    public void PositionToOffset_GiuaDong_DungCot()
    {
        // Dòng 2 "def": cột 2 (1-based) là 'e' — offset 6 trong file CRLF.
        Assert.Equal(6, TextPositionMath.PositionToOffset(CrlfText, 2, 2));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, -3)]
    [InlineData(99, 99)]
    public void PositionToOffset_ngoaiPhamVi_ClampAnToan(int line, int column)
    {
        int offset = TextPositionMath.PositionToOffset(CrlfText, line, column);

        Assert.InRange(offset, 0, CrlfText.Length);
        var (l, c) = TextPositionMath.OffsetToPosition(CrlfText, offset);
        Assert.True(l >= 1 && c >= 1);
    }

    [Fact]
    public void OffsetToPosition_offsetVuotdoDai_ClampCuoiFile()
    {
        var (line, column) = TextPositionMath.OffsetToPosition(CrlfText, 1000);
        Assert.Equal(3, line);          // dòng cuối
        Assert.Equal(4, column);        // "ghi" có 3 ký tự → cuối = cột 4
    }

    [Fact]
    public void RoundTrip_LF_va_CRLF_NguocNhuNhau()
    {
        foreach (string text in new[] { LfText, CrlfText })
        {
            for (int offset = 0; offset <= text.Length; offset++)
            {
                var (line, column) = TextPositionMath.OffsetToPosition(text, offset);
                Assert.Equal(offset, TextPositionMath.PositionToOffset(text, line, column));
            }
        }
    }

    [Fact]
    public void OffsetGiuaCRvaLF_KyTuRiengLe_VanTraVeDungDongHienTai()
    {
        // Offset 3 = 'c' (cuối dòng 1), offset 4 = '\r' vẫn thuộc dòng 1.
        Assert.Equal((1, 4), TextPositionMath.OffsetToPosition(CrlfText, 3));
        Assert.Equal((1, 5), TextPositionMath.OffsetToPosition(CrlfText, 4));
    }

    [Fact]
    public void VanBanTrong_KhongCrash_TraVe1_1hoac0()
    {
        Assert.Equal(0, TextPositionMath.PositionToOffset("", 1, 1));
        Assert.Equal((1, 1), TextPositionMath.OffsetToPosition("", 0));
        Assert.Equal((1, 1), TextPositionMath.OffsetToPosition("", 50));
    }
}
