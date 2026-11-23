using System.Drawing.Imaging;
using TPx = Bolok.TrailArt.TPx;

namespace Bolok;

// 눈길 마중 그리기 (docs/ice-run.md 2-3): 판 200×120칸을 칸 하나 = f 화면 점으로.
// 길은 줄마다 FillRectangle (눈밭·길·가장자리·눈 턱·점선), 길 위의 것은 크기 12단계로 미리 만든 그림을 찍는다 (판을 열 때 한 번 만든다).
// 하늘은 비워 작업 화면이 그대로 보이고, 지평선에 낮은 산 띠만 (시안 A1). 반투명·글자 없음, 프레임마다 새로 만드는 것 없음.

// 그림 한 장: 앵커(발 가운데)에서 왼쪽 위까지
sealed class TrailPic : IDisposable
{
    public readonly Bitmap Bmp;
    public readonly double AnchorX;
    public readonly int AnchorY, W, H;
    public TrailPic(TPx g, int f, double ax, int ay) { Bmp = g.ToBmp(f); (AnchorX, AnchorY, W, H) = (ax, ay, g.W, g.H); }
    public void Dispose() => Bmp.Dispose();
}

// 마중 나온 친구 그림 (크기 단계마다): 서 있기, 폴짝 두 장
sealed class TrailFriend : IDisposable
{
    public readonly string Species;
    public readonly TrailPic?[] Idle, Hop1, Hop2;
    public readonly int HeartRow;   // 친구 그림에서 처음 찬 줄 (칸, 배율 1 그림 기준)
    public TrailFriend(string species, Sprites sp, int f, double[] steps)
    {
        Species = species;
        Idle = new TrailPic?[steps.Length]; Hop1 = new TrailPic?[steps.Length]; Hop2 = new TrailPic?[steps.Length];
        var idle = TPx.From(sp.Get("adult_sweet/0/idle", 0, 1));
        var hop1 = TPx.From(sp.Get("adult_sweet/0/hop", 1, 1)); var hop2 = TPx.From(sp.Get("adult_sweet/0/hop", 2, 1));
        HeartRow = 40;
        for (int y = 0; y < idle.H && HeartRow == 40; y++) for (int x = 0; x < idle.W; x++) if (idle.P[y * idle.W + x] != 0) { HeartRow = y; break; }
        for (int i = 0; i < steps.Length; i++)
        {
            if (steps[i] < .22) continue;   // 먼 것은 눈사람 점으로 갈음
            Idle[i] = Make(idle, steps[i], f); Hop1[i] = Make(hop1, steps[i], f); Hop2[i] = Make(hop2, steps[i], f);
        }
    }

    static TrailPic Make(TPx src, double s, int f)
    {
        int w = Math.Max(1, (int)Math.Round(src.W * s)), h = Math.Max(1, (int)Math.Round(src.H * s));
        var g = new TPx(w, h);
        g.BlitScaled(src, s, w / 2.0, h);
        return new TrailPic(g, f, w / 2.0, (int)Math.Round(37 * s));   // 발 줄(37)이 길 줄에 닿게
    }

    public void Dispose() { foreach (var set in new[] { Idle, Hop1, Hop2 }) foreach (var p in set) p?.Dispose(); }
}

// 한 프레임 그릴 때 게임이 넘기는 값 (그림 이름·표시): 프레임마다 새로 만들지 않고 고쳐 쓴다
sealed class TrailShow
{
    public string PetRow = "";       // "child/0/away" 같은 그림 줄 이름
    public int PetFrame;
    public bool Blink, Bang;
    public int Count;                // 3·2·1 (0 이면 없음)
    public bool Result, Won, NewBest;
    public int Best, Fires, Secs, Score;
    public bool Cleared;
    public double Clock;
}

sealed class TrailView : IDisposable
{
    public const int PW = 200, PH = 120, Hz = 44, PetRow = 107, Rows = 76, Steps = 12;
    const double Z0 = 60, Mid = 64, HalfRoad = 70, SMin = .12, SMax = 1.19;
    readonly int f;
    readonly Sprites art;
    readonly double[] rowZ = new double[Rows + 2], rowS = new double[Rows + 2], rowHw = new double[Rows + 2], rowCw = new double[Rows + 2];
    public static readonly double[] StepS = MakeSteps();
    static double[] MakeSteps() { var a = new double[Steps]; for (int i = 0; i < Steps; i++) a[i] = SMin * Math.Pow(SMax / SMin, i / (Steps - 1.0)); return a; }
    static readonly double InvLog = (Steps - 1) / Math.Log(SMax / SMin);

