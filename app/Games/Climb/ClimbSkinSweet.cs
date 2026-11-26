using Px = Bolok.ClimbArt.Px;

namespace Bolok;

// T2 과자 탑: 마카롱·샌드 과자가 발판. 시간 = 서 있는 과자의 속 크림이 녹아 흘러내리고 위 과자가 주저앉는다
sealed class SweetSkin : ClimbSkin
{
    public override ClimbTheme Theme => ClimbTheme.Sweet;
    public override int Variants => 6;

    static uint C(string h) => SkinKit.C(h);
    static readonly SkinTone Pink = SkinKit.Tone("#F7A8C4"), Mint = SkinKit.Tone("#9ADCC0"), Lilac = SkinKit.Tone("#C8A8F0"), Choco = SkinKit.Tone("#9A6450", .38, .3), Cookie = SkinKit.Tone("#E8B968");
    static readonly SkinTone CreamW = SkinKit.Tone("#FFF6EA", .5, .12), CreamPink = SkinKit.Tone("#FFD6E4", .5, .15), Jam = SkinKit.Tone("#E5566E", .35, .25);

    const int CX = 24, Y0 = 12;

    // half-widths per row
    static void Layer(Px g, int cx, int y, int[] hw, SkinTone t, bool feetTop = false, bool feetBottom = false, int dx = 0)
    {
        for (int j = 0; j < hw.Length; j++)
        {
            uint c = j == 0 ? t.H : t.F;
            if (j == hw.Length - 1 && feetBottom) c = t.S;
            if (j == 0 && feetTop) c = t.S;
            for (int x = cx - hw[j] + dx; x < cx + hw[j] + dx; x++) g.Put(x, y + j, c);
        }
    }

