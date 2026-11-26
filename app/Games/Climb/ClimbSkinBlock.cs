using Px = Bolok.ClimbArt.Px;

namespace Bolok;

// T4 장난감 블록: 칠한 원목 블록이 발판. 시간 = 서 있는 블록에 금이 가다 둘로 쪼개진다
sealed class BlockSkin : ClimbSkin
{
    public override ClimbTheme Theme => ClimbTheme.Block;
    public override int Variants => 6;

    static uint C(string h) => SkinKit.C(h);
    static readonly SkinTone Wood = SkinKit.Tone("#F4D9A0", .45, .22);
    static readonly SkinTone[] Col =
    {
        SkinKit.Tone("#EF6B5C", .4, .3), SkinKit.Tone("#5D9BE8", .4, .3), SkinKit.Tone("#F6C945", .45, .3), SkinKit.Tone("#61C28A", .4, .3), SkinKit.Tone("#FF9F4A", .4, .3), SkinKit.Tone("#A98BF0", .4, .3),
    };
    static readonly uint CrackC = C("#6B4A3A");
    const int X0 = 5, Y0 = 10, BW = 34, BH = 14;   // 윗면 4줄 + 앞면 10줄

    static void Top(Px g, int x0, int y0, int w, uint hi, uint face, uint edge)
    {
        int[] inset = { 3, 2, 1, 0 };
        for (int r = 0; r < 4; r++) SkinKit.Row(g, x0 + inset[r], x0 + w - 1 - inset[r], y0 + r, r == 0 ? hi : r == 3 ? edge : face);
    }

    // kind 0 별 · 1 구멍 셋 · 2 아치 · 3 둥근 기둥 · 4 작은 블록 둘 · 5 줄무늬
    Px Make(int v)
    {
        var g = new Px(60, 36);
        var t = Col[v % Col.Length];
        int x0 = X0, y0 = Y0, w = BW;
        if (v == 4)
        {
            var t2 = Col[(v + 3) % Col.Length];
            foreach (var (xx, tt) in new[] { (x0, t), (x0 + 18, t2) })
            {
                SkinKit.Block(g, xx, y0 + 4, 16, 10, tt, 1);
                Top(g, xx, y0, 16, Wood.H, Wood.F, Wood.S);
                g.Put(xx + 7, y0 + 8, tt.S); g.Put(xx + 8, y0 + 8, tt.S); g.Put(xx + 7, y0 + 9, tt.H); g.Put(xx + 8, y0 + 9, tt.H);
                g.Fill(xx + 14, y0 + 5, 2, 8, tt.S);
            }
            return g;
        }
        if (v == 3)
        {
            SkinKit.Block(g, x0, y0 + 3, w, 11, t, 3);
            g.Ell(x0 + w / 2.0, y0 + 2.2, w / 2.0 - .6, 2.8, Wood.F);
            SkinKit.Row(g, x0 + 5, x0 + w - 6, y0 + 0, Wood.H);
            for (int y = y0 + 4; y < y0 + BH - 1; y++) { g.Put(x0 + 3, y, t.H); g.Put(x0 + w - 3, y, t.S); g.Put(x0 + w - 4, y, t.S); }
            g.Fill(x0 + 10, y0 + 6, 2, 6, t.H); g.Fill(x0 + 21, y0 + 6, 2, 6, t.S);
            return g;
        }
        SkinKit.Block(g, x0, y0 + 4, w, 10, t, 1);
        Top(g, x0, y0, w, Wood.H, Wood.F, Wood.S);
        g.Fill(x0 + w - 2, y0 + 5, 2, 8, t.S);
        int cx = x0 + w / 2;
        if (v == 0)
        {
            var star = new[] { (cx, y0 + 6), (cx + 1, y0 + 6), (cx - 1, y0 + 7), (cx, y0 + 7), (cx + 1, y0 + 7), (cx + 2, y0 + 7), (cx - 2, y0 + 8), (cx - 1, y0 + 8), (cx, y0 + 8), (cx + 1, y0 + 8), (cx + 2, y0 + 8), (cx + 3, y0 + 8), (cx - 1, y0 + 9), (cx, y0 + 9), (cx + 1, y0 + 9), (cx + 2, y0 + 9), (cx - 1, y0 + 10), (cx + 2, y0 + 10), (cx - 2, y0 + 11), (cx + 3, y0 + 11) };
            foreach (var (x, y) in star) g.Put(x, y, C("#FFF4D6"));
        }
        else if (v == 1)
        {
            foreach (int dx in new[] { -9, 0, 9 }) { g.Fill(cx + dx - 1, y0 + 8, 3, 3, t.S); g.Fill(cx + dx - 1, y0 + 8, 3, 1, SkinKit.Mix(t.S, C("#000000"), .2)); g.Put(cx + dx, y0 + 11, t.H); }
        }
        else if (v == 2)
        {
            g.Ell(cx, y0 + 9, 5, 4.4, t.H);
            g.Ell(cx + .5, y0 + 9.5, 3.7, 3.3, SkinKit.Mix(t.S, C("#2A1A28"), .45));
            g.Put(cx - 2, y0 + 8, White); g.Put(cx - 1, y0 + 7, White);
        }
        else
        {
            for (int dx = -w / 2 + 3; dx < w / 2 - 2; dx += 6) g.Fill(cx + dx, y0 + 5, 3, 8, t.H);
        }
        return g;
    }

