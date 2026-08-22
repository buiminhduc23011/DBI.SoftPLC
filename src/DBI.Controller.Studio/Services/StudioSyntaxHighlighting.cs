using System.IO;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace DBI.Controller.Studio.Services;

/// <summary>
/// Cung cấp bảng màu cú pháp C# chuẩn Visual Studio 2022 (Dark Theme) cho AvalonEdit.
/// </summary>
public static class StudioSyntaxHighlighting
{
    private static IHighlightingDefinition? _csharpDark;

    public static IHighlightingDefinition CSharpDark
    {
        get
        {
            if (_csharpDark is not null) return _csharpDark;
            _csharpDark = CreateDarkDefinition();
            return _csharpDark;
        }
    }

    private static IHighlightingDefinition CreateDarkDefinition()
    {
        // Sử dụng XSHD chuẩn của AvalonEdit nhưng áp bảng màu Visual Studio 2022 Dark
        using var reader = new StringReader(DarkThemeXshd);
        using var xmlReader = XmlReader.Create(reader);
        return HighlightingLoader.Load(xmlReader, HighlightingManager.Instance);
    }

    private const string DarkThemeXshd = """
        <?xml version="1.0"?>
        <SyntaxDefinition name="C#-Dark" extensions=".cs" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
            <Color name="Comment" foreground="#57A64A" />
            <Color name="DocComment" foreground="#608B4E" />
            <Color name="String" foreground="#D69D85" />
            <Color name="Char" foreground="#D69D85" />
            <Color name="Preprocessor" foreground="#9B9B9B" />
            <Color name="Punctuation" foreground="#D4D4D4" />
            <Color name="ValueTypeKeywords" foreground="#569CD6" />
            <Color name="ReferenceTypeKeywords" foreground="#4EC9B0" />
            <Color name="MethodCall" foreground="#DCDCAA" />
            <Color name="NumberLiteral" foreground="#B5CEA8" />
            <Color name="Keywords" foreground="#569CD6" />
            <Color name="ControlKeywords" foreground="#C586C0" />
            <Color name="TrueFalse" foreground="#569CD6" />
            <Color name="Null" foreground="#569CD6" />

            <RuleSet name="CommentMarkerSet">
                <Keywords fontWeight="bold" foreground="#E5C07B">
                    <Word>TODO</Word>
                    <Word>FIXME</Word>
                    <Word>NOTE</Word>
                </Keywords>
            </RuleSet>

            <RuleSet>
                <!-- Doc Comments /// -->
                <Span color="DocComment">
                    <Begin>///</Begin>
                    <RuleSet>
                        <Import ruleSet="CommentMarkerSet" />
                    </RuleSet>
                </Span>

                <!-- Single-line comments // -->
                <Span color="Comment">
                    <Begin>//</Begin>
                    <RuleSet>
                        <Import ruleSet="CommentMarkerSet" />
                    </RuleSet>
                </Span>

                <!-- Multi-line comments /* */ -->
                <Span color="Comment" multiline="true">
                    <Begin>/\*</Begin>
                    <End>\*/</End>
                    <RuleSet>
                        <Import ruleSet="CommentMarkerSet" />
                    </RuleSet>
                </Span>

                <!-- Regular Strings " " -->
                <Span color="String">
                    <Begin>"</Begin>
                    <End>"</End>
                    <RuleSet>
                        <Span begin="\\" end="." />
                    </RuleSet>
                </Span>

                <!-- Verbatim Strings @" " -->
                <Span color="String" multiline="true">
                    <Begin>@"</Begin>
                    <End>"</End>
                    <RuleSet>
                        <Span begin="&quot;&quot;" />
                    </RuleSet>
                </Span>

                <!-- Interpolated Strings $" " -->
                <Span color="String">
                    <Begin>\$"</Begin>
                    <End>"</End>
                    <RuleSet>
                        <Span begin="\\" end="." />
                    </RuleSet>
                </Span>

                <!-- Characters ' ' -->
                <Span color="Char">
                    <Begin>'</Begin>
                    <End>'</End>
                    <RuleSet>
                        <Span begin="\\" end="." />
                    </RuleSet>
                </Span>

                <!-- Preprocessor directives # -->
                <Span color="Preprocessor">
                    <Begin>\#</Begin>
                </Span>

                <Keywords color="TrueFalse">
                    <Word>true</Word>
                    <Word>false</Word>
                </Keywords>

                <Keywords color="Null">
                    <Word>null</Word>
                </Keywords>

                <Keywords color="Keywords">
                    <Word>abstract</Word>
                    <Word>as</Word>
                    <Word>base</Word>
                    <Word>byte</Word>
                    <Word>class</Word>
                    <Word>const</Word>
                    <Word>delegate</Word>
                    <Word>double</Word>
                    <Word>enum</Word>
                    <Word>event</Word>
                    <Word>explicit</Word>
                    <Word>extern</Word>
                    <Word>float</Word>
                    <Word>implicit</Word>
                    <Word>in</Word>
                    <Word>int</Word>
                    <Word>interface</Word>
                    <Word>internal</Word>
                    <Word>is</Word>
                    <Word>lock</Word>
                    <Word>long</Word>
                    <Word>namespace</Word>
                    <Word>new</Word>
                    <Word>object</Word>
                    <Word>operator</Word>
                    <Word>out</Word>
                    <Word>override</Word>
                    <Word>params</Word>
                    <Word>partial</Word>
                    <Word>private</Word>
                    <Word>protected</Word>
                    <Word>public</Word>
                    <Word>readonly</Word>
                    <Word>ref</Word>
                    <Word>sbyte</Word>
                    <Word>sealed</Word>
                    <Word>short</Word>
                    <Word>sizeof</Word>
                    <Word>stackalloc</Word>
                    <Word>static</Word>
                    <Word>string</Word>
                    <Word>struct</Word>
                    <Word>this</Word>
                    <Word>typeof</Word>
                    <Word>uint</Word>
                    <Word>ulong</Word>
                    <Word>unchecked</Word>
                    <Word>unsafe</Word>
                    <Word>ushort</Word>
                    <Word>using</Word>
                    <Word>virtual</Word>
                    <Word>void</Word>
                    <Word>volatile</Word>
                    <Word>var</Word>
                    <Word>record</Word>
                    <Word>init</Word>
                    <Word>required</Word>
                </Keywords>

                <Keywords color="ControlKeywords">
                    <Word>break</Word>
                    <Word>case</Word>
                    <Word>catch</Word>
                    <Word>continue</Word>
                    <Word>default</Word>
                    <Word>do</Word>
                    <Word>else</Word>
                    <Word>finally</Word>
                    <Word>for</Word>
                    <Word>foreach</Word>
                    <Word>goto</Word>
                    <Word>if</Word>
                    <Word>return</Word>
                    <Word>switch</Word>
                    <Word>throw</Word>
                    <Word>try</Word>
                    <Word>while</Word>
                    <Word>yield</Word>
                    <Word>await</Word>
                    <Word>async</Word>
                </Keywords>

                <Keywords color="ReferenceTypeKeywords">
                    <Word>IOContainer</Word>
                    <Word>ControllerProgram</Word>
                    <Word>Ton</Word>
                    <Word>Tof</Word>
                    <Word>Tp</Word>
                    <Word>RisingEdge</Word>
                    <Word>FallingEdge</Word>
                    <Word>CounterUp</Word>
                    <Word>CounterDown</Word>
                    <Word>TimeSpan</Word>
                    <Word>DateTime</Word>
                    <Word>Task</Word>
                    <Word>Action</Word>
                    <Word>Func</Word>
                </Keywords>

                <Keywords color="ValueTypeKeywords">
                    <Word>bool</Word>
                </Keywords>

                <Rule color="MethodCall">
                    \b[\d\w_]+(?=\s*\()
                </Rule>

                <Rule color="NumberLiteral">
                    \b0[xX][0-9a-fA-F]+|(\b\d+(\.[0-9]+)?|\.[0-9]+)([eE][+-]?[0-9]+)?
                </Rule>

                <Rule color="Punctuation">
                    [?,.;()\[\]{}+\-/%*&lt;&gt;^+~!|&amp;]+
                </Rule>
            </RuleSet>
        </SyntaxDefinition>
        """;
}
