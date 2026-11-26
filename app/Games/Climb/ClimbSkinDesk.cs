using Px = Bolok.ClimbArt.Px;

namespace Bolok;

// T1 책상 위 문구: 책·지우개·메모 뭉치가 발판. 시간 = 서 있는 발판 옆면에 붙은 쪽지가 말려 떨어진다
sealed class DeskSkin : ClimbSkin
{
    public override ClimbTheme Theme => ClimbTheme.Desk;
    public override int Variants => 6;

    static uint C(string h) => SkinKit.C(h);
    static readonly uint Cream = C("#FFF3E0"), PageLine = C("#E3CFAE"), PageShade = C("#D9C09A"), Gold = C("#F6D55C");
    static readonly uint NoteF = C("#FFE873"), NoteH = C("#FFF6B8"), NoteS = C("#E9C64A"), NoteUnder = C("#FFF9D8");
    static readonly SkinTone[] Cov = { SkinKit.Tone("#EE7C74"), SkinKit.Tone("#6FB4E8"), SkinKit.Tone("#76CBA0"), SkinKit.Tone("#B79CE4") };

    const int X0 = 4, Y0 = 9, BW = 36, BH = 12;   // 몸통 자리 (오른쪽 보기)

    // 앞쪽 옆면에 붙은 쪽지 (stage 1~3)
    static void Note(Px g, int stage)
    {
        int nx = X0 + 10, ny = Y0 + 2, nw = 13, nh = 8;
        if (stage == 1)
        {
            g.Fill(nx, ny, nw, nh, NoteF);
            SkinKit.Row(g, nx, nx + nw - 1, ny, NoteS);          // 풀 띠
            SkinKit.Row(g, nx, nx + nw - 1, ny + 1, NoteH);
            SkinKit.Row(g, nx, nx + nw - 1, ny + nh - 1, NoteS);
            g.Put(nx + nw - 1, ny + nh - 1, 0);
        }
        else if (stage == 2)
        {
            g.Fill(nx, ny, nw, nh, NoteF);
            SkinKit.Row(g, nx, nx + nw - 1, ny, NoteS);
            SkinKit.Row(g, nx, nx + nw - 1, ny + 1, NoteH);
            // 오른쪽 아래 모서리가 말려 올라온다: 모서리를 도려내고 접힌 뒷면을 얹는다
            for (int j = 0; j < 5; j++) for (int i = 0; i < 5; i++) if (i + j >= 4) g.Put(nx + nw - 5 + i, ny + nh - 5 + j, 0);
            for (int j = 0; j < 4; j++) for (int i = 0; i < 4; i++) if (i + j >= 3) g.Put(nx + nw - 4 + i - 1, ny + nh - 4 + j - 1 + 0, NoteUnder);
            g.Put(nx + nw - 5, ny + nh - 1, NoteS);
        }
        else
        {
            // 아래쪽 절반이 앞으로 말려 올라온다 (위 풀 띠만 붙어 있다)
            g.Fill(nx, ny, nw, 4, NoteF);
            SkinKit.Row(g, nx, nx + nw - 1, ny, NoteS);
            SkinKit.Row(g, nx, nx + nw - 1, ny + 1, NoteH);
            g.Fill(nx - 1, ny + 4, nw + 2, 2, NoteUnder);       // 뒤집힌 안쪽
            SkinKit.Row(g, nx - 1, nx + nw, ny + 5, NoteS);
            g.Ell(nx + nw / 2.0 + .5, ny + 6.6, nw / 2.0 + 1.6, 2.1, NoteF);   // 말린 통
            g.Ell(nx + nw / 2.0 + .5, ny + 6.1, nw / 2.0 - 1, .9, NoteH);
            SkinKit.Row(g, nx + 1, nx + nw - 1, ny + 8, NoteS);
        }
    }

    // 쪽지가 떨어져 나가 팔랑이며 내려간다
    static void Fallen(Px g)
    {
        g.Fill(X0 + 3, Y0 + BH + 5, 7, 5, NoteF); SkinKit.Row(g, X0 + 3, X0 + 9, Y0 + BH + 5, NoteH);
        g.Put(X0 + 9, Y0 + BH + 9, 0);
        g.Put(X0 + 1, Y0 + BH + 4, NoteS); g.Put(X0 + 11, Y0 + BH + 7, NoteS);
    }

