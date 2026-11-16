using System.Drawing.Imaging;

namespace Bolok;

// 버그 떼 모듈 그림: 무기 일곱 자루(손에 든 그림·머리 위 그림)와 레벨 업 갈래 그림 셋. 글자 한 칸 = 색 하나 (. 은 투명).
// 모두 오른쪽을 보는 그림이고 왼쪽은 뒤집어 쓴다 (돌리지 않는다: 작은 도트가 뭉개진다). 테두리는 펫과 같은 진한 갈색.
// 배율마다 한 번만 만들어 둔다
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

    static Stamp Make(string[] rows, int scale)
    {
        int w = rows[0].Length, h = rows.Length;
        using var bmp = new Bitmap(w * scale, h * scale, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (rows[y][x] == '.') continue;
                    using var b = new SolidBrush(Colors[rows[y][x]]);
                    g.FillRectangle(b, x * scale, y * scale, scale, scale);
                }
        return new Stamp(bmp);
    }

    static readonly Dictionary<(int Kind, int Scale), Stamp> made = new();   // Kind: 0~6 손(오른쪽) · 10~16 손(왼쪽) · 20~26 머리 위 · 30~32 갈래
    static Stamp Cached(int kind, int scale, Func<string[]> rows)
    {
        lock (made) return made.TryGetValue((kind, scale), out var hit) ? hit : made[(kind, scale)] = Make(rows(), scale);
    }

    public static Stamp Hand(Arm.Id id, int scale, bool left) => Cached((int)id + (left ? 10 : 0), scale, () => left ? Flip(Hands[(int)id]) : Hands[(int)id]);
    public static Stamp Icon(Arm.Id id, int scale) => Cached(20 + (int)id, scale, () => IconRows(id));
    public static Stamp UpIcon(Swarm.Up up, int scale) => Cached(30 + (int)up, scale, () => Ups[(int)up]);

    // 자가 검사: 줄 길이가 같고 색 글자가 팔레트 안인지
    public static string? Broken()
    {
        foreach (var (rows, i) in Hands.Concat(Ups).Select((r, i) => (r, i)))
            if (rows.Any(r => r.Length != rows[0].Length || r.Any(ch => ch != '.' && !Colors.ContainsKey(ch)))) return $"그림 {i}";
        return Hands.Length == Arm.Kinds && Ups.Length == 3 ? null : "그림 수";
    }

    // 그려 보기: 무기 일곱 자루(손·왼손·머리 위)와 갈래 그림을 크게 한 장에
    public static void Sheet(string path, int scale)
    {
        using var bmp = new Bitmap(Arm.Kinds * 22 * scale, 64 * scale);
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
        }
        bmp.Save(path);
    }
}
