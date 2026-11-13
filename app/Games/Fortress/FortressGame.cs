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

    static readonly Dictionary<string, Sound> Sounds = new() { ["jump"] = Sound.Tap, ["boom"] = Sound.Boom, ["splash:me"] = Sound.Hurt, ["splash:cpu"] = Sound.Pick };
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
        if (lands.Count == 0) // 섬이 될 창모드 창이 없다: 말 대신 「X 표 창」 그림과 고개 젓기 (PetForm.NoWindow 와 같다)
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
        upWas = true; // 누른 채 시작한 ↑ 로 뛰지 않게
        (fresh, boomsSeen) = (0, 0);
        (rivalSpot, meSpot, cpuSpot, aimSpot, hudSpot) = (default, default, default, default, default);
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

    // 디버그: worms [multi|giant|normal] / worms fire <각도°> <힘 0~1> / worms stop  (fortress 로 써도 같다)
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
    Stamp aim = null!, ball = null!, cloud = null!, crown = null!, rocketSmall = null!, rocketBig = null!;
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
        (rocketSmall, rocketBig) = (st.Get("game/rocket", 0, small), st.Get("game/rocket", 0, k));
        for (int i = 0; i < rockets.Length; i++) { rockets[i]?.Bmp.Dispose(); rockets[i] = null; }
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

    // 포탄 그림: 멀티캐논은 작은 쇠공, 나머지는 날아가는 쪽을 보는 로켓 (왕대포는 두 배)
    Stamp ShellStamp(Fortress w, Fortress.Shell s)
    {
        if (s.Kind == "multi") return ball;
        int big = s.Kind == "giant" ? 1 : 0, step = (int)Math.Round(Math.Atan2(s.Vy, s.Vx) / (2 * Math.PI) * RocketSteps) & (RocketSteps - 1);
        return rockets[big * RocketSteps + step] ??= Turned(artOf!.Get("game/rocket", 0, big == 1 ? w.K * 2 : w.K), step * 360.0 / RocketSteps);
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
    static Rectangle HudRect(Fortress w) => new(w.Field.Left + w.Field.Width / 2 - 46 * w.K, w.Field.Top + 4 * w.K, 92 * w.K, 14 * w.K);
    int BarY(Fortress w, Fortress.Hog h) => (int)(h.Y - (h == w.Me ? meHead : cpuHead)) - 7 * w.K; // 체력 막대 윗변: 귀 위로 조금 띄워서

    // 체력 막대 + 숫자 + 그 위 표시(차례 화살표·힘 막대·왕관)가 다 들어가는 자리
    Rectangle LabelRect(Fortress w, Fortress.Hog h)
    {
        int k = w.K, small = GameHud.Small(k), x = (int)h.X, y = BarY(w, h);
        return Rectangle.FromLTRB(x - 11 * k, y - 12 * k, x + 12 * k + 17 * small, y + k + 5 * small);
    }

    // 막대 위 표시: 0 없음 · 1 왕관(이긴 쪽) · 100+ 차례 화살표(까딱거림) · 1000+ 힘 막대(찬 px)
    static int Mark(Fortress w, Fortress.Hog h)
    {
        if (w.Stage == Fortress.Phase.Won && h == w.Me || w.Stage == Fortress.Phase.Lost && h == w.Cpu) return 1;
        if (w.Stage != Fortress.Phase.Turn || w.Active != h) return 0;
        if (h == w.Me && w.ChargeFrom >= 0) return 1000 + (int)(20 * w.K * w.Power);
        return 100 + (int)(Math.Sin(w.Clock * 6) * w.K);
    }

    static bool AimOn(Fortress w) => w.Stage == Fortress.Phase.Turn && !w.Active.Dead && (w.MyTurn || w.CpuAiming);

    static (int X, int Y) AimAt(Fortress w)
    {
        var h = w.Active;
        double a = w.MyTurn ? w.Aim : w.CpuAim, reach = 40 * w.K * .9;
        return ((int)(h.X + Math.Cos(a) * reach), (int)(w.CenterY(h) + Math.Sin(a) * reach));
    }

    // ── 다시 칠할 자리: 프레임마다 한 번(Track) 바뀐 것만 모아 두고, 그림 창마다 그대로 넘긴다 ──
    // 움직이지 않는 것(라이벌·체력 막대·조준경·위쪽 상자)은 값이 바뀐 프레임에만 옛 자리와 새 자리를 넣는다
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
    Spot rivalSpot, meSpot, cpuSpot, aimSpot, hudSpot;

    void Track()
    {
        var w = game!;
        int k = w.K;
        Art(k);
        dirty.Clear();
        if (fresh < 2) { fresh++; dirty.Add(w.Field); } // 판이 막 열렸다: 한 번은 통째로
        // 새로 터진 자리: 구멍 그림을 고치고 그 자리를 한 번 다시 칠한다 (윗변 풀이 지워지는 깊이까지)
        for (int i = Math.Max(0, w.Blasts.Count - (w.Booms - boomsSeen)); i < w.Blasts.Count; i++)
        {
            var bl = w.Blasts[i];
            double reach = bl.R * 1.35 + Math.Max(2 * Fortress.Cell, 3 * k + Fortress.Cell);
            var r = Overlay.Around(bl.X, bl.Y, reach, reach);
            PaintHoles(w, r);
            dirty.Add(r);
        }
        boomsSeen = w.Booms;

        var cpu = w.Cpu;
        if (cpu.Out) rivalSpot.Mark(dirty, default, 0);
        else
        {
            var st = RivalStamp(w);
            rivalSpot.Mark(dirty, Rectangle.Inflate(st.Box((int)cpu.X, (int)cpu.Y - 20 * k), 1, 1), RuntimeHelpers.GetHashCode(st));
        }
        if (showMe && !w.Me.Out) dirty.Add(Fx.PetRect(w.Me.X, w.Me.Y, 40 * k));
        Label(w, w.Me, ref meSpot);
        Label(w, cpu, ref cpuSpot);
        if (AimOn(w)) { var (ax, ay) = AimAt(w); aimSpot.Mark(dirty, Rectangle.Inflate(aim.Box(ax, ay), 1, 1), 1); }
        else aimSpot.Mark(dirty, default, 0);
        foreach (var s in w.Shells) dirty.Add(Rectangle.Inflate(ShellStamp(w, s).Box((int)s.X, (int)s.Y), 1, 1));
        foreach (var bl in w.Blasts) if (w.Clock - bl.At < BlastArt.Sec) dirty.Add(BlastArt.PopRect(bl.X, bl.Y, bl.R, k));
        hudSpot.Mark(dirty, HudRect(w), HashCode.Combine((int)Math.Round(w.Wind * 10), (int)Math.Ceiling(w.TimeLeft), w.Active.Gun.Name, w.MyTurn));
    }

    void Label(Fortress w, Fortress.Hog h, ref Spot spot)
    {
        if (h.Out) spot.Mark(dirty, default, 0);
        else spot.Mark(dirty, LabelRect(w, h), HashCode.Combine(h.Hp, Mark(w, h)));
    }

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (game is not { } w || !o.Bounds.IntersectsWith(w.Field)) return;
        foreach (var r in dirty) rs.Add(o.Client(r));
        if (w.Clock < GuideSec) rs.Add(o.Client(GuideRect(o, w)));
    }

    // ── 그리기: 섬 풀·흙 줄, 구멍, 라이벌, 체력 막대, 조준경, 포탄, 불덩이, 위쪽 상자(바람·남은 시간·대포), 안내 ──
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
        var cpu = w.Cpu; // 라이벌 (볼록이는 제 창으로 그려진다)
        if (!cpu.Out) RivalStamp(w).Put(o, g, (int)cpu.X, (int)cpu.Y - 20 * k);
        if (showMe && !w.Me.Out) Fx.Pet(o, g, artOf!, host.Pet.P.Stage, w.PoseOf(w.Me), w.Clock, k, w.Me.X, w.Me.Y, w.Me.Dir < 0);
        DrawLabel(o, g, w, w.Me);
        DrawLabel(o, g, w, cpu);
        if (AimOn(w)) { var (ax, ay) = AimAt(w); aim.Put(o, g, ax, ay); }
        foreach (var s in w.Shells) ShellStamp(w, s).Put(o, g, (int)s.X, (int)s.Y);
        foreach (var bl in w.Blasts) if (w.Clock - bl.At < BlastArt.Sec) BlastArt.Pop(o, g, bl.X, bl.Y, bl.R, w.Clock - bl.At, k);
        DrawHud(o, g, w);
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

    // 머리 위: 체력 막대와 숫자, 그 위에 차례 화살표(까딱까딱) · 힘 막대(누르고 있는 동안) · 왕관(이긴 쪽)
    void DrawLabel(Overlay o, Graphics g, Fortress w, Fortress.Hog h)
    {
        if (h.Out) return;
        int k = w.K, small = GameHud.Small(k), x = (int)h.X, y = BarY(w, h), mark = Mark(w, h);
        GameHud.Bar(o, g, new Rectangle(x - 10 * k, y, 20 * k, 2 * k), k, (double)h.Hp / h.Gun.Hp, h.Hp * 3 < h.Gun.Hp ? Pal.Red : HpGood);
        GameHud.Number(o, g, h.Hp, x + 12 * k + 3 * small, y + k, small, -1);
        if (mark == 1) crown.Put(o, g, x, y - 6 * k);
        else if (mark >= 1000) GameHud.Bar(o, g, new Rectangle(x - 10 * k, y - 6 * k, 20 * k, 3 * k), k, w.Power, w.Power > .8 ? Pal.Red : Pal.Gold);
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

    // 위 가운데 상자: 구름 + 바람 화살표(길이)와 세기 칸 다섯, 남은 시간, 지금 차례 대포
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
        (gun.Shells > 1 ? ball : gun.Name == "giant" ? rocketBig : rocketSmall).Put(o, g, at.X + 84 * k, midY);
    }
}
