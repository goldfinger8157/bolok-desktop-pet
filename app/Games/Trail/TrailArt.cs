using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Bolok;

// 눈길 마중 그림 (docs/ice-run.md 5장, 시안 A1·B1·C·D): 칸 그림판(TPx)에 도트로 그려 크기 단계마다 미리 만든다.
// 시안 코드(run/mockups/ice-run/trail-mock.patch)의 그리기 함수를 옮겨 왔다. 글자는 없다
static class TrailArt
{
    internal static uint C(int r, int g, int b) => 0xFF000000u | (uint)(r << 16 | g << 8 | b);
    internal static uint C(string hex) => 0xFF000000u | Convert.ToUInt32(hex.TrimStart('#'), 16);
    internal static uint Mix(uint a, uint b, double t)
    {
        int M(int sh) { int x = (int)(a >> sh & 255), y = (int)(b >> sh & 255); return (int)Math.Round(x + (y - x) * t); }
        return C(M(16), M(8), M(0));
    }
    internal static readonly uint Line = C(92, 58, 40), White = C(255, 255, 255), Cream = C(255, 243, 224);
    internal static readonly uint F0 = C(255, 236, 120), F1 = C(255, 170, 50), F2 = C(232, 80, 40), FEdge = C(196, 60, 36);
    internal static readonly uint Snow = C("#F6F9FC"), SnowShade = C("#C6D2E0"), Wood = C("#9A6238"), WoodHi = C("#C88A50"), Stone = C("#A4A9B4"), StoneHi = C("#C9CDD5");
    internal static readonly uint Red = C(232, 72, 92), Gold = C(250, 185, 40);


    // 길 모습 A1 (하늘 투명 · 낮은 산 띠 · 차분한 푸른 회색 눈길)
    public static class A1
    {
        public static readonly uint Field = C("#DDE3EA"), Field2 = C("#D3DAE3"), Road = C("#BAC6D3"), Road2 = C("#A9B7C7"), Edge = C("#8F9DAF"),
            Bank = C("#F3F6F9"), BankShade = C("#C2CCD8"), Dash = C("#CDD6E0"), Mount = C("#9AA5B5"), MountHi = C("#D5DBE4"), Shadow = C("#8994A6"),
            Mark = C("#C9D1DB"), Glow = C("#E9D7C4"), Water = C("#3D6683"), WaterDeep = C("#2B4E68"), Rim = C("#EDF2F7");
    }