    SkinPx Book(SkinTone cov, int stage)
    {
        var g = new Px(46, 30);
        int w = BW, h = BH, x0 = X0, y0 = Y0;
        g.Fill(x0, y0, w, 2, cov.F); SkinKit.Row(g, x0, x0 + w - 1, y0, cov.H);
        g.Fill(x0, y0 + h - 2, w, 2, cov.F); SkinKit.Row(g, x0, x0 + w - 1, y0 + h - 1, cov.S);
        g.Fill(x0 + 4, y0 + 2, w - 5, h - 4, Cream);
        SkinKit.Row(g, x0 + 4, x0 + w - 2, y0 + 2, PageShade);
        for (int y = y0 + 4; y < y0 + h - 2; y += 2) SkinKit.Row(g, x0 + 4, x0 + w - 2, y, PageLine);
        g.Fill(x0, y0, 4, h, cov.F); SkinKit.Row(g, x0, x0 + 3, y0, cov.H); SkinKit.Row(g, x0, x0 + 3, y0 + h - 1, cov.S);
        g.Fill(x0 + 3, y0 + 1, 1, h - 2, cov.S);
        g.Fill(x0, y0 + 4, 3, 2, Gold);
        g.Put(x0, y0, 0); g.Put(x0, y0 + h - 1, 0); g.Put(x0 + w - 1, y0, 0); g.Put(x0 + w - 1, y0 + h - 1, 0);
        // 끈 책갈피
        g.Fill(x0 + w - 8, y0 + h, 2, 4, ClimbArt.Red);
        g.Put(x0 + w - 8, y0 + h + 3, 0);
        if (stage >= 1 && stage <= 3) Note(g, stage); else if (stage == 4) Fallen(g);
        return Fin(g, x0 + w / 2, y0);
    }

    SkinPx Eraser(int stage)
    {
        var g = new Px(46, 30);
        int w = BW, h = BH, x0 = X0, y0 = Y0;
        var pink = SkinKit.Tone("#F59BB4");
        SkinKit.Block(g, x0 + 22, y0, w - 22, h, pink, 2);
        uint sl = C("#F4F7FF"), slS = C("#C9D5EC"), blue = C("#5B8FD6");
        g.Fill(x0, y0, 24, h, sl); SkinKit.Row(g, x0, x0 + 23, y0, C("#FFFFFF")); SkinKit.Row(g, x0, x0 + 23, y0 + h - 1, slS);
        g.Fill(x0 + 20, y0, 4, h, slS); g.Fill(x0 + 20, y0, 1, h, sl);
        g.Fill(x0 + 5, y0, 3, h, blue); SkinKit.Row(g, x0 + 5, x0 + 7, y0, C("#8DB3EC")); SkinKit.Row(g, x0 + 5, x0 + 7, y0 + h - 1, C("#3F6EB4"));
        g.Fill(x0 + 11, y0, 2, h, ClimbArt.Red); SkinKit.Row(g, x0 + 11, x0 + 12, y0, C("#F59AA8"));
        g.Put(x0, y0, 0); g.Put(x0, y0 + h - 1, 0);
        // 닳은 모서리 (가루)
        g.Put(x0 + w - 1, y0 + h - 2, 0); g.Put(x0 + w - 1, y0 + 1, 0);
        if (stage >= 1 && stage <= 3) Note(g, stage); else if (stage == 4) Fallen(g);
        return Fin(g, x0 + w / 2, y0);
    }

    SkinPx Pad(int stage)
    {
        var g = new Px(46, 30);
        int w = BW, h = BH, x0 = X0, y0 = Y0;
        var t = SkinKit.Tone("#8FD6C2");
        SkinKit.Block(g, x0, y0, w, h, t, 1);
        for (int y = y0 + 3; y < y0 + h - 1; y += 2) SkinKit.Row(g, x0 + 1, x0 + w - 2, y, t.S);
        SkinKit.Row(g, x0, x0 + w - 1, y0 + 1, t.H);
        // 맨 위 한 장은 살짝 들떠 있다
        g.Fill(x0 + 2, y0 - 1, w - 4, 2, C("#FFC4DA")); SkinKit.Row(g, x0 + 2, x0 + w - 3, y0 - 1, C("#FFE3EE"));
        g.Put(x0 + w - 3, y0, C("#F59AB8"));
        // 풀 띠 (왼쪽)
        g.Fill(x0, y0 + 2, 3, h - 3, SkinKit.Mix(t.F, C("#FFFFFF"), .15));
        if (stage >= 1 && stage <= 3) Note(g, stage); else if (stage == 4) Fallen(g);
        return Fin(g, x0 + w / 2, y0 - 1);
    }

