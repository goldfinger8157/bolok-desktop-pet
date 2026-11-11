namespace Bolok;

// 게임 조준경 = 실제 마우스 커서. 앱이 그리면 한두 프레임 늦게 따라와 굼뜨다
public static class Crosshair
{
    public static readonly int[] Gaps = { 5, 10, 16, 24, 34 }; // 퍼짐 단계별 십자 틈(px, 배율 3 기준)
    static readonly Dictionary<(int, bool, int), Cursor> made = new();

    public static int Level(double bloom) => Array.FindLastIndex(Gaps, g => g <= 5 + bloom * .9) is var i and >= 0 ? i : 0;

    public static Cursor Get(int level, bool hit, int scale)
    {
        if (made.TryGetValue((level, hit, scale), out var c)) return c;
        int gap = Gaps[level] * scale / 3, arm = 10 * scale / 3, th = Math.Max(2, scale * 2 / 3), size = (gap + arm + 4) * 2 + 1, mid = size / 2;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            using var edge = new SolidBrush(Color.FromArgb(30, 30, 30));
            using var ink = new SolidBrush(hit ? Color.FromArgb(232, 72, 92) : Color.White);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var r = dx != 0
                    ? new Rectangle(dx > 0 ? mid + gap : mid - gap - arm, mid - th / 2, arm, th)
                    : new Rectangle(mid - th / 2, dy > 0 ? mid + gap : mid - gap - arm, th, arm);
                g.FillRectangle(edge, Rectangle.Inflate(r, 1, 1));
                g.FillRectangle(ink, r);
            }
            g.FillRectangle(edge, mid - th / 2 - 1, mid - th / 2 - 1, th + 2, th + 2);
            g.FillRectangle(ink, mid - th / 2, mid - th / 2, th, th);
        }
        return made[(level, hit, scale)] = Native.MakeCursor(bmp, mid, mid);
    }
}
