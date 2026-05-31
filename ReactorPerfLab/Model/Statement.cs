using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ReactorPerfLab.Model;

// ─────────────────────────────────────────────────────────────────────────────
// Faithful model of the Trigger Editor's "statement row": each row is a
// VARIABLE-LENGTH, kind-heterogeneous run of inline "fragment" elements produced by
// flattening an expression tree (mirrors NodeTextRenderer → List<TextFragment>).
// Heavy rows reach 30–60+ fragments incl. real interactive controls (buttons,
// checkboxes) — the real cost driver that pushed the XAML version off a cliff.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Styling/behaviour class of one inline fragment, mirroring TextFragment.CssClass.</summary>
public enum FragKind
{
    Normal,    // gray text / punctuation / separators
    Keyword,   // 关键词 (bold, purple)
    Editable,  // editable value (variable / literal) — subtle chip + light-blue text
    Operator,  // + - = ≠ etc. — gold
    Paren,     // ( ) [ ] nesting — dim
    Type,      // type label chip — dark-blue background
    Badge,     // info badge chip — teal background
    Button,    // [+] / [×] action — a real Button control
    Checkbox,  // nullable / toggle — a real CheckBox control
}

/// <summary>Statement kinds, mirroring the ~15 kinds the real editor renders differently.</summary>
public enum StatementKind
{
    If, While, Switch, SwitchCase, For, ForOf, ForOfKV,
    Call, Assignment, VariableDecl, Return, Try, Comment,
}

public sealed class Fragment
{
    public required string Text { get; init; }
    public required FragKind Kind { get; init; }

    /// <summary>Drives the fragment-level template selector (XAML) / element switch (Reactor).</summary>
    public string Category => Kind switch
    {
        FragKind.Button => "Button",
        FragKind.Checkbox => "Checkbox",
        _ => "Text",
    };

    // A "text chip" gets a background + padding (Type / Badge label chips, and editable values).
    public bool IsTextChip => Kind is FragKind.Type or FragKind.Badge or FragKind.Editable;

    // ── XAML x:Bind surface (Text-category template) ──
    public Brush Foreground => HexBrush.Get(Palette.FragFg(Kind));
    public Brush ChipBackground => Palette.FragChipBg(Kind) is { Length: > 0 } hex
        ? HexBrush.Get(hex)
        : HexBrush.Transparent;
    public Thickness ChipPadding => IsTextChip ? new Thickness(6, 1, 6, 1) : new Thickness(0);
    public Windows.UI.Text.FontWeight Weight => Kind == FragKind.Keyword ? FontWeights.SemiBold : FontWeights.Normal;

    // ── Reactor surface ──
    public string ForegroundHex => Palette.FragFg(Kind);
    public string ChipBackgroundHex => Palette.FragChipBg(Kind);
    public bool Bold => Kind == FragKind.Keyword;
}

public sealed class StatementRow
{
    public required string Id { get; init; }
    public required StatementKind Kind { get; init; }
    public required int Depth { get; init; }
    public required IReadOnlyList<Fragment> Fragments { get; init; }

    public string KindLabel => Kind switch
    {
        StatementKind.If => "如果",
        StatementKind.While => "当重复",
        StatementKind.Switch => "选择",
        StatementKind.SwitchCase => "当值为",
        StatementKind.For => "计数循环",
        StatementKind.ForOf => "遍历",
        StatementKind.ForOfKV => "遍历键值",
        StatementKind.Call => "调用",
        StatementKind.Assignment => "赋值",
        StatementKind.VariableDecl => "声明",
        StatementKind.Return => "返回",
        StatementKind.Try => "尝试",
        _ => "注释",
    };

    public string KindHex => Palette.KindBg(Kind);
    public Brush KindBrush => HexBrush.Get(KindHex);
    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    public string Category => Kind switch
    {
        StatementKind.Comment => "Comment",
        StatementKind.Call or StatementKind.Assignment or StatementKind.VariableDecl => "Call",
        _ => "Control",
    };
}

/// <summary>
/// Builds a flat list of statement rows whose fragments come from a flattened expression tree,
/// so fragment counts are organic and heavy (nested parens, calls + arg lists, index, casts).
/// </summary>
public static class StatementData
{
    private static readonly StatementKind[] Kinds = Enum.GetValues<StatementKind>();

    /// <param name="fragmentsPerRow">Target inline fragments per row (the "复杂度" dial).</param>
    public static (IReadOnlyList<StatementRow> Rows, int FragmentTotal) Build(int rowCount, int fragmentsPerRow, int maxDepth)
    {
        var rows = new List<StatementRow>(rowCount);
        int fragTotal = 0;
        for (int i = 0; i < rowCount; i++)
        {
            var kind = Kinds[i % Kinds.Length];
            int depth = maxDepth <= 0 ? 0 : i % (maxDepth + 1);

            // Every 7th row is a "heavy nested expression" row (~2.5x fragments).
            int target = Math.Max(3, i % 7 == 0 ? fragmentsPerRow * 5 / 2 : fragmentsPerRow);
            var frags = BuildRowFragments(kind, target, new Random(i));
            fragTotal += frags.Count;

            rows.Add(new StatementRow { Id = $"s{i}", Kind = kind, Depth = depth, Fragments = frags });
        }
        return (rows, fragTotal);
    }