    // 붓
    readonly SolidBrush brField = new(Color.FromArgb((int)TrailArt.A1.Field)), brField2 = new(Color.FromArgb((int)TrailArt.A1.Field2)),
        brRoad = new(Color.FromArgb((int)TrailArt.A1.Road)), brRoad2 = new(Color.FromArgb((int)TrailArt.A1.Road2)),
        brEdge = new(Color.FromArgb((int)TrailArt.A1.Edge)), brBank = new(Color.FromArgb((int)TrailArt.A1.Bank)), brShade = new(Color.FromArgb((int)TrailArt.A1.BankShade)),
        brDash = new(Color.FromArgb((int)TrailArt.A1.Dash)), brMark = new(Color.FromArgb((int)TrailArt.A1.Mark)), brWhite = new(Color.White),
        brRed = new(Color.FromArgb((int)TrailArt.Red)), brCream = new(Color.FromArgb((int)TrailArt.Cream)), brLine = new(Color.FromArgb((int)TrailArt.Line)),
        brGold = new(Color.FromArgb(250, 185, 40)), brWind = new(Color.FromArgb(228, 236, 244));

    // 그림 (크기 단계 12 × 종류)
    readonly TrailPic[] pile = new TrailPic[Steps], hole = new TrailPic[Steps], snowman = new TrailPic[Steps], fallen = new TrailPic[Steps], ice = new TrailPic[Steps],
        berry = new TrailPic[Steps], glass = new TrailPic[Steps], star = new TrailPic[Steps], post = new TrailPic[Steps], ballShadow = new TrailPic[Steps], glow = new TrailPic[Steps];
    readonly TrailPic[][] ball = { new TrailPic[Steps], new TrailPic[Steps], new TrailPic[Steps], new TrailPic[Steps] }, fire = { new TrailPic[Steps], new TrailPic[Steps] };
    readonly TrailPic[] petShadow = new TrailPic[3];
    readonly TrailPic fireOn, fireOff, fireOnBig, fireOffBig, flag, flagBig, clock, arrow;
    readonly Bitmap band;     // 산 띠 + 지평선 안개 (가로 400칸: 굽이에서 옆으로 민다)
    public readonly TrailFriend?[] Friends;
    readonly (float U, float Z)[] marks = new (float, float)[56];
    readonly (int X, int Y, int Len)[] streaks = new (int, int, int)[22];

    public int Scale => f;

    // friends: 쉼터마다 마중 나올 친구 (null 이면 눈사람)
    public TrailView(Sprites art, int f, (string Species, Sprites Sp)?[] friends)
    {
        (this.art, this.f) = (art, f);
        for (int d = 1; d <= Rows + 1; d++)
        {
            rowZ[d] = Z0 * Mid / d; rowS[d] = d / Mid; rowHw[d] = HalfRoad * rowS[d]; rowCw[d] = Math.Pow((Rows - d) / (double)Rows, 2) * HalfRoad;
        }
        for (int i = 0; i < Steps; i++)
        {
            double s = StepS[i];
            bool far = s < .22;
            pile[i] = Pic(far ? TrailArt.Dot(false) : TrailArt.SnowPile(s));
            hole[i] = Pic(far ? TrailArt.Dot(false, true) : TrailArt.Hole(s), half: true);
            snowman[i] = Pic(far ? TrailArt.Dot(false) : TrailArt.Snowman(s));
            fallen[i] = Pic(far ? TrailArt.Dot(false) : Scaled(TrailArt.Fallen(), s / .85));
            ice[i] = Pic(far ? TrailArt.Dot(false, true) : TrailArt.IcePatch(s), half: true);
            berry[i] = Pic(far ? TrailArt.Dot(true) : TrailArt.Berry(s));
            glass[i] = Pic(far ? TrailArt.Dot(true, false, true) : TrailArt.Hourglass(s));
            star[i] = Pic(far ? TrailArt.Dot(true, false, true) : TrailArt.StarArt(s));
            post[i] = Pic(TrailArt.Post(s, false));
            for (int k = 0; k < 4; k++) ball[k][i] = Pic(far ? TrailArt.Dot(false) : TrailArt.Snowball(s, k));
            for (int k = 0; k < 2; k++) fire[k][i] = Pic(s < .3 ? TrailArt.FarFire(s) : TrailArt.Campfire(s, k));
            ballShadow[i] = Dotted(7 * s, 1.6 * s);
            glow[i] = s < .3 ? ballShadow[i] : DottedColor(26 * s, 6.5 * s, TrailArt.A1.Glow, true);
        }
        var sh = new double[] { 1, .8, .6 };
        for (int i = 0; i < 3; i++) petShadow[i] = Dotted(10 * sh[i], 2.2 * sh[i]);
        int sm = GameHud.Small(f);
        fireOn = Glyph(TrailArt.FireIcon, sm); fireOff = Glyph(TrailArt.FireOff, sm); fireOnBig = Glyph(TrailArt.FireIcon, f); fireOffBig = Glyph(TrailArt.FireOff, f);
        flag = Glyph(TrailArt.FlagRows, sm); flagBig = Glyph(TrailArt.FlagRows, f); clock = Glyph(TrailArt.ClockIcon, sm); arrow = Glyph(TrailArt.ArrowRows, f);
        band = MakeBand();
        for (int n = 0; n < marks.Length; n++)
        {
            ulong h = Hash(n, 7);
            marks[n] = ((float)((h % 1000 / 1000.0 * 2.6 + 1.25) * ((h >> 10) % 2 == 0 ? 1 : -1)), (float)((h >> 20) % 1000 / 1000.0 * 420));
        }
        for (int n = 0; n < streaks.Length; n++) { ulong h = Hash(n, 31); streaks[n] = ((int)(h % 200), 48 + (int)((h >> 12) % 70), 3 + (int)((h >> 22) % 6)); }
        Friends = new TrailFriend?[friends.Length];
        for (int i = 0; i < friends.Length; i++)
            if (friends[i] is { } fr) Friends[i] = new TrailFriend(fr.Species, fr.Sp, f, StepS);
    }

