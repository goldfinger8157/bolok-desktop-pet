using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Bolok;

// 총쏘기 모듈: 앱에 붙이기(판·조작·볼록이·게임할 창 고르기), 그리기, 기록, 자가 검사, 디버그 명령. 규칙은 Shoot.cs, 볼록이가 도망 다니는 법은 Prey.cs
// 판은 메뉴를 열기 전 쓰던 창 하나 (없으면 모니터). 볼록이 본체는 그대로 두고(꼭두각시) 그 창 안에서 도망 다니게 하고, 총·탄흔·표시만 그림 창에 그린다
// 조작: 마우스 조준(조준경 = 마우스 커서), 왼쪽 쏘기(기관총은 누르고 있으면 연사), 오른쪽 장전. 키는 쓰지 않는다. Esc 끝 (보스 키: 결과 화면 없이 바로)
// 표시·효과·소리·조작은 공통 도구(app/Games/Kit)를 쓴다. 하는 동안 메모리를 새로 잡지 않고, 바뀐 자리만 다시 칠한다
sealed class ShootGame : IGameMode
{
    readonly IGameHost host;
    readonly Func<int, int, bool> hitTest;   // 한 번만 만들어 둔다 (쏠 때마다 새로 만들지 않게)
    Shoot? game;
    Prey? prey;
    Session? ses;
    Random fx = new();
    int best, bestBefore, forgive, cursorShown;
    IntPtr window;      // 게임하는 창 (사라지면 끝낸다). 모니터 전체에서 하면 0
    double checkAt;
    bool real, mock;

    static readonly Dictionary<string, Sound> Sounds = new()
    {
        ["pistol"] = Sound.Shot, ["mg"] = Sound.ShotMg, ["shotgun"] = Sound.ShotGun,
        ["hit"] = Sound.Hit, ["empty"] = Sound.Tap, ["reload"] = Sound.Reload, ["boom"] = Sound.Boom,
    };

    public ShootGame(IGameHost host) { this.host = host; hitTest = HitsPet; }
    public string Id => "shoot";
    public string MenuKey => "menu.shoot";
    public bool Running => game != null;

    static readonly string[] GuideKeys = { "note.shoot_aim", "note.shoot_fire", "note.shoot_reload", "note.esc" };

    // 봐주는 둘레 (그림 칸 수, YELL 2026-11-13): 몸이 작을수록 넉넉하게. 아기 6, 어린이 4, 어른 3 (어른 모습은 이름으로 세지 않는다: adult 로 시작하면 어른)
    public static int Forgive(string stage) => stage == "baby" ? 6 : stage.StartsWith("adult", StringComparison.Ordinal) ? 3 : 4;

    // 그 자리가 볼록이 몸에 맞는가: 본체 창에 그려진 그림 픽셀 + 봐주는 둘레. 창 뒤로 숨은 몸은 안 맞는다
    bool HitsPet(int x, int y) => prey is { } p && !p.Shielded(x, y) && host.HitPet(x, y, forgive);

    public void Start() => Start(null, null);

    void Start(string? gunName, string? theme)
    {
        if (Running) return;
        var b = host.Pet;
        var rng = new Random();
        var wins = Native.ListWindows();
        var screens = Screen.AllScreens.Select(s => s.WorkingArea).ToList();
        // 메뉴를 열기 전에 쓰던 창이 게임판이 된다. 그 창만 꾸미고, 볼록이도 그 안에서만 움직인다
        var (arena, win) = Shoot.ChooseArena(Native.Foreground(), wins, b.Area, screens, b.CellPx);
        int k = b.Scale;
        var gm = new Shoot(arena, best, k, rng, Shoot.Guns.FirstOrDefault(g => g.Name == gunName), theme != null && Props.Sets.ContainsKey(theme) ? theme : null);
        // 총은 그 창 아래 테두리 가운데에 반쯤 걸친다 (총 그림은 손잡이 자리에서 위로 8칸, 아래로 1칸). 화면 밖으로는 안 나간다
        gm.GunPivot = new Point(arena.Left + arena.Width / 2, Math.Min(arena.Bottom + 6 * k, Screen.FromRectangle(arena).Bounds.Bottom - 2 * k));
        gm.Decor = Decorate(gm, wins.Where(w => w.H == win).ToList(), screens, rng);
        // 그 창 위에 겹친 창들 뒤에 숨는다 (모니터 전체에서 하면 모든 창 뒤에)
        var covers = Props.Covers(win == IntPtr.Zero ? wins : wins.TakeWhile(w => w.H != win).ToList(), arena, b.CellPx);
        (window, real) = (win, true);
        ses = Session.Open(host, arena, () => Stop(true)); // 그 창 위에만 판을 깐다. 창 밖은 평소처럼 클릭된다
        ses.Guide(GuideKeys);
        Open(gm, covers, rng);
        Dbg.Log("mode-start", ("id", Id), ("gun", gm.Gun.Name), ("theme", gm.Theme), ("arena", arena.ToString()), ("window", win != IntPtr.Zero), ("decor", gm.Decor.Count), ("covers", covers.Count));
    }

    // 판을 편다 (화면 없는 검사·장면도 같이 쓴다): 볼록이는 그 창 안에서 도망 다닌다
    Shoot Open(Shoot gm, IEnumerable<Cover>? covers, Random rng)
    {
        var b = host.Pet;
        game = gm;
        (gm.OnShot, gm.OnHole) = (OnShot, OnHole);
        b.StartPuppet();
        prey = new Prey(b, gm.Arena, covers, rng);
        (fx, bestBefore, checkAt, cursorShown, forgive) = (rng, best, 0, int.MinValue, Forgive(b.P.Stage));
        Reset();
        return gm;
    }

    // 소품: 게임 창 윗변에 둘, 창 안 바닥에 둘 (총·안내 말풍선·나가기 버튼 자리는 비운다)
    List<Prop> Decorate(Shoot gm, IReadOnlyList<Win> target, IReadOnlyList<Rectangle> screens, Random rng)
    {
        int k = gm.Scale;
        var (sp, names) = (host.Sprites, Props.Sets[gm.Theme]);
        Func<string, bool[,]> solid = n => SolidOf(sp, n);
        var gunSpot = new Rectangle(gm.GunPivot.X - 40 * k * 2, gm.GunPivot.Y - 30 * k * 2, 80 * k * 2, 34 * k * 2);
        var list = Props.OnWindows(target, screens, names, rng, solid, Array.Empty<Rectangle>(), 2);
        var noteSpot = new Rectangle((int)GuideX(gm) - 80 * k, gm.Arena.Bottom - 40 * k, 80 * k, 40 * k); // 처음 4초 동안 안내 말풍선이 뜨는 자리
        list.AddRange(Props.OnFloor(gm.Arena, names, rng, solid, new[] { gunSpot, noteSpot, Rectangle.Inflate(gm.ExitButton(), 20 * k, 20 * k) }, 2));
        return list;
    }

    // 소품 그림의 찬 픽셀 (그림 묶음마다 한 번만 읽는다)
    static readonly ConditionalWeakTable<Sprites, Dictionary<string, bool[,]>> solids = new();
    static bool[,] SolidOf(Sprites sp, string name)
    {
        var map = solids.GetValue(sp, _ => new());
        return map.TryGetValue(name, out var s) ? s : map[name] = Props.Solid(sp.Get(name, 0, 1));
    }

    public void Frame(double dt)
    {
        var (gm, s, b) = (game!, ses!, host.Pet);
        s.Tick();
        var m = s.Mouse;
        forgive = Forgive(b.P.Stage); // 하는 중에 자랄 수도 있다
        if (s.RightHit) gm.Reload();
        if (s.LeftHit) Press(gm, m.X, m.Y);
        if (s.LeftUp || real && gm.Trigger && (Control.MouseButtons & MouseButtons.Left) == 0) gm.Release(); // 떼는 걸 놓쳐도 계속 쏘지 않게
        gm.Update(dt, m, hitTest);
        prey!.Update(dt, m);
        MoveBits(dt);
        if (real && window != IntPtr.Zero && gm.Aiming && gm.Clock > checkAt) // 게임하던 창이 사라지면 끝 (결과는 보여 준다)
        {
            checkAt = gm.Clock + .25;
            var st = Native.Probe(window);
            if (!st.Alive || st.Minimized) { Dbg.Log("shoot-window-gone"); gm.Finish(); }
        }
        SyncCursor(gm);
        Track();
        if (gm.Stage == Shoot.Phase.Done) Stop(false);
    }

