using System.Text.Json;

namespace Bolok;

// 눈길 마중 모듈 (docs/ice-run.md): 앱에 붙이기(판 자리·조작·볼록이), 그리기, 기록, 디버그 명령. 규칙은 Trail.cs, 손잡이는 TrailTune.cs,
// 그림은 TrailArt.cs·TrailView.cs, 자동 조종은 TrailBot.cs, 자가 검사·장면·속도 재기·봇 표는 TrailCheck.cs·TrailTable.cs
// 화면 안쪽으로 뻗은 눈길을 달려 시간 안에 모닥불 쉼터 다섯에 닿는다. ←→ 좌우, Ctrl 점프, Shift 질주, ↓ 천천히, Esc 끝 (방향키·↓·↑ 는 판이 앞 창일 때만, Ctrl·Shift 는 어느 창에서든)
// 판은 200×120칸 고정 (칸 = f 점, f = 볼록이 배율, 최대 4). 쓰던 창이 있으면 그 창 안 아래 가운데에, 없으면 볼록이 둘레에 얹고 볼록이는 판 위에 직접 그린다 (본체 창은 숨김)
sealed partial class TrailGame : IGameMode
{
    readonly IGameHost host;
    Trail? run;
    Session? ses;
    TrailView? view;
    Rectangle panel;
    IntPtr winH;                       // 판이 얹힌 창 (0 이면 바탕화면 판)
    int f, panelVer;
    int best, stage = 1, maxStage, clears, plays, bestLegs;
    internal bool auto, practice, desktopOnly, newBest, forceNewBest;
    int? forceStage, forceSeed;
    TrailBotState? bot;
    internal TrailSkill skill = TrailSkill.Good;
    bool ctrlWas = true, upWas = true;
    Watch still;
    readonly TrailShow show = new();
    string rowAway = "", rowHurt = "", rowDizzy = "", rowFreeze = "", rowHappy = "", rowSad = "";
    internal Func<IReadOnlyList<string>>? FriendList;   // 장면·검사: 마중 나올 친구 목록을 정한다
    internal Func<string, Sprites?>? FriendArt;         // 장면·검사: 친구 그림
    internal Func<Rectangle, Session>? FakeOpen;        // 장면·검사: 판을 열지 않고 Start 를 돌려 본다

    static readonly Dictionary<string, Sound> Sounds = new()
    {
        ["count"] = Sound.Tap, ["go"] = Sound.Pick, ["jump"] = Sound.Tap, ["hit"] = Sound.Hurt, ["smash"] = Sound.Hit, ["gate"] = Sound.Pick,
        ["item"] = Sound.Pick, ["win"] = Sound.Pick, ["over"] = Sound.Hurt,
        ["cry"] = Sound.Hit, ["bird"] = Sound.Hurt,   // 새가 멀리서 나타날 때 삐약 (지금 있는 소리), 맞으면 다친 소리
    };
    internal const double StepLen = 10.5;   // 달리기 두 자세가 바뀌는 걸음 (빠르기 ×1.5 에 맞춰 7 → 10.5)
    // 시작 안내 (줄 순서 규칙: 마우스 → 키 → Esc)
    static readonly string[] GuideKeys = { "note.trail_steer", "note.trail_jump", "note.trail_dash", "note.esc" };

    public TrailGame(IGameHost host) => this.host = host;
    public string Id => "trail";
    public string? MenuKey => "menu.trail";
    public bool Running => run != null;
    public string HudLine => plays > 0 ? $"아이스런 {stage}단계 (최고 {best})" : "";

