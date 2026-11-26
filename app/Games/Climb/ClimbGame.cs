using System.Text.Json;

namespace Bolok;

// 클라이밍 모듈 (docs/climb.md): 앱에 붙이기(판 자리·조작·볼록이), 그리기, 기록, 디버그 명령. 규칙은 Climb.cs, 손잡이는 ClimbTune.cs,
// 그림은 ClimbArt.cs·ClimbView.cs, 자동 조종은 ClimbBot.cs, 판 자리는 ClimbPlace.cs, 자가 검사·장면·속도 재기·봇 표는 ClimbCheck.cs·ClimbTable.cs
// 볼록이 발밑에서 콩나무가 솟는다. 다음 잎이 있는 쪽 방향키(← →)를 눌러 오른다 (방향키는 판이 앞 창일 때만). 틀린 쪽을 누르거나 막대(잎 조각)가 다 되면 떨어진다. Esc 보스 키
// 모양은 둘: A2 콩나무 투명(150×176칸, 넓은 창·바탕화면), A4 창 테두리 덩굴(96×176칸, 가끔·창 테두리 가까이일 때). 판 안 볼록이는 판 위에 직접 그린다 (본체 창은 숨김)
// 그림 테마는 다섯: 덩굴 · 책상 위 문구 · 과자 탑 · 풍선 · 장난감 블록. A4 는 늘 덩굴, A2 는 씨로 다섯 가운데 무작위 (규칙은 모두 같다, ClimbSkin.cs)
sealed partial class ClimbGame : IGameMode
{
    readonly IGameHost host;
    Climb? run;
    Session? ses;
    ClimbView? view;
    Rectangle panel;
    IntPtr winH;                       // 판이 얹힌 창 (0 이면 바탕화면 판)
    int f, panelVer, edgeSide;
    ClimbLook look;
    ClimbTheme theme;                  // 이번 판의 그림 테마 (A4 는 늘 덩굴)
    Point winOff;                      // 판이 창 왼쪽 위에서 떨어진 만큼 (창을 따라갈 때 같은 자리로)
    int best, plays, last, berries, friendsMax;
    internal bool auto, practice, desktopOnly, newBest;
    int? forceSeed, forceStart, forceBest;
    internal int lookMode;             // 0 보통(세 판에 한 번쯤 A4) · 1 A4 로(가능할 때) · 2 A2 로
    internal ClimbTheme? themeForced;  // 디버그 climb theme: 정한 테마로 (null 이면 씨로 고른다)
    internal ClimbSkill skill = ClimbSkill.Good;
    ClimbBotState? bot;
    bool resumed = true;
    double resumeLeft;
    readonly ClimbShow show = new();
    double visual;                     // 그림 시계 (멈춘 동안에도 간다)
    Watch hud;
    double friendSince;
    int friendCount;
    readonly Dictionary<string, ClimbFriend?> friendCache = new();
    internal Func<IReadOnlyList<string>>? FriendList;   // 장면·검사: 손 흔들러 올 친구 목록을 정한다
    internal Func<string, Sprites?>? FriendArt;         // 장면·검사: 친구 그림
    internal Func<Rectangle, Session>? FakeOpen;        // 장면·검사: 판을 열지 않고 Start 를 돌려 본다
    internal Func<List<Win>>? FakeWindows;              // 검사: 창 목록
    internal Func<IntPtr, WinState>? FakeProbe;         // 검사: 창 상태 (움직이거나 닫히는 것)
    internal Rectangle[]? FakeScreens;

    static readonly Dictionary<string, Sound> Sounds = new()
    {
        ["hop"] = Sound.Tap, ["berry"] = Sound.Pick, ["puff"] = Sound.Pick, ["mile"] = Sound.Pick, ["zone"] = Sound.Pick, ["curl"] = Sound.Pick, ["best"] = Sound.Pick,
        ["wrong"] = Sound.Hurt, ["fall"] = Sound.Hurt, ["save"] = Sound.Tap,
    };
    // 시작 안내 (줄 순서 규칙: 마우스 → 키 → Esc)
    static readonly string[] GuideKeys = { "note.climb_hop", "note.esc" };