    static ulong Hash(int a, int b) { ulong h = (ulong)(a * 73856093 ^ b * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return h; }

    TrailPic Pic(TPx g, bool half = false) => new(g, f, g.W / 2.0, g.H - 1 - (half ? (int)Math.Round(g.H * .5) : 0));
    TrailPic Glyph(string[] rows, int scale) { var g = TrailArt.Glyph(rows, TrailArt.GP); return new TrailPic(g, scale, g.W / 2.0, g.H / 2); }
    static TPx Scaled(TPx src, double k)
    {
        int w = Math.Max(1, (int)Math.Round(src.W * k)), h = Math.Max(1, (int)Math.Round(src.H * k));
        var g = new TPx(w, h);
        g.BlitScaled(src, k, w / 2.0, h);
        return g;
    }

    // 점무늬 타원 (그림자 · 모닥불 둘레): 가운데가 앵커
    TrailPic Dotted(double rx, double ry) => DottedColor(rx, ry, TrailArt.A1.Shadow, false);
    TrailPic DottedColor(double rx, double ry, uint color, bool sparse)
    {
        int w = (int)Math.Ceiling(rx * 2) + 3, h = (int)Math.Ceiling(ry * 2) + 3;
        var g = new TPx(w, h);
        double cx = w / 2.0, cy = h / 2.0;
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                double a = (i + .5 - cx) / rx, b = (j + .5 - cy) / ry, r = a * a + b * b;
                if (r < 1 && (i + j) % 2 == 0 && (!sparse || r < .45 || (i / 2 + j) % 2 == 0)) g.P[j * w + i] = color;
            }
        if (!g.P.Any(p => p != 0)) g.P[h / 2 * w + w / 2] = color;   // 아주 작은 그림자도 한 점은
        return new TrailPic(g, f, cx, h / 2);
    }

    // 산 띠 (Hz−12 ~ Hz 줄) + 지평선 안개 점무늬 (Hz ~ Hz+5 줄). 가로 두 바퀴(400칸)를 이어 굽이에서 옆으로 민다
    Bitmap MakeBand()
    {
        var g = new TPx(400, 17);
        for (int x = 0; x < 400; x++)
        {
            double a = Math.PI * 2 * x / PW;
            double h = 5.5 + 3.2 * Math.Sin(a * 3 + 1.3) + 2.2 * Math.Sin(a * 7 + .4) + 1.1 * Math.Sin(a * 15);
            int top = 12 - (int)Math.Round(Math.Max(1, h));
            for (int y = top; y < 12; y++) g.P[y * 400 + x] = TrailArt.A1.Mount;
            if (h > 7.2) for (int y = top; y < top + (int)((h - 7.2) * .9) + 1; y++) g.P[y * 400 + x] = TrailArt.A1.MountHi;
        }
        uint haze = TrailArt.Mix(TrailArt.A1.Field, TrailArt.A1.Mount, .35);
        for (int y = 12; y < 17; y++)
            for (int x = 0; x < 400; x++)
                if ((x + y) % 2 == 0 && (y < 15 || (x / 2 + y) % 2 == 0)) g.P[y * 400 + x] = haze;
        return g.ToBmp(f);
    }

