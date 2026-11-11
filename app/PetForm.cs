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
    public bool AutoUpdate { get; set; } = true;
    public int RunBest { get; set; }
    public int RunLast { get; set; }
    public int RunLevel { get; set; } = 1;
    public int BreakBest { get; set; }
    public int WormsWins { get; set; }
    public List<RunRecord> RunScores { get; set; } = new();   // 최근 50판
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}

// 볼록이 본체 창. 투명색 자리는 클릭이 뒤로 지나가고, 몸통 픽셀만 마우스를 받는다
public sealed class PetForm : Form
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
    readonly ToolStripMenuItem sleepItem = new("재우기");
    readonly ToolStripMenuItem hideItem = new("숨기기");
    readonly ToolStripMenuItem soundItem = new("게임 소리") { CheckOnClick = true };
    readonly ToolStripMenuItem autoUpdateItem = new("자동 업데이트") { CheckOnClick = true, Checked = true };
    readonly ToolStripMenuItem updateItem = new("새 버전으로 다시 시작") { Visible = false };
    readonly NotifyIcon tray = new();
    readonly System.Windows.Forms.Timer timer = new() { Interval = 30 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    DevLink? dev;
    Sprites sprites;
    double lastSec, nextSave, nextTop, nextBelow, fps, frameMs;
    Rectangle scaledFor;
    bool locked, hiddenByUser, pressing, moved, below;
    Point pressAt;
    PointF pressCell;
    (string, int, bool, int) drawn;
    IntPtr returnFocus;

    public PetForm(Sprites sprites, Tuning k, string dataDir, string? runDir, string? assetsDir)
    {
        this.sprites = sprites;
        debug = runDir != null;
        savePath = Path.Combine(dataDir, "pet.json");
        var area = Screen.PrimaryScreen!.WorkingArea;
        b = new Brain(LoadPet(k), k, new World(Screen.AllScreens.Select(s => s.WorkingArea)), area) { Scale = ScaleFor(k) };
        b.Windows = Native.ListWindows;
        b.Foreground = () => Native.OwnForeground() ? b.Active : Native.Foreground(); // 메뉴를 열어도 활성 창이 바뀐 걸로 치지 않는다
        Restore();

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Key;
        TransparencyKey = Key;
        Text = "볼록이";
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        ClientSize = new Size(b.CellPx + 16, b.CellPx);

        BuildOverlays();
        pad.MouseDown += (_, e) => OnMouseDown(e); // 받침은 볼록이 창과 같은 자리·크기라 좌표가 그대로다
        pad.MouseMove += (_, e) => OnMouseMove(e);
        pad.MouseUp += (_, e) => OnMouseUp(e);
        hud = new Hud(b, debug ? DebugLine : null) { RunLine = () => runLast > 0 ? $"달리기 Lv.{runLevel} {runLast} (최고 {runBest})" : "" };
        if (!debug) // 새 버전을 받아 바꿔 두면 메뉴에 알린다. 그냥 둬도 다음에 켤 때 새 버전이다
            Updater.Start(() => autoUpdateItem.Checked, tag => BeginInvoke(() => { updateItem.Text = $"새 버전 {tag} 으로 다시 시작"; updateItem.Visible = true; }));
        foreach (var o in overlays) { o.Runner = () => runner; o.Break = () => breakout; o.WormsGame = () => worms; o.MinigunGame = () => minigun; o.IcbmGame = () => icbm; }
        hud.PlaceAt(area);
        if (runDir != null) dev = new DevLink(runDir, assetsDir!, b, this);

        BuildMenu();
        tray.Icon = Icon.FromHandle(this.sprites.Get("child/0/idle", 0, 1).GetHicon());
        tray.Text = "볼록이";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleHud(); };

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
        if (!hiddenByUser) ShowOverlays();
        hud.PlaceAt(Screen.PrimaryScreen!.WorkingArea);
        Native.Topmost(Handle);
        Dbg.Log("screens-changed", ("screens", DescribeScreens()));
    }

    public void SwapSprites(Sprites s) { sprites = s; drawn = default; Invalidate(); }

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
        if (pendingSave == null) return;
        gameBest = pendingSave.GameBest;
        Sfx.On = pendingSave.GameSound;
        autoUpdateItem.Checked = pendingSave.AutoUpdate;
        (breakBest, wormsWins) = (pendingSave.BreakBest, pendingSave.WormsWins);
        (runBest, runLast, runLevel, runScores) = (pendingSave.RunBest, pendingSave.RunLast, Math.Max(1, pendingSave.RunLevel), pendingSave.RunScores ?? new());
        if (pendingSave.X != 0) { b.X = pendingSave.X; b.Y = pendingSave.Y; b.Settle(); }
        foreach (var h in pendingSave.Holes) b.AddHole(h[0], h[1], false);
        pendingSave = null;
    }

    void Save()
    {
        var data = new SaveData { Pet = b.P, X = b.X, Y = b.Y, Holes = b.Holes.Select(h => new[] { h.Cx, h.Cy }).ToList(), GameBest = gameBest, GameSound = Sfx.On, AutoUpdate = autoUpdateItem.Checked, RunBest = runBest, RunLast = runLast, RunLevel = runLevel, RunScores = runScores, BreakBest = breakBest, WormsWins = wormsWins };
        var tmp = savePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data));
        File.Move(tmp, savePath, true);
    }

    void BuildMenu()
    {
        void Add(string text, Action go) => menu.Items.Add(text, null, (_, _) => go());
        // 돌보기 / 게임 / 앱 / 종료 를 분리선으로 나눈다
        Add("밥 주기", () => b.Care("feed"));
        Add("놀아주기", () => b.Care("play"));
        sleepItem.Click += (_, _) => b.Care(b.P.Asleep ? "wake" : "sleep");
        menu.Items.Add(sleepItem);
        menu.Items.Add(new ToolStripSeparator());
        Add("총쏘기 게임", () => BeginInvoke(StartGame)); // 메뉴가 닫히고 포커스가 돌아온 뒤에 시작한다
        Add("달리기 게임", () => BeginInvoke(StartRun));
        Add("벽돌깨기", () => BeginInvoke(StartBreak));
        Add("미니건", () => BeginInvoke(StartMinigun));
        Add("ICBM 발사", () => BeginInvoke(StartIcbm));
        var cannon = new ToolStripMenuItem("포트리스"); // 대포를 고르면 체력이 정해진다
        foreach (var gun in Worms.Guns)
            cannon.DropDownItems.Add($"{gun.Label} (HP {gun.Hp}{(gun.Shells > 1 ? $", {gun.Shells}발" : "")})", null, (_, _) => BeginInvoke(() => StartWorms(gun.Name)));
        menu.Items.Add(cannon);
        soundItem.Click += (_, _) => { Sfx.On = soundItem.Checked; Save(); };
        menu.Items.Add(soundItem);
        menu.Items.Add(new ToolStripSeparator());
        Add("상태 보기", ToggleHud);
        hideItem.Click += (_, _) => SetHidden(!hiddenByUser);
        menu.Items.Add(hideItem);
        Add("화면 돌려받기", b.GiveBack);
        autoUpdateItem.Click += (_, _) => Save();
        menu.Items.Add(autoUpdateItem);
        updateItem.Click += (_, _) => { Save(); Updater.Restart(); };
        menu.Items.Add(updateItem);
        menu.Items.Add(new ToolStripSeparator());
        Add("종료", Application.Exit);
        menu.Opening += (_, _) =>
        {
            sleepItem.Text = b.P.Asleep ? "깨우기" : "재우기";
            hideItem.Text = hiddenByUser ? "다시 나오기" : "숨기기";
            soundItem.Checked = Sfx.On;
        };
        menu.Closed += (_, _) =>
        {
            if (returnFocus != IntPtr.Zero) Native.SetForegroundWindow(returnFocus);
            returnFocus = IntPtr.Zero;
        };
    }

    void ToggleHud()
    {
        if (hud.Visible) hud.Hide();
        else hud.Show();
    }

    void SetHidden(bool hide) // 발표·화면 공유 때. 숨은 동안은 자리 비움으로 쳐서 벌이 없다
    {
        hiddenByUser = hide;
        Visible = !hide;
        foreach (var o in overlays) o.Visible = !hide;
        if (!hide) Native.Topmost(Handle);
        Dbg.Log("hidden", ("value", hide));
    }

    void OnSessionSwitch(object? s, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) locked = true;
        if (e.Reason == SessionSwitchReason.SessionUnlock) locked = false;
    }

    bool Afk() => dev?.ForceAfk ?? (locked || hiddenByUser || Native.IdleMinutes() >= b.K.AfkMin);

    void Frame()
    {
        long started = Stopwatch.GetTimestamp();
        double sec = clock.Elapsed.TotalSeconds, dt = Math.Min(.25, sec - lastSec);
        lastSec = sec;
        if (dt > 0) fps = fps * .95 + .05 / dt;

        b.Mouse = MousePosition;
        if (breakAuto && breakout is { } auto && auto.Balls.Where(x => !x.Stuck).MaxBy(x => x.Vy > 0 ? x.Y : x.Y - 2000) is { } low)
            b.Mouse = new Point((int)low.X, b.Mouse.Y); // 디버그 시험: 가장 낮게 내려오는 공을 받는다
        if (runner != null) RunFrame(dt);
        b.Update(dt, !InGame && Afk());
        if (breakout != null) BreakFrame(dt);
        if (worms != null) WormsFrame(dt);
        if (minigun != null) MinigunFrame(dt);
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
        bool on = b.ClickPadOn && !b.Behind && !hiddenByUser && game == null;
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
        else if (b.Now > nextTop && !hiddenByUser)
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
        if (InGame || hiddenByUser || b.P.Stage == "egg") return;
        var area = b.Area; // 볼록이가 있는 모니터 아래쪽
        var solids = new Dictionary<string, bool[,]>();
        (int, int, int, int) Measure(string name, int scale)
        {
            if (!solids.TryGetValue(name, out var solid)) solids[name] = solid = Props.Solid(sprites.Get(name, 0, 1));
            var (x0, y0, x1, y1) = Props.Trim(solid);
            return ((x1 - x0 + 1) * scale, (y1 - y0 + 1) * scale, x0 * scale, (y1 + 1) * scale);
        }
        runner = new Runner(area, area.Bottom - b.K.FloorGap, b.CellPx, new Random(), Measure, runLevel); // 저장된 레벨에서 시작
        runner.OnEvent = e =>
        {
            switch (e)
            {
                case "jump": case "jump2": Sfx.Play("empty"); break;
                case "seed": Sfx.Play("hit"); break;
                case "hit": Sfx.Play("pistol"); b.Emote("bang", .5); break;
                case "heart": b.Emote("heart", .8); break;
                case "level": b.Emote("star", 1.2); Sfx.Play("reload"); break;
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

    void EndRun(bool quit, bool record = true)
    {
        var r = runner!;
        runner = null;
        b.EndRun();
        Native.HideFromCapture(Handle, false);
        foreach (var o in overlays) { Native.HideFromCapture(o.Handle, false); o.Invalidate(); }
        Dbg.Log("run-quit", ("byKeys", quit), ("record", record));
        if (!record) return;
        runLast = r.Score;
        runBest = Math.Max(runBest, r.Score);
        runLevel = r.Level; // 다음 판은 오른 레벨에서
        runScores.Add(new RunRecord(DateTime.Now, r.Level, r.Score, r.Seeds));
        if (runScores.Count > 50) runScores.RemoveRange(0, runScores.Count - 50);
        Save();
    }

    bool InGame => game != null || runner != null || breakout != null || worms != null || minigun != null || icbm != null;

    // 게임판: 그 모니터 위에 1% 판을 깔아 클릭·키를 받는다. 다른 창은 안 눌린다
    GameLayer OpenLayer(Rectangle field, MouseEventHandler down, MouseEventHandler up, Action quit)
    {
        var layer = new GameLayer(field, b.K.GameLayerOpacity);
        layer.MouseDown += down;
        layer.MouseUp += up;
        layer.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) quit(); };
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
        if (InGame || hiddenByUser || b.P.Stage == "egg") return;
        if (b.P.Asleep) { b.Emote("zzz"); return; }
        if (b.P.Energy < 10) { b.Emote("no"); return; } // 고칠 기운이 없으면 안 쏜다
        var ic = icbm = new Icbm(Screen.AllScreens.Select(s => s.WorkingArea).ToList(), b.X, b.Y, b.Scale, new Random());
        ic.OnEvent = e =>
        {
            switch (e)
            {
                case "press": Sfx.Play("empty"); break;
                case "launch": Sfx.Play("shotgun"); break;
                case "boom": Sfx.Play("boom"); b.Emote("bang", 1); break;
                case "nowhere": b.Say(3, "떨어뜨릴 곳이 없어요"); break;
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
        if (InGame || hiddenByUser || b.P.Stage == "egg") return;
        gameFocus = Native.GetForegroundWindow();
        var wins = Native.ListWindows();
        var (field, rects) = Worms.Islands(Native.Foreground(), wins, b.Area, Screen.AllScreens.Select(s => s.WorkingArea).ToList(), b.CellPx, 8, topRoom: 0);
        if (rects.Count == 0)
        {
            b.Say(3.5, "부술 창이 없어요", "최대화 안 한 창을 하나 띄워 주세요");
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
        if (InGame || hiddenByUser || b.P.Stage == "egg") return;
        gameFocus = Native.GetForegroundWindow();
        var (field, wins) = PickIslands();
        if (wins.Count == 0)
        {
            b.Say(3.5, "섬이 될 창이 없어요", "최대화 안 한 창을 하나 띄워 주세요");
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
        if (InGame || hiddenByUser || b.P.Stage == "egg") return;
        gameFocus = Native.GetForegroundWindow();
        var screens = Screen.AllScreens.Select(s => s.WorkingArea).ToList();
        if (Breakout.Pick(Native.Foreground(), Native.ListWindows(), b.Area, screens, b.CellPx) is not { } pick)
        {
            b.Say(3.5, "벽돌로 만들 창이 없어요", "최대화 안 한 창을 하나 띄워 주세요"); // 창모드 창이 없다
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
        if (InGame || hiddenByUser || b.P.Stage == "egg") return;
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
        layer.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { Dbg.Log("press", ("result", "esc")); game?.Finish(); } };
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
        if (name != null) g.DrawImageUnscaled(sprites.Get(name, frame, b.Scale, flip), Left - v.X + 8, Top - v.Y);
        bmp.Save(path, ImageFormat.Png);
    }
}
