using Px = Bolok.ClimbArt.Px;

namespace Bolok;

// 클라이밍 콤보·카페 그림 (docs/climb-combo.md 1-4·5-6, 시안 run/mockups/climb-combo): 조각·도트 숫자 표·카페. 알파 0·255 뿐, 테두리 1칸 Line.
static class FxPaint
{
    public static uint C(string h) => ClimbArt.C(h);
    public static readonly uint Line = ClimbArt.Line, White = ClimbArt.White;
    static uint Mix(uint a, uint b, double t) => ClimbArt.Mix(a, b, t);

    // 단계 색: 1단 크림 · 2단 노랑 · 3단 분홍·하늘 번갈아
    public static readonly uint Cream = C("#FFF3D2"), Yellow = C("#FFD84A"), Pink = C("#FF8FBA"), Sky = C("#7FD0FF");
    public static uint TierCol(int tier, int k = 0) => tier switch { 1 => Cream, 2 => Yellow, _ => (k & 1) == 0 ? Pink : Sky };

    // ── 작은 그림 만들기: 글자 그림 + 색 목록 ("g=#6FC04A,h=#B2E57A") ──
    public static Px Pc(string[] rows, string pal, bool outline = true)
    {
        var map = pal.Split(',').ToDictionary(s => s[0], s => C(s[2..]));
        var g = ClimbArt.Glyph(rows, ch => map.TryGetValue(ch, out var v) ? v : 0);
        var p = new Px(g.W + 2, g.H + 2);
        p.Blit(g, 1, 1);
        if (outline) p.Outline(Line);
        return p;
    }

    // ── 테마별 조각: [단계 1~3][모양] ──
    public static Px[][] Pieces(ClimbTheme th) => th switch
    {
        ClimbTheme.Desk => new[]
        {
            new[] { Pc(new[] { "www", "wwk", "www" }, "w=#FFFFFF,k=#C4D2E6"), Pc(new[] { "ww", "ww" }, "w=#FFFFFF") },
            new[] { Pc(new[] { "pp", "pp", "pP" }, "p=#F7A0C0,P=#E0709A"), Pc(new[] { "bbb", "bBB" }, "b=#8EC9F0,B=#5AA0D8"), Pc(new[] { "gg", "gg", "Gg" }, "g=#B8E08A,G=#8CC060") },
            new[] { Pc(new[] { ".y.", "yYy", ".y." }, "y=#FFE94A,Y=#FFFFFF"), Pc(new[] { ".p.", "pYp", ".p." }, "p=#FF8FD0,Y=#FFFFFF") },
        },
        ClimbTheme.Sweet => new[]
        {
            new[] { Pc(new[] { "ww", "ww" }, "w=#FFFFFF"), Pc(new[] { "w", "w" }, "w=#FFF2F6") },
            new[] { Pc(new[] { "ppp" }, "p=#FF7FB0"), Pc(new[] { "bbb" }, "b=#6FC0F0"), Pc(new[] { "ggg" }, "g=#8CD878"), Pc(new[] { "yyy" }, "y=#FFD84A") },
            new[] { Pc(new[] { ".p.", "pPp", ".p." }, "p=#FF8FB8,P=#FFE4EE"), Pc(new[] { ".l.", "lPl", ".l." }, "l=#B89CF0,P=#F4ECFF") },
        },
        ClimbTheme.Balloon => new[]
        {
            new[] { Pc(new[] { ".b.", "bbb", "bhb" }, "b=#7FC8F8,h=#D8F2FF"), Pc(new[] { "bb", "bh" }, "b=#7FC8F8,h=#D8F2FF") },
            new[] { Pc(new[] { "rr", "rr", "rR" }, "r=#F0506E,R=#C03050"), Pc(new[] { "yyy", "yYY" }, "y=#FFD84A,Y=#E0A820"), Pc(new[] { "bb", "bb", "Bb" }, "b=#6FB0F0,B=#4880C8"), Pc(new[] { "ggg", "gGG" }, "g=#8CD878,G=#58B050") },
            new[] { Pc(new[] { ".s.", "sSs", ".s." }, "s=#B8CCE4,S=#FFFFFF"), Pc(new[] { ".s.", "sYs", ".s." }, "s=#D8DCF0,Y=#FFF2A0") },
        },
        ClimbTheme.Block => new[]
        {
            new[] { Pc(new[] { "tt", "tt" }, "t=#DDB680"), Pc(new[] { "t", "t" }, "t=#C99A60") },
            new[] { Pc(new[] { "hhh", "rrr", "rrr" }, "h=#FF9A9A,r=#E85A5A"), Pc(new[] { "hhh", "bbb", "bbb" }, "h=#8CC0FF,b=#4A90E0"), Pc(new[] { "hhh", "yyy", "yyy" }, "h=#FFEA8A,y=#F5C23A") },
            new[] { Pc(new[] { "hhh", "yYy", "yyy" }, "h=#FFF1A0,y=#F5C23A,Y=#FFFFFF"), Pc(new[] { "hhh", "gYg", "ggg" }, "h=#A8E8A8,g=#4CAF6A,Y=#FFFFFF") },
        },
        _ => new[]   // 덩굴
        {
            new[] { Pc(new[] { ".gh", "ggg", "gg." }, "g=#6FC04A,h=#B2E57A"), Pc(new[] { "hg.", "ggg", ".gg" }, "g=#6FC04A,h=#B2E57A") },
            new[] { Pc(new[] { ".h.", "ppp", ".p." }, "p=#F59AB8,h=#FFD0E0"), Pc(new[] { "hp.", "ppp", ".p." }, "p=#F7A8C4,h=#FFE0EC") },
            new[] { Pc(new[] { ".y.", "yYy", ".y." }, "y=#F7C23A,Y=#FFF6C0"), Pc(new[] { ".y.", "yPy", ".y." }, "y=#F7C23A,P=#FFB0CC") },
        },
    };