    // ── 시작 ──
    public void Start()
    {
        if (Running) return;
        var b = host.Pet;
        f = Math.Min(Math.Max(b.Scale, 2), 4);
        var area = b.Area;
        while (f > 2 && (TrailView.PW * f > area.Width || TrailView.PH * f > area.Height)) f--;
        if (TrailView.PW * f > area.Width || TrailView.PH * f > area.Height) { b.Emote("shock", 1.5); return; }   // 쓸 자리 없음
        int w = TrailView.PW * f, h = TrailView.PH * f;
        winH = IntPtr.Zero;
        Rectangle p = default;
        if (!desktopOnly)
        {
            screens.Clear();
            foreach (var s in Screen.AllScreens) screens.Add(s.WorkingArea);   // 판을 여는 때 한 번 (창을 따라갈 때 모니터 고르기에도 쓴다)
            // 볼록이가 올라가 있는(볼록이 몸 가운데가 들어 있는) 맨 위 창. 판이 들어갈 만큼 크면 그 창 안 볼록이 가까이, 아니면 바탕화면 볼록이 둘레 (YELL 2026-11-24: 가까운 자리에서 열린다)
            var at = new Point((int)b.X, (int)(b.Y - b.CellPx / 2.0));
            foreach (var wn in Native.ListWindows())   // 위 창부터
            {
                if (wn.Topmost || !wn.R.Contains(at)) continue;
                var inside = Rectangle.Intersect(wn.R, MostOn(wn.R));
                if (inside.Width >= w + 24 && inside.Height >= h + 48)
                {
                    winH = wn.H;
                    p = PanelPlace.NearPet(inside, b.X, b.Y, w, h, f);
                    winOff = new Point(p.X - wn.R.X, p.Y - wn.R.Y);   // 창이 움직이면 창 안의 같은 자리로 따라간다
                }
                break;   // 볼록이 바로 밑 창만 본다 (그 아래 가려진 창은 쓰지 않는다)
            }
        }
        if (winH == IntPtr.Zero) p = PanelPlace.Around(area, b.X, b.Y, w, h, f);
        int seed = forceSeed ?? Random.Shared.Next(1, 1 << 30);
        (practice, int play) = (forceStage != null, forceStage ?? stage);
        (forceStage, forceSeed) = (null, null);
        var (rx, _) = PetArt.Embedded.Size(b.P.Stage);
        var t = new Trail(seed, play, rx);
        var s0 = FakeOpen?.Invoke(p) ?? Session.Open(host, p, () => Stop(true));
        Begin(t, p, s0);
        s0.Tick();
        ctrlWas = s0.Ctrl; upWas = s0.Up;   // 메뉴를 고를 때 눌려 있던 키로 바로 뛰지 않게
        b.StartPuppet();
        host.ShowPet(false);
        Dbg.Log("mode-start", ("id", Id), ("field", p.ToString()), ("where", winH == IntPtr.Zero ? "desktop" : "window"), ("scale", f), ("stage", play), ("seed", seed), ("practice", practice));
    }

    // 창 안 아래 가운데
    internal static Rectangle InWindow(Rectangle win, int w, int h, int k) => new(win.Left + (win.Width - w) / 2, win.Bottom - h - 6 * k, w, h);

    Point winOff;   // 판이 창 왼쪽 위에서 떨어진 만큼 (창을 따라갈 때 같은 자리로)

    // 판을 건다 (처음이거나 장면에서 직접)
    internal void Begin(Trail t, Rectangle p, Session s)
    {
        Close();
        (run, ses, panel) = (t, s, p);
        f = Math.Max(2, p.Width / TrailView.PW);
        (still, newBest, ctrlWas, upWas) = (default, false, true, true);
        panelVer = 0;
        string st = host.Pet.P.Stage;
        (rowAway, rowHurt, rowDizzy, rowFreeze, rowHappy, rowSad) = ($"{st}/0/away", $"{st}/0/hurt", $"{st}/0/dizzy", $"{st}/0/freeze", $"{st}/0/happy", $"{st}/0/sad");
        view = new TrailView(host.Sprites, f, Friends(t.Seed, t.Gates.Length));
        // 쓸 그림은 판을 열 때 다 만들어 둔다 (캐릭터 줄은 처음 쓸 때 그리는 데 몇 ms 걸린다)
        var stamps = Stamps.Of(host.Sprites);
        foreach (var row in new[] { rowAway, rowHurt, rowDizzy, rowFreeze, rowHappy, rowSad }) if (stamps.Has(row)) stamps.Row(row, f);
        foreach (int k in new[] { f, GameHud.Small(f), f + 1 }) { stamps.Digits(k); stamps.Get("game/crown", 0, k); stamps.Get("game/spark", 0, k); }
        foreach (var name in new[] { "emo/bang", "emo/heart" }) if (stamps.Has(name)) { stamps.Get(name, 0, f); stamps.Get(name, 0, GameHud.Small(f)); }
        guide = GuideKeys.Select(k => L.T(k)).ToArray();
        (guideOn, guidePic) = (null, null);
        bot = auto ? new TrailBotState(t.Seed + 99) : null;
        t.OnEvent = e =>
        {
            Snd.Play(Sounds, e);
            if (e is "hit" or "gate" or "item" or "over" or "win" or "smash" or "bird")
                Dbg.Log("trail", ("event", e), ("kind", e == "hit" ? t.LastHit.ToString() : e == "bird" ? "Bird" : ""), ("leg", t.Leg), ("sec", (int)t.Time), ("score", t.Score));
        };
        Pose(t);
        Track(t);
    }

    void Close()
    {
        view?.Dispose();
        view = null;
        guidePic?.Dispose();
        (guidePic, guideOn) = (null, null);
    }

