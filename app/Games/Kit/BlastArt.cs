using System.Drawing.Imaging;

namespace Bolok;

// 폭발 그림 도우미: 찢긴 구멍(칸 격자)과 들쭉날쭉한 불덩이. 모양은 Ragged 가 정한다
static class BlastArt
{
    // ── 구멍: 안쪽은 hole, 한 칸 두께 가장자리와 바깥으로 뻗는 금(반 칸)은 rim. ctr 은 창 좌표 ──
    public static void Hole(Graphics g, Ragged s, Point ctr, double r, double r0, int cell, Brush rim, Brush hole) =>
        Hole(g, s.Cells(r, cell), s.Cracks(r, r0, cell / 2), ctr, cell, rim, hole);

    // 모양을 미리 뽑아 둔 것으로 (프레임마다 다시 계산하지 않게 게임이 들고 있다)
    public static void Hole(Graphics g, List<(int I, int J, bool Core)> cells, List<(int X, int Y)> cracks, Point ctr, int cell, Brush rim, Brush hole)
    {
        foreach (var (i, j, core) in cells) g.FillRectangle(core ? hole : rim, ctr.X + i * cell, ctr.Y + j * cell, cell, cell);
        foreach (var (x, y) in cracks) g.FillRectangle(rim, ctr.X + x, ctr.Y + y, cell / 2, cell / 2);
    }

    // ── 불덩이: 하얀 섬광 → 노랑 → 주황·빨강 → 가운데부터 걷히는 연기 (game/boom 과 같은 색 차례를 들쭉날쭉한 윤곽으로) ──
    public const int Frames = 5, Variants = 8;   // 모양 넷 × 좌우 뒤집기
    public const double Sec = .45;               // 다섯 장이 지나가는 시간

    static readonly double[] Grow = { 5 / 18.0, 9 / 18.0, 13 / 18.0, 16 / 18.0, 1 };
    static readonly (Color Core, Color Rim)[] Rings =
    {
        (Color.FromArgb(255, 255, 240), Color.FromArgb(255, 230, 90)), (Color.FromArgb(255, 220, 70), Color.FromArgb(250, 140, 40)),
        (Color.FromArgb(250, 140, 40), Color.FromArgb(210, 60, 40)), (Color.FromArgb(120, 110, 110), Color.FromArgb(210, 60, 40)),
        (Color.FromArgb(150, 145, 145), Color.FromArgb(110, 105, 105)),
    };
    static readonly Color Spark = Color.FromArgb(255, 220, 70);
    static readonly Dictionary<(int, int, int, int), Bitmap> bursts = new();

    public static int Variant(double x, double y) => (int)((uint)(int)(x * 7 + y * 13) % Variants); // 터진 자리로 정한다 (터지는 동안 같은 모양)
    public static int Frame(double age, double sec = Sec) => Math.Clamp((int)(age / sec * Frames), 0, Frames - 1);
    static int Half(int rPx, int px) => ((int)Math.Ceiling(rPx * 1.35 / px) + 1) * px; // 그림 한 변의 절반 (가장 크게 퍼졌을 때 + 불티)

    // 다 퍼졌을 때 반지름이 rPx 쯤인 불덩이의 frame 번째 장. 도트 한 칸은 px. 그림 가운데가 터진 자리다. 크기별로 한 번만 그려 둔다
    public static Bitmap Burst(int variant, int frame, int rPx, int px)
    {
        px = Math.Max(1, px);
        rPx = Math.Max(2 * px, (rPx + px / 2) / px * px);
        (variant, frame) = (variant & (Variants - 1), Math.Clamp(frame, 0, Frames - 1));
        var key = (variant, frame, rPx, px);
        if (bursts.TryGetValue(key, out var hit)) return hit;
        if (bursts.Count > 400) bursts.Clear(); // 크기가 끝없이 달라지는 게임이 있어도 쌓이지 않게 (버린 그림은 GC 가 치운다)

        var shape = Ragged.Seeded(1009 * ((variant & 3) + 1));
        bool flip = (variant & 4) != 0;
        int n = Half(rPx, px) / px;
        double r = rPx * Grow[frame];
        var bmp = new Bitmap(2 * n * px, 2 * n * px, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        using var core = new SolidBrush(Rings[frame].Core);
        using var rim = new SolidBrush(Rings[frame].Rim);
        void Dot(Brush b, int i, int j) { if (i >= -n && i < n && j >= -n && j < n) g.FillRectangle(b, ((flip ? -i - 1 : i) + n) * px, (j + n) * px, px, px); }
        for (int j = -n; j < n; j++)
            for (int i = -n; i < n; i++)
            {
                double x = (i + .5) * px, y = (j + .5) * px, d = Math.Sqrt(x * x + y * y), lim = r * shape.K(Math.Atan2(y, x));
                if (d >= lim || frame >= 3 && d < lim * .5) continue; // 연기는 가운데부터 걷힌다
                Dot(d < lim * .65 ? core : rim, i, j);
            }
        if (frame <= 2) // 사방으로 튀는 불티: 모양마다 6~9 방향
        {
            using var spark = new SolidBrush(Spark);
            var rng = new Random(variant & 3);
            int count = 6 + rng.Next(4);
            for (int m = 0; m < count; m++)
            {
                double a = (m + rng.NextDouble() * .7) * Math.PI * 2 / count, lim = r * shape.K(a);
                for (int t = 0; t < 2; t++)
                {
                    double d = lim + (2 + t * 2) * px;
                    Dot(spark, (int)Math.Floor(Math.Cos(a) * d / px), (int)Math.Floor(Math.Sin(a) * d / px));
                }
            }
        }
        return bursts[key] = bmp;
    }

    // 터진 지 age 초 된 불덩이를 화면 좌표 (x, y) 에 (Sec 가 지나면 마지막 장: 부르는 쪽이 그 전에 지운다)
    public static void Pop(Overlay o, Graphics g, double x, double y, double r, double age, int px)
    {
        var bmp = Burst(Variant(x, y), Frame(age), (int)r, px);
        g.DrawImageUnscaled(bmp, (int)x - o.Left - bmp.Width / 2, (int)y - o.Top - bmp.Height / 2);
    }

    // 그 불덩이가 그려지는 자리 (화면 좌표, 다시 칠할 자리)
    public static Rectangle PopRect(double x, double y, double r, int px)
    {
        px = Math.Max(1, px);
        int half = Half(Math.Max(2 * px, ((int)r + px / 2) / px * px), px);
        return new Rectangle((int)x - half, (int)y - half, 2 * half, 2 * half);
    }
}