    // 왼쪽 누름: 나가기 버튼, 결과 닫기, 아니면 쏘기 (디버그 명령 fire 도 여기로)
    void Press(Shoot gm, int x, int y)
    {
        string result = gm.Press(x, y, hitTest);
        if (Dbg.On && result is "exit" or "close" or "wait") Dbg.Log("press", ("result", result));
    }

    void OnShot(string result, int x, int y)
    {
        if (game is not { } gm || prey is not { } p) return;
        if (result == "hit") { p.Hit(gm.GunPivot.X, gm.GunPivot.Y); Sparks(gm, x, y); }
        else if (result == "miss") p.NearMiss(x, y);
        Snd.Play(Sounds, result == "miss" ? gm.Gun.Name : result);
        if (result is "hit" or "miss") Casing(gm);
        if (Dbg.On && result != "loaded") Dbg.Log("fire", ("result", result), ("x", x), ("y", y), ("ammo", gm.Ammo), ("hits", gm.Hits));
    }

    // 조준경 커서: 퍼진 만큼 십자가 벌어지고, 맞히면 잠깐 빨갛다. 끝날 때 터지는 동안은 숨기고, 결과 화면에서는 보통 커서. 바뀔 때만 갈아 끼운다
    void SyncCursor(Shoot gm)
    {
        if (ses?.Layer is not { } layer) return;
        int want = gm.Stage == Shoot.Phase.Boom ? -1 : !gm.Aiming ? -2
            : Crosshair.Level(gm.Bloom + gm.Gun.Spread * .7) * 2 + (gm.Clock < gm.HitUntil ? 1 : 0); // 샷건은 처음부터 넓다
        if (want == cursorShown) return;
        cursorShown = want;
        if (want == -1) layer.HidePointer();
        else if (want == -2) layer.ShowPointer();
        else layer.Cursor = Crosshair.Get(want / 2, want % 2 == 1, gm.Scale);
    }

    // quit(보스 키·디버그 stop): 결과 화면·표정 없이 바로 끝낸다. 기록은 조용히 남긴다.
    // 제대로 끝난 판은 놀아주기 한 번이다. 맞아도 기분은 안 내려간다: 한 번도 안 맞았으면 신나 하고, 6번 넘게 맞았으면 어지러워한다
    public void Stop(bool quit)
    {
        if (game is not { } gm) return;
        game = null;
        best = Math.Max(best, gm.Hits);
        var b = host.Pet;
        if (quit) ses?.Close(b, true, false, Id, ("hits", gm.Hits), ("shots", gm.Shots), ("esc", true));
        else
        {
            ses?.Close();
            b.LeavePuppet();
            b.P.Act(b.K, "play");
            if (gm.Hits == 0) { b.Greet("happy", 1.4); b.Emote("note"); }
            else b.Greet(gm.Hits >= 6 ? "dizzy" : "giggle", gm.Hits >= 6 ? 2 : 1.4);
            Dbg.Log("shoot-end", ("hits", gm.Hits), ("shots", gm.Shots), ("best", best));
        }
        (ses, prey, window, real) = (null, null, IntPtr.Zero, false);
        Reset();
        Dbg.Log("mode-end", ("id", Id));
        host.Save();
    }

    // ── 효과: 맞은 자리의 불티, 총 옆으로 튀는 탄피 ──
    struct Bit { public double X, Y, Vx, Vy, G, Life; public byte Kind; } // Kind 0 불티 · 1 탄피 · 2 산탄 탄피(빨강)
    const int MaxBits = 96;
    readonly Bag<Bit> bits = new(MaxBits);
    static readonly SolidBrush Brass = new(Color.FromArgb(220, 170, 60));

    double Rand(double a, double b) => a + fx.NextDouble() * (b - a);
    static int BitSize(int k) => Math.Max(4, 8 * k / 3);

    void Sparks(Shoot gm, int x, int y)
    {
        double s = gm.Scale / 3.0;
        for (int i = 0; i < 10 && !bits.Full; i++)
            bits.Add() = new Bit { X = x, Y = y, Vx = Rand(-220, 220) * s, Vy = Rand(-280, -40) * s, G = 700 * s, Life = Rand(.25, .5) };
    }

    void Casing(Shoot gm)
    {
        if (bits.Full) return;
        double a = gm.GunAngle(), len = 12 * gm.Scale * 2, s = gm.Scale / 3.0;
        bits.Add() = new Bit
        {
            X = gm.GunPivot.X + Math.Cos(a) * len, Y = gm.GunPivot.Y + Math.Sin(a) * len - 6 * gm.Scale,
            Vx = -Math.Sign(Math.Cos(a)) * Rand(120, 260) * s, Vy = -Rand(200, 380) * s, G = 1500 * s, Life = .6, Kind = (byte)(gm.Gun.Pellets > 1 ? 2 : 1),
        };
    }

    void MoveBits(double dt)
    {
        int keep = 0;
        for (int i = 0; i < bits.Count; i++)
        {
            ref var p = ref bits[i];
            (p.X, p.Y, p.Vy, p.Life) = (p.X + p.Vx * dt, p.Y + p.Vy * dt, p.Vy + p.G * dt, p.Life - dt);
            if (p.Life <= 0) continue;
            if (keep != i) bits[keep] = p;
            keep++;
        }
        bits.Trim(keep);
    }

    // ── 그리기: 소품, 탄흔, 쏜 자리 표시, 불티·탄피, 위쪽 표시(남은 시간·총알·맞힌 수), 나가기 버튼, 총, 끝의 조준경 폭발, 3·2·1, 결과 ──
    // 자주 찍는 그림은 한 번만 찾아 둔다 (개발 중 그림이 바뀌면 다시)
    Sprites? artOf;
    int artK;
    Weapon? artGun;
    Shoot? artGame;
    readonly Stamp[] hole = new Stamp[3];
    Stamp hitMark = null!, missMark = null!, exitArt = null!, gunIcon = null!, round = null!, roundOff = null!;
    Stamp[] decorArt = Array.Empty<Stamp>();
    GunArt gunArt = null!;
    Rectangle rowAt, holeBox;   // 총알·맞힌 수 줄 (화면 좌표), 탄흔 그림 셋을 다 덮는 자리 (탄흔 가운데에서)

    void Art(Sprites sp, Shoot gm)
    {
        int k = gm.Scale;
        if (ReferenceEquals(artOf, sp) && artK == k && ReferenceEquals(artGun, gm.Gun) && ReferenceEquals(artGame, gm)) return;
        var st = Stamps.Of(sp);
        (artOf, artK, artGun, artGame) = (sp, k, gm.Gun, gm);
        for (int i = 0; i < 3; i++) hole[i] = st.Get("game/hole" + i, 0, k);
        holeBox = Rectangle.Inflate(Rectangle.Union(hole[0].Box(0, 0), Rectangle.Union(hole[1].Box(0, 0), hole[2].Box(0, 0))), 1, 1);
        string ammo = gm.Gun.Pellets > 1 ? "game/shell" : "game/bullet";
        (hitMark, missMark, exitArt, gunIcon) = (st.Get("game/hit", 0, k), st.Get("game/spark", 0, k), st.Get("game/exit", 0, k), st.Get("game/" + gm.Gun.Name, 0, k));
        (round, roundOff) = (st.Get(ammo, 0, k), st.Get(ammo + "Off", 0, k));
        gunArt = new GunArt(sp, gm.Gun.Name, 2 * k);
        decorArt = new Stamp[gm.Decor.Count];
        for (int i = 0; i < decorArt.Length; i++) decorArt[i] = st.Get(gm.Decor[i].Name, 0, gm.Decor[i].Scale);

        var bar = BarAt(gm.Arena, k);
        int cy = bar.Y + 13 * k;
        rowAt = Rectangle.Union(gunIcon.Box(bar.X + 9 * k, cy), GameHud.StarRect(bar.Right - 22 * k, cy, k));
        rowAt = Rectangle.Union(rowAt, gm.Gun.Mag <= 8
            ? Rectangle.Union(round.Box(bar.X + 22 * k, cy), round.Box(bar.X + 22 * k + (gm.Gun.Mag - 1) * 6 * k, cy))
            : new Rectangle(bar.X + 20 * k, bar.Y + 10 * k, gm.Gun.Mag * 2 * k, 6 * k));
        rowAt.Inflate(1, 1);
    }

