using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Bolok;

// 클라이밍 그림 (docs/climb.md 5장, 시안 run/mockups/climb): 칸(그림 한 점) 단위로 그린 잎·덩굴·줍는 것·구름·표시 그림.
// 판을 열 때 한 번 만들어 두고 프레임에서는 칸 그림판(Px)에 찍기만 한다. 알파는 0·255 뿐 (색 열쇠 창으로 되는 것).
static class ClimbArt
{
    // ── 색 ──
    public static uint C(int r, int g, int b, int a = 255) => (uint)a << 24 | (uint)(r << 16 | g << 8 | b);
    public static uint C(string hex) => 0xFF000000u | Convert.ToUInt32(hex.TrimStart('#'), 16) & 0xFFFFFF;
    public static uint Mix(uint a, uint b, double t)
    {
        int M(int sh) { int x = (int)(a >> sh & 255), y = (int)(b >> sh & 255); return (int)Math.Round(x + (y - x) * t); }
        return C(M(16), M(8), M(0));
    }
    public static readonly uint Line = C(92, 58, 40), White = C(255, 255, 255), Cream = C(255, 243, 224), Red = C(232, 72, 92), Gold = C(250, 185, 40);
    public static readonly uint StemFill = C("#5DAA45"), StemHi = C("#93D46A");
    public static readonly uint ShadowDot = C("#7E8883");

    // 잎 색 묶음 (보통 · 높이 구역마다 조금씩)
    public sealed record LeafPal(uint Fill, uint Hi, uint Shade, uint Vein);
    public static readonly LeafPal Fresh = new(C("#7CCB4E"), C("#B2E57A"), C("#4F9E3C"), C("#5DAE44"));
    public static readonly LeafPal Wilt = new(C("#D9B04C"), C("#EFD584"), C("#A97B34"), C("#B98B3A"));
    public static readonly LeafPal[] ZoneLeaf =
    {
        Fresh,
        new(C("#5FB548"), C("#96D86C"), C("#3C8836"), C("#4A9E3E")),          // 나무 위: 짙은 초록
        new(C("#93D6A0"), C("#CDF0D2"), C("#5FA97A"), C("#79BE8C")),          // 구름: 옅은 민트
        new(C("#C7C25A"), C("#F0D88A"), C("#8E8A3E"), C("#A9A148")),          // 노을: 노을빛 받은 잎
        new(C("#5BAE9A"), C("#9FE0CC"), C("#2F7468"), C("#479A88")),          // 별밤: 푸른 잎
        new(C("#9EC9C0"), C("#E4F6F2"), C("#5E8C88"), C("#7EAAA4")),          // 달: 은빛 잎
    };

