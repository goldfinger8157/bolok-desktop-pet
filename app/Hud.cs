namespace Bolok;

// 상태창: 달걀 기기의 LCD. 디버그 모드에서는 내부 값도 함께 보여준다
public sealed class Hud : Form
{
    readonly Brain b;
    readonly Func<string>? debugLine;
    readonly Font big, small;
    readonly SolidBrush ink = new(Color.FromArgb(38, 48, 30));
    string shown = "";

    public Func<string> RunLine = () => "";

    // 어른이 될 쪽 비율 (아기·어린이 때만): 통56 애30 장14 심0
    string Growth()
    {
        var p = b.P;
        if (p.Stage is not ("baby" or "child")) return "";
        var gr = p.Growth(b.K);
        double sum = gr.Sum();
        return sum <= 0 ? "" : $"통{gr[0] / sum * 100:F0} 애{gr[1] / sum * 100:F0} 장{gr[2] / sum * 100:F0} 심{gr[3] / sum * 100:F0}";
    }
    public Func<string> SpeciesName = () => "";   // 도감 이름 (알이면 빈칸)

    public Hud(Brain brain, Func<string>? debugLine)
    {
        b = brain;
        this.debugLine = debugLine;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(188, 201, 166);
        big = new Font("Malgun Gothic", 8.5f, FontStyle.Bold); // 작게 (YELL 2026-11-13: 글자·창을 줄여)
        small = new Font("Malgun Gothic", 7.5f);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(LogicalToDeviceUnits(196), LogicalToDeviceUnits(debugLine != null ? 128 : 96));
        Click += (_, _) => Hide();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var c = base.CreateParams;
            c.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return c;
        }
    }

    public void PlaceAt(Rectangle area) => Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);

    static string Hearts(double v)
    {
        int n = (int)Math.Ceiling(v / 25);
        return new string('♥', n) + new string('♡', 4 - n);
    }

    string[] Lines()
    {
        var p = b.P;
        string kind = SpeciesName() is { Length: > 0 } sp ? $"{sp} {Names.Form(p.Stage)}" : Names.Form(p.Stage);
        return new[]
        {
            $"볼록이 · {kind}    {(int)(p.AgeMin / 60)}시간 {(int)(p.AgeMin % 60)}분",
            $"포만 {Hearts(p.Full)}",
            $"기분 {Hearts(p.Mood)}",
            $"기운 {Hearts(p.Energy)}",
            $"볼 속 {Math.Max(p.Bites, b.Holes.Count)}입  밥 {p.Food}/{b.K.FoodMax}  {Growth()}  {RunLine()}",
            debugLine?.Invoke() ?? "",
        };
    }

    public void Sync()
    {
        if (!Visible) return;
        var text = string.Join("\n", Lines());
        if (text != shown) { shown = text; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        int y = LogicalToDeviceUnits(6), x = LogicalToDeviceUnits(8), line = LogicalToDeviceUnits(16);
        var lines = shown.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == 5)
                e.Graphics.DrawString(lines[i], small, ink, new RectangleF(x, y, Width - x * 2, line * 2));
            else
                e.Graphics.DrawString(lines[i], big, ink, x, y);
            y += line;
        }
    }
}
