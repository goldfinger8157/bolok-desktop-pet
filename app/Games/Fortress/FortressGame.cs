using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Bolok;

// 포트리스 모듈: 앱에 붙이기(판·조작·볼록이), 그리기, 기록, 디버그 명령. 규칙은 Fortress.cs, 자가 검사·장면·속도 재기는 FortressCheck.cs
// 판은 창모드 창들이 있는 모니터 전체 (창이 섬). 볼록이는 본체 창 그대로 움직이고(Puppet), 라이벌·구멍·포탄·표시만 그림 창에 그린다
// 조작 (YELL 2026-11-13): 마우스 조준, 왼쪽 버튼 꾹 눌렀다 놓기 = 쏘기, ← → 걷기, ↑ 점프 (방향키는 판이 앞 창일 때만). Esc 는 보스 키
// 표시·폭발·소리·조작은 공통 도구(app/Games/Kit)를 쓴다
sealed partial class FortressGame : IGameMode
{
    public const double GuideSec = 5;   // 안내 말풍선 (여섯 줄이라 조금 길게)

    readonly IGameHost host;
    readonly GameChoice[] choices;
    Fortress? game;
    Session? ses;
    int wins;
    bool upWas;
    bool showMe;        // 장면 그림에서만: 볼록이도 여기서 그린다 (실제로는 본체 창이 그린다)
    string[]? guide;
    double meHead, cpuHead;   // 발에서 머리 위까지 (px): 체력 막대를 머리 바로 위에 둔다

    static readonly Dictionary<string, Sound> Sounds = new()
    {
        ["jump"] = Sound.Tap, ["boom"] = Sound.Boom, ["splash:me"] = Sound.Hurt, ["splash:cpu"] = Sound.Pick,
        ["bounce"] = Sound.Tap, ["split"] = Sound.Tap, // 튕김탄이 튕길 때 · 집속탄이 갈라질 때
    };
    static readonly SolidBrush Grass = new(Color.FromArgb(120, 200, 90)), GrassDark = new(Color.FromArgb(70, 140, 60)), Dirt = new(Color.FromArgb(139, 94, 60)),
        HpGood = new(Color.FromArgb(110, 200, 110)), WindFill = new(Color.FromArgb(96, 150, 230)), SceneLand = new(Color.FromArgb(225, 228, 232));

    public FortressGame(IGameHost host)
    {
        this.host = host;
        choices = new GameChoice[Fortress.Guns.Length]; // 대포를 고르면 체력이 정해진다
        for (int i = 0; i < choices.Length; i++)
        {
            var gun = Fortress.Guns[i];
            choices[i] = new GameChoice(() => gun.Shells > 1 ? L.T("menu.gun_shells", gun.Label, gun.Hp, gun.Shells) : L.T("menu.gun", gun.Label, gun.Hp), () => Begin(gun.Name));
        }
    }

    public string Id => "fortress";
    public string MenuKey => "menu.fortress";
    public bool Running => game != null;
    public IReadOnlyList<GameChoice>? Choices => choices;

    public void Start() => Begin(null);

    void Begin(string? gun)
    {
        if (Running) return; // 하는 중에 또 골라도 판은 그대로
        var b = host.Pet;
        var screens = new List<Rectangle>();
        foreach (var s in Screen.AllScreens) screens.Add(s.WorkingArea);
        var (field, lands) = WindowPick.Islands(Native.Foreground(), Native.ListWindows(), b.Area, screens, b.CellPx, 4);
        if (lands.Count == 0) // 섬이 될 창모드 창이 없다: 말 대신 「X 표 창」 그림과 고개 젓기 (펫은 말하지 않는다)
        {
            b.Emote("nowin", 3);
            b.ShakeUntil = b.Now + .5;
            Dbg.Log("worms-none");
            return;
        }
        var w = new Fortress(field, lands, b.Scale, new Random(), gun);
        Open(w, Session.Open(host, field, () => Stop(true)));
        b.StartPuppet();
        Dbg.Log("mode-start", ("id", Id), ("field", field.ToString()), ("me", w.Me.Gun.Name), ("cpu", w.Cpu.Gun.Name), ("lands", string.Join(" ", w.Lands.Select(r => $"{r.X},{r.Y},{r.Width}x{r.Height}"))));
    }

    // 판 하나를 붙인다 (실제 시작, 그리고 화면 없는 장면·검사·속도 재기)
    void Open(Fortress w, Session s, bool events = true)
    {
        (game, ses) = (w, s);
        var b = host.Pet;
        if (events)
            w.OnEvent = e =>
            {
                if (e == "fire") Snd.Play(w.Active.Gun.Shells > 1 ? Sound.ShotMg : Sound.ShotGun);
                else Snd.Play(Sounds, e);
                if (e == "hurt:me") b.Emote("bang", .6);
                if (e is "won" or "lost" or "splash:me" or "splash:cpu" or "boom") Dbg.Log("worms", ("event", e), ("me", w.Me.Hp), ("cpu", w.Cpu.Hp));
            };
        guide = new[] { L.T("note.worms_gun", w.Me.Gun.Label, w.Me.Gun.Hp), L.T("note.gun_aim"), L.T("note.worms_fire"), L.T("note.worms_walk"), L.T("note.worms_wind"), L.T("note.esc") };
        (meHead, cpuHead) = (40 * w.K * PetArt.Embedded.HeadRatio(b.P.Stage), 40 * w.K * PetArt.Embedded.HeadRatio(Rival));
        using (var font = new Font(L.Font, 4 * b.Scale + 3, FontStyle.Bold, GraphicsUnit.Pixel)) noteHalf = (font.Height * guide.Length + 6 * b.Scale) / 2.0; // Overlay.NoteRect 와 같은 높이
        hudMode = -1;
        upWas = true; // 누른 채 시작한 ↑ 로 뛰지 않게
        (fresh, boomsSeen) = (0, 0);
        (rivalSpot, meSpot, cpuSpot, meGunSpot, cpuGunSpot, aimSpot, ghostSpot, hudSpot) = (default, default, default, default, default, default, default, default);
        NewTiles(w);
        DropGuide();
        Sync();
        Track();
    }