    protected override SkinPx StepR(int v, int stage)
    {
        SkinPx m = v switch { 0 => Book(Cov[0], stage), 1 => Book(Cov[1], stage), 2 => Book(Cov[2], stage), 3 => Book(Cov[3], stage), 4 => Eraser(stage), _ => Pad(stage) };
        return m;
    }

    // 약한 발판: 찢어진 얇은 종이 한 장
    protected override SkinPx WeakR(int stage)
    {
        var g = new Px(46, 30);
        int y0 = 12, x0 = 4, w = 36;
        uint paper = C("#FFFFFF"), edge = C("#C7D3E6"), fold = C("#E6EEF8"), blue = C("#9DB7DC");
        if (stage == 0)
        {
            g.Fill(x0, y0, w, 3, paper); SkinKit.Row(g, x0, x0 + w - 1, y0, C("#F4F8FF"));
            SkinKit.Row(g, x0, x0 + w - 1, y0 + 2, edge);
            // 찢어진 아랫단 톱니
            for (int i = 0; i < w; i += 4) { g.Put(x0 + i, y0 + 3, paper); g.Put(x0 + i + 1, y0 + 3, paper); g.Put(x0 + i + 2, y0 + 3, edge); }
            g.Put(x0 + w - 1, y0, 0); g.Put(x0 + w - 2, y0, 0); g.Put(x0 + w - 1, y0 + 1, 0);
            g.Put(x0, y0, 0);
            // 접힌 모서리와 구멍
            g.Fill(x0 + 1, y0 + 1, 3, 1, fold);
            g.Put(x0 + 14, y0 + 1, blue); g.Put(x0 + 15, y0 + 1, blue);
            g.Put(x0 + 24, y0 + 1, blue); g.Put(x0 + 25, y0 + 1, blue); g.Put(x0 + 26, y0 + 1, blue);
        }
        else
        {
            // 버티는 중: 가운데가 처져 U 자
            for (int i = 0; i < w; i++)
            {
                double u = (i - (w - 1) / 2.0) / ((w - 1) / 2.0);
                int dy = (int)Math.Round((1 - u * u) * 3.5);
                g.Put(x0 + i, y0 + dy, C("#F4F8FF")); g.Put(x0 + i, y0 + dy + 1, paper); g.Put(x0 + i, y0 + dy + 2, edge);
            }
            g.Put(x0 + 12, y0 + 5, C("#E0E8F4")); g.Put(x0 + 22, y0 + 5, C("#E0E8F4"));
        }
        return Fin(g, x0 + w / 2, y0);   // 처짐은 그림만 (볼록이는 안 가라앉는다)
    }

    public DeskSkin()
    {
        // 열매: 커피잔
        var cup = ClimbArt.Glyph(new[]
        {
            "cccccccc....",
            "mMMmmmmmwww.",
            "mMmmmmmmw.ww",
            "mmmmmmmmw..w",
            "mmmmmmmmwww.",
            "mNmmmmmN....",
            ".NNNNNNN....",
        }, ch => ch switch { 'c' => C("#8A5A44"), 'm' => C("#9ADCC0"), 'M' => C("#D2F4E6"), 'N' => C("#6FB89C"), 'w' => C("#FFFFFF"), _ => 0 });
        cup = SkinKit.Pad(cup, 3);
        cup.Outline(SkinKit.Line);
        // 김은 테두리 없이
        var steam = C("#B9D0EA");
        foreach (var (sx, sy) in new[] { (5, 0), (8, 0), (4, 1), (7, 1), (5, 2), (8, 2), (6, 1) }) cup.Put(sx, sy, steam);
        Berry = SkinKit.Base(cup);

        // 한 번 살려 주는 것: 되돌리기 단추 (둥근 파랑 + 되감는 화살표)
        Save = SkinKit.Base(UndoBadge(true));
        SaveHead = new SkinPx(UndoBadge(false), 10, 20);


        // 구름 띠: 겹친 흰 종이 뭉치
        for (int i = 0; i < 2; i++) Band[i] = Papers(i);

        Pip[0] = PipNote(0); Pip[1] = PipNote(1); Pip[2] = PipNote(2); Pip[3] = PipNote(3);
    }