    static (int X, int Y)[] CrackPath(int cx, int y0) => new[] { (cx + 3, y0), (cx + 2, y0 + 1), (cx + 1, y0 + 2), (cx + 2, y0 + 3), (cx + 3, y0 + 4), (cx + 2, y0 + 5), (cx + 1, y0 + 6), (cx + 2, y0 + 7), (cx + 4, y0 + 8), (cx + 3, y0 + 9), (cx + 2, y0 + 10), (cx + 3, y0 + 11), (cx + 4, y0 + 12), (cx + 3, y0 + 13) };

    static int CrackX(int cx, int y0, int y)
    {
        var p = CrackPath(cx, y0);
        int idx = Math.Clamp(y - y0, 0, p.Length - 1);
        return p[idx].X;
    }

    protected override SkinPx StepR(int v, int stage)
    {
        var g = Make(v);
        int cx = X0 + BW / 2;
        int ay = Y0 + 2;
        if (stage == 2)
        {
            foreach (var (x, y) in CrackPath(cx, Y0)) g.Put(x, y, CrackC);
            // 떨어져 나가는 조각
            var t = Col[v % Col.Length];
            for (int j = 0; j < 3; j++) for (int i = 0; i < 3 - j; i++) g.Set(X0 + BW - 3 + i, Y0 + 4 + j, 0);
            g.Fill(X0 + BW + 3, Y0 - 3, 3, 3, t.F); g.Fill(X0 + BW + 3, Y0 - 3, 3, 1, t.H); g.Put(X0 + BW + 5, Y0 - 1, t.S);
            return Fin(g, cx, ay);
        }
        if (stage == 3)
        {
            var sp = SkinKit.Split(g, y => CrackX(cx, Y0, y) + (y >= Y0 ? 0 : 0), -3, 2, 3, 1, 6);
            // 갈라진 단면에 금 그림자
            foreach (var (x, y) in CrackPath(cx, Y0)) { sp.Put(x + 6 - 2 - 1, y + 6 + 2, CrackC); sp.Put(x + 6 + 2 + 1, y + 6 + 1, CrackC); }
            // 먼지
            foreach (var (dx, dy) in new[] { (-6, 15), (0, 16), (7, 15), (12, 13), (-12, 13) }) { sp.Put(cx + 6 + dx, Y0 + 6 + dy, White); sp.Put(cx + 6 + dx + 1, Y0 + 6 + dy, C("#E4E0DA")); }
            return Fin(sp, cx + 6, ay + 6);
        }
        if (stage == 4)
        {
            var e = new Px(60, 36);
            var t = Col[v % Col.Length];
            foreach (var (dx, dy, s) in new[] { (-13, 8, 5), (-4, 17, 4), (6, 11, 6), (14, 19, 4), (-7, 2, 4), (2, 3, 3) })
            {
                e.Fill(cx + dx, Y0 + dy, s, s - 1, t.F); e.Fill(cx + dx, Y0 + dy, s, 1, t.H); e.Fill(cx + dx, Y0 + dy + s - 2, s, 1, t.S);
            }
            foreach (var (dx, dy) in new[] { (-15, 4), (16, 8), (0, 21), (10, 3), (-10, 15) }) e.Ell(cx + dx, Y0 + dy, 2.2, 2, White);
            e.Outline(SkinKit.Line);
            return new SkinPx(e, cx, ay, 0);
        }
        return Fin(g, cx, ay);
    }

    // 약한 발판: 삭은 헌 블록 (회갈색, 모서리가 떨어져 나가고 못이 튀어나옴)
    protected override SkinPx WeakR(int stage)
    {
        var g = new Px(60, 40);
        int x0 = X0, y0 = Y0, w = BW;
        var t = SkinKit.Tone("#BDB69E", .35, .3);
        SkinKit.Block(g, x0, y0 + 4, w, 10, t, 1);
        Top(g, x0, y0, w, C("#E2DAC0"), C("#D2C8A8"), C("#A89E80"));
        // 떨어져 나간 모서리 · 옹이 · 못
        for (int j = 0; j < 4; j++) for (int i = 0; i < 4 - j; i++) g.Set(x0 + w - 1 - i, y0 + 4 + j + 0, 0);
        for (int i = 0; i < 3; i++) g.Set(x0, y0 + 13 - i, 0);
        g.Fill(x0 + 6, y0 + 8, 3, 2, t.S); g.Put(x0 + 7, y0 + 8, C("#7A6E58"));
        g.Fill(x0 + 21, y0 + 7, 2, 2, C("#8A8A94")); g.Put(x0 + 21, y0 + 7, C("#D0D0DA"));
        uint ck = C("#7A6E58");
        for (int i = 0; i < 9; i++) g.Put(x0 + 14 + (i % 3 == 1 ? 1 : 0) - (i / 5), y0 + 4 + i, ck);
        int cx = x0 + w / 2;
        if (stage == 0) return Fin(g, cx, y0 + 2);
        var sp = SkinKit.Split(g, y => x0 + 14 - (y - y0 - 4) / 5, -1, 3, 1, 2, 4);
        return Fin(sp, cx + 4, y0 + 2 + 4);
    }

