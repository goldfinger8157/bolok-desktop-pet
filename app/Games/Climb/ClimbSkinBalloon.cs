using Px = Bolok.ClimbArt.Px;

namespace Bolok;

// T3 풍선: 떠 있는 풍선이 발판. 시간 = 서 있는 풍선의 바람이 빠져 쪼그라들고 볼록이가 가라앉는다
sealed class BalloonSkin : ClimbSkin
{
    public override ClimbTheme Theme => ClimbTheme.Balloon;
    public override int Variants => 7;

    static uint C(string h) => SkinKit.C(h);
    static readonly SkinTone[] Col =
    {
        SkinKit.Tone("#F0566E", .45, .3), SkinKit.Tone("#FFD24A", .5, .3), SkinKit.Tone("#5AA9F0", .45, .3), SkinKit.Tone("#5CC98B", .45, .3),
        SkinKit.Tone("#FF94C2", .5, .28), SkinKit.Tone("#A98BF0", .45, .3), SkinKit.Tone("#FF9F4A", .45, .3),
    };
    static readonly uint StringC = C("#8A7468");
    const int CXB = 24, CY0 = 20;   // 풍선 가운데

    static void Body(Px g, double cx, double cy, double rx, double ry, double e, SkinTone t, uint? tint = null)
    {
        for (int y = (int)(cy - ry) - 1; y <= (int)(cy + ry) + 1; y++)
            for (int x = (int)(cx - rx) - 1; x <= (int)(cx + rx) + 1; x++)
            {
                double dx = (x + .5 - cx) / rx, dy = (y + .5 - cy) / ry;
                if (Math.Pow(Math.Abs(dx), e) + Math.Pow(Math.Abs(dy), e) >= 1) continue;
                double s = dx * .5 + dy * .85;
                uint c = s > .5 ? t.S : s < -.62 ? t.H : t.F;
                if (tint is { } tc) c = SkinKit.Mix(c, tc, .5);
                g.Put(x, y, c);
            }
    }

    // 매듭
    static void Knot(Px g, double cx, double by, SkinTone t) => g.Poly(new[] { (cx - 2.6, by - .6), (cx + 2.6, by - .6), (cx, by + 3.0) }, t.S);

    // 끈 (테두리 뒤에 가는 선 한 줄)
    static void Str(Px g, int x, int y, int len, int seed = 0)
    {
        for (int i = 0; i < len; i++) g.Put(x + (int)Math.Round(Math.Sin((i + seed) * .75) * 1.4), y + i, StringC);
    }

    SkinPx Balloon(SkinTone t, int stage)
    {
        var g = new Px(52, 50);
        double ry = 11, rx = 13.5, e = 2.6;
        if (stage == 2) { ry = 8.8; rx = 14.5; e = 2.2; }
        else if (stage == 3) { ry = 6.5; rx = 14; e = 2.0; }
        else if (stage == 4) return Popped(t);
        uint? tint = stage == 3 ? C("#B8B0C0") : stage == 2 ? C("#E8E0E8") : null;
        Body(g, CXB, CY0, rx, ry, e, t, stage == 3 ? tint : null);
        // 반짝
        if (stage < 3) { g.Ell(CXB - rx * .45, CY0 - ry * .42, 1.7, 2.6, White); g.Put((int)(CXB - rx * .45), (int)(CY0 - ry * .42 + 4), White); }
        else g.Put(CXB - 6, CY0 - 2, White);
        // 주름
        uint w = t.S;
        if (stage >= 2)
        {
            g.LineTo(CXB + 4, CY0 - ry + 2, CXB + 8, CY0 - ry + 4, w); g.LineTo(CXB + 1, CY0 - ry + 3, CXB + 4, CY0 - 1, w);
            g.LineTo(CXB - 9, CY0 - 1, CXB - 4, CY0 + 1, w);
        }
        if (stage == 3)
        {
            g.LineTo(CXB + 9, CY0 - 1, CXB + 12, CY0 + 2, w); g.LineTo(CXB - 2, CY0 - ry + 1, CXB - 5, CY0 - 2, w); g.LineTo(CXB + 6, CY0 + 3, CXB + 11, CY0 + 4, w);
        }
        double bot = CY0 + ry;
        Knot(g, CXB, bot, t);
        int top = CY0 - 11;
        int sink = (int)Math.Round(11 - ry);
        g.Outline(SkinKit.Line);
        Str(g, CXB, (int)bot + 4, 12 + (11 - (int)ry) / 2);
        if (stage >= 2)
        {
            // 삐익 하고 새는 바람
            uint a = C("#FFFFFF"), ao = C("#9AB4D4");
            foreach (var (dx, dy) in new[] { (7, 6), (11, 9), (9, 12) }) { g.Put(CXB + dx, (int)bot + dy - 6 - (stage - 2) * 0, a); g.Put(CXB + dx + 1, (int)bot + dy - 6, a); }
            _ = ao;
        }
        return new SkinPx(g, CXB, top + 1, sink);
    }

