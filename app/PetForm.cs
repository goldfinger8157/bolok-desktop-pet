using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using Microsoft.Win32;

namespace Bolok;

public sealed record RunRecord(DateTime At, int Level, int Score, int Seeds); // 옛 저장 칸(RunScores)의 한 줄

public sealed class SaveData
{
    public Pet Pet { get; set; } = new();
    public double X { get; set; }
    public double Y { get; set; }
    public List<int[]> Holes { get; set; } = new();
    public int GameBest { get; set; }                 // 옛 저장 칸: 게임이 모듈이 되기 전 기록 (GameBest·Run…·BreakBest·WormsWins). 읽기만 하고, GameModes.WithLegacy 가 GameData 로 바꿔 넘긴다
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
    Region? clipShown;
    bool flashShown;
    IntPtr gameFocus;
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
        b.SpeciesKey = () => species; // 창 위 빼꼼 높이를 그 종의 눈 높이로
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
        hud = new Hud(b, debug ? DebugLine : null) { GameLine = () => string.Join("  ", modes.Select(m => m.HudLine).Where(line => line.Length > 0)) };
        hud.SpeciesName = () => b.P.Stage == "egg" ? "" : L.Species(species);
        if (!debug) // 새 버전을 받아 바꿔 두면 메뉴에 알린다. 그냥 둬도 다음에 켤 때 새 버전이다
            Updater.Start(() => autoUpdate, tag => BeginInvoke(() => { updateTag = tag; updateItem.Text = L.T("menu.update", tag); updateItem.Visible = true; settings?.ShowReady(); }));
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

    void BuildOverlays() // 처음과 모니터가 바뀔 때 (연결도 여기서 다 한다: 다시 만들 때 친구·선물·머리 장식·꿈·게임 그림이 빠지던 것)
    {
        foreach (var sc in Screen.AllScreens)
        {
            var o = new Overlay(b, () => sprites, sc.Bounds) { Sky = () => weather };
            o.Guest = () => visitor; o.GuestSprites = () => visitorSprites; o.Present = () => present; o.Clover = CloverShown; o.LeafPile = LeafPileShown; o.Snowman = SnowmanShown; o.Dreams = Dreams; o.Modes = () => modes;
            overlays.Add(o);
        }
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
        Sfx.On = pendingSave.GameSound;
        autoUpdate = pendingSave.AutoUpdate;
        if (pendingSave.Species != species || pendingSave.Variant != variant) SetSpecies(pendingSave.Species, pendingSave.Variant);
        (met, released) = (pendingSave.Met?.ToHashSet() ?? new(), pendingSave.Released ?? new());
        if (b.P.Stage != "egg") met.Add(species); // 도감 전부터 키우던 아이도 도감에
        (visitDay, visitMin, lastVisitDay, visits) = (pendingSave.VisitDay ?? "", pendingSave.VisitMin, pendingSave.LastVisitDay ?? "", pendingSave.Visits ?? new());
        language = pendingSave.Language;
        L.Use(language);
        var gameData = GameModes.WithLegacy(pendingSave); // 모듈이 되기 전 기록 칸도 모듈 기록으로 바꿔 넘긴다
        foreach (var m in modes) m.LoadState(gameData.GetValueOrDefault(m.Id));
        clover.Load(pendingSave.Clover);
        (gifts, album, cards, visitCounts) = (pendingSave.Gifts ?? new(), pendingSave.Album ?? new(), pendingSave.Cards ?? new(), pendingSave.VisitCounts ?? new());
        (cardFrom, cardForm, cardDay, morningDay, nightDay, cardVariant) = (pendingSave.CardFrom, pendingSave.CardForm ?? "child", pendingSave.CardDay ?? "", pendingSave.MorningDay ?? "", pendingSave.NightDay ?? "", pendingSave.CardVariant);
        if (pendingSave.X != 0) { b.X = pendingSave.X; b.Y = pendingSave.Y; b.Settle(); }
        foreach (var h in pendingSave.Holes) b.AddHole(h[0], h[1], false);
        pendingSave = null;
    }

