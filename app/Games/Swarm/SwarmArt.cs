using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Bolok;

// 버그 떼 모듈 그림: 무기 일곱 자루(손에 든 그림·머리 위 그림)와 레벨 업 갈래 그림 셋. 글자 한 칸 = 색 하나 (. 은 투명).
// 모두 오른쪽을 보는 그림이고 왼쪽은 뒤집어 쓴다 (돌리지 않는다: 작은 도트가 뭉개진다). 테두리는 펫과 같은 진한 갈색.
// 배율마다 한 번만 만들어 둔다.
// 단계 표시(깃발·위 화살표)와 왕관 색 바꿈·엘리트 테두리도 여기서 만든다 (docs/swarm-levels.md 3·5장)
// 특수기 그림(날아가는 칼 네 장·새끼 폭탄·불 자리 두 장·밝은 무기 판)도 여기서 (docs/swarm-specials.md 8장)
// 수류탄 그림(몸·날아가는 네 장·그림자)도 여기서 (docs/swarm-grenade.md 5장): sprites.png 의 game/grenade 는 미니건 것이라 쓰지 않는다 (게임 모듈끼리 매이지 않게)
static class SwarmArt
{
    public static readonly Dictionary<char, Color> Colors = new()
    {
        ['o'] = Color.FromArgb(0x5C, 0x3A, 0x28),   // 테두리
        ['w'] = Color.FromArgb(0xFF, 0xFF, 0xFF), ['l'] = Color.FromArgb(0xD6, 0xDA, 0xE2), ['g'] = Color.FromArgb(0x9A, 0xA0, 0xAC), ['d'] = Color.FromArgb(0x5E, 0x64, 0x70),   // 쇠: 밝은 쪽부터
        ['b'] = Color.FromArgb(0x9A, 0x62, 0x38), ['t'] = Color.FromArgb(0xD0, 0x9A, 0x5C),   // 나무
        ['y'] = Color.FromArgb(0xFA, 0xB9, 0x28), ['n'] = Color.FromArgb(0xFF, 0xAA, 0x32), ['r'] = Color.FromArgb(0xE8, 0x48, 0x5C),   // 금·주황·빨강
        ['e'] = Color.FromArgb(0x7E, 0x9C, 0x52), ['f'] = Color.FromArgb(0x55, 0x73, 0x3A),   // 국방색
        ['c'] = Color.FromArgb(0x50, 0xD6, 0xE2),
    };

    // ── 손에 든 그림 (Arm.Id 순서). 쌍검·쌍권총은 한 자루만 그려 두고 양손에 하나씩 든다 ──
    static readonly string[][] Hands =
    {
        new[] // 쌍검: 짧은 칼 한 자루
        {
            ".....oo",
            "....owo",
            ".o.owlo",
            ".yowlo.",
            ".oylo..",
            ".oboyo.",
            "obo.o..",
            "oo.....",
        },
        new[] // 장검
        {
            ".........oo",
            "........owo",
            ".......owlo",
            "......owlo.",
            ".....owlo..",
            ".o..owlo...",
            ".yoowlo....",
            ".oyylo.....",
            "..oyyo.....",
            ".obooyo....",
            "obo..o.....",
            "oo.........",
        },
        new[] // 쌍권총: 권총 한 자루
        {
            "ooooooooo",
            "ogggggglo",
            "oddoooooo",
            "obo.o....",
            "obo......",
            "ooo......",
        },
        new[] // 산탄총: 긴 총열 아래 나무 덧손잡이
        {
            "..oooooooooooooo",
            "oogggggggggggglo",
            "obbddddottttoooo",
            "obboooooooooo...",
            "ooo.............",
        },
        new[] // 돌격소총: 휜 탄창과 가늠쇠
        {
            "............o....",
            "ooooooooooooooooo",
            "obbdddddddgttgglo",
            "obboooodooooooooo",
            "ooo..obooddo.....",
            ".....obo.oddo....",
            ".....ooo..odo....",
            "..........ooo....",
        },
        new[] // 바주카포: 굵은 국방색 통
        {
            ".....oo.........",
            "oo..oooooooooooo",
            "ofoooeeeeeeeeedo",
            "offeefeeeeeeeedo",
            "ofoooffffffffffo",
            "oo..oooobooooooo",
            ".......obo......",
            ".......ooo......",
        },
        new[] // 화염방사기: 빨간 연료통과 끝이 벌어진 관
        {
            ".ooo...........",
            "orrro......oo..",
            "ornroooooooodo.",
            "orrrgggggggggdo",
            "ornroooboooodo.",
            "orrro.obo..oo..",
            ".ooo..ooo......",
        },
    };