    // 터진 풍선 조각 (stage 4)
    SkinPx Popped(SkinTone t)
    {
        var g = new Px(52, 50);
        uint y = C("#FFE98A"), w = White;
        // 번쩍
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4 + .2, r0 = 5, r1 = i % 2 == 0 ? 11 : 8;
            g.LineTo(CXB + Math.Cos(a) * r0, CY0 + Math.Sin(a) * r0 * .9, CXB + Math.Cos(a) * r1, CY0 + Math.Sin(a) * r1 * .9, i % 2 == 0 ? y : w);
        }
        g.Ell(CXB, CY0, 3.4, 3, w);
        // 고무 조각
        g.Poly(new[] { (CXB - 14.0, CY0 + 2.0), (CXB - 9.0, CY0 - 3.0), (CXB - 6.0, CY0 + 3.0) }, t.F);
        g.Poly(new[] { (CXB + 8.0, CY0 + 6.0), (CXB + 14.0, CY0 + 1.0), (CXB + 15.0, CY0 + 8.0) }, t.S);
        g.Poly(new[] { (CXB - 4.0, CY0 + 9.0), (CXB + 2.0, CY0 + 8.0), (CXB - 1.0, CY0 + 12.0) }, t.F);
        g.Outline(SkinKit.Line);
        Str(g, CXB, CY0 + 14, 8);
        return new SkinPx(g, CXB, CY0 - 10, 0);
    }

    protected override SkinPx StepR(int v, int stage) => Balloon(Col[v % Col.Length], stage);

    // 약한 발판: 바람 새는 연보라 풍선
    protected override SkinPx WeakR(int stage)
    {
        var g = new Px(52, 50);
        var t = SkinKit.Tone("#CFC6E6", .5, .3);
        double ry = stage == 0 ? 8.6 : 5.6, rx = stage == 0 ? 12 : 12.5;
        Body(g, CXB, CY0 + 2.4, rx, ry, 2.1, t);
        g.Put(CXB - 5, CY0 - 2, White);
        g.LineTo(CXB + 3, (int)(CY0 + 2.4 - ry) + 2, CXB + 7, CY0 + 1, t.S); g.LineTo(CXB - 8, CY0 + 2, CXB - 3, CY0 + 3, t.S);
        // 빈 숨구멍: 작은 구멍 하나
        g.Fill(CXB + 3, CY0 + 1, 6, 5, C("#FFF3D0")); g.LineTo(CXB + 3, CY0 + 1, CXB + 8, CY0 + 5, C("#C98A5A")); g.LineTo(CXB + 8, CY0 + 1, CXB + 3, CY0 + 5, C("#C98A5A"));
        double bot = CY0 + 2.4 + ry;
        Knot(g, CXB, bot, t);
        g.Outline(SkinKit.Line);
        Str(g, CXB, (int)bot + 4, 12, 2);
        foreach (var (dx, dy) in new[] { (9, 3), (13, 1), (12, 6) }) { g.Put(CXB + dx, CY0 + dy, White); g.Put(CXB + dx + 1, CY0 + dy, White); g.Put(CXB + dx + 2, CY0 + dy - 1, C("#C9D8EE")); }
        int top0 = (int)Math.Floor((double)CY0 + 2.4 - 8.6) + 1;
        return new SkinPx(g, CXB, top0, stage == 0 ? 0 : 3);
    }

    public BalloonSkin()
    {
        // 열매: 공기 펌프
        var p = ClimbArt.Glyph(new[]
        {
            "..kkkkkkk..",
            ".....g.....",
            ".....g.....",
            "...bbbbb...",
            "...bBBBb...",
            "...bbbbb...",
            "...bwwwb...",
            "...bbbbb...",
            "...bbbbb...",
            ".sssssssss.",
            ".sssssssss.",
        }, ch => ch switch { 'k' => C("#F0566E"), 'g' => C("#B8C4D8"), 'b' => C("#5AA9F0"), 'B' => C("#BFE0FA"), 'w' => White, 's' => C("#8A94AE"), _ => 0 });
        p = SkinKit.Pad(p, 1); p.Outline(SkinKit.Line);
        Berry = SkinKit.Base(p);

        Save = SkinKit.Base(Star(true));
        SaveHead = new SkinPx(Star(false), 10, 10);
        for (int i = 0; i < 2; i++) Band[i] = ClimbArt.Cloud(i == 0 ? 44 : 40, 12, 31 + i * 7, White, C("#E4ECF5"), C("#A9BCD2"));
        Pip[0] = PipBal(0); Pip[1] = PipBal(1); Pip[2] = PipBal(2); Pip[3] = PipBal(3);
    }

    // 은박 별 풍선 (줍는 것 / 머리 위)
    static Px Star(bool stalk)
    {
        var g = new Px(23, stalk ? 38 : 22);
        var pts = new List<(double X, double Y)>();
        for (int i = 0; i < 10; i++) { double a = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 == 0 ? 9.2 : 4.4; pts.Add((11.5 + Math.Cos(a) * r, 10.8 + Math.Sin(a) * r)); }
        g.Poly(pts.ToArray(), C("#FFD24A"));
        g.Poly(new[] { (11.5, 10.8), pts[0], pts[1], }, C("#FFE98A"));
        g.Poly(new[] { (11.5, 10.8), pts[4], pts[5], pts[6], }, C("#EBA52A"));
        g.Put(10, 6, White); g.Put(10, 7, White); g.Put(9, 8, White);
        g = SkinKit.Pad(g, 1); g.Outline(SkinKit.Line);
        if (stalk) { Str(g, 12, 25, 11, 1); } else Str(g, 12, 24, 4);
        return g;
    }

    static Px PipBal(int st)
    {
        var g = new Px(9, 8);
        uint o = st == 3 ? C("#8C96A0") : SkinKit.Line;
        uint f = st == 0 ? C("#F0566E") : st == 1 ? C("#F28A98") : C("#C9A3AD");
        uint h = st == 0 ? C("#FFB0BC") : st == 1 ? C("#FFC8D0") : C("#E4CCD2");
        string[] a = st switch
        {
            0 => new[] { "..ooooo..", ".ohhfffo.", "ohhffffso", "offffffso", "offffffso", ".offfffo.", "..oosoo..", "....o...." },
            1 => new[] { ".........", "..ooooo..", ".ohhffso.", "offfffso.", "offfffo..", ".oofffo..", "...oso...", "....o...." },
            2 => new[] { ".........", ".........", "..oooo...", ".ohfffoo.", "offfffffo", ".ooosooo.", "....o....", "....o...." },
            _ => new[] { "..ooooo..", ".o.....o.", "o.......o", "o.......o", "o.......o", ".o.....o.", "..ooooo..", "....o...." },
        };
        for (int y = 0; y < 8; y++) for (int x = 0; x < 9; x++) { char ch = a[y][x]; if (ch == 'o') g.Put(x, y, o); else if (ch == 'f') g.Put(x, y, f); else if (ch == 'h') g.Put(x, y, h); else if (ch == 's') g.Put(x, y, st == 3 ? o : SkinKit.Mix(f, C("#5A3048"), .3)); }
        return g;
    }

}
