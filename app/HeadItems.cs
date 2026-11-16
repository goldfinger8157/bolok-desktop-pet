namespace Bolok;

// 볼록이 머리에 얹는 것: 머리 장식(네잎클로버·새싹·해바라기·낙엽 넷)과 밤 수면 모자. 자리 재기와 그리기를 한곳에 (PetForm·그려 보기가 같이 쓴다).
// 크기는 성장 단계별 (YELL 2026-11-16: 「악세사리 크기를 아기-어린이-어른별로 다르게, 지금 크기는 어른용」):
//   아기 ≈55% · 어린이 ≈80% · 어른 100% — 도트가 깨지지 않게 정수 배율로 반올림 (화면 배율 3이면 장식 아기 1·어린이 2·어른 2, 모자 아기 2·어린이 2·어른 3)
sealed class HeadItems
{
    readonly Dictionary<(string, int, bool, int), (int X, int Top)> spots = new();
    readonly Dictionary<string, (int X, int Y)> stems = new();
    readonly Dictionary<(string, int, bool), (int X, int Top)> capSpots = new();
    Sprites? of;

    public static double StageSize(string stage) => stage == "baby" ? .55 : stage == "child" ? .8 : 1;
    public static int ItemScale(int k, string stage) => Math.Max(1, (int)Math.Round(Clover.Scale(k) * StageSize(stage)));
    public static int CapScale(int k, string stage) => Math.Max(1, (int)Math.Round(k * StageSize(stage)));

    public void Clear() { of = null; spots.Clear(); stems.Clear(); capSpots.Clear(); }
    void Fresh(Sprites sp) { if (!ReferenceEquals(of, sp)) { Clear(); of = sp; } }

    // 머리 장식: 그 열에서 가장 위 칸에 줄기 끝을 꽂는다 (프레임마다 몸이 들썩이면 같이).
    //   새싹은 정수리 가운데, 네잎클로버·해바라기는 왼쪽 옆, 낙엽은 가운데에서 살짝 오른쪽에 45° 기울여 꼭지부터 (YELL 2026-11-13)
    // ox, oy: 볼록이 칸 왼쪽 위 (창 좌표). s 를 주면 그 배율로 (비교 그림용)
    public void DrawWorn(Graphics g, Sprites sp, string name, int frame, bool flip, int k, string stage, string kind, int look, int ox, int oy = 0, int? s = null)
    {
        Fresh(sp);
        string item = "item/" + kind;
        bool tilt = Clover.Falls(kind) && sp.Has(item + "_45");
        bool mirror = tilt && look % 2 == 1; // 왼쪽·오른쪽 어느 쪽으로 기울지는 내려앉을 때마다 무작위
        if (tilt) item += "_45";
        if (!sp.Has(item)) return;
        int n = Math.Max(1, sp.Frames(name)), f = (frame % n + n) % n, side = kind == "sprout" ? 0 : Clover.Falls(kind) ? 3 : -4;
        if (!spots.TryGetValue((name, f, flip, side), out var spot))
        {
            var one = sp.Get(name, f, 1, flip);
            int top = one.Height;
            for (int x = one.Width * 3 / 10; x < one.Width * 6 / 10; x++)
                for (int y = 0; y < one.Height; y++)
                    if (one.GetPixel(x, y).A > 0) { top = Math.Min(top, y); break; }
            int row = Math.Min(one.Height - 1, top + 4), x0 = one.Width, x1 = 0;
            for (int x = 0; x < one.Width; x++) if (one.GetPixel(x, row).A > 0) (x0, x1) = (Math.Min(x0, x), Math.Max(x1, x));
            int col = Math.Clamp((x0 + x1) / 2 + (flip ? -side : side), 0, one.Width - 1), surface = one.Height - 1;
            for (int y = 0; y < one.Height; y++) if (one.GetPixel(col, y).A > 0) { surface = y; break; }
            spots[(name, f, flip, side)] = spot = (col, surface);
        }
        string stemKey = mirror ? item + "|m" : item;
        if (!stems.TryGetValue(stemKey, out var stem)) // 그림의 줄기 끝 (가장 아래 칸)
        {
            var icon1 = sp.Get(item, 0, 1, mirror);
            stem = (20, 23);
            for (int y = icon1.Height - 1, found = 0; y >= 0 && found == 0; y--)
            {
                int x0 = -1;
                for (int x = 0; x < icon1.Width; x++) if (icon1.GetPixel(x, y).A > 0) { x0 = x; break; }
                if (x0 >= 0) { stem = (x0, y); found = 1; }
            }
            stems[stemKey] = stem;
        }
        int sc = s ?? ItemScale(k, stage);
        var (sx, sy) = stem;
        int sink = tilt ? Math.Max(1, (int)Math.Round(k * sc / (double)Clover.Scale(k))) : 0; // 낙엽은 꼭지 끝이 머리에 한 칸 묻히게 (꽂힌 듯), 작으면 덜
        g.DrawImageUnscaled(sp.Get(item, 0, sc, mirror), ox + spot.X * k - sx * sc, oy + spot.Top * k - sy * sc + sink); // 줄기 끝만 머리에 닿고 잎은 살짝 떠 있게 (YELL)
    }