    public void Frame(double dt)
    {
        var (w, s) = (game!, ses!);
        s.Tick();
        var me = w.Me;
        if (w.MyTurn && w.Stage == Fortress.Phase.Turn && !me.Dead)
        {
            var m = s.Mouse;
            w.Aim = Math.Atan2(m.Y - w.CenterY(me), m.X - me.X);
            if (s.LeftHit) w.StartCharge();     // 누르고 있는 동안 힘이 찬다
            if (s.LeftUp) w.ReleaseCharge();    // 놓으면 쏜다 (꽉 차면 저절로)
            if (w.ChargeFrom < 0 && w.Stage == Fortress.Phase.Turn)
            {
                if (s.AxisX != 0) w.Walk(me, s.AxisX, dt);
                else me.Dir = m.X >= me.X ? 1 : -1;
                if (s.Up && !upWas) w.Jump(me);
            }
        }
        upWas = s.Up;
        w.Update(dt);
        Sync();
        Track();
        if (w.Done) Stop(false);
    }

    // 볼록이 본체를 규칙의 내 햄스터 자리·자세로
    void Sync()
    {
        var (w, b) = (game!, host.Pet);
        (b.X, b.Y, b.Dir, b.Pose) = (w.Me.X, w.Me.Y, w.Me.Dir, w.PoseOf(w.Me));
    }

    // quit(보스 키·디버그 stop): 표정·별 없이 바로 끝낸다. 그림은 그 자리에서 다 사라진다 (Session.Close 가 그림 창을 통째로 다시 칠한다)
    public void Stop(bool quit)
    {
        if (game is not { } w) return;
        game = null;
        var b = host.Pet;
        bool won = w.Stage == Fortress.Phase.Won;
        if (won) wins++;
        if (w.Me.Out) b.Y = w.Field.Bottom - b.K.FloorGap; // 물에 빠졌으면 바닥으로 돌아온다
        ses?.Close(b, quit, won, "worms", ("me", w.Me.Hp), ("cpu", w.Cpu.Hp), ("esc", quit));
        ses = null;
        DropTiles();
        DropGuide();
        Dbg.Log("mode-end", ("id", Id));
        host.Save();
    }

