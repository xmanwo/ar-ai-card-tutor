using System.Collections.Generic;

/// <summary>
/// Chinese text pack. Optional and deliberately partial for P0: it exists to prove that language
/// switching and the English fallback work. Any key missing here falls back to English, which is
/// the intended behaviour, not an error.
///
/// Fill the rest only when the UI is being translated; until then a half-filled pack would just
/// mix two languages on screen.
/// </summary>
public static class ZhText
{
    public static readonly Dictionary<string, string> Pack = new Dictionary<string, string>
    {
        { "why.sameColor",
          "{card} 能出：和桌顶的 {top} 同色（{colour}）" },
        { "why.sameValue",
          "{card} 能出：和桌顶的 {top} 同为 {value}" },
        { "why.noMatch",
          "{card} 不能出：桌顶是 {top}，颜色和数字都不匹配" }
    };
}