    // 반짝 (지나온 발판 위, 3단): 네 갈래
    public static readonly Px Twinkle = Pc(new[] { ".w.", "wYw", ".w." }, "w=#FFFFFF,Y=#FFD84A");
    // 작은 꽃 (덩굴 3단: 지나온 잎에 핀다)
    public static readonly Px Bloom = Pc(new[] { ".p.", "pYp", ".p." }, "p=#FF9CC0,Y=#FFE066");

    // ── 도트 숫자 (글자가 아니라 그림): 작은 3×5, 큰 5×7 (3단). 쪽지는 프레임마다 새 그림을 만들지 않고 이 표에서 바로 칠한다 (ClimbViewFx.PutChip)
    public static readonly string[] F35 =
    {
        "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
        "111100111001111", "111100111101111", "111001001010010", "111101111101111", "111101111001111",
    };
    public static readonly string[] F57 =
    {
        "01110100011001110101100110001001110", "00100011000010000100001000010001110", "01110100010000100110010000100001111", "11110000010000101110000010000111110",
        "00010001100101010010111110001000010", "11111100001111000001000011000101110", "00110010001000011110100011000101110", "11111000010001000100010000100001000",
        "01110100011000101110100011000101110", "01110100011000101111000010001001100",
    };
    public static readonly uint ChipFill = C("#FFFBF0"), GreyFrame = C("#B8B2AA"), GreyInk = C("#8A857E"), GreyFill = C("#ECE8E0");

    // ── 컵 그림 (간판·탁자): 5×5 + 손잡이 ──
    public static Px CupIcon(bool upside = false, bool steam = false)
    {
        var rows = upside
            ? new[] { "wwwww", "wbbbw", "wwwww", "wwwww", ".www." }
            : new[] { "ccccc", "wwwwwh", "wbbbbh", "wwwwwh", ".www." };
        var g = ClimbArt.Glyph(rows, ch => ch switch { 'w' => White, 'c' => C("#7A4A32"), 'b' => C("#6FB0F0"), 'h' => C("#E8DCCB"), _ => 0 });
        var p = new Px(g.W + 2, g.H + 2 + (steam ? 4 : 0));
        p.Blit(g, 1, 1 + (steam ? 4 : 0));
        p.Outline(Line);
        if (steam) { p.Put(2, 1, C("#DCE8F4")); p.Put(3, 0, C("#DCE8F4")); p.Put(5, 2, C("#DCE8F4")); p.Put(4, 1, C("#DCE8F4")); }
        return p;
    }

