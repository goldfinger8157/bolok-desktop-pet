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
    public Func<string> SpeciesName = () => "";   // 도감 이름 (알이면 빈칸)

    public Hud(Brain brain, Func<string>? debugLine)
    {
        b = brain;
        this.debugLine = debugLine;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(188, 201, 166);
        big = new Font("Malgun Gothic", 10.5f, FontStyle.Bold);
        small = new Font("Malgun Gothic", 8.5f);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Size = new Size(LogicalToDeviceUnits(230), LogicalToDeviceUnits(debugLine != null ? 165 : 122));
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

    // 설정 창 상태 탭도 이 글을 쓴다 (디버그 줄은 빼고)
    public string StatusText() => string.Join("\n", Lines().Take(5));

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
            $"실수 {p.Miss}    볼 속 {Math.Max(p.Bites, b.Holes.Count)}입  {RunLine()}",
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
        int y = LogicalToDeviceUnits(8), x = LogicalToDeviceUnits(10), line = LogicalToDeviceUnits(21);
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
