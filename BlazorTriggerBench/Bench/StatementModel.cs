namespace BlazorTriggerBench.Bench;

// Mirror of ReactorPerfLab's statement-row model so the Blazor bench renders the SAME workload
// (variable-length, kind-heterogeneous inline fragments incl. real buttons/checkboxes). Colours are
// CSS hex strings (no WinUI types). Keep the generator logic identical for a fair comparison.

public enum FragKind { Normal, Keyword, Editable, Operator, Paren, Type, Badge, Button, Checkbox }

public enum StatementKind
{
    If, While, Switch, SwitchCase, For, ForOf, ForOfKV,
    Call, Assignment, VariableDecl, Return, Try, Comment,
}

public sealed class Fragment
{
    public required string Text { get; init; }
    public required FragKind Kind { get; init; }

    /// <summary>Inline CSS for a text-style fragment (color, optional chip background/padding, weight).</summary>
    public string Style => Kind switch
    {
        FragKind.Keyword => "color:#C586C0;font-weight:600",
        FragKind.Editable => "color:#9CDCFE;background:#2D2D30;border-radius:3px;padding:1px 6px",
        FragKind.Operator => "color:#D7BA7D",
        FragKind.Paren => "color:#808080",
        FragKind.Type => "color:#FFFFFF;background:#264F78;border-radius:3px;padding:1px 6px",
        FragKind.Badge => "color:#1E1E1E;background:#4EC9B0;border-radius:3px;padding:1px 6px",
        _ => "color:#CCCCCC",
    };
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

    public string KindColor => Kind switch
    {
        StatementKind.If or StatementKind.While or StatementKind.Switch or StatementKind.SwitchCase => "#C586C0",
        StatementKind.For or StatementKind.ForOf or StatementKind.ForOfKV => "#569CD6",
        StatementKind.Call or StatementKind.Assignment or StatementKind.VariableDecl => "#4EC9B0",
        StatementKind.Return or StatementKind.Try => "#CE9178",
        _ => "#6A9955",
    };
}

public static class StatementData
{
    private static readonly StatementKind[] Kinds = Enum.GetValues<StatementKind>();

    public static (List<StatementRow> Rows, int FragmentTotal) Build(int rowCount, int fragmentsPerRow)
    {
        var rows = new List<StatementRow>(rowCount);
        int fragTotal = 0;
        for (int i = 0; i < rowCount; i++)
        {
            var kind = Kinds[i % Kinds.Length];
            int depth = i % 5;
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

        if (kind == StatementKind.Comment)
        {
            int n = Math.Max(2, target / 3);
            for (int i = 0; i < n; i++) f.Add(new Fragment { Text = i == 0 ? "说明" : "文字", Kind = FragKind.Normal });
            return f;
        }

        EmitTerm(f, rnd, depth: 0);
        while (f.Count < target)
        {
            f.Add(Op(rnd));
            EmitTerm(f, rnd, depth: 0, allowNested: rnd.Next(3) == 0);
        }

        if (kind is StatementKind.Return or StatementKind.VariableDecl && rnd.Next(2) == 0)
            f.Add(new Fragment { Text = "可空", Kind = FragKind.Checkbox });

        return f;
    }

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
            case 0:
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

            case 1:
                EmitLeaf(f, rnd);
                f.Add(Paren("["));
                EmitLeaf(f, rnd);
                f.Add(Paren("]"));
                break;

            case 2:
                EmitLeaf(f, rnd);
                f.Add(new Fragment { Text = "as", Kind = FragKind.Operator });
                f.Add(TypeChip(rnd));
                break;

            default:
                EmitLeaf(f, rnd);
                f.Add(Op(rnd));
                EmitLeaf(f, rnd);
                break;
        }
    }

    private static void EmitLeaf(List<Fragment> f, Random rnd) =>
        f.Add(new Fragment { Text = rnd.Next(3) == 0 ? rnd.Next(1000).ToString() : $"var{rnd.Next(200)}", Kind = FragKind.Editable });

    private static Fragment Op(Random rnd) => new()
    {
        Text = rnd.Next(6) switch { 0 => "+", 1 => "-", 2 => "*", 3 => "=", 4 => "≠", _ => ">" },
        Kind = FragKind.Operator,
    };

    private static Fragment Paren(string t) => new() { Text = t, Kind = FragKind.Paren };
    private static Fragment Normal(string t) => new() { Text = t, Kind = FragKind.Normal };
    private static Fragment TypeChip(Random rnd) => new()
    {
        Text = rnd.Next(4) switch { 0 => "Number", 1 => "String", 2 => "Unit", _ => "Bool" },
        Kind = FragKind.Type,
    };
}