    public void Dispose()
    {
        foreach (var set in new[] { pile, hole, snowman, fallen, ice, berry, glass, star, post, ballShadow, glow, petShadow }) foreach (var p in set) p.Dispose();
        foreach (var set in ball.Concat(fire)) foreach (var p in set) p.Dispose();
        foreach (var p in new[] { fireOn, fireOff, fireOnBig, fireOffBig, flag, flagBig, clock, arrow }) p.Dispose();
        band.Dispose();
        foreach (var fr in Friends) fr?.Dispose();
    }

    // ── 투영: 볼록이 앞 D 걸음, 좌우 u 의 자리 → 판 안 칸 좌표 (x, 발 줄 y, 크기 s) ──
    public static int StepFor(double s) => Math.Clamp((int)Math.Round(Math.Log(s / SMin) * InvLog), 0, Steps - 1);
    double CwAt(double d) { double u = (Rows - d) / Rows; return u * u * HalfRoad; }

    bool Proj(Trail t, double ahead, double u, out double x, out int y, out double s, out int step)
    {
        double d = Z0 * Mid / (Z0 + ahead);
        (x, y, s, step) = (0, 0, 0, 0);
        if (d < 1 || d > 72.5 || ahead > t.T.SeeDist + 8) return false;
        s = d / Mid;
        x = 100 + t.Curve * CwAt(d) + u * HalfRoad * s;
        y = (int)Math.Round(Hz - 1 + d);
        step = StepFor(s);
        return true;
    }

    // 그림을 찍는다. 판 밖으로 나가는 부분은 잘라서 찍는다 (그림 창에 판 밖 그림이 남지 않게: Graphics 에 클립을 걸지 않는다)
    void Put(Graphics g, int ox, int oy, TrailPic p, double x, int y)
    {
        int cx = (int)Math.Round(x - p.AnchorX), cy = y - p.AnchorY;
        if (cx >= 0 && cy >= 0 && cx + p.W <= PW && cy + p.H <= PH) { g.DrawImageUnscaled(p.Bmp, ox + cx * f, oy + cy * f); return; }
        int x0 = Math.Max(0, cx), y0 = Math.Max(0, cy), x1 = Math.Min(PW, cx + p.W), y1 = Math.Min(PH, cy + p.H);
        if (x0 >= x1 || y0 >= y1) return;
        g.DrawImage(p.Bmp, new Rectangle(ox + x0 * f, oy + y0 * f, (x1 - x0) * f, (y1 - y0) * f), (x0 - cx) * f, (y0 - cy) * f, (x1 - x0) * f, (y1 - y0) * f, GraphicsUnit.Pixel);
    }

    void Rect(Graphics g, Brush b, int ox, int oy, int x, int y, int w, int h = 1)
    {
        int x0 = Math.Max(0, x), x1 = Math.Min(PW, x + w);
        if (x1 > x0 && y >= 0 && y + h <= PH) g.FillRectangle(b, ox + x0 * f, oy + y * f, (x1 - x0) * f, h * f);
    }

    // 자가 검사: 크기 단계가 있는 그림 모음
    internal IEnumerable<(string, TrailPic[])> Gallery()
    {
        yield return ("눈더미", pile); yield return ("물구멍", hole); yield return ("눈사람", snowman); yield return ("무너진 눈사람", fallen); yield return ("빙판", ice);
        yield return ("열매", berry); yield return ("모래시계", glass); yield return ("별", star); yield return ("등불 기둥", post); yield return ("눈덩이 그림자", ballShadow);
        yield return ("모닥불 둘레", glow);
        for (int k = 0; k < 4; k++) yield return ($"눈덩이 {k}", ball[k]);
        for (int k = 0; k < 2; k++) yield return ($"모닥불 {k}", fire[k]);
    }