    public ClimbGame(IGameHost host) => this.host = host;
    public string Id => "climb";
    public string? MenuKey => "menu.climb";
    public bool Running => run != null;
    public string HudLine => plays > 0 ? $"클라이밍 최고 {best}잎" : "";

    // ── 시작 ──
    public void Start()
    {
        if (Running) return;
        var b = host.Pet;
        f = Math.Min(Math.Max(b.Scale, 2), 4);
        var area = b.Area;
        int ph = ClimbView.PH, pw = ClimbView.WideA2;
        while (f > 2 && (pw * f > area.Width || ph * f > area.Height)) f--;
        if (pw * f > area.Width || ph * f > area.Height) { b.Emote("shock", 1.5); return; }   // 쓸 자리 없음
        int seed = forceSeed ?? Random.Shared.Next(1, 1 << 30);
        var screens = FakeScreens ?? Screen.AllScreens.Select(s => s.WorkingArea).ToArray();   // 판을 여는 때 한 번 (창을 따라갈 때 모니터 고르기에도 쓴다)
        screensNow = screens;
        List<Win> wins = desktopOnly ? new() : FakeWindows?.Invoke() ?? Native.ListWindows();
        bool edge = lookMode == 1 || lookMode == 0 && ClimbPlace.Coin(seed);
        if (lookMode == 2 || desktopOnly || themeForced is { } tf && tf != ClimbTheme.Vine && lookMode == 0) edge = false;   // 덩굴 말고 정한 테마는 A4 가 없다
        var plan = ClimbPlace.Plan(wins, b.X, b.Y, b.CellPx, f, screens, area, edge);
        (look, panel, winH, edgeSide, winOff) = (plan.Look, plan.Panel, plan.Win, plan.Side, plan.Off);
        (practice, int start) = (forceStart != null, forceStart ?? 0);
        (forceStart, forceSeed) = (null, null);
        var t = new Climb(seed, null, start) { Best = forceBest ?? best };
        var s0 = FakeOpen?.Invoke(panel) ?? Session.Open(host, panel, () => Stop(true));
        var th = ClimbPlace.Theme(seed, plan.Look, themeForced);
        Begin(t, plan.Look, panel, s0, host.Pet.P.Stage, th);
        s0.Tick();
        b.StartPuppet();
        host.ShowPet(false);
        Dbg.Log("mode-start", ("id", Id), ("field", panel.ToString()), ("where", winH == IntPtr.Zero ? "desktop" : "window"), ("look", look.ToString()), ("theme", theme.ToString()), ("why", plan.Why), ("scale", f), ("seed", seed), ("practice", practice));
    }

    Rectangle[] screensNow = Array.Empty<Rectangle>();