    internal sealed class TPx
    {
        public readonly int W, H;
        public readonly uint[] P;
        public TPx(int w, int h) { W = w; H = h; P = new uint[w * h]; }
        public uint Get(int x, int y) => x >= 0 && y >= 0 && x < W && y < H ? P[y * W + x] : 0;
        public void Put(int x, int y, uint c, bool clip = false)
        {
            if (c == 0 || x < 0 || y < 0 || x >= W || y >= H) return;
            if (clip && P[y * W + x] == 0) return;
            P[y * W + x] = c;
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
        public void Outline(uint c)
        {
            var edge = new List<int>();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (P[y * W + x] != 0) continue;
                    if (Get(x + 1, y) != 0 || Get(x - 1, y) != 0 || Get(x, y + 1) != 0 || Get(x, y - 1) != 0) edge.Add(y * W + x);
                }
            foreach (var i in edge) P[i] = c;
        }
        // src 의 (0,0) 이 (x, y) 에
        public void Blit(TPx s, int x, int y, bool flip = false)
        {
            for (int j = 0; j < s.H; j++)
                for (int i = 0; i < s.W; i++)
                {
                    uint c = s.P[j * s.W + (flip ? s.W - 1 - i : i)];
                    if (c != 0) Put(x + i, y + j, c);
                }
        }
        // 가장 가까운 점으로 줄여서: 아래 가운데가 (bx, by)
        public void BlitScaled(TPx s, double k, double bx, int by)
        {
            int w = Math.Max(1, (int)Math.Round(s.W * k)), h = Math.Max(1, (int)Math.Round(s.H * k));
            int x0 = (int)Math.Round(bx - w / 2.0), y0 = by - h;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    uint c = s.P[Math.Min(s.H - 1, (int)(j / k)) * s.W + Math.Min(s.W - 1, (int)(i / k))];
                    if (c != 0) Put(x0 + i, y0 + j, c);
                }
        }
        // 칸 하나를 k 점으로 키운 그림 (미리 곱한 알파)
        public Bitmap ToBmp(int k)
        {
            var bmp = new Bitmap(W * k, H * k, PixelFormat.Format32bppPArgb);
            var row = new int[W * k];
            var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++) Array.Fill(row, unchecked((int)P[y * W + x]), x * k, k);
                for (int j = 0; j < k; j++) Marshal.Copy(row, 0, d.Scan0 + (y * k + j) * d.Stride, row.Length);
            }
            bmp.UnlockBits(d);
            return bmp;
        }
        public static TPx From(Bitmap b)
        {
            var r = new TPx(b.Width, b.Height);
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var row = new int[b.Width];
            for (int y = 0; y < b.Height; y++)
            {
                Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, row.Length);
                for (int x = 0; x < row.Length; x++) r.P[y * b.Width + x] = (uint)row[x] >> 24 == 0 ? 0 : (uint)row[x] | 0xFF000000u;
            }
            b.UnlockBits(d);
            return r;
        }
        public TPx Crop(int x, int y, int w, int h)
        {
            var r = new TPx(w, h);
            for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) r.P[j * w + i] = Get(x + i, y + j);
            return r;
        }
    }

    internal static TPx Glyph(string[] rows, Func<char, uint> pal)
    {
        int w = rows.Max(r => r.Length);
        var g = new TPx(w, rows.Length);
        for (int y = 0; y < rows.Length; y++) for (int x = 0; x < rows[y].Length; x++) if (rows[y][x] != '.') g.Put(x, y, pal(rows[y][x]));
        return g;
    }
    internal static uint GP(char ch) => ch switch
    {
        'o' => Line, 'w' => White, 'c' => Cream, 'r' => F2, 'n' => F1, 'y' => F0, 'b' => Wood, 't' => WoodHi, 'g' => C("#DCEFF8"), 's' => Gold,
        'd' => C("#5A5560"), 'k' => C("#3E3A44"), 'R' => Red, 'h' => C("#E0A060"), 'a' => C("#C87F3F"), 'A' => C("#E8B07A"), 'K' => C("#8A5428"),
        'G' => C("#78BA64"), 'L' => C("#4E8A44"), 'p' => C("#FFC2CC"), 'e' => C("#B4BAC6"), _ => C("#FF00FF"),
    };

    internal static void Shadow(TPx w, double x, int y, double rx, double ry)
    {
        for (int j = (int)(y - ry); j <= y + ry; j++)
            for (int i = (int)(x - rx); i <= x + rx; i++)
            {
                double a = (i + .5 - x) / rx, b = (j + .5 - y) / ry;
                if (a * a + b * b < 1 && (i + j) % 2 == 0) w.Put(i, j, A1.Shadow);
            }
    }

    internal static TPx Dot(bool pick, bool flat = false, bool gold = false)
    {
        if (pick) return Glyph(new[] { ".o.", "oxo", ".o." }, ch => ch == 'x' ? (gold ? Gold : Red) : (gold ? C("#B07818") : C("#8A2A3A")));
        if (flat) return Glyph(new[] { "oxxo" }, ch => ch == 'x' ? A1.Water : Mix(A1.Water, Line, .3));
        return Glyph(new[] { ".oo.", "oxxo", "oooo" }, ch => ch == 'x' ? Snow : Line);
    }

    internal static TPx Shaded(int w, int h, Action<TPx, uint, bool, double> shapes, uint shade, uint light)
    {
        var g = new TPx(w + 2, h + 2);
        shapes(g, shade, false, 0);
        shapes(g, light, true, .9);
        g.Outline(Line);
        return g;
    }

    internal static TPx SnowPile(double s)
    {
        int W0 = (int)Math.Ceiling(26 * s) + 2, H0 = (int)Math.Ceiling(11 * s) + 2;
        var g = Shaded(W0, H0, (p, c, clip, k) =>
        {
            double cx = W0 / 2.0 + 1, by = H0 + .4;
            p.Ell(cx - k * .7, by - k * .2, 12.5 * s - k, 7.5 * s - k * .6, c, clip);
            p.Ell(cx - 4.5 * s - k * .7, by - 3 * s - k * .3, 6.5 * s - k, 6.5 * s - k * .6, c, clip);
            p.Ell(cx + 4 * s - k * .7, by - 2.5 * s - k * .3, 5.5 * s - k, 5.5 * s - k * .6, c, clip);
        }, SnowShade, Snow);
        // 바닥 줄 자르기 (판판한 밑)
        for (int x = 0; x < g.W; x++) { g.P[(g.H - 1) * g.W + x] = 0; if (g.P[(g.H - 2) * g.W + x] != 0) g.P[(g.H - 2) * g.W + x] = Line; }
        if (s > .45) { g.Put((int)(g.W * .3), (int)(g.H * .35), White); g.Put((int)(g.W * .3) + 1, (int)(g.H * .35), White); }
        return g;
    }

    internal static TPx Hole(double s)
    {
        double rx = 14 * s, ry = Math.Max(1.6, 4.2 * s);
        int W0 = (int)Math.Ceiling(rx * 2) + 4, H0 = (int)Math.Ceiling(ry * 2) + 4;
        var g = new TPx(W0, H0);
        double cx = W0 / 2.0, cy = H0 / 2.0;
        g.Ell(cx, cy, rx, ry, A1.Rim);
        g.Ell(cx, cy + .3, rx - Math.Max(1, 1.6 * s), ry - Math.Max(.8, 1.1 * s), A1.Water, true);
        g.Ell(cx, cy - ry * .25, rx - Math.Max(1.2, 2.2 * s), ry * .45, A1.WaterDeep, true);
        if (s > .4) { g.Put((int)(cx + rx * .3), (int)(cy + ry * .35), Mix(A1.Water, White, .45)); g.Put((int)(cx + rx * .3) + 1, (int)(cy + ry * .35), Mix(A1.Water, White, .45)); g.Put((int)(cx - rx * .4), (int)(cy + ry * .2), Mix(A1.Water, White, .3)); }
        g.Outline(Mix(A1.Water, Line, .5));
        return g;
    }

    internal static TPx Snowman(double s)
    {
        int W0 = (int)Math.Ceiling(22 * s) + 4, H0 = (int)Math.Ceiling(34 * s) + 3;
        double cx = W0 / 2.0;
        var g = Shaded(W0, H0, (p, c, clip, k) =>
        {
            p.Ell(cx + 1 - k * .6, H0 - 7.5 * s - k * .4, 8.5 * s - k, 7.6 * s - k * .7, c, clip);
            p.Ell(cx + 1 - k * .6, H0 - 19 * s - k * .4, 6.2 * s - k, 5.6 * s - k * .7, c, clip);
            p.Ell(cx + 1 - k * .6, H0 - 28.5 * s - k * .4, 4.6 * s - k, 4.4 * s - k * .7, c, clip);
        }, SnowShade, Snow);
        double hx = cx + 1, hy = H0 - 28.5 * s + 1;
        // 목도리 (빨강) + 얼굴
        g.Ell(hx, H0 - 24 * s + 1, 5.2 * s, Math.Max(.8, 1.2 * s), Red);
        if (s > .5) g.Ell(hx + 3 * s, H0 - 21.5 * s + 1, 1.2 * s, 2.2 * s, Red);
        int ex = (int)Math.Round(1.6 * s);
        g.Put((int)(hx - .5) - ex, (int)(hy - .8 * s), Line); g.Put((int)(hx - .5) + ex + (s > .6 ? 1 : 0), (int)(hy - .8 * s), Line);
        g.Put((int)(hx - .5) + (s > .6 ? 1 : 0), (int)(hy + .6 * s), F1);
        if (s > .6) g.Put((int)(hx - .5) + 2, (int)(hy + .6 * s), F1);
        if (s > .5) // 단추
            for (int i = 0; i < 2; i++) g.Put((int)(hx - .5), (int)(H0 - (20.5 - i * 3) * s + 1), Line);
        if (s > .4) // 나뭇가지 팔
        {
            for (int i = 0; i < (int)(5 * s) + 1; i++) { g.Put((int)(hx - 6 * s) - i, (int)(H0 - 19 * s) - i / 2, Wood); g.Put((int)(hx + 6 * s) + i, (int)(H0 - 19 * s) - i / 2, Wood); }
        }
        return g;
    }

    // 무너진 눈사람: 눈 더미 + 옆으로 굴러간 머리 + 목도리·당근
    internal static TPx Fallen()
    {
        var pile = SnowPile(.85);
        var g = new TPx(pile.W + 12, pile.H + 2);
        g.Blit(pile, 0, 2);
        var head = Shaded(9, 9, (p, c, clip, k) => p.Ell(5.5 - k * .6, 5.5 - k * .5, 4.4 - k * .9, 4.2 - k * .9, c, clip), SnowShade, Snow);
        g.Blit(head, pile.W - 2, g.H - head.H);
        g.Put(pile.W + 3, g.H - 6, Line); g.Put(pile.W + 6, g.H - 6, Line);
        g.Put(pile.W + 7, g.H - 4, F1); g.Put(pile.W + 8, g.H - 4, F1);
        for (int i = 0; i < 7; i++) g.Put(4 + i, g.H - 4 + (i % 3 == 0 ? 1 : 0), Red);
        for (int i = 0; i < 4; i++) g.Put(pile.W / 2 + i * 2 - 2, 3 + i % 2, Wood);
        return g;
    }

    internal static void WindStreaksIn(TPx w)
    {
        for (int n = 0; n < 6; n++)
        {
            int x = 2 + n * 3 % 14, y = 2 + n * 3, len = 6 + n % 3 * 2;
            for (int i = 0; i < len; i++) w.Put(x + i, y + i / 3, i < 2 ? Line : i < 4 ? White : C("#DDE6EF"));
        }
    }

    internal static TPx Snowball(double s, int frame)
    {
        double r = 8 * s;
        int W0 = (int)Math.Ceiling(r * 2) + 4;
        double c = W0 / 2.0;
        var g = Shaded(W0, W0, (p, col, clip, k) => p.Ell(c - k * .6, c - k * .5, r - k * .9, r - k * .9, col, clip), SnowShade, Snow);
        if (s > .35) // 굴러가는 결
            for (int i = 0; i < 3; i++)
            {
                double a = frame * .9 + i * 2.1;
                g.Put((int)(c + Math.Cos(a) * r * .5), (int)(c + Math.Sin(a) * r * .45), SnowShade);
                g.Put((int)(c + Math.Cos(a) * r * .5) + 1, (int)(c + Math.Sin(a) * r * .45), SnowShade);
            }
        return g;
    }

    internal static TPx IcePatch(double s)
    {
        double rx = 21 * s, ry = Math.Max(1.5, 4.6 * s);
        int W0 = (int)Math.Ceiling(rx * 2) + 4, H0 = (int)Math.Ceiling(ry * 2) + 4;
        var g = new TPx(W0, H0);
        double cx = W0 / 2.0, cy = H0 / 2.0;
        uint ice = C("#7FAED6"), ice2 = C("#93BEE0");
        g.Ell(cx, cy, rx, ry, ice);
        g.Ell(cx - rx * .15, cy - ry * .2, rx * .7, ry * .55, ice2, true);
        for (int k = 0; k < 3; k++) // 반들 빛줄
        {
            int x0 = (int)(cx - rx * .5 + k * rx * .4), len = Math.Max(1, (int)(4 * s));
            for (int i = 0; i < len; i++) g.Put(x0 + i, (int)(cy + ry * .3) - i / 2, White, true);
        }
        g.Outline(C("#557EA8"));
        return g;
    }

    internal static TPx Berry(double s)
    {
        double k = Math.Max(.45, s);
        int W0 = (int)Math.Ceiling(12 * k) + 2, H0 = (int)Math.Ceiling(10 * k) + 2;
        var g = new TPx(W0, H0);
        double cy = H0 - 3.4 * k - 1;
        g.Ell(W0 / 2.0 - 2.4 * k, cy, 2.8 * k, 2.8 * k, Red);
        g.Ell(W0 / 2.0 + 2.4 * k, cy + .4, 2.8 * k, 2.8 * k, Red);
        g.Ell(W0 / 2.0 - .2, cy - 3.6 * k, 2.4 * k, 1.1 * k + .3, C("#78BA64"));
        g.Outline(Line);
        g.Put((int)(W0 / 2.0 - 3.4 * k), (int)(cy - 1.2 * k), White);
        g.Put((int)(W0 / 2.0 + 1.4 * k), (int)(cy - .8 * k), White);
        return g;
    }

    static readonly string[] GlassRows =
    {
        "ooooooooo",
        "otttttttO",
        "oooooooOo",
        ".ogggggo.",
        ".osssggo.",
        "..ossso..",
        "...oso...",
        "..ogsgo..",
        ".oggsggo.",
        ".osssssg.",
        "oooooooOo",
        "otttttttO",
        "ooooooooo",
    };
    internal static TPx Hourglass(double s)
    {
        var g = Glyph(GlassRows.Select(r => r.Replace('O', 'o')).ToArray(), ch => ch == 'g' ? C("#D8F0FA") : GP(ch));
        double k = Math.Max(.45, s) * 1.15;
        var r = new TPx((int)Math.Ceiling(g.W * k) + 1, (int)Math.Ceiling(g.H * k) + 1);
        r.BlitScaled(g, k, r.W / 2.0, r.H);
        return r;
    }

    internal static TPx StarArt(double s)
    {
        double k = Math.Max(.45, s), R = 7 * k, r = 3.2 * k;
        int W0 = (int)Math.Ceiling(R * 2) + 4;
        var g = new TPx(W0, W0);
        double c = W0 / 2.0;
        var pts = new (double, double)[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? R : r;
            pts[i] = (c + Math.Cos(a) * rr, c + .6 + Math.Sin(a) * rr);
        }
        g.Poly(pts, C("#F0A020"));
        g.Poly(pts.Select(p => (c + (p.Item1 - c) * .8 - .3, c + (p.Item2 - c) * .8 - .5)).ToArray(), C("#FFD84A"), true);
        g.Outline(Line);
        g.Put((int)(c - 1.5 * k), (int)(c - 1.2 * k), White);
        if (k > .7) { g.Put((int)(c - 1.5 * k) + 1, (int)(c - 1.2 * k), White); g.Put((int)(c - 1.5 * k), (int)(c - 1.2 * k) + 1, C("#FFF3B0")); }
        return g;
    }

    // 모닥불: 둘레 돌, 엇갈린 장작, 세 겹 불꽃 (frame 으로 깜빡)
    internal static TPx Campfire(double s, int frame)
    {
        double k = Math.Max(.3, s);
        int W0 = (int)Math.Ceiling(22 * k) + 4, H0 = (int)Math.Ceiling(24 * k) + 3;
        var g = new TPx(W0, H0);
        double cx = W0 / 2.0, by = H0 - 1.5;
        // 장작
        g.Poly(new[] { (cx - 8 * k, by - 1 * k), (cx - 7 * k, by - 3.6 * k), (cx + 8 * k, by - 6 * k), (cx + 7.4 * k, by - 3.2 * k) }, Wood);
        g.Poly(new[] { (cx + 8 * k, by - 1 * k), (cx + 7 * k, by - 3.6 * k), (cx - 8 * k, by - 6 * k), (cx - 7.4 * k, by - 3.2 * k) }, WoodHi);
        // 불꽃
        double sway = frame % 2 == 0 ? 1 : -1;
        var flame = new TPx(W0, H0);
        void Fl(double rx, double ry, double tip, uint c)
        {
            flame.Ell(cx, by - 6 * k - ry * .55, rx, ry * .65, c);
            flame.Poly(new[] { (cx - rx * .92, by - 6 * k - ry * .7), (cx + rx * .92, by - 6 * k - ry * .7), (cx + sway * 1.6 * k, by - 6 * k - ry * .7 - tip) }, c);
        }
        Fl(6.2 * k, 9 * k, 10 * k, F2);
        Fl(4.4 * k, 7 * k, 7 * k, F1);
        Fl(2.4 * k, 4.6 * k, 3.6 * k, F0);
        flame.Outline(FEdge);
        // 작은 불티
        if (k > .5) { flame.Put((int)(cx + 4 * k * sway), (int)(by - 22 * k), F1); flame.Put((int)(cx - 3 * k * sway), (int)(by - 20 * k) + frame % 2, F0); }
        g.Outline(Line);
        g.Blit(flame, 0, 0);
        // 돌 (앞쪽)
        var st = new TPx(W0, H0);
        for (int i = 0; i < 5; i++)
        {
            double a = Math.PI * (.08 + i * .21);
            st.Ell(cx - Math.Cos(a) * 9.5 * k, by - 1.2 * k + Math.Sin(a) * 1.4 * k, 2.4 * k + .3, 1.7 * k + .3, Stone);
        }
        for (int i = 0; i < 5; i++) { double a = Math.PI * (.08 + i * .21); st.Put((int)(cx - Math.Cos(a) * 9.5 * k - .8 * k), (int)(by - 2 * k + Math.Sin(a) * 1.4 * k), StoneHi); }
        st.Outline(Line);
        g.Blit(st, 0, 0);
        return g;
    }

    internal static TPx FarFire(double s)
    {
        if (s > .3) return Campfire(s, 0);
        return Glyph(new[] { "..r..", ".rnr.", ".nyn.", "obbbo", }, GP);
    }

    // 등불 기둥: 나무 기둥 + 따뜻한 등
    internal static TPx Post(double s, bool night)
    {
        double k = Math.Max(.25, s);
        int H0 = (int)Math.Ceiling(30 * k) + 3, W0 = (int)Math.Ceiling(9 * k) + 4;
        var g = new TPx(W0, H0);
        double cx = W0 / 2.0;
        int pw = Math.Max(1, (int)Math.Round(2.2 * k));
        g.Fill((int)Math.Round(cx - pw / 2.0), (int)(8 * k) + 1, pw, H0 - (int)(8 * k) - 2, Wood);
        g.Fill((int)Math.Round(cx - 3.2 * k), (int)(1.5 * k) + 1, Math.Max(2, (int)Math.Round(6.4 * k)), Math.Max(2, (int)Math.Round(6.5 * k)), night ? C("#FFE9A0") : C("#FFD873"));
        g.Fill((int)Math.Round(cx - 4 * k), 1, Math.Max(3, (int)Math.Round(8 * k)), Math.Max(1, (int)Math.Round(1.4 * k)), Red);
        g.Outline(Line);
        if (k > .5) g.Put((int)Math.Round(cx - 1.5 * k), (int)(3.5 * k) + 1, White);
        return g;
    }

    public static readonly string[] FireIcon = { "...r...", "..rnr..", ".rnynr.", ".rnyyr.", "..nyn..", "obbtbbo", ".oo.oo." };
    public static readonly string[] FireOff = { "...o...", "..odo..", ".odeedo", ".odeedo", "..odo..", "okkKkko", ".oo.oo." };
    public static readonly string[] FlagRows = { "oo........", "oboooooo..", "obhhhhhho.", "obhaaaaaao", "obaaaaaao.", "obKKKKKKo.", "oboooooo..", "ob........", "ob........", "oo........" };
    public static readonly string[] ArrowRows = { "...o...", "..oGo..", ".oGGGo.", "oGGGGGo", "ooGGGoo", "..oGo..", "..oGo..", "..ooo.." };
    public static readonly string[] ClockIcon = { "ooooooo", "ottttto", "ooooooo", ".ogggo.", "..oso..", "..oso..", ".ogsgo.", ".ossso.", "ooooooo", "ottttto", "ooooooo" };
}