    // kind 0 마카롱 · 1 초코 샌드 · 2 잼 쿠키
    SkinPx Sandwich(SkinTone shell, SkinTone cream, int kind, int stage)
    {
        var g = new Px(52, 46);
        int[] top, bot; int[] cr;
        if (kind == 0) { top = new[] { 8, 11, 13, 14, 15, 15 }; cr = new[] { 14, 14 }; bot = new[] { 15, 15, 14, 12 }; }
        else if (kind == 1) { top = new[] { 14, 15, 15, 15 }; cr = new[] { 14, 14 }; bot = new[] { 15, 15, 15, 14 }; }
        else { top = new[] { 11, 14, 15, 15, 15 }; cr = new[] { 14, 14 }; bot = new[] { 15, 15, 15, 13 }; }
        int topH = top.Length, crH = cr.Length, botH = bot.Length;
        int s = stage == 2 ? 1 : stage == 3 ? 3 : 0;
        int bulge = stage == 2 ? 1 : stage >= 3 ? 3 : 0;
        int y0 = Y0;
        int creamY = y0 + topH, botY = creamY + crH, endY = botY + botH;
        // 웅덩이 (녹는 중 · 뒤)
        if (stage == 3) g.Ell(CX, endY + 1.2, 19, 2.6, cream.F);
        if (stage == 4)
        {
            g.Ell(CX, endY - 1, 21, 3.4, cream.F);
            g.Ell(CX - 9, endY - 2.5, 7, 2.4, shell.F);
            g.Ell(CX + 9, endY - 2, 6, 2.2, shell.F);
            g.Ell(CX, endY - 3.4, 5, 1.6, cream.H);
            foreach (var (dx, dy) in new[] { (-12, -10), (13, -12), (2, -15), (-4, -7) }) g.Ell(CX + dx, endY + dy, 1.4, 1.4, cream.F);
            foreach (var (dx, dy) in new[] { (-16, -6), (16, -7) }) g.Put(CX + dx, endY + dy, shell.F);
            return Fin(g, CX, endY - 3);
        }
        // 크림 (녹으면 옆으로 불룩)
        for (int j = 0; j < crH + (stage >= 3 ? 1 : 0); j++)
        {
            int hw = cr[Math.Min(j, crH - 1)] + bulge - (j == 0 && stage < 2 ? 0 : 0);
            uint c = j == 0 ? cream.H : cream.F;
            for (int x = CX - hw; x < CX + hw; x++) g.Put(x, creamY + j, c);
            if (bulge > 0) { g.Put(CX - hw - 1, creamY + j, cream.F); g.Put(CX + hw, creamY + j, cream.F); }
        }
        Layer(g, CX, botY, bot, shell, false, true);
        if (kind == 0) for (int x = CX - bot[0]; x < CX + bot[0]; x++) g.Put(x, botY, shell.S);   // 마카롱 치마
        // 흘러내리는 방울
        var drips = stage == 2 ? new[] { (-9, 4), (7, 6) } : stage == 3 ? new[] { (-13, 7), (-6, 11), (3, 6), (11, 9) } : Array.Empty<(int, int)>();
        foreach (var (dx, len) in drips)
        {
            int ty = creamY + 1, ey = endY + len;
            for (int y = ty; y < ey; y++) { g.Put(CX + dx, y, cream.F); g.Put(CX + dx + 1, y, cream.H); }
            g.Ell(CX + dx + .5, ey, 2, 2, cream.F); g.Put(CX + dx, ey - 1, cream.H);
        }
        // 위 과자 (가라앉고 살짝 기움)
        int tdx = stage == 3 ? 2 : 0;
        Layer(g, CX, y0 + s, top, shell, false, kind == 0, tdx);
        if (kind == 0) { for (int x = CX - top[topH - 1] + tdx; x < CX + top[topH - 1] + tdx; x++) g.Put(x, y0 + s + topH - 1, shell.S); }
        // 무늬
        if (kind == 1) { foreach (int x in new[] { -9, -3, 3, 9 }) { g.Put(CX + x + tdx, y0 + s + 1, shell.S); g.Put(CX + x + 2 + tdx, y0 + s + 2, shell.S); } foreach (int x in new[] { -8, -1, 6, 11 }) g.Put(CX + x, botY + 1, shell.S); }
        if (kind == 2) { foreach (var (x, y) in new[] { (-6, 1), (2, 2), (8, 1), (-1, 3) }) { g.Put(CX + x + tdx, y0 + s + y, C("#7A4A32")); g.Put(CX + x + 1 + tdx, y0 + s + y, C("#7A4A32")); } foreach (int x in new[] { -9, -2, 5, 10 }) g.Put(CX + x, botY + 1, C("#7A4A32")); }
        if (kind == 0) { g.Put(CX - 5 + tdx, y0 + s + 1, White); g.Put(CX - 6 + tdx, y0 + s + 2, White); }
        if (stage == 2) { g.Put(CX - 5, y0 + s + 1, White); g.Put(CX - 4, y0 + s + 1, White); g.Put(CX + 5, y0 + s + 2, White); }
        var m = Fin(g, CX, y0 + (kind == 0 ? 1 : 0), s);
        return m;
    }

    protected override SkinPx StepR(int v, int stage) => v switch
    {
        0 => Sandwich(Pink, CreamW, 0, stage),
        1 => Sandwich(Mint, CreamPink, 0, stage),
        2 => Sandwich(Choco, CreamW, 1, stage),
        3 => Sandwich(Cookie, Jam, 2, stage),
        4 => Sandwich(Lilac, CreamW, 0, stage),
        _ => Sandwich(Choco, CreamPink, 1, stage),
    };