    // ── 레벨 업 갈래 그림 (Swarm.Up 순서: 빠르게 » · 넓게 ↔ · 세게 ★) ──
    static readonly string[][] Ups =
    {
        new[]
        {
            "oo...oo....",
            "oyo..oyo...",
            ".oyo..oyo..",
            "..oyo..oyo.",
            ".oyo..oyo..",
            "oyo..oyo...",
            "oo...oo....",
        },
        new[]
        {
            "...o.....o...",
            "..oco...oco..",
            ".occooooocco.",
            "occccccccccco",
            ".occooooocco.",
            "..oco...oco..",
            "...o.....o...",
        },
        new[]
        {
            "....o....",
            "...oro...",
            "...oro...",
            "oooorrooo",
            "orrrrrrro",
            ".orrrrro.",
            "..orrro..",
            ".orrorro.",
            ".oro.oro.",
            ".oo...oo.",
        },
    };

    // ── 수류탄: 왼쪽 금 고리(핀), 쇠 뚜껑, 오른쪽으로 내려오는 손잡이, 띠 두른 국방색 몸. 줍는 것·표시·머리 위가 같이 쓴다 ──
    static readonly string[] NadeRows =
    {
        "..oo.ooo.",
        "oyyooglo.",
        "oyo.odgoo",
        ".o.oooodo",
        "..oeeeeoo",
        ".oewefeeo",
        ".oeeefeeo",
        ".offffffo",
        ".oeeefeeo",
        "..oeefeo.",
        "...oooo..",
    };

    // 날아가는 수류탄 (뚜껑이 위): 글자 격자를 90° 씩 돌려 네 장 (비트맵을 돌리지 않으니 뭉개지지 않는다)
    static readonly string[] FlyRows =
    {
        "..ooo..",
        "..olo..",
        ".ooooo.",
        "oeweeeo",
        "oeeefeo",
        "oeeffeo",
        ".ooooo.",
    };

    // 그림자 (바닥, 점무늬라 반투명이 필요 없다): 낮게 날 때 큰 것, 높이가 꼭대기의 절반을 넘으면 작은 것
    static readonly string[] ShadeBig = { ".d.d.d.", "d.d.d.d", ".d.d.d." };
    static readonly string[] ShadeSmall = { ".d.d.", "d.d.d" };

    // ── 특수기 (글자 없음): 날아가는 칼 (7×7 비스듬히, 90° 씩 네 장) · 새끼 폭탄 (5×5) · 불 자리 (15×15, 테두리 없이 성긴 불씨라 밑의 보석·벌레가 보인다) ──
    static readonly string[] BladeRows =
    {
        ".....oo",
        "....owo",
        "...owlo",
        "..owlo.",
        "ooblo..",
        "obbo...",
        ".oo....",
    };
    static readonly string[] BombRows =
    {
        ".ooo.",
        "oeweo",
        "oeefo",
        "offfo",
        ".ooo.",
    };
    static readonly string[] FireRows =
    {
        "......n........",
        ".....nyn...n...",
        "..n..nyn..nyn..",
        ".nyn..n...nyn..",
        ".nyn.......n...",
        "..n....r.......",
        "......ryr...n..",
        "...n..ryr..nyn.",
        "..nyn..r...nyn.",
        "..nyn.......n..",
        "...n....n......",
        ".......nyn..r..",
        "....r..nyn.ryr.",
        "...ryr..n...r..",
        "....r..........",
    };

    // 줄을 시계 방향으로 90° 돌린다 (정사각형 격자)
    static string[] Rot(string[] rows)
    {
        int n = rows.Length;
        var turned = new string[n];
        for (int y = 0; y < n; y++)
        {
            var c = new char[n];
            for (int x = 0; x < n; x++) c[x] = rows[n - 1 - x][y];
            turned[y] = new string(c);
        }
        return turned;
    }