    // 위쪽 가운데: 남은 시간 막대. 그 아래 줄에 총 그림·총알·별 + 맞힌 수
    static Rectangle BarAt(Rectangle a, int k) => new(a.Left + a.Width / 2 - 60 * k, a.Top + 9 * k, 120 * k, 4 * k);
    static int BoomR(int k) => 36 * k;   // 끝날 때 조준경이 터지는 크기 (도트 한 칸은 2 × 배율)
    static double GuideX(Shoot gm) => gm.GunPivot.X - 90 * gm.Scale;  // 안내 말풍선: 총 왼쪽 바닥에 (총이 돌아도 닿지 않는 자리)
    static double GuideY(Shoot gm) => gm.Arena.Bottom - 2 * gm.Scale;

    // ── 다시 칠할 자리: 프레임마다 한 번 모아 둔다 (화면 좌표). 움직이는 것은 지금 자리(지난 자리는 그림 창이 기억한다),
    // 가만히 있는 것(총·막대·총알 줄·가운데 숫자)은 바뀐 프레임에만 ──
    readonly List<Rectangle> frame = new(256), holeDirty = new(32);
    Watch barW, rowW, midW;
    Rectangle gunShown;
    double gunA, gunKick;
    int gunFlash;
    bool tracked, guideWas, guideDirty;   // 첫 프레임에 자리를 모으기 전에는 그리지 않는다

    void Reset()
    {
        frame.Clear();
        holeDirty.Clear();
        bits.Trim(0);
        (barW, rowW, midW, gunShown, tracked, guideWas, guideDirty, artGame) = (default, default, default, default, false, false, false, null);
    }

    void OnHole(int x, int y) => holeDirty.Add(artGame == null ? Overlay.Around(x, y, 60, 60) : new Rectangle(x + holeBox.X, y + holeBox.Y, holeBox.Width, holeBox.Height));