    // ── 한 프레임 ──
    public void Draw(Overlay o, Graphics g, Rectangle panel, Trail t, TrailShow sh)
    {
        int ox = panel.X - o.Left, oy = panel.Y - o.Top;
        Road(g, ox, oy, t);
        g.DrawImage(band, new Rectangle(ox, oy + (Hz - 12) * f, PW * f, 17 * f), (PW + (int)Math.Round(t.Curve * 14)) * f, 0, PW * f, 17 * f, GraphicsUnit.Pixel);
        Marks(g, ox, oy, t);
        if (t.WindLeft > 0 || WindWarning(t, out _)) Wind(g, ox, oy, t, sh.Clock);
        World(o, g, ox, oy, panel, t, sh);
        Hud(o, g, panel, t, sh);
        if (sh.Count > 0) GameHud.Count(o, g, panel, f, sh.Count);
        if (sh.Result) Result(o, g, panel, sh);
    }

    // 눈밭과 길: 줄마다 (줄무늬는 달린 거리에 맞춰 흐른다)
    void Road(Graphics g, int ox, int oy, Trail t)
    {
        double dist = t.Pos, curve = t.Curve;
        for (int d = 1; d <= Rows; d++)
        {
            int y = Hz - 1 + d;
            double z = rowZ[d], s = rowS[d];
            bool on = ((int)Math.Floor((z + dist) / 14) & 1) == 0;
            Rect(g, on ? brField : brField2, ox, oy, 0, y, PW);
            double cx = 100 + curve * rowCw[d], hw = rowHw[d];
            int x0 = (int)Math.Round(cx - hw), x1 = (int)Math.Round(cx + hw);
            Rect(g, on ? brRoad : brRoad2, ox, oy, x0, y, x1 - x0);
            int ew = s > .7 ? 2 : 1;
            Rect(g, brEdge, ox, oy, x0, y, ew); Rect(g, brEdge, ox, oy, x1 - ew, y, ew);
            if (on)   // 가장자리 눈 턱: 바깥 한 칸은 그늘
            {
                int bw = Math.Max(1, (int)Math.Round(3.2 * s));
                Rect(g, brShade, ox, oy, x0 - bw, y, 1); Rect(g, brBank, ox, oy, x0 - bw + 1, y, bw - 1);
                Rect(g, brBank, ox, oy, x1, y, bw - 1); Rect(g, brShade, ox, oy, x1 + bw - 1, y, 1);
            }
            if (d > 2 && ((int)Math.Floor((z + dist) / 7) & 3) == 0)
            {
                int dw = Math.Max(1, (int)Math.Round(1.6 * s));
                Rect(g, brDash, ox, oy, (int)Math.Round(cx - dw / 2.0), y, dw);
            }
        }
    }

    // 눈밭 자국: 길 양옆에서 흘러오는 짧은 점
    void Marks(Graphics g, int ox, int oy, Trail t)
    {
        double off = t.Pos % 420;
        for (int n = 0; n < marks.Length; n++)
        {
            double z = 62 + ((marks[n].Z - off) % 420 + 420) % 420;
            double d = Z0 * Mid / z;
            if (d < 1) continue;
            double s = d / Mid, x = 100 + t.Curve * CwAt(d) + marks[n].U * HalfRoad * s;
            int y = (int)Math.Round(Hz - 1 + d);
            if (y >= PH || y <= Hz + 1) continue;
            Rect(g, brMark, ox, oy, (int)x, y, Math.Max(1, (int)Math.Round(3 * s)));
        }
    }

    static bool WindWarning(Trail t, out double dir)
    {
        dir = 0;
        for (int i = t.Lo; i < t.Things.Length; i++)
        {
            ref var th = ref t.Things[i];
            if (th.P0 - t.Pos > t.T.WindWarn + 40) break;
            if (th.Kind == TrailKind.Wind && !th.On && th.P - t.Pos < t.T.WindWarn) { dir = th.U; return true; }
        }
        return false;
    }

    // 옆바람 눈발 (한쪽으로 날린다): 예고(2초 전)와 부는 동안
    void Wind(Graphics g, int ox, int oy, Trail t, double clock)
    {
        double dir = t.WindLeft > 0 ? t.WindDir : WindWarning(t, out double wd) ? wd : 1;
        for (int n = 0; n < streaks.Length; n++)
        {
            var (sx, sy, len) = streaks[n];
            int x = (int)(((sx + clock * 60 * dir) % PW + PW) % PW);
            Rect(g, brWhite, ox, oy, x, sy, Math.Min(2, len));
            Rect(g, brWind, ox, oy, x + (dir > 0 ? 2 : -(len - 2)), sy, len - 2);
        }
    }