    // 판을 건다 (처음이거나 장면에서 직접)
    internal void Begin(Climb t, ClimbLook lk, Rectangle p, Session s, string stage, ClimbTheme th = ClimbTheme.Vine)
    {
        Close();
        t.Guard = true;   // 먼 잎을 읽어 고리가 덮이는 일을 막는다 (Climb.At)
        (run, ses, panel, look) = (t, s, p, lk);
        theme = lk == ClimbLook.A4 ? ClimbTheme.Vine : th;
        f = Math.Max(2, p.Width / ClimbPlace.Width(lk));
        (hud, newBest, resumed, resumeLeft, visual, panelVer, friendCount) = (default, false, true, 0, 0, 0, 0);
        show.FriendAt = -1; show.Friend = null; show.Result = false; show.NewBest = false; show.Paused = false;
        show.BestLeaf = t.Best > 0 ? t.Best : -1;
        view = new ClimbView(host.Sprites, stage, lk, f, theme);
        // 쓸 그림은 판을 열 때 다 만들어 둔다
        var stamps = Stamps.Of(host.Sprites);
        int sm = GameHud.Small(f);
        stamps.Digits(sm); stamps.Digits(f + 1); stamps.Get("game/crown", 0, sm); stamps.Get("game/crown", 0, f + 1); stamps.Get("game/spark", 0, sm); stamps.Get("game/spark", 1, sm);
        foreach (var name in new[] { "emo/bang", "emo/heart" }) if (stamps.Has(name)) stamps.Get(name, 0, sm);
        guide = GuideKeys.Select(k => L.T(k)).ToArray();
        (guideOn, guidePic) = (null, null);
        bot = auto ? new ClimbBotState(t.Seed + 99) : null;
        t.OnEvent = e =>
        {
            Snd.Play(Sounds, e);
            if (e == "zone") { friendCount++; friendSince = t.Clock; show.FriendAt = t.N; show.Friend = FriendFor(friendCount); show.FriendSide = 0; }   // 자리는 볼록이가 내려선 뒤 정한다 (ClimbView.Update)
            if (Dbg.On && e is "mile" or "berry" or "puff" or "save" or "curl" or "zone" or "best" or "wrong" or "fall" or "over")   // 로그가 꺼져 있으면 인자도 안 만든다
                Dbg.Log("climb", ("event", e), ("leaf", t.N), ("bar", Math.Round(t.Bar, 2)), ("sec", Math.Round(t.RunClock, 1)));
        };
        friendNames = (FriendList?.Invoke() ?? host.Friends).Where(x => !string.IsNullOrEmpty(x)).GroupBy(PetArt.Base).Select(g => g.First())
            .OrderBy(x => (StableHash(PetArt.Base(x)) ^ t.Seed * 2654435761L) & 0xffffff).ToList();
        for (int n = 1; n <= Math.Min(friendNames.Count, 6); n++) FriendFor(n);   // 손 흔들러 올 친구 그림은 판을 열 때 미리 (100잎에서 그림을 만드느라 멈칫하지 않게)
        Pose(t);
        view.Update(t, show, 0);
        Track(t);
    }

    void Close()
    {
        view?.Dispose();
        view = null;
        guidePic?.Dispose();
        (guidePic, guideOn) = (null, null);
    }

    // ── 시작 안내 말풍선: 처음 4초. 안 바뀌니 한 번만 그려 둔다 ──
    string[] guide = Array.Empty<string>();
    Overlay? guideOn;
    Rectangle guideRect, guideScreen;   // 안내 말풍선 자리 (창 좌표·화면 좌표). 판 밖으로 나올 수 있어 따로 칠한다
    int guideGoneFrame = -1;
    Bitmap? guidePic;
    bool GuideOn(Climb r) => visual < Session.GuideSec && r.Steps == 0 && guide.Length > 0;

