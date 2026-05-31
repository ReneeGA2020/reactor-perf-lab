using System.ComponentModel;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;   // PropertyGrid attributes, TypeRegistry, TypeMetadata
using Microsoft.UI.Reactor.Data;        // FieldDescriptor
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
// System.ComponentModel.Component 会和 reactor 的 Component 撞名，显式取后者。
using Component = Microsoft.UI.Reactor.Core.Component;

// ─────────────────────────────────────────────────────────────────────────────
// PropertyGrid 探针 —— 这个 reactor 实验分支(perf-lab)里的 PropertyGrid 控件长啥样、怎么用。
//
// PropertyGrid 的公开 API 极简，就一个工厂方法：
//
//     PropertyGrid(object target, TypeRegistry registry, Action<object>? onRootChanged = null)
//
//   • target   —— 要编辑的对象。实现 INotifyPropertyChanged 时会被自动订阅，属性变化自动重渲染。
//   • registry —— 类型→编辑器的映射表。内置了常见类型的 fallback，自定义类型才需要显式 Register。
//   • onRootChanged —— 当 target 是不可变类型(record)、整体被替换时的回传回调。
//
// 它会按 [PropertyCategory] 分组、按 [PropertyOrder] 排序，根据每个属性的"运行时类型"
// 自动选编辑器。复合类型(嵌套 record/class)还能展开成子属性递归编辑。
// ─────────────────────────────────────────────────────────────────────────────
ReactorApp.Run<ProbeApp>("PropertyGrid Probe", width: 760, height: 820);


// ══════════════════════════ 演示用的数据模型 ══════════════════════════

enum CharacterClass { Warrior, Mage, Rogue, Cleric }

// 不可变嵌套 record —— PropertyGrid 会把它拆成 Width / Height 两个子属性，可展开编辑。
record SpriteBounds(double Width, double Height)
{
    public override string ToString() => $"{Width}×{Height}";
}

// 集合元素类型 —— 用来探针 List<T> 在 grid 里到底渲染成什么。
class SkillEntry
{
    public string Name { get; set; } = "";
    public int Rank { get; set; }
    public SkillEntry() { }
    public SkillEntry(string name, int rank) { Name = name; Rank = rank; }
    public override string ToString() => $"{Name} (R{Rank})";
}

// 模式一：纯反射模型。只要实现 INPC，PropertyGrid 不需要任何显式注册就能渲染 + 双向编辑。
// 编辑器全部来自内置 fallback：string→TextBox, bool→ToggleSwitch, int/double→NumberBox,
// enum→ComboBox, DateTimeOffset→日期选择器, Windows.UI.Color→ColorPicker, 嵌套 record→可展开。
class CharacterSettings : INotifyPropertyChanged
{
    // 分类标题按字母序排，所以用数字前缀来控制分组顺序，把占地大的 ColorPicker 排到最后。

    // ── 集合属性：探针 List<T> 的渲染。期望看到增删项/上下移工具条，实测见下。 ──
    [PropertyCategory("0 · Collections")]
    [PropertyDescription("string 列表")]
    [PropertyOrder(0)]
    public List<string> Tags { get; set; } = new() { "hero", "ranged" };

    [PropertyCategory("0 · Collections")]
    [PropertyDescription("复杂对象列表 (SkillEntry)")]
    [PropertyOrder(1)]
    public List<SkillEntry> Skills { get; set; } = new() { new("Fireball", 3), new("Heal", 1) };

    private string _name = "Aria";
    [PropertyCategory("1 · Identity")]
    [PropertyDescription("角色显示名")]
    [PropertyOrder(0)]
    public string Name { get => _name; set => Set(ref _name, value); }

    [PropertyCategory("1 · Identity")]
    [PropertyReadOnly]                       // 即便有 setter，也强制只读
    [PropertyDescription("自动生成的唯一 ID")]
    [PropertyOrder(1)]
    public string Id { get; } = Guid.NewGuid().ToString()[..8];

    private CharacterClass _class = CharacterClass.Mage;
    [PropertyCategory("2 · Stats")]
    [PropertyDisplayName("职业")]              // 覆盖显示名(默认用属性名)
    [PropertyOrder(0)]
    public CharacterClass Class { get => _class; set => Set(ref _class, value); }

    private int _level = 7;
    [PropertyCategory("2 · Stats")]
    [PropertyOrder(1)]
    public int Level { get => _level; set => Set(ref _level, value); }

    private double _health = 92.5;
    [PropertyCategory("2 · Stats")]
    [PropertyOrder(2)]
    public double Health { get => _health; set => Set(ref _health, value); }

    private SpriteBounds _bounds = new(64, 64);
    [PropertyCategory("2 · Stats")]
    [PropertyDescription("包围盒尺寸 —— 点右侧 ▶ 展开编辑 Width/Height")]
    [PropertyOrder(3)]
    public SpriteBounds Bounds { get => _bounds; set => Set(ref _bounds, value); }

    private DateTimeOffset _created = new(2026, 5, 31, 9, 0, 0, TimeSpan.Zero);
    [PropertyCategory("3 · Meta")]
    [PropertyDisplayName("创建时间")]
    public DateTimeOffset Created { get => _created; set => Set(ref _created, value); }

