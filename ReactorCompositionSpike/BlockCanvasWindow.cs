using System.Diagnostics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace ReactorCompositionSpike;

/// <summary>
/// "Self-draw blocks" demo — built in pure code (no XAML; the WinUI XAML compiler silently crashes on
/// this window's markup, and a canvas-centric window needs almost no markup anyway). A Win2D
/// <see cref="CanvasControl"/> IMMEDIATE-draws a big tree of rounded "block" tokens with text; only the
/// visible rows are drawn → flat FPS regardless of count. Interaction:
///   • DRAG self-drawn — pointer events + our hit-test + drawn ghost + drop-indicator (drag kind chip → reorder).
///   • TEXT INPUT via "real control on demand" — click a pill → ONE overlaid real TextBox (native editing + IME);
///     commit on Enter/blur → write back to model + redraw. At most one TextBox ever (no per-cell TextBox/TSF tax).
/// All native composition tree → overlaying real controls on the canvas has no airspace problem.
/// </summary>
public sealed class BlockCanvasWindow : Window
{
    private record struct Token(string Text, Color Fill, Color TextColor, bool Editable);
    private sealed record BlockRow(int Indent, Token[] Tokens);
    private readonly record struct Hit(int Row, int Tok, float X, float Y, float W, float H, bool Editable);

    private const double RowH = 30, PillH = 22, Pad = 7, Gap = 6, FontSize = 13;

    private readonly CanvasControl _cvs = new();
    private readonly TextBox _edit;
    private readonly NumberBox _countBox;
    private readonly ToggleButton _autoBtn;
    private readonly TextBlock _status;

    private BlockRow[] _rows = System.Array.Empty<BlockRow>();
    private double _scroll;
    private int _lastDrawnRows;
    private readonly List<Hit> _hits = new();

    private readonly CanvasTextFormat _fmt = new()
    {
        FontSize = (float)FontSize,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _frames;
    private double _fpsWinStart, _fps;
    private bool _auto;
    private double _dir = 1;

    private bool _pressed, _dragging;
    private double _pressX, _pressY;
    private Hit _pressHit;
    private double _dragY;
    private int _dropRow;

    private bool _editing;
    private int _editRow, _editTok;

    private static readonly Color[] KindColors =
    {
        Color.FromArgb(255, 0xC5, 0x86, 0xC0), Color.FromArgb(255, 0x56, 0x9C, 0xD6),
        Color.FromArgb(255, 0x4E, 0xC9, 0xB0), Color.FromArgb(255, 0xCE, 0x91, 0x78),
        Color.FromArgb(255, 0x6A, 0x99, 0x55),
    };
    private static readonly (Color Fill, Color Text)[] FragStyles =
    {
        (Color.FromArgb(255, 0x37, 0x41, 0x4F), Color.FromArgb(255, 0x9C, 0xDC, 0xFE)),
        (Color.FromArgb(255, 0x26, 0x4F, 0x78), Microsoft.UI.Colors.White),
        (Color.FromArgb(255, 0x4D, 0x46, 0x36), Color.FromArgb(255, 0xD7, 0xBA, 0x7D)),
        (Color.FromArgb(255, 0x2D, 0x2D, 0x30), Color.FromArgb(255, 0xCC, 0xCC, 0xCC)),
        (Color.FromArgb(255, 0x3A, 0x3A, 0x3A), Color.FromArgb(255, 0xAA, 0xAA, 0xAA)),
    };
    private static readonly string[] KindLabels = { "如果", "计数循环", "调用", "返回", "注释" };

    public BlockCanvasWindow()
    {
        Title = "方块自绘 (Win2D) - 原生即时绘制";
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1120, 820));

        _countBox = new NumberBox { Value = 5000, Minimum = 10, Maximum = 200000, Width = 130, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var genBtn = new Button { Content = "生成" };
        genBtn.Click += OnGenerate;
        _autoBtn = new ToggleButton { Content = "自动滚动" };
        _autoBtn.Click += OnToggleAuto;
        _status = new TextBlock { Foreground = Brush(0x4E, 0xC9, 0xB0), FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center, Text = "--" };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Padding = new Thickness(10), Background = Brush(0x25, 0x25, 0x26) };
        bar.Children.Add(Label("方块行数"));
        bar.Children.Add(_countBox);
        bar.Children.Add(genBtn);
        bar.Children.Add(_autoBtn);
        bar.Children.Add(_status);

        _cvs.IsTabStop = false; // don't let the canvas grab focus from the on-demand edit TextBox
        _cvs.Draw += OnDraw;
        _cvs.PointerWheelChanged += OnWheel;
        _cvs.PointerPressed += OnPressed;
        _cvs.PointerMoved += OnMoved;
        _cvs.PointerReleased += OnReleased;