    // 길 위의 것 (먼 것부터), 쉼터, 볼록이
    void World(Overlay o, Graphics g, int ox, int oy, Rectangle panel, Trail t, TrailShow sh)
    {
        int hi = t.Things.Length - 1;
        while (hi >= 0 && t.Things[hi].P0 - t.Pos > t.T.SeeDist + 160) hi--;   // 굴러오는 눈덩이는 처음 자리가 더 앞이다
        int gate = t.Leg < t.Gates.Length ? t.Leg : -1;
        double gateD = gate >= 0 ? t.Gates[gate] - t.Pos : double.MaxValue;
        bool gateDone = gate < 0 || gateD > t.T.SeeDist + 8 || gateD < -7, petDone = false;
        // 지난 쉼터가 아직 보이면 (볼록이 뒤 몇 걸음) 그것도
        int prevGate = t.Leg > 0 ? t.Leg - 1 : -1;
        double prevD = prevGate >= 0 ? t.Gates[prevGate] - t.Pos : double.MinValue;
        bool prevDone = prevGate < 0 || prevD < -7;
        for (int i = hi; i >= t.Lo - 1; i--)
        {
            double ahead = i >= t.Lo ? t.Ahead(t.Things[i]) : double.MinValue;
            if (!gateDone && ahead < gateD) { Gate(o, g, ox, oy, t, sh, gate, gateD); gateDone = true; }
            if (!petDone && ahead < 0) { Pet(o, g, ox, oy, panel, t, sh); petDone = true; }
            if (!prevDone && ahead < prevD) { Gate(o, g, ox, oy, t, sh, prevGate, prevD); prevDone = true; }
            if (i >= t.Lo) Thing(g, ox, oy, t, ref t.Things[i]);
        }
        if (!gateDone) Gate(o, g, ox, oy, t, sh, gate, gateD);
        if (!petDone) Pet(o, g, ox, oy, panel, t, sh);
        if (!prevDone) Gate(o, g, ox, oy, t, sh, prevGate, prevD);
    }

    void Thing(Graphics g, int ox, int oy, Trail t, ref TrailThing th)
    {
        if (th.Gone || th.Kind == TrailKind.Wind) return;
        if (th.Kind == TrailKind.Ball && !th.On && th.P0 - t.Pos > t.T.SeeDist) return;
        double ahead = t.Ahead(th);
        if (!Proj(t, ahead, th.U, out double x, out int y, out double s, out int st)) return;
        switch (th.Kind)
        {
            case TrailKind.Pile: Put(g, ox, oy, pile[st], x, y); break;
            case TrailKind.Hole: Put(g, ox, oy, hole[st], x, y); break;
            case TrailKind.Snowman: Put(g, ox, oy, th.Hit ? fallen[st] : snowman[st], x, y); break;
            case TrailKind.Ball: Put(g, ox, oy, ballShadow[st], x, y); Put(g, ox, oy, ball[th.Frame & 3][st], x, y); break;
            case TrailKind.Ice: Put(g, ox, oy, ice[st], x, y); break;
            case TrailKind.Berry: Put(g, ox, oy, berry[st], x, y); break;
            case TrailKind.Glass: Put(g, ox, oy, glass[st], x, y); break;
            case TrailKind.Star: Put(g, ox, oy, star[st], x, y); break;
        }
    }

    // 쉼터: 모닥불(옆, 따뜻한 둘레) · 등불 기둥 둘 + 줄 · 마중 나온 친구 (없으면 눈사람)
    void Gate(Overlay o, Graphics g, int ox, int oy, Trail t, TrailShow sh, int gate, double gateD)
    {
        if (Proj(t, gateD + 6, -1.5, out double fx, out int fy, out double fs, out int fst))
        {
            if (fs >= .3) Put(g, ox, oy, glow[fst], fx, fy);
            Put(g, ox, oy, fire[(int)(sh.Clock * 5) & 1][fst], fx, fy);
        }
        if (Proj(t, gateD, 0, out double cx, out int ry, out double rs, out int rst))
        {
            Put(g, ox, oy, post[rst], Proj(t, gateD, -1.12, out double lx, out _, out _, out _) ? lx : cx, ry);
            Put(g, ox, oy, post[rst], Proj(t, gateD, 1.12, out double rx, out _, out _, out _) ? rx : cx, ry);
            Rope(g, ox, oy, cx, ry, rs);
        }
        if (Proj(t, gateD - 1, -.86, out double px, out int py, out double ps, out int pst))
        {
            var fr = gate < Friends.Length ? Friends[gate] : null;
            if (fr == null || fr.Idle[pst] == null) { Put(g, ox, oy, snowman[pst], px, py); return; }
            bool joy = gateD < 40 || t.GateAt > t.Clock - 1.6 && t.Leg > gate;
            var pic = joy ? ((int)(sh.Clock * 5) & 1) == 0 ? fr.Hop1[pst]! : fr.Hop2[pst]! : fr.Idle[pst]!;
            Put(g, ox, oy, pic, px, py);
            if (joy && gateD < 22 && ps > .6 && Stamps.Of(art).Has("emo/heart"))   // 머리 위 하트 말풍선
            {
                int top = (int)Math.Round(fr.HeartRow * ps);
                var heart = Stamps.Of(art).Get("emo/heart", 0, f);
                int hx = (int)Math.Round(px - pic.AnchorX) + 9, hy = py - pic.AnchorY + top - 22;
                int cell = art.Cell * f;
                heart.DrawCell(g, ox + hx * f, oy + hy * f, cell);
            }
        }
    }