    // 수면 모자: 몸 가운데 열에서 가장 위 칸 (프레임마다 따로 재서 숨 쉬며 들썩이는 머리를 따라간다)
    public void DrawCap(Graphics g, Sprites sp, string name, int frame, bool flip, int k, string stage, int ox, int oy = 0, int? s = null)
    {
        Fresh(sp);
        if (!sp.Has("game/nightcap")) return;
        int n = Math.Max(1, sp.Frames(name)), f = (frame % n + n) % n;
        if (!capSpots.TryGetValue((name, f, flip), out var spot))
        {
            var one = sp.Get(name, f, 1, flip);
            int top = one.Height;
            for (int x = one.Width * 3 / 10; x < one.Width * 6 / 10; x++)
                for (int y = 0; y < one.Height; y++)
                    if (one.GetPixel(x, y).A > 0) { top = Math.Min(top, y); break; }
            int row = Math.Min(one.Height - 1, top + 4), x0 = one.Width, x1 = 0;
            for (int x = 0; x < one.Width; x++) if (one.GetPixel(x, row).A > 0) (x0, x1) = (Math.Min(x0, x), Math.Max(x1, x));
            capSpots[(name, f, flip)] = spot = ((x0 + x1) / 2, top);
        }
        int c = s ?? CapScale(k, stage);
        g.DrawImageUnscaled(sp.Get("game/nightcap", 0, c), ox + spot.X * k - 23 * c, oy + spot.Top * k - 21 * c); // 모자 그림의 (23, 21) 칸이 머리 꼭대기에
    }

    // ── 화면 없이 그려 보기: Bolok.exe --head-look 그림.png [종 ...] ──
    // 줄마다 한 종: 아기·어린이·어른(통통이) × [지금(어른 크기) | 단계별 크기] — 네잎클로버·해바라기·단풍잎·수면 모자
    public static void Look(string path, IReadOnlyList<string> species)
    {
        var art = PetArt.Embedded;
        const int k = 3, cell = PetArt.Cell * k;
        string[] forms = { "baby", "child", "adult_chubby" };
        (string Kind, bool Cap)[] items = { ("clover", false), ("sunflower", false), ("maple", false), ("", true) };
        int cols = forms.Length * 2, rowH = cell + 6;
        using var bmp = new Bitmap(10 + items.Length * (cols * cell + 20), 10 + species.Count * rowH);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(236, 239, 244));
        using var tint = new SolidBrush(Color.FromArgb(222, 228, 238));
        var heads = new HeadItems();
        for (int r = 0; r < species.Count; r++)
        {
            var sp = Sprites.Drawn(art, species[r], null);
            for (int it = 0; it < items.Length; it++)
            {
                int x0 = 10 + it * (cols * cell + 20), y = 10 + r * rowH;
                for (int fi = 0; fi < forms.Length; fi++)
                    for (int now = 0; now < 2; now++)
                    {
                        int x = x0 + (fi * 2 + now) * cell;
                        if (now == 1) g.FillRectangle(tint, x, y, cell, cell); // 바꾼 뒤 칸은 옅은 바탕
                        string pose = items[it].Cap ? "sleep" : "idle", name = $"{forms[fi]}/0/{pose}";
                        g.DrawImageUnscaled(sp.Get(name, 0, k), x, y);
                        if (items[it].Cap) heads.DrawCap(g, sp, name, 0, false, k, forms[fi], x, y, now == 0 ? k : null);
                        else heads.DrawWorn(g, sp, name, 0, false, k, forms[fi], items[it].Kind, 0, x, y, now == 0 ? Clover.Scale(k) : null);
                    }
            }
        }
        bmp.Save(path);
    }
}