    // 약한 발판: 금 간 비스킷
    protected override SkinPx WeakR(int stage)
    {
        var g = new Px(52, 40);
        int y0 = 14, x0 = 8, w = 32, h = 6;
        var t = SkinKit.Tone("#EBCB92", .4, .25);
        SkinKit.Block(g, x0, y0, w, h, t, 1);
        g.Put(x0 + w - 1, y0, 0); g.Put(x0 + w - 2, y0, 0); g.Put(x0 + w - 1, y0 + 1, 0);        // 한 입 베어 문 자리
        g.Put(x0 + w - 3, y0 - 0, 0);
        foreach (var (x, y) in new[] { (5, 1), (9, 3), (20, 2), (26, 3), (14, 4) }) g.Put(x0 + x, y0 + y, C("#C79A5A"));
        uint crack = C("#8A5A3A");
        var path = new[] { (14, 0), (16, 1), (14, 2), (17, 3), (15, 4), (17, 5) };
        for (int i = 0; i < path.Length; i++) g.Put(x0 + path[i].Item1, y0 + path[i].Item2, crack);
        if (stage == 0)
        {
            g.Put(x0 + 28, y0 + h + 2, C("#D7A862")); g.Put(x0 + 3, y0 + h + 3, C("#D7A862"));
            return Fin(g, x0 + w / 2, y0);
        }
        var src = new Px(52, 40);
        src.Blit(g, 0, 0);
        int cutX = x0 + 16;
        var sp = SkinKit.Split(src, y => cutX - (y - y0) % 2 + 0, -1, 3, 1, 2, 4);
        sp.Put(x0 + 4 + 4 + 3, y0 + h + 8, C("#D7A862")); sp.Put(x0 + 24 + 4, y0 + h + 10, C("#D7A862")); sp.Put(x0 + 16 + 4, y0 + h + 11, C("#D7A862"));
        return Fin(sp, x0 + w / 2 + 4, y0 + 4, 2);
    }

    public SweetSkin()
    {
        // 열매: 체리 한 쌍
        var ch = new Px(13, 13);
        uint red = ClimbArt.Red, rh = C("#FF9AAE"), rs = C("#B72E4A"), stem = C("#4E8A3C");
        ch.LineTo(4, 7, 7, 1, stem); ch.LineTo(9, 7, 7, 1, stem);
        ch.Ell(3.5, 9, 3.2, 3, red); ch.Ell(9.5, 9.5, 3.2, 3, red);
        ch.Ell(3.5, 10.5, 2.6, 1.4, rs, true); ch.Ell(9.5, 11, 2.6, 1.4, rs, true);
        ch.Put(2, 8, rh); ch.Put(8, 8, rh); ch.Put(2, 7, White); ch.Put(8, 7, White);
        ch.Poly(new[] { (7.0, 1.0), (11.0, 0.0), (10.0, 3.0) }, C("#6FBE4A"));
        ch = SkinKit.Pad(ch, 1); ch.Outline(SkinKit.Line);
        Berry = SkinKit.Base(ch);

        Save = SkinKit.Base(Cotton(true));
        SaveHead = new SkinPx(Cotton(false), 11, 8);
        for (int i = 0; i < 2; i++) Band[i] = Whip(i);
        Pip[0] = PipMac(0); Pip[1] = PipMac(1); Pip[2] = PipMac(2); Pip[3] = PipMac(3);
    }

    static Px Cotton(bool cone)
    {
        var g = new Px(23, cone ? 34 : 18);
        uint f = C("#FBB3D4"), h = C("#FFDCEB"), s = C("#E88CB8"), l = C("#CDB6F5");
        void Blob(double x, double y, double r) { g.Ell(x, y, r, r * .92, f); }
        Blob(11.5, 8, 6.2); Blob(5.8, 9, 4.6); Blob(17.3, 9, 4.6); Blob(8.5, 4.6, 4); Blob(14.5, 4.6, 4); Blob(11.5, 12.6, 4);
        for (int y = 0; y < 18; y++) for (int x = 0; x < 23; x++) if (g.On(x, y) && y >= 11) g.Set(x, y, s);
        foreach (var (x, y) in new[] { (9, 3), (10, 3), (8, 4), (13, 4), (14, 4), (12, 3) }) g.Put(x, y, h);
        for (int i = 0; i < 9; i++) g.Put(5 + i * 1, 7 + (i % 3 == 1 ? 1 : 0) + i / 4, l);
        g.Put(7, 6, h); g.Put(15, 7, h);
        if (cone)
        {
            // 과자 콘 (격자무늬)
            var c = new Px(23, 34); c.Blit(g, 0, 0);
            g = c;
            g.Poly(new[] { (6.5, 15.0), (16.5, 15.0), (11.5, 30.0) }, C("#EBCB92"));
            for (int k = 0; k < 4; k++) { g.LineTo(7 + k * 2.4, 15.5, 11.5, 15.5 + (5 - Math.Abs(k - 1.5)) * 2.6, C("#C9A262")); g.LineTo(16 - k * 2.4, 15.5, 11.5, 15.5 + (5 - Math.Abs(k - 1.5)) * 2.6, C("#C9A262")); }
            g.Fill(7, 15, 9, 1, C("#F6E0B4"));
        }
        g = SkinKit.Pad(g, 1); g.Outline(SkinKit.Line);
        return g;
    }