    public BlockSkin()
    {
        // 열매: 건전지
        var b = ClimbArt.Glyph(new[]
        {
            "..ssss..",
            "..yyyy..",
            ".gggggg.",
            ".ggwwgg.",
            ".ggwwgg.",
            ".wwwwww.",
            ".wwwwww.",
            ".ggwwgg.",
            ".ggwwgg.",
            ".gggggg.",
            ".GGGGGG.",
            ".GGGGGG.",
        }, ch => ch switch { 's' => C("#C8CEDA"), 'y' => C("#F6C945"), 'g' => C("#61C28A"), 'G' => C("#3F9A66"), 'w' => White, _ => 0 });
        b = SkinKit.Pad(b, 1); b.Outline(SkinKit.Line);
        Berry = SkinKit.Base(b);

        Save = SkinKit.Base(Pinwheel(true));
        SaveHead = new SkinPx(Pinwheel(false), 11, 10);
        for (int i = 0; i < 2; i++)
        {
            var c = ClimbArt.Cloud(i == 0 ? 44 : 40, 12, 41 + i * 5, White, C("#EAE2F6"), C("#B5A8D6"));
            for (int x = 8; x < c.W - 8; x += 6) { c.Put(x, 9 + ((x / 6) & 1), C("#CFC4EA")); c.Put(x + 1, 9 + ((x / 6) & 1), C("#CFC4EA")); c.Put(x + 2, 9 + ((x / 6) & 1), C("#CFC4EA")); }
            Band[i] = c;
        }
        Pip[0] = PipBlk(0); Pip[1] = PipBlk(1); Pip[2] = PipBlk(2); Pip[3] = PipBlk(3);
    }

    static Px Pinwheel(bool stalk)
    {
        var g = new Px(25, stalk ? 38 : 22);
        double cx = 12, cy = 11;
        uint[] cols = { C("#EF6B5C"), C("#F6C945"), C("#5D9BE8"), C("#61C28A") };
        for (int k = 0; k < 4; k++)
        {
            double a = -Math.PI / 4 + k * Math.PI / 2;
            double ax = cx + Math.Cos(a) * 10, ay = cy + Math.Sin(a) * 10;
            double bx = cx + Math.Cos(a + Math.PI / 2) * 10, by = cy + Math.Sin(a + Math.PI / 2) * 10;
            double mx = cx + Math.Cos(a + Math.PI / 4) * 5.6, my = cy + Math.Sin(a + Math.PI / 4) * 5.6;
            g.Poly(new[] { (cx, cy), (ax, ay), (mx, my) }, cols[k]);
            g.Poly(new[] { (cx, cy), (mx, my), (bx, by) }, SkinKit.Mix(cols[k], C("#5A3048"), .22));
        }
        g.Ell(cx, cy, 1.8, 1.8, White);
        g = SkinKit.Pad(g, 1); g.Outline(SkinKit.Line);
        if (stalk) { for (int y = 24; y < 36; y++) { g.Put(13, y, C("#C9A062")); g.Put(14, y, C("#9A7440")); } }
        return g;
    }

    static Px PipBlk(int st)
    {
        var g = new Px(9, 8);
        uint o = st == 3 ? C("#8C96A0") : SkinKit.Line;
        uint f = st == 0 ? C("#EF6B5C") : st == 1 ? C("#E8897C") : C("#D4A09A");
        uint h = st == 0 ? C("#F4D9A0") : st == 1 ? C("#F4D9A0") : C("#EAD8B8");
        string[] a = st switch
        {
            0 => new[] { "..ooooooo", ".ohhhhho.", "ooooooo.o", "offfffo.o", "offfffo.o", "offfffoo.", "ooooooo..", "........." },
            1 => new[] { "..ooooooo", ".ohhhhho.", "ooooooo.o", "offfoffo.", "offofffo.", "offfoffo.", "ooooooo..", "........." },
            2 => new[] { "..oooo.oo", ".ohhho.ho", "oooooo.oo", "offffo.fo", ".offo..fo", "oofoo.ofo", ".ooo..oo.", "........." },
            _ => new[] { "..ooooooo", ".o.....o.", "ooooooo.o", "o.....o.o", "o.....o.o", "o.....oo.", "ooooooo..", "........." },
        };
        for (int y = 0; y < 8; y++) for (int x = 0; x < 9; x++) { char ch = a[y][x]; if (ch == 'o') g.Put(x, y, o); else if (ch == 'f') g.Put(x, y, f); else if (ch == 'h') g.Put(x, y, h); }
        return g;
    }

}