    // 다시 뽑기 표시: 오른쪽 위가 화살촉인 고리 (첫 장) 와 90° 돌린 것 (0.25초마다 번갈아). 9×9
    static readonly string[] SwapRows =
    {
        "..ooooo.o",
        ".oyyyyyoo",
        "oyyoooyyo",
        "oyo..oyyo",
        "oyo..oooo",
        "oyo...oyo",
        "oyyoooyyo",
        ".oyyyyyo.",
        "..ooooo..",
    };
    static readonly string[][] SwapFrames = { SwapRows, Rot(SwapRows) };
    public static Stamp Swap(int frame, int scale) => Peek(41 + (frame & 1), scale) ?? Store(41 + (frame & 1), scale, Make(SwapFrames[frame & 1], scale));

    // 강철 딱정벌레 (5단계부터): 딱정벌레 그림(game/beetle)의 초록 넷을 쇠 색으로. 밝은 등 무늬(#B0E296)가 광택 노릇. 눈은 그대로
    static uint SteelMap(uint p) => p switch
    {
        0xFF224A2C => 0xFF3A3F4A, 0xFF40864A => 0xFF5E6470, 0xFF68B860 => 0xFF9AA0AC, 0xFFB0E296 => 0xFFD6DAE2, _ => p,
    };
    public static Stamp Steel(Stamp src, int frame, int scale) => Peek(70 + frame, scale) ?? Store(70 + frame, scale, Remake(src, 0, SteelMap, 0));

    static readonly string[][] BladeFlyRows = BuildBlade();
    static string[][] BuildBlade()
    {
        var all = new string[4][];
        all[0] = BladeRows;
        for (int i = 1; i < 4; i++) all[i] = Rot(all[i - 1]);
        return all;
    }

    // 불 자리 둘째 장: 좌우로 뒤집고 노랑·주황을 맞바꾼다 (0.16초마다 번갈아 일렁인다)
    static string[] FireTwin()
    {
        var rows = Flip(FireRows);
        for (int i = 0; i < rows.Length; i++) rows[i] = rows[i].Replace('n', 'x').Replace('y', 'n').Replace('x', 'y');
        return rows;
    }

    static readonly string[][] Fly = BuildFly();
    static string[][] BuildFly()
    {
        var all = new string[4][];
        all[0] = FlyRows;
        for (int i = 1; i < 4; i++) all[i] = Rot(all[i - 1]);
        return all;
    }

    // 밝은 판(번쩍): 국방색 → 흰 쇠. 회색 판(0개·빈 손): 국방색·금 → 쇠
    static Color Bright(char ch) => ch switch { 'e' => Colors['w'], 'f' => Colors['l'], _ => Colors[ch] };
    static Color Gray(char ch) => ch switch { 'e' => Colors['g'], 'f' => Colors['d'], 'y' => Colors['l'], _ => Colors[ch] };

    public static Stamp Nade(int scale) => Peek(100, scale) ?? Store(100, scale, Make(NadeRows, scale));
    public static Stamp NadeBright(int scale) => Peek(101, scale) ?? Store(101, scale, Make(NadeRows, scale, Bright));
    public static Stamp NadeGray(int scale) => Peek(102, scale) ?? Store(102, scale, Make(NadeRows, scale, Gray));
    public static Stamp NadeFly(int frame, int scale) => Peek(110 + (frame & 3), scale) ?? Store(110 + (frame & 3), scale, Make(Fly[frame & 3], scale));
    public static Stamp NadeShade(bool small, int scale) => Peek(small ? 121 : 120, scale) ?? Store(small ? 121 : 120, scale, Make(small ? ShadeSmall : ShadeBig, scale));

    // 밝은 판(특수기 번쩍): 테두리만 두고 나머지 색을 흰색 쪽으로 70% 섞는다
    static Color Light(char ch) => ch == 'o' ? Colors['o'] : Mix(Colors[ch], Color.White, .7);
    public static Stamp HandBright(Arm.Id id, int scale, bool left) => Peek(130 + (int)id + (left ? 10 : 0), scale) ?? Store(130 + (int)id + (left ? 10 : 0), scale, Make(left ? Flip(Hands[(int)id]) : Hands[(int)id], scale, Light));
    public static Stamp IconBright(Arm.Id id, int scale) => Peek(150 + (int)id, scale) ?? Store(150 + (int)id, scale, Make(IconRows(id), scale, Light));
    public static Stamp BladeFly(int frame, int scale) => Peek(160 + (frame & 3), scale) ?? Store(160 + (frame & 3), scale, Make(BladeFlyRows[frame & 3], scale));
    public static Stamp Bomb(int scale) => Cached(170, scale, BombRows);
    public static Stamp FireFloor(int frame, int scale) => Peek(180 + (frame & 1), scale) ?? Store(180 + (frame & 1), scale, Make((frame & 1) == 0 ? FireRows : FireTwin(), scale));