    // ── 시작 안내 말풍선: 처음 4초. 안 바뀌니 한 번만 그려 둔다 (프레임마다 글자를 재면 메모리를 잡는다) ──
    string[] guide = Array.Empty<string>();
    Overlay? guideOn;
    Rectangle guideRect;
    Bitmap? guidePic;
    bool GuideOn(Trail r) => r.Clock < Session.GuideSec && guide.Length > 0;

    Bitmap GuidePic(Overlay o, Trail r)
    {
        if (guidePic != null && ReferenceEquals(guideOn, o)) return guidePic;
        guidePic?.Dispose();
        guideOn = o;
        double x = GuideX(r), feet = panel.Y + TrailView.PetRow * f;
        guideRect = o.Client(o.NoteRect(x, feet, panel, guide));
        guidePic = new Bitmap(guideRect.Width, guideRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var gg = Graphics.FromImage(guidePic);
        gg.TranslateTransform(-guideRect.X, -guideRect.Y);
        o.DrawNoteAt(gg, x, feet, panel, guide);
        return guidePic;
    }

    // 쉼터마다 마중 나올 친구: 떠난 친구를 씨로 섞어 앞에서부터 (모자라면 눈사람). 같은 씨면 같은 친구
    (string, Sprites)?[] Friends(int seed, int gates)
    {
        var names = (FriendList?.Invoke() ?? host.Friends).Where(n => !string.IsNullOrEmpty(n)).GroupBy(PetArt.Base).Select(g => g.First())
            .OrderBy(n => (StableHash(PetArt.Base(n)) ^ seed * 2654435761L) & 0xffffff).ToList();
        var res = new (string, Sprites)?[gates];
        for (int i = 0; i < gates && i < names.Count; i++)
            if ((FriendArt?.Invoke(names[i]) ?? host.SpritesOf(names[i])) is { } sp && sp.Has("adult_sweet/0/hop")) res[i] = (names[i], sp);
        return res;
    }

    static long StableHash(string s) { long h = 17; foreach (char c in s) h = h * 31 + c; return h; }

    // ── 한 프레임 ──
    public void Frame(double dt)
    {
        var (r, s) = (run!, ses!);
        s.Tick();
        if (r.State == Trail.Phase.Run || r.State == Trail.Phase.Count)
        {
            if (auto) TrailBot.Play(r, skill, bot!);
            else
            {
                int ax = s.AxisX;
                bool slow = s.AxisY > 0;
                if (!s.Active && r.State == Trail.Phase.Run)   // 판이 앞 창이 아니다: 길 가운데로 천천히 돌아오고 빠르기를 늦춘다 (조종이 끊겨도 시간을 다 잃지 않게)
                    (ax, slow) = (Math.Abs(r.X) < .08 ? 0 : r.X > 0 ? -1 : 1, true);
                r.Steer(ax);
                bool jump = s.Ctrl && !ctrlWas || s.Up && !upWas;   // 누르는 순간만
                (ctrlWas, upWas) = (s.Ctrl, s.Up);
                if (jump) r.Jump();
                r.Hold(s.Shift, slow);
            }
        }
        r.Update(dt);
        FollowWindow();
        Pose(r);
        Track(r);
        host.Pet.X = panel.X + panel.Width / 2.0;   // 본체는 숨었지만 끝나면 여기서 나타난다
        host.Pet.Y = Math.Min(panel.Bottom - 2 * f, host.Pet.Area.Bottom);
        if (r.Done && r.EndedFor > 2.5 || r.Done && r.EndedFor > .6 && (s.LeftHit || s.RightHit)) Stop(false);
    }

    readonly List<Rectangle> screens = new();
    Rectangle MostOn(Rectangle r) => PanelPlace.MostOn(screens, host.Pet.Area, r);   // 계산은 Kit/PanelPlace

    // 창이 움직이면 판이 따라가고, 닫히거나 작아지면 그 자리에 두고 바탕화면 판처럼 (놓친 판을 갑자기 옮기지 않는다)
    void FollowWindow()
    {
        if (winH == IntPtr.Zero || ses?.Layer is not { IsDisposed: false } layer) return;
        var st = Native.Probe(winH);
        if (!st.Alive || st.Minimized || st.R.Width < panel.Width + 24 || st.R.Height < panel.Height + 48) { winH = IntPtr.Zero; Dbg.Log("trail-window-lost", ("reason", !st.Alive ? "closed" : st.Minimized ? "minimized" : "small")); return; }
        // 창이 가장 많이 걸친 모니터 안에서만 (펫이 있던 모니터와 다를 수 있다). 잘린 창이 판보다 작으면 따라가기를 그만둔다 (판이 화면 밖으로 나가지 않게)
        var inter = Rectangle.Intersect(st.R, MostOn(st.R));
        if (inter.Width < panel.Width + 24 || inter.Height < panel.Height + 48) { winH = IntPtr.Zero; Dbg.Log("trail-window-lost", ("reason", "offscreen")); return; }
        var want = new Rectangle(st.R.X + winOff.X, st.R.Y + winOff.Y, panel.Width, panel.Height);
        var now = PanelPlace.NearPet(inter, want.X + panel.Width / 2.0, want.Bottom - 8 * f, panel.Width, panel.Height, f);   // 창 안 같은 자리 (창이 잘리면 안쪽으로)
        if (now == panel) return;
        panel = now;
        guidePic?.Dispose(); guidePic = null;
        layer.Bounds = now;
        panelVer++;
    }

    void Pose(Trail t)
    {
        var sh = show;
        sh.Clock = t.Clock;
        sh.Count = t.State == Trail.Phase.Count ? (int)Math.Ceiling(t.CountLeft / t.T.CountSec) : 0;
        sh.Result = t.Done;
        sh.Blink = t.State == Trail.Phase.Run && t.Safe && !t.Halted && t.StarLeft <= 0 && ((int)(t.RunClock * 10) & 1) == 0;
        sh.Bang = t.State == Trail.Phase.Run && t.Clock - t.HurtAt < .6 && t.LastHit != TrailKind.Hole;
        bool flinch = t.State == Trail.Phase.Run && t.Clock - t.BirdAt < .3 && t.Clock - t.HurtAt >= .35 && !t.Halted;   // 새에 맞아 움찔 (멈추지는 않는다)
        (sh.PetRow, sh.PetFrame) = t.State switch
        {
            Trail.Phase.Win => (rowHappy, (int)(t.Clock * 4) & 1),
            Trail.Phase.Over => (rowSad, 0),
            Trail.Phase.Count => (rowAway, 0),
            _ => t.Clock - t.HurtAt < .35 ? (rowHurt, t.Clock - t.HurtAt < .18 ? 0 : 1)
               : t.Halted ? t.LastHit == TrailKind.Hole ? (rowFreeze, (int)(t.Clock * 4) % 3) : (rowDizzy, (int)(t.Clock * 6) & 3)
               : t.Clock - t.GateAt < .6 ? (rowHappy, (int)(t.Clock * 6) & 1)
               : flinch ? (rowHurt, t.Clock - t.BirdAt < .15 ? 0 : 1)
               : t.Airborne ? (rowAway, 1) : (rowAway, (int)(t.Pos / StepLen) & 1),
        };
        if (t.Done)
        {
            sh.Won = t.State == Trail.Phase.Win;
            (sh.Score, sh.Best, sh.Fires, sh.Secs, sh.Cleared) = (t.Score, Math.Max(best, t.Score), t.Leg, (int)t.RunClock, sh.Won);
            sh.NewBest = forceNewBest || !practice && !auto && t.Score > best && t.Score > 0;
        }
    }

    // ── 끝 ──
    public void Stop(bool quit)
    {
        if (run is not { } r) return;
        run = null;
        if (!auto && !practice && (r.Done || !quit)) Keep(r);
        var won = r.State == Trail.Phase.Win;
        var log = new (string, object?)[] { ("score", r.Score), ("legs", r.Leg), ("stage", r.Stage), ("sec", (int)r.RunClock), ("falls", r.Hits), ("birds", r.BirdHits), ("lost", r.BirdLost), ("dash", (int)(100 * r.DashSec / Math.Max(1, r.RunSec))), ("esc", quit), ("practice", practice || auto) };
        ses?.Close(host.Pet, quit, won, "trail", log);
        ses = null;
        Close();
        host.ShowPet(true);
        Dbg.Log("trail-end", log);
        Dbg.Log("mode-end", ("id", Id));
        auto = practice = false;
        host.Save();
    }

    // 기록: 완주하면 다음 판은 한 단계 위, 시간 초과는 같은 단계. 연습 판·자동 판은 안 남긴다
    void Keep(Trail r)
    {
        plays++;
        best = Math.Max(best, r.Score);
        bestLegs = Math.Max(bestLegs, r.Leg);
        if (r.State != Trail.Phase.Win) return;
        clears++;
        maxStage = Math.Max(maxStage, r.Stage);
        if (r.Stage == stage) stage = Math.Min(99, stage + 1);
    }

    // ── 그리기: 판 하나 (모니터에 걸친 만큼). 멈춘 동안(3·2·1, 결과)은 바뀔 때만 ──
    bool Here(Overlay o) => o.Bounds.IntersectsWith(panel);

    // 프레임마다 한 번 (그림 창이 모니터마다 있어도 한 번만 센다): 멈춘 동안 바뀐 것
    void Track(Trail r) => still.Set(r.State == Trail.Phase.Count ? 1000 + show.Count + panelVer * 4 : r.Done ? 2000 + (r.EndedFor > .2 ? 1 : 0) + (show.NewBest ? 2 : 0) + panelVer * 4 : -1 - (int)(r.Clock * 100));

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (run is not { } r || !Here(o)) return;
        if (r.State == Trail.Phase.Run || still.Changed) rs.Add(o.Client(panel));
    }