    // 쉼터 줄: 빨강·크림 마디
    void Rope(Graphics g, int ox, int oy, double cx, int y, double s)
    {
        double hw = HalfRoad * s * 1.12;
        int th = Math.Max(1, (int)Math.Round(1.6 * s)), x0 = (int)(cx - hw), x1 = (int)(cx + hw);
        double seg = Math.Max(2, 4 * s);
        Rect(g, brLine, ox, oy, x0, y + 1, x1 - x0 + 1); Rect(g, brLine, ox, oy, x0, y - th, x1 - x0 + 1);
        int n = 0;
        for (double x = x0; x <= x1; x += seg, n++)
            Rect(g, n % 2 == 0 ? brRed : brCream, ox, oy, (int)x, y - th + 1, (int)Math.Min(seg, x1 - x + 1), th);
    }

    // 볼록이: 길 줄 위에서 (그림자 → 발밑 눈 튀김 → 본체)
    public double PetX(Trail t) => 100 + t.Curve * CwAt(Mid) + t.X * HalfRoad;

    void Pet(Overlay o, Graphics g, int ox, int oy, Rectangle panel, Trail t, TrailShow sh)
    {
        double x = PetX(t);
        int air = (int)Math.Round(t.Air), cellPx = art.Cell * f;
        Put(g, ox, oy, petShadow[air > 14 ? 2 : air > 3 ? 1 : 0], x, PetRow + 1);
        if (t.State == Trail.Phase.Run && air == 0 && t.Speed > 4 && !t.Halted)   // 발밑 눈 튀김
        {
            int fr = (int)(t.Pos / 7) & 1;
            foreach (var (dx, dy, white) in Spray)
                Rect(g, white ? brWhite : brBank, ox, oy, (int)x + dx + fr, PetRow + dy - fr, 1);
        }
        if (!sh.Blink)
        {
            var stamps = Stamps.Of(art);
            if (stamps.Has(sh.PetRow))
            {
                var row = stamps.Row(sh.PetRow, f);
                row[sh.PetFrame % row.Length].DrawCell(g, ox + ((int)Math.Round(x) - 20) * f, oy + (PetRow - 37 - air) * f, cellPx);
            }
        }
        if (sh.Bang && BangStamp() is { } bang)
            bang.Put(o, g, panel.X + (int)Math.Round(x) * f, panel.Y + (PetRow - 37 - air - 14) * f);
        if (t.StarLeft > 0)   // 별: 몸 둘레에 반짝
        {
            var spark = Stamps.Of(art).Get("game/spark", 0, GameHud.Small(f));
            for (int k = 0; k < 3; k++)
            {
                double a = sh.Clock * 7 + k * 2.1;
                spark.Put(o, g, panel.X + (int)(x * f + Math.Cos(a) * 13 * f), panel.Y + (int)((PetRow - 20 - air) * f + Math.Sin(a) * 11 * f));
            }
        }
    }

    Stamp? BangStamp() => Stamps.Of(art).Has("emo/bang") ? Stamps.Of(art).Get("emo/bang", 0, GameHud.Small(f)) : null;

    static readonly (int, int, bool)[] Spray = { (-9, -1, true), (-11, -3, true), (8, -2, true), (11, -1, false), (-7, -4, false), (6, -5, true), (13, -4, true) };