    // ── 흙더미 (가만히 있기 막기): 땅에서 벌레가 올라오기 0.8초 전부터 셋(펴짐 → 갈라짐 → 부풀어 떨림), 올라온 직후 흙덩이가 튀는 둘. 모두 15×11 (아래가 땅)
    static readonly string[][] MoundRows =
    {
        new[]
        {
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "...............",
            "......ooo......",
            "....oobttboo...",
            "...obbbttbbbo..",
            "...oobbbbbboo..",
            ".....oooooooo..",
        },
        new[]
        {
            "...............",
            "...............",
            "...............",
            "...............",
            ".......o.......",
            ".....oootooo...",
            "....obbtttbbo..",
            "...obbbtoobbbo.",
            "...obbbbobbbbo.",
            "..obbbbbbbbbbbo",
            "...ooooooooooo.",
        },
        new[]
        {
            ".....g....g....",
            "......o.o..g...",
            ".....ooooo.....",
            "....obtttbo....",
            "...obtttttbbo..",
            "..obbtoootbbbo.",
            "..obbtobobbbbo.",
            ".obbbbobbobbbbo",
            ".obbbbbbbbbbbbo",
            "..obbbbbbbbbbo.",
            "...oooooooooo..",
        },
    };
    static readonly string[][] BurstRows =
    {
        new[]
        {
            "...............",
            "...o.......o...",
            "..obo..o...obo.",
            "...o..obo...o..",
            "..o....o....o..",
            "...............",
            "...............",
            "....ooooooo....",
            "..oobbbbbbboo..",
            ".obbbbtttbbbbo.",
            "..ooooooooooo..",
        },
        new[]
        {
            ".o...........o.",
            "obo....o....obo",
            ".o....obo....o.",
            "...............",
            "...............",
            "...............",
            "...............",
            ".....ooooo.....",
            "..oobbbbbbboo..",
            ".obbbtbbbtbbbo.",
            "..ooooooooooo..",
        },
    };
    public static Stamp Mound(int stage, int scale) => Peek(310 + stage, scale) ?? Store(310 + stage, scale, Make(MoundRows[stage], scale));
    public static Stamp Burst(int frame, int scale) => Peek(320 + frame, scale) ?? Store(320 + frame, scale, Make(BurstRows[frame & 1], scale));

    public static bool Dual(Arm.Id id) => id is Arm.Id.Twin or Arm.Id.Pistols;   // 양손에 하나씩 드는 무기

    static string[] Flip(string[] rows)
    {
        var flipped = new string[rows.Length];
        for (int i = 0; i < rows.Length; i++) { var c = rows[i].ToCharArray(); Array.Reverse(c); flipped[i] = new string(c); }
        return flipped;
    }

    // 머리 위·표시용 그림: 양손 무기는 두 자루를 마주 보게 나란히
    static string[] IconRows(Arm.Id id)
    {
        var one = Hands[(int)id];
        if (!Dual(id)) return one;
        var (left, pair) = (Flip(one), new string[one.Length]);
        for (int i = 0; i < one.Length; i++) pair[i] = left[i] + "." + one[i];
        return pair;
    }

    static Stamp Make(string[] rows, int scale, Func<char, Color>? tint = null)
    {
        int w = rows[0].Length, h = rows.Length;
        using var bmp = new Bitmap(w * scale, h * scale, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (rows[y][x] == '.') continue;
                    using var b = new SolidBrush(tint?.Invoke(rows[y][x]) ?? Colors[rows[y][x]]);
                    g.FillRectangle(b, x * scale, y * scale, scale, scale);
                }
        return new Stamp(bmp);
    }