    private static IReadOnlyList<Fragment> BuildRowFragments(StatementKind kind, int target, Random rnd)
    {
        var f = new List<Fragment>(target + 4);

        // Comment rows are just dim prose — a few normal fragments.
        if (kind == StatementKind.Comment)
        {
            int n = Math.Max(2, target / 3);
            for (int i = 0; i < n; i++) f.Add(new Fragment { Text = i == 0 ? "说明" : "文字", Kind = FragKind.Normal });
            return f;
        }

        // Build the row's expression(s) until we hit the fragment target.
        EmitTerm(f, rnd, depth: 0);
        while (f.Count < target)
        {
            f.Add(Op(rnd));
            EmitTerm(f, rnd, depth: 0, allowNested: rnd.Next(3) == 0);
        }

        // Return statements often carry a "no value" checkbox; assignments a trailing toggle.
        if (kind is StatementKind.Return or StatementKind.VariableDecl && rnd.Next(2) == 0)
            f.Add(new Fragment { Text = "可空", Kind = FragKind.Checkbox });

        return f;
    }

    // Emits one "term": a leaf, a call (recv.method(args)), or an index — optionally a parenthesised binop.
    private static void EmitTerm(List<Fragment> f, Random rnd, int depth, bool allowNested = false)
    {
        if (allowNested && depth < 3)
        {
            f.Add(Paren("("));
            EmitTerm(f, rnd, depth + 1);
            f.Add(Op(rnd));
            EmitTerm(f, rnd, depth + 1, allowNested: rnd.Next(3) == 0);
            f.Add(Paren(")"));
            return;
        }

        switch (rnd.Next(4))
        {
            case 0: // function call: recv . method (args) [+]
                EmitLeaf(f, rnd);
                f.Add(Normal("."));
                f.Add(new Fragment { Text = $"f{rnd.Next(99)}", Kind = FragKind.Editable });
                f.Add(TypeChip(rnd));
                f.Add(Paren("("));
                int args = 1 + rnd.Next(3);
                for (int a = 0; a < args; a++)
                {
                    if (a > 0) f.Add(Normal(","));
                    EmitLeaf(f, rnd);
                }
                f.Add(new Fragment { Text = "+", Kind = FragKind.Button });
                f.Add(Paren(")"));
                break;

            case 1: // index access: a [ b ]
                EmitLeaf(f, rnd);
                f.Add(Paren("["));
                EmitLeaf(f, rnd);
                f.Add(Paren("]"));
                break;

            case 2: // cast: x as Type
                EmitLeaf(f, rnd);
                f.Add(new Fragment { Text = "as", Kind = FragKind.Operator });
                f.Add(TypeChip(rnd));
                break;

            default: // simple binary: a op b
                EmitLeaf(f, rnd);
                f.Add(Op(rnd));
                EmitLeaf(f, rnd);
                break;
        }
    }

    private static void EmitLeaf(List<Fragment> f, Random rnd)
    {
        f.Add(rnd.Next(3) == 0
            ? new Fragment { Text = rnd.Next(1000).ToString(), Kind = FragKind.Editable } // numeric literal
            : new Fragment { Text = $"var{rnd.Next(200)}", Kind = FragKind.Editable });     // variable
    }

    private static Fragment Op(Random rnd) => new()
    {
        Text = (rnd.Next(6)) switch { 0 => "+", 1 => "-", 2 => "*", 3 => "=", 4 => "≠", _ => ">" },
        Kind = FragKind.Operator,
    };

    private static Fragment Paren(string text) => new() { Text = text, Kind = FragKind.Paren };
    private static Fragment Normal(string text) => new() { Text = text, Kind = FragKind.Normal };
    private static Fragment TypeChip(Random rnd) => new()
    {
        Text = (rnd.Next(4)) switch { 0 => "Number", 1 => "String", 2 => "Unit", _ => "Bool" },
        Kind = FragKind.Type,
    };
}

// ── Color palette + brush cache (shared by Node/Statement; UI-thread access) ───
internal static class Palette
{
    public static string FragFg(FragKind k) => k switch
    {
        FragKind.Keyword => "#C586C0",
        FragKind.Editable => "#9CDCFE",
        FragKind.Operator => "#D7BA7D",
        FragKind.Paren => "#808080",
        FragKind.Type => "#FFFFFF",
        FragKind.Badge => "#1E1E1E",
        _ => "#CCCCCC",
    };

    public static string FragChipBg(FragKind k) => k switch
    {
        FragKind.Type => "#264F78",
        FragKind.Badge => "#4EC9B0",
        FragKind.Editable => "#2D2D30",
        _ => "",
    };

    public static string KindBg(StatementKind k) => k switch
    {
        StatementKind.If or StatementKind.While or StatementKind.Switch or StatementKind.SwitchCase => "#C586C0",
        StatementKind.For or StatementKind.ForOf or StatementKind.ForOfKV => "#569CD6",
        StatementKind.Call or StatementKind.Assignment or StatementKind.VariableDecl => "#4EC9B0",
        StatementKind.Return or StatementKind.Try => "#CE9178",
        _ => "#6A9955",
    };
}

internal static class HexBrush
{
    public static readonly Brush Transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    private static readonly Dictionary<string, Brush> Cache = new();

    public static Brush Get(string hex)
    {
        if (Cache.TryGetValue(hex, out var b)) return b;
        b = new SolidColorBrush(FromHex(hex));
        Cache[hex] = b;
        return b;
    }

    private static Color FromHex(string hex)
    {
        var s = hex.TrimStart('#');
        byte r = Convert.ToByte(s.Substring(0, 2), 16);
        byte g = Convert.ToByte(s.Substring(2, 2), 16);
        byte b = Convert.ToByte(s.Substring(4, 2), 16);
        return Color.FromArgb(0xFF, r, g, b);
    }
}
