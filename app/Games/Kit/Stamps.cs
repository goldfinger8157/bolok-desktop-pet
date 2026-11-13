using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bolok;

// 판 위에 찍는 그림 한 장: 칸(40×배율)에서 빈 둘레를 잘라 내고 미리 곱한 알파(PArgb)로 바꿔 둔 것.
// 벌레·보석처럼 칸보다 훨씬 작은 그림을 수백 장 찍을 때 칠하는 픽셀이 몇 분의 일로 준다 (찍히는 모양은 칸째 그릴 때와 같다)
sealed class Stamp
{
    public readonly Bitmap Bmp;
    public readonly int Dx, Dy;   // 칸 가운데에서 잘라 낸 그림 왼쪽 위까지
    public Rectangle Box(int cx, int cy) => new(cx + Dx, cy + Dy, Bmp.Width, Bmp.Height); // 칸 가운데가 (cx, cy) 일 때 그려지는 자리

    internal Stamp(Bitmap cell)
    {
        var box = Opaque(cell);
        Bmp = cell.Clone(box, PixelFormat.Format32bppPArgb);
        (Dx, Dy) = (box.X - cell.Width / 2, box.Y - cell.Height / 2);
    }

    // 칸 가운데가 창 좌표 (cx, cy) 에 오게
    public void Draw(Graphics g, int cx, int cy) => g.DrawImageUnscaled(Bmp, cx + Dx, cy + Dy);

    // 칸 가운데가 화면 좌표 (x, y) 에 오게 (Overlay.Put 과 같은 자리)
    public void Put(Overlay o, Graphics g, int x, int y) => g.DrawImageUnscaled(Bmp, x - o.Left + Dx, y - o.Top + Dy);

    // 칸 왼쪽 위가 창 좌표 (x, y) 에 오게 (칸째 DrawImageUnscaled 하던 자리)
    public void DrawCell(Graphics g, int x, int y, int cellPx) => g.DrawImageUnscaled(Bmp, x + cellPx / 2 + Dx, y + cellPx / 2 + Dy);

    static Rectangle Opaque(Bitmap b)
    {
        var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
        try
        {
            var row = new int[b.Width];
            for (int y = 0; y < b.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, b.Width);
                for (int x = 0; x < row.Length; x++)
                    if ((uint)row[x] >> 24 != 0) (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
            }
        }
        finally { b.UnlockBits(data); }
        return x1 < 0 ? new Rectangle(b.Width / 2, b.Height / 2, 1, 1) : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1); // 빈 그림은 한 점
    }
}

// 그림 묶음(Sprites)마다 하나: 찍을 그림을 이름·프레임·배율·뒤집기로 찾아 한 번만 만든다.
// 자주 쓰는 그림은 게임이 판을 열 때 Row 로 받아 들고 있으면 프레임마다 찾지 않아도 된다
sealed class Stamps
{
    static readonly ConditionalWeakTable<Sprites, Stamps> all = new();
    public static Stamps Of(Sprites sp) => all.GetValue(sp, s => new Stamps(s));

    readonly Sprites sp;
    readonly Dictionary<(string, int, bool), Stamp[]> rows = new();
    readonly Dictionary<int, Stamp[]> digits = new();
    readonly Dictionary<string, bool> has = new();
    Stamps(Sprites sp) => this.sp = sp;

    public bool Has(string name) => has.TryGetValue(name, out bool yes) ? yes : has[name] = sp.Has(name);

    // 도트 숫자 0~9 와 + (10), × (11)
    static readonly string[] DigitNames = { "num/0", "num/1", "num/2", "num/3", "num/4", "num/5", "num/6", "num/7", "num/8", "num/9", "num/+", "num/x" };
    public Stamp[] Digits(int scale)
    {
        if (digits.TryGetValue(scale, out var d)) return d;
        d = new Stamp[DigitNames.Length];
        for (int i = 0; i < d.Length; i++) d[i] = new Stamp(sp.Get(DigitNames[i], 0, scale));
        return digits[scale] = d;
    }

    public Stamp Get(string name, int frame, int scale, bool flip = false)
    {
        var row = Row(name, scale, flip);
        return row[frame % row.Length];
    }

    // 그 줄의 모든 프레임 (프레임 번호로 바로 꺼낸다: row[n % row.Length])
    public Stamp[] Row(string name, int scale, bool flip = false)
    {
        if (rows.TryGetValue((name, scale, flip), out var row)) return row;
        row = new Stamp[sp.Frames(name)];
        for (int i = 0; i < row.Length; i++) row[i] = new Stamp(sp.Get(name, i, scale, flip));
        return rows[(name, scale, flip)] = row;
    }
}