    static readonly Dictionary<(int Kind, int Scale), Stamp> made = new();   // Kind: 0~6 손(오른쪽) · 10~16 손(왼쪽) · 20~26 머리 위 · 30~32 갈래 · 40 위 화살표 · 41~42 다시 뽑기 · 70~71 강철 · 50~59 깃발 · 60~69 왕관 · 190~196 못 깬 무기 · 200~299 등급 숫자 · 80~ 엘리트 · 90 러시 느낌표 · 100 수류탄 · 101 밝은 판 · 102 회색 판 · 110~113 날아가는 네 장 · 120·121 그림자 · 130~136 밝은 손(오른쪽)·140~146 밝은 손(왼쪽)·150~156 밝은 머리 그림 · 160~163 날아가는 칼 · 170 새끼 폭탄 · 180·181 불 자리 · 310~312 흙더미 · 320·321 흙덩이
    // 이미 만든 그림은 바로 (프레임마다 부르는 곳에서 람다·새 객체를 안 만든다: 만드는 쪽은 따로 둔 메서드)
    static Stamp? Peek(int kind, int scale) { lock (made) return made.TryGetValue((kind, scale), out var hit) ? hit : null; }
    static Stamp Store(int kind, int scale, Stamp stamp) { lock (made) return made.TryGetValue((kind, scale), out var hit) ? hit : made[(kind, scale)] = stamp; }
    static Stamp Cached(int kind, int scale, string[] rows) => Peek(kind, scale) ?? Store(kind, scale, Make(rows, scale));

    // ── 단계 깃발·위 화살표 (깃발 색 = 등급. a 본색·h 밝은 쪽·k 어두운 쪽) ──
    static readonly string[] FlagRows =
    {
        "oo........",
        "oboooooo..",
        "obhhhhhho.",
        "obhaaaaaao",
        "obaaaaaao.",
        "obkkkkkko.",
        "oboooooo..",
        "ob........",
        "ob........",
        "oo........",
    };
    static readonly string[] ArrowRows =
    {
        "...o...",
        "..oyo..",
        ".oyyyo.",
        "oyyyyyo",
        "ooyyyoo",
        "..oyo..",
        "..oyo..",
        "..ooo..",
    };

    // 등급: 1~2단계 동 · 3~4 은 · 5~6 금 · 7~9 보라 · 10 이상 무지개 (색이 천천히 돈다)
    static readonly Color[] TierMain = { Color.FromArgb(0xC8, 0x7F, 0x3F), Color.FromArgb(0xC4, 0xCA, 0xD6), Color.FromArgb(0xFA, 0xB9, 0x28), Color.FromArgb(0xA8, 0x5C, 0xE0) };
    static readonly Color[] Rainbow = { Color.FromArgb(0xE8, 0x48, 0x5C), Color.FromArgb(0xFF, 0xAA, 0x32), Color.FromArgb(0xF8, 0xD8, 0x3C), Color.FromArgb(0x5C, 0xCB, 0x5C), Color.FromArgb(0x50, 0xD6, 0xE2), Color.FromArgb(0xA8, 0x5C, 0xE0) };
    public const int Tiers = 5, RainbowTier = 4;
    public static int Tier(int stage) => stage >= 10 ? RainbowTier : stage >= 7 ? 3 : stage >= 5 ? 2 : stage >= 3 ? 1 : 0;
    // 등급과 무지개 차례(0.5초마다 한 칸)로 고른 색 (0~9: 동·은·금·보라·무지개 여섯)
    static int ColorKey(int tier, int phase) => tier < RainbowTier ? tier : RainbowTier + Math.Abs(phase) % 6;
    static Color KeyColor(int key) => key < RainbowTier ? TierMain[key] : Rainbow[key - RainbowTier];
    static Color Mix(Color a, Color b, double t) => Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static Stamp Flag(int tier, int phase, int scale)
    {
        int key = ColorKey(tier, phase);
        return Peek(50 + key, scale) ?? Store(50 + key, scale, BuildFlag(key, scale));
    }

    static Stamp BuildFlag(int key, int scale)
    {
        var main = KeyColor(key);
        return Make(FlagRows, scale, ch => ch switch { 'a' => main, 'h' => Mix(main, Color.White, .45), 'k' => Mix(main, Color.Black, .35), _ => Colors[ch] });
    }

    public static Stamp Arrow(int scale) => Cached(40, scale, ArrowRows);

    // 러시 예고 느낌표: 흐린 주황, 테두리 없이 두 칸 굵기 (YELL 2026-11-17 시안 2번)
    static readonly string[] BangRows = { "aa", "aa", "aa", "aa", "..", "aa" };
    static readonly Color BangColor = Color.FromArgb(0xE0, 0xA8, 0x58);
    public static Stamp Bang(int scale) => Peek(90, scale) ?? Store(90, scale, BuildBang(scale));
    static Stamp BuildBang(int scale) => Make(BangRows, scale, _ => BangColor);

