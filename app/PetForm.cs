using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using Microsoft.Win32;

namespace Bolok;

public sealed record RunRecord(DateTime At, int Level, int Score, int Seeds);

public sealed class SaveData
{
    public Pet Pet { get; set; } = new();
    public double X { get; set; }
    public double Y { get; set; }
    public List<int[]> Holes { get; set; } = new();
    public int GameBest { get; set; }
    public bool GameSound { get; set; }
    public bool AutoUpdate { get; set; }
    public string Species { get; set; } = Sprites.DefaultSpecies;
    public List<string> Met { get; set; } = new();               // 알에서 깨어나 만나 본 종 (도감)
    public List<ReleasedPet> Released { get; set; } = new();     // 자연으로 떠난 친구들
    public int RunBest { get; set; }
    public int RunLast { get; set; }
    public int RunLevel { get; set; } = 1;
    public int BreakBest { get; set; }
    public int WormsWins { get; set; }
    public string VisitDay { get; set; } = "";       // 떠난 친구 방문: 오늘 계획을 세운 날
    public int VisitMin { get; set; } = -1;           // 그날 몇 시 몇 분(분)에 올지. -1 이면 오늘은 안 온다
    public string LastVisitDay { get; set; } = "";    // 마지막으로 놀러 온 날 (하루 한 번)
    public Dictionary<string, DateTime> Visits { get; set; } = new(); // 종마다 마지막으로 놀러 온 때
    public string? Language { get; set; }             // 설정에서 고른 언어. 없으면 윈도우 언어
    public Dictionary<string, string> GameData { get; set; } = new(); // 게임 모듈 기록 (id → JSON 글)
    public Clover.Saved? Clover { get; set; }         // 네잎클로버 (바닥·머리·오늘 돋았나)
    public int Variant { get; set; }                  // 색 변형: 0 기본 색, 1·2 (알에서 깰 때 무작위)
    public int CardVariant { get; set; }              // 엽서 보낼 친구의 색
    public List<GiftRecord> Gifts { get; set; } = new();     // 추억: 친구가 두고 간 선물
    public List<PhotoRecord> Album { get; set; } = new();    // 추억: 자랄 때마다 찍힌 사진
    public List<CardRecord> Cards { get; set; } = new();     // 추억: 놀러 왔던 친구가 보낸 엽서
    public Dictionary<string, int> VisitCounts { get; set; } = new(); // 우정 하트: 종마다 놀러 온 횟수
    public string? CardFrom { get; set; }             // 엽서를 보낼 친구와 그 모습, 그날 (그날 이후 처음 볼 때)
    public string CardForm { get; set; } = "child";
    public string CardDay { get; set; } = "";
    public string MorningDay { get; set; } = "";      // 아침·밤 인사를 한 날
    public string NightDay { get; set; } = "";
    public List<RunRecord> RunScores { get; set; } = new();   // 최근 50판
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}

// 볼록이 본체 창. 투명색 자리는 클릭이 뒤로 지나가고, 몸통 픽셀만 마우스를 받는다
public sealed class PetForm : Form, IGameHost
{
    public static readonly Color Key = Color.FromArgb(255, 0, 255);

    static readonly Dictionary<string, int> PoseMs = new()
    {
        ["idle"] = 600, ["walk"] = 130, ["sleep"] = 800, ["eat"] = 180, ["happy"] = 220,
        ["sad"] = 500, ["hungry"] = 500, ["chomp"] = 240, ["spit"] = 320, ["held"] = 240,
        ["fly"] = 70, ["land"] = 100, ["dizzy"] = 180,
        ["peek"] = 700, ["shock"] = 180, ["shy"] = 420, ["angry"] = 130, ["freeze"] = 300, ["look"] = 500,
        ["nuzzle"] = 380, ["squish"] = 200, ["giggle"] = 110, ["flop"] = 400, ["love"] = 260, ["wink"] = 300, ["pout"] = 420,
        ["hurt"] = 90, ["groom"] = 200, ["stretch"] = 400, ["hop"] = 130, ["roll"] = 120, ["dig"] = 160, ["think"] = 700,
    };

    readonly Brain b;
    readonly string savePath;
    readonly bool debug;
    readonly List<Overlay> overlays = new();
    readonly ClickPad pad = new();
    readonly List<GameLayer> layers = new();
    Game? game;
    bool pointerShown;
    Cover? clipShown;
    bool flashShown;
    IntPtr gameFocus, gameWindow;
    int gameBest;
    readonly Hud hud;
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem sleepItem = new();
    readonly ToolStripMenuItem updateItem = new() { Visible = false };
    readonly List<(ToolStripItem Item, Func<string> Text)> menuTexts = new(); // 언어가 바뀌면 다시 쓴다
    string? language, updateTag;
    bool autoUpdate;        // 기본 꺼짐. 설정 창 「업데이트」 탭에서 켠다
    Settings? settings;
    readonly NotifyIcon tray = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    DevLink? dev;
    Sprites sprites;
    readonly string? assetsDir;   // 디버그: 그림을 assets 폴더에서 읽는다 (일반 실행은 exe 안의 그림)
    double lastSec, nextSave, nextTop, nextBelow, fps, frameMs;
    Rectangle scaledFor;
    bool locked, pressing, moved, below;
    Point pressAt;
    PointF pressCell;
    (string, int, bool, int) drawn;
    IntPtr returnFocus;

