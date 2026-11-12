using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Bolok;

// 설정 창 부품. patio 설정 창(macOS 시스템 설정 꼴)을 따른다:
// 어두운 바탕, 왼쪽 색 아이콘 목록, 오른쪽은 「묶음 제목 → 둥근 카드 안의 줄들 → 아래 설명」, 초록 스위치, 둥근 버튼
static class Ui
{
    public static readonly Color Bg = Hex(0x1c1d20), Side = Hex(0x18191b), Line = Hex(0x2a2c30), Card = Hex(0x242529), CardLine = Hex(0x2f3136),
        Fg = Hex(0xd4d4d4), Dim = Hex(0x8a8f98), Accent = Hex(0x0a84ff), NavSel = Hex(0x2f5ea8), Hover = Hex(0x26282c), Ok = Hex(0x30d158),
        Danger = Hex(0xff6961), DangerFill = Hex(0xff453a), Field = Hex(0x1a1b1e), FieldLine = Hex(0x34363b), Btn = Hex(0x2c2e33), BtnHover = Hex(0x34373c),
        BtnLine = Hex(0x3a3c42), Track = Hex(0x3a3c42), Pink = Hex(0xff375f), Orange = Hex(0xff9f0a), Indigo = Hex(0x5e5ce6), Gray = Hex(0x8e8e93), Blue = Hex(0x0a84ff);