    // 구름 띠: 생크림 (알록달록 설탕 알갱이)
    static Px Whip(int seed)
    {
        var g = new Px(50, 22);
        var rnd = new Random(seed * 7 + 3);
        uint cr = C("#FFFFFF"), sh = C("#F3E2EC"), edge = C("#C79DB0");
        for (int i = 0; i < 6; i++) { double x = 6 + i * 7.6 + rnd.NextDouble() * 2, r = 5 + rnd.NextDouble() * 2.5; g.Ell(x, 14 - r * .55, r, r, cr); }
        g.Fill(7, 11, 36, 6, cr);
        for (int y = 0; y < g.H; y++) for (int x = 0; x < g.W; x++) if (g.On(x, y) && y > 13) g.Set(x, y, sh);
        // 소용돌이 홈
        for (int i = 0; i < 4; i++) { g.Put(10 + i * 9, 9 + (i & 1), sh); g.Put(11 + i * 9, 9 + (i & 1), sh); g.Put(12 + i * 9, 10 + (i & 1), sh); }
        foreach (var (x, y, c) in new[] { (9, 6, "#F0506E"), (20, 4, "#5AA9F0"), (31, 5, "#FFD24A"), (38, 8, "#76CBA0"), (15, 11, "#B79CE4"), (27, 10, "#F0506E") }) { g.Put(x, y, C(c)); g.Put(x + 1, y, C(c)); }
        g.Outline(edge);
        return g;
    }

    static Px PipMac(int st)
    {
        var g = new Px(9, 8);
        uint o = st == 3 ? C("#8C96A0") : SkinKit.Line;
        uint f = st == 0 ? C("#F7A8C4") : st == 1 ? C("#F4B4A8") : C("#EFC0A0");
        uint h = st == 0 ? C("#FFD0E0") : st == 1 ? C("#FFD2C4") : C("#FFE0CA");
        uint cr = C("#FFFFFF");
        string[] a = st switch
        {
            0 => new[] { "..ooooo..", ".ohhfffo.", "offfffffo", "occcccco.", "offfffffo", "offfffffo", ".ooooooo.", "........." },
            1 => new[] { "..ooooo..", ".ohhfffo.", "offfffffo", "occcccco.", "offfffffo", "oooofooo.", "...ocoo..", "....o...." },
            2 => new[] { ".........", "..ooooo..", ".ohhffo..", "occccccco", "offfffffo", "oocccccoo", ".ooooooo.", "........." },
            _ => new[] { "..ooooo..", ".o.....o.", "o.......o", "o.......o", "o.......o", "o.......o", ".ooooooo.", "........." },
        };
        for (int y = 0; y < 8; y++) for (int x = 0; x < 9; x++) { char ch = a[y][x]; if (ch == 'o') g.Put(x, y, o); else if (ch == 'f') g.Put(x, y, f); else if (ch == 'h') g.Put(x, y, h); else if (ch == 'c') g.Put(x, y, cr); }
        return g;
    }

}