    // ── 카페 (A2): 36×52, 발판 쪽(왼쪽) 가장자리 위가 발이 닿는 줄(SY), 앉는 자리 x = AX ──
    public const int W = 36, H = 52, SY = 46, AX = 12;
    public const int W4 = 30, H4 = 46, SY4 = 40, AX4 = 10;

    static void Rect(Px g, int x, int y, int w, int h, uint c) { for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) g.Put(i, j, c); }
    static void Tn(Px g, int x, int y, int w, int h, string hex, int r = 1) => SkinKit.Block(g, x, y, w, h, SkinKit.Tone(hex), r);

    static void Lamp(Px g, int x, int y, bool on)
    {
        if (on)
        {
            for (int j = -3; j <= 3; j++) for (int i = -3; i <= 3; i++)
                    if (Math.Abs(i) + Math.Abs(j) is >= 3 and <= 4 && ((i + j) & 1) == 0) g.Put(x + i, y + j, C("#FFF0A0"));
            Rect(g, x - 1, y - 1, 3, 3, C("#FFE45A")); g.Put(x - 1, y - 1, White);
        }
        else Rect(g, x - 1, y - 1, 3, 3, C("#A8A8B0"));
    }

    static void Steam(Px g, int x, int y) { foreach (var (dx, dy) in new[] { (0, 0), (1, -2), (0, -4) }) { g.Put(x + dx, y + dy, C("#FFFFFF")); g.Put(x + dx, y + dy - 1, C("#DCE8F4")); } }

    static void CounterCup(Px g, int x, int y, bool open)   // x,y = 컵 아래 가운데
    {
        var c = CupIcon(!open);
        g.Blit(c, x - c.W / 2, y - c.H + 1);
        if (open) Steam(g, x, y - c.H - 1);
    }

    static Px Dim(Px g)
    {
        for (int i = 0; i < g.P.Length; i++) if (g.P[i] >> 24 != 0 && g.P[i] != Line) g.P[i] = Mix(g.P[i], C("#9A9AA6"), .34);
        return g;
    }

    // 테마마다 카페 하나, 같은 틀 (오른쪽에 선 모습: 발판 쪽이 왼쪽). open=false 는 다녀간 뒤 (불 꺼짐·컵 엎어짐·색이 바램)
    public static Px Cafe(ClimbTheme th, bool open)
    {
        var g = new Px(W, H);
        switch (th)
        {
            case ClimbTheme.Desk: CafeDesk(g, open); break;
            case ClimbTheme.Sweet: CafeSweet(g, open); break;
            case ClimbTheme.Balloon: CafeBalloon(g, open); break;
            case ClimbTheme.Block: CafeBlock(g, open); break;
            default: CafeVine(g, open); break;
        }
        if (!open) Dim(g);
        // 불·컵·김은 흐려진 뒤에 다시 (불 꺼짐은 회색)
        g.Outline(Line);
        return g;
    }

    static void CafeVine(Px g, bool open)
    {
        Tn(g, 0, SY, 36, 5, "#B07A4A");                                    // 통나무 바닥
        for (int x = 3; x < 36; x += 5) { g.Put(x, SY + 2, C("#8A5A38")); g.Put(x + 1, SY + 3, C("#8A5A38")); }
        foreach (int x in new[] { 9, 25 }) { Rect(g, x, SY + 5, 2, 3, C("#5DAA45")); g.Put(x + 2, SY + 7, C("#7CCB4E")); g.Put(x - 1, SY + 6, C("#7CCB4E")); }
        Tn(g, 33, 16, 3, SY - 16, "#FFF3E0", 0);                           // 버섯 기둥
        Rect(g, 35, 17, 1, SY - 17, C("#E8D2B0"));
        // 버섯 지붕
        uint cap = C("#E86A7A"), capH = C("#FF9AA6"), capS = C("#B84A5E");
        for (int y = 0; y <= 16; y++)
            for (int x = 0; x < 36; x++)
            {
                double a = (x + .5 - 19) / 18.0, b = (y + .5 - 14) / 14.5;
                if (a * a + b * b < 1 && y <= 16) g.Put(x, y, y < 3 ? capH : cap);
            }
        for (int x = 0; x < 36; x++) if (g.On(x, 16)) { g.Put(x, 16, capS); g.Put(x, 15, Mix(cap, capS, .4)); }
        foreach (var (cx, cy, r) in new[] { (9, 8, 2.3), (19, 4, 2.0), (28, 9, 2.4), (14, 12, 1.6), (24, 13, 1.5) }) g.Ell(cx, cy, r, r * .85, White, true);
        // 간판: 컵 그림 판이 덩굴 줄에 매달린다
        Rect(g, 2, 17, 1, 4, C("#5DAA45")); Rect(g, 8, 17, 1, 4, C("#5DAA45"));
        Tn(g, 0, 21, 11, 10, "#C99560");
        var ic = CupIcon(!open); g.Blit(ic, 2, 22);
        g.Put(1, 17, C("#7CCB4E")); g.Put(9, 18, C("#7CCB4E")); g.Put(10, 17, C("#7CCB4E"));
        // 탁자: 그루터기
        Tn(g, 25, SY - 8, 9, 8, "#B88A56");
        g.Ell(29.5, SY - 8, 4.6, 1.5, C("#E8C896")); g.Put(29, SY - 8, C("#B88A56")); g.Put(30, SY - 8, C("#B88A56"));
        CounterCup(g, 29, SY - 9, open);
        Lamp(g, 16, 20, open); Rect(g, 16, 17, 1, 2, C("#5DAA45"));
    }

    static void CafeDesk(Px g, bool open)
    {
        Tn(g, 0, SY, 36, 4, "#F2C94C");                                    // 자 (바닥)
        for (int x = 2; x < 36; x += 4) { g.Put(x, SY + 1, C("#B8892A")); if (x % 8 == 2) g.Put(x, SY + 2, C("#B8892A")); }
        foreach (int x in new[] { 2, 33 })                                  // 연필 기둥
        {
            Rect(g, x, 20, 3, SY - 20, C("#F5C842")); Rect(g, x + 2, 20, 1, SY - 20, C("#D9A528")); Rect(g, x, 20, 3, 2, C("#F59AB0"));
        }
        // 줄무늬 종이 차양 (빨강·흰), 물결 가장자리
        uint red = C("#E85A5A"), redS = C("#C03C3C");
        for (int y = 9; y <= 19; y++)
        {
            int inset = y < 11 ? 3 : y < 13 ? 1 : 0;
            for (int x = inset; x < 36 - inset; x++)
            {
                bool r = (x / 4) % 2 == 0;
                if (y >= 18 && ((x / 2) % 2 == 1) && y == 19) continue;       // 물결
                g.Put(x, y, y == 18 || y == 19 ? (r ? redS : C("#D8DCE6")) : r ? red : White);
            }
        }
        // 탁자: 뒤집은 종이컵
        for (int y = 0; y < 10; y++)
        {
            int hw = 5 + y * 3 / 9, cx = 29;
            for (int x = cx - hw; x < cx + hw; x++) g.Put(x, SY - 10 + y, y == 0 ? White : x >= cx + hw - 2 ? C("#E0E6F0") : White);
        }
        Rect(g, 24, SY - 8, 11, 1, red); Rect(g, 23, SY - 4, 13, 1, red);
        for (int x = 23; x < 36; x++) g.Put(x, SY - 10, C("#FFFFFF"));
        CounterCup(g, 29, SY - 11, open);
        // 간판: 메모지 (컵 그림)
        Tn(g, 0, 22, 11, 11, "#FFE76A", 0);
        g.Put(10, 22, 0); g.Put(10, 23, 0); g.Put(9, 22, C("#E8C84A"));
        Rect(g, 3, 20, 5, 2, C("#BFD0E4"));
        var ic = CupIcon(!open); g.Blit(ic, 2, 24);
        Lamp(g, 17, 23, open); g.Put(17, 20, Line);
    }

    static void CafeSweet(Px g, bool open)
    {
        Tn(g, 0, SY, 36, 5, "#D9A55A");                                    // 웨이퍼 바닥
        for (int x = 2; x < 36; x += 4) { g.Put(x, SY + 2, C("#B9863F")); g.Put(x + 2, SY + 3, C("#B9863F")); }
        foreach (int x in new[] { 2, 33 })                                  // 사탕 막대
            for (int y = 20; y < SY; y++) { g.Put(x, y, (y / 3) % 2 == 0 ? C("#E8485C") : White); g.Put(x + 1, y, (y / 3) % 2 == 0 ? C("#C03050") : C("#E8DCE0")); }
        // 크림 지붕 (층층이) + 체리
        g.Ell(19, 14, 18, 5.2, C("#F49DBB")); g.Ell(19, 10, 13, 4.8, C("#FFB9D3")); g.Ell(19, 6.5, 8, 4.2, C("#FFD9E8"));
        for (int x = 3; x < 36; x++) { g.Put(x, 17, C("#E58AAC")); }
        g.Ell(19, 2.4, 2.8, 2.6, C("#E8485C")); g.Put(18, 1, White); Rect(g, 20, 0, 1, 1, C("#5DAA45"));
        foreach (var (x, y, c) in new[] { (9, 13, "#FFFFFF"), (15, 9, "#5FB6EE"), (24, 12, "#FFD84A"), (28, 15, "#7ACB66"), (12, 15, "#FFD84A"), (21, 8, "#FFFFFF"), (30, 13, "#6FC0F0") }) { g.Put(x, y, C(c)); g.Put(x + 1, y, C(c)); }
        // 탁자: 마카롱 쌓기
        Tn(g, 24, SY - 11, 10, 4, "#F08FB0"); Rect(g, 24, SY - 7, 10, 2, White); Tn(g, 24, SY - 5, 10, 5, "#7FCDB4");
        CounterCup(g, 29, SY - 12, open);
        // 간판: 쿠키
        g.Ell(5.5, 27, 5.4, 5.4, C("#D9A55A")); g.Ell(5.5, 27, 4, 4, C("#FFF1DC"));
        Rect(g, 5, 19, 1, 3, C("#E58AAC")); Rect(g, 6, 18, 1, 3, C("#E58AAC"));
        var ic = CupIcon(!open); g.Blit(ic, 2, 24);
        Lamp(g, 17, 22, open); g.Put(17, 19, C("#E58AAC"));
    }

    static void CafeBalloon(Px g, bool open)
    {
        // 바구니: 뒷벽 + 가장자리, 아래 엮은 몸
        Tn(g, 1, SY - 9, 34, 2, "#C48A48", 0);
        for (int y = SY - 7; y < SY; y++) for (int x = 2; x < 34; x++) g.Put(x, y, ((x + y) & 1) == 0 ? C("#C99555") : C("#B9803C"));
        Tn(g, 0, SY, 36, 3, "#B07A3A", 0);
        for (int y = SY + 3; y < SY + 8; y++) for (int x = 3 + (y - SY - 3); x < 33 - (y - SY - 3); x++) g.Put(x, y, ((x + y) & 1) == 0 ? C("#D9A25A") : C("#B9803C"));
        // 풍선
        g.Ell(18, 8.5, 14.5, 8.8, C("#F0506E")); g.Ell(22, 10, 9, 6.5, C("#C93A58"), true);
        g.Ell(18, 8, 13.5, 8, C("#F0506E"), true); for (int y = 1; y < 8; y++) for (int x = 5; x < 14; x++) if (g.On(x, y) && (x + y) % 4 == 0) g.Put(x, y, C("#FF8FA3"));
        g.Ell(9, 5, 1.6, 2.6, White); Rect(g, 17, 16, 3, 2, C("#C93A58")); g.Put(18, 18, C("#C93A58"));
        // 간판: 깃발
        Rect(g, 1, 14, 1, SY - 22, Line);
        Rect(g, 2, 14, 8, 9, C("#FFE76A")); g.Put(9, 14, 0); g.Put(9, 15, 0); g.Put(9, 21, 0); g.Put(9, 22, 0);
        var ic = CupIcon(!open); g.Blit(ic, 2, 15);
        // 통
        Tn(g, 26, SY - 8, 8, 8, "#A86A3C"); Rect(g, 26, SY - 6, 8, 1, C("#6B4220")); Rect(g, 26, SY - 3, 8, 1, C("#6B4220"));
        CounterCup(g, 30, SY - 9, open);
        // 줄 (테두리 없는 가는 줄)
        var ropes = new List<(int, int)>();
        // 불꽃
        if (open) { g.Ell(18.5, 21, 2.4, 3, C("#FF9A3C")); g.Ell(18.5, 21.8, 1.2, 1.8, C("#FFE45A")); }
        else Rect(g, 17, 19, 3, 2, C("#A8A8B0"));
    }

    static void CafeBlock(Px g, bool open)
    {
        Tn(g, 0, SY, 36, 5, "#4CAF6A");                                    // 바닥판 + 돌기
        for (int x = 3; x < 34; x += 5) { Rect(g, x, SY - 1, 3, 1, C("#7ED08E")); }
        // 기둥: 쌓은 블록
        foreach (int x in new[] { 1, 31 })
        {
            Tn(g, x, SY - 8, 5, 8, "#E85A5A"); Tn(g, x, SY - 16, 5, 8, "#4A90E0"); Tn(g, x, SY - 24, 5, 8, "#F5C23A");
        }
        // 차양: 파랑·노랑 블록 줄 + 돌기
        for (int i = 0; i < 4; i++)
        {
            Tn(g, i * 9, 12, 9, 8, i % 2 == 0 ? "#4A90E0" : "#F5C23A");
            Rect(g, i * 9 + 2, 10, 2, 2, i % 2 == 0 ? C("#6FB0F0") : C("#FFD860")); Rect(g, i * 9 + 6, 10, 2, 2, i % 2 == 0 ? C("#6FB0F0") : C("#FFD860"));
        }
        // 간판: 흰 블록 (컵 그림)
        Tn(g, 12, 0, 12, 10, "#FFFFFF");
        var ic = CupIcon(!open); g.Blit(ic, 15, 2);
        // 탁자: 블록 쌓기
        Tn(g, 24, SY - 7, 10, 7, "#E85A5A"); Tn(g, 26, SY - 12, 8, 5, "#F5C23A");
        Rect(g, 27, SY - 13, 2, 1, C("#FFD860")); Rect(g, 31, SY - 13, 2, 1, C("#FFD860"));
        CounterCup(g, 30, SY - 13, open);
        Lamp(g, 17, 24, open); g.Put(17, 21, Line);
    }

    // 점무늬 실루엣: 둘레는 한 줄, 안은 바둑판 (색 열쇠 창이라 반투명 없이 잔상을 만든다)
    public static Px Ghost(Px src, uint col)
    {
        var g = new Px(src.W, src.H);
        for (int y = 0; y < src.H; y++) for (int x = 0; x < src.W; x++)
            {
                if (!src.On(x, y)) continue;
                bool edge = !src.On(x - 1, y) || !src.On(x + 1, y) || !src.On(x, y - 1) || !src.On(x, y + 1);
                if (edge || ((x & 1) == 0 && (y & 1) == 0 && ((x + y) & 2) == 0)) g.Put(x, y, col);
            }
        return g;
    }

    // 창 테두리 덩굴(A4) 카페: 22~30칸 폭 줄인 판. 곁가지 끝에 버섯 지붕 + 작은 간판 + 불
    public static Px CafeA4(bool open)
    {
        var g = new Px(W4, H4);
        Tn(g, 0, SY4, 30, 4, "#B07A4A");
        for (int x = 3; x < 30; x += 5) { g.Put(x, SY4 + 2, C("#8A5A38")); g.Put(x + 1, SY4 + 3, C("#8A5A38")); }
        Rect(g, 6, SY4 + 4, 2, 3, C("#5DAA45")); g.Put(8, SY4 + 6, C("#7CCB4E")); Rect(g, 22, SY4 + 4, 2, 3, C("#5DAA45")); g.Put(24, SY4 + 6, C("#7CCB4E"));
        Tn(g, 27, 14, 3, SY4 - 14, "#FFF3E0", 0); Rect(g, 29, 15, 1, SY4 - 15, C("#E8D2B0"));
        uint cap = C("#E86A7A"), capH = C("#FF9AA6"), capS = C("#B84A5E");
        for (int y = 0; y <= 13; y++) for (int x = 0; x < 30; x++)
            {
                double a = (x + .5 - 15.5) / 15.0, b = (y + .5 - 11.5) / 12.0;
                if (a * a + b * b < 1) g.Put(x, y, y < 3 ? capH : cap);
            }
        for (int x = 0; x < 30; x++) if (g.On(x, 13)) { g.Put(x, 13, capS); g.Put(x, 12, Mix(cap, capS, .4)); }
        foreach (var (cx, cy, r) in new[] { (8, 6, 2.0), (16, 3, 1.8), (23, 7, 2.1), (12, 10, 1.4) }) g.Ell(cx, cy, r, r * .85, White, true);
        Rect(g, 2, 14, 1, 3, C("#5DAA45"));
        Tn(g, 0, 17, 9, 8, "#C99560"); var ic = CupIcon(!open); g.Blit(ic, 1, 18);
        Lamp(g, 15, 17, open); g.Put(15, 14, C("#5DAA45"));
        if (!open) Dim(g);
        g.Outline(Line);
        return g;
    }
}
// 한 판의 콤보·카페 그림 묶음: 판을 열 때 그 테마 것만 구워 둔다 (프레임에서는 찍기만)
sealed class ClimbFxArt
{
    public readonly Px[][] Pieces;                 // [1단·2단·3단][모양]
    public readonly Px[][] Trail;                  // 발밑에 쌓이는 조각 [1·2·3단]: 1단 조각 · 2단 1+2단 조각 · 3단 2+3단 조각
    public readonly Px[] Burst;                    // 5마다 터지는 고리 조각
    public readonly Px Twinkle = FxPaint.Twinkle, Bloom = FxPaint.Bloom;
    readonly Px[,,] ghost = new Px[4, 2, 2];       // 잔상 [크림·노랑·분홍·하늘][오른쪽·왼쪽 보기][걸음 1번·3번] — 볼록이 걸음 그림을 점무늬 실루엣으로
    readonly Px[,] cafe = new Px[2, 2];            // [열림·다녀감][오른쪽·왼쪽]
    readonly int ax, sy;
    public readonly int SeatOff, CafeW, CafeH;     // 볼록이가 앉는 자리: 발판 가운데에서 카페 쪽으로 이만큼