    public static Color Hex(int v) => Color.FromArgb(v >> 16 & 255, v >> 8 & 255, v & 255);
    public static Color Mix(Color a, Color b, double t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    // 배율: 설정 창이 DPI 에 맞춰 정한다 (창이 하나뿐이라 공용으로 둔다)
    public static float K = 1;
    public static int D(float v) => (int)Math.Round(v * K);

    static readonly Dictionary<(float, bool, float, string), Font> fonts = new();
    public static Font F(float px, bool bold = false) // 글꼴은 언어에 따라 (한글 맑은 고딕, 일본어 Yu Gothic UI, 중국어 YaHei·JhengHei, 그 밖 Segoe UI)
    {
        var key = (px, bold, K, L.Font);
        if (!fonts.TryGetValue(key, out var f))
            fonts[key] = f = new Font(L.Font, px * K, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        return f;
    }

    public const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding; // TextBoxControl 을 넣으면 러시아어가 단어 중간에서 끊긴다
    public const TextFormatFlags OneLine = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;

    public static Size Measure(string text, Font font, int width = int.MaxValue)
    {
        if (string.IsNullOrEmpty(text)) return Size.Empty;
        if (width == int.MaxValue) return TextRenderer.MeasureText(text, font, Big, Plain);
        var lines = Lines(text, font, width);
        return new Size(lines.Max(l => One(l, font)), lines.Count * LineH(font));
    }

    public static void Text(Graphics g, string text, Font font, Rectangle r, Color c, TextFormatFlags flags = Wrap)
    {
        if ((flags & TextFormatFlags.WordBreak) == 0) { TextRenderer.DrawText(g, text, font, r, c, flags); return; }
        var one = (flags & ~(TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter | TextFormatFlags.Bottom)) | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        int h = LineH(font), y = r.Y;
        foreach (var line in Lines(text, font, r.Width))
        {
            if (y >= r.Bottom) break;
            TextRenderer.DrawText(g, line, font, new Rectangle(r.X, y, r.Width, h), c, one);
            y += h;
        }
    }

    // 줄바꿈은 직접 한다: 윈도우 GDI 는 한국어 시스템에서 러시아어 같은 글을 단어 중간에서 끊는다.
    // 띄어쓰기에서 끊고, 중국어·일본어처럼 띄어쓰기가 없는 글은 글자 단위로. 같은 글·너비는 다시 재지 않는다
    static readonly Dictionary<(string, Font, int), List<string>> wrapped = new();
    static readonly Dictionary<(string, Font), int> widths = new();

    static int One(string s, Font font)
    {
        if (widths.Count > 4000) widths.Clear();
        if (!widths.TryGetValue((s, font), out int w)) widths[(s, font)] = w = TextRenderer.MeasureText(s, font, Big, Plain).Width;
        return w;
    }

    static int LineH(Font font) => TextRenderer.MeasureText("Ag가", font, Big, Plain).Height;

    // 잴 때는 말줄임(…) 없이, 끝없이 넓은 칸에서 (말줄임을 넣으면 너비가 줄어 나온다)
    static readonly Size Big = new(int.MaxValue, int.MaxValue);
    const TextFormatFlags Plain = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

    static bool Cjk(char ch) => ch is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff' or >= '\uff00' and <= '\uffef' or >= '\u3000' and <= '\u303f';

    public static List<string> Lines(string text, Font font, int width)
    {
        if (wrapped.Count > 1000) wrapped.Clear();
        if (wrapped.TryGetValue((text, font, width), out var hit)) return hit;
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var tokens = new List<string>();
            var word = new System.Text.StringBuilder();
            foreach (char ch in para)
            {
                if (Cjk(ch)) { if (word.Length > 0) { tokens.Add(word.ToString()); word.Clear(); } tokens.Add(ch.ToString()); continue; }
                word.Append(ch);
                if (ch == ' ') { tokens.Add(word.ToString()); word.Clear(); }
            }
            if (word.Length > 0) tokens.Add(word.ToString());
            // 「、」「。」 같은 문장 부호는 앞 글자에 붙인다 (줄 첫머리에 오지 않게)
            for (int i = tokens.Count - 1; i > 0; i--)
                if (tokens[i].Length == 1 && "、。，．！？）」』,.!?)".Contains(tokens[i][0])) { tokens[i - 1] += tokens[i]; tokens.RemoveAt(i); }
            string line = "";
            foreach (var t in tokens)
            {
                string next = line + t;
                if (line.Length > 0 && One(next.TrimEnd(), font) > width) { lines.Add(line.TrimEnd()); line = t.TrimStart(); }
                else line = next;
            }
            lines.Add(line.TrimEnd());
        }
        return wrapped[(text, font, width)] = lines;
    }

    public static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Fill(Graphics g, RectangleF r, float rad, Color c)
    {
        using var b = new SolidBrush(c);
        using var p = Round(r, rad);
        g.FillPath(b, p);
    }

    public static void Stroke(Graphics g, RectangleF r, float rad, Color c, float w = 1)
    {
        using var pen = new Pen(c, w);
        using var p = Round(new RectangleF(r.X + w / 2, r.Y + w / 2, r.Width - w, r.Height - w), rad);
        g.DrawPath(pen, p);
    }

    // 픽셀 그림은 뭉개지지 않게 그대로 키운다
    public static void Pixel(Graphics g, Image img, Rectangle r, float alpha = 1)
    {
        var (mode, offset) = (g.InterpolationMode, g.PixelOffsetMode);
        (g.InterpolationMode, g.PixelOffsetMode) = (InterpolationMode.NearestNeighbor, PixelOffsetMode.Half);
        if (alpha >= 1) g.DrawImage(img, r);
        else
        {
            using var a = new ImageAttributes();
            a.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
            g.DrawImage(img, r, 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, a);
        }
        (g.InterpolationMode, g.PixelOffsetMode) = (mode, offset);
    }

    // 그림의 일부(src)를 dest 에 그대로 키워 그린다
    public static void Pixel(Graphics g, Image img, Rectangle dest, Rectangle src)
    {
        var (mode, offset) = (g.InterpolationMode, g.PixelOffsetMode);
        (g.InterpolationMode, g.PixelOffsetMode) = (InterpolationMode.NearestNeighbor, PixelOffsetMode.Half);
        g.DrawImage(img, dest, src, GraphicsUnit.Pixel);
        (g.InterpolationMode, g.PixelOffsetMode) = (mode, offset);
    }

    // 투명하지 않은 픽셀을 감싸는 상자 (+ 여유). 아기·알처럼 칸 안에서 작은 그림을 크게 보여 줄 때
    public static Rectangle Opaque(Bitmap b, int margin)
    {
        int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).A > 0) (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        if (x1 < 0) return new Rectangle(0, 0, b.Width, b.Height);
        return Rectangle.FromLTRB(Math.Max(0, x0 - margin), Math.Max(0, y0 - margin), Math.Min(b.Width, x1 + 1 + margin), Math.Min(b.Height, y1 + 1 + margin));
    }