    // 결과 화면 무기 줄 (docs/swarm-levels.md 2-3): 같은 주황 붓 하나 (이번 판 무기 밑줄)
    public static readonly SolidBrush Amber = new(BangColor);
    const uint DigitWhite = 0xFFFFFFFF;
    static readonly SolidBrush[] KeyBrushes = Enumerable.Range(0, TierMain.Length + 6).Select(i => new SolidBrush(KeyColor(i))).ToArray();
    public static SolidBrush TierBrush(int tier, int phase) => KeyBrushes[ColorKey(tier, phase)];   // 등급 색 붓 (점 같은 네모용)
    public static readonly Color DimFill = Color.FromArgb(0x8C, 0x7B, 0x6E);   // 못 깬 무기: 테두리만 남기고 속을 이 한 색으로
    public static readonly SolidBrush DimBrush = new(DimFill);

    // 등급 색 숫자: 도트 숫자(num/N, 흰 속 + 갈색 테두리)의 흰색만 등급 색으로. src 는 그 배율의 숫자 그림 (한 번만 만든다)
    public static Stamp Digit(Stamp src, int d, int tier, int phase, int scale)
    {
        int key = ColorKey(tier, phase);
        return Peek(200 + key * 10 + d, scale) ?? Store(200 + key * 10 + d, scale, BuildDigit(src, key));
    }

    static Stamp BuildDigit(Stamp src, int key)
    {
        uint to = (uint)KeyColor(key).ToArgb();
        return Remake(src, 0, p => p == DigitWhite ? to : p, 0);
    }

    // 못 깬 무기 그림: 테두리(o)만 그대로, 속은 한 색
    public static Stamp IconDim(Arm.Id id, int scale) => Peek(190 + (int)id, scale) ?? Store(190 + (int)id, scale, Make(IconRows(id), scale, ch => ch == 'o' ? Colors['o'] : DimFill));

    // 왕관 색 바꿈: 왕관 그림(금색 한 가지 + 갈색 선)의 금색을 등급 색으로
    const uint CrownGold = 0xFFFAB928;
    public static Stamp Crown(Stamp src, int tier, int phase, int scale)
    {
        int key = ColorKey(tier, phase);
        return Peek(60 + key, scale) ?? Store(60 + key, scale, BuildCrown(src, key));
    }

    static Stamp BuildCrown(Stamp src, int key)
    {
        uint to = (uint)KeyColor(key).ToArgb();
        return Remake(src, 0, p => p == CrownGold ? to : p, 0);
    }

    // 엘리트: 벌레 그림 바깥으로 금색 테두리를 pad(px) 두른 그림. key 는 벌레 종류·걸음 (한 배율에 한 번만 만든다)
    public static Stamp Elite(Stamp src, int key, int pad) => Peek(80 + key, pad) ?? Store(80 + key, pad, Remake(src, pad, null, (uint)Color.FromArgb(0xFA, 0xB9, 0x28).ToArgb()));