    public ClimbFxArt(ClimbTheme th, ClimbLook look, ClimbPet pet)
    {
        Pieces = FxPaint.Pieces(th);
        Trail = new[] { Pieces[0], Pieces[0].Concat(Pieces[1]).ToArray(), Pieces[1].Concat(Pieces[2]).ToArray() };
        Burst = Pieces[1].Concat(Pieces[2]).ToArray();
        uint[] cols = { FxPaint.Cream, FxPaint.Yellow, FxPaint.Pink, FxPaint.Sky };
        for (int c = 0; c < 4; c++)
            for (int l = 0; l < 2; l++)
                for (int k = 0; k < 2; k++) ghost[c, l, k] = FxPaint.Ghost(pet.Walk.Get(1 + k * 2, l == 1), cols[c]);
        bool a4 = look == ClimbLook.A4;
        for (int o = 0; o < 2; o++)
        {
            var r = a4 ? FxPaint.CafeA4(o == 0) : FxPaint.Cafe(th, o == 0);
            cafe[o, 0] = r; cafe[o, 1] = r.Flipped();
        }
        (ax, sy) = a4 ? (FxPaint.AX4, FxPaint.SY4) : (FxPaint.AX, FxPaint.SY);
        (CafeW, CafeH) = (cafe[0, 0].W, cafe[0, 0].H);
        SeatOff = a4 ? 14 : 28;
    }

    public Px Ghost(int color, bool left, int step) => ghost[color, left ? 1 : 0, step];

    // 카페 그림과 그 안 앉는 자리 (그림 안 x) · 발이 닿는 줄 (그림 안 y): d = 카페가 선 쪽
    public Px Cafe(bool open, int d, out int seatX, out int footY)
    {
        seatX = d < 0 ? CafeW - 1 - ax : ax;
        footY = sy;
        return cafe[open ? 0 : 1, d < 0 ? 1 : 0];
    }

    public IEnumerable<(string, Px)> Gallery()
    {
        for (int t = 0; t < Pieces.Length; t++) for (int i = 0; i < Pieces[t].Length; i++) yield return ($"조각 {t + 1}단/{i}", Pieces[t][i]);
        yield return ("반짝 조각", Twinkle); yield return ("작은 꽃", Bloom);
        for (int c = 0; c < 4; c++) yield return ($"잔상 {c}", ghost[c, 0, 0]);
        for (int o = 0; o < 2; o++) yield return (o == 0 ? "카페 열림" : "카페 다녀감", cafe[o, 0]);
    }
}