    // 아직 못 만난 친구: 색을 빼고 흐리게
    public static Bitmap Faded(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height);
        using var g = Graphics.FromImage(dst);
        using var a = new ImageAttributes();
        a.SetColorMatrix(new ColorMatrix(new[]
        {
            new[] { .3f, .3f, .3f, 0, 0 }, new[] { .45f, .45f, .45f, 0, 0 }, new[] { .1f, .1f, .1f, 0, 0 },
            new[] { 0, 0, 0, .3f, 0 }, new[] { .05f, .05f, .06f, 0, 1 },
        }));
        g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, a);
        return dst;
    }

    // ── 창 꾸미기: 어두운 제목줄 (안 되는 윈도우면 그냥 넘어간다) ──
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

    public static void DarkTitle(Form f, Color caption)
    {
        try
        {
            int on = 1, color = caption.R | caption.G << 8 | caption.B << 16;
            if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4); // 어두운 제목줄 (옛 윈도우 10 은 19)
            DwmSetWindowAttribute(f.Handle, 35, ref color, 4); // 제목줄 색 (윈도우 11)
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }
}

// 높이를 너비에 맞춰 정하는 것 (세로로 쌓을 때 쓴다)
interface IFlow { int Measure(int width); }

// 직접 그리는 부품의 바탕
abstract class View : Control
{
    protected View()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.StandardDoubleClick, false); // 빠르게 두 번 눌러도 두 번 눌린다
    }

    protected static int D(float v) => Ui.D(v);
}

// 스위치 (켜면 초록)
sealed class Switch : View
{
    bool on;
    public event Action? Changed;

    public Switch(bool on)
    {
        this.on = on;
        Size = new Size(D(38), D(22));
        Cursor = Cursors.Hand;
        BackColor = Ui.Card;
        TabStop = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool On
    {
        get => on;
        set { if (on == value) return; on = value; Invalidate(); Changed?.Invoke(); }
    }

    protected override void OnClick(EventArgs e) { On = !On; base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) On = !On; base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float h = Height - 1, w = Width - 1;
        Ui.Fill(g, new RectangleF(0, 0, w, h), h / 2, on ? Ui.Ok : Ui.Track);
        if (Focused && ShowFocusCues) Ui.Stroke(g, new RectangleF(0, 0, Width, Height), h / 2, Ui.Accent, 1.5f);
        float k = h - D(4), x = on ? w - k - D(2) : D(2);
        using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillEllipse(shadow, x, D(2) + 1, k, k);
        g.FillEllipse(Brushes.White, x, D(2), k, k);
    }
}

enum BtnKind { Normal, Primary, Danger, DangerFill, Ghost }

// 둥근 버튼
sealed class Btn : View
{
    public BtnKind Kind;
    bool hover, down;

    public Btn(string text, BtnKind kind = BtnKind.Normal)
    {
        Kind = kind;
        Text = text;
        Cursor = Cursors.Hand;
        BackColor = Ui.Card;
        TabStop = true;
        Fit();
    }

    void Fit()
    {
        var s = Ui.Measure(Text, Ui.F(13));
        Size = new Size(s.Width + D(28), D(30));
    }

    protected override void OnTextChanged(EventArgs e) { Fit(); Invalidate(); base.OnTextChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var (fill, line, ink) = Kind switch
        {
            BtnKind.Primary => (hover ? Ui.Hex(0x2b95ff) : Ui.Accent, hover ? Ui.Hex(0x2b95ff) : Ui.Accent, Color.White),
            BtnKind.DangerFill => (hover ? Ui.Hex(0xff6159) : Ui.DangerFill, hover ? Ui.Hex(0xff6159) : Ui.DangerFill, Color.White),
            BtnKind.Ghost => (hover ? Ui.Hover : BackColor, hover ? Ui.Hover : BackColor, hover ? Ui.Fg : Ui.Dim),
            _ => (hover ? Ui.BtnHover : Ui.Btn, Ui.BtnLine, Kind == BtnKind.Danger ? Ui.Hex(0xff6961) : Ui.Fg),
        };
        if (down) fill = Ui.Mix(fill, Color.Black, .12);
        if (!Enabled) (fill, line, ink) = (Ui.Mix(fill, BackColor, .55), Ui.Mix(line, BackColor, .55), Ui.Mix(ink, BackColor, .55));
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        Ui.Fill(g, r, D(7), fill);
        Ui.Stroke(g, new RectangleF(0, 0, Width, Height), D(7), Focused && ShowFocusCues ? Ui.Accent : line);
        Ui.Text(g, Text, Ui.F(13), ClientRectangle, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | Ui.OneLine);
    }
}