        _edit = new TextBox
        {
            Visibility = Visibility.Collapsed,
            FontSize = 13,
            MinWidth = 60,
            Height = 26,
            Padding = new Thickness(4, 1, 4, 1),
            Background = Brush(0x2D, 0x2D, 0x30),
            Foreground = Brush(0xFF, 0xFF, 0xFF),
            BorderBrush = Brush(0x56, 0x9C, 0xD6),
            BorderThickness = new Thickness(1),
        };
        _edit.KeyDown += OnEditKey;
        _edit.LostFocus += OnEditLostFocus;
        var overlay = new Canvas();
        overlay.Children.Add(_edit);

        var canvasCell = new Grid();
        canvasCell.Children.Add(_cvs);
        canvasCell.Children.Add(overlay);

        var root = new Grid { Background = Brush(0x1E, 0x1E, 0x1E) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(bar, 0);
        Grid.SetRow(canvasCell, 1);
        root.Children.Add(bar);
        root.Children.Add(canvasCell);
        Content = root;

        Generate(5000);
        CompositionTarget.Rendering += OnFrame;
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnFrame;
            _fmt.Dispose();
            _cvs.RemoveFromVisualTree();
        };
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));
    private static TextBlock Label(string text) => new() { Text = text, Foreground = Brush(0xCC, 0xCC, 0xCC), VerticalAlignment = VerticalAlignment.Center };

    private void OnGenerate(object sender, RoutedEventArgs e) => Generate((int)_countBox.Value);

    private void Generate(int n)
    {
        CancelEdit();
        var rnd = new Random(12345);
        var rows = new BlockRow[n];
        for (int i = 0; i < n; i++)
        {
            int indent = i % 6;
            int count = 3 + rnd.Next(9);
            var tokens = new Token[count];
            tokens[0] = new Token(KindLabels[i % KindLabels.Length], KindColors[i % KindColors.Length], Microsoft.UI.Colors.White, false);
            for (int j = 1; j < count; j++)
            {
                var (fill, text) = FragStyles[rnd.Next(FragStyles.Length)];
                tokens[j] = new Token(FragText(rnd, j), fill, text, true);
            }
            rows[i] = new BlockRow(indent, tokens);
        }
        _rows = rows;
        _scroll = 0;
        _cvs.Invalidate();
    }

    private static string FragText(Random rnd, int j) => (j % 5) switch
    {
        0 => $"var{rnd.Next(200)}",
        1 => rnd.Next(4) switch { 0 => "Number", 1 => "String", 2 => "Unit", _ => "Bool" },
        2 => rnd.Next(4) switch { 0 => "+", 1 => "=", 2 => "≠", _ => ">" },
        3 => rnd.Next(1000).ToString(),
        _ => rnd.Next(2) == 0 ? "(" : ")",
    };

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        if (_editing) CommitEdit();
        int delta = e.GetCurrentPoint(_cvs).Properties.MouseWheelDelta;
        _scroll = Clamp(_scroll - delta);
        _cvs.Invalidate();
    }

    private void OnToggleAuto(object sender, RoutedEventArgs e) => _auto = _autoBtn.IsChecked == true;

    private double MaxScroll() => System.Math.Max(0, _rows.Length * RowH - _cvs.ActualHeight);
    private double Clamp(double v) => System.Math.Max(0, System.Math.Min(v, MaxScroll()));

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_editing) CommitEdit();
        var p = e.GetCurrentPoint(_cvs).Position;
        var hit = HitTest(p.X, p.Y);
        if (hit is null) return;
        _pressed = true;
        _dragging = false;
        _pressX = p.X;
        _pressY = p.Y;
        _pressHit = hit.Value;
        _cvs.CapturePointer(e.Pointer);
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed) return;
        var p = e.GetCurrentPoint(_cvs).Position;
        if (!_dragging && _pressHit.Tok == 0 &&
            (System.Math.Abs(p.X - _pressX) > 5 || System.Math.Abs(p.Y - _pressY) > 5))
        {
            _dragging = true;
        }
        if (_dragging)
        {
            _dragY = p.Y;
            _dropRow = System.Math.Clamp((int)System.Math.Round((p.Y + _scroll) / RowH), 0, _rows.Length);
            _cvs.Invalidate();
        }
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        _cvs.ReleasePointerCapture(e.Pointer);
        if (!_pressed) return;
        _pressed = false;
        if (_dragging)
        {
            _dragging = false;
            MoveRow(_pressHit.Row, _dropRow);
            _cvs.Invalidate();
            return;
        }
        // Defer to after the click fully resolves — focusing the TextBox synchronously inside the
        // pointer-released handler bounces (the CanvasControl grabs focus back → instant LostFocus → hide).
        if (_pressHit.Editable)
        {
            var h = _pressHit;
            _cvs.DispatcherQueue.TryEnqueue(() => BeginEdit(h));
        }
    }

    private void MoveRow(int from, int to)
    {
        if (from < 0 || from >= _rows.Length) return;
        var list = new List<BlockRow>(_rows);
        var item = list[from];
        list.RemoveAt(from);
        if (to > from) to--;
        to = System.Math.Clamp(to, 0, list.Count);
        list.Insert(to, item);
        _rows = list.ToArray();
    }

    private void BeginEdit(Hit hit)
    {
        _editing = true;
        _editRow = hit.Row;
        _editTok = hit.Tok;
        _edit.Text = _rows[hit.Row].Tokens[hit.Tok].Text;
        Canvas.SetLeft(_edit, hit.X);
        Canvas.SetTop(_edit, hit.Y - 2);
        _edit.Width = System.Math.Max(hit.W + 8, 60);
        _edit.Visibility = Visibility.Visible;
        _edit.Focus(FocusState.Programmatic);
        _edit.SelectAll();
    }

    private void OnEditKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) { e.Handled = true; CommitEdit(); }
        else if (e.Key == VirtualKey.Escape) { e.Handled = true; CancelEdit(); }
    }

    private void OnEditLostFocus(object sender, RoutedEventArgs e)
    {
        if (_editing) CommitEdit();
    }

    private void CommitEdit()
    {
        if (!_editing) return;
        var t = _rows[_editRow].Tokens[_editTok];
        _rows[_editRow].Tokens[_editTok] = t with { Text = _edit.Text };
        EndEdit();
        _cvs.Invalidate();
    }

    private void CancelEdit()
    {
        if (!_editing) return;
        EndEdit();
    }

    private void EndEdit()
    {
        _editing = false;
        _edit.Visibility = Visibility.Collapsed;
    }

    private Hit? HitTest(double px, double py)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
        {
            var h = _hits[i];
            if (px >= h.X && px <= h.X + h.W && py >= h.Y && py <= h.Y + h.H) return h;
        }
        return null;
    }

    private void OnFrame(object? sender, object e)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        _frames++;
        if (now - _fpsWinStart >= 500)
        {
            _fps = _frames * 1000.0 / (now - _fpsWinStart);
            _frames = 0;
            _fpsWinStart = now;
            UpdateStatus();
        }
        if (_auto)
        {
            double max = MaxScroll();
            _scroll += _dir * 40;
            if (_scroll >= max) { _scroll = max; _dir = -1; }
            else if (_scroll <= 0) { _scroll = 0; _dir = 1; }
            _cvs.Invalidate();
        }
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(Color.FromArgb(255, 0x1E, 0x1E, 0x1E));
        _hits.Clear();
        if (_rows.Length == 0) return;

        double vh = sender.ActualHeight;
        int first = System.Math.Max(0, (int)(_scroll / RowH));
        int last = System.Math.Min(_rows.Length - 1, (int)((_scroll + vh) / RowH) + 1);

        int drawn = 0;
        for (int r = first; r <= last; r++)
        {
            float y = (float)(r * RowH - _scroll);
            DrawRow(ds, _rows[r], r, y, 1f, recordHits: true);
            drawn++;
        }
        _lastDrawnRows = drawn;

        if (_dragging)
        {
            float dropY = (float)(_dropRow * RowH - _scroll);
            ds.FillRectangle(0, dropY - 1, (float)sender.ActualWidth, 2, Color.FromArgb(255, 0x56, 0x9C, 0xD6));
            DrawRow(ds, _rows[_pressHit.Row], _pressHit.Row, (float)(_dragY - RowH / 2), 0.7f, recordHits: false);
        }
    }

    private void DrawRow(CanvasDrawingSession ds, BlockRow row, int rowIndex, float y, float alpha, bool recordHits)
    {
        float pillY = y + (float)((RowH - PillH) / 2);
        float x = (float)(8 + row.Indent * 16);

        ds.DrawText("›", x, y + 4, Mul(Color.FromArgb(255, 0x80, 0x80, 0x80), alpha), _fmt);
        x += 14;

        for (int t = 0; t < row.Tokens.Length; t++)
        {
            var tok = row.Tokens[t];
            float w = EstWidth(tok.Text);
            ds.FillRoundedRectangle(x, pillY, w, (float)PillH, 4, 4, Mul(tok.Fill, alpha));
            ds.DrawText(tok.Text, x + (float)Pad, y + 5, Mul(tok.TextColor, alpha), _fmt);
            if (recordHits)
                _hits.Add(new Hit(rowIndex, t, x, pillY, w, (float)PillH, tok.Editable));
            x += w + (float)Gap;
        }
    }

    private static Color Mul(Color c, float a) =>
        a >= 1f ? c : Color.FromArgb((byte)(c.A * a), c.R, c.G, c.B);

    private static float EstWidth(string text) => (float)(text.Length * (FontSize * 0.62) + 2 * Pad);

    private void UpdateStatus() =>
        _status.Text = $"方块行: {_rows.Length:N0} | 本帧绘制: {_lastDrawnRows} | FPS: {_fps:F0}" +
                       "   (点片段=编辑/中文IME · 拖kind徽章=重排 · 滚轮/自动滚动)";
}