    void Track()
    {
        var gm = game!;
        int k = gm.Scale;
        Art(host.Sprites, gm);
        frame.Clear();
        tracked = true;
        var a = gm.Arena;
        var bar = BarAt(a, k);
        barW.Set((int)(bar.Width * gm.TimeLeft / Shoot.PlaySec));
        rowW.Set(HashCode.Combine(gm.Loaded, gm.Hits));
        midW.Set(gm.Stage switch { Shoot.Phase.Count => gm.CountNumber, Shoot.Phase.Result => 9, _ => 0 });
        if (barW.Fresh) // 판이 막 열렸다: 한 번은 통째로 (창 윗변에 앉은 소품은 판 밖에 있다)
        {
            frame.Add(a);
            foreach (var p in gm.Decor) frame.Add(p.Bounds);
        }
        foreach (var r in holeDirty) frame.Add(r);
        holeDirty.Clear();
        for (int i = 0; i < gm.Marks.Count; i++)
        {
            ref var m = ref gm.Marks[i];
            frame.Add(Rectangle.Inflate((m.Hit ? hitMark : missMark).Box(m.X, m.Y), 1, 1));
        }
        int half = BitSize(k) / 2 + 2;
        for (int i = 0; i < bits.Count; i++) { ref var p = ref bits[i]; frame.Add(Overlay.Around(p.X, p.Y, half, half)); }

        // 총: 각도·밀림·불꽃이 바뀐 프레임에만 옛 자리와 새 자리. 값은 조금 뭉툭하게 잡는다 (가만히 있는데 밀림이 끝없이 0 으로 다가가며 다시 칠하지 않게)
        int flash = gm.Clock < gm.FlashUntil ? (int)(gm.Clock * 30) % 2 : -1; // 기관총은 쏘는 동안 깜빡인다
        double aim = gm.GunAngle(), ang = Math.Round((aim - gm.GunRise * Math.Sign(Math.Cos(aim))) * 512) / 512, kick = Math.Round(gm.GunKick * 2) / 2;
        if (gunShown.IsEmpty || ang != gunA || kick != gunKick || flash != gunFlash)
        {
            if (!gunShown.IsEmpty) frame.Add(gunShown);
            (gunA, gunKick, gunFlash) = (ang, kick, flash);
            gunShown = gunArt.Rect(gm.GunPivot.X, gm.GunPivot.Y, ang, kick, flash);
            frame.Add(gunShown);
        }
        if (barW.Changed) frame.Add(GameHud.BarRect(bar, k));
        if (rowW.Changed) frame.Add(rowAt);
        if (midW.Changed) { frame.Add(GameHud.CountRect(a, k)); frame.Add(GameHud.CenterRect(a, k)); }
        if (gm.Stage == Shoot.Phase.Boom) frame.Add(BlastArt.PopRect(gm.BoomX, gm.BoomY, BoomR(k), 2 * k));
        bool guide = ses?.GuideOn(gm.Clock) == true;
        (guideDirty, guideWas) = (barW.Fresh || guide != guideWas, guide);
        if (mock) { frame.Add(Fx.PetRect(host.Pet.X, host.Pet.Y, host.Pet.CellPx)); frame.Add(Overlay.Around(gm.AimX, gm.AimY, 30 * k, 30 * k)); }
    }

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (game is not { } gm || !tracked) return;
        foreach (var r in frame) rs.Add(o.Client(r));
        if (guideDirty) ses?.DirtyGuide(o, rs, 0, GuideX(gm), GuideY(gm)); // 안내 말풍선은 뜰 때와 사라질 때만
    }

    public void Draw(Overlay o, Graphics g)
    {
        if (game is not { } gm || !tracked) return;
        int k = gm.Scale;
        Art(o.Art, gm);
        var a = gm.Arena;
        if (mock) DrawMock(o, g, gm);
        for (int i = 0; i < decorArt.Length; i++)
        {
            var p = gm.Decor[i];
            if (g.IsVisible(o.Client(p.Bounds))) decorArt[i].DrawCell(g, p.Origin.X - o.Left, p.Origin.Y - o.Top, o.Art.Cell * p.Scale);
        }
        for (int i = 0; i < gm.HoleCount; i++) // 탄흔은 많다: 다시 칠하는 곳에 걸친 것만
        {
            ref var h = ref gm.Holes[i];
            var st = hole[h.Kind];
            if (g.IsVisible(h.X - o.Left + st.Dx, h.Y - o.Top + st.Dy, st.Bmp.Width, st.Bmp.Height)) st.Put(o, g, h.X, h.Y);
        }
        for (int i = 0; i < gm.Marks.Count; i++) { ref var m = ref gm.Marks[i]; (m.Hit ? hitMark : missMark).Put(o, g, m.X, m.Y); }
        int size = BitSize(k);
        for (int i = 0; i < bits.Count; i++)
        {
            ref var p = ref bits[i];
            g.FillRectangle(p.Kind == 0 ? Pal.Gold : p.Kind == 1 ? Brass : Pal.Red, (int)p.X - o.Left - size / 2, (int)p.Y - o.Top - size / 2, size, size);
        }

        var bar = BarAt(a, k);
        GameHud.Bar(o, g, bar, k, gm.TimeLeft / Shoot.PlaySec, Pal.Gold);
        if (g.IsVisible(o.Client(rowAt)))
        {
            int cy = bar.Y + 13 * k, loaded = gm.Loaded;
            gunIcon.Put(o, g, bar.X + 9 * k, cy);
            if (gm.Gun.Mag <= 8)
                for (int i = 0; i < gm.Gun.Mag; i++) (i < loaded ? round : roundOff).Put(o, g, bar.X + 22 * k + i * 6 * k, cy);
            else // 기관총: 탄창 눈금
            {
                var tick = o.ToClient(bar.X + 20 * k, bar.Y + 10 * k);
                for (int i = 0; i < gm.Gun.Mag; i++) g.FillRectangle(i < loaded ? Pal.Gold : Pal.Line, tick.X + i * 2 * k, tick.Y, k, 6 * k);
            }
            GameHud.Star(o, g, bar.Right - 22 * k, cy, k, gm.Hits);
        }

        var exit = gm.ExitButton();
        exitArt.Put(o, g, exit.X + exit.Width / 2, exit.Y + exit.Height / 2);

        // 게임 창 아래 테두리에 걸쳐 조준점을 향해 돌아가는 총. 쏘면 뒤로 밀리고 총구가 들리며 불꽃이 난다 (값은 Track 이 정한 것: 다시 칠한 자리와 꼭 맞게)
        if (o.Bounds.Contains(gm.GunPivot)) gunArt.Draw(g, gm.GunPivot.X - o.Left, gm.GunPivot.Y - o.Top, gunA, gunKick, gunFlash);
        if (gm.Stage == Shoot.Phase.Boom) BlastArt.Pop(o, g, gm.BoomX, gm.BoomY, BoomR(k), gm.T * BlastArt.Sec / Shoot.BoomSec, 2 * k);
        if (gm.Stage == Shoot.Phase.Count) GameHud.Count(o, g, a, k, gm.CountNumber);
        if (gm.Stage == Shoot.Phase.Result) GameHud.Result(o, g, a, k, gm.Hits, bestBefore, gm.Hits > bestBefore, "game/star"); // 끝: 맞힌 수 크게, 새 기록이면 큰 왕관
        ses?.DrawGuide(o, g, gm.Clock, GuideX(gm), GuideY(gm));
        if (mock && gm.Aiming) // 장면 그림에서만: 조준경 (실제 앱에서는 마우스 커서다)
        {
            var c = Crosshair.Art(Crosshair.Level(gm.Bloom + gm.Gun.Spread * .7), gm.Clock < gm.HitUntil, k);
            g.DrawImageUnscaled(c, (int)gm.AimX - o.Left - c.Width / 2, (int)gm.AimY - o.Top - c.Height / 2);
        }
    }

    // 장면 그림(--mode-scene)에서만: 게임 창과 그 위에 겹친 창을 밝은 네모로, 볼록이를 그 사이에 그려 넣는다 (실제 앱에서는 진짜 창과 본체 창이 그 자리에 있다)
    static readonly SolidBrush Glass = new(Color.FromArgb(232, 236, 242)), GlassBar = new(Color.FromArgb(96, 124, 168)), Glass2 = new(Color.FromArgb(214, 222, 234));
    readonly List<Rectangle> mockCovers = new();

    void DrawMock(Overlay o, Graphics g, Shoot gm)
    {
        int k = gm.Scale;
        var (r, pet) = (o.Client(gm.Arena), host.Pet);
        g.FillRectangle(Glass, r);
        g.FillRectangle(GlassBar, r.X, r.Y, r.Width, 8 * k);
        foreach (var c0 in mockCovers)
        {
            var c = o.Client(c0);
            g.FillRectangle(Glass2, c);
            g.FillRectangle(GlassBar, c.X, c.Y, c.Width, 8 * k);
        }
        string pose = Stamps.Of(host.Sprites).Has($"{pet.P.Stage}/0/{pet.Pose}") ? pet.Pose : "idle";
        if (pet.PuppetClip is not { } hidden) { Fx.Pet(o, g, host.Sprites, pet.P.Stage, pose, gm.Clock, k, pet.X, pet.Y, pet.Dir < 0); return; }
        using var mask = hidden.Clone(); // 창 뒤로 숨은 몸은 안 그린다 (본체 창이 하는 것과 같게)
        using var keep = g.Clip;
        mask.Translate(-o.Left, -o.Top);
        g.SetClip(mask, CombineMode.Exclude);
        Fx.Pet(o, g, host.Sprites, pet.P.Stage, pose, gm.Clock, k, pet.X, pet.Y, pet.Dir < 0);
        g.Clip = keep;
    }

    // 디버그: game [pistol|mg|shotgun] [garden|snack] (시작) / game stop / fire <x> <y> (그 자리를 쏜다) / reload. game 대신 shoot 도 된다
    public bool Command(string[] a)
    {
        if (a.Length == 0) return false;
        switch (a[0])
        {
            case "fire":
                if (game is { } gm) { Press(gm, int.Parse(a[1]), int.Parse(a[2])); gm.Release(); }
                return true;
            case "reload":
                game?.Reload();
                return true;
            case "game" or "shoot":
                if (a.Length > 1 && a[1] == "stop") Stop(true);
                else if (!Running && !host.Busy) Start(a.Length > 1 ? a[1] : null, a.Length > 2 ? a[2] : null);
                return true;
        }
        return false;
    }

    public IEnumerable<GameStat> Stats()
    {
        yield return new GameStat(L.T("game.shoot"), best > 0 ? L.T("game.shoot_val", best) : L.T("game.never"), best > 0);
    }

    sealed record Record(int Best);
    public string? SaveState() => best > 0 ? JsonSerializer.Serialize(new Record(best)) : null;

    public void LoadState(string? json) // 모듈이 되기 전 기록(SaveData.GameBest)도 {Best} 로 넘어온다 (GameModes.WithLegacy)
    {
        if (json == null) return;
        try { if (JsonSerializer.Deserialize<Record>(json) is { } r) best = r.Best; }
        catch (JsonException) { /* 깨진 기록은 버린다 */ }
    }

    // ── 화면 없이: 가짜 판 (자가 검사·장면·속도 재기) ──
    static readonly Rectangle TestArena = new(300, 160, 1320, 800);   // 1920×1080 모니터 안의 창 하나
    static readonly Rectangle TestCover = new(1100, 250, 400, 260);   // 그 위에 겹친 작은 창

    sealed class Rig
    {
        public readonly SceneHost Host;
        public readonly ShootGame Game;
        public readonly Shoot Gm;
        public readonly HashSet<Keys> Keys = new();
        public Point Mouse;

        public Rig(Rectangle arena, Weapon gun, int k = 3, int seed = 5, string stage = "child", Rectangle[]? covers = null, bool guide = false, bool decor = false, int best = 0, double x = 900, double y = 700)
        {
            Host = new SceneHost(stage, k);
            (Host.Pet.X, Host.Pet.Y) = (x, y);
            Game = new ShootGame(Host) { best = best };
            var rng = new Random(seed);
            var gm = new Shoot(arena, best, k, rng, gun, "garden") { GunPivot = new Point(arena.Left + arena.Width / 2, Math.Min(arena.Bottom + 6 * k, SceneHost.Area.Bottom - 2 * k)) };
            if (decor) gm.Decor = Game.Decorate(gm, new[] { new Win(new IntPtr(1), arena, false) }, new[] { SceneHost.Area }, rng);
            Game.ses = Session.Fake(arena, Keys.Contains, () => true, () => Mouse);
            if (guide) Game.ses.Guide(GuideKeys);
            var wins = (covers ?? Array.Empty<Rectangle>()).Select((r, i) => new Win(new IntPtr(10 + i), r, false)).ToList();
            Game.mockCovers.AddRange(covers ?? Array.Empty<Rectangle>());
            Gm = Game.Open(gm, Props.Covers(wins, arena, Host.Pet.CellPx), rng);
        }

        public Brain Pet => Host.Pet;
        public Prey Prey => Game.prey!;

        // 볼록이 그림은 자세마다 처음 쓸 때 그린다 (한 줄에 수십 ms): 재기 전에 미리 그려 둔다 (실제 앱에서는 본체 창이 그린다)
        public void WarmPet()
        {
            var (pose0, dir0) = (Pet.Pose, Pet.Dir);
            foreach (string pose in new[] { "walk", "idle", "hurt", "land", "peek", "giggle" })
                foreach (int dir in new[] { 1, -1 }) { (Pet.Pose, Pet.Dir) = (pose, dir); Host.HitPet(0, 0, 0); }
            (Pet.Pose, Pet.Dir) = (pose0, dir0);
        }
        public void Frame() { Pet.Update(1 / 60.0, false); Game.Frame(1 / 60.0); } // 앱처럼: 볼록이 → 게임
        public void Frames(int n) { for (int i = 0; i < n && Game.Running; i++) Frame(); }
        public void Click(MouseButtons button) { Game.ses!.Press(button, true); Frames(1); Game.ses?.Press(button, false); Frames(1); }
        public void Play() { while (Game.Running && Gm.Stage == Shoot.Phase.Count) Frame(); }   // 3·2·1 을 넘긴다
    }

    // 자동 조종: 조준점이 볼록이를 초당 520px 로 쫓고, 1.5초마다 1초씩 누르고 있다가 뗀다. 5초마다 오른쪽 버튼(장전)
    sealed class Bot
    {
        readonly Rig rig;
        double mx = 700, my = 400;
        public int F;
        public Bot(Rig rig) => this.rig = rig;

        public void Step()
        {
            var pet = rig.Pet;
            double tx = pet.X + 60 * Math.Sin(F * .05), ty = pet.Y - 54 + 40 * Math.Sin(F * .031), dx = tx - mx, dy = ty - my, d = Math.Sqrt(dx * dx + dy * dy) + 1, step = Math.Min(d, 520 / 60.0);
            (mx, my) = (mx + dx / d * step, my + dy / d * step);
            rig.Mouse = new Point((int)mx, (int)my);
            var io = rig.Game.ses!;
            if (F % 90 == 0) io.Press(MouseButtons.Left, true);
            if (F % 90 == 60) io.Press(MouseButtons.Left, false);
            if (F % 300 == 299) io.Press(MouseButtons.Right, true);
            F++;
        }
    }

    // 같은 씨로 한 판: 기관총으로 3·2·1 부터 결과가 닫힐 때까지 (--mode-bench shoot)
    public static string BenchRun()
    {
        var rig = new Rig(TestArena, Shoot.MachineGun, covers: new[] { TestCover }, guide: true, decor: true);
        using var ov = rig.Host.Overlay(rig.Game);
        var bot = new Bot(rig);
        rig.WarmPet();
        string res = Bench.Run("shoot", ov, rig.Game, TestArena, () => { if (!rig.Game.Running) return false; bot.Step(); return true; }, rig.Frame);
        return res + $" / 맞힘 {rig.Gm.Hits}, 쏨 {rig.Gm.Shots}, 탄흔 {rig.Gm.HoleCount}";
    }

    // 장면 다섯: 3·2·1 과 안내 → 쏘는 중(총구 불꽃·탄흔·쏜 자리) → 창 뒤에 숨은 볼록이 → 끝의 조준경 폭발 → 결과(새 기록 왕관).
    // 뒤에 총 이름을 주면 그 총으로: --mode-scene shoot 그림.png shotgun
    public static void Scene(string path, int k)
    {
        var gun = Shoot.Guns.FirstOrDefault(g => g.Name == GameModes.SceneArg) ?? Shoot.MachineGun;
        var arena = new Rectangle(960 - 180 * k, 1000 - 180 * k, 360 * k, 180 * k);
        var cover = new Rectangle(arena.Left + 200 * k, arena.Top + 40 * k, 110 * k, 70 * k);
        if (!SceneHost.Area.Contains(arena)) (arena, cover) = (new Rectangle(40, 60, 1840, 960), new Rectangle(1200, 300, 420, 300)); // 배율이 커서 모니터를 넘으면 모니터에 맞춘다
        var rig = new Rig(arena, gun, k, covers: new[] { cover }, guide: true, decor: true, x: arena.Left + 120 * k, y: arena.Bottom - 30 * k);
        var (game, gm) = (rig.Game, rig.Gm);
        game.mock = true;
        using var ov = rig.Host.Overlay(game);
        var bot = new Bot(rig);
        int f = 0, hidAt = -1, boomAt = -1, endAt = -1;
        SceneKit.Shots(path, ov, Rectangle.Intersect(Rectangle.Inflate(arena, 10 * k, 24 * k), SceneHost.Area), 5, shot =>
        {
            if (!game.Running) return true;
            f++;
            if (gm.Stage != Shoot.Phase.Result) bot.Step(); // 결과 화면은 누르지 않고 둔다
            if (shot == 2 && rig.Prey.Shield == null && f > 60 * 4 && gm.Stage == Shoot.Phase.Play) rig.Mouse = new Point((int)rig.Pet.X - 60 * k, (int)rig.Pet.Y - 20 * k); // 바짝 다가가 숨게 한다
            rig.Frame();
            if (!game.Running) return true;
            if (hidAt < 0 && shot == 2 && rig.Prey.Shield != null && rig.Pet.Pose is "peek" or "idle") hidAt = f;
            if (boomAt < 0 && gm.Stage == Shoot.Phase.Boom) boomAt = f;
            if (endAt < 0 && gm.Stage == Shoot.Phase.Result) endAt = f;
            return shot switch
            {
                0 => f == 20,
                1 => gm.Stage == Shoot.Phase.Play && gm.Clock < gm.FlashUntil && gm.HoleCount >= 6 && gm.Marks.Count > 0,
                2 => hidAt > 0 && f >= hidAt + 6 || gm.Stage != Shoot.Phase.Play,
                3 => boomAt > 0 && f == boomAt + 12,
                _ => endAt > 0 && f == endAt + 30,
            };
        });
    }

    // ── 자가 검사 (화면 없이) ──
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    public static string Check()
    {
        var arena = new Rectangle(0, 0, 1920, 1080);
        var rng = new Random(5);
        var k = new Tuning();
        bool Miss(int x, int y) => false;
        bool HitAll(int x, int y) => true;
        var still = new Point(900, 600);
        void Tick(Shoot g, double sec, Point mouse, Func<int, int, bool> hits) { for (int i = 0; i < sec * 60; i++) g.Update(1 / 60.0, mouse, hits); }
        double Far(Shoot g, int last = int.MaxValue) // 탄흔이 겨눈 곳에서 가장 멀리 간 거리 (마지막 last 개 중)
        {
            double far = 0;
            for (int i = Math.Max(0, g.HoleCount - last); i < g.HoleCount; i++) far = Math.Max(far, Math.Sqrt(Math.Pow(g.Holes[i].X - still.X, 2) + Math.Pow(g.Holes[i].Y - still.Y, 2)));
            return far;
        }

        var guns = Enumerable.Range(0, 60).Select(_ => new Shoot(arena, 0, 3, rng)).ToList();
        Expect(Shoot.Guns.All(w => guns.Any(g => g.Gun == w)), "무기는 셋 중 랜덤");
        Expect(guns.Select(g => g.Theme).Distinct().Count() == Props.Sets.Count, "소품 묶음도 랜덤");

        // 샷건: 한 번에 여러 알이 퍼지고, 한 알이라도 맞으면 명중 하나. 같은 거리에서 권총보다 잘 맞는다
        var sg = new Shoot(arena, 0, 3, rng, Shoot.Shotgun);
        Tick(sg, Shoot.CountSec + .05, still, Miss);
        sg.Press(900, 600, Miss);
        sg.Release();
        Expect(sg.Shots == 1 && sg.Ammo == 4 && sg.HoleCount == 8 && sg.Marks.Count == 8, "샷건 한 발에 8알");
        Expect(Far(sg) > 20, "산탄이 퍼진다");
        sg.Press(900, 600, Miss);
        Expect(sg.Shots == 1, "펌프하는 동안은 못 쏜다");
        bool Near(int x, int y) => Math.Pow(x - 940, 2) + Math.Pow(y - 600, 2) < 28 * 28; // 겨눈 곳에서 40px 옆의 작은 표적
        int RateOf(Weapon wpn)
        {
            int hits = 0;
            for (int i = 0; i < 300; i++)
            {
                var gg = new Shoot(arena, 0, 3, new Random(i), wpn);
                gg.Update(Shoot.CountSec + .05, still, Miss);
                gg.Press(900, 600, Near);
                hits += gg.Hits;
            }
            return hits;
        }
        int sgRate = RateOf(Shoot.Shotgun), pRate = RateOf(Shoot.Pistol);
        Expect(sgRate > pRate * 3 && sgRate > 150, $"대충 겨눠도 샷건이 잘 맞는다: 샷건 {sgRate}/300, 권총 {pRate}/300");
        var once = new Shoot(arena, 0, 3, rng, Shoot.Shotgun);
        once.Update(Shoot.CountSec + .05, still, Miss);
        once.Press(900, 600, HitAll);
        Expect(once.Hits == 1 && once.HoleCount == 0, "여러 알이 맞아도 명중은 하나");

        // 권총: 한 번 누르면 한 발, 6발 뒤 장전
        var p = new Shoot(arena, 3, 3, rng, Shoot.Pistol);
        Expect(p.Press(500, 500, HitAll) == "wait", "3·2·1 동안은 안 나간다");
        Tick(p, Shoot.CountSec + .05, still, Miss);
        Expect(p.Stage == Shoot.Phase.Play, "3·2·1 뒤에 시작한다");
        p.Press(900, 600, Miss);
        Tick(p, .5, still, Miss);
        Expect(p.Shots == 1 && p.Marks.Count == 0, "권총은 누르고 있어도 한 발, 쏜 자리 표시는 잠깐만");
        p.Release();
        for (int i = 0; i < 5; i++) { p.Press(900, 600, Miss); p.Release(); Tick(p, .2, still, Miss); }
        Expect(p.Ammo == 0 && p.Shots == 6 && p.HoleCount == 6 && p.ReloadLeft > 0, "6발 쏘면 알아서 장전을 시작한다");
        Expect(p.Press(900, 600, Miss) == "empty", "장전하는 동안은 안 나간다");
        p.Release();
        Tick(p, .75, still, Miss);
        Expect(p.Ammo == 6, "장전하면 6발");
        p.Press(900, 600, Miss); p.Release();
        p.Reload();
        Expect(p.ReloadLeft > 0, "오른쪽 클릭 장전도 된다");
        Tick(p, .75, still, Miss);

        // 기관총: 누르고 있으면 연사. 조준점은 마우스에서 안 벗어나고, 탄이 퍼진다
        var m = new Shoot(arena, 0, 3, rng, Shoot.MachineGun);
        Tick(m, Shoot.CountSec + .05, still, Miss);
        m.Press(still.X, still.Y, Miss);
        Tick(m, 1, still, Miss);
        Expect(m.Shots is >= 10 and <= 13, $"기관총은 누르고 있으면 연사: 1초에 {m.Shots}발");
        Expect(m.AimX == still.X && m.AimY == still.Y, "반동이 있어도 조준점은 마우스 그대로 (Doom 처럼)");
        Expect(m.Bloom > 20 && Crosshair.Level(m.Bloom) >= 3, $"연사하면 퍼짐이 커지고 조준경 십자가 벌어진다: {m.Bloom:F0}");
        Expect(Far(m, 4) > 8, "탄이 퍼진다");
        m.Release();
        Tick(m, .8, still, Miss);
        Expect(m.Bloom < 3 && Crosshair.Level(m.Bloom) == 0, $"손을 떼면 금방 모인다: {m.Bloom:F1}");
        m.Press(still.X, still.Y, Miss);
        Tick(m, 2, still, Miss); // 남은 탄을 다 쏘고(약 1.6초) 장전하는 중 (장전 0.9초)
        m.Release();
        Expect(m.Ammo == 0 && m.ReloadLeft > 0 || m.Ammo == Shoot.MachineGun.Mag, $"다 쏘면 알아서 장전한다 (탄 {m.Ammo}, 장전 {m.ReloadLeft:F2})");
        Tick(m, Shoot.MachineGun.ReloadSec + .05, still, Miss);
        Expect(m.Ammo == Shoot.MachineGun.Mag, "자동 장전이 끝나면 30발");

        // 탄흔은 120개까지: 넘으면 가장 오래된 것부터 지워지고, 생기고 지워진 자리를 알려 준다 (그 둘레만 다시 칠한다)
        var many = new Shoot(arena, 0, 3, rng, Shoot.Shotgun);
        int told = 0;
        many.OnHole = (_, _) => told++;
        Tick(many, Shoot.CountSec + .05, still, Miss);
        for (int i = 0; i < 17; i++) { many.Press(900, 600, Miss); many.Release(); Tick(many, .6, still, Miss); if (many.ReloadLeft > 0) Tick(many, .75, still, Miss); }
        Expect(many.Shots == 17 && many.HoleCount == Shoot.MaxHoles && told == 17 * 8 + (17 * 8 - Shoot.MaxHoles), $"탄흔은 {Shoot.MaxHoles}개까지 ({many.Shots}발, {many.HoleCount}개, 알림 {told}번)");

        // 20초 뒤 조준경이 터지고, 결과는 클릭해야 닫힌다. 결과 중 클릭은 뒤 창으로 가지 않는다
        var end = new Shoot(arena, 0, 3, rng, Shoot.Pistol);
        Tick(end, Shoot.CountSec + .05, still, Miss);
        end.Press(still.X, still.Y, HitAll);
        end.Release();
        Tick(end, Shoot.PlaySec, still, Miss);
        Expect(end.Stage == Shoot.Phase.Boom && !end.Aiming, "20초가 지나면 조준경이 터진다");
        Expect(end.Press(500, 500, HitAll) == "wait" && end.Hits == 1, "터지는 동안엔 안 쏴진다");
        Tick(end, Shoot.BoomSec + .05, still, Miss);
        Expect(end.Stage == Shoot.Phase.Result && end.NewBest && end.Best == 1, "그다음 결과와 최고 기록");
        Expect(end.Press(500, 500, HitAll) == "close" && end.Stage == Shoot.Phase.Result, "결과는 잠깐은 보여 준다");
        Tick(end, Shoot.ResultMinSec, still, Miss);
        Expect(end.Press(500, 500, HitAll) == "close" && end.Stage == Shoot.Phase.Done, "그 뒤 클릭하면 닫힌다");
        var idle = new Shoot(arena, 0, 3, rng);
        Tick(idle, Shoot.CountSec + Shoot.PlaySec + Shoot.BoomSec + Shoot.ResultSec + .2, still, Miss);
        Expect(idle.Stage == Shoot.Phase.Done, "안 눌러도 결과는 저절로 닫힌다");

        var quit = new Shoot(arena, 0, 3, rng);
        Tick(quit, Shoot.CountSec + .05, still, Miss);
        var exit = quit.ExitButton();
        Expect(quit.Press(exit.X + exit.Width / 2, exit.Y + exit.Height / 2, HitAll) == "exit" && quit.Stage == Shoot.Phase.Result, "나가기 버튼");

        // 명중은 그림의 실제 픽셀로. 봐주는 둘레는 아기 6, 어린이 4, 어른 3칸 (어른 모습이 몇이든 adult 로 시작하면 어른)
        var sp = Sprites.Embedded();
        var one = sp.Get("child/0/idle", 0, 1);
        Expect(Sprites.Near(one, 20, 30, 2) && !Sprites.Near(one, 1, 1, 2), "그림의 실제 픽셀로 맞힌다");
        int adults = 0;
        foreach (string form in Showcase.Forms)
        {
            int pad = Forgive(form);
            Expect(pad == (form == "baby" ? 6 : form == "child" ? 4 : 3), $"봐주는 둘레 ({form}): {pad}칸");
            if (pad == 3) adults++;
            foreach (int scale in new[] { 3, 5 })
            {
                // 몸 왼쪽 끝에서 pad - 1 칸 떨어진 자리는 맞고, pad + 2 칸 떨어진 자리는 빗나간다 (권총 한 발은 2 × 배율/3 px 안에서 흔들린다)
                foreach (bool near in new[] { true, false })
                {
                    var rig = new Rig(arena, Shoot.Pistol, scale, stage: form);
                    rig.Gm.Update(Shoot.CountSec + .05, still, Miss);
                    var pet = rig.Pet;
                    var body = sp.Get(pet.SpriteName(), 0, 1, pet.Dir < 0);
                    var box = Ui.Opaque(body, 0);
                    int row = box.Top;
                    while (body.GetPixel(box.Left, row).A == 0) row++; // 몸의 맨 왼쪽 픽셀이 있는 줄
                    int cells = near ? pad - 1 : pad + 2;
                    int x = (int)(pet.X - pet.CellPx / 2.0) + (box.Left - cells) * scale + scale / 2, y = (int)(pet.Y - pet.CellPx) + row * scale + scale / 2;
                    string res = rig.Gm.Press(x, y, rig.Game.hitTest);
                    Expect(res == (near ? "hit" : "miss"), $"{form} ×{scale}: 몸에서 {cells}칸 떨어진 자리는 {(near ? "맞는다" : "빗나간다")} (봐주는 둘레 {pad}칸): {res}");
                }
            }
        }
        Expect(adults >= 4 && Forgive("adult_아직_없는_모습") == 3, $"어른 모습 {adults}가지 모두 3칸");

        // 창 윗변 소품
        var solids = Props.All.ToDictionary(n => n, n => Props.Solid(sp.Get(n, 0, 1)));
        var front = new Win(new IntPtr(1), new Rectangle(300, 300, 900, 500), false);
        var names = Props.Sets["garden"];
        int sat = 0;
        for (int seed = 0; seed < 10; seed++)
        {
            var on = Props.OnWindows(new[] { front }, new[] { arena }, names, new Random(seed), n => solids[n], Array.Empty<Rectangle>());
            sat += on.Count;
            Expect(on.All(q => Math.Abs(q.Bounds.Bottom - front.R.Top) <= 3 && q.Bounds.Left >= front.R.Left && q.Bounds.Right <= front.R.Right), "창 윗변에 앉는다");
            Expect(on.All(q => q.Bounds.Height < 120), "창 위 소품은 작다");
        }
        Expect(sat >= 10, $"창 위에 소품이 놓인다: {sat}");
        Expect(Props.OnWindows(new[] { new Win(new IntPtr(1), arena, false) }, new[] { arena }, names, rng, n => solids[n], Array.Empty<Rectangle>()).Count == 0, "최대화 창 위에는 안 놓는다");
        Expect(Props.OnWindows(new[] { new Win(new IntPtr(2), new Rectangle(0, 200, 1920, 200), false), front }, new[] { arena }, names, rng, n => solids[n], Array.Empty<Rectangle>())
            .All(q => Math.Abs(q.Bounds.Bottom - front.R.Top) > 3), "덮인 창의 윗변에는 안 놓는다 (덮은 창 윗변은 괜찮다)");
        Expect(Props.OnWindows(new[] { front with { Topmost = true } }, new[] { arena }, names, rng, n => solids[n], Array.Empty<Rectangle>()).Count == 0, "항상 위 창은 빼고");

        // 게임할 창 고르기
        var screensOne = new[] { arena };
        var used = new Win(new IntPtr(5), new Rectangle(300, 200, 900, 600), false);
        Expect(Shoot.ChooseArena(used, new[] { used }, arena, screensOne, 120) == (used.R, used.H), "메뉴 전에 쓰던 창에서 한다");
        var tiny = new Win(new IntPtr(6), new Rectangle(10, 10, 200, 150), false);
        var bigWin = new Win(new IntPtr(7), new Rectangle(100, 100, 1400, 800), false);
        Expect(Shoot.ChooseArena(tiny, new[] { tiny, used, bigWin }, arena, screensOne, 120).Window == bigWin.H, "쓰던 창이 너무 작으면 가장 큰 창");
        Expect(Shoot.ChooseArena(null, Array.Empty<Win>(), arena, screensOne, 120) == (arena, IntPtr.Zero), "창이 없으면 모니터 전체");
        var off = new Win(new IntPtr(8), new Rectangle(1500, 700, 900, 600), false);
        Expect(arena.Contains(Shoot.ChooseArena(off, new[] { off }, arena, screensOne, 120).Arena), "화면 밖으로 나간 부분은 자른다");

        // 창 안에서만 논다
        Brain NewPet(double x, double y, World? world = null)
        {
            var pet = new Brain(new Pet { Stage = "child", Full = 90, Mood = 50, Energy = 90 }, k, world ?? new World(new[] { arena }), arena) { X = x, Y = y };
            pet.StartPuppet();
            return pet;
        }
        void Step(Brain pet, Prey pr, Point at) { pet.Mouse = at; pet.Update(1 / 60.0, false); pr.Update(1 / 60.0, at); } // 앱처럼: 볼록이 → 게임
        var room = used.R;
        var inRoom = NewPet(1700, 1000);
        var roomPrey = new Prey(inRoom, room, null, new Random(1));
        for (int i = 0; i < 600; i++)
        {
            Step(inRoom, roomPrey, new Point(room.Left + (i * 37) % room.Width, room.Top + (i * 53) % room.Height));
            Expect(room.Contains((int)inRoom.X, (int)inRoom.Y - 1), $"게임 창 안에서만 움직인다 ({inRoom.X:F0},{inRoom.Y:F0})");
        }
        var floor = Props.OnFloor(room, Props.Sets["snack"], new Random(2), n => solids[n], new[] { new Rectangle(room.Left + room.Width / 2 - 200, room.Bottom - 200, 400, 200) });
        Expect(floor.Count == 2 && floor.All(q => room.Contains(q.Bounds) && q.Bounds.Bottom >= room.Bottom - 8 && q.Bounds.Height < 120), "창 아래쪽 안에 작은 소품 둘");
        Expect(floor.All(q => !q.Bounds.IntersectsWith(new Rectangle(room.Left + room.Width / 2 - 200, room.Bottom - 200, 400, 200))), "총 자리는 비운다");

        var aimGame = new Shoot(arena, 0, 3, rng) { GunPivot = new Point(960, 1040) };
        aimGame.Update(.01, new Point(1500, 400), Miss);
        Expect(aimGame.GunAngle() is > -Math.PI / 2 and < 0, "총구가 오른쪽 위 조준점을 향한다");
        aimGame.Update(.01, new Point(200, 1070), Miss);
        Expect(aimGame.GunAngle() < -Math.PI / 2, "총구는 아래로는 안 돌아간다");

        // 엄폐: 커서가 다가오면 실제 창 가장자리 뒤로 숨고, 창 안쪽 몸은 안 맞고 안 그린다. 그 창 위의 창도 함께 가린다
        var cw = new Rectangle(600, 300, 600, 400);
        var top = new Rectangle(1100, 250, 400, 200);
        var covs = Props.Covers(new[] { new Win(new IntPtr(2), top, false), new Win(new IntPtr(1), cw, false), new Win(new IntPtr(3), arena, false) }, arena, 120);
        Expect(covs.Count == 2, "최대화 창은 엄폐물에서 뺀다");
        var hide = NewPet(300, 900);
        var hidePrey = new Prey(hide, arena, new[] { covs[1] }, new Random(1));
        bool ducked = false;
        int duckAt = 0;
        for (; duckAt < 600 && !ducked; duckAt++)
        {
            Step(hide, hidePrey, new Point((int)hide.X - 200, (int)hide.Y - 60));
            ducked = hidePrey.Shield == covs[1];
        }
        Expect(ducked && hide.PuppetClip != null, "커서가 다가오면 창 뒤로 숨는다 (숨은 몸은 안 그린다)");
        Expect(hidePrey.Shielded(cw.X + 10, cw.Y + 10) && hidePrey.Shielded(top.Right - 5, top.Top + 5) && !hidePrey.Shielded(cw.X - 10, cw.Y - 10),
            "숨은 창과 그 위 창 안쪽만 가려 준다");
        for (int i = 0; i < 240; i++) Step(hide, hidePrey, new Point(100, 100));
        Expect(hidePrey.Shield == null && hide.PuppetClip == null, "잠깐 숨었다가 다시 나온다");

        // 도망 다니기: 달리고 멈추기를 되풀이한다. 모니터 밖으로 안 나가고, 쫓아와도 거리를 벌린다
        var prey = NewPet(900, 700, new World(new[] { new Rectangle(-1920, 0, 1920, 1080), arena }));
        var run = new Prey(prey, arena, null, new Random(1));
        double far = 0;
        int still2 = 0, moving = 0, quick = 0;
        var mouse = new PointF(1500, 300);
        for (int i = 0; i < 600; i++)
        {
            double dx = prey.X - mouse.X, dy = prey.Y - 50 - mouse.Y, d = Math.Sqrt(dx * dx + dy * dy) + 1;
            mouse = new PointF((float)(mouse.X + dx / d * 4), (float)(mouse.Y + dy / d * 4)); // 커서가 초당 240px 로 쫓아온다
            var (px, py) = (prey.X, prey.Y);
            Step(prey, run, Point.Round(mouse));
            if (Math.Abs(prey.X - px) + Math.Abs(prey.Y - py) < .5) still2++; else moving++;
            if (prey.Pose == "walk" && prey.PuppetMs == 70) quick++;
            Expect(arena.Contains((int)prey.X, (int)prey.Y - 1) && prey.Mode == Mode.Puppet, $"게임 모니터 안에 있다 ({prey.X:F0},{prey.Y:F0})");
            far += d;
        }
        Expect(still2 > 60 && moving > 120, $"달리고 멈추기를 되풀이한다: 멈춤 {still2}, 달림 {moving}");
        Expect(far / 600 > 150, $"쫓아와도 거리를 벌린다: 평균 {far / 600:F0}px");
        Expect(quick > 100, $"도망칠 땐 발을 빨리 구른다 (그림 한 장 70ms): {quick}프레임");

        // 맞으면 멈칫하고 번쩍이며 총 반대쪽으로 밀린다
        var shot = NewPet(900, 600);
        var shotPrey = new Prey(shot, arena, null, new Random(1));
        shotPrey.Hit(960, 1040);
        var (hx, hy) = (shot.X, shot.Y);
        Expect(hy < 600 && shotPrey.Frozen && shot.Now < shot.FlashUntil, "총 반대쪽(위)으로 밀리고 멈칫, 번쩍");
        shotPrey.Update(.03, still);
        Expect(shot.X == hx && shot.Y == hy, "멈칫하는 동안은 안 움직인다");
        shotPrey.Update(.05, still);
        for (int i = 0; i < 20; i++) shotPrey.Update(1 / 60.0, still);
        Expect(shot.X != hx || shot.Y != hy, "멈칫이 끝나면 달아난다");
        prey.P.Stage = "adult_chubby";
        prey.Advance(0, false);
        Expect(prey.Mode == Mode.Puppet, "게임 중엔 다른 동작이 끼어들지 않는다");

        // 조작: 글자 키·Space 로는 아무 일도 없다. 왼쪽 = 쏘기(기관총은 누르는 동안), 오른쪽 = 장전. 하는 중에 또 시작해도 그대로
        var play = new Rig(TestArena, Shoot.MachineGun, covers: new[] { TestCover }, guide: true, decor: true);
        var (game, gm) = (play.Game, play.Gm);
        play.Mouse = new Point(500, 300);
        play.Play();
        foreach (var key in new[] { Keys.R, Keys.Space, Keys.A, Keys.D, Keys.W, Keys.S, Keys.Tab, Keys.Left, Keys.Up, Keys.ControlKey, Keys.ShiftKey }) play.Keys.Add(key);
        play.Frames(30);
        Expect(gm.Shots == 0 && gm.ReloadLeft == 0, "키로는 아무 일도 없다 (조준·쏘기·장전은 마우스만)");
        play.Keys.Clear();
        game.ses!.Press(MouseButtons.Left, true);
        play.Frames(60);
        game.ses.Press(MouseButtons.Left, false);
        int shots = gm.Shots;
        play.Frames(20);
        Expect(shots is >= 10 and <= 13 && gm.Shots == shots, $"왼쪽을 누르고 있는 동안만 쏜다: {shots}발");
        play.Click(MouseButtons.Right);
        Expect(gm.ReloadLeft > 0, "오른쪽 버튼 = 장전");
        Expect(play.Pet.Mode == Mode.Puppet && TestArena.Contains((int)play.Pet.X, (int)play.Pet.Y - 1), "볼록이는 꼭두각시로 게임 창 안에서 도망 다닌다");
        game.Start();
        Expect(ReferenceEquals(game.game, gm) && game.Command(new[] { "game" }) && ReferenceEquals(game.game, gm), "하는 중에 또 시작해도 판은 그대로다");

        // 디버그 명령: fire <x> <y> 는 그 자리를 쏘고, reload 는 장전, game stop 은 보스 키처럼 끝낸다
        play.Frames(60);
        (shots, int ammo) = (gm.Shots, gm.Ammo);
        Expect(game.Command(new[] { "fire", "500", "300" }) && gm.Shots == shots + 1 && !gm.Trigger, "디버그 fire: 한 발 쏘고 뗀다");
        Expect(game.Command(new[] { "reload" }) && gm.ReloadLeft > 0 && gm.Ammo == ammo - 1, "디버그 reload");
        Expect(!game.Command(new[] { "raid" }) && !game.Command(Array.Empty<string>()), "남의 명령은 받지 않는다");

        // 보스 키(Stop(true)): 결과 화면·표정 없이 바로 끝난다. 숨어 있었어도 몸이 다시 다 보이고, 기록은 조용히 남는다
        gm.Hits = 4;
        play.Pet.PuppetClip = covs[0].Mask();
        Expect(game.Command(new[] { "game", "stop" }) && !game.Running, "디버그 game stop");
        Expect(play.Pet.Mode == Mode.Wander && !play.Pet.Emoting && play.Pet.PuppetClip == null && play.Pet.PuppetMs == 0 && play.Pet.P.Plays == 0, "보스 키로 끝내면 결과 화면·표정 없이 바로 끝난다");
        Expect(game.best == 4 && game.SaveState()!.Contains('4'), "보스 키로 끝내도 기록은 조용히 남는다");
        var rects = new List<Rectangle>(2048);
        using (var ov0 = play.Host.Overlay(game))
        using (var peek = new Bitmap(64, 64))
        using (var pg = Graphics.FromImage(peek))
        {
            game.Dirty(ov0, rects);
            game.Draw(ov0, pg);
            Expect(rects.Count == 0 && peek.GetPixel(10, 10).A == 0, "끝나면 그림도 같은 프레임에 사라진다 (다시 칠할 자리도, 그릴 것도 없다)");
        }

        // 제대로 끝난 판: 20초 → 조준경 폭발 → 결과 → 0.8초 뒤 누르면 닫힌다. 한 판은 놀아주기 한 번이고, 맞은 수에 따라 신나거나 깔깔대거나 어지러워한다
        string EndWith(int hits, out Rig rig)
        {
            rig = new Rig(TestArena, Shoot.Pistol, best: 5);
            rig.Play();
            rig.Gm.Hits = hits;
            rig.Gm.T = Shoot.PlaySec - .1;
            rig.Frames(10);
            Expect(rig.Gm.Stage == Shoot.Phase.Boom, "20초가 지나면 조준경이 터진다 (모듈)");
            rig.Frames((int)(Shoot.BoomSec * 60) + 2);
            Expect(rig.Gm.Stage == Shoot.Phase.Result && rig.Game.Running, "터진 뒤 결과 화면");
            rig.Click(MouseButtons.Left);
            Expect(rig.Game.Running, "결과는 잠깐은 보여 준다 (모듈)");
            rig.Frames((int)(Shoot.ResultMinSec * 60));
            rig.Click(MouseButtons.Left);
            Expect(!rig.Game.Running && rig.Pet.Mode == Mode.Anim && rig.Pet.P.Plays == 1 && rig.Pet.PuppetClip == null, $"0.8초 뒤 누르면 닫히고, 한 판은 놀아주기 한 번 (맞힘 {hits})");
            return rig.Pet.Pose;
        }
        Expect(EndWith(0, out var none) == "happy" && none.Pet.Emoting && none.Game.best == 5, "한 번도 안 맞았으면 신나 한다");
        Expect(EndWith(3, out var some) == "giggle" && some.Game.best == 5, "조금 맞았으면 깔깔댄다");
        Expect(EndWith(8, out var lots) == "dizzy" && lots.Game.best == 8 && lots.Game.SaveState()!.Contains('8'), "6번 넘게 맞았으면 어지러워하고, 새 기록이 남는다");

        // 옛 기록(SaveData.GameBest)은 id shoot 으로 넘어온다
        var old = new ShootGame(new SceneHost());
        Expect(!old.Stats().First().Played, "처음엔 기록이 없다");
        old.LoadState(GameModes.WithLegacy(new SaveData { GameBest = 7 }).GetValueOrDefault("shoot"));
        Expect(old.best == 7 && old.Stats().First().Played, "옛 기록(GameBest)을 이어받는다");
        old.LoadState("깨진 글");
        Expect(old.best == 7, "깨진 기록은 버린다");

        // 다시 칠할 자리: 3·2·1 부터 쏘고 숨고 터지고 결과가 닫힐 때까지. 바뀐 자리만 다시 그려도 화면에 지나간 그림이 남지 않는다
        var paint = new Rig(TestArena, Shoot.MachineGun, covers: new[] { TestCover }, guide: true, decor: true);
        using var ov = paint.Host.Overlay(paint.Game);
        var bot = new Bot(paint);
        int painted = 0;
        var (bad, first) = SceneKit.Repaint(ov, paint.Game, TestArena, () =>
        {
            bot.Step();
            if (bot.F == 600) paint.Gm.T = Shoot.PlaySec - .3; // 10초쯤 하고 끝으로 건너뛴다
            paint.Frame();
            paint.Pet.EmoteUntil = 0; // 놀리는 표정(머리 위 그림)은 그림 창이 따로 칠한다: 여기서는 게임이 칠하는 것만 본다
            painted++;
            return paint.Game.Running;
        }, 60 * 40, every: 5);
        Expect(bad == 0 && !paint.Game.Running && paint.Gm.Shots > 30 && paint.Gm.HoleCount > 10, $"바뀐 자리만 다시 칠해도 화면이 맞다: {bad}번 다름 (처음 {first}프레임), 쏨 {paint.Gm.Shots}");

        // 하는 동안 메모리를 거의 새로 잡지 않는다
        var lean = new Rig(TestArena, Shoot.MachineGun, covers: new[] { TestCover }, decor: true);
        using var ov2 = lean.Host.Overlay(lean.Game);
        var bot2 = new Bot(lean);
        lean.WarmPet();
        for (int i = 0; i < 150; i++) { bot2.Step(); lean.Frame(); rects.Clear(); lean.Game.Dirty(ov2, rects); }
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 600; i++) { bot2.Step(); lean.Game.Frame(1 / 60.0); rects.Clear(); lean.Game.Dirty(ov2, rects); }
        long perFrame = (GC.GetAllocatedBytesForCurrentThread() - a0) / 600;
        Expect(lean.Gm.Shots > 60 && perFrame < 200, $"쏘는 동안 프레임마다 새로 잡는 메모리가 거의 없다: {perFrame}B ({lean.Gm.Shots}발)");
        a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++) { bot2.Step(); lean.Pet.Update(1 / 60.0, false); }
        long petFrame = (GC.GetAllocatedBytesForCurrentThread() - a0) / 300; // 앱이 프레임마다 돌리는 볼록이 갱신(Brain.Update)이 잡는 것 (게임 밖의 몫)

        return $"총쏘기: 샷건 {sgRate}/300·권총 {pRate}/300 명중, 봐주는 둘레 아기 6·어린이 4·어른 3칸({adults}모습), {duckAt}프레임 만에 창 뒤로 숨음, 달림 {moving}·멈춤 {still2}프레임(평균 {far / 600:F0}px 떨어짐), {painted}프레임 다시 칠하기, 쏘는 동안 {perFrame}B/프레임 (볼록이 갱신은 따로 {petFrame}B)";
    }
}