// 막대 (포만·기분·기운): 막대와 오른쪽 숫자
sealed class Meter : View
{
    double value;
    readonly Color color;

    public Meter(Color color)
    {
        this.color = color;
        Size = new Size(D(200), D(22));
        BackColor = Ui.Card;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Value
    {
        get => value;
        set { value = Math.Clamp(value, 0, 100); if ((int)value == (int)this.value) { this.value = value; return; } this.value = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int num = D(34), h = D(8);
        var bar = new RectangleF(0, (Height - h) / 2f, Width - num - D(10), h);
        Ui.Fill(g, bar, h / 2f, Ui.Track);
        if (value > 0) Ui.Fill(g, new RectangleF(bar.X, bar.Y, Math.Max(h, bar.Width * (float)(value / 100)), h), h / 2f, value < 25 ? Ui.Danger : color);
        Ui.Text(g, ((int)Math.Round(value)).ToString(), Ui.F(13, true), new Rectangle(Width - num, 0, num, Height), value < 25 ? Ui.Danger : Ui.Fg,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | Ui.OneLine);
    }
}

// 묶음 카드: 둥근 상자 안에 줄을 쌓는다. 줄은 「이름·설명 + 오른쪽 값/부품」 또는 통째로 다른 부품
sealed class Card : View, IFlow
{
    public sealed class Item
    {
        public string Label = "";
        public string? Desc, Value;
        public Color? LabelColor, ValueColor;
        public Control? Right, Body;
        public Image? Icon;
        public float IconAlpha = 1;
        public bool Hidden;
        public int BodyPad = 12;
        public Action? Click; // 줄 전체를 누를 수 있다 (언어 고르기)
        internal Rectangle Box;
    }

    Item? hover;
    Item? At(Point p) => items.FirstOrDefault(i => !i.Hidden && i.Click != null && i.Box.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var it = At(e.Location);
        Cursor = it != null ? Cursors.Hand : Cursors.Default;
        if (it != hover) { hover = it; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { if (hover != null) { hover = null; Invalidate(); } base.OnMouseLeave(e); }
    protected override void OnMouseClick(MouseEventArgs e) { At(e.Location)?.Click?.Invoke(); base.OnMouseClick(e); }

    readonly List<Item> items = new();
    public IReadOnlyList<Item> Items => items;

    public Card() { BackColor = Ui.Bg; }

    public Item Add(string label, string? desc = null, Control? right = null, string? value = null)
    {
        var it = new Item { Label = label, Desc = desc, Right = right, Value = value };
        if (right != null) Controls.Add(right);
        items.Add(it);
        return it;
    }

    public Item AddBody(Control body, int pad = 12)
    {
        var it = new Item { Body = body, BodyPad = pad };
        Controls.Add(body);
        items.Add(it);
        return it;
    }

    public void SetRight(Item it, Control? right)
    {
        if (it.Right != null) { Controls.Remove(it.Right); it.Right.Dispose(); }
        it.Right = right;
        if (right != null) Controls.Add(right);
    }

    public void Clear()
    {
        foreach (var it in items) { it.Right?.Dispose(); it.Body?.Dispose(); }
        Controls.Clear();
        items.Clear();
    }

    int padX => D(14);
    int IconBox(Item it) => it.Icon == null ? 0 : D(40) + D(12);

    public int Measure(int width)
    {
        int y = 0, gap = D(16);
        bool first = true;
        foreach (var it in items)
        {
            if (it.Right != null) it.Right.Visible = !it.Hidden;
            if (it.Body != null) it.Body.Visible = !it.Hidden;
            if (it.Hidden) continue;
            if (!first) y += 1; // 줄 사이 선
            first = false;
            int h;
            if (it.Body != null)
            {
                int pad = D(it.BodyPad), inner = width - pad * 2;
                int bh = it.Body is IFlow f ? f.Measure(inner) : it.Body.Height;
                it.Body.Bounds = new Rectangle(pad, y + pad, inner, bh);
                h = bh + pad * 2;
            }
            else
            {
                int rightW = it.Right?.Width ?? (it.Value != null ? Ui.Measure(it.Value, Ui.F(13)).Width : 0);
                int textW = width - padX * 2 - IconBox(it) - (rightW > 0 ? rightW + gap : 0);
                int th = Ui.Measure(it.Label, Ui.F(13), textW).Height + (it.Desc != null ? D(2) + Ui.Measure(it.Desc, Ui.F(12), textW).Height : 0);
                h = Math.Max(D(46), Math.Max(th, Math.Max(it.Right?.Height ?? 0, it.Icon != null ? D(40) : 0)) + D(10) * 2);
                if (it.Right != null) it.Right.Location = new Point(width - padX - it.Right.Width, y + (h - it.Right.Height) / 2);
            }
            it.Box = new Rectangle(0, y, width, h);
            y += h;
        }
        return Math.Max(y, D(46));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Ui.Bg);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var box = new RectangleF(0, 0, Width - 1, Height - 1);
        Ui.Fill(g, box, D(10), Ui.Card);
        if (hover != null && !hover.Hidden) // 누를 수 있는 줄 위에 마우스: 살짝 밝게 (둥근 모서리 안에서만)
        {
            using var round = Ui.Round(box, D(10));
            var clip = g.Clip;
            g.SetClip(round);
            using (var hl = new SolidBrush(Ui.Hover)) g.FillRectangle(hl, hover.Box);
            g.Clip = clip;
        }
        Ui.Stroke(g, new RectangleF(0, 0, Width, Height), D(10), Ui.CardLine);
        g.SmoothingMode = SmoothingMode.None;
        using var line = new Pen(Ui.CardLine);
        bool first = true;
        foreach (var it in items)
        {
            if (it.Hidden) continue;
            if (!first) g.DrawLine(line, 1, it.Box.Y - 1, Width - 2, it.Box.Y - 1);
            first = false;
            if (it.Body != null) continue;
            int x = padX, gap = D(16);
            if (it.Icon != null)
            {
                int s = D(40);
                Ui.Pixel(g, it.Icon, new Rectangle(x, it.Box.Y + (it.Box.Height - s) / 2, s, s), it.IconAlpha);
                x += IconBox(it);
            }
            int rightW = it.Right?.Width ?? (it.Value != null ? Ui.Measure(it.Value, Ui.F(13)).Width : 0);
            int textW = Width - x - padX - (rightW > 0 ? rightW + gap : 0);
            var lh = Ui.Measure(it.Label, Ui.F(13), textW).Height;
            var dh = it.Desc != null ? D(2) + Ui.Measure(it.Desc, Ui.F(12), textW).Height : 0;
            int ty = it.Box.Y + (it.Box.Height - lh - dh) / 2;
            Ui.Text(g, it.Label, Ui.F(13), new Rectangle(x, ty, textW, lh), it.LabelColor ?? Ui.Fg);
            if (it.Desc != null) Ui.Text(g, it.Desc, Ui.F(12), new Rectangle(x, ty + lh + D(2), textW, dh), Ui.Dim);
            if (it.Right == null && it.Value != null)
                Ui.Text(g, it.Value, Ui.F(13), new Rectangle(Width - padX - rightW, it.Box.Y, rightW, it.Box.Height), it.ValueColor ?? Ui.Dim,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | Ui.OneLine);
        }
    }
}

// 글 한 덩이 (페이지 설명, 묶음 제목, 묶음 아래 설명)
sealed class TextBlock : View, IFlow
{
    readonly float px;
    readonly bool bold;
    public Color Ink;
    public int Indent;

    public TextBlock(string text, float px = 12, bool bold = false, Color? ink = null, int indent = 6)
    {
        (Text, this.px, this.bold, Ink, Indent) = (text, px, bold, ink ?? Ui.Dim, indent);
        BackColor = Ui.Bg;
    }

    public int Measure(int width) => Ui.Measure(Text, Ui.F(px, bold), width - D(Indent) * 2).Height;
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        Ui.Text(e.Graphics, Text, Ui.F(px, bold), new Rectangle(D(Indent), 0, Width - D(Indent) * 2, Height), Ink);
    }
}

// 페이지: 위에서 아래로 쌓고, 넘치면 스크롤. 스크롤바는 patio 처럼 오른쪽에 얇게 직접 그린다 (윈도우 기본 막대는 밝아서 튄다)
sealed class Stack : Control
{
    readonly List<(Control C, int Gap)> blocks = new();
    readonly Panel content = new() { BackColor = Ui.Bg };
    readonly ThinBar bar;
    int offset, contentH;

    public Stack()
    {
        BackColor = Ui.Bg;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        bar = new ThinBar(this);
        Controls.Add(content);
        Controls.Add(bar);
        bar.BringToFront();
    }

    public T Add<T>(T c, int gap = 0) where T : Control
    {
        blocks.Add((c, gap));
        content.Controls.Add(c);
        return c;
    }

    public void Desc(string text) => Add(new TextBlock(text, 12, indent: 4), 0);

    // 묶음: 제목, 카드, 아래 설명. 묶음 사이는 20
    public Card Group(string? title, string? foot = null) => Group(title, foot, out _, out _);

    public Card Group(string? title, string? foot, out TextBlock? head, out TextBlock? under)
    {
        bool firstBlock = blocks.Count == 0;
        head = title != null ? Add(new TextBlock(title, 12, true), firstBlock ? 0 : 20) : null;
        var card = Add(new Card(), head != null ? 7 : firstBlock ? 0 : 20);
        under = foot != null ? Add(new TextBlock(foot), 7) : null;
        return card;
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(offset - e.Delta * Ui.D(56) / 120);
        if (e is HandledMouseEventArgs h) h.Handled = true;
        base.OnMouseWheel(e);
    }

    internal int Offset => offset;
    internal int ContentHeight => contentH;

    public void ScrollTo(int y)
    {
        y = Math.Clamp(y, 0, Math.Max(0, contentH - Height));
        if (y == offset) return;
        offset = y;
        content.Top = -offset;
        bar.Invalidate();
    }

    public void Relayout()
    {
        if (blocks.Count == 0) return;
        int padX = Ui.D(24), y = Ui.D(18), w = Width - padX * 2;
        if (w <= 0) return;
        content.SuspendLayout();
        foreach (var (c, gap) in blocks)
        {
            y += Ui.D(gap);
            int h = c is IFlow f ? f.Measure(w) : c.Height;
            c.Bounds = new Rectangle(padX, y, w, h);
            c.Invalidate();
            y += h;
        }
        content.ResumeLayout();
        contentH = y + Ui.D(28);
        content.Bounds = new Rectangle(0, -offset, Width - Ui.D(12), Math.Max(contentH, Height)); // 스크롤바 자리(오른쪽 여백)와 겹치지 않게
        bar.Bounds = new Rectangle(Width - Ui.D(12), 0, Ui.D(12), Height);
        bar.Visible = contentH > Height;
        int keep = offset;
        offset = -1;
        ScrollTo(keep); // 내용이 줄었으면 끝을 넘지 않게
        if (offset < 0) { offset = 0; content.Top = 0; }
    }

    // 얇은 스크롤바: 6px 손잡이, 마우스를 올리면 진해지고 끌어서 옮길 수 있다
    sealed class ThinBar : View
    {
        readonly Stack owner;
        bool hover, dragging;
        int grabY, grabOffset;

        public ThinBar(Stack owner) { this.owner = owner; BackColor = Ui.Bg; }

        RectangleF Thumb()
        {
            int view = owner.Height, total = Math.Max(owner.ContentHeight, 1);
            float h = Math.Max(Ui.D(28), (float)view * view / total), room = view - h - Ui.D(4);
            float y = Ui.D(2) + (total > view ? room * owner.Offset / (total - view) : 0);
            return new RectangleF((Width - Ui.D(6)) / 2f, y, Ui.D(6), h);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            var t = Thumb();
            if (e.Y < t.Top || e.Y > t.Bottom) // 손잡이 밖을 누르면 그쪽으로 한 화면
                owner.ScrollTo(owner.Offset + Math.Sign(e.Y - t.Top) * (owner.Height - Ui.D(40)));
            (dragging, grabY, grabOffset) = (true, e.Y, owner.Offset);
            Capture = true;
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                var t = Thumb();
                float room = owner.Height - t.Height - Ui.D(4);
                if (room > 0) owner.ScrollTo(grabOffset + (int)((e.Y - grabY) * (owner.ContentHeight - owner.Height) / room));
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Ui.Fill(g, Thumb(), Ui.D(3), dragging ? Ui.Hex(0x55585f) : hover ? Ui.Hex(0x44474e) : Ui.Hex(0x34373c));
        }
    }
}

// 왼쪽 목록: 맨 위는 볼록이 얼굴(누르면 상태), 아래는 색 아이콘 + 이름
sealed class NavList : View
{
    public sealed record Entry(string Id, string Label, Color Color, Action<Graphics, RectangleF, Color> Glyph);

    readonly List<Entry> entries;
    string active;
    int hover = -1;
    public Image? Face;
    public string Name1 = "", Name2 = "", Foot = "";
    public event Action<string>? Picked;

    public NavList(List<Entry> entries, string active)
    {
        (this.entries, this.active) = (entries, active);
        BackColor = Ui.Side;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Active { get => active; set { active = value; Invalidate(); } }

    int ListTop => D(14) + D(64) + D(12);
    Rectangle ItemBox(int i) => new(D(10), ListTop + i * D(36), Width - D(20), D(32));
    Rectangle FaceBox => new(D(10), D(14), Width - D(20), D(64));

    int Hit(Point p)
    {
        if (FaceBox.Contains(p)) return -2;
        for (int i = 0; i < entries.Count; i++) if (ItemBox(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = Hit(e.Location);
        Cursor = h != -1 ? Cursors.Hand : Cursors.Default;
        if (h != hover) { hover = h; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int h = Hit(e.Location);
        if (h == -2) Picked?.Invoke(entries[0].Id);
        else if (h >= 0) Picked?.Invoke(entries[h].Id);
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Ui.Side);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // 오른쪽 경계선
        using (var line = new Pen(Ui.Line)) g.DrawLine(line, Width - 1, 0, Width - 1, Height);

        // 볼록이 얼굴 카드
        var fb = FaceBox;
        if (hover == -2) Ui.Fill(g, fb, D(10), Ui.Hover);
        var pic = new Rectangle(fb.X + D(8), fb.Y + (fb.Height - D(48)) / 2, D(48), D(48));
        Ui.Fill(g, pic, D(24), Ui.Card);
        if (Face != null) Ui.Pixel(g, Face, new Rectangle(pic.X + D(4), pic.Y + D(4), D(40), D(40)));
        int tx = pic.Right + D(10), tw = fb.Right - tx - D(6);
        Ui.Text(g, Name1, Ui.F(14, true), new Rectangle(tx, fb.Y + D(14), tw, D(20)), Ui.Fg, Ui.OneLine);
        Ui.Text(g, Name2, Ui.F(12), new Rectangle(tx, fb.Y + D(35), tw, D(18)), Ui.Dim, Ui.OneLine);

        for (int i = 0; i < entries.Count; i++)
        {
            var en = entries[i];
            var r = ItemBox(i);
            bool on = en.Id == active;
            if (on) Ui.Fill(g, r, D(7), Ui.NavSel);
            else if (i == hover) Ui.Fill(g, r, D(7), Ui.Hover);
            var badge = new RectangleF(r.X + D(8), r.Y + (r.Height - D(22)) / 2f, D(22), D(22));
            Ui.Fill(g, badge, D(6), en.Color);
            float gs = D(14);
            en.Glyph(g, new RectangleF(badge.X + (badge.Width - gs) / 2, badge.Y + (badge.Height - gs) / 2, gs, gs), en.Color);
            Ui.Text(g, en.Label, Ui.F(13), new Rectangle((int)badge.Right + D(10), r.Y, r.Right - (int)badge.Right - D(14), r.Height), on ? Color.White : Ui.Fg,
                TextFormatFlags.VerticalCenter | Ui.OneLine);
        }
        Ui.Text(g, Foot, Ui.F(11), new Rectangle(D(20), Height - D(32), Width - D(40), D(18)), Ui.Hex(0x5f636b), Ui.OneLine);
    }
}

// 목록 아이콘 (흰 그림, 14px 상자 안)
static class Glyphs
{
    static float S(RectangleF b) => b.Width / 14f;

    public static void Heart(Graphics g, RectangleF b, Color _)
    {
        float s = S(b), r = 3.4f * s;
        g.FillEllipse(Brushes.White, b.X + 4.1f * s - r, b.Y + 5 * s - r, r * 2, r * 2);
        g.FillEllipse(Brushes.White, b.X + 9.9f * s - r, b.Y + 5 * s - r, r * 2, r * 2);
        g.FillPolygon(Brushes.White, new[] { new PointF(b.X + 1 * s, b.Y + 6.4f * s), new PointF(b.X + 13 * s, b.Y + 6.4f * s), new PointF(b.X + 7 * s, b.Y + 12.8f * s) });
    }

    public static void Paw(Graphics g, RectangleF b, Color _)
    {
        float s = S(b);
        g.FillEllipse(Brushes.White, b.X + 3.6f * s, b.Y + 6.6f * s, 6.8f * s, 6 * s);
        foreach (var (x, y) in new[] { (.6f, 4.2f), (3.6f, 1), (7.4f, 1), (10.4f, 4.2f) })
            g.FillEllipse(Brushes.White, b.X + x * s, b.Y + y * s, 3 * s, 3.6f * s);
    }

    public static void Gear(Graphics g, RectangleF b, Color badge)
    {
        float s = S(b), cx = b.X + 7 * s, cy = b.Y + 7 * s;
        var st = g.Save();
        for (int i = 0; i < 8; i++)
        {
            g.TranslateTransform(cx, cy);
            g.RotateTransform(i * 45);
            g.FillRectangle(Brushes.White, -1.5f * s, -6.8f * s, 3 * s, 3 * s);
            g.ResetTransform();
        }
        g.Restore(st);
        g.FillEllipse(Brushes.White, cx - 5 * s, cy - 5 * s, 10 * s, 10 * s);
        using var hole = new SolidBrush(badge);
        g.FillEllipse(hole, cx - 2.1f * s, cy - 2.1f * s, 4.2f * s, 4.2f * s);
    }

    public static void Star(Graphics g, RectangleF b, Color _)
    {
        float s = S(b), cx = b.X + 7 * s, cy = b.Y + 7.4f * s;
        var pts = new PointF[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, r = (i % 2 == 0 ? 6.6 : 2.8) * s;
            pts[i] = new PointF(cx + (float)(Math.Cos(a) * r), cy + (float)(Math.Sin(a) * r));
        }
        g.FillPolygon(Brushes.White, pts);
    }

    public static void Download(Graphics g, RectangleF b, Color _)
    {
        float s = S(b);
        using var pen = new Pen(Color.White, 1.7f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLine(pen, b.X + 7 * s, b.Y + 1.5f * s, b.X + 7 * s, b.Y + 9 * s);
        g.DrawLines(pen, new[] { new PointF(b.X + 3.6f * s, b.Y + 5.8f * s), new PointF(b.X + 7 * s, b.Y + 9.2f * s), new PointF(b.X + 10.4f * s, b.Y + 5.8f * s) });
        g.DrawLines(pen, new[] { new PointF(b.X + 1.5f * s, b.Y + 9.5f * s), new PointF(b.X + 1.5f * s, b.Y + 12.5f * s), new PointF(b.X + 12.5f * s, b.Y + 12.5f * s), new PointF(b.X + 12.5f * s, b.Y + 9.5f * s) });
    }
}
