using System.Drawing.Imaging;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Bolok;

// 버그 떼 타격감 그림 (docs/swarm-feel.md 4장, 시안 run/mockups/swarm-feel/ 의 추천 = 보통·말벌은 1 팡, 딱정벌레·강철·엘리트·보스는 3 톡).
// 벌레 그림(진짜 game/bug·wasp·beetle·bugboss, 엘리트·강철 포함)을 「도트 한 칸」 단위로 읽어 흰 판·찌그러짐·고리·별·껍질 조각을 미리 만들어 둔다 (판을 열 때 한 번, 배율마다 한 번).
// 판은 색 열쇠 창이라 알파가 없다: 모두 알파 0·255 두 가지로만 그린다. 흰 판의 속은 아주 옅은 파랑 기운이 도는 흰색 (밝은 창 위에서도 모양이 보이게)
sealed class FeelArt
{
    // 줄 열: 벌레 그림 줄 (SwarmGame.FoeArt 가 고르는 줄과 같은 차례)
    public enum Row { Bug, Wasp, Beetle, Boss, Queen, EliteBug, EliteWasp, EliteBeetle, Steel, EliteSteel }
    public const int Rows = 10;

    public sealed class Piece   // 껍질 조각 하나: 날아갈 때(보통·좁아짐·뒤집힘)와 사라질 때(바둑판·성긴 점) 그림
    {
        public Stamp Norm = null!, Narrow = null!, Flip = null!, NormChk = null!, NormSp = null!, FlipChk = null!, FlipSp = null!;
        public double Ox, Oy;   // 몸 가운데에서 조각 가운데까지 (칩 칸)
        public double Ang;      // 흩어지는 쪽 (몸 가운데에서 조각이 있는 쪽)
        public bool Flips;      // 이 조각은 한 번 뒤집힌다
        public int W, H;        // 조각 크기 (그림 칸)
    }

    public sealed class RowArt
    {
        public Stamp[] Flash = null!;                  // 줄의 그림마다 흰 판 (테두리만 남기고 속을 옅은 흰색으로)
        public Stamp Squash = null!, SquashBig = null!; // 찌그러짐 (가로 120%·세로 70%, 연쇄의 마지막은 한 칸 크게)
        public Stamp[] Ring = new Stamp[4];            // 팡: [큼 × 2 + 단계] 단계 0 = 흰 고리, 1 = 점선 고리
        public Stamp[] Star = new Stamp[2];            // 톡: 흰 별 (작은 것·큰 것)
        public Piece[][] Shells = Array.Empty<Piece[]>();   // 톡: 껍질 조각 묶음 (변형마다)
        public Brush[] Main = null!, Light = null!;    // 칩 색: 0·1 몸 색 둘, 2 흰, 3·4 금, 5·6 흙
        public double Half;                            // 몸 반지름 (칩 칸)
        public double Spawn;                           // 찌그러진 채 있는 시간 (보스는 길다)
        public bool Tok;                               // 껍질 조각으로 터지는 놈 (딱정벌레·강철·엘리트·보스)
        public double Dot;                             // 이 줄 그림 한 칸이 칩 한 칸의 몇 배인가 (여왕만 한 배율 크다)
    }

    public readonly RowArt[] Arts = new RowArt[Rows];
    public readonly int Small;                         // 칩 한 칸 = 화면 px

    // ── 색 ──
    public static readonly Color FlashFill = Color.FromArgb(0xEA, 0xEE, 0xFB);   // 흰 판의 속 (아주 옅은 파랑 기운)
    static readonly uint FillU = (uint)FlashFill.ToArgb();
    static readonly Color Gold = Color.FromArgb(0xFA, 0xB9, 0x28), GoldLt = Color.FromArgb(0xFF, 0xE2, 0x7A), GoldDk = Color.FromArgb(0xE0, 0x8A, 0x1E);
    static readonly Color Dirt = Color.FromArgb(0x8A, 0x5C, 0x32), DirtLt = Color.FromArgb(0xB4, 0x86, 0x52);
    static Color Mix(Color a, Color b, double t) => Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
    static Color Lighter(Color c, double t) => Mix(c, Color.White, t);