    double GuideX(Trail r) => panel.X + (view?.PetX(r) ?? 100) * f;

    public void Draw(Overlay o, Graphics g)
    {
        if (run is not { } r || view is not { } v || !Here(o)) return;
        v.Draw(o, g, panel, r, show);
        if (GuideOn(r)) { var pic = GuidePic(o, r); g.DrawImageUnscaled(pic, guideRect.X, guideRect.Y); }
    }

    // ── 디버그 ──
    //   trail / trail stop (바로 끝, 기록 안 남김) / trail auto (봇이 달린다, 기록 안 남김) / trail stage N (다음 판 N단계, 연습 판) / trail seed N (다음 판 씨)
    //   trail gate (다음 쉼터 30 걸음 앞으로) / trail time N (남은 시간) / trail put <종류|bird> (2초 앞에 하나, 새는 3초 앞에서 만난다) / trail desktop (창이 있어도 바탕화면 판)
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != Id) return false;
        string sub = a.Length > 1 ? a[1] : "";
        int n = a.Length > 2 && int.TryParse(a[2], out int v) ? v : -1;
        switch (sub)
        {
            case "stop": if (run != null) { auto = true; Stop(true); } break;
            case "stage": forceStage = Math.Max(1, n); break;
            case "seed": forceSeed = Math.Max(1, n); break;
            case "desktop": desktopOnly = !desktopOnly; break;
            case "gate": run?.SkipToGate(30); break;
            case "time": if (run != null && n >= 0) run.Time = n; break;
            case "put":
                if (run != null && a.Length > 2 && a[2].Equals("bird", StringComparison.OrdinalIgnoreCase)) run.PutBird();
                else if (run != null && a.Length > 2 && Enum.TryParse<TrailKind>(a[2], true, out var k)) run.Put(k);
                break;
            default:
                if (run == null && !host.Busy) { auto = sub == "auto"; Start(); if (run == null) auto = false; }
                else if (run != null && sub == "auto") { auto = true; bot ??= new TrailBotState(run.Seed + 99); }
                break;
        }
        return true;
    }

    public IEnumerable<GameStat> Stats()
    {
        yield return new GameStat(L.T("game.trail"), plays > 0 ? L.T("game.trail_val", stage, best.ToString("N0", L.Culture)) : L.T("game.trail_none"), plays > 0);
    }

    // 기록: { Best, Stage, MaxStage, Clears, Plays, BestLegs }
    sealed record Record(int Best, int Stage, int MaxStage, int Clears, int Plays, int BestLegs);

    public string? SaveState() => plays > 0 || stage > 1 ? JsonSerializer.Serialize(new Record(best, stage, maxStage, clears, plays, bestLegs)) : null;

    public void LoadState(string? json)
    {
        if (json == null) return;
        try
        {
            if (JsonSerializer.Deserialize<Record>(json) is not { } r) return;
            (best, stage, maxStage, clears, plays, bestLegs) = (r.Best, Math.Clamp(r.Stage, 1, 99), r.MaxStage, r.Clears, r.Plays, r.BestLegs);
        }
        catch (JsonException) { /* 깨진 기록은 버린다 */ }
    }

    // 자가 검사용: 기록을 직접 만져 본다
    internal (int Best, int Stage, int MaxStage, int Clears, int Plays, int BestLegs) State => (best, stage, maxStage, clears, plays, bestLegs);
    internal void KeepForCheck(Trail r, bool practiceRun = false, bool autoRun = false) { (practice, auto) = (practiceRun, autoRun); if (!auto && !practice) Keep(r); (practice, auto) = (false, false); }

    // 게임 한 줄(GameEntry)이 부르는 것: 자가 검사·장면·속도 재기 (TrailCheck.cs)
    public static string Check() => TrailCheck.Check();
    public static void Scene(string path, int k) => TrailCheck.Scene(path, k);
    public static string BenchRun() => TrailCheck.BenchRun();
}