    // 그림 한 장을 가운데 맞춰 다시 만든다: map 이 색을 바꾸고, edge 가 0 이 아니면 그림 둘레 pad px 에 그 색 테두리를 두른다 (그림이 있는 자리는 그대로)
    static Stamp Remake(Stamp src, int pad, Func<uint, uint>? map, uint edge)
    {
        var b = src.Bmp;
        int w = b.Width, h = b.Height, half = Math.Max(Math.Max(-src.Dx, src.Dx + w), Math.Max(-src.Dy, src.Dy + h)) + pad, size = 2 * half, ox = half + src.Dx, oy = half + src.Dy;
        var from = new int[w * h];
        var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < h; y++) Marshal.Copy(data.Scan0 + y * data.Stride, from, y * w, w); }
        finally { b.UnlockBits(data); }
        var to = new int[size * size];
        if (edge != 0 && pad > 0)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if ((uint)from[y * w + x] >> 24 != 0)
                        for (int dy = -pad; dy <= pad; dy++)
                            for (int dx = -pad; dx <= pad; dx++) to[(oy + y + dy) * size + ox + x + dx] = (int)edge;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                uint p = (uint)from[y * w + x];
                if (p >> 24 != 0) to[(oy + y) * size + ox + x] = (int)(map?.Invoke(p) ?? p);
            }
        using var cell = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var cd = cell.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < size; y++) Marshal.Copy(to, y * size, cd.Scan0 + y * cd.Stride, size); }
        finally { cell.UnlockBits(cd); }
        return new Stamp(cell);
    }

    public static Stamp Hand(Arm.Id id, int scale, bool left) => Peek((int)id + (left ? 10 : 0), scale) ?? Store((int)id + (left ? 10 : 0), scale, Make(left ? Flip(Hands[(int)id]) : Hands[(int)id], scale));
    public static Stamp Icon(Arm.Id id, int scale) => Peek(20 + (int)id, scale) ?? Store(20 + (int)id, scale, Make(IconRows(id), scale));
    public static Stamp UpIcon(Swarm.Up up, int scale) => Cached(30 + (int)up, scale, Ups[(int)up]);

    // 자가 검사: 줄 길이가 같고 색 글자가 팔레트 안인지
    public static string? Broken()
    {
        foreach (var (rows, i) in Hands.Concat(Ups).Concat(new[] { FlagRows, ArrowRows, SwapRows, NadeRows, FlyRows, ShadeBig, ShadeSmall, BladeRows, BombRows, FireRows }.Concat(MoundRows).Concat(BurstRows)).Select((r, i) => (r, i)))
            if (rows.Any(r => r.Length != rows[0].Length || r.Any(ch => ch != '.' && !Colors.ContainsKey(ch) && !(rows == FlagRows && ch is 'a' or 'h' or 'k')))) return $"그림 {i}";
        if (FlyRows.Length != FlyRows[0].Length || Fly.Any(f => f.Length != FlyRows.Length || f.Any(r => r.Length != FlyRows.Length))) return "날아가는 수류탄은 정사각형";
        if (BladeRows.Length != BladeRows[0].Length || BladeFlyRows.Any(f => f.Length != BladeRows.Length || f.Any(r => r.Length != BladeRows.Length))) return "날아가는 칼은 정사각형";
        if (FireRows.Length != FireRows[0].Length || FireTwin().Any(r => r.Length != FireRows[0].Length || r.Any(ch => ch != '.' && !Colors.ContainsKey(ch)))) return "불 자리는 정사각형";
        return Hands.Length == Arm.Kinds && Ups.Length == 3 ? null : "그림 수";
    }

    // 그려 보기: 무기 일곱 자루(손·왼손·머리 위)와 갈래 그림을 크게 한 장에
    public static void Sheet(string path, int scale)
    {
        using var bmp = new Bitmap(Arm.Kinds * 22 * scale, 132 * scale);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(150, 160, 170));
            for (int i = 0; i < Arm.Kinds; i++)
            {
                int x = i * 22 * scale + 11 * scale;
                Hand((Arm.Id)i, scale, false).Draw(g, x, 8 * scale);
                Hand((Arm.Id)i, scale, true).Draw(g, x, 24 * scale);
                Icon((Arm.Id)i, scale).Draw(g, x, 40 * scale);
                if (i < 3) UpIcon((Swarm.Up)i, scale).Draw(g, x, 56 * scale);
            }
            // 수류탄: 몸 · 밝은 판 · 회색 판 · 날아가는 네 장 · 그림자 둘
            var set = new[] { Nade(scale), NadeBright(scale), NadeGray(scale), NadeFly(0, scale), NadeFly(1, scale), NadeFly(2, scale), NadeFly(3, scale), NadeShade(false, scale), NadeShade(true, scale) };
            for (int i = 0; i < set.Length; i++) set[i].Draw(g, i * 16 * scale + 11 * scale, 74 * scale);
            // 특수기: 밝은 손(오른쪽)·밝은 머리 그림, 날아가는 칼 네 장·새끼 폭탄·불 자리 두 장
            for (int i = 0; i < Arm.Kinds; i++)
            {
                HandBright((Arm.Id)i, scale, false).Draw(g, i * 22 * scale + 11 * scale, 92 * scale);
                IconBright((Arm.Id)i, scale).Draw(g, i * 22 * scale + 11 * scale, 104 * scale);
            }
            var special = new[] { BladeFly(0, scale), BladeFly(1, scale), BladeFly(2, scale), BladeFly(3, scale), Bomb(scale), FireFloor(0, scale), FireFloor(1, scale) };
            for (int i = 0; i < special.Length; i++) special[i].Draw(g, i * 22 * scale + 11 * scale, 122 * scale);
        }
        bmp.Save(path);
    }
}