    static Px UndoBadge(bool stalk)
    {
        var g = new Px(21, stalk ? 32 : 21);
        uint b = C("#5B8FD6"), bh = C("#8DB3EC"), bs = C("#3F6EB4");
        double cx = 10, cy = 10, R = 8.6;
        g.Ell(cx, cy, R, R, b);
        g.Ell(cx + .6, cy + 2.6, R - .6, R - 2.6, bs, true);
        g.Ell(cx - .4, cy - .4, R - 1.4, R - 1.6, b, true);
        for (int y = 2; y < 7; y++) for (int x = 3; x < 9; x++) if (g.On(x, y) && (x - cx) * (x - cx) + (y - cy) * (y - cy) > 40) g.Set(x, y, bh);
        // 되돌리기 화살표 (흰색): 위로 돌아 왼쪽 아래를 가리킨다
        uint w = White;
        for (double t = -0.7; t < 3.45; t += .03)
        {
            double r = 4.3, x = cx + Math.Cos(t) * r, y = cy + 0.5 - Math.Sin(t) * r;
            g.Put((int)Math.Floor(x), (int)Math.Floor(y), w); g.Put((int)Math.Floor(x + .6), (int)Math.Floor(y), w); g.Put((int)Math.Floor(x), (int)Math.Floor(y + .6), w);
        }
        double ex = cx + Math.Cos(3.45) * 4.3, ey = cy + .5 - Math.Sin(3.45) * 4.3;
        g.Poly(new[] { (ex - 3.2, ey - .4), (ex + 3.4, ey - .4), (ex + .2, ey + 3.6) }, w);
        g.Outline(SkinKit.Line);
        if (stalk) { for (int i = 0; i < 10; i++) g.Put(10 + (int)Math.Round(Math.Sin(i * .8)), 22 + i, C("#8E7B6B")); }
        return g;
    }

    static Px Papers(int seed)
    {
        var g = new Px(48, 20);
        uint paper = C("#FFFFFF"), lineC = C("#C7D3E6"), shade = C("#E4ECF8");
        for (int k = 2; k >= 0; k--)
        {
            int ox = 2 + k * 3 + (seed == 0 ? 0 : 1), oy = 2 + k * 2;
            g.Fill(ox, oy, 36, 10, k == 0 ? paper : shade);
            SkinKit.Row(g, ox, ox + 35, oy, paper);
            if (k == 0)
            {
                for (int y = oy + 3; y < oy + 9; y += 2) SkinKit.Row(g, ox + 3, ox + 26 - (y % 3) * 4, y, lineC);
                g.Fill(ox + 29, oy + 2, 4, 4, C("#F2D880"));            // 노란 쪽지 한 장
                for (int i = 0; i < 4; i++) g.Put(ox + 35 - i, oy + 9 - i, 0);
                g.Put(ox + 34, oy + 9, 0); g.Put(ox + 35, oy + 8, 0); g.Put(ox + 35, oy + 9, 0);
                for (int i = 0; i < 3; i++) g.Put(ox + 33 - i + 1, oy + 8 - i + 0, shade);
            }
            g.Outline(C("#A9BCD2"));
        }
        return g;
    }

    static Px PipNote(int state)
    {
        uint o = state == 3 ? C("#8C96A0") : SkinKit.Line;
        uint f = state switch { 0 => NoteF, 1 => C("#FFC857"), 2 => C("#E8A04A"), _ => C("#E6EAEE") };
        uint hl = state switch { 0 => NoteH, 1 => C("#FFE29A"), 2 => C("#F5C07A"), _ => C("#F4F6F8") };
        var g = new Px(9, 8);
        string[] a = state switch
        {
            0 => new[] { "ooooooooo", "ohhhhhhho", "offffffff", "offffffff", "offffffff", "offffffff", "offffffo.", "ooooooo.." },
            1 => new[] { "ooooooooo", "ohhhhhhho", "offffffff", "offffffff", "offfffoo.", "offfoHHo.", "offfoHo..", "ooooooo.." },
            2 => new[] { "ooooooooo", "ohhhhhhho", "offffffff", "offfffoo.", "offfooHHo", "ooooHHho.", "..oHHho..", "...ooo..." },
            _ => new[] { "ooooooooo", "o.......o", "o.......o", "o.......o", "o.......o", "o.......o", "o.......o.", "ooooooo.." },
        };
        for (int y = 0; y < 8; y++) for (int x = 0; x < 9; x++) { char ch = a[y][x]; if (ch == 'o') g.Put(x, y, o); else if (ch == 'f') g.Put(x, y, f); else if (ch == 'h') g.Put(x, y, hl); else if (ch == 'H') g.Put(x, y, NoteUnder); }
        return g;
    }

}