    public PetForm(Sprites sprites, Tuning k, string dataDir, string? runDir, string? assetsDir)
    {
        this.sprites = sprites;
        species = sprites.BaseSpecies;
        this.assetsDir = assetsDir;
        debug = runDir != null;
        savePath = Path.Combine(dataDir, "pet.json");
        memDir = Path.Combine(dataDir, "memories");
        var area = Screen.PrimaryScreen!.WorkingArea;
        b = new Brain(LoadPet(k), k, new World(Screen.AllScreens.Select(s => s.WorkingArea)), area) { Scale = ScaleFor(k) };
        modes = GameModes.Create(this); // 게임 모듈 (app/Games): 기록을 읽기 전에 만든다
        if (debug) modes.Add(showcase = new Showcase(this, PoseMs)); // 디버그 앱: 캐릭터 확인 (모든 종의 아기·어린이·어른)
        b.Windows = Native.ListWindows;
        b.Foreground = () => Native.OwnForeground() ? b.Active : Native.Foreground(); // 메뉴를 열어도 활성 창이 바뀐 걸로 치지 않는다
        Restore();

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Key;
        TransparencyKey = Key;
        Text = L.Name;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        ClientSize = new Size(b.CellPx + 16, b.CellPx);

        BuildOverlays();
        pad.MouseDown += (_, e) => OnMouseDown(e); // 받침은 볼록이 창과 같은 자리·크기라 좌표가 그대로다
        pad.MouseMove += (_, e) => OnMouseMove(e);
        pad.MouseUp += (_, e) => OnMouseUp(e);
        hud = new Hud(b, debug ? DebugLine : null) { RunLine = () => runLast > 0 ? $"달리기 Lv.{runLevel} {runLast} (최고 {runBest})" : "" };
        hud.SpeciesName = () => b.P.Stage == "egg" ? "" : L.Species(species);
        if (!debug) // 새 버전을 받아 바꿔 두면 메뉴에 알린다. 그냥 둬도 다음에 켤 때 새 버전이다
            Updater.Start(() => autoUpdate, tag => BeginInvoke(() => { updateTag = tag; updateItem.Text = L.T("menu.update", tag); updateItem.Visible = true; settings?.ShowReady(); }));
        foreach (var o in overlays) { o.Runner = () => runner; o.Break = () => breakout; o.WormsGame = () => worms; o.MinigunGame = () => minigun; o.IcbmGame = () => icbm; o.Guest = () => visitor; o.GuestSprites = () => visitorSprites; o.Present = () => present; o.Clover = () => clover.State == Clover.Phase.Ground ? (clover.Kind, clover.X, clover.Y) : null; o.Dreams = Dreams; o.Modes = () => modes; }
        hud.PlaceAt(area);
        if (runDir != null) dev = new DevLink(runDir, assetsDir!, b, this);

        BuildMenu();
        tray.Icon = Icon.FromHandle(this.sprites.Get("child/0/idle", 0, 1).GetHicon());
        tray.Text = L.Name;
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowSettings("status"); }; // 상태는 설정 창에서
        L.Changed += Localize;

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.DisplaySettingsChanged += (_, _) => BeginInvoke(ReloadScreens);
        timer.Tick += (_, _) => Frame();
        Shown += (_, _) =>
        {
            ShowOverlays();
            if (debug) hud.Show();
            Native.Topmost(Handle);
            lastSec = clock.Elapsed.TotalSeconds;
            timer.Start();
            Dbg.Log("start", ("stage", b.P.Stage), ("scale", b.Scale), ("screens", DescribeScreens()));
        };
        FormClosing += (_, _) => { Save(); tray.Visible = false; Dbg.Log("quit"); };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var c = base.CreateParams;
            c.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return c;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_DPICHANGED) return; // 크기는 SyncScale 이 정한다
        base.WndProc(ref m);
    }

    int ScaleFor(Tuning k, int dpi = 96) => k.Scale > 0 ? k.Scale : Math.Max(2, (int)Math.Round(3 * dpi / 96.0));

    public void ApplyScale() => scaledFor = default;

    // 모니터마다 배율이 다를 수 있다. 건너가면 그 모니터에 맞춰 크기를 다시 정한다
    void SyncScale()
    {
        if (b.Area == scaledFor) return;
        scaledFor = b.Area;
        int s = ScaleFor(b.K, Native.DpiAt(new Point(b.Area.X + 10, b.Area.Y + 10)));
        if (s != b.Scale) { b.Scale = s; drawn = default; Dbg.Log("scale", ("value", s)); }
        ClientSize = new Size(b.CellPx + 16, b.CellPx);
    }

    static string DescribeScreens() => string.Join(" ", Screen.AllScreens.Select(s =>
        $"{s.WorkingArea.X},{s.WorkingArea.Y},{s.WorkingArea.Width}x{s.WorkingArea.Height}@{Native.DpiAt(new Point(s.Bounds.X + 10, s.Bounds.Y + 10))}"));

    void BuildOverlays()
    {
        foreach (var s in Screen.AllScreens) overlays.Add(new Overlay(b, () => sprites, () => game, s.Bounds));
    }

    void ShowOverlays()
    {
        foreach (var (o, s) in overlays.Zip(Screen.AllScreens))
        {
            o.Show();
            o.Bounds = s.Bounds; // 다른 배율의 모니터로 옮겨지며 어긋나지 않게 다시 맞춘다
        }
    }

    void ReloadScreens() // 모니터를 꽂거나 뺐을 때
    {
        b.World = new World(Screen.AllScreens.Select(s => s.WorkingArea));
        b.Settle();
        scaledFor = default;
        foreach (var o in overlays) o.Dispose();
        overlays.Clear();
        BuildOverlays();
        ShowOverlays();
        hud.PlaceAt(Screen.PrimaryScreen!.WorkingArea);
        Native.Topmost(Handle);
        Dbg.Log("screens-changed", ("screens", DescribeScreens()));
    }

    public void SwapSprites(Sprites s) { sprites = s; drawn = default; capSpot.Clear(); cloverSpot.Clear(); Invalidate(); foreach (var o in overlays) o.Invalidate(); } // 머리 자리는 그림마다 다시 잰다

    // 키우는 종. 그림(sprites)은 아직 못 받았으면 다른 종일 수 있다 — 알일 때는 공통 그림만 쓰니 티가 안 난다
    string species;
    int variant; // 색 변형 (0 기본 색)
    public string Species => species;
    public string SpriteKey => PetArt.Key(species, variant); // 그림 열쇠: "cat#1" 처럼 색까지

    // 캐릭터 그림은 데이터로 그린다 (assets/pets/art.json). 개발은 저장소 것을 읽어 고치면 바로 다시 그린다
    PetArt Art => assetsDir != null ? PetArt.FromDir(assetsDir) : PetArt.Embedded;
    Sprites? SpritesFor(string id) => Art.Can(id) ? Sprites.Drawn(Art, id, assetsDir) : null;

    // 캐릭터 바꾸기: 그림만 바뀌고 키우던 수치는 그대로. 모르는 종(없어진 종)이면 햄스터. v: 색 변형
    public void SetSpecies(string id, int v = 0)
    {
        if (!Art.Can(id)) { Dbg.Log("species-unknown", ("id", id)); id = Sprites.DefaultSpecies; }
        species = id;
        variant = Math.Clamp(v, 0, Art.Variants(id) - 1);
        SwapSprites(SpritesFor(PetArt.Key(id, variant))!);
        Dbg.Log("species", ("id", id), ("variant", variant));
    }

    // 새 알: 종도 색도 무작위 (YELL 2026-11-12: 종마다 색 세 벌 중 하나)
    void NewEgg(string id) => SetSpecies(id, Random.Shared.Next(Art.Variants(id)));


    string DebugLine() => $"{b.Mode}/{b.Pose} ({(int)b.X},{(int)b.Y}) x{b.Speed:0.#} {fps:0}fps cpu {dev?.Cpu:0.0}%{(Afk() ? " 자리비움" : "")}";

    Pet LoadPet(Tuning k)
    {
        if (!File.Exists(savePath)) return new Pet();
        try
        {
            pendingSave = JsonSerializer.Deserialize<SaveData>(File.ReadAllText(savePath));
            var pet = pendingSave?.Pet ?? new Pet();
            double away = pendingSave == null ? 0 : (DateTime.UtcNow - pendingSave.SavedAt).TotalMinutes;
            if (away >= k.AfkMin) pet.Tick(k, away, true); // 오래 꺼져 있던 시간은 잔 것으로 친다
            return pet;
        }
        catch (Exception ex)
        {
            Dbg.Log("load-error", ("msg", ex.Message));
            return new Pet();
        }
    }

    SaveData? pendingSave;

    void Restore()
    {
        if (pendingSave == null) // 처음 켰다(또는 저장을 못 읽었다): 첫 알도 무엇이 나올지 모른다 (YELL 2026-11-12)
        {
            if (b.P.Stage == "egg")
            {
                string first = RandomSpecies();
                NewEgg(first); // 햄스터가 아니면 알인 동안 그림을 받는다 (못 받으면 깨어나길 기다린다)
                TakePhoto("egg");
                Dbg.Log("first-egg", ("species", first));
            }
            return;
        }
        gameBest = pendingSave.GameBest;
        Sfx.On = pendingSave.GameSound;
        autoUpdate = pendingSave.AutoUpdate;
        if (pendingSave.Species != species || pendingSave.Variant != variant) SetSpecies(pendingSave.Species, pendingSave.Variant);
        (met, released) = (pendingSave.Met?.ToHashSet() ?? new(), pendingSave.Released ?? new());
        if (b.P.Stage != "egg") met.Add(species); // 도감 전부터 키우던 아이도 도감에
        (breakBest, wormsWins) = (pendingSave.BreakBest, pendingSave.WormsWins);
        (visitDay, visitMin, lastVisitDay, visits) = (pendingSave.VisitDay ?? "", pendingSave.VisitMin, pendingSave.LastVisitDay ?? "", pendingSave.Visits ?? new());
        language = pendingSave.Language;
        L.Use(language);
        foreach (var m in modes) m.LoadState(pendingSave.GameData?.GetValueOrDefault(m.Id));
        clover.Load(pendingSave.Clover);
        (gifts, album, cards, visitCounts) = (pendingSave.Gifts ?? new(), pendingSave.Album ?? new(), pendingSave.Cards ?? new(), pendingSave.VisitCounts ?? new());
        (cardFrom, cardForm, cardDay, morningDay, nightDay, cardVariant) = (pendingSave.CardFrom, pendingSave.CardForm ?? "child", pendingSave.CardDay ?? "", pendingSave.MorningDay ?? "", pendingSave.NightDay ?? "", pendingSave.CardVariant);
        (runBest, runLast, runLevel, runScores) = (pendingSave.RunBest, pendingSave.RunLast, Math.Max(1, pendingSave.RunLevel), pendingSave.RunScores ?? new());
        if (pendingSave.X != 0) { b.X = pendingSave.X; b.Y = pendingSave.Y; b.Settle(); }
        foreach (var h in pendingSave.Holes) b.AddHole(h[0], h[1], false);
        pendingSave = null;
    }

    void Save()
    {
        var data = new SaveData { Pet = b.P, X = b.X, Y = b.Y, Holes = b.Holes.Select(h => new[] { h.Cx, h.Cy }).ToList(), GameBest = gameBest, GameSound = Sfx.On, AutoUpdate = autoUpdate, Species = species, Met = met.ToList(), Released = released, RunBest = runBest, RunLast = runLast, RunLevel = runLevel, RunScores = runScores, BreakBest = breakBest, WormsWins = wormsWins, VisitDay = visitDay, VisitMin = visitMin, LastVisitDay = lastVisitDay, Visits = visits, Language = language, Clover = clover.Snapshot(), Variant = variant, CardVariant = cardVariant,
            GameData = modes.Select(m => (m.Id, Json: m.SaveState())).Where(x => x.Json != null).ToDictionary(x => x.Id, x => x.Json!),
            Gifts = gifts, Album = album, Cards = cards, VisitCounts = visitCounts, CardFrom = cardFrom, CardForm = cardForm, CardDay = cardDay, MorningDay = morningDay, NightDay = nightDay };
        var tmp = savePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data));
        File.Move(tmp, savePath, true);
    }

    void BuildMenu()
    {
        void Add(string key, Action go) => menuTexts.Add((menu.Items.Add(L.T(key), null, (_, _) => go()), () => L.T(key)));
        // 돌보기 / 게임 / 앱 / 종료 를 분리선으로 나눈다
        var feedItem = menu.Items.Add("", null, (_, _) => b.Care("feed")); // 밥 주기 (바구니에 남은 먹이)
        menuTexts.Add((feedItem, () => L.T("menu.feed_n", b.P.Food, b.K.FoodMax)));
        Add("menu.play", () => b.Care("play"));
        sleepItem.Click += (_, _) => b.Care(b.P.Asleep ? "wake" : "sleep");
        menu.Items.Add(sleepItem);
        menuTexts.Add((sleepItem, () => L.T(b.P.Asleep ? "menu.wake" : "menu.sleep")));
        menu.Items.Add(new ToolStripSeparator());
        Add("menu.shoot", () => BeginInvoke(StartGame)); // 메뉴가 닫히고 포커스가 돌아온 뒤에 시작한다
        Add("menu.run", () => BeginInvoke(StartRun));
        Add("menu.breakout", () => BeginInvoke(StartBreak));
        Add("menu.minigun", () => BeginInvoke(StartMinigun));
        Add("menu.icbm", () => BeginInvoke(StartIcbm));
        foreach (var m in modes) if (m.MenuKey is { } key) Add(key, () => BeginInvoke(() => { if (!InGame && b.P.Stage != "egg") m.Start(); })); // 게임 모듈
        var cannon = new ToolStripMenuItem(); // 대포를 고르면 체력이 정해진다
        menuTexts.Add((cannon, () => L.T("menu.fortress")));
        foreach (var gun in Worms.Guns)
        {
            var item = cannon.DropDownItems.Add("", null, (_, _) => BeginInvoke(() => StartWorms(gun.Name)));
            menuTexts.Add((item, () => gun.Shells > 1 ? L.T("menu.gun_shells", gun.Label, gun.Hp, gun.Shells) : L.T("menu.gun", gun.Label, gun.Hp)));
        }
        menu.Items.Add(cannon);
        if (showcase != null) { menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(showcase.Menu(menuTexts, () => InGame, a => BeginInvoke(a))); }
        menu.Items.Add(new ToolStripSeparator());
        Add("menu.settings", () => ShowSettings());
        updateItem.Click += (_, _) => { Save(); Updater.Restart(); };
        menu.Items.Add(updateItem);
        menuTexts.Add((updateItem, () => L.T("menu.update", updateTag ?? "")));
        menu.Items.Add(new ToolStripSeparator());
        Add("menu.quit", Application.Exit);
        Localize();
        menu.Opening += (_, _) => { sleepItem.Text = L.T(b.P.Asleep ? "menu.wake" : "menu.sleep"); Localize(); }; // 바구니 수도 새로
        menu.Closed += (_, _) =>
        {
            if (returnFocus != IntPtr.Zero) Native.SetForegroundWindow(returnFocus);
            returnFocus = IntPtr.Zero;
        };
    }

    // ── 게임 모듈에 빌려주는 것 (IGameHost) ──
    readonly List<IGameMode> modes;
    readonly Showcase? showcase;
    Brain IGameHost.Pet => b;
    Sprites IGameHost.Sprites => sprites;
    bool IGameHost.Busy => InGame || b.P.Stage == "egg";
    GameLayer IGameHost.OpenLayer(Rectangle field, MouseEventHandler down, MouseEventHandler up, Action quit) { gameFocus = Native.GetForegroundWindow(); return OpenLayer(field, down, up, quit); }
    void IGameHost.CloseLayer() => CloseLayer();
    void IGameHost.ShowPet(bool show) { if (show) { Show(); Native.Topmost(Handle); } else Hide(); }
    void IGameHost.Save() => Save();
    Sprites? IGameHost.SpritesOf(string id) => SpritesFor(id);

    // 디버그 명령 중 게임 모듈 것 (<게임 id> ...)
    public bool ModeCommand(string[] a) => modes.Any(m => m.Command(a));

    // 언어가 바뀌면 메뉴·트레이 글을 다시 쓴다 (설정 창은 스스로 다시 짠다)
    void Localize()
    {
        foreach (var (item, text) in menuTexts) item.Text = text();
        tray.Text = L.Name;
        Text = L.Name;
    }

    // 설정 → 일반에서 언어를 골랐다
    public void ChooseLanguage(string code)
    {
        language = code;
        L.Use(code);
        Save();
    }

    void OnSessionSwitch(object? s, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) locked = true;
        if (e.Reason == SessionSwitchReason.SessionUnlock) locked = false;
    }

    bool Afk() => dev?.ForceAfk ?? (locked || Native.IdleMinutes() >= b.K.AfkMin);

    void Frame()
    {
        long started = Stopwatch.GetTimestamp();
        double sec = clock.Elapsed.TotalSeconds, dt = Math.Min(.25, sec - lastSec);
        lastSec = sec;
        if (dt > 0) fps = fps * .95 + .05 / dt;

        b.Mouse = MousePosition;
        BossKeyTick();
        if (breakAuto && breakout is { } auto && auto.Balls.Where(x => !x.Stuck).MaxBy(x => x.Vy > 0 ? x.Y : x.Y - 2000) is { } low)
            b.Mouse = new Point((int)low.X, b.Mouse.Y); // 디버그 시험: 가장 낮게 내려오는 공을 받는다
        if (runner != null) RunFrame(dt);
        b.Update(dt, !InGame && Afk());
        if (breakout != null) BreakFrame(dt);
        if (worms != null) WormsFrame(dt);
        if (minigun != null) MinigunFrame(dt);
        foreach (var m in modes) if (m.Running) m.Frame(dt);
        if (leaving) LeaveFrame(dt);
        WatchHatch();
        CloverTick();
        GestureTick();
        VisitTick();
        PresentTick();
        if (visitor != null) visitor.Update(dt, b.X, b.Y, b.Mode is Mode.Wander or Mode.Anim || b.P.Asleep, b.P.Asleep, b.Area);
        if (icbm != null) IcbmFrame(dt);
        SyncScale();
        if (game != null)
        {
            game.Update(dt, MousePosition, HitPet);
            SyncCrosshair();
            if (gameWindow != IntPtr.Zero && game.Stage is Game.Phase.Count or Game.Phase.Play && b.Now > nextWindowCheck)
            {
                nextWindowCheck = b.Now + .25;
                var st = Native.Probe(gameWindow);
                if (!st.Alive || st.Minimized) { Dbg.Log("game-window-gone"); game.Finish(); } // 게임하던 창이 사라지면 끝
            }
            if (game.Stage == Game.Phase.Result && !pointerShown) { pointerShown = true; foreach (var l in layers) l.ShowPointer(); }
            if (game.Stage == Game.Phase.Done) EndGame();
        }

        var at = new Point((int)b.X - Width / 2, (int)b.Y - Height);
        if (at != Location) Location = at;

        string name = b.SpriteName();
        int ms = b.Mode == Mode.Run && b.Pose == "walk" ? 60 : b.Mode == Mode.Held && b.Pose == "fly" ? 50 : b.Mode == Mode.Prey && b.Pose == "walk" ? 70 : PoseMs.GetValueOrDefault(b.Pose, 600); // 흔들리거나 도망칠 땐 더 빨리
        int frame = (int)(sec * 1000 / ms);
        int shake = b.Now < b.ShakeUntil ? (int)(Math.Sin(b.Now * 40) * 5) : 0;
        var now = (name, frame, b.Dir < 0, shake);
        bool flash = b.Now < b.FlashUntil || b.Blink;
        if (now != drawn || b.CoverProp != clipShown || flash != flashShown) { drawn = now; clipShown = b.CoverProp; flashShown = flash; Invalidate(); }

        int want = b.Mode is Mode.Thrown or Mode.Held or Mode.Prey or Mode.Run or Mode.Paddle or Mode.Puppet ? 15 : 30; // 빠르게 움직일 때만 자주 그린다
        if (timer.Interval != want) timer.Interval = want;

        SyncPad();
        long syncStart = Stopwatch.GetTimestamp();
        foreach (var o in overlays) o.Sync();
        if (breakout != null) perf.Sync(Stopwatch.GetElapsedTime(syncStart).TotalMilliseconds);
        hud.Sync();
        dev?.Poll(fps, frameMs);

        if (b.Now > nextSave) { nextSave = b.Now + 60; Save(); }
        ApplyZOrder();
        double took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        frameMs = frameMs * .95 + .05 * took;
        if (breakout != null && !perfSkip) perf.Frame(took, dt);
        perfSkip = false;
    }

    // 벽돌깨기 한 판의 시간 재기: 프레임 전체, 규칙(Update), 화면 알림(Sync), 그리기(OnPaint)
    sealed class Perf
    {
        int frames;
        double frameSum, frameMax, updSum, updMax, syncSum, syncMax, dtSum, dtMax;
        public void Frame(double ms, double dt) { frames++; (frameSum, frameMax, dtSum, dtMax) = (frameSum + ms, Math.Max(frameMax, ms), dtSum + dt, Math.Max(dtMax, dt)); }
        public void Update(double ms) => (updSum, updMax) = (updSum + ms, Math.Max(updMax, ms));
        public void Sync(double ms) => (syncSum, syncMax) = (syncSum + ms, Math.Max(syncMax, ms));
        public (string, object?)[] Report()
        {
            int n = Math.Max(1, frames), p = Math.Max(1, Overlay.Paints);
            return new (string, object?)[] { ("fps", Math.Round(frames / Math.Max(.001, dtSum))), ("gapMax", Math.Round(dtMax * 1000)),
                ("frameAvg", Math.Round(frameSum / n, 2)), ("frameMax", Math.Round(frameMax, 1)), ("updAvg", Math.Round(updSum / n, 2)), ("updMax", Math.Round(updMax, 1)),
                ("syncAvg", Math.Round(syncSum / n, 2)), ("syncMax", Math.Round(syncMax, 1)),
                ("paintAvg", Math.Round(Overlay.PaintMsSum / p, 2)), ("paintMax", Math.Round(Overlay.PaintMsMax, 1)), ("paints", Overlay.Paints) };
        }
    }
    Perf perf = new();
    bool perfSkip; // 판을 까는 프레임(창 목록·판 만들기)은 재지 않는다

    // 평소엔 맨 앞이고, 일부러 숨을 때만 그 창 바로 아래로 들어간다
    void SyncPad()
    {
        bool on = b.ClickPadOn && !b.Behind && game == null;
        if (on)
        {
            pad.Bounds = Bounds;
            if (!pad.Visible) pad.Show();
            Native.PutBelow(pad.Handle, Handle); // 몸통 픽셀은 볼록이 창이, 나머지는 받침이 받는다
        }
        else if (pad.Visible) pad.Hide();
    }

    void ApplyZOrder()
    {
        if (b.Behind)
        {
            if (!below) { below = true; nextBelow = 0; Native.NotTopmost(Handle); }
            if (b.Now <= nextBelow) return;
            nextBelow = b.Now + 1; // 다른 창을 눌러 순서가 바뀌어도 그 창 바로 뒤에 붙어 있는다
            Native.PutBelow(Handle, b.HideWnd);
            var z = Native.ZGap(Handle, b.HideWnd);
            if (!z.Below || z.VisibleBetween > 0) Dbg.Log("z-off", ("below", z.Below), ("between", z.Between), ("visible", z.VisibleBetween));
        }
        else if (below)
        {
            below = false;
            nextTop = b.Now + 5;
            Native.Topmost(Handle);
        }
        else if (b.Now > nextTop)
        {
            nextTop = b.Now + 5;
            Native.Topmost(Handle);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var (name, frame, flip, shake) = drawn;
        if (name == null) return;
        if (b.CoverProp is { } c) // 소품 뒤로 숨은 부분은 안 그린다
        {
            using var mask = c.Mask().Clone();
            mask.Translate(-Left, -Top);
            e.Graphics.SetClip(mask, CombineMode.Exclude);
        }
        var bmp = sprites.Get(name, frame, b.Scale, flip);
        if (b.Blink) return; // 다친 뒤 깜빡
        if (b.Now < b.FlashUntil) // 맞으면 하얗게 번쩍
        {
            using var white = new System.Drawing.Imaging.ImageAttributes();
            white.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix(new[]
            {
                new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 }, new float[] { 1, 1, 1, 0, 1 },
            }));
            e.Graphics.DrawImage(bmp, new Rectangle(8 + shake, 0, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, white);
        }
        else e.Graphics.DrawImageUnscaled(bmp, 8 + shake, 0);
        DrawClover(e.Graphics, name, frame, flip, shake);
        DrawCap(e.Graphics, name, frame, flip, shake);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            pressing = true; moved = false; pressAt = MousePosition;
            float cx = (e.X - 8f) / b.Scale; // 볼록이 칸 안의 자리. 뒤집혀 있으면 좌우를 되돌린다
            pressCell = new PointF(b.Dir < 0 ? 40 - cx : cx, (float)e.Y / b.Scale);
        }
        else if (e.Button == MouseButtons.Right)
        {
            returnFocus = Native.GetForegroundWindow(); // 메뉴를 닫으면 일하던 창으로 돌려준다
            Native.SetForegroundWindow(Handle);
            menu.Show(MousePosition);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!pressing || moved) return;
        var p = MousePosition;
        if (Math.Abs(p.X - pressAt.X) + Math.Abs(p.Y - pressAt.Y) > 6)
        {
            moved = true;
            b.Lift();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !pressing) return;
        pressing = false;
        if (moved) b.Drop();
        else if (b.P.Asleep) b.Care("wake");
        else b.Click(pressCell.X, pressCell.Y);
    }

    // ── 쿠키런 모드: 일하던 창은 그대로, 키보드로만 ──
    Runner? runner;
    int runBest, runLast, runLevel = 1;
    List<RunRecord> runScores = new();

    public void StartRun()
    {
        if (InGame || b.P.Stage == "egg") return;
        var area = b.Area; // 볼록이가 있는 모니터 아래쪽
        var solids = new Dictionary<string, bool[,]>();
        (int, int, int, int) Measure(string name, int scale)
        {
            if (!solids.TryGetValue(name, out var solid)) solids[name] = solid = Props.Solid(sprites.Get(name, 0, 1));
            var (x0, y0, x1, y1) = Props.Trim(solid);
            return ((x1 - x0 + 1) * scale, (y1 - y0 + 1) * scale, x0 * scale, (y1 + 1) * scale);
        }
        var (rx, ry) = PetArt.Embedded.Size(b.P.Stage); // 판정은 지금 몸 크기대로 (아기는 작게, 어른은 크게)
        runner = new Runner(area, area.Bottom - b.K.FloorGap, b.CellPx, RunBody.Of(rx, ry), new Random(), Measure, runLevel); // 저장된 레벨에서 시작
        runner.OnEvent = e =>
        {
            switch (e)
            {
                case "jump": case "jump2": Sfx.Play("empty"); break;
                case "seed": Sfx.Play("hit"); break;
                case "hit": Sfx.Play("pistol"); b.Emote("bang", .5); break;
                case "heart": b.Emote("heart", .8); break;
                case "level": Sfx.Play("reload"); break; // 머리 위 별 + 레벨 숫자는 Overlay.DrawRun 이 그린다
            }
            if (e is "hit" or "heart" or "over" or "level") Dbg.Log("run", ("event", e), ("energy", (int)(runner?.Energy ?? 0)), ("score", runner?.Score));
        };
        runner.Keys(Native.KeyDown(Keys.ControlKey), false, 0); // 메뉴를 고를 때 눌려 있던 Ctrl 로 바로 뛰지 않게
        uint petCap = Native.HideFromCapture(Handle, true); // 화면 공유·녹화에는 안 찍히게
        foreach (var o in overlays) Native.HideFromCapture(o.Handle, true);
        Dbg.Log("run-capture", ("affinity", petCap));
        pad.Hide();
        b.StartRun(runner);
    }

    // 키보드는 포커스 없이 상태만 읽는다: Ctrl 톡 점프, Shift 누르기 슬라이드, Ctrl+Shift 0.5초 끝
    void RunFrame(double dt)
    {
        var r = runner!;
        bool quit = r.Keys(Native.KeyDown(Keys.ControlKey), Native.KeyDown(Keys.ShiftKey), dt);
        r.Update(dt);
        if (r.Over || quit) EndRun(quit);
    }

    public void StopRun() { if (runner != null) EndRun(false, record: false); } // 디버그 시험 판: 기록에 안 남긴다

    const double RunScoreSec = 2.5;
    int runEnds; // 끝난 횟수: 늦게 도는 「녹화에 다시 보이기」가 그 사이 새로 시작한 판을 건드리지 않게

    void RunCapture(bool hide)
    {
        Native.HideFromCapture(Handle, hide);
        foreach (var o in overlays) Native.HideFromCapture(o.Handle, hide);
    }

    void EndRun(bool quit, bool record = true)
    {
        var r = runner!;
        runner = null;
        b.EndRun();
        int ended = ++runEnds;
        if (r.Over && !quit) // 끝까지 달렸으면 머리 위에 점수를 잠깐. Esc·Ctrl+Shift 로 숨길 땐 아무것도 안 띄운다
        {
            b.EmoteUntil = 0; // 별 말풍선 자리에 점수가 뜬다
            foreach (var o in overlays) o.ShowRunScore(r.Score, record && r.Score > runBest, RunScoreSec);
            // 점수가 떠 있는 동안은 화면 공유·녹화에 계속 안 찍히게 둔다
            var later = new System.Windows.Forms.Timer { Interval = (int)(RunScoreSec * 1000) + 200 };
            later.Tick += (_, _) => { later.Dispose(); if (ended == runEnds && runner == null) RunCapture(false); };
            later.Start();
        }
        else RunCapture(false);
        foreach (var o in overlays) o.Invalidate();
        Dbg.Log("run-quit", ("byKeys", quit), ("record", record));
        if (!record) return;
        runLast = r.Score;
        runBest = Math.Max(runBest, r.Score);
        runLevel = r.Level; // 다음 판은 오른 레벨에서
        runScores.Add(new RunRecord(DateTime.Now, r.Level, r.Score, r.Seeds));
        if (runScores.Count > 50) runScores.RemoveRange(0, runScores.Count - 50);
        Save();
    }

    void ShowSettings(string? tab = null)
    {
        if (settings is { IsDisposed: false }) { if (tab != null) settings.SelectTab(tab); settings.Activate(); return; }
        settings = new Settings(Sfx.On, autoUpdate, () => sprites, Status, Book()) { ReleaseAsked = Release, LanguageChosen = ChooseLanguage };
        settings.Changed += () => { (Sfx.On, autoUpdate) = (settings.Sound, settings.AutoUpdate); Save(); };
        settings.RestartAsked += () => { Save(); Updater.Restart(); };
        if (tab != null) settings.SelectTab(tab);
        settings.Show();
    }

    // ── 도감·풀어주기: 캐릭터는 고르지 않는다. 풀어주면 자연으로 떠나고 새 알이 온다 (무엇이 나올지는 깨질 때까지 모른다) ──
    HashSet<string> met = new();
    List<ReleasedPet> released = new();
    bool leaving;
    double leaveTo;
    string lastStage = "";

    PetBook Book() => new(species, b.P.Stage == "egg", met, released, visits) { Variant = variant, Gifts = gifts, Album = album, Cards = cards, VisitCounts = visitCounts };
    void BookChanged() { if (settings is { IsDisposed: false }) settings.UpdateBook(Book()); }

    // 설정 창 상태 탭에 보여줄 것
    PetStatus Status()
    {
        var (p, k) = (b.P, b.K);
        (double? left, double frac, string? to) = p.Stage switch
        {
            "egg" when p.AgeMin >= k.EggMin => ((double?)null, 1.0, "waiting"), // GrowTo: grow.<키>
            "egg" => (k.EggMin - p.AgeMin, p.AgeMin / k.EggMin, "hatch"),
            "baby" => (k.BabyMin - p.AgeMin, (p.AgeMin - k.EggMin) / (k.BabyMin - k.EggMin), "child"),
            "child" => (k.ChildMin - p.AgeMin, (p.AgeMin - k.BabyMin) / (k.ChildMin - k.BabyMin), "adult"),
            _ => ((double?)null, 1.0, (string?)null),
        };
        string never = L.T("game.never");
        return new PetStatus(species, p.Stage, p.AgeMin, p.Full, p.Mood, p.Energy, p.Asleep, p.Need(k), p.Miss, Math.Max(p.Bites, b.Holes.Count),
            p.BitesTotal, left is { } l ? Math.Max(0, l) : null, Math.Clamp(frac, 0, 1), to, new GameStat[]
            {
                new(L.T("game.run"), runLast > 0 ? L.T("game.run_val", runLevel, runBest.ToString("N0", L.Culture)) : never, runLast > 0),
                new(L.T("game.breakout"), breakBest > 0 ? L.T("game.breakout_val", breakBest.ToString("N0", L.Culture)) : never, breakBest > 0),
                new(L.T("game.fortress"), wormsWins > 0 ? L.T("game.fortress_val", wormsWins) : L.T("game.fortress_none"), wormsWins > 0),
                new(L.T("game.shoot"), gameBest > 0 ? L.T("game.shoot_val", gameBest) : never, gameBest > 0),
            }.Concat(modes.SelectMany(m => m.Stats())).ToList()); // 게임 모듈 기록도
    }

    // 확인은 설정 창 도감 탭에서 받는다. 지금 풀어줄 수 없으면 false
    public bool Release()
    {
        if (leaving || InGame || b.P.Stage == "egg") return false;
        TakePhoto(b.P.Stage, "love"); // 작별 사진
        released.Add(new ReleasedPet(species, b.P.Stage, Math.Round(b.P.AgeMin), DateTime.Now, variant));
        var area = b.Area;
        leaveTo = b.X < area.Left + area.Width / 2.0 ? area.Left - b.CellPx : area.Right + b.CellPx; // 가까운 쪽 화면 밖 자연으로
        leaving = true;
        b.StartPuppet();
        b.Emote("heart", 2.5);
        Dbg.Log("release", ("species", species), ("stage", b.P.Stage), ("age", (int)b.P.AgeMin));
        Save();
        return true;
    }

    void LeaveFrame(double dt)
    {
        double step = 160 * b.Scale / 3.0 * dt;
        (b.Dir, b.Pose) = (leaveTo > b.X ? 1 : -1, "walk");
        b.X += Math.Clamp(leaveTo - b.X, -step, step);
        if (Math.Abs(leaveTo - b.X) > 1) return;
        leaving = false;
        string next = RandomSpecies();
        b.Reset();
        var area = b.Area;
        (b.X, b.Y) = (area.Left + area.Width / 2.0, area.Bottom - b.K.FloorGap);
        NewEgg(next);
        lastStage = "egg";
        b.Emote("ask", 2.5);
        TakePhoto("egg");
        BookChanged();
        Dbg.Log("new-egg", ("species", next));
        Save();
    }


    // ── 떠난 친구가 하루 한 번 놀러 온다 (YELL 2026-11-12): 한 친구, 5분. 같이 다니고 같이 잔다 ──
    Visitor? visitor;
    Sprites? visitorSprites;
    string visitDay = "", lastVisitDay = "";
    int visitMin = -1;
    Dictionary<string, DateTime> visits = new();
    long nextVisitCheck;

    static string Today => DateTime.Now.ToString("yyyy-MM-dd");
    static int NowMin => DateTime.Now.Hour * 60 + DateTime.Now.Minute;

    // 오늘 언제 올지: 그날 처음 볼 때 정한다. 지금부터 20분~3시간 사이 아무 때 (밤 11시 반을 넘으면 오늘은 안 온다)
    void PlanVisit()
    {
        if (visitDay == Today) return;
        visitDay = Today;
        int at = NowMin + 20 + Random.Shared.Next(160);
        visitMin = at < 23 * 60 + 30 ? at : -1;
        Dbg.Log("visit-plan", ("at", visitMin < 0 ? "내일" : $"{visitMin / 60}:{visitMin % 60:00}"));
        Save();
    }

    // 1초에 한 번: 때가 됐고, 볼록이가 한가하고, YELL 이 자리에 있으면 부른다
    void VisitTick()
    {
        if (Environment.TickCount64 < nextVisitCheck || visitor != null) return;
        nextVisitCheck = Environment.TickCount64 + 1000;
        PlanVisit();
        GreetTick();
        CardTick();
        if (lastVisitDay == Today || visitMin < 0 || NowMin < visitMin) return;
        if (released.Count == 0 || InGame || b.P.Stage == "egg" || Afk() || b.Mode is not (Mode.Wander or Mode.Anim)) return;
        StartVisit(null);
    }

    public void StartVisit(string? only, bool counts = true)
    {
        if (visitor != null) return;
        var pick = only != null
            ? released.LastOrDefault(r => r.Species == only) ?? new ReleasedPet(only, "child", 0, DateTime.Now)
            : Visitor.Pick(released, visits, species, Random.Shared);
        if (pick == null) return;
        if (SpritesFor(PetArt.Key(pick.Species, pick.Variant)) is not { } friendSprites) return; // 없어진 종 (떠날 때 색 그대로 온다)
        visitorSprites = friendSprites;
        var area = b.Area;
        double x = b.X < area.Left + area.Width / 2.0 ? area.Left - b.CellPx : area.Right + b.CellPx; // 가까운 화면 옆에서 걸어 들어온다
        var v = visitor = new Visitor(pick.Species, pick.Stage, x, area.Bottom - b.K.FloorGap, b.Scale, new Random());
        v.OnEvent = e =>
        {
            if (e is "hello" or "bye") b.Emote("heart", 2); // 반갑고, 아쉽다
            if (e == "bye") present = (Memories.Kinds[Random.Shared.Next(Memories.Kinds.Length)], v.Species, v.X, v.Y, Environment.TickCount64 / 1000.0); // 선물을 두고 간다
            else if (e == "play" && Random.Shared.NextDouble() < .5) b.Emote("heart", 1.2);
            if (e != "play") Dbg.Log("visit", ("event", e), ("friend", v.Species));
            if (e == "gone") { visitor = null; visitorSprites = null; foreach (var o in overlays) o.Invalidate(); }
        };
        if (counts) // 디버그로 부른 건 오늘의 방문으로 치지 않는다
        {
            lastVisitDay = Today;
            visits[pick.Species] = DateTime.Now;
            visitCounts[pick.Species] = visitCounts.GetValueOrDefault(pick.Species) + 1; // 우정 하트
            if (Random.Shared.NextDouble() < Memories.CardChance) (cardFrom, cardForm, cardDay, cardVariant) = (pick.Species, pick.Stage, DateTime.Now.AddDays(1).ToString("yyyy-MM-dd"), pick.Variant); // 내일 엽서
        }
        v.Baby = visitCounts.GetValueOrDefault(pick.Species) >= 5; // 우정 하트 다섯: 아기를 데리고 온다
        Dbg.Log("visit-start", ("friend", pick.Species), ("form", pick.Stage), ("counts", counts));
        Save();
        BookChanged();
    }

    // 디버그: visit (지금 부르기) / visit <종> / visit end (지금 돌아가기)
    public void VisitCommand(string[] a)
    {
        if (a.Length > 1 && a[1] == "end") visitor?.GoHome();
        else if (a.Length > 1 && a[1] == "card") { cardFrom ??= released.LastOrDefault()?.Species ?? species; cardDay = ""; nextVisitCheck = 0; }
        else if (a.Length > 1 && a[1] == "photo") TakePhoto(b.P.Stage);
        else if (a.Length > 1 && a[1] == "baby") { if (StartVisitWithBaby()) Dbg.Log("visit-baby"); }
        else if (a.Length > 1 && a[1] == "morning") { b.Emote("sun", 2.5); b.Greet("stretch", 1.6); }
        else if (a.Length > 1 && a[1] == "night") { b.Emote("moon", 2.5); b.Greet("stretch", 1.6); }
        else if (a.Length > 1 && a[1] == "nightcap") { ForceNight = !ForceNight; capSpot.Clear(); Invalidate(); }
        else if (a.Length > 1 && a[1] == "dream") { nextDream = Environment.TickCount64 / 1000.0; Dbg.Log("dream-now", ("asleep", b.P.Asleep), ("pose", b.Pose), ("game", InGame), ("behind", b.Behind), ("x", (int)b.X), ("y", (int)b.Y)); } // 자는 중이면 꿈 구름을 바로
        else StartVisit(a.Length > 1 ? a[1] : null, counts: false);
    }

    bool StartVisitWithBaby() { StartVisit(null, counts: false); if (visitor == null) return false; visitor.Baby = true; return true; }

    // ── 추억: 선물 줍기, 사진, 엽서 (모두 그림만) ──
    readonly string memDir;
    List<GiftRecord> gifts = new();
    List<PhotoRecord> album = new();
    List<CardRecord> cards = new();
    Dictionary<string, int> visitCounts = new();
    string? cardFrom;
    string cardForm = "child", cardDay = "", morningDay = "", nightDay = "";
    int cardVariant;
    (string Kind, string From, double X, double Y, double At)? present;
    long nextPresentWalk;

    // 친구가 두고 간 선물: 친구가 다 가면 볼록이가 걸어가 줍는다 (바쁘면 3분 뒤 저절로 추억 상자에)
    void PresentTick()
    {
        if (present is not { } pr || visitor != null) return;
        bool free = b.Mode == Mode.Wander && !b.P.Asleep && !InGame;
        double age = Environment.TickCount64 / 1000.0 - pr.At;
        if (free && Math.Abs(b.X - pr.X) < b.CellPx * .45 && Math.Abs(b.Y - pr.Y) < b.CellPx * .6 || age > 180)
        {
            gifts.Add(new GiftRecord(pr.Kind, pr.From, DateTime.Now));
            present = null;
            b.Emote("heart", 1.6);
            foreach (var o in overlays) o.Invalidate();
            Dbg.Log("gift", ("kind", pr.Kind), ("from", pr.From), ("picked", age <= 180));
            Save();
            BookChanged();
            return;
        }
        if (free && Environment.TickCount64 > nextPresentWalk) { nextPresentWalk = Environment.TickCount64 + 3000; b.GoTo(pr.X, pr.Y); }
    }

    // 사진: 알이 올 때, 깨어날 때, 자랄 때, 떠날 때 (그 종 그림이 있을 때만)
    void TakePhoto(string stage, string pose = "happy")
    {
        try
        {
            string sprite = stage == "egg" ? "egg/idle" : $"{stage}/0/{pose}";
            if (!sprites.Has(sprite) || stage != "egg" && sprites.BaseSpecies != species) return;
            var file = Path.Combine(memDir, "album", $"{DateTime.Now:yyyyMMdd-HHmmss}-{species}-{stage}.png");
            Memories.Photo(sprites, sprite, stage, file);
            album.Add(new PhotoRecord(species, stage, DateTime.Now, file));
            Dbg.Log("photo", ("species", species), ("stage", stage));
            BookChanged();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException) { Dbg.Log("photo-error", ("msg", e.Message)); }
    }

    // 엽서: 놀러 왔던 친구가 다음 날 보낸다. 볼록이가 한가하고 YELL 이 있을 때 편지 그림과 함께
    void CardTick()
    {
        if (cardFrom is not { } from || string.CompareOrdinal(Today, cardDay) < 0 || visitor != null) return;
        if (InGame || Afk() || b.Mode != Mode.Wander || b.P.Asleep) return;
        try
        {
            var friend = SpritesFor(PetArt.Key(from, cardVariant));
            var file = Path.Combine(memDir, "cards", $"{DateTime.Now:yyyyMMdd-HHmmss}-{from}.png");
            Memories.Card(sprites, friend, PetArt.Key(from, cardVariant), cardForm, DateTime.Now.DayOfYear * 31 + from.Sum(ch => ch), file);
            cards.Add(new CardRecord(from, DateTime.Now, file));
            b.Emote("mail", 2.5);
            b.Greet("happy", 1.2);
            Dbg.Log("card", ("from", from));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException) { Dbg.Log("card-error", ("msg", e.Message)); }
        cardFrom = null;
        Save();
        BookChanged();
    }

    // 아침 첫 만남엔 기지개와 해, 밤 9시 넘으면 하품과 달 (하루에 한 번씩)
    static bool Night => DateTime.Now.Hour >= 21 || DateTime.Now.Hour < 6;
    static string NightKey => DateTime.Now.AddHours(-6).ToString("yyyy-MM-dd");

    void GreetTick()
    {
        if (InGame || b.P.Stage == "egg" || Afk() || b.Mode != Mode.Wander || b.P.Asleep) return;
        int h = DateTime.Now.Hour;
        if (h is >= 5 and < 12 && morningDay != Today) { morningDay = Today; b.Emote("sun", 2.5); b.Greet("stretch", 1.6); Dbg.Log("greet", ("when", "morning")); Save(); }
        else if (Night && nightDay != NightKey) { nightDay = NightKey; b.Emote("moon", 2.5); b.Greet("stretch", 1.6); Dbg.Log("greet", ("when", "night")); Save(); }
    }

    // 꿈 말풍선: 잘 때 가끔 한 번씩 (YELL: 계속 보이면 걸리적거린다). 잠든 지 8초 뒤 처음, 그 뒤 25~45초마다 4초 동안 그림 하나
    //   그림: 떠난 친구·받은 선물·밥 중 아무거나 (게임·숨기 중엔 안)
    double nextDream, dreamUntil;
    string[] dreamNow = Array.Empty<string>();

    IReadOnlyList<string> Dreams()
    {
        if (!b.P.Asleep || b.Pose != "sleep" || InGame || b.Behind) { nextDream = 0; return Array.Empty<string>(); }
        double now = Environment.TickCount64 / 1000.0;
        if (nextDream == 0) nextDream = now + 8;
        if (now >= nextDream)
        {
            var list = released.Select(r => "thumb/" + PetArt.Key(r.Species, r.Variant)).Distinct().Concat(gifts.Select(g => "item/" + g.Kind).Distinct()).ToList();
            list.Add("icon/seed");
            if (released.Count == 0) list.Add("icon/heart");
            dreamNow = new[] { list[Random.Shared.Next(list.Count)] };
            (dreamUntil, nextDream) = (now + 4, now + 25 + Random.Shared.NextDouble() * 20);
        }
        return now < dreamUntil ? dreamNow : Array.Empty<string>();
    }

    // ── 마우스 손짓 (규칙은 Gestures.cs): 누르지 않고 커서만 움직여 쓰다듬기·툭툭·찌르기 (YELL 2026-11-13) ──
    readonly Gestures gestures = new();

    void GestureTick()
    {
        var (name, frame, flip, _) = drawn;
        if (pressing || name == null || !Visible || InGame || b.P.Stage == "egg") { gestures.Reset(); return; }
        var p = Control.MousePosition;
        int k = b.Scale, px = (int)Math.Floor((p.X - Left - 8.0) / k), py = (int)Math.Floor((double)(p.Y - Top) / k);
        double cx = (p.X - Left - 8.0) / k, cy = (double)(p.Y - Top) / k;
        if (b.Dir < 0) cx = 40 - cx; // 뒤집혀 있으면 좌우를 되돌린다 (클릭과 같게)
        bool inside = px is >= 0 and < 40 && py is >= 0 and < 40 && sprites.Get(name, frame, 1, flip).GetPixel(px, py).A > 0;
        string zone = inside ? Clicks.Zone(b.P.Stage, cx, cy) : "";
        if (gestures.Feed(Environment.TickCount64 / 1000.0, cx, cy, inside, zone) is { } g) b.Gesture(g);
    }

    // ── 네잎클로버 (규칙은 Clover.cs): 돋으면 "!" 하고 주우러 가서 머리에 꽂는다. 던져지면 떨어지고 다시 줍는다 ──
    readonly Clover clover = new();
    long nextCloverRoll, nextCloverWalk, nextCloverTick;

    void CloverTick()
    {
        long t = Environment.TickCount64;
        if (t < nextCloverTick) return;
        nextCloverTick = t + 250;
        double now = t / 1000.0, ground = b.Area.Bottom - b.K.FloorGap;
        void Changed(string ev) { Dbg.Log("clover", ("event", ev), ("kind", clover.Kind), ("x", (int)clover.X), ("y", (int)clover.Y), ("petX", (int)b.X), ("petY", (int)b.Y)); Invalidate(); foreach (var o in overlays) o.Invalidate(); Save(); }
        if (clover.Wilt(Today, now)) { Changed("wilt"); return; }
        if (b.Mode == Mode.Thrown && clover.Drop(b.X, ground, now)) { Changed("drop"); return; } // 던져지면 머리에서 떨어진다
        bool free = b.Mode == Mode.Wander && !b.P.Asleep && !InGame && !b.Behind && b.P.Stage != "egg" && !leaving;
        if (clover.State == Clover.Phase.Ground)
        {
            if (free && clover.TryPick(b.X, b.Y, b.CellPx)) { b.Greet("happy", 1.6); b.Emote("heart", 1.6); Changed("pick"); }
            else if (clover.Lost(now)) Changed("lost");
            else if (free && t > nextCloverWalk) { nextCloverWalk = t + 3000; b.GoTo(clover.X, clover.Y); }
            return;
        }
        if (clover.State == Clover.Phase.None && free && t > nextCloverRoll)
        {
            nextCloverRoll = t + 60_000;
            if (clover.TrySprout(Today, now, b.X, b.Dir, ground, b.CellPx, b.Area, Random.Shared)) { b.Emote("bang", 1.2); Changed("sprout"); }
        }
    }

    // 디버그: clover [clover|sprout] (지금 돋기) / clover wear [종류] (바로 머리에) / clover drop / clover off
    public void CloverCommand(string[] a)
    {
        double now = Environment.TickCount64 / 1000.0, ground = b.Area.Bottom - b.K.FloorGap;
        string what = a.Length > 1 ? a[1] : "", kind = a.Length > 2 ? a[2] : what;
        if (what == "wear") clover.Force(Clover.Phase.Worn, b.X, ground, Today, now, kind);
        else if (what == "drop") clover.Force(Clover.Phase.Ground, b.X, ground, Today, now, kind);
        else if (what == "off") clover.Force(Clover.Phase.None, 0, 0, "", now);
        else
        {
            clover.Force(Clover.Phase.None, 0, 0, "", now);
            clover.TrySprout(Today, now, b.X, b.Dir, ground, b.CellPx, b.Area, Random.Shared, 1);
            if (Clover.Kinds.Contains(what)) clover.Kind = what;
            b.Emote("bang", 1.2);
        }
        Dbg.Log("clover", ("event", "cmd-" + (what is "" or "clover" or "sprout" ? "sprout" : what)), ("kind", clover.Kind), ("x", (int)clover.X), ("y", (int)clover.Y), ("petX", (int)b.X), ("petY", (int)b.Y));
        Invalidate();
        foreach (var o in overlays) o.Invalidate();
    }

    // 머리에 꽂은 네잎클로버·새싹. 그 열에서 가장 위 칸에 줄기 끝을 꽂는다 (프레임마다 몸이 들썩이면 같이)
    //   네잎클로버는 머리 왼쪽(뒤집히면 오른쪽), 새싹은 머리 꼭대기 가운데에서 자라난 것처럼 (YELL: 머리에 꽂은 잎사귀가 귀엽다)
    readonly Dictionary<(string, int, bool, int), (int X, int Top)> cloverSpot = new();
    readonly Dictionary<string, (int X, int Y)> cloverStem = new();
    void DrawClover(Graphics g, string name, int frame, bool flip, int shake)
    {
        string item = "item/" + clover.Kind;
        if (clover.State != Clover.Phase.Worn || b.Pose is "fly" or "roll" or "flop" || !sprites.Has(item) || name.StartsWith("egg")) return;
        int n = sprites.Frames(name), f = (frame % n + n) % n, side = clover.Kind == "sprout" ? 0 : -4;
        if (!cloverSpot.TryGetValue((name, f, flip, side), out var spot))
        {
            var one = sprites.Get(name, f, 1, flip);
            int top = one.Height;
            for (int x = one.Width * 3 / 10; x < one.Width * 6 / 10; x++)
                for (int y = 0; y < one.Height; y++)
                    if (one.GetPixel(x, y).A > 0) { top = Math.Min(top, y); break; }
            int row = Math.Min(one.Height - 1, top + 4), x0 = one.Width, x1 = 0;
            for (int x = 0; x < one.Width; x++) if (one.GetPixel(x, row).A > 0) (x0, x1) = (Math.Min(x0, x), Math.Max(x1, x));
            int col = Math.Clamp((x0 + x1) / 2 + (flip ? -side : side), 0, one.Width - 1), surface = one.Height - 1;
            for (int y = 0; y < one.Height; y++) if (one.GetPixel(col, y).A > 0) { surface = y; break; }
            cloverSpot[(name, f, flip, side)] = spot = (col, surface);
        }
        if (!cloverStem.TryGetValue(item, out var stem)) // 그림의 줄기 끝 (가장 아래 칸)
        {
            var icon1 = sprites.Get(item, 0, 1);
            stem = (20, 23);
            for (int y = icon1.Height - 1, found = 0; y >= 0 && found == 0; y--)
                for (int x = 0; x < icon1.Width; x++) if (icon1.GetPixel(x, y).A > 0) { stem = (x, y); found = 1; break; }
            cloverStem[item] = stem;
        }
        int k = b.Scale, s = Clover.Scale(k);
        var (sx, sy) = stem;
        g.DrawImageUnscaled(sprites.Get(item, 0, s), 8 + shake + spot.X * k - sx * s, spot.Top * k - sy * s); // 줄기 끝만 머리에 닿고 잎은 살짝 떠 있게 (YELL)
    }

    // 밤에 자는 동안 수면 모자 (머리 꼭대기: 몸 가운데 열에서 가장 위 칸)
    readonly Dictionary<(string, bool), (int X, int Top)> capSpot = new();
    public bool ForceNight; // 디버그·시험용
    void DrawCap(Graphics g, string name, int frame, bool flip, int shake)
    {
        if (!(Night || ForceNight) || !b.P.Asleep || b.Pose != "sleep" || !sprites.Has("game/nightcap")) return;
        if (!capSpot.TryGetValue((name, flip), out var spot))
        {
            var one = sprites.Get(name, frame, 1, flip);
            int top = one.Height;
            for (int x = one.Width * 3 / 10; x < one.Width * 6 / 10; x++)
                for (int y = 0; y < one.Height; y++)
                    if (one.GetPixel(x, y).A > 0) { top = Math.Min(top, y); break; }
            int row = Math.Min(one.Height - 1, top + 4), x0 = one.Width, x1 = 0;
            for (int x = 0; x < one.Width; x++) if (one.GetPixel(x, row).A > 0) (x0, x1) = (Math.Min(x0, x), Math.Max(x1, x));
            capSpot[(name, flip)] = spot = ((x0 + x1) / 2, top);
        }
        int k = b.Scale;
        g.DrawImageUnscaled(sprites.Get("game/nightcap", 0, k), 8 + shake + (spot.X - 20 - 3) * k, (spot.Top - 23 + 2) * k);
    }

    // 새 알의 종: 34종 중 아무나 (처음 알도, 풀어준 뒤 오는 알도)
    string RandomSpecies()
    {
        var all = sprites.SpeciesList.ToList();
        return all[Random.Shared.Next(all.Count)];
    }

    // 알이 깨지면 도감에 올리고 알려 준다
    void WatchHatch()
    {
        if (lastStage == "egg" && b.P.Stage != "egg")
        {
            met.Add(species);
            TakePhoto(b.P.Stage);
            b.Emote("star", 2.5); // 펫은 말하지 않는다 (YELL 2026-11-13): 「태어났어요!」 대신 반짝 별
            BookChanged();
            Save();
        }
        else if (lastStage is "baby" or "child" && b.P.Stage != lastStage) TakePhoto(b.P.Stage); // 어린이가 되거나 어른이 됐다
        lastStage = b.P.Stage;
    }

    bool InGame => Playing || leaving;

    // 게임에 쓸 창(최대화 안 한 창)이 없다: 말 대신 「X 표 창」 그림을 머리 위에 띄우고 고개를 젓는다 (YELL 2026-11-13: 알림 말풍선도 그림으로)
    void NoWindow() { b.Emote("nowin", 3); b.ShakeUntil = b.Now + .5; }
    bool Playing => game != null || runner != null || breakout != null || worms != null || minigun != null || icbm != null || modes.Any(m => m.Running);

    // ── 보스 키 (YELL 2026-11-13: "게임에서 esc 는 언제나 빠져나가기다!!") ──
    // 어느 창에 초점이 있든(판을 놓쳐 일하는 창을 눌렀어도) Esc 를 누르는 순간 하던 게임을 결과 화면 없이 곧바로 다 끝내고,
    // 볼록이도 별·눈물·말풍선 없이 하던 대로 돌아간다. 키는 초점 없이 상태만 본다. 누르고 있던 Esc 로 시작하자마자 끝나지 않게 눌리는 순간만 본다
    bool escWas = true;
    void BossKeyTick()
    {
        bool esc = Native.KeyDown(Keys.Escape);
        if (esc && !escWas) BossKey();
        escWas = esc;
    }

    public void BossKey(Action? quit = null)
    {
        if (!Playing) { foreach (var o in overlays) o.HideAfterGame(); return; } // 끝난 뒤 잠깐 뜨는 점수도 Esc 면 바로 숨긴다
        Dbg.Log("boss-key");
        quit?.Invoke(); // 판이 알려 준 끝내기 (아래와 겹쳐도 게임마다 한 번만 끝난다)
        foreach (var m in modes) if (m.Running) m.Stop(true);
        if (runner != null) EndRun(true);
        if (breakout != null) EndBreak(true);
        if (worms != null) EndWorms(true);
        if (minigun != null) EndMinigun(true);
        if (icbm != null) EndIcbm();
        if (game is { } gm) { gm.Finish(); gm.Finish(); EndGame(); } // 결과 화면 없이
        b.Hush();
        foreach (var o in overlays) o.Invalidate();
    }

    // 게임판: 그 모니터 위에 1% 판을 깔아 클릭·키를 받는다. 다른 창은 안 눌린다
    GameLayer OpenLayer(Rectangle field, MouseEventHandler down, MouseEventHandler up, Action quit)
    {
        var layer = new GameLayer(field, b.K.GameLayerOpacity);
        layer.MouseDown += down;
        layer.MouseUp += up;
        layer.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) BossKey(quit); }; // 판에 초점이 있을 때도 보스 키 하나로
        layers.Add(layer);
        layer.Show();
        layer.Bounds = field;
        layer.ShowPointer();
        layer.Activate(); // Esc 를 받으려면 활성 창이어야 한다
        foreach (var o in overlays) Native.Topmost(o.Handle); // 판 위에 그림이 보이게
        Native.SetClickThrough(Handle, true);
        Native.Topmost(Handle);
        pad.Hide();
        return layer;
    }

    void CloseLayer()
    {
        foreach (var l in layers) l.Close();
        layers.Clear();
        Native.SetClickThrough(Handle, false);
        if (gameFocus != IntPtr.Zero) Native.SetForegroundWindow(gameFocus); // 하던 일로 돌려준다
        foreach (var o in overlays) o.Invalidate();
        Native.Topmost(Handle);
    }

    (Rectangle Field, List<Rectangle> Wins) PickIslands() =>
        Worms.Islands(Native.Foreground(), Native.ListWindows(), b.Area, Screen.AllScreens.Select(s => s.WorkingArea).ToList(), b.CellPx, 4);

    // ── ICBM: 볼록이가 버튼을 누르면 미사일이 날아갔다가 아무 데나(활성 창은 빼고) 떨어지고, 볼록이가 달려가 고친다 ──
    Icbm? icbm;

    public void StartIcbm()
    {
        if (icbm != null) { if (!icbm.Launch()) b.Emote("no"); return; } // 이미 하는 중이면 한 발 더 (구멍 다섯까지)
        if (InGame || b.P.Stage == "egg") return;
        if (b.P.Asleep) { b.Emote("zzz"); return; }
        if (b.P.Energy < 10) { b.Emote("no"); return; } // 고칠 기운이 없으면 안 쏜다
        var ic = icbm = new Icbm(Screen.AllScreens.Select(s => s.WorkingArea).ToList(), b.X, b.Y, b.Scale, new Random())
            { Friends = released.Select(r => PetArt.Key(r.Species, r.Variant)).Distinct().ToList() }; // 구멍에서 가끔 빼꼼 (떠날 때 색 그대로)
        ic.OnEvent = e =>
        {
            switch (e)
            {
                case "press": Sfx.Play("empty"); break;
                case "launch": Sfx.Play("shotgun"); break;
                case "fall": Sfx.Play("rumble"); break;
                case "peek": b.Emote("heart", 1.6); Dbg.Log("icbm-peek", ("friend", ic.Peeks.LastOrDefault()?.Species)); break; // 옛 친구를 알아본다
                case "boom": Sfx.Play("boom"); b.Emote("bang", 1); break;
                case "blown": b.Throw(ic.BlownVx, ic.BlownVy, hard: true); break; // 어지러워하다가 거기서부터 고치러 간다
                case "nowhere": b.Emote("ask", 2); break; // 떨어뜨릴 곳이 없다: 갸웃 (말 대신 그림)
                case "repaired": b.P.Energy = Math.Max(0, b.P.Energy - 4); b.Emote("spark", .8); break; // 일은 기운이 든다
            }
            if (e is "boom" or "nowhere" or "repaired") Dbg.Log("icbm", ("event", e), ("craters", ic.Craters.Count), ("x", (int)ic.TX), ("y", (int)ic.TY));
        };
        b.StartPuppet();
        ic.Launch();
        Dbg.Log("icbm-start");
    }

    void IcbmFrame(double dt)
    {
        var ic = icbm!;
        ic.Update(dt, ic.Stage == Icbm.Phase.Away ? Native.Foreground()?.R : null);
        if (b.Mode != Mode.Puppet) // 잡아서 옮기거나 던지는 중: 볼록이가 가는 대로 두고, 내려앉으면 거기서 이어서 한다
        {
            ic.Hold(b.X, b.Y);
            if (b.Mode == Mode.Wander) b.StartPuppet();
            return;
        }
        (b.X, b.Y, b.Dir, b.Pose) = (ic.X, ic.Y, ic.Dir, ic.Pose);
        if (ic.Finished) EndIcbm();
    }

    // 디버그: icbm / icbm stop
    public void IcbmCommand(string[] a) { if (a.Length > 1 && a[1] == "stop") EndIcbm(); else StartIcbm(); }

    void EndIcbm()
    {
        if (icbm is not { } ic) return;
        icbm = null;
        foreach (var o in overlays) o.Invalidate();
        b.EndPuppet(true, "icbm", ("launched", ic.Launched));
    }

    // ── 미니건: 위쪽 창모드 창들을 쏴서 부순다 (스트레스 풀기, 점수 없음, Esc 끝) ──
    Minigun? minigun;
    List<(IntPtr H, Rectangle R)> gunWindows = new();
    bool gunFire, gunForceFire;
    Point? gunForceAim;
    double gunCheckAt;

    public void StartMinigun()
    {
        if (InGame || b.P.Stage == "egg") return;
        gameFocus = Native.GetForegroundWindow();
        var wins = Native.ListWindows();
        var (field, rects) = Worms.Islands(Native.Foreground(), wins, b.Area, Screen.AllScreens.Select(s => s.WorkingArea).ToList(), b.CellPx, 8, topRoom: 0);
        if (rects.Count == 0)
        {
            NoWindow();
            Dbg.Log("gun-none");
            return;
        }
        gunWindows = wins.Where(w => rects.Contains(Rectangle.Intersect(w.R, field))).Select(w => (w.H, w.R)).ToList();
        var mg = minigun = new Minigun(field, rects, field.Bottom - b.K.FloorGap, b.Scale, new Random());
        mg.OnEvent = e =>
        {
            switch (e)
            {
                case "shot": if (mg.Shots % 4 == 0) Sfx.Play("mg"); break; // 매 발 소리를 내면 소리가 서로 끊긴다
                case "throw": Sfx.Play("empty"); break;
                case "boom": Sfx.Play("boom"); break;
                case "chunk": Sfx.Play("hit"); break;
                case "dual": b.Emote("star", 1.2); Sfx.Play("reload"); break; // 쌍 미니건
            }
            if (e is "boom" or "dual") Dbg.Log("gun-" + e, ("shots", mg.Shots));
        };
        (gunFire, gunForceFire, gunForceAim, gunCheckAt) = (false, false, null, 0);
        var layer = OpenLayer(field, (_, e) =>
        {
            if (e.Button == MouseButtons.Left) gunFire = true;
            else if (e.Button == MouseButtons.Right) minigun?.Throw(MousePosition.X, MousePosition.Y);
        }, (_, e) => { if (e.Button == MouseButtons.Left) gunFire = false; }, () => EndMinigun(true));
        layer.Cursor = Cursors.Cross;
        b.StartPuppet();
        Dbg.Log("gun-start", ("field", field.ToString()), ("targets", string.Join(" ", rects.Select(r => $"{r.X},{r.Y},{r.Width}x{r.Height}"))));
    }

    void MinigunFrame(double dt)
    {
        var mg = minigun!;
        var m = gunForceAim ?? MousePosition;
        bool fire = gunForceFire || gunFire && (MouseButtons & MouseButtons.Left) != 0; // 떼는 걸 놓쳐도 계속 쏘지 않게
        mg.Update(dt, m.X, m.Y, fire);
        (b.X, b.Y, b.Dir) = (mg.X, mg.Ground, Math.Cos(mg.Aim) >= 0 ? 1 : -1);
        b.Pose = mg.Clock - mg.ThrowAt < .3 ? "hop" : mg.Firing ? "angry" : mg.Clock - mg.WalkAt < .1 ? "walk" : "idle";
        if (mg.Clock > gunCheckAt) // 창이 닫히거나 움직이면 그 창만 대상에서 뺀다
        {
            gunCheckAt = mg.Clock + .5;
            foreach (var (h, r) in gunWindows.ToList())
            {
                var st = Native.Probe(h);
                if (st.Alive && !st.Minimized && st.R == r) continue;
                gunWindows.Remove((h, r));
                mg.RemoveTarget(Rectangle.Intersect(r, mg.Field));
                Dbg.Log("gun-window-gone");
            }
        }
    }

    // 디버그: minigun / minigun aim <x> <y> / minigun fire on|off / minigun nade <x> <y> / minigun dual / minigun stop
    public void MinigunCommand(string[] a)
    {
        if (a.Length > 1 && a[1] == "stop") EndMinigun(true);
        else if (a.Length > 3 && a[1] == "aim") gunForceAim = new Point(int.Parse(a[2]), int.Parse(a[3]));
        else if (a.Length > 2 && a[1] == "fire") gunForceFire = a[2] == "on";
        else if (a.Length > 3 && a[1] == "nade") minigun?.Throw(double.Parse(a[2]), double.Parse(a[3]));
        else if (a.Length > 1 && a[1] == "dual") minigun?.StartDual();
        else StartMinigun();
    }

    void EndMinigun(bool quit)
    {
        if (minigun is not { } mg) return;
        minigun = null;
        CloseLayer();
        b.EndPuppet(true, "gun", ("shots", mg.Shots), ("gone", mg.GoneCount()), ("esc", quit)); // 이기고 지는 건 없다. 실컷 쏘면 즐겁다
    }

    // ── 포트리스: 창모드 창들이 섬, 볼록이와 라이벌이 번갈아 쏜다 ──
    Worms? worms;
    int wormsWins;
    bool spaceWas;

    public void StartWorms(string? gun = null)
    {
        if (InGame || b.P.Stage == "egg") return;
        gameFocus = Native.GetForegroundWindow();
        var (field, wins) = PickIslands();
        if (wins.Count == 0)
        {
            NoWindow();
            Dbg.Log("worms-none");
            return;
        }
        var w = worms = new Worms(field, wins, b.Scale, new Random(), gun);
        w.OnEvent = e =>
        {
            switch (e)
            {
                case "fire": Sfx.Play(w.Active.Gun.Shells > 1 ? "mg" : "shotgun"); break;
                case "boom": Sfx.Play("boom"); break;
                case "splash": Sfx.Play("reload"); break;
                case "hurt:me": b.Emote("bang", .6); break;
            }
            if (e is "won" or "lost" or "splash" or "boom") Dbg.Log("worms", ("event", e), ("me", w.Me.Hp), ("cpu", w.Cpu.Hp));
        };
        OpenLayer(field, (_, e) =>
        {
            if (e.Button == MouseButtons.Left) worms?.StartCharge();
        }, (_, e) => { if (e.Button == MouseButtons.Left) worms?.ReleaseCharge(); }, () => EndWorms(true));
        spaceWas = Native.KeyDown(Keys.Space);
        b.StartPuppet();
        Dbg.Log("worms-start", ("field", field.ToString()), ("me", w.Me.Gun.Name), ("cpu", w.Cpu.Gun.Name), ("lands", string.Join(" ", w.Lands.Select(r => $"{r.X},{r.Y},{r.Width}x{r.Height}"))));
    }

    void WormsFrame(double dt)
    {
        var w = worms!;
        var me = w.Me;
        if (w.MyTurn && w.Stage == Worms.Phase.Turn && !me.Dead)
        {
            var m = MousePosition;
            w.Aim = Math.Atan2(m.Y - w.CenterY(me), m.X - me.X);
            if (w.ChargeFrom < 0)
            {
                if (Native.KeyDown(Keys.Left) || Native.KeyDown(Keys.A)) w.Walk(me, -1, dt);
                else if (Native.KeyDown(Keys.Right) || Native.KeyDown(Keys.D)) w.Walk(me, 1, dt);
                else me.Dir = m.X >= me.X ? 1 : -1;
                bool space = Native.KeyDown(Keys.Space);
                if (space && !spaceWas) w.Jump(me);
                spaceWas = space;
            }
        }
        w.Update(dt);
        (b.X, b.Y, b.Dir, b.Pose) = (me.X, me.Y, me.Dir, w.PoseOf(me));
        if (w.Done) EndWorms(false);
    }

    // 디버그: worms [multi|giant|normal] / worms fire <각도°> <힘 0~1> / worms stop
    public void WormsCommand(string[] a)
    {
        if (a.Length > 1 && a[1] == "stop") EndWorms(true);
        else if (a.Length > 3 && a[1] == "fire" && worms is { } w && w.MyTurn) w.Fire(w.Me, double.Parse(a[2]) * Math.PI / 180, double.Parse(a[3]));
        else StartWorms(a.Length > 1 ? a[1] : null);
    }

    void EndWorms(bool quit)
    {
        if (worms is not { } w) return;
        worms = null;
        CloseLayer();
        bool won = w.Stage == Worms.Phase.Won;
        if (won) wormsWins++;
        if (w.Me.Out) b.Y = w.Field.Bottom - b.K.FloorGap; // 물에 빠졌으면 바닥으로 돌아온다
        b.EndPuppet(won, "worms", ("me", w.Me.Hp), ("cpu", w.Cpu.Hp), ("esc", quit));
        Save();
    }

    // ── 벽돌깨기: 창모드 창 하나를 조각내고, 볼록이가 쟁반으로 쇠공을 튕긴다 ──
    Breakout? breakout;
    List<(IntPtr H, Rectangle R)> breakWindows = new();
    int breakBest;

    public void StartBreak()
    {
        if (InGame || b.P.Stage == "egg") return;
        gameFocus = Native.GetForegroundWindow();
        var screens = Screen.AllScreens.Select(s => s.WorkingArea).ToList();
        if (Breakout.Pick(Native.Foreground(), Native.ListWindows(), b.Area, screens, b.CellPx) is not { } pick)
        {
            NoWindow(); // 창모드 창이 없다
            Dbg.Log("break-none");
            return;
        }
        breakWindows = pick.Targets.Select(t => (t.Win.H, t.Win.R)).ToList();
        var bo = breakout = new Breakout(pick.Field, pick.Targets.Select(t => t.Board).ToList(), b.Scale, new Random());
        bo.OnEvent = e =>
        {
            switch (e)
            {
                case "plate": Sfx.Play("empty"); break;
                case "break": Sfx.Play("hit"); break;
                case "miss": Sfx.Play("boom"); b.Emote("tear", 1); break;
                case "won": Sfx.Play("reload"); break;
                default: if (e.StartsWith("item:")) { Sfx.Play("reload"); b.Emote(e == "item:life" ? "heart" : "spark", .8); } break;
            }
            if (e is "miss" or "won" or "over" || e.StartsWith("item:")) Dbg.Log("break", ("event", e), ("broken", bo.Broken), ("lives", bo.Lives), ("balls", bo.Balls.Count));
        };
        // 그 모니터 위에 판을 깐다: 클릭은 공 던지기, Esc 는 끝. 다른 창은 눌리지 않는다
        var layer = new GameLayer(pick.Field, b.K.GameLayerOpacity);
        layer.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) breakout?.Launch(); };
        layer.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) EndBreak(true); };
        layers.Add(layer);
        layer.Show();
        layer.Bounds = pick.Field;
        layer.Activate(); // Esc 를 받으려면 활성 창이어야 한다
        foreach (var o in overlays) Native.Topmost(o.Handle); // 판 위에 조각·공이 보이게
        Native.SetClickThrough(Handle, true);
        Native.Topmost(Handle);
        pad.Hide();
        b.StartBreak(bo);
        (perf, perfSkip) = (new Perf(), true);
        (Overlay.PaintMsSum, Overlay.PaintMsMax, Overlay.Paints) = (0, 0, 0);
        Dbg.Log("break-start", ("field", pick.Field.ToString()), ("boards", string.Join(" ", bo.Boards.Select(r => $"{r.X},{r.Y},{r.Width}x{r.Height}"))), ("tiles", bo.Tiles.Count));
    }

    void BreakFrame(double dt)
    {
        var bo = breakout!;
        long t0 = Stopwatch.GetTimestamp();
        bo.Update(dt, b.X, PlateTop());
        perf.Update(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        if (bo.Stage == Breakout.Phase.Play && b.Now > nextWindowCheck)
        {
            nextWindowCheck = b.Now + .25;
            foreach (var (h, r) in breakWindows)
            {
                var st = Native.Probe(h);
                // 창이 닫히거나 움직이면 조각이 어긋나서 끝낸다
                if (!st.Alive || st.Minimized || st.R != r) { Dbg.Log("break-window-gone"); EndBreak(false); return; }
            }
        }
        if (bo.Done) EndBreak(false);
    }

    // 디버그: break / break launch / break stop / break auto(쟁반이 공을 따라간다, 기록 안 남김) / break item <종류>
    bool breakAuto;
    public void BreakCommand(string what, string? arg)
    {
        if (what == "stop") EndBreak(true);
        else if (what == "launch") breakout?.Launch();
        else if (what == "auto") breakAuto = true;
        else if (what == "item" && arg != null) breakout?.Apply(arg);
        else StartBreak();
    }

    void EndBreak(bool quit)
    {
        if (breakout is not { } bo) return;
        breakout = null;
        foreach (var l in layers) l.Close();
        layers.Clear();
        Native.SetClickThrough(Handle, false);
        if (gameFocus != IntPtr.Zero) Native.SetForegroundWindow(gameFocus); // 하던 일로 돌려준다
        if (!breakAuto) breakBest = Math.Max(breakBest, bo.Broken);
        breakAuto = false;
        Dbg.Log("break-perf", perf.Report().Append(("tiles", bo.Tiles.Count)).Append(("broken", bo.Broken)).ToArray());
        b.EndBreak();
        foreach (var o in overlays) o.Invalidate();
        Native.Topmost(Handle);
        Dbg.Log("break-quit", ("esc", quit));
        Save();
    }

    // 쟁반 윗면: 머리 위로 올린 앞발에 쟁반 아래 두 줄이 걸친다
    readonly Dictionary<string, int> pawTop = new();
    double PlateTop()
    {
        string name = b.SpriteName();
        if (!pawTop.TryGetValue(name, out int top)) pawTop[name] = top = Props.Trim(Props.Solid(sprites.Get(name, 0, 1))).Y0;
        return b.Y - (sprites.Cell - top) * b.Scale - 3 * b.Scale;
    }

    // ── 총쏘기 게임 ──

    public void StartGame() => StartGame(null, null);

    public void StartGame(string? gun, string? theme)
    {
        if (InGame || b.P.Stage == "egg") return;
        gameFocus = Native.GetForegroundWindow();
        var rng = new Random();
        var wins = Native.ListWindows();
        var screens = Screen.AllScreens.Select(s => s.WorkingArea).ToList();
        // 메뉴를 열기 전에 쓰던 창이 게임판이 된다. 그 창만 꾸미고, 볼록이도 그 안에서만 움직인다
        var (arena, window) = Game.ChooseArena(Native.Foreground(), wins, b.Area, screens, b.CellPx);
        gameWindow = window;
        game = new Game(arena, gameBest, b.Scale, rng, Game.Guns.FirstOrDefault(g => g.Name == gun), theme);
        game.OnShot = OnShot;
        pointerShown = false;
        shownCursor = null;
        int k = b.Scale;
        // 총은 그 창 아래 테두리 가운데에 반쯤 걸친다 (총 그림은 손잡이 자리에서 위로 8칸, 아래로 1칸). 화면 밖으로는 안 나간다
        game.GunPivot = new Point(arena.Left + arena.Width / 2, Math.Min(arena.Bottom + 6 * k, Screen.FromRectangle(arena).Bounds.Bottom - 2 * k));

        var names = Props.Sets[game.Theme];
        var solids = Props.All.ToDictionary(n => n, n => Props.Solid(sprites.Get(n, 0, 1)));
        var gunSpot = new Rectangle(game.GunPivot.X - 40 * k * 2, game.GunPivot.Y - 30 * k * 2, 80 * k * 2, 34 * k * 2);
        var target = wins.Where(w => w.H == window).ToList();
        game.Decor = Props.OnWindows(target, screens, names, rng, n => solids[n], new List<Rectangle>(), 2)
            .Concat(Props.OnFloor(arena, names, rng, n => solids[n], new[] { gunSpot, Rectangle.Inflate(game.ExitButton(), 20 * k, 20 * k) }, 2)).ToList();
        Dbg.Log("game-stage", ("arena", arena.ToString()), ("window", window != IntPtr.Zero), ("decor", game.Decor.Count));

        // 그 창 위에만 판을 깐다. 창 밖은 평소처럼 클릭된다
        var layer = new GameLayer(arena, b.K.GameLayerOpacity);
        layer.MouseDown += (_, e) => GameMouse(e.Button == MouseButtons.Right, layer.Left + e.X, layer.Top + e.Y);
        layer.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) game?.Release(); };
        layer.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) BossKey(); }; // 결과 화면 없이 바로 끝
        layers.Add(layer);
        layer.Show();
        layer.Bounds = arena;
        Dbg.Log("game", ("gun", game.Gun.Name), ("theme", game.Theme));
        layer.Activate(); // Esc 를 받으려면 활성 창이어야 한다
        foreach (var o in overlays) Native.Topmost(o.Handle); // 판 위에 탄흔·시간이 보이게
        Native.SetClickThrough(Handle, true);
        Native.Topmost(Handle);
        pad.Hide();
        var above = wins.TakeWhile(w => w.H != window).ToList(); // 그 창 위에 겹친 창들 뒤에 숨는다
        b.StartGame(arena, window == IntPtr.Zero ? Props.Covers(wins, arena, b.CellPx) : Props.Covers(above, arena, b.CellPx));
    }

    void EndGame()
    {
        var gm = game!;
        game = null;
        foreach (var l in layers) l.Close();
        layers.Clear();
        Native.SetClickThrough(Handle, false);
        if (gameFocus != IntPtr.Zero) Native.SetForegroundWindow(gameFocus); // 하던 일로 돌려준다
        gameBest = gm.Best;
        b.EndGame(gm.Hits);
        foreach (var o in overlays) o.Invalidate();
        Native.Topmost(Handle);
        Save();
    }

    // 왼쪽 누름은 발사(기관총은 떼기 전까지 연사), 오른쪽은 장전. 디버그 명령도 여기로 들어온다
    public void GameMouse(bool right, int x, int y)
    {
        if (game is not { } gm) return;
        if (right) { gm.Reload(); return; }
        string result = gm.Press(x, y, HitPet);
        if (result is "exit" or "close" or "wait") Dbg.Log("press", ("result", result));
    }

    public void GameRelease() => game?.Release();

    void OnShot(string result, int x, int y)
    {
        var gm = game!;
        switch (result)
        {
            case "hit":
                b.GameHit(x, y, gm.GunPivot.X, gm.GunPivot.Y);
                Sfx.Play("hit");
                break;
            case "miss":
                b.NearMiss(x, y);
                Sfx.Play(gm.Gun.Name);
                break;
            case "empty": Sfx.Play("empty"); break;
            case "reload": Sfx.Play("reload"); break;
            case "boom": Sfx.Play("boom"); break;
        }
        if (result is "hit" or "miss") // 탄피가 총 옆으로 튀어 떨어진다
        {
            double a = gm.GunAngle(), len = 12 * b.Scale * 2;
            double ex = gm.GunPivot.X + Math.Cos(a) * len, ey = gm.GunPivot.Y + Math.Sin(a) * len - 6 * b.Scale;
            b.Bits.Add(new Bit { X = ex, Y = ey, Vx = -Math.Sign(Math.Cos(a)) * new Random().Next(120, 260) * b.Scale / 3.0, Vy = -new Random().Next(200, 380) * b.Scale / 3.0,
                G = 1500 * b.Scale / 3.0, Life = .6, C = gm.Gun.Pellets > 1 ? Color.FromArgb(232, 72, 92) : Color.FromArgb(220, 170, 60) }); // 샷건은 빨간 탄피
        }
        if (result != "loaded") Dbg.Log("fire", ("result", result), ("x", x), ("y", y), ("ammo", gm.Ammo), ("hits", gm.Hits));
    }

    Cursor? shownCursor;
    double nextWindowCheck;

    // 조준경 커서: 퍼진 만큼 십자가 벌어지고, 맞히면 잠깐 빨갛다. 바뀔 때만 갈아 끼운다
    void SyncCrosshair()
    {
        var gm = game!;
        Cursor? want = gm.Aiming ? Crosshair.Get(Crosshair.Level(gm.Bloom + gm.Gun.Spread * .7), gm.Clock < gm.HitUntil, b.Scale) : null; // 샷건은 처음부터 넓다
        if (gm.Stage == Game.Phase.Boom && shownCursor != null) { foreach (var l in layers) l.HidePointer(); shownCursor = null; return; }
        if (want == null || want == shownCursor) return;
        shownCursor = want;
        foreach (var l in layers) l.Cursor = want;
    }

    bool HitPet(int x, int y)
    {
        var (name, frame, flip, shake) = drawn;
        if (name == null || b.Shielded(x, y)) return false; // 창 뒤로 숨은 몸은 안 맞는다
        var bmp = sprites.Get(name, frame, b.Scale, flip);
        return Game.HitBitmap(bmp, x - (Left + 8 + shake), y - Top, 3 * b.Scale); // 둘레 9px 까지는 봐준다
    }

    // 디버그: 그 자리를 누르면 무엇이 클릭을 받는지
    public string WhoGetsClick(int x, int y)
    {
        var h = Native.RootAt(new Point(x, y));
        if (h == Handle) return "pet";
        if (h == pad.Handle) return "pad";
        if (layers.Any(l => l.Handle == h)) return "game-layer";
        if (overlays.Any(o => o.Handle == h)) return "overlay";
        return "other";
    }

    // 디버그: 앱이 그리는 것만 회색 바탕에 다시 그린다. 실제 화면을 찍지 않아서 일하던 내용이 안 남는다

    // 진짜 상태로 설정 창을 화면 밖에서 그려 본다 (디버그용: 띄워 둔 설정 창은 건드리지 않는다)
    public void SettingsShot(string path, string tab)
    {
        using var f = new Settings(Sfx.On, autoUpdate, () => sprites, Status, Book()) { StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
        f.Show();
        f.SelectTab(tab);
        Application.DoEvents();
        using var bmp = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
        f.Close();
        bmp.Save(path);
        Dbg.Log("settings-shot", ("tab", tab));
    }

    public void Shot(string path)
    {
        var v = SystemInformation.VirtualScreen;
        using var bmp = new Bitmap(v.Width, v.Height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(40, 44, 50)); // 모니터 사이 빈 공간
        using var ground = new SolidBrush(Color.FromArgb(120, 140, 155));
        foreach (var o in overlays)
        {
            var at = new Rectangle(o.Left - v.X, o.Top - v.Y, o.Width, o.Height);
            g.ResetTransform();
            g.SetClip(at);
            g.FillRectangle(ground, at);
            g.TranslateTransform(at.X, at.Y);
            o.Render(g);
        }
        g.ResetClip();
        g.ResetTransform();
        var (name, frame, flip, _) = drawn;
        if (name != null)
        {
            g.DrawImageUnscaled(sprites.Get(name, frame, b.Scale, flip), Left - v.X + 8, Top - v.Y);
            var at = g.Transform;
            g.TranslateTransform(Left - v.X, Top - v.Y);
            DrawClover(g, name, frame, flip, 0);
            DrawCap(g, name, frame, flip, 0); // 수면 모자도 (본체 창과 같은 자리)
            g.Transform = at;
        }
        bmp.Save(path, ImageFormat.Png);
    }
}