    // ── 표시: 점수(왼쪽 위) · 시간 막대와 남은 초(위 가운데) · 쉼터 불 다섯과 단계 깃발(오른쪽 위). 글자 없음 ──
    void Hud(Overlay o, Graphics g, Rectangle panel, Trail t, TrailShow sh)
    {
        int k = f, sm = GameHud.Small(k);
        GameHud.Corners(o, g, panel, k);
        GameHud.Score(o, g, panel, k, t.Score);
        int y = panel.Top + 7 * k, cx = panel.Left + panel.Width / 2, bw = 46 * k;
        DrawGlyph(o, g, clock, cx - bw / 2 - 6 * k, y);
        double frac = t.Time / t.T.TimeCap;
        bool low = t.Time < 5 && t.State == Trail.Phase.Run;
        GameHud.Bar(o, g, new Rectangle(cx - bw / 2, y - 4 * k / 3, bw, Math.Max(4, 8 * k / 3)), k, frac, low && ((int)(sh.Clock * 4) & 1) == 0 ? brRed : brGold);
        GameHud.Number(o, g, (int)Math.Ceiling(t.Time), cx + bw / 2 + 6 * k, y, sm, -1);
        int fx = panel.Right - 6 * k - 5 * sm;
        DrawGlyph(o, g, flag, fx, y - sm);
        for (int i = 0; i < t.Gates.Length; i++)
            DrawGlyph(o, g, i < t.Leg ? fireOn : fireOff, fx - 10 * k - (t.Gates.Length - 1 - i) * 8 * sm, y - 1);
    }

    // 그림 한가운데가 화면 좌표 (cx, cy) 에 오게
    void DrawGlyph(Overlay o, Graphics g, TrailPic p, int cx, int cy) => g.DrawImageUnscaled(p.Bmp, cx - o.Left - p.Bmp.Width / 2, cy - o.Top - p.Bmp.Height / 2);

    // ── 끝 화면 (시안 D): 갈색 상자에 큰 점수, 위에 큰 왕관(새 기록), 아래 모닥불 다섯 줄 · 모래시계 + 걸린 초 · 작은 왕관 + 최고 (완주면 깃발 ↑) ──
    void Result(Overlay o, Graphics g, Rectangle panel, TrailShow sh)
    {
        int k = f, big = k + 1, sm = GameHud.Small(k);
        int cx = panel.Left + panel.Width / 2, cy = (int)(panel.Top + panel.Height * .45);
        int half = 74 * k / 2 + 6 * k;
        var box = o.Client(Rectangle.FromLTRB(cx - half, cy - 5 * big - 4 * k, cx + half, cy + 5 * big + 27 * k));
        g.FillRectangle(brCream, box.X + k, box.Y - k, box.Width - 2 * k, box.Height + 2 * k);
        g.FillRectangle(brCream, box.X - k, box.Y + k, box.Width + 2 * k, box.Height - 2 * k);
        g.FillRectangle(brLine, box.X + k, box.Y, box.Width - 2 * k, box.Height);
        g.FillRectangle(brLine, box.X, box.Y + k, box.Width, box.Height - 2 * k);
        GameHud.Number(o, g, sh.Score, cx, cy, big);
        var stamps = Stamps.Of(art);
        if (sh.NewBest)
        {
            stamps.Get("game/crown", 0, big).Put(o, g, cx, panel.Top + (int)(panel.Height * .45) - 5 * big - 4 * k - 7 * big);
            foreach (var (dx, dy) in new[] { (-30, -6), (31, -10), (-22, -16) })
                stamps.Get("game/spark", 0, sm).Put(o, g, cx + dx * k / 2, cy - 5 * big - 4 * k + dy * k / 2 - 7 * big);
        }
        int ry = cy + 5 * big + 9 * k;
        for (int i = 0; i < 5; i++) DrawGlyph(o, g, i < sh.Fires ? fireOnBig : fireOffBig, cx - 2 * 10 * k + i * 10 * k, ry);
        int r2 = ry + 12 * k;
        DrawGlyph(o, g, clock, cx - 31 * k, r2);
        GameHud.Number(o, g, sh.Secs, cx - 25 * k, r2, sm, -1);
        if (sh.Cleared) { DrawGlyph(o, g, flagBig, cx + 14 * k, r2 - 2); DrawGlyph(o, g, arrow, cx + 26 * k, r2 - 1); }
        else if (sh.Best > 0)
        {
            stamps.Get("game/crown", 0, sm).Put(o, g, cx + 8 * k, r2);
            GameHud.Number(o, g, sh.Best, cx + 14 * k, r2, sm, -1);
        }
    }
}