    void Save()
    {
        var data = new SaveData { Pet = b.P, X = b.X, Y = b.Y, Holes = b.Holes.Select(h => new[] { h.Cx, h.Cy }).ToList(), GameSound = Sfx.On, AutoUpdate = autoUpdate, Species = species, Met = met.ToList(), Released = released, VisitDay = visitDay, VisitMin = visitMin, LastVisitDay = lastVisitDay, Visits = visits, Language = language, Clover = clover.Snapshot(), Variant = variant, CardVariant = cardVariant,
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
        foreach (var m in modes) // 게임 모듈. 그 게임을 하는 중에 또 고르면 게임에 알린다 (한 발 더 쏘기 같은 것)
        {
            if (m.MenuKey is not { } key) continue;
            void Go(Action start) => BeginInvoke(() => { if ((!InGame || m.Running) && b.P.Stage != "egg") start(); }); // 메뉴가 닫히고 포커스가 돌아온 뒤에 시작한다
            if (m.Choices is not { } choices) { Add(key, () => Go(m.Start)); continue; }
            var parent = new ToolStripMenuItem(); // 하위 메뉴: 시작할 때 하나를 고르는 게임
            menuTexts.Add((parent, () => L.T(key)));
            foreach (var c in choices) menuTexts.Add((parent.DropDownItems.Add("", null, (_, _) => Go(c.Start)), c.Text));
            menu.Items.Add(parent);
        }
        if (showcase != null) { menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(showcase.Menu(menuTexts, () => InGame, a => BeginInvoke(a))); }
        if (debug) menu.Items.Add(EnvMenu());
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
    bool IGameHost.HitPet(int x, int y, int pad) // 지금 본체 창에 그려진 그림의 픽셀로 본다
    {
        var (name, frame, flip, shake) = drawn;
        return name != null && Sprites.Near(sprites.Get(name, frame, 1, flip), (x - (Left + 8 + shake)) / (double)b.Scale, (y - Top) / (double)b.Scale, pad);
    }
    void IGameHost.HideFromCapture(bool on)
    {
        Native.HideFromCapture(Handle, on);
        foreach (var o in overlays) Native.HideFromCapture(o.Handle, on);
        foreach (var l in layers) Native.HideFromCapture(l.Handle, on);
    }
    IReadOnlyList<string> IGameHost.Friends => released.Select(r => PetArt.Key(r.Species, r.Variant)).Distinct().ToList();
    void IGameHost.Repaint() { foreach (var o in overlays) o.Invalidate(); }

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
        b.Update(dt, !InGame && Afk());
        foreach (var m in modes) if (m.Running) m.Frame(dt);
        if (leaving) LeaveFrame(dt);
        WatchHatch();
        CloverTick();
        GestureTick();
        VisitTick();
        PresentTick();
        WeatherTick();
        if (visitor != null) visitor.Update(dt, b.X, b.Y, b.Mode is Mode.Wander or Mode.Anim || b.P.Asleep, b.P.Asleep, b.Area);
        SyncScale();
        var at = new Point((int)b.X - Width / 2, (int)b.Y - Height);
        if (at != Location) Location = at;

        string name = b.SpriteName();
        int ms = b.Mode == Mode.Puppet && b.PuppetMs > 0 ? b.PuppetMs : b.Mode == Mode.Held && b.Pose == "fly" ? 50 : PoseMs.GetValueOrDefault(b.Pose, 600); // 게임이 정했으면 그대로, 흔들릴 땐 더 빨리
        int frame = (int)(sec * 1000 / ms);
        int shake = b.Now < b.ShakeUntil ? (int)(Math.Sin(b.Now * 40) * 5) : 0;
        var now = (name, frame, b.Dir < 0, shake);
        bool flash = b.Now < b.FlashUntil || b.Blink;
        if (now != drawn || b.PuppetClip != clipShown || flash != flashShown) { drawn = now; clipShown = b.PuppetClip; flashShown = flash; Invalidate(); }
        WeatherLayout();

        int want = b.Mode is Mode.Thrown or Mode.Held or Mode.Puppet ? 15 : 30; // 빠르게 움직일 때만 자주 그린다
        if (timer.Interval != want) timer.Interval = want;

        SyncPad();
        foreach (var o in overlays) o.Sync();
        hud.Sync();
        dev?.Poll(fps, frameMs);

        if (b.Now > nextSave) { nextSave = b.Now + 60; Save(); }
        ApplyZOrder();
        double took = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        frameMs = frameMs * .95 + .05 * took;
    }

    // 평소엔 맨 앞이고, 일부러 숨을 때만 그 창 바로 아래로 들어간다
    void SyncPad()
    {
        bool on = b.ClickPadOn && !b.Behind;
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
        if (b.PuppetClip is { } c) // 게임이 가린 부분(창 뒤에 숨은 몸)은 안 그린다
        {
            using var mask = c.Clone();
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
        if (!weather.HidesHeadItem) DrawClover(e.Graphics, name, frame, flip, shake); // 우산 잎을 들거나 머리에 눈이 쌓이면 머리 장식은 잠깐 내려 둔다 (겹치지 않게)
        DrawCap(e.Graphics, name, frame, flip, shake);
        weather.DrawWet(e.Graphics, sprites, name, frame, flip, b.Scale, 8 + shake, 0, b.Now);
        weather.DrawPile(e.Graphics, sprites, name, frame, flip, b.Scale, 8 + shake, 0);
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
        return new PetStatus(species, p.Stage, p.AgeMin, p.Full, p.Mood, p.Energy, p.Asleep, p.Need(k), p.Miss, Math.Max(p.Bites, b.Holes.Count),
            p.BitesTotal, left is { } l ? Math.Max(0, l) : null, Math.Clamp(frac, 0, 1), to, modes.SelectMany(m => m.Stats()).ToList()); // 게임 기록은 게임 모듈이 낸다
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
        if (clover.State == Clover.Phase.Falling) // 낙엽이 내려오는 중: 다 내려오면 머리 위에 살포시 (날아가는 중이면 바닥에)
        {
            bool onHead = b.Mode is not (Mode.Thrown or Mode.Held) && !b.Behind && !InGame;
            if (clover.Land(now, onHead, b.X, ground)) { if (clover.State == Clover.Phase.Worn) b.Emote("heart", 1.4); Changed(clover.State == Clover.Phase.Worn ? "land" : "fell"); }
            return;
        }
        if (clover.State == Clover.Phase.Ground)
        {
            if (free && clover.TryPick(b.X, b.Y, b.CellPx)) { if (Clover.Falls(clover.Kind)) pileUntil = now + 20; b.Greet("happy", 1.6); b.Emote("heart", 1.6); Changed("pick"); } // 낙엽 더미는 주운 뒤에도 잠시 남는다
            else if (clover.Lost(now)) Changed("lost");
            else if (free && t > nextCloverWalk) { nextCloverWalk = t + 3000; b.GoTo(clover.X, clover.Y); }
            return;
        }
        if (clover.State == Clover.Phase.None && free && t > nextCloverRoll)
        {
            nextCloverRoll = t + 60_000;
            if (clover.TrySprout(Today, now, b.X, b.Dir, ground, b.CellPx, b.Area, Random.Shared, headY: b.HeadY)) { if (clover.State != Clover.Phase.Falling) b.Emote("bang", 1.2); Changed(clover.State == Clover.Phase.Falling ? "fall" : "sprout"); }
        }
    }

    // 땅의 낙엽 더미: 낙엽이 땅에 있는 동안(던져서 떨궜을 때 포함), 그리고 주운 뒤 20초 (YELL 2026-11-13: 쌓인 낙엽에서 줍는 느낌)
    double pileX, pileY, pileUntil;
    (double X, double Y, int Seed)? LeafPileShown()
    {
        if (clover.State == Clover.Phase.Ground && Clover.Falls(clover.Kind)) (pileX, pileY) = (clover.X, clover.Y);
        else if (Environment.TickCount64 / 1000.0 >= pileUntil) return null;
        return (pileX, pileY, (int)Math.Abs(pileX) % 97);
    }

    // 바닥에 있거나 내려오는 중인 머리 장식 (Overlay 가 그린다)
    (string Kind, double X, double Y, bool Falling, bool Flip)? CloverShown()
    {
        if (clover.State == Clover.Phase.Ground) return (clover.Kind, clover.X, clover.Y, false, false);
        if (clover.State != Clover.Phase.Falling) return null;
        double now = Environment.TickCount64 / 1000.0;
        var (x, y) = clover.FallPos(now, b.X, b.HeadY, b.CellPx);
        return (clover.Kind, x, y, true, clover.Flip(now));
    }

    // 디버그 앱 메뉴 「환경」(YELL 2026-11-13: 환경 제어를 컨텍스트 메뉴에서 고르게, 디버그 모드에서만): 날씨·머리 장식·밤 모자·자리 비움.
    // 디버그 전용이라 글은 한국어 그대로 (lang.json 에 넣지 않는다)
    ToolStripMenuItem EnvMenu()
    {
        var env = new ToolStripMenuItem("환경 (디버그)");
        ToolStripMenuItem Group(string text) { var g = new ToolStripMenuItem(text); env.DropDownItems.Add(g); return g; }
        void Item(ToolStripMenuItem g, string text, Action act) => g.DropDownItems.Add(text, null, (_, _) => BeginInvoke(() => { act(); Dbg.Log("env-menu", ("item", g.Text + "/" + text)); }));
        void Wake() { if (dev != null) dev.ForceAfk = false; if (b.P.Asleep) b.Care("wake"); } // 보여 주려면 깨어 있어야 한다
        var w = Group("날씨");
        Item(w, "눈", () => { Wake(); WeatherCommand(new[] { "weather", "snow" }); });
        Item(w, "눈 (머리에 바로 다 쌓기)", () => { Wake(); WeatherCommand(new[] { "weather", "snow", "pile" }); });
        Item(w, "비 — 잎 우산", () => { Wake(); WeatherCommand(new[] { "weather", "rain", "umbrella" }); });
        Item(w, "비 — 그냥 맞기", () => { Wake(); WeatherCommand(new[] { "weather", "rain", "wet" }); });
        Item(w, "비 — 창 뒤로 피하기", () => { Wake(); WeatherCommand(new[] { "weather", "rain", "shelter" }); });
        Item(w, "그치기", () => WeatherCommand(new[] { "weather", "off" }));
        var h = Group("머리 장식");
        foreach (var (kind, text) in new[] { ("sprout", "새싹 돋기"), ("clover", "네잎클로버 돋기"), ("sunflower", "해바라기 돋기"),
                                              ("fallen", "낙엽(잎맥 잎) 내려오기"), ("ginkgo", "은행잎 내려오기"), ("maple", "단풍잎 내려오기"), ("redmaple", "빨간 단풍잎 내려오기") })
            Item(h, text, () => { Wake(); CloverCommand(new[] { "clover", kind }); });
        Item(h, "낙엽 더미 (땅에 떨구기)", () => CloverCommand(new[] { "clover", "drop", new[] { "fallen", "ginkgo", "maple", "redmaple" }[Random.Shared.Next(4)] }));
        Item(h, "벗기", () => CloverCommand(new[] { "clover", "off" }));
        var etc = Group("그 밖");
        Item(etc, "밤 수면 모자 켜기/끄기", () => { ForceNight = !ForceNight; capSpot.Clear(); Invalidate(); });
        Item(etc, "자리 비움: 안 함 (계속 깨어 있기)", () => { if (dev != null) dev.ForceAfk = false; });
        Item(etc, "자리 비움: 바로 (잠들기)", () => { if (dev != null) dev.ForceAfk = true; });
        Item(etc, "자리 비움: 자동", () => { if (dev != null) dev.ForceAfk = null; });
        return env;
    }

    // 디버그: clover [clover|sprout|sunflower|fallen|ginkgo|maple|redmaple] (지금 돋기, 낙엽 넷은 내려오기) / clover wear [종류] (바로 머리에) / clover drop / clover off
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
            clover.TrySprout(Today, now, b.X, b.Dir, ground, b.CellPx, b.Area, Random.Shared, 1, b.HeadY, Clover.Kinds.Contains(what) ? what : null);
            if (clover.State != Clover.Phase.Falling) b.Emote("bang", 1.2);
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
        bool tilt = Clover.Falls(clover.Kind) && sprites.Has(item + "_45"); // 낙엽: 45° 기울여 꼭지부터 꽂는다 (YELL 2026-11-13: 꽂힌 게 제일 귀엽다)
        bool mirror = tilt && clover.Look % 2 == 1;                           // 왼쪽·오른쪽 어느 쪽으로 기울지는 내려앉을 때마다 무작위
        if (tilt) item += "_45";
        if (clover.State != Clover.Phase.Worn || b.Pose is "fly" or "roll" or "flop" || !sprites.Has(item) || name.StartsWith("egg")) return;
        int n = sprites.Frames(name), f = (frame % n + n) % n, side = clover.Kind == "sprout" ? 0 : Clover.Falls(clover.Kind) ? 3 : -4; // 새싹은 정수리 가운데, 네잎클로버·해바라기는 왼쪽 옆, 낙엽은 가운데에서 살짝 오른쪽 (YELL: 한가운데는 어색 → 6칸은 너무 치우침, 낙엽 폭 절반만큼 안으로)
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
        string stemKey = mirror ? item + "|m" : item;
        if (!cloverStem.TryGetValue(stemKey, out var stem)) // 그림의 줄기 끝 (가장 아래 칸). 누운 낙엽은 맨 아래 줄의 가운데
        {
            var icon1 = sprites.Get(item, 0, 1, mirror);
            stem = (20, 23);
            for (int y = icon1.Height - 1, found = 0; y >= 0 && found == 0; y--)
            {
                int x0 = -1, x1 = -1;
                for (int x = 0; x < icon1.Width; x++) if (icon1.GetPixel(x, y).A > 0) { if (x0 < 0) x0 = x; x1 = x; }
                if (x0 >= 0) { stem = (x0, y); found = 1; }
            }
            cloverStem[stemKey] = stem;
        }
        int k = b.Scale, s = Clover.Scale(k);
        var (sx, sy) = stem;
        int sink = tilt ? k : 0; // 낙엽은 꼭지 끝이 머리에 한 칸 묻히게 (꽂힌 듯)
        g.DrawImageUnscaled(sprites.Get(item, 0, s, mirror), 8 + shake + spot.X * k - sx * s, spot.Top * k - sy * s + sink); // 줄기 끝만 머리에 닿고 잎은 살짝 떠 있게 (YELL)
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

    // ── 날씨 (규칙은 Weather.cs): 하루 몇 번 볼록이 모니터에 눈·비가 조금. 일하는 창 위엔 안 그린다. 게임·잠·자리 비움엔 멈춘다 ──
    readonly Weather weather = new();
    long nextWeatherRoll;
    bool weatherShown;
    int weatherLook; // 본체 창에 그린 날씨 모습 (물방울 장·우산): 바뀌면 다시 그린다

    void WeatherTick()
    {
        bool quiet = Weather.Quiet(InGame, b.P, Afk());
        long t = Environment.TickCount64;
        if (t >= nextWeatherRoll)
        {
            nextWeatherRoll = t + 1000;
            if (weather.Tick(b.Now, DateTime.Now, !quiet && visitor == null && b.Mode == Mode.Wander, Random.Shared) is { } ev)
                Dbg.Log("weather", ("event", ev), ("sky", weather.Now.ToString()), ("pet", weather.Reaction.ToString()), ("sec", (int)(weather.Until - weather.From)));
        }
        weatherShown = weather.On && !quiet;
        b.Calm = weatherShown && weather.Reaction != Weather.Cover.Shelter; // 눈·비: 많이 안 움직이게 (창 뒤로 피하기는 그대로 간다)
        SnowmanTick();
        weather.React(b, weatherShown, Random.Shared);
    }

    // 눈사람 (YELL 2026-11-13): 눈이 오면 볼록이 곁 바닥에 볼록이 50% 크기 눈사람을 하나 — 6초마다 한 단계씩 쌓아 올린다
    // (아래 눈덩이 → 머리 → 얼굴·팔·목도리). 눈이 그치면 1분 뒤 녹아 없어진다
    double snowmanAt = -1, snowmanGone;
    PointF snowmanPos;
    int snowmanStage;
    void SnowmanTick()
    {
        double now = b.Now;
        bool snowing = weatherShown && weather.Now == Weather.Sky.Snow;
        if (snowing && snowmanAt < 0 && weather.Density(now) > .5 && b.Mode == Mode.Wander)
        {
            float x = (float)Math.Clamp(b.X + b.Dir * b.CellPx * .85, b.Area.Left + b.CellPx, b.Area.Right - b.CellPx);
            (snowmanAt, snowmanGone, snowmanPos) = (now, 0, new PointF(x, (float)b.Y));
            b.Greet("dig", 1.6); // 눈을 굴려 쌓는다
            Dbg.Log("snowman", ("x", (int)x), ("y", (int)b.Y));
        }
        if (snowmanAt < 0) return;
        if (snowing) snowmanGone = 0;
        else if (snowmanGone == 0) snowmanGone = now + 60;
        else if (now > snowmanGone) { snowmanAt = -1; foreach (var o in overlays) o.Invalidate(); return; }
        int stage = Math.Min(2, (int)((now - snowmanAt) / 6));
        if (stage != snowmanStage) { snowmanStage = stage; if (stage > 0) b.Greet("dig", 1.2); }
    }
    (PointF Pos, int Stage)? SnowmanShown() => snowmanAt < 0 ? null : (snowmanPos, snowmanStage);

    // 눈·비가 내리는 곳: 화면 전체가 아니라 볼록이 머리 위 좁은 구역만 (YELL 2026-11-13: 화면 전체면 너무 티 난다).
    // 볼록이 크기를 100% 로 보고 가로 300%, 발에서 위로 300% — 볼록이를 따라다닌다
    Rectangle WeatherZone()
    {
        int c = b.CellPx;
        var zone = new Rectangle((int)b.X - c * 3 / 2, (int)b.Y - c * 3, c * 3, c * 3);
        return Rectangle.Intersect(zone, b.Area);
    }

    // 눈·비도 볼록이와 같은 층 (YELL 2026-11-13: 「펫하고 같은 레벨(z-index)이면 좋겠는데, 활성화된 창에 눈/비가 가려진다」):
    // 볼록이가 맨 위면 일하는 창 위로도 내리고, 창 뒤에 숨었으면 그 창에 가린다
    Rectangle WeatherHidden() => b.Behind && b.HideWnd != IntPtr.Zero && Native.Probe(b.HideWnd) is { Alive: true, Minimized: false } s ? s.R : Rectangle.Empty;

    // 그림 자리가 정해진 뒤 (본체 창 자리·그림): 우산 잎 자리와 알갱이
    void WeatherLayout()
    {
        var (name, frame, flip, shake) = drawn;
        weather.PlaceLeaf(sprites, name, frame, flip, b.Scale, Left + 8 + shake, Top, b.Now);
        weather.Layout(weatherShown, b.Now, WeatherZone(), b.Scale, WeatherHidden());
        int look = (weather.Soaked ? 1 + Weather.WetFrame(b.Now) : 0) + (weather.UmbrellaUp ? 4 : 0) + weather.PileShown * 8;
        if (look != weatherLook) { weatherLook = look; Invalidate(); }
    }

    // 디버그: weather snow [pile] / weather rain [umbrella|wet|shelter] / weather off (뒤에 초를 붙이면 그만큼, 없으면 3분). weather 만 쓰면 지금 상태를 로그에.
    //   weather snow pile: 머리에 눈이 바로 다 쌓인다 (가만히 → 털기를 바로 보인다)
    public void WeatherCommand(string[] a)
    {
        string what = a.Length > 1 ? a[1] : "";
        double sec = a.Length > 2 && double.TryParse(a[^1], out var n) ? n : 180;
        var cover = a.Length > 2 ? a[2] switch { "umbrella" => Weather.Cover.Umbrella, "wet" => Weather.Cover.Wet, "shelter" => Weather.Cover.Shelter, _ => Weather.Cover.None } : Weather.Cover.None;
        if (what == "off") weather.Stop(b.Now);
        else if (what is "snow" or "rain")
        {
            bool pile = what == "snow" && a.Length > 2 && a[2] == "pile";
            if (!pile || weather.Now != Weather.Sky.Snow) weather.Begin(what == "snow" ? Weather.Sky.Snow : Weather.Sky.Rain, b.Now, Random.Shared, cover, sec); // 오던 눈에 pile 만 더하면 그대로
            if (pile) weather.FillPile();
        }
        else if (what != "") throw new ArgumentException("weather snow [pile] | rain [umbrella|wet|shelter] | off");
        Dbg.Log("weather", ("event", "cmd-" + (what == "" ? "state" : what)), ("sky", weather.Now.ToString()), ("pet", weather.Reaction.ToString()), ("sec", (int)Math.Max(0, weather.Until - b.Now)),
            ("quiet", Weather.Quiet(InGame, b.P, Afk())), ("asleep", b.P.Asleep), ("game", InGame), ("mode", b.Mode.ToString()));
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

    bool Playing
    {
        get { foreach (var m in modes) if (m.Running) return true; return false; } // 프레임마다 여러 번 묻는다: 메모리를 잡지 않게 돈다
    }

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
        if (!Playing) return;
        Dbg.Log("boss-key");
        quit?.Invoke(); // 판이 알려 준 끝내기 (아래와 겹쳐도 게임마다 한 번만 끝난다)
        foreach (var m in modes) if (m.Running) m.Stop(true);
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
            if (!weather.HidesHeadItem) DrawClover(g, name, frame, flip, 0);
            DrawCap(g, name, frame, flip, 0); // 수면 모자도 (본체 창과 같은 자리)
            weather.DrawWet(g, sprites, name, frame, flip, b.Scale, 8, 0, b.Now);
            weather.DrawPile(g, sprites, name, frame, flip, b.Scale, 8, 0);
            g.Transform = at;
        }
        bmp.Save(path, ImageFormat.Png);
    }
}