    // ── 도트 그림판: 한 칸 = 한 도트, 알파 0 = 빈 칸. 왼쪽 위가 몸 가운데에서 (Ox, Oy) 칸 ──
    public sealed class Dots
    {
        public int W, H, Ox, Oy;
        public uint[] P;
        public Dots(int w, int h, int ox, int oy) { W = w; H = h; Ox = ox; Oy = oy; P = new uint[Math.Max(1, w * h)]; }
        public bool On(int x, int y) => (uint)x < (uint)W && (uint)y < (uint)H && P[y * W + x] >> 24 != 0;
        public void Set(int x, int y, uint c) { if ((uint)x < (uint)W && (uint)y < (uint)H) P[y * W + x] = c; }
        public bool Edge(int x, int y) => On(x, y) && (!On(x - 1, y) || !On(x + 1, y) || !On(x, y - 1) || !On(x, y + 1));
    }

    // Stamp 를 도트로: 그림 한 칸이 dotPx 픽셀 (알파가 반 넘으면 켠 칸)
    static Dots FromStamp(Stamp st, int dotPx)
    {
        var b = st.Bmp;
        int w = (b.Width + dotPx - 1) / dotPx, h = (b.Height + dotPx - 1) / dotPx;
        var raw = new int[b.Width * b.Height];
        var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < b.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, raw, y * b.Width, b.Width); }
        finally { b.UnlockBits(data); }
        var d = new Dots(w, h, (int)Math.Round(st.Dx / (double)dotPx), (int)Math.Round(st.Dy / (double)dotPx));
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int px = Math.Min(b.Width - 1, x * dotPx + dotPx / 2), py = Math.Min(b.Height - 1, y * dotPx + dotPx / 2);
                uint v = (uint)raw[py * b.Width + px];
                if (v >> 24 >= 128) d.P[y * w + x] = v | 0xFF000000u;
            }
        return d;
    }

    // 도트를 Stamp 로 (가운데 = 몸 가운데)
    static Stamp ToStamp(Dots d, int dotPx)
    {
        int ext = Math.Max(Math.Max(Math.Abs(d.Ox), Math.Abs(d.Ox + d.W)), Math.Max(Math.Abs(d.Oy), Math.Abs(d.Oy + d.H))) + 1, size = ext * 2 * dotPx;
        var buf = new int[size * size];
        for (int y = 0; y < d.H; y++)
            for (int x = 0; x < d.W; x++)
            {
                uint c = d.P[y * d.W + x];
                if (c >> 24 == 0) continue;
                int x0 = (ext + d.Ox + x) * dotPx, y0 = (ext + d.Oy + y) * dotPx;
                for (int j = 0; j < dotPx; j++)
                    for (int i = 0; i < dotPx; i++) buf[(y0 + j) * size + x0 + i] = (int)c;
            }
        using var cell = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var cd = cell.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < size; y++) Marshal.Copy(buf, y * size, cd.Scan0 + y * cd.Stride, size); }
        finally { cell.UnlockBits(cd); }
        return new Stamp(cell);
    }

    // 테두리 색: 테두리 칸에서 가장 많은 색
    static uint EdgeColor(Dots d)
    {
        var cnt = new Dictionary<uint, int>();
        for (int y = 0; y < d.H; y++)
            for (int x = 0; x < d.W; x++)
                if (d.Edge(x, y)) cnt[d.P[y * d.W + x]] = cnt.GetValueOrDefault(d.P[y * d.W + x]) + 1;
        uint best = 0xFF000000;
        int top = -1;
        foreach (var kv in cnt) if (kv.Value > top) (best, top) = (kv.Key, kv.Value);
        return best;
    }

    // 몸 색 셋 (많은 순, 테두리·거의 흰색 빼고)
    static List<uint> BodyColors(Dots d, uint edge)
    {
        var cnt = new Dictionary<uint, int>();
        foreach (uint c in d.P)
            if (c >> 24 != 0 && c != edge && Math.Min(Math.Min((int)(c >> 16 & 255), (int)(c >> 8 & 255)), (int)(c & 255)) < 205) cnt[c] = cnt.GetValueOrDefault(c) + 1;
        var top = cnt.OrderByDescending(k => k.Value).ThenBy(k => k.Key).Select(k => k.Key).Take(3).ToList();
        if (top.Count == 0) top.Add(edge);
        while (top.Count < 3) top.Add(top.Count == 1 ? edge : top[^1]);
        return top;
    }

    static Dots FlashOf(Dots d, uint edge)
    {
        var o = new Dots(d.W, d.H, d.Ox, d.Oy);
        for (int i = 0; i < d.P.Length; i++) o.P[i] = d.P[i] >> 24 == 0 ? 0u : d.P[i] == edge ? d.P[i] : FillU;
        return o;
    }

    // 찌그러짐: 가로 wk·세로 hk 배, 바닥은 그대로. 줄어든 모양에서 테두리를 다시 뽑아 속이 비어 보이지 않게
    static Dots SquashOf(Dots d, uint edge, double wk, double hk)
    {
        int nw = (int)Math.Round(d.W * wk), nh = (int)Math.Max(1, Math.Round(d.H * hk));
        var o = new Dots(nw, nh, d.Ox + d.W / 2 - nw / 2, d.Oy + d.H - nh);
        bool M(int x, int y) => x >= 0 && y >= 0 && x < nw && y < nh && d.P[Math.Min(d.H - 1, y * d.H / nh) * d.W + Math.Min(d.W - 1, x * d.W / nw)] >> 24 != 0;
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
                if (M(x, y)) o.P[y * nw + x] = !M(x - 1, y) || !M(x + 1, y) || !M(x, y - 1) || !M(x, y + 1) ? edge : FillU;
        return o;
    }

    // 고리 (칩 칸 기준: 가운데가 몸 가운데). dotted 면 한 칸 걸러
    static Dots RingOf(double r, double thick, bool dotted, uint fill, uint halo)
    {
        int n = (int)Math.Ceiling(r + thick + 2);
        var d = new Dots(2 * n + 1, 2 * n + 1, -n, -n);
        for (int dy = -n; dy <= n; dy++)
            for (int dx = -n; dx <= n; dx++)
            {
                double dist = Math.Sqrt(dx * dx + dy * dy), q = Math.Abs(dist - r);
                if (dotted && ((dx + dy) & 1) != 0) continue;
                if (q <= thick / 2) d.Set(dx + n, dy + n, fill);
                else if (q <= thick / 2 + 1.05 && halo != 0) d.Set(dx + n, dy + n, halo);
            }
        return d;
    }

    // 흰 별: 속은 흰색, 바깥 한 칸은 몸 테두리 색
    static Dots StarOf(double r, uint fill, uint outline)
    {
        int n = (int)r + 2;
        var d = new Dots(2 * n + 1, 2 * n + 1, -n, -n);
        bool In(int dx, int dy) => Math.Pow(Math.Abs(dx), .62) + Math.Pow(Math.Abs(dy), .62) <= Math.Pow(r, .62);
        for (int dy = -n; dy <= n; dy++)
            for (int dx = -n; dx <= n; dx++)
            {
                if (In(dx, dy)) d.Set(dx + n, dy + n, fill);
                else if (In(dx + 1, dy) || In(dx - 1, dy) || In(dx, dy + 1) || In(dx, dy - 1)) d.Set(dx + n, dy + n, outline);
            }
        return d;
    }

    static uint U(Color c) => (uint)c.ToArgb();

    // ── 껍질 조각: 벌레 그림을 씨앗 몇 개로 나눈 칸 묶음 (가장 가까운 씨앗의 조각). 한 번에 여러 변형을 만들어 둔다 ──
    static Piece[] Shell(Dots s, int n, bool boss, int seed, int dotPx, int smallPx)
    {
        var rnd = new Random(seed);
        var seeds = new List<(double X, double Y)>();
        double ph = rnd.NextDouble() * 6.28;
        for (int i = 0; i < n; i++)
        {
            double a = ph + (i + rnd.NextDouble() * .4) / n * Math.PI * 2, r = (.32 + .28 * rnd.NextDouble()) * Math.Min(s.W, s.H) / 2 * (boss ? 1.15 : 1);
            seeds.Add((s.W / 2.0 + Math.Cos(a) * r, s.H / 2.0 + Math.Sin(a) * r * .9));
        }
        if (n >= 6) seeds.Add((s.W / 2.0 + rnd.NextDouble() - .5, s.H / 2.0 + rnd.NextDouble() - .5));
        var cell = new int[s.W * s.H];
        for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                int best = 0;
                double bd = 1e9;
                for (int i = 0; i < seeds.Count; i++) { double d = (x + .5 - seeds[i].X) * (x + .5 - seeds[i].X) + (y + .5 - seeds[i].Y) * (y + .5 - seeds[i].Y); if (d < bd) (bd, best) = (d, i); }
                cell[y * s.W + x] = best;
            }
        var list = new List<Piece>();
        double scale = dotPx / (double)smallPx;   // 그림 한 칸 → 칩 칸
        for (int i = 0; i < seeds.Count; i++)
        {
            int x0 = s.W, y0 = s.H, x1 = -1, y1 = -1, cnt = 0;
            for (int y = 0; y < s.H; y++)
                for (int x = 0; x < s.W; x++)
                    if (cell[y * s.W + x] == i && s.P[y * s.W + x] >> 24 != 0) { (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y)); cnt++; }
            if (cnt < 4) continue;
            int pw = x1 - x0 + 1, ph2 = y1 - y0 + 1;
            var px = new Dots(pw, ph2, -pw / 2, -ph2 / 2);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (cell[y * s.W + x] == i) px.P[(y - y0) * pw + x - x0] = s.P[y * s.W + x];
            double ox = (x0 + x1 + 1) / 2.0 - s.W / 2.0, oy = (y0 + y1 + 1) / 2.0 - s.H / 2.0;   // 그림 가운데에서 조각 가운데까지 (그림 칸)
            double len = Math.Sqrt(ox * ox + oy * oy);
            // 몸 가운데(칸 가운데)에서 조각 가운데까지: 그림 왼쪽 위가 몸 가운데에서 (s.Ox, s.Oy) 칸 떨어져 있다
            var p = new Piece { W = pw, H = ph2, Ox = (s.Ox + (x0 + x1 + 1) / 2.0) * scale, Oy = (s.Oy + (y0 + y1 + 1) / 2.0) * scale, Ang = len < 1.5 ? rnd.NextDouble() * 6.28 : Math.Atan2(oy, ox), Flips = rnd.NextDouble() < .6 };
            Dots Narrow()
            {
                int nw = Math.Max(1, (pw + 1) / 2);
                var d = new Dots(nw, ph2, px.Ox, px.Oy);
                for (int y = 0; y < ph2; y++)
                    for (int x = 0; x < nw; x++) d.P[y * nw + x] = px.P[y * pw + Math.Min(pw - 1, x * 2)];
                return d;
            }
            Dots Mirror()
            {
                var d = new Dots(pw, ph2, px.Ox, px.Oy);
                for (int y = 0; y < ph2; y++)
                    for (int x = 0; x < pw; x++) d.P[y * pw + x] = px.P[y * pw + pw - 1 - x];
                return d;
            }
            Dots Dither(Dots src, bool checker)
            {
                var d = new Dots(src.W, src.H, src.Ox, src.Oy);
                for (int y = 0; y < src.H; y++)
                    for (int x = 0; x < src.W; x++)
                        if (checker ? ((x + y) & 1) == 0 : (x & 1) == 0 && (y & 1) == 0) d.P[y * src.W + x] = src.P[y * src.W + x];
                return d;
            }
            var mir = Mirror();
            (p.Norm, p.Narrow, p.Flip) = (ToStamp(px, dotPx), ToStamp(Narrow(), dotPx), ToStamp(mir, dotPx));
            (p.NormChk, p.NormSp, p.FlipChk, p.FlipSp) = (ToStamp(Dither(px, true), dotPx), ToStamp(Dither(px, false), dotPx), ToStamp(Dither(mir, true), dotPx), ToStamp(Dither(mir, false), dotPx));
            list.Add(p);
        }
        return list.ToArray();
    }

    FeelArt(int small, Stamp[][] rows)
    {
        Small = small;
        for (int r = 0; r < Rows; r++)
        {
            var row = rows[r];
            var kind = (Row)r;
            bool elite = kind is Row.EliteBug or Row.EliteWasp or Row.EliteBeetle or Row.EliteSteel;
            bool tok = kind is not (Row.Bug or Row.Wasp);
            int dotPx = kind == Row.Queen ? small + 1 : small;
            var ra = Arts[r] = new RowArt { Tok = tok, Dot = dotPx / (double)small, Spawn = kind is Row.Boss or Row.Queen ? .08 : .033 };
            var d0 = FromStamp(row[0], dotPx);
            uint edge = EdgeColor(d0);
            var body = BodyColors(d0, edge);
            ra.Half = Math.Max(d0.W, d0.H) / 2.0 * ra.Dot;
            ra.Flash = new Stamp[row.Length];
            for (int i = 0; i < row.Length; i++)
            {
                var di = i == 0 ? d0 : FromStamp(row[i], dotPx);
                ra.Flash[i] = ToStamp(FlashOf(di, edge), dotPx);
            }
            ra.Squash = ToStamp(SquashOf(d0, edge, 1.2, .7), dotPx);
            ra.SquashBig = ToStamp(SquashOf(d0, edge, 1.2 * 1.1, .7), dotPx);
            Color c0 = elite ? Gold : Color.FromArgb((int)body[0]), c1 = elite ? GoldLt : Color.FromArgb((int)body[1]);
            ra.Main = new Brush[] { new SolidBrush(c0), new SolidBrush(c1), new SolidBrush(FlashFill), new SolidBrush(Gold), new SolidBrush(GoldLt), new SolidBrush(Dirt), new SolidBrush(DirtLt) };
            ra.Light = new Brush[] { new SolidBrush(Lighter(c0, .45)), new SolidBrush(Lighter(c1, .45)), new SolidBrush(Color.White), new SolidBrush(Color.White), new SolidBrush(Color.White), new SolidBrush(Lighter(Dirt, .4)), new SolidBrush(Lighter(DirtLt, .4)) };
            double R = ra.Half;
            if (!tok)
            {
                uint pastel = U(Lighter(Color.FromArgb((int)body[0]), .3));
                for (int big = 0; big < 2; big++)
                {
                    double bk = big == 0 ? 1 : 1.25;
                    ra.Ring[big * 2] = ToStamp(RingOf(R * .8 * bk, 1.6, false, FillU, pastel), small);
                    ra.Ring[big * 2 + 1] = ToStamp(RingOf(R * 1.15 * bk, 1.0, true, FillU, pastel), small);
                }
            }
            else
            {
                for (int big = 0; big < 2; big++) ra.Star[big] = ToStamp(StarOf(R * (kind is Row.Boss or Row.Queen ? .8 : .95) * (big == 0 ? 1 : 1.25), FillU | 0xFF000000u, edge), small);
                int n = kind switch { Row.Boss or Row.Queen => 8, Row.Beetle or Row.Steel or Row.EliteSteel => 6, _ => 6 };
                ra.Shells = new Piece[3][];
                for (int v = 0; v < ra.Shells.Length; v++) ra.Shells[v] = Shell(d0, n, kind is Row.Boss or Row.Queen, 7919 * (r + 1) + 131 * v, dotPx, small);
            }
        }
    }

    // 배율(small)마다 한 번: 같은 그림 묶음에서 만든 줄이 그대로면 되쓴다 (게임을 여러 번 열어도 다시 만들지 않는다)
    static readonly ConditionalWeakTable<Sprites, Dictionary<int, FeelArt>> cache = new();

    public static FeelArt Of(Sprites sp, int small, Stamp[][] rows)
    {
        var byScale = cache.GetValue(sp, _ => new Dictionary<int, FeelArt>());
        lock (byScale) return byScale.TryGetValue(small, out var hit) ? hit : byScale[small] = new FeelArt(small, rows);
    }

    // 줄 고르기 (SwarmGame.FoeArt 와 같은 차례)
    public static Row RowOf(Swarm.Kind kind, int tier, bool elite, bool heavy) =>
        kind == Swarm.Kind.Boss && tier >= Swarm.Bosses ? Row.Queen : heavy ? (elite ? Row.EliteSteel : Row.Steel) : elite && kind != Swarm.Kind.Boss ? Row.EliteBug + (int)kind : (Row)(int)kind;
}