    // 디버그: worms [multi|giant|normal|bounce|cluster|drill|push] / worms fire <각도°> <힘 0~1> / worms stop  (fortress 로 써도 같다)
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != "worms" && a[0] != Id) return false;
        if (a.Length > 1 && a[1] == "stop") Stop(true);
        else if (a.Length > 3 && a[1] == "fire" && game is { MyTurn: true } w)
            w.Fire(w.Me, double.Parse(a[2], CultureInfo.InvariantCulture) * Math.PI / 180, double.Parse(a[3], CultureInfo.InvariantCulture));
        else if (!Running && !host.Busy) Begin(a.Length > 1 ? a[1] : null);
        return true;
    }

    public IEnumerable<GameStat> Stats()
    {
        yield return new GameStat(L.T("game.fortress"), wins > 0 ? L.T("game.fortress_val", wins) : L.T("game.fortress_none"), wins > 0);
    }

    // 옛 저장 칸(WormsWins)도 이 모양으로 넘어온다 (GameModes.WithLegacy)
    sealed record Record(int Wins);
    public string? SaveState() => wins > 0 ? JsonSerializer.Serialize(new Record(wins)) : null;

    public void LoadState(string? json)
    {
        if (json == null) return;
        try { if (JsonSerializer.Deserialize<Record>(json) is { } r) wins = r.Wins; }
        catch (JsonException) { /* 깨진 기록은 버린다 */ }
    }

    // ── 그림: 자주 찍는 것은 한 번만 찾아 둔다 (개발 중 그림이 바뀌면 다시) ──
    const string Rival = "adult_grumpy";    // 라이벌은 늘 심술이 모습 (볼이 불룩한 그림)
    const int RocketSteps = 32;             // 포탄이 날아가는 쪽: 미리 돌려 둔 그림 32방향
    Sprites? artOf;
    int artK;
    Stamp aim = null!, aimGhost = null!, ball = null!, ballBig = null!, cloud = null!, crown = null!, rocketSmall = null!, rocketBig = null!;
    readonly Stamp?[] rockets = new Stamp?[2 * RocketSteps]; // 보통 포탄(배율 k), 왕대포(배율 2k)
    readonly Dictionary<string, Stamp[]>[] rival = { new(), new() }; // 자세 → 그림 줄 (오른쪽 보기, 왼쪽 보기)

    void Art(int k)
    {
        var sp = host.Sprites;
        if (ReferenceEquals(artOf, sp) && artK == k) return;
        (artOf, artK) = (sp, k);
        var st = Stamps.Of(sp);
        int small = GameHud.Small(k);
        (aim, ball, cloud, crown) = (st.Get("game/aim", 0, small), st.Get("game/ball", 0, small), st.Get("prop/cloud", 0, Math.Max(1, k / 3)), st.Get("game/crown", 0, small));
        (rocketSmall, rocketBig, ballBig) = (st.Get("game/rocket", 0, small), st.Get("game/rocket", 0, k), st.Get("game/ball", 0, k + 1));
        aimGhost?.Bmp.Dispose();
        aimGhost = Faded(sp.Get("game/aim", 0, small), GhostAlpha);
        for (int i = 0; i < rockets.Length; i++) { rockets[i]?.Bmp.Dispose(); rockets[i] = null; }
        for (int i = 0; i < gunArt.Length; i++) { gunArt[i]?.Bmp.Dispose(); gunArt[i] = null; }
        rival[0].Clear();
        rival[1].Clear();
    }

    Stamp RivalStamp(Fortress w)
    {
        string pose = w.PoseOf(w.Cpu);
        var rows = rival[w.Cpu.Dir < 0 ? 1 : 0];
        if (!rows.TryGetValue(pose, out var row)) rows[pose] = row = Stamps.Of(artOf!).Row($"{Rival}/1/{pose}", w.K, w.Cpu.Dir < 0);
        return row[(int)(w.Clock * 6) % row.Length];
    }

    // 포탄 그림 (대포의 Look): 날아가는 쪽을 보는 로켓(왕대포는 두 배) · 작은 쇠공(멀티캐논, 집속탄의 새끼 탄) · 큰 쇠공(튕김탄·밀치기탄)
    // 드릴탄은 땅속에서도 파고드는 쪽을 보는 로켓
    Stamp ShellStamp(Fortress w, Fortress.Shell s)
    {
        if (s.Look >= 2) return s.Look == 2 ? ball : ballBig;
        int big = s.Look, step = (int)Math.Round(Math.Atan2(s.Vy, s.Vx) / (2 * Math.PI) * RocketSteps) & (RocketSteps - 1);
        return rockets[big * RocketSteps + step] ??= Turned(artOf!.Get("game/rocket", 0, big == 1 ? w.K * 2 : w.K), step * 360.0 / RocketSteps);
    }

    // ── 대포 그림 (체력 막대 왼쪽에 작게): 누가 어떤 대포를 쓰는지 (YELL 2026-11-13: 둘이 다른 대포를 쓴다). 글자 없이 모양으로 ──
    // 멀티캐논 = 가는 포신 셋(포구가 어둡다), 왕대포 = 빨간 띠를 두른 굵은 포신, 일반대포 = 보통 포신,
    // 튕김탄 = 초록 줄무늬(용수철) 포신, 집속탄 = 포구 앞에 노란 알 넷, 드릴탄 = 빨강·흰 줄무늬 송곳 끝, 밀치기탄 = 파란 큰 밀대. 포구는 상대 쪽을 본다
    // 칸 12×10, 점 하나가 볼록이 도트 하나 (오른쪽 보기). L 테두리 · h 밝은 쇠 · d 어두운 쇠 · w 바퀴·노란 알 · R 빨강 · g 초록 · b 파랑 · c 흰색
    const int GunW = 12, GunH = 10;
    static readonly string[] GunBounce =
    {
        "............",
        "............",
        "........LLL.",
        ".LLLLLLLLgL.",
        "LhghghghLgL.",
        "LdgdgdgdLgL.",
        ".LLLLLLLLgL.",
        "...LwwL.LLL.",
        "...LwwL.....",
        "....LL......",
    }, GunCluster =
    {
        "........ww..",
        "........ww..",
        ".LLLLLL...ww",
        "LhhhhhhL..ww",
        "LhhhhhhL....",
        "LddddddL..ww",
        ".LLLLLL...ww",
        "...LwwL.ww..",
        "...LwwL.ww..",
        "....LL......",
    }, GunDrill =
    {
        "............",
        "............",
        ".......LL...",
        ".LLLLLLRcLL.",
        "LhhhhhhRcRcL",
        "LddddddRcRcL",
        ".LLLLLLRcLL.",
        "...LwwLLL...",
        "...LwwL.....",
        "....LL......",
    }, GunPush =
    {
        "............",
        "........LLL.",
        "........LbbL",
        ".LLLLLLLLbbL",
        "LhhhhhhhLbbL",
        "LdddddddLbbL",
        ".LLLLLLLLbbL",
        "...LwwL.LbbL",
        "...LwwL.LLL.",
        "....LL......",
    }, GunMulti =
    {
        ".LLLLLLLLL..",
        "LhhhhhhhddL.",
        ".LLLLLLLLLL.",
        "LhhhhhhhhddL",
        ".LLLLLLLLLL.",
        "LhhhhhhhddL.",
        ".LLLLLLLLL..",
        "...LwwL.....",
        "...LwwL.....",
        "....LL......",
    }, GunGiant =
    {
        "...LLLLLLLL.",
        "..LhhhhhRhhL",
        ".LhhhhhhRhhL",
        ".LddddddRddL",
        ".LddddddRddL",
        "..LdddddRddL",
        "...LLLLLLLL.",
        "...LwwL.....",
        "...LwwL.....",
        "....LL......",
    }, GunNormal =
    {
        "............",
        "............",
        "........LLL.",
        ".LLLLLLLLhL.",
        "LhhhhhhhLhL.",
        "LdddddddLdL.",
        ".LLLLLLLLdL.",
        "...LwwL.LLL.",
        "...LwwL.....",
        "....LL......",
    };
    static readonly string[][] GunRows = { GunMulti, GunGiant, GunNormal, GunBounce, GunCluster, GunDrill, GunPush }; // Fortress.Guns 와 같은 차례
    readonly Stamp?[] gunArt = new Stamp?[GunRows.Length * 2]; // 대포마다 (오른쪽 보기, 왼쪽 보기). 처음 쓸 때 한 번 그려 둔다

    Stamp GunStamp(Fortress w, Fortress.Hog h)
    {
        bool left = (h == w.Me ? -Back(w) : Back(w)) < 0; // 상대 쪽을 본다
        int kind = Math.Max(0, Array.IndexOf(Fortress.Guns, h.Gun));
        return gunArt[kind * 2 + (left ? 1 : 0)] ??= GunIcon(GunRows[kind], w.K, left);
    }

    static Stamp GunIcon(string[] rows, int s, bool flip)
    {
        using var bmp = new Bitmap(GunW * s, GunH * s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            for (int y = 0; y < GunH; y++)
                for (int x = 0; x < GunW; x++)
                {
                    Brush? b = rows[y][flip ? GunW - 1 - x : x] switch
                    {
                        'L' => Pal.Line, 'h' => Pal.Chip, 'd' => Pal.ChipEdge, 'w' => Pal.Gold, 'R' => Pal.Red, 'g' => HpGood, 'b' => WindFill, 'c' => Pal.White, _ => null,
                    };
                    if (b != null) g.FillRectangle(b, x * s, y * s, s, s);
                }
        return new Stamp(bmp);
    }

    // 대포 그림 칸의 가운데: 체력 막대 왼쪽, 막대와 같은 높이 (그 위의 힘 막대·지난 힘 세모보다 아래, 볼록이 귀보다 위)
    (int X, int Y) GunAt(Fortress w, Fortress.Hog h) => ((int)h.X - (12 + GunW / 2) * w.K, BarY(w, h) + w.K);
    Rectangle GunBox(Fortress w, Fortress.Hog h) { var (x, y) = GunAt(w, h); return GunStamp(w, h).Box(x, y); }

    // 흐리게 만든 그림 (지난 각도 조준경): 한 번 만들어 두고 찍는다. 자르는 자리는 원래 그림과 같다
    const float GhostAlpha = .4f;
    static Stamp Faded(Bitmap src, float alpha)
    {
        using var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        using (var fade = new ImageAttributes())
        {
            fade.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, fade);
        }
        return new Stamp(bmp);
    }

    static Stamp Turned(Bitmap src, double deg)
    {
        using var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            (g.InterpolationMode, g.PixelOffsetMode) = (InterpolationMode.NearestNeighbor, PixelOffsetMode.Half);
            g.TranslateTransform(src.Width / 2f, src.Height / 2f);
            g.RotateTransform((float)deg);
            g.DrawImage(src, new Rectangle(-src.Width / 2, -src.Height / 2, src.Width, src.Height));
        }
        return new Stamp(bmp);
    }

    // ── 구멍 그림: 깎인 칸 하나가 점 하나 (까만 구멍, 남은 땅과 맞닿은 가장자리는 그을음). 64px 조각으로 나눠 그려 두고, 터진 자리 조각만 고친다 ──
    const int TileCells = 16, TilePx = TileCells * Fortress.Cell;
    static readonly int[] tileBuf = new int[TilePx * TilePx];
    Bitmap?[] tiles = Array.Empty<Bitmap?>();
    int tw, th;
    bool anyTiles;
    // 섬 윗변의 풀·밑면의 흙 줄: 맨 윗줄·맨 아랫줄 칸이 남아 있는 곳만 (깎인 자리 위에 풀이 떠 있지 않게). 섬마다 (왼쪽, 오른쪽) px
    readonly List<(int Land, int X0, int X1)> grassRuns = new(), dirtRuns = new();
    // 섬(창) 가장자리는 4px 칸에 딱 맞지 않아 칸 밖으로 0~3px 이 남는다: 가장자리 칸이 깎였으면 그 자투리도 까맣게 (구멍 위에 창 테두리 한 줄이 떠 있지 않게)
    readonly List<Rectangle> gaps = new();

    void NewTiles(Fortress w)
    {
        DropTiles();
        (tw, th) = ((w.Gw + TileCells - 1) / TileCells, (w.Gh + TileCells - 1) / TileCells);
        tiles = new Bitmap?[tw * th];
        Strips(w);
    }

    void DropTiles()
    {
        foreach (var t in tiles) t?.Dispose();
        (tiles, anyTiles) = (Array.Empty<Bitmap?>(), false);
    }

    // 화면 좌표 r 에 걸친 조각을 다시 그린다 (가장자리 그을음이 바뀌는 옆 칸까지)
    void PaintHoles(Fortress w, Rectangle r)
    {
        const int c = Fortress.Cell;
        int gx0 = Math.Max(0, (r.Left - w.Field.Left) / c - 1), gx1 = Math.Min(w.Gw - 1, (r.Right - w.Field.Left) / c + 1);
        int gy0 = Math.Max(0, (r.Top - w.Field.Top) / c - 1), gy1 = Math.Min(w.Gh - 1, (r.Bottom - w.Field.Top) / c + 1);
        for (int ty = gy0 / TileCells; ty <= gy1 / TileCells; ty++)
            for (int tx = gx0 / TileCells; tx <= gx1 / TileCells; tx++) PaintTile(w, tx, ty);
        Strips(w);
    }

    void PaintTile(Fortress w, int tx, int ty)
    {
        const int c = Fortress.Cell;
        int hole = Pal.Hole.Color.ToArgb(), soot = Pal.Soot.Color.ToArgb();
        bool any = false;
        Array.Clear(tileBuf);
        for (int j = 0; j < TileCells; j++)
            for (int i = 0; i < TileCells; i++)
            {
                int gx = tx * TileCells + i, gy = ty * TileCells + j;
                if (!w.LandCell(gx, gy) || w.SolidCell(gx, gy)) continue;
                bool rim = w.SolidCell(gx - 1, gy) || w.SolidCell(gx + 1, gy) || w.SolidCell(gx, gy - 1) || w.SolidCell(gx, gy + 1);
                int color = rim ? soot : hole;
                for (int y = j * c; y < j * c + c; y++) Array.Fill(tileBuf, color, y * TilePx + i * c, c);
                any = true;
            }
        ref var tile = ref tiles[ty * tw + tx];
        if (!any) return; // 깎인 칸이 없는 조각은 그림도 없다 (땅은 다시 메워지지 않는다)
        tile ??= new Bitmap(TilePx, TilePx, PixelFormat.Format32bppPArgb);
        var data = tile.LockBits(new Rectangle(0, 0, TilePx, TilePx), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        Marshal.Copy(tileBuf, 0, data.Scan0, tileBuf.Length);
        tile.UnlockBits(data);
        anyTiles = true;
    }

    void Strips(Fortress w)
    {
        const int c = Fortress.Cell;
        int fx = w.Field.Left, fy = w.Field.Top;
        grassRuns.Clear();
        dirtRuns.Clear();
        gaps.Clear();
        for (int n = 0; n < w.Lands.Count; n++)
        {
            var l = w.Lands[n];
            int gx0 = (l.Left - fx + c - 1) / c, gx1 = (l.Right - fx) / c - 1, top = (l.Top - fy + c - 1) / c, bottom = (l.Bottom - fy) / c - 1; // 이 섬의 칸
            int x0 = fx + gx0 * c, x1 = fx + (gx1 + 1) * c, y0 = fy + top * c, y1 = fy + (bottom + 1) * c;                                      // 칸이 덮는 자리
            for (int pass = 0; pass < 2; pass++) // 맨 윗줄(풀)과 맨 아랫줄(흙)
            {
                int gy = pass == 0 ? top : bottom;
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    bool solid = w.SolidCell(gx, gy);
                    int from = gx;
                    while (gx + 1 <= gx1 && w.SolidCell(gx + 1, gy) == solid) gx++;
                    int a = from == gx0 ? l.Left : fx + from * c, b = gx == gx1 ? l.Right : fx + (gx + 1) * c; // 섬 끝까지 이어지면 창 끝까지
                    if (solid) (pass == 0 ? grassRuns : dirtRuns).Add((n, a, b));
                    else if (pass == 0 && y0 > l.Top) gaps.Add(Rectangle.FromLTRB(a, l.Top, b, y0));
                    else if (pass == 1 && l.Bottom > y1) gaps.Add(Rectangle.FromLTRB(a, y1, b, l.Bottom));
                }
            }
            for (int pass = 0; pass < 2; pass++) // 맨 왼쪽·맨 오른쪽 칸
            {
                int gx = pass == 0 ? gx0 : gx1, a = pass == 0 ? l.Left : x1, b = pass == 0 ? x0 : l.Right;
                if (a >= b) continue;
                for (int gy = top; gy <= bottom; gy++)
                {
                    if (w.SolidCell(gx, gy)) continue;
                    int from = gy;
                    while (gy + 1 <= bottom && !w.SolidCell(gx, gy + 1)) gy++;
                    gaps.Add(Rectangle.FromLTRB(a, fy + from * c, b, fy + (gy + 1) * c));
                }
            }
        }
    }

    // ── 자리 (화면 좌표): 그리기와 다시 칠할 자리가 같은 계산을 쓴다 ──
    // ── 바람 상자 자리 (YELL 2026-11-13: 판 위 가운데는 조준할 때 보는 곳에서 멀다 → 볼록이 가까이로) ──
    //  0 머리 위: 체력 막대·힘 막대·조준경이 도는 둘레·안내 말풍선보다 위, 볼록이 바로 위에
    //  1 발 아래: 머리 위에 자리가 없을 때 (섬이 화면 위쪽에 붙어 있다). 말풍선 아랫변 밑, 라이벌 반대쪽으로 뻗는다 (앞쪽·위쪽을 겨누는 조준경에 안 걸린다)
    //  2 조준경 둘레 아래: 뒤쪽이 판 끝이라 1 도 못 쓸 때
    // 한번 고른 쪽은 자리가 있는 한 그대로 쓴다 (위아래로 오락가락하지 않게).
    // 볼록이가 걷고 뛰고 날아가는 동안에는 제자리에 있고, 멈춰 선 뒤(HudRest 초)에만 새 자리로 스르륵 옮겨 간다:
    // 걸음마다 따라다니면 바람을 읽을 수 없고 프레임마다 상자를 다시 칠하게 된다. 조금(6칸px 안) 움직인 것으로는 옮기지 않는다
    const int HudW = 92, HudH = 14;     // × 배율
    const double HudRest = .25, HudGlide = 14; // 멈춰 서고 이만큼 지나야 자리를 다시 잡는다 · 옮겨 가는 빠르기 (1/초: 0.3초쯤이면 다 간다)
    int hudMode = -1, hudBack;          // -1 아직 안 잡았다. hudBack: 라이벌 반대쪽 (-1 왼쪽 · 1 오른쪽)
    double hudX, hudY, hudClock;        // 지금 그려지는 자리 (왼쪽 위)
    double hudAirAt;                    // 볼록이가 마지막으로 떠 있던 때
    Point hudTo;                        // 가야 할 자리
    double hudForX, hudForY;            // 그 자리를 잡았을 때의 볼록이 발
    double noteHalf;                    // 안내 말풍선 높이의 절반

    Rectangle HudRect(Fortress w) => new((int)Math.Round(hudX), (int)Math.Round(hudY), HudW * w.K, HudH * w.K);
    static int Back(Fortress w) => w.Me.X <= w.Cpu.X ? -1 : 1;

    // mode 쪽에 둘 때의 자리. 판 밖으로 나가면 빈 자리 (force: 그래도 판 안으로 밀어 넣어서)
    Rectangle HudAt(Fortress w, int mode, bool force = false)
    {
        var me = w.Me;
        int k = w.K, hw = HudW * k, hh = HudH * k, x = (int)me.X, cy = (int)w.CenterY(me), reach = (int)(40 * k * .9) + 1;
        var room = Rectangle.Inflate(w.Field, -2 * k, -2 * k);
        double noteMid = me.Y - host.Pet.CellPx * .4;
        int left = x - hw / 2, top;
        if (mode == 0) top = Math.Min(Math.Min(LabelRect(w, me).Top, cy - reach + aim.Dy), (int)(noteMid - noteHalf)) - k - hh;
        else if (mode == 1) (left, top) = (Back(w) < 0 ? x + 4 * k - hw : x - 4 * k, (int)Math.Ceiling(noteMid + noteHalf) + k);
        else top = cy + reach + aim.Dy + aim.Bmp.Height + k;
        int fit = Math.Clamp(left, room.Left, Math.Max(room.Left, room.Right - hw));
        if (force) return new(fit, Math.Clamp(top, room.Top, Math.Max(room.Top, room.Bottom - hh)), hw, hh);
        return mode == 1 && fit != left || top < room.Top || top + hh > room.Bottom ? default : new(fit, top, hw, hh);
    }

    // 프레임마다 한 번: 볼록이가 멈춰 섰으면 자리를 다시 잡고, 가야 할 자리가 있으면 조금씩 다가간다
    void PlaceHud(Fortress w)
    {
        var me = w.Me;
        bool first = hudMode < 0;
        if (first || me.Air) hudAirAt = first ? -9 : w.Clock;
        if (first || !me.Out && !me.Air && w.Clock - Math.Max(me.WalkAt, hudAirAt) >= HudRest
            && (Math.Abs(me.X - hudForX) > 6 * w.K || Math.Abs(me.Y - hudForY) >= 1 || hudMode == 1 && Back(w) != hudBack))
        {
            var at = first ? default : HudAt(w, hudMode);
            for (int m = 0; m < 3 && at.IsEmpty; m++) at = HudAt(w, hudMode = m);
            if (at.IsEmpty) at = HudAt(w, hudMode = 0, force: true); // 어디에도 자리가 없다 (판이 아주 작다): 겹치더라도 판 안에
            (hudTo, hudForX, hudForY, hudBack) = (at.Location, me.X, me.Y, Back(w));
            if (first) (hudX, hudY) = (at.X, at.Y);
        }
        double dt = w.Clock - hudClock;
        hudClock = w.Clock;
        if (hudX == hudTo.X && hudY == hudTo.Y) return;
        double step = 1 - Math.Exp(-Math.Max(0, dt) * HudGlide);
        (hudX, hudY) = (hudX + (hudTo.X - hudX) * step, hudY + (hudTo.Y - hudY) * step);
        if (Math.Abs(hudTo.X - hudX) < 1 && Math.Abs(hudTo.Y - hudY) < 1) (hudX, hudY) = (hudTo.X, hudTo.Y);
    }
    int BarY(Fortress w, Fortress.Hog h) => (int)(h.Y - (h == w.Me ? meHead : cpuHead)) - 7 * w.K; // 체력 막대 윗변: 귀 위로 조금 띄워서

    // 체력 막대 + 숫자 + 그 위 표시(차례 화살표·힘 막대와 지난 힘 세모·왕관)가 다 들어가는 자리. 대포 그림은 그 왼쪽에 따로 (GunBox: 힘이 차는 동안 같이 다시 칠하지 않게)
    Rectangle LabelRect(Fortress w, Fortress.Hog h)
    {
        int k = w.K, small = GameHud.Small(k), x = (int)h.X, y = BarY(w, h);
        return Rectangle.FromLTRB(x - 13 * k, y - 12 * k, x + 12 * k + 17 * small, y + k + 5 * small);
    }

    // 막대 위 표시: 0 없음 · 1 왕관(이긴 쪽) · 100+ 차례 화살표(까딱거림) · 1000+ 힘 막대(찬 px)
    // 한 번이라도 쏜 뒤에는 내 차례 내내 빈 힘 막대를 보여 준다 (차례 화살표 대신): 지난 힘 세모를 누르기 전부터 볼 수 있게
    static int Mark(Fortress w, Fortress.Hog h)
    {
        if (w.Stage == Fortress.Phase.Won && h == w.Me || w.Stage == Fortress.Phase.Lost && h == w.Cpu) return 1;
        if (w.Stage != Fortress.Phase.Turn || w.Active != h) return 0;
        if (h == w.Me && (w.ChargeFrom >= 0 || w.HasLast)) return 1000 + (int)(20 * w.K * w.Power);
        return 100 + (int)(Math.Sin(w.Clock * 6) * w.K);
    }

    static bool AimOn(Fortress w) => w.Stage == Fortress.Phase.Turn && !w.Active.Dead && (w.MyTurn || w.CpuAiming);

    static (int X, int Y) AimAt(Fortress w)
    {
        var h = w.Active;
        return AimAt(w, h, w.MyTurn ? w.Aim : w.CpuAim);
    }

    static (int X, int Y) AimAt(Fortress w, Fortress.Hog h, double a)
    {
        double reach = 40 * w.K * .9;
        return ((int)(h.X + Math.Cos(a) * reach), (int)(w.CenterY(h) + Math.Sin(a) * reach));
    }

    // 지난 각도 조준경 (흐리게): 한 번이라도 쏜 뒤 내 차례 내내, 지금 서 있는 자리에서 지난번에 겨눈 쪽에 (YELL 2026-11-13). 라이벌 차례·포탄이 나는 동안에는 없다
    static bool GhostOn(Fortress w) => w.HasLast && w.MyTurn && w.Stage == Fortress.Phase.Turn && !w.Me.Dead;

    // 지난 힘 세모의 뾰족한 끝 (힘 막대 왼쪽 끝에서 px): 힘이 그만큼 찼을 때 채워진 끝과 같은 자리
    static int LastTip(Fortress w) => (int)(20 * w.K * Math.Min(1, w.LastPower));

    // ── 다시 칠할 자리: 프레임마다 한 번(Track) 바뀐 것만 모아 두고, 그림 창마다 그대로 넘긴다 ──
    // 움직이지 않는 것(라이벌·체력 막대·대포 그림·조준경·바람 상자)은 값이 바뀐 프레임에만 옛 자리와 새 자리를 넣는다
    struct Spot
    {
        Rectangle last;
        int key;
        public void Mark(List<Rectangle> rs, Rectangle now, int k)
        {
            if (now == last && k == key) return;
            if (!last.IsEmpty) rs.Add(last);
            if (!now.IsEmpty && now != last) rs.Add(now);
            (last, key) = (now, k);
        }
    }

    readonly List<Rectangle> dirty = new(64);
    int fresh, boomsSeen;
    Spot rivalSpot, meSpot, cpuSpot, meGunSpot, cpuGunSpot, aimSpot, ghostSpot, hudSpot;

    void Track()
    {
        var w = game!;
        int k = w.K;
        Art(k);
        dirty.Clear();
        if (fresh < 2) { fresh++; dirty.Add(w.Field); } // 판이 막 열렸다: 한 번은 통째로
        // 새로 터진 자리: 구멍 그림을 고치고 그 자리를 한 번 다시 칠한다 (윗변 풀이 지워지는 깊이까지)
        for (int i = Math.Max(0, w.Blasts.Count - (w.Carves - boomsSeen)); i < w.Blasts.Count; i++)
        {
            var bl = w.Blasts[i];
            double reach = bl.R * 1.35 + Math.Max(2 * Fortress.Cell, 3 * k + Fortress.Cell);
            var r = Overlay.Around(bl.X, bl.Y, reach, reach);
            PaintHoles(w, r);
            dirty.Add(r);
        }
        boomsSeen = w.Carves;

        var cpu = w.Cpu;
        if (cpu.Out) rivalSpot.Mark(dirty, default, 0);
        else
        {
            var st = RivalStamp(w);
            rivalSpot.Mark(dirty, Rectangle.Inflate(st.Box((int)cpu.X, (int)cpu.Y - 20 * k), 1, 1), RuntimeHelpers.GetHashCode(st));
        }
        if (showMe && !w.Me.Out) dirty.Add(Fx.PetRect(w.Me.X, w.Me.Y, 40 * k));
        Label(w, w.Me, ref meSpot, ref meGunSpot);
        Label(w, cpu, ref cpuSpot, ref cpuGunSpot);
        if (AimOn(w)) { var (ax, ay) = AimAt(w); aimSpot.Mark(dirty, Rectangle.Inflate(aim.Box(ax, ay), 1, 1), 1); }
        else aimSpot.Mark(dirty, default, 0);
        if (GhostOn(w)) { var (gx, gy) = AimAt(w, w.Me, w.LastAim); ghostSpot.Mark(dirty, Rectangle.Inflate(aimGhost.Box(gx, gy), 1, 1), 1); }
        else ghostSpot.Mark(dirty, default, 0);
        foreach (var s in w.Shells) dirty.Add(Rectangle.Inflate(ShellStamp(w, s).Box((int)s.X, (int)s.Y), 1, 1));
        foreach (var bl in w.Blasts) if (!bl.Quiet && w.Clock - bl.At < BlastArt.Sec) dirty.Add(BlastArt.PopRect(bl.X, bl.Y, bl.R, k));
        PlaceHud(w);
        hudSpot.Mark(dirty, HudRect(w), HashCode.Combine((int)Math.Round(w.Wind * 10), (int)Math.Ceiling(w.TimeLeft), w.Active.Gun.Name, w.MyTurn));
    }

    void Label(Fortress w, Fortress.Hog h, ref Spot spot, ref Spot gun)
    {
        if (h.Out) spot.Mark(dirty, default, 0);
        else spot.Mark(dirty, LabelRect(w, h), HashCode.Combine(h.Hp, Mark(w, h), h == w.Me && w.HasLast ? LastTip(w) : -1));
        if (h.Out) gun.Mark(dirty, default, 0);
        else { var st = GunStamp(w, h); var (x, y) = GunAt(w, h); gun.Mark(dirty, st.Box(x, y), RuntimeHelpers.GetHashCode(st)); }
    }

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (game is not { } w || !o.Bounds.IntersectsWith(w.Field)) return;
        foreach (var r in dirty) rs.Add(o.Client(r));
        if (w.Clock < GuideSec) rs.Add(o.Client(GuideRect(o, w)));
    }

    // ── 그리기: 섬 풀·흙 줄, 구멍, 바람 상자(바람·남은 시간·대포), 라이벌, 체력 막대, 지난 각도 조준경(흐리게)·조준경, 포탄, 불덩이, 안내 ──
    // 바람 상자는 볼록이 가까이에 있어서 맨 밑에 깐다: 뛰어오른 볼록이의 체력 막대나 지나가는 포탄·불덩이가 상자에 가리지 않게
    public void Draw(Overlay o, Graphics g)
    {
        if (game is not { } w || !o.Bounds.IntersectsWith(w.Field)) return;
        int k = w.K;
        Art(k);
        if (showMe) foreach (var l in w.Lands) g.FillRectangle(SceneLand, o.Client(l)); // 장면 그림: 섬이 된 창도 보이게
        foreach (var (n, x0, x1) in grassRuns) // 섬 윗변의 풀 (섬 두께가 보이게 밑면에는 흙 줄)
        {
            int y = w.Lands[n].Top - o.Top;
            g.FillRectangle(Grass, x0 - o.Left, y, x1 - x0, 2 * k);
            g.FillRectangle(GrassDark, x0 - o.Left, y + 2 * k, x1 - x0, k);
        }
        foreach (var (n, x0, x1) in dirtRuns) g.FillRectangle(Dirt, x0 - o.Left, w.Lands[n].Bottom - o.Top - 2 * k, x1 - x0, 2 * k);
        if (anyTiles) // 깎인 자리: 다시 칠하는 곳에 걸친 조각만
        {
            var f = o.Client(w.Field);
            var clip = g.ClipBounds;
            int x0 = (int)Math.Max(0, Math.Floor((clip.Left - f.X) / TilePx)), x1 = (int)Math.Min(tw - 1, Math.Floor((clip.Right - f.X) / TilePx));
            int y0 = (int)Math.Max(0, Math.Floor((clip.Top - f.Y) / TilePx)), y1 = (int)Math.Min(th - 1, Math.Floor((clip.Bottom - f.Y) / TilePx));
            for (int ty = y0; ty <= y1; ty++)
                for (int tx = x0; tx <= x1; tx++)
                    if (tiles[ty * tw + tx] is { } tile) g.DrawImageUnscaled(tile, f.X + tx * TilePx, f.Y + ty * TilePx);
            foreach (var r in gaps) g.FillRectangle(Pal.Hole, r.X - o.Left, r.Y - o.Top, r.Width, r.Height);
        }
        DrawHud(o, g, w);
        var cpu = w.Cpu; // 라이벌 (볼록이는 제 창으로 그려진다)
        if (!cpu.Out) RivalStamp(w).Put(o, g, (int)cpu.X, (int)cpu.Y - 20 * k);
        if (showMe && !w.Me.Out) Fx.Pet(o, g, artOf!, host.Pet.P.Stage, w.PoseOf(w.Me), w.Clock, k, w.Me.X, w.Me.Y, w.Me.Dir < 0);
        DrawLabel(o, g, w, w.Me);
        DrawLabel(o, g, w, cpu);
        if (GhostOn(w)) { var (gx, gy) = AimAt(w, w.Me, w.LastAim); aimGhost.Put(o, g, gx, gy); } // 지금 조준경 밑에 깔린다
        if (AimOn(w)) { var (ax, ay) = AimAt(w); aim.Put(o, g, ax, ay); }
        foreach (var s in w.Shells) ShellStamp(w, s).Put(o, g, (int)s.X, (int)s.Y);
        foreach (var bl in w.Blasts) if (!bl.Quiet && w.Clock - bl.At < BlastArt.Sec) BlastArt.Pop(o, g, bl.X, bl.Y, bl.R, w.Clock - bl.At, k);
        if (w.Clock < GuideSec) DrawGuide(o, g, w);
    }

    // ── 안내 말풍선: 글자를 프레임마다 다시 쓰면 느려서(한 번에 1ms 안팎) 한 번 그려 둔 그림을 찍는다. 꼬리가 왼쪽인 것·오른쪽인 것 하나씩 ──
    readonly Bitmap?[] guideArt = new Bitmap?[2];
    Rectangle guideAt;
    double guideX = double.NaN, guideY;

    // 말풍선 자리 (화면 좌표). 볼록이가 움직였을 때만 다시 잰다
    Rectangle GuideRect(Overlay o, Fortress w)
    {
        var b = host.Pet;
        if (b.X != guideX || b.Y != guideY) (guideX, guideY, guideAt) = (b.X, b.Y, o.NoteRect(b.X, b.Y, w.Field, guide!));
        return guideAt;
    }

    void DrawGuide(Overlay o, Graphics g, Fortress w)
    {
        var r = GuideRect(o, w);
        ref var art = ref guideArt[r.X < host.Pet.X ? 1 : 0];
        if (art == null)
        {
            art = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppPArgb);
            using var ag = Graphics.FromImage(art);
            ag.TranslateTransform(o.Left - r.X, o.Top - r.Y);
            o.DrawNoteAt(ag, host.Pet.X, host.Pet.Y, w.Field, guide!);
        }
        g.DrawImageUnscaled(art, r.X - o.Left, r.Y - o.Top);
    }

    void DropGuide()
    {
        for (int i = 0; i < guideArt.Length; i++) { guideArt[i]?.Dispose(); guideArt[i] = null; }
        guideX = double.NaN;
    }

    // 머리 위: 대포 그림(막대 왼쪽), 체력 막대와 숫자, 그 위에 차례 화살표(까딱까딱) · 힘 막대(누르고 있는 동안, 한 번 쏜 뒤에는 내 차례 내내) · 왕관(이긴 쪽)
    // 힘 막대 위에는 지난번에 쏜 힘 자리에 작은 역삼각형 (YELL 2026-11-13): 채워진 끝이 세모 끝에 닿을 때 놓으면 같은 힘
    void DrawLabel(Overlay o, Graphics g, Fortress w, Fortress.Hog h)
    {
        if (h.Out) return;
        int k = w.K, small = GameHud.Small(k), x = (int)h.X, y = BarY(w, h), mark = Mark(w, h);
        var (gunX, gunY) = GunAt(w, h);
        GunStamp(w, h).Put(o, g, gunX, gunY);
        GameHud.Bar(o, g, new Rectangle(x - 10 * k, y, 20 * k, 2 * k), k, (double)h.Hp / h.Gun.Hp, h.Hp * 3 < h.Gun.Hp ? Pal.Red : HpGood);
        GameHud.Number(o, g, h.Hp, x + 12 * k + 3 * small, y + k, small, -1);
        if (mark == 1) crown.Put(o, g, x, y - 6 * k);
        else if (mark >= 1000)
        {
            GameHud.Bar(o, g, new Rectangle(x - 10 * k, y - 6 * k, 20 * k, 3 * k), k, w.Power, w.Power > .8 ? Pal.Red : Pal.Gold);
            if (!w.HasLast) return;
            // 도트 세 줄(5·3·1칸)에 갈색 테두리, 끝은 막대 테두리에 붙는다
            int e = Math.Max(1, k / 2), left = x - 10 * k + LastTip(w) - k / 2 - o.Left, bottom = y - 6 * k - e - o.Top;
            for (int i = 0; i < 3; i++) g.FillRectangle(Pal.Line, left - i * k - e, bottom - (i + 1) * k - e, (2 * i + 1) * k + 2 * e, k + 2 * e);
            for (int i = 0; i < 3; i++) g.FillRectangle(Pal.White, left - i * k, bottom - (i + 1) * k, (2 * i + 1) * k, k);
        }
        else if (mark >= 100 - k) // 아래를 가리키는 세모 (도트 세 줄, 갈색 테두리)
        {
            int e = Math.Max(1, k / 2), cx = x - o.Left, top = y - 6 * k + (mark - 100) - o.Top;
            for (int i = 0; i < 3; i++) g.FillRectangle(Pal.Line, cx - (3 - i) * k - e, top + i * k - e, 2 * (3 - i) * k + 2 * e, k + 2 * e);
            for (int i = 0; i < 3; i++) g.FillRectangle(Pal.Gold, cx - (3 - i) * k, top + i * k, 2 * (3 - i) * k, k);
        }
    }

    // 안내 말풍선과 같은 모양의 상자: 모서리를 한 칸 깎은 갈색 테두리에 흰 바탕
    static void Bubble(Graphics g, Rectangle box, int u)
    {
        g.FillRectangle(Pal.Line, box.X + u, box.Y, box.Width - 2 * u, box.Height);
        g.FillRectangle(Pal.Line, box.X, box.Y + u, box.Width, box.Height - 2 * u);
        g.FillRectangle(Pal.White, box.X + u, box.Y + u, box.Width - 2 * u, box.Height - 2 * u);
    }

    // 바람 상자 (볼록이 가까이, 자리는 PlaceHud): 구름 + 바람 화살표(길이)와 세기 칸 다섯, 남은 시간, 지금 차례 대포
    void DrawHud(Overlay o, Graphics g, Fortress w)
    {
        int k = w.K, small = GameHud.Small(k);
        var at = HudRect(w);
        var hud = o.Client(at);
        Bubble(g, hud, k);
        int mid = hud.Y + hud.Height / 2, midY = at.Y + at.Height / 2;
        cloud.Put(o, g, at.X + 9 * k, midY);
        int wx = hud.X + 30 * k, len = (int)(Math.Abs(w.Wind) * 12 * k) + k, dir = w.Wind >= 0 ? 1 : -1;
        g.FillRectangle(WindFill, Math.Min(wx, wx + dir * len), mid - k, len, 2 * k);
        for (int i = 0; i < 4; i++) g.FillRectangle(WindFill, dir > 0 ? wx + len + i * k : wx - len - (i + 1) * k, mid - (4 - i) * k, k, 2 * (4 - i) * k); // 화살촉 (도트 계단)
        int bars = (int)Math.Round(Math.Abs(w.Wind) * 5);
        for (int i = 0; i < 5; i++) g.FillRectangle(i < bars ? WindFill : Pal.Cream, hud.X + 48 * k + i * 3 * k, mid - 3 * k, 2 * k, 6 * k);
        GameHud.Number(o, g, (int)Math.Ceiling(w.TimeLeft), at.X + 70 * k, midY, small);
        var gun = w.Active.Gun;
        (gun.Look == 1 ? rocketBig : gun.Look >= 2 ? ball : rocketSmall).Put(o, g, at.X + 84 * k, midY);

    }
}