    Bitmap GuidePic(Overlay o)
    {
        if (guidePic != null && ReferenceEquals(guideOn, o)) return guidePic;
        guidePic?.Dispose();
        guideOn = o;
        var foot = view!.PetFoot(panel, run!);
        guideRect = o.Client(GuideBox(o));
        guidePic = new Bitmap(guideRect.Width, guideRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var gg = Graphics.FromImage(guidePic);
        gg.TranslateTransform(-guideRect.X, -guideRect.Y);
        o.DrawNoteAt(gg, foot.X, foot.Y, panel, guide);
        return guidePic;
    }

    bool guideBoxOk;
    Rectangle GuideBox(Overlay o)
    {
        if (guideBoxOk) return guideScreen;
        var foot = view!.PetFoot(panel, run!);
        guideBoxOk = true;
        return guideScreen = o.NoteRect(foot.X, foot.Y, panel, guide);
    }

    // 100잎마다 손 흔들러 오는 친구: 떠난 친구를 씨로 섞어 차례로 (없으면 작은 새). 같은 씨면 같은 친구
    List<string> friendNames = new();
    ClimbFriend? FriendFor(int n)
    {
        if (friendNames.Count == 0) return null;
        string name = friendNames[(n - 1) % friendNames.Count];
        if (friendCache.TryGetValue(name, out var hit)) return hit;
        var sp = FriendArt?.Invoke(name) ?? host.SpritesOf(name);
        return friendCache[name] = sp != null && sp.Has("adult_sweet/0/hop") ? new ClimbFriend(name, sp) : null;
    }

    static long StableHash(string s) { long h = 17; foreach (char c in s) h = h * 31 + c; return h; }

    // ── 한 프레임 ──
    public void Frame(double dt)
    {
        var (r, s) = (run!, ses!);
        s.Tick();
        visual += dt;
        frameNo++;
        bool paused = !auto && !s.Active && r.State == Climb.Phase.Run;
        if (!r.Done)
        {
            if (auto) { ClimbBot.Play(r, skill, bot!); }
            else if (s.Active)
            {
                if (!resumed) { resumed = true; r.GraceLeft = Math.Max(r.GraceLeft, .5); }   // 다시 판을 누르면 0.5초 뒤부터 막대가 줄어든다
                bool l = s.ArrowLeftHit, rr = s.ArrowRightHit;
                if (l != rr) r.Press(l ? -1 : 1);                                           // 같은 프레임에 두 키가 눌리면 둘 다 버린다 (꼼수 막기)
            }
            else if (r.State == Climb.Phase.Run) resumed = false;
        }
        if (!paused) r.Update(dt);
        FollowWindow();
        if (guidePic != null && !GuideOn(r)) { guidePic.Dispose(); (guidePic, guideOn, guideGoneFrame) = (null, null, frameNo); }
        Pose(r);
        view!.Update(r, show, dt);
        Track(r);
        var foot = view.PetFoot(panel, r);
        host.Pet.X = Math.Clamp(foot.X, host.Pet.Area.Left + host.Pet.CellPx / 2.0, host.Pet.Area.Right - host.Pet.CellPx / 2.0);   // 본체는 숨었지만 끝나면 여기서 나타난다
        host.Pet.Y = Math.Min(foot.Y, host.Pet.Area.Bottom);
        if (r.State == Climb.Phase.Over && (r.EndedFor > r.T.ResultSec || r.EndedFor > r.T.ResultMin && (s.LeftHit || s.RightHit))) Stop(false);
    }

    // 창이 움직이면 판이 따라가고, 닫히거나 작아지면 그 자리에 두고 바탕화면 판처럼 (놓친 판을 갑자기 옮기지 않는다)
    void FollowWindow()
    {
        if (winH == IntPtr.Zero || ses == null) return;
        var st = FakeProbe?.Invoke(winH) ?? Native.Probe(winH);
        string? lost = !st.Alive ? "closed" : st.Minimized ? "minimized" : null;
        Rectangle? now = null;
        if (lost == null)
        {
            var mon = PanelPlace.MostOn(screensNow, host.Pet.Area, st.R);   // 창이 가장 많이 걸친 모니터 안에서만
            if (look == ClimbLook.A4) now = ClimbPlace.EdgePanel(st.R, edgeSide, st.R.Y + winOff.Y, panel.Width, panel.Height, mon);
            else
            {
                var inter = Rectangle.Intersect(st.R, mon);
                if (inter.Width >= panel.Width + 24 && inter.Height >= panel.Height + 48) now = PanelPlace.NearPet(inter, st.R.X + winOff.X + panel.Width / 2.0, st.R.Y + winOff.Y + panel.Height - 8 * f, panel.Width, panel.Height, f);   // 창 안 같은 자리 (창이 잘리면 안쪽으로)
            }
            if (now == null) lost = "small";
        }
        if (lost != null) { winH = IntPtr.Zero; Dbg.Log("climb-window-lost", ("reason", lost), ("look", look.ToString())); return; }
        if (now is not { } p || p == panel) return;
        (oldPanel, movedFrame) = (panel, frameNo);
        panel = p;
        view?.ForceCompose();
        guidePic?.Dispose(); guidePic = null; guideBoxOk = false;
        if (ses?.Layer is { IsDisposed: false } l2) l2.Bounds = p;
        panelVer++;
    }

    void Pose(Climb t)
    {
        var sh = show;
        sh.Clock = visual;
        sh.Paused = !auto && ses is { Active: false } && t.State == Climb.Phase.Run;
        sh.Result = t.State == Climb.Phase.Over;
        sh.Guide = GuideOn(t);
        if (sh.FriendAt >= 0 && t.Clock - friendSince > 5) sh.FriendAt = -1;
        sh.FriendAge = t.Clock - friendSince;
        if (t.State == Climb.Phase.Over)
        {
            sh.NewBest = newBest = forceBestShown(t);
            sh.Best = Math.Max(forceBest ?? best, t.N);
        }
    }

    bool forceBestShown(Climb t) => !practice && !auto && t.N > (forceBest ?? best) && t.N > 0;

    // ── 끝 ──
    public void Stop(bool quit)
    {
        if (run is not { } r) return;
        run = null;
        if (!auto && !practice && r.Done) Keep(r);
        bool won = !auto && (newBest || r.N >= 50);
        var log = new (string, object?)[] { ("score", r.N), ("steps", r.Steps), ("sec", Math.Round(r.RunClock, 1)), ("cause", r.CauseName), ("pace", Math.Round(r.Steps / Math.Max(1, r.RunClock), 2)), ("berries", r.Berries), ("saves", r.Saves), ("friends", r.Friends), ("look", look.ToString()), ("theme", theme.ToString()), ("esc", quit), ("practice", practice || auto) };
        ses?.Close(host.Pet, quit, won, "climb", log);
        ses = null;
        Close();
        host.ShowPet(true);
        Dbg.Log("climb-end", log);
        Dbg.Log("mode-end", ("id", Id));
        auto = practice = false;
        host.Save();
    }

    // 기록: 끝난 판만. 연습 판·자동 판은 안 남긴다
    void Keep(Climb r)
    {
        plays++;
        best = Math.Max(best, r.N);
        last = r.N;
        berries += r.Berries;
        friendsMax = Math.Max(friendsMax, r.Friends);
    }

    // ── 그리기: 판 하나 (모니터에 걸친 만큼). 멈춘 동안은 바뀐 것만 ──
    bool Here(Overlay o) => o.Bounds.IntersectsWith(panel);

    // 프레임마다 한 번 (그림 창이 모니터마다 있어도 한 번만 센다): 멈춘 동안 바뀐 표시
    void Track(Climb r) => hud.Set((int)(r.Bar * 360) * 4 + (r.Puff ? 2 : 0) + (r.State == Climb.Phase.Run ? 1 : 0) + panelVer * 100000 + r.N * 7);

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (run is not { } r || view is not { } v || !Here(o)) return;
        if (movedFrame == frameNo) rs.Add(o.Client(oldPanel));   // 판이 창을 따라 옮겨 갔다: 옛 자리도 지운다
        if (GuideOn(r)) rs.Add(o.Client(GuideBox(o)));
        if (guideGoneFrame == frameNo) rs.Add(o.Client(guideScreen));   // 안내가 사라지는 프레임: 마지막으로 한 번 더 지운다
        if (v.WorldChanged || hud.Fresh) rs.Add(o.Client(panel));
        else if (hud.Changed) { rs.Add(o.Client(v.ScoreRect(panel, Math.Max(1, GameHud.Digits(r.N))))); rs.Add(o.Client(v.PipsRect(panel))); }
    }