    // ── 칸 그림판 (0 = 빈 칸, 알파가 있으면 찍힌 칸) ──
    public sealed class Px
    {
        public readonly int W, H;
        public readonly uint[] P;
        public Px(int w, int h) { W = w; H = h; P = new uint[w * h]; }
        public bool On(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && P[y * W + x] >> 24 != 0;
        public void Set(int x, int y, uint c) { if (x >= 0 && y >= 0 && x < W && y < H) P[y * W + x] = c; }
        public void Put(int x, int y, uint c, bool clip = false)
        {
            if (c >> 24 == 0 || x < 0 || y < 0 || x >= W || y >= H) return;
            int i = y * W + x;
            if (clip && P[i] >> 24 == 0) return;
            P[i] = c;
        }
        public void Fill(int x, int y, int w, int h, uint c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Put(i, j, c); }
        public void Ell(double cx, double cy, double rx, double ry, uint c, bool clip = false)
        {
            if (rx <= 0 || ry <= 0) return;
            for (int y = (int)(cy - ry) - 1; y <= (int)(cy + ry) + 1; y++)
                for (int x = (int)(cx - rx) - 1; x <= (int)(cx + rx) + 1; x++)
                {
                    double a = (x + .5 - cx) / rx, b = (y + .5 - cy) / ry;
                    if (a * a + b * b < 1) Put(x, y, c, clip);
                }
        }
        public void Poly((double X, double Y)[] pts, uint c, bool clip = false)
        {
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X), minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            for (int y = (int)minY - 1; y <= (int)maxY + 1; y++)
                for (int x = (int)minX - 1; x <= (int)maxX + 1; x++)
                {
                    double px = x + .5, py = y + .5;
                    bool inside = false;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        var (x1, y1) = pts[i];
                        var (x2, y2) = pts[(i + pts.Length - 1) % pts.Length];
                        if ((y1 > py) != (y2 > py) && px < (x2 - x1) * (py - y1) / (y2 - y1) + x1) inside = !inside;
                    }
                    if (inside) Put(x, y, c, clip);
                }
        }
        public void LineTo(double x0, double y0, double x1, double y1, uint c)
        {
            int n = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0))) + 1;
            for (int i = 0; i <= n; i++) { double t = (double)i / n; Put((int)Math.Floor(x0 + (x1 - x0) * t), (int)Math.Floor(y0 + (y1 - y0) * t), c); }
        }
        public void Outline(uint c)
        {
            var edge = new List<int>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (On(x, y)) continue;
                    if (On(x + 1, y) || On(x - 1, y) || On(x, y + 1) || On(x, y - 1)) edge.Add(y * W + x);
                }
            foreach (var i in edge) P[i] = c;
        }
        public Px Flipped()
        {
            var r = new Px(W, H);
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) r.P[y * W + x] = P[y * W + W - 1 - x];
            return r;
        }
        // 찍힌 칸만 (x, y) 에 얹는다. 판 밖은 자른다
        public void Blit(Px s, int x, int y)
        {
            int x0 = Math.Max(0, -x), x1 = Math.Min(s.W, W - x), y0 = Math.Max(0, -y), y1 = Math.Min(s.H, H - y);
            if (x0 >= x1) return;
            for (int j = y0; j < y1; j++)
            {
                int si = j * s.W, di = (y + j) * W + x;
                for (int i = x0; i < x1; i++) { uint c = s.P[si + i]; if (c >> 24 != 0) P[di + i] = c; }
            }
        }
    }

    // 칸 40×40 그림(볼록이 한 장)을 칸 그림판으로 (알파 128 아래는 빈 칸)
    public static uint[] FromBitmap(Bitmap b)
    {
        var px = new int[b.Width * b.Height];
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < b.Height; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * b.Width, b.Width);
        b.UnlockBits(d);
        var r = new uint[px.Length];
        for (int i = 0; i < px.Length; i++) r[i] = (uint)px[i] >> 24 < 128 ? 0 : (uint)px[i] | 0xFF000000u;
        return r;
    }

    public static Px Glyph(string[] rows, Func<char, uint> pal)
    {
        int w = rows.Max(r => r.Length);
        var g = new Px(w, rows.Length);
        for (int y = 0; y < rows.Length; y++) for (int x = 0; x < rows[y].Length; x++) if (rows[y][x] != '.') g.Put(x, y, pal(rows[y][x]));
        return g;
    }

    public static readonly int[,] Bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };

    // ── 잎 (dir +1: 끝이 오른쪽). Ax·Ay: 잎 윗면 가운데 (볼록이 발이 닿는 줄) ──
    public sealed record Sprite(Px P, int Ax, int Ay);

    public static Sprite Leaf(LeafPal pal, int dir, bool droop = false, bool big = false)
    {
        double s = big ? 1.25 : 1;
        int w = (int)(32 * s), h = (int)(14 * s);
        var g = new Px(w, h);
        double cx = 14 * s, cy = 6 * s;
        g.Ell(cx, cy, 9.6 * s, 3.7 * s, pal.Fill);
        g.Ell(cx - 7.5 * s, cy + .6 * s, 3.2 * s, 2.9 * s, pal.Fill); // 잎 밑 (하트 모양 둥근 쪽)
        if (droop) g.Poly(new[] { (cx + 6 * s, cy - 2.6 * s), (cx + 11.5 * s, cy + 4.8 * s), (cx + 5.5 * s, cy + 3.3 * s) }, pal.Fill);
        else g.Poly(new[] { (cx + 6 * s, cy - 3.2 * s), (cx + 13.5 * s, cy - 3.6 * s), (cx + 6.5 * s, cy + 3.2 * s) }, pal.Fill);
        // 아랫면 그늘 · 윗면 빛 · 잎맥
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!g.On(x, y)) continue;
                if (y >= cy + 1.6 * s) g.Set(x, y, pal.Shade);
                else if (y <= cy - 2.4 * s && x > cx - 8 * s && x < cx + 6 * s && g.On(x, y - 1) == false) g.Set(x, y, pal.Hi);
            }
        for (double t = 0; t <= 1; t += .02)
        {
            double x = cx - 8 * s + t * (droop ? 15 : 19) * s, y = cy + .2 * s - Math.Sin(t * Math.PI) * .9 * s + (droop ? t * t * 3 * s : -t * 1.2 * s);
            if (g.On((int)x, (int)y)) g.Set((int)x, (int)y, pal.Vein);
        }
        if (pal == Wilt) foreach (var (sx, sy) in new[] { (cx - 3, cy - 1), (cx + 4, cy + 1), (cx + 1, cy - 2) }) g.Put((int)sx, (int)sy, C("#8E6430"), true);
        g.Outline(Line);
        if (dir < 0) g = g.Flipped();
        int top = 0;
        for (int y = 0; y < h && top == 0; y++) for (int x = 0; x < w; x++) if (g.On(x, y)) { top = y; break; }
        int ax = dir > 0 ? (int)Math.Round(cx) : w - 1 - (int)Math.Round(cx);
        return new Sprite(g, ax, top + 1);
    }

    // ── 줍는 것·기믹 ──
    public static Px Berry()
    {
        var g = new Px(9, 9);
        g.Ell(4.5, 5, 3.1, 3, Red);
        g.Ell(4.5, 6.3, 2.6, 1.6, C("#C23A50"), true);
        g.Put(3, 4, White); g.Put(3, 3, C("#FFC2CC"));
        g.Put(4, 1, C("#4E9A3C")); g.Put(3, 2, C("#6FBE4A")); g.Put(5, 2, C("#6FBE4A")); g.Put(4, 2, C("#4E9A3C"));
        g.Outline(Line);
        return g;
    }

    public static Px Puff()
    {
        var g = new Px(17, 19);
        uint stalk = C("#7BB35A"), spoke = C("#D3DAE2"), tuft = C("#FFFFFF");
        g.LineTo(8, 9, 8, 18, stalk);
        g.Put(7, 18, stalk); g.Put(9, 18, stalk);
        for (int i = 0; i < 14; i++)
        {
            double a = i * Math.PI * 2 / 14 + .2;
            for (double r = 1; r < 4.6; r += .5) g.Put((int)Math.Floor(8.5 + Math.Cos(a) * r), (int)Math.Floor(7.5 + Math.Sin(a) * r), spoke);
            g.Ell(8.5 + Math.Cos(a) * 5.4, 7.5 + Math.Sin(a) * 5.4, 1.15, 1.15, tuft);
        }
        g.Ell(8.5, 7.5, 1.6, 1.6, C("#C9B48A"));
        g.Outline(C("#9AA6B2"));
        return g;
    }

    // 머리 위에 떠 있는 홀씨 (줄기 없이)
    public static Px PuffHead()
    {
        var g = new Px(17, 13);
        uint spoke = C("#D3DAE2"), tuft = C("#FFFFFF");
        for (int i = 0; i < 14; i++)
        {
            double a = i * Math.PI * 2 / 14 + .2;
            for (double r = 1; r < 4.6; r += .5) g.Put((int)Math.Floor(8.5 + Math.Cos(a) * r), (int)Math.Floor(6.5 + Math.Sin(a) * r), spoke);
            g.Ell(8.5 + Math.Cos(a) * 5.4, 6.5 + Math.Sin(a) * 5.4, 1.15, 1.15, tuft);
        }
        g.Ell(8.5, 6.5, 1.6, 1.6, C("#C9B48A"));
        g.Outline(C("#9AA6B2"));
        return g;
    }

    // 덩굴손: 말림 · 펴짐 (던져 올릴 때)
    public static Px Curl(bool open = false)
    {
        var g = new Px(15, 19);
        uint c = C("#A6DC6A"), d = C("#6DB24A");
        g.LineTo(6, 18, 6, 11, d); g.LineTo(7, 18, 7, 11, d);
        if (open)
        {
            for (double t = 0; t < 1; t += .01) { int x = (int)Math.Floor(6.5 + Math.Sin(t * 5) * 2.5), y = (int)Math.Floor(11 - t * 11); g.Put(x, y, c); g.Put(x + 1, y, c); }
        }
        else
            for (double t = 0; t < 1; t += .003)
            {
                double a = Math.PI + t * Math.PI * 2.7, r = 5.2 * (1 - t * .8);
                int x = (int)Math.Floor(7.5 + 5.2 + Math.Cos(a) * r), y = (int)Math.Floor(6.5 + Math.Sin(a) * r);
                g.Put(x, y, c); if (t < .55) g.Put(x, y + 1, c);
            }
        g.Outline(Line);
        return g;
    }

    // 덩굴손이 풀리며 튀어 오르는 모습 (밟는 순간): 1 풀리는 중 (큰 곡선) · 2 쭉 뻗음 (튀어 오름) · 3 되튐 (조금 줄어듦). 맨 아래 줄 가운데가 잎 위, 같은 너비 15 (아래를 맞춰 얹는다)
    public static Px CurlSpring(int frame)
    {
        int h = frame == 2 ? 31 : 24;
        var g = new Px(15, h);
        uint c = C("#A6DC6A"), d = C("#6DB24A");
        int baseY = h - 1;
        g.LineTo(6, baseY, 6, baseY - 7, d); g.LineTo(7, baseY, 7, baseY - 7, d);
        double top = baseY - 7, amp = frame == 1 ? 4.6 : frame == 2 ? 2.2 : 3.4, len = frame == 1 ? 12 : frame == 2 ? 22 : 13;
        for (double t = 0; t < 1; t += .006)
        {
            int x = (int)Math.Floor(6.5 + Math.Sin(t * (frame == 1 ? 6.5 : 5)) * amp * (1 - t * .35)), y = (int)Math.Floor(top - t * len);
            g.Put(x, y, c); g.Put(x + 1, y, c);
        }
        g.Outline(Line);
        return g;
    }

    public static Px Flower()
    {
        var g = new Px(15, 11);
        uint p = C("#FF9EC4"), ph = C("#FFD3E4");
        for (int i = 0; i < 5; i++) { double a = -Math.PI / 2 + i * Math.PI * 2 / 5; g.Ell(7.5 + Math.Cos(a) * 3.4, 5.5 + Math.Sin(a) * 2.6, 2.6, 2.1, p); }
        g.Ell(6.5, 3.5, 1.2, 1, ph);
        g.Ell(7.5, 5.5, 1.7, 1.5, C(255, 220, 90));
        g.Outline(Line);
        return g;
    }

    public static Px FlowerBud()
    {
        var g = new Px(15, 11);
        uint p = C("#FF9EC4"), ph = C("#FFD3E4"), leaf = C("#6FBE4A");
        g.Ell(7.5, 6.2, 2.4, 3.1, p);
        g.Ell(6.8, 5.2, .9, 1.2, ph);
        g.Poly(new[] { (5.5, 8.6), (7.5, 5.8), (9.5, 8.6), (7.5, 9.6) }, leaf, true);
        g.Outline(Line);
        return g;
    }

    public static Px Cloud(int w, int h, int seed, uint fill, uint shade, uint edge)
    {
        var g = new Px(w + 4, h + 4);
        var rnd = new Random(seed);
        int n = Math.Max(3, w / 7);
        for (int i = 0; i < n; i++)
        {
            double x = 2 + w * (i + .5) / n + rnd.NextDouble() * 2 - 1, r = h * (.42 + rnd.NextDouble() * .25);
            g.Ell(x, 2 + h - r * .9, r * 1.05, r, fill);
        }
        g.Fill(2 + h / 3, 2 + h * 2 / 3, w - h * 2 / 3, h / 3, fill);
        for (int y = 0; y < g.H; y++) for (int x = 0; x < g.W; x++) if (g.On(x, y) && y > 2 + h * .72) g.Set(x, y, shade);
        if (edge >> 24 != 0) g.Outline(edge);
        return g;
    }

    // 작은 새 (친구가 없을 때): 날갯짓 두 장
    public static Px Bird(int frame)
    {
        var g = new Px(15, 11);
        uint body = C("#6BA3E8"), belly = C("#E8F2FF"), beak = C("#F2A83A");
        g.Ell(7, 6.4, 4.4, 3.2, body);
        g.Ell(6.4, 7.6, 3, 1.8, belly);
        g.Ell(10.6, 4.6, 2.5, 2.3, body);
        g.Put(13, 5, beak); g.Put(14, 5, beak);
        g.Put(11, 4, Line);
        g.Poly(frame == 0 ? new[] { (3.0, 6.0), (0.0, 7.5), (4.0, 7.5) } : new[] { (3.0, 6.0), (0.0, 4.0), (4.5, 7.0) }, body);
        g.Poly(frame == 0 ? new[] { (5.0, 4.0), (6.5, 1.0), (8.5, 4.2) } : new[] { (5.0, 4.0), (6.5, 6.0), (8.5, 4.2) }, C("#4F86CB"));
        g.Outline(Line);
        return g;
    }

    // 작은 잎 아이콘 (막대 대신 조각·결과 화면). state 0 싱싱 1 노랑 2 갈색 3 빈 칸
    static readonly string[] LeafIcon =
    {
        "......ooo",
        "....ooGGo",
        "...oGHHGo",
        "..oGHGGGo",
        ".oGGGGGo.",
        ".oGGGLo..",
        "oLooo....",
    };
    public static Px Icon(int state) => Glyph(LeafIcon, ch => ch switch
    {
        'o' => state == 3 ? C("#8C96A0") : Line,
        'G' => state switch { 0 => Fresh.Fill, 1 => Wilt.Fill, 2 => C("#B0794A"), _ => C("#E6EAEE") },
        'H' => state switch { 0 => Fresh.Hi, 1 => Wilt.Hi, 2 => C("#CF9A62"), _ => C("#F4F6F8") },
        'L' => state == 3 ? C("#8C96A0") : C("#4F9E3C"),
        _ => 0,
    });

    // ── 덩굴 ──
    // 마디 사이 굽은 선 한 토막 (모양 A2): 잎 j 의 마디(발밑 줄기 자리)에서 잎 j+1 의 마디까지. dx = 마디 사이 가로 (22·−8·8·−22), bow = 굽는 쪽 (홀짝 번갈아).
    // 테두리 겹과 속 겹을 따로 만들어 둔다 (이웃 토막과 이어지는 자리에서 테두리가 속을 가르지 않게: 테두리 전부 → 속 전부 순서로 찍는다)
    public sealed record Seg(Px Out, Px In, int Ox, int Oy);   // (Ox, Oy): 마디(0,0)에서 그림 왼쪽 위까지

    public static Seg StemSeg(int dx, int dy, double bowSign, double r = 1.6)
    {
        double len = Math.Sqrt(dx * dx + dy * dy), bow = bowSign * 3.2;
        double mx = dx / 2.0, my = dy / 2.0, cx = mx - dy / len * bow, cy = my + dx / len * bow;
        var pts = new List<(double X, double Y)>();
        for (double t = 0; t < 1; t += .02)
        {
            double u = 1 - t;
            pts.Add((2 * u * t * cx + t * t * dx, 2 * u * t * cy + t * t * dy));
        }
        int pad = (int)Math.Ceiling(r) + 2;
        int minX = (int)Math.Floor(pts.Min(p => p.X)) - pad, maxX = (int)Math.Ceiling(pts.Max(p => p.X)) + pad, minY = (int)Math.Floor(pts.Min(p => p.Y)) - pad, maxY = (int)Math.Ceiling(pts.Max(p => p.Y)) + pad;
        var o = new Px(maxX - minX + 1, maxY - minY + 1);
        var f = new Px(o.W, o.H);
        foreach (var (x, y) in pts) o.Ell(x - minX, y - minY, r + 1, r + 1, Line);
        foreach (var (x, y) in pts) f.Ell(x - minX, y - minY, r, r, StemFill);
        foreach (var (x, y) in pts) f.Ell(x - minX - .7, y - minY - .4, .55, .55, StemHi);
        return new Seg(o, f, minX, minY);
    }

    // 모양 A4: 곧은 줄기 한 토막 (세로 32칸이 한 바퀴, 위아래로 이어 붙인다). 가운데 x = Seg.Ox 의 반대
    public const int StripH = 32;
    public static Seg StemStrip()
    {
        double r = 2.1;
        const int w = 12;
        var o = new Px(w, StripH);
        var f = new Px(w, StripH);
        double mid = w / 2.0;                  // 가운데는 칸 경계 (시안의 줄기 자리와 같다)
        var pts = new List<(double X, double Y)>();
        for (int y = -6; y < StripH + 6; y++) pts.Add((mid + Math.Sin(y * Math.PI * 2 / StripH) * .8, y));
        foreach (var (x, y) in pts) o.Ell(x, y, r + 1, r + 1, Line);
        foreach (var (x, y) in pts) f.Ell(x, y, r, r, StemFill);
        foreach (var (x, y) in pts) f.Ell(x - .7, y - .4, .55, .55, StemHi);
        return new Seg(o, f, -w / 2, 0);
    }

    // 잎자루 (모양 A4: 줄기에서 잎 밑까지)
    public static Px Petiole()
    {
        var g = new Px(8, 6);                  // 가운데 (4, 3) 이 줄기에서 3칸 옆, 잎 윗면에서 5칸 아래
        g.Ell(4, 3, 2.6, 1.5, Line);
        g.Ell(4, 3, 1.6, .7, StemFill);
        return g;
    }
}
