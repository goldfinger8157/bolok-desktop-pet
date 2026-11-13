namespace Bolok;

// 게임 조준경 = 실제 마우스 커서. 앱이 그리면 한두 프레임 늦게 따라와 굼뜨다
static class Crosshair
{
    public static readonly int[] Gaps = { 5, 10, 16, 24, 34 }; // 퍼짐 단계별 십자 틈(px, 배율 3 기준)
    static readonly Dictionary<(int, bool, int), Cursor> made = new();
    static readonly Dictionary<(int, bool, int), Bitmap> drawn = new();

    public static int Level(double bloom)
    {
        int level = 0;
        for (int i = 0; i < Gaps.Length; i++) if (Gaps[i] <= 5 + bloom * .9) level = i;
        return level;
    }

    public static Cursor Get(int level, bool hit, int scale)
    {
        if (made.TryGetValue((level, hit, scale), out var c)) return c;
        var bmp = Art(level, hit, scale);
        return made[(level, hit, scale)] = Native.MakeCursor(bmp, bmp.Width / 2, bmp.Height / 2);
    }

    // 조준경 그림 (가운데가 겨눈 자리). 커서를 못 쓰는 장면 그림(--mode-scene)도 이것을 그린다
    public static Bitmap Art(int level, bool hit, int scale)
    {
        if (drawn.TryGetValue((level, hit, scale), out var bmp)) return bmp;
        int gap = Gaps[level] * scale / 3, arm = 10 * scale / 3, th = Math.Max(2, scale * 2 / 3), size = (gap + arm + 4) * 2 + 1, mid = size / 2;
        bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            using var edge = new SolidBrush(Color.FromArgb(30, 30, 30));
            var ink = hit ? Pal.Red : Pal.White;
            for (int n = 0; n < 4; n++)
            {
                var (dx, dy) = n switch { 0 => (1, 0), 1 => (-1, 0), 2 => (0, 1), _ => (0, -1) };
                var r = dx != 0
                    ? new Rectangle(dx > 0 ? mid + gap : mid - gap - arm, mid - th / 2, arm, th)
                    : new Rectangle(mid - th / 2, dy > 0 ? mid + gap : mid - gap - arm, th, arm);
                g.FillRectangle(edge, Rectangle.Inflate(r, 1, 1));
                g.FillRectangle(ink, r);
            }
            g.FillRectangle(edge, mid - th / 2 - 1, mid - th / 2 - 1, th + 2, th + 2);
            g.FillRectangle(ink, mid - th / 2, mid - th / 2, th, th);
        }
        return drawn[(level, hit, scale)] = bmp;
    }
}