    private bool _visible = true;
    [PropertyCategory("4 · Appearance")]
    [PropertyDescription("是否在场景中可见")]
    public bool Visible { get => _visible; set => Set(ref _visible, value); }

    private Windows.UI.Color _tint = Windows.UI.Color.FromArgb(255, 80, 160, 240);
    [PropertyCategory("4 · Appearance")]
    [PropertyDisplayName("主色调")]
    public Windows.UI.Color Tint { get => _tint; set => Set(ref _tint, value); }

    [PropertyHidden]                         // 完全不在 grid 中显示
    public int InternalHandle { get; set; } = 0xBEEF;

    // ── INPC 样板 ──
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        if (!Equals(field, value)) { field = value; PropertyChanged?.Invoke(this, new(name)); }
    }
}

// 模式二/三用的不可变自定义类型 —— 没有内置支持，必须通过 TypeRegistry 显式注册元数据。
record RgbColor(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}


// ══════════════════════════ 应用本体 ══════════════════════════

class ProbeApp : Component
{
    public override Element Render()
    {
        var (mode, setMode) = UseState(0);

        // ── 自定义类型注册：给不可变的 RgbColor 提供 Editor + Decompose + Compose ──
        // Editor   : 怎么显示这个值(这里就显示十六进制文本)。
        // Decompose: 拆成可单独编辑的子字段(R/G/B 三个 byte，自动得到 NumberBox)。
        // Compose  : 子字段改了之后，怎么重新拼回一个新的不可变值。
        var registry = new TypeRegistry();
        registry.Register<RgbColor>(new TypeMetadata
        {
            DisplayName = "RGB Color",
            Editor = (val, _) => TextBlock(((RgbColor)val).ToString()).SemiBold(),
            Decompose = val =>
            {
                var c = (RgbColor)val;
                return new List<FieldDescriptor>
                {
                    new() { Name = "R", FieldType = typeof(byte), GetValue = _ => c.R, Order = 0 },
                    new() { Name = "G", FieldType = typeof(byte), GetValue = _ => c.G, Order = 1 },
                    new() { Name = "B", FieldType = typeof(byte), GetValue = _ => c.B, Order = 2 },
                };
            },
            Compose = (val, updates) =>
            {
                var c = (RgbColor)val;
                byte Pick(string k, byte cur) => updates.TryGetValue(k, out var v) ? (byte)v : cur;
                return new RgbColor(Pick("R", c.R), Pick("G", c.G), Pick("B", c.B));
            },
        });

        // INPC 目标：UseRef 给它稳定身份，UseObservable 让属性变更触发重渲染。
        var character = UseRef(new CharacterSettings());
        UseObservable(character.Current);

        // 不可变 record 目标：用 UseState 持有，整体替换时通过 onRootChanged 回写。
        var (rgb, setRgb) = UseState(new RgbColor(255, 128, 0));

        object target = mode switch
        {
            0 => character.Current,
            _ => rgb,
        };
        Action<object>? onRootChanged = mode == 1 ? o => setRgb((RgbColor)o) : null;

        return VStack(14,
            Heading("PropertyGrid Probe"),
            TextBlock("一个工厂方法 PropertyGrid(target, registry, onRootChanged) 就是全部入口。")
                .Foreground(SecondaryText),

            // 目标切换
            HStack(8,
                TextBlock("Target:").VAlign(Microsoft.UI.Xaml.VerticalAlignment.Center),
                ComboBox(new[] { "反射 INPC 模型 (CharacterSettings)", "自定义不可变类型 (RgbColor)" },
                         mode, setMode)
            ),

            mode switch
            {
                0 => TextBlock("反射模式：分类 / 排序 / 只读 / 隐藏 / 显示名 都靠属性上的特性。"
                             + "编辑器全是内置 fallback —— 改任意值看下方实时回读。")
                        .Foreground(SecondaryText),
                _ => TextBlock("自定义类型：RgbColor 是不可变 record，靠 registry 注册的 "
                             + "Editor+Decompose+Compose 渲染，整体替换经 onRootChanged 回写。")
                        .Foreground(SecondaryText),
            },

            // ★ 核心：PropertyGrid 控件本体 ★
            ScrollView(
                PropertyGrid(target, registry, onRootChanged)
            ).Height(480),

            // 实时回读，证明双向绑定确实生效
            Border(
                VStack(4,
                    TextBlock("Live readback").SemiBold(),
                    mode switch
                    {
                        0 => TextBlock($"Name={character.Current.Name}  Class={character.Current.Class}  "
                                     + $"Level={character.Current.Level}  Health={character.Current.Health:F1}  "
                                     + $"Visible={character.Current.Visible}  Bounds={character.Current.Bounds}  "
                                     + $"Tags=[{string.Join(",", character.Current.Tags)}]  Skills={character.Current.Skills.Count}  "
                                     + $"Tint=#{character.Current.Tint.R:X2}{character.Current.Tint.G:X2}{character.Current.Tint.B:X2}"),
                        _ => TextBlock($"RgbColor = {rgb}"),
                    }
                )
            ).Padding(12).Background(SubtleFill)
        ).Padding(20);
    }
}