    int frameNo, movedFrame = -1;
    Rectangle oldPanel;

    public void Draw(Overlay o, Graphics g)
    {
        if (run is not { } r || view is not { } v || !Here(o)) return;
        v.Draw(o, g, panel, r, show);
        if (GuideOn(r)) { var pic = GuidePic(o); g.DrawImageUnscaled(pic, guideRect.X, guideRect.Y); }
    }

    // ── 디버그 ──
    //   climb / climb stop (바로 끝, 기록 안 남김) / climb auto [easy|good] (봇이 오른다, 기록 안 남김) / climb seed N (다음 판 씨) / climb at N (다음 판을 N잎에서, 연습 판)
    //   climb bar N (막대를 N%로) / climb put <berry|puff|wilt|curl|cloud> (세 칸 앞 잎을 그 종류로) / climb best N (지난 최고를 N 으로 본다, 저장 안 함)
    //   climb desktop (창이 있어도 바탕화면 판) / climb look <a2|a4|auto> (다음 판 모양: a4 는 창 테두리가 맞을 때만, 아니면 a2)
    //   climb theme <vine|desk|sweet|balloon|block|auto> (다음 판 그림: 덩굴 말고 정하면 A4 는 안 쓴다. auto 는 씨로 무작위)
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != Id) return false;
        string sub = a.Length > 1 ? a[1] : "";
        int n = a.Length > 2 && int.TryParse(a[2], out int v) ? v : -1;
        switch (sub)
        {
            case "stop": if (run != null) { auto = true; Stop(true); } break;
            case "seed": forceSeed = Math.Max(1, n); break;
            case "at": forceStart = Math.Max(0, n); break;
            case "best": forceBest = Math.Max(0, n); break;
            case "desktop": desktopOnly = !desktopOnly; break;
            case "look": lookMode = a.Length > 2 ? a[2] switch { "a4" => 1, "a2" => 2, _ => 0 } : 0; break;
            case "theme": themeForced = a.Length > 2 ? a[2] switch { "vine" => ClimbTheme.Vine, "desk" => ClimbTheme.Desk, "sweet" => ClimbTheme.Sweet, "balloon" => ClimbTheme.Balloon, "block" => ClimbTheme.Block, _ => null } : null; break;
            case "bar": if (run != null && n >= 0) run.Bar = Math.Min(1, n / 100.0); break;
            case "put":
                if (run != null && a.Length > 2)
                {
                    if (a[2].Equals("cloud", StringComparison.OrdinalIgnoreCase)) { for (int i = 0; i < 10; i++) run.At(run.N + 3 + i).Fog = true; }
                    else if (Enum.TryParse<ClimbKind>(a[2] switch { "puff" => "Puff", "wilt" => "Wilt", "curl" => "Curl", "berry" => "Berry", "mile" => "Flower", _ => a[2] }, true, out var k)) run.Put(k);
                }
                break;
            default:
                if (run == null && !host.Busy)
                {
                    auto = sub == "auto";
                    skill = a.Length > 2 && a[2] == "easy" ? ClimbSkill.Easy : ClimbSkill.Good;
                    Start();
                    if (run == null) auto = false;
                }
                else if (run != null && sub == "auto") { auto = true; bot ??= new ClimbBotState(run.Seed + 99); skill = a.Length > 2 && a[2] == "easy" ? ClimbSkill.Easy : ClimbSkill.Good; }
                break;
        }
        return true;
    }

    public IEnumerable<GameStat> Stats()
    {
        yield return new GameStat(L.T("game.climb"), plays > 0 ? L.T("game.climb_val", best.ToString("N0", L.Culture), plays) : L.T("game.climb_none"), plays > 0);
    }

    // 기록: { Best, Plays, Last, Berries, Friends } — Best 최고 잎 수, Friends 한 판에서 만난 친구 최대 수
    sealed record Record(int Best, int Plays, int Last, int Berries, int Friends);

    public string? SaveState() => plays > 0 ? JsonSerializer.Serialize(new Record(best, plays, last, berries, friendsMax)) : null;

    public void LoadState(string? json)
    {
        if (json == null) return;
        try
        {
            if (JsonSerializer.Deserialize<Record>(json) is not { } r) return;
            (best, plays, last, berries, friendsMax) = (Math.Max(0, r.Best), Math.Max(0, r.Plays), Math.Max(0, r.Last), Math.Max(0, r.Berries), Math.Max(0, r.Friends));
        }
        catch (JsonException) { /* 깨진 기록은 버린다 */ }
    }

    // 자가 검사용: 기록을 직접 만져 본다
    internal (int Best, int Plays, int Last, int Berries, int Friends) State => (best, plays, last, berries, friendsMax);
    internal void KeepForCheck(Climb r, bool practiceRun = false, bool autoRun = false) { (practice, auto) = (practiceRun, autoRun); if (!auto && !practice) Keep(r); (practice, auto) = (false, false); }
    internal (ClimbLook Look, Rectangle Panel, IntPtr Win) Placed => (look, panel, winH);
    internal ClimbView? View => view;
    internal ClimbTheme Theme => theme;
    internal ClimbShow Show => show;
    internal Session? Ses => ses;

    // 게임 한 줄(GameEntry)이 부르는 것: 자가 검사·장면·속도 재기 (ClimbCheck.cs)
    public static string Check() => ClimbCheck.Check();
    public static void Scene(string path, int k) => ClimbCheck.Scene(path, k);
    public static string BenchRun() => ClimbCheck.BenchRun();
}
