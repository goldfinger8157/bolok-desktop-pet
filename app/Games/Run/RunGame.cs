using System.Drawing.Imaging;
using System.Text.Json;

namespace Bolok;

// 달리기 모듈: 앱에 붙이기(조작·볼록이·녹화 숨김), 그리기, 기록, 디버그 명령. 규칙은 Runner.cs, 자가 검사·장면·속도 재기는 RunCheck.cs
// 판이 없다: 일하던 창은 초점도 그대로 두고, 어느 창에서든 Ctrl(점프)·Shift(앉기)만 읽는다 (Session, layer: false).
// 볼록이는 본체 창 그대로 Puppet 으로 제자리에서 달리고, 그림 창에는 길 위의 것과 발밑의 가는 체력 막대만 그린다 (점수판·배경 없음).
// 끝: Esc(보스 키 → Stop(true))는 점수·표정 없이 바로. 체력이 다 닳아 끝나면 머리 위에 점수를 2.5초 띄운다 —
//     그동안에도 Running 이다 (녹화 숨김을 유지하고, 그사이 Esc 를 누르면 점수도 바로 지운다). Ctrl+Shift 를 0.5초 누르는 옛 끝내기도 남아 있다
sealed partial class RunGame : IGameMode
{
    const double ScoreSec = 2.5, LevelSec = 1.6;
    const int Keep = 50;        // 기록에 남기는 최근 판 수

    readonly IGameHost host;
    readonly Func<bool> blink;
    Runner? run;                // 달리는 중인 판
    Session? ses;               // 열려 있는 동안 화면 공유·녹화에 안 찍힌다: 끝난 점수가 사라질 때까지 열어 둔다
    int best, last, level = 1;
    List<Played> scores = new();
    bool auto;                  // 디버그: 알아서 달린다 (기록 안 남김)
    Watch bar, note;            // 체력 막대·안내 말풍선: 바뀐 프레임에만 다시 칠한다
    string[] guide = Array.Empty<string>();
    double scoreLeft;           // 끝난 점수를 머리 위에 띄워 둘 남은 시간
    int scoreShown;
    bool scoreBest;
    Rectangle wipe;             // 판이 끝난 프레임에 한 번 통째로 다시 칠할 자리 (길 위의 것·막대·안내를 한꺼번에 지운다)

    static readonly Dictionary<string, Sound> Sounds = new()
    {
        ["jump"] = Sound.Tap, ["jump2"] = Sound.Tap, ["seed"] = Sound.Hit, ["hit"] = Sound.Hurt, ["heart"] = Sound.Pick, ["level"] = Sound.Pick,
    };

    public RunGame(IGameHost host)
    {
        this.host = host;
        blink = () => run is { } r && r.Clock < r.InvUntil && (int)(r.Clock * 12) % 2 == 0; // 다친 뒤 안 다치는 동안 깜빡
    }

    public string Id => "run";
    public string MenuKey => "menu.run";
    public bool Running => run != null || scoreLeft > 0;
    public string HudLine => last > 0 ? $"달리기 Lv.{level} {last} (최고 {best})" : "";

    public void Start()
    {
        if (run != null) return;    // 달리는 중에 또 고르면 그대로 달린다
        EndScore();                 // 끝난 점수가 떠 있으면 지우고 새 판
        var b = host.Pet;
        var area = b.Area;          // 볼록이가 있는 모니터 아래쪽
        var r = New(host.Sprites, area, area.Bottom - b.K.FloorGap, b.Scale, b.P.Stage, new Random(), level); // 저장된 레벨에서 시작
        var s = Session.Open(host, area, () => Stop(true), layer: false);
        s.Tick();
        r.Keys(s.Ctrl, false, 0);   // 메뉴를 고를 때 눌려 있던 Ctrl 로 바로 뛰지 않게
        Begin(r, s);
        Dbg.Log("mode-start", ("id", Id), ("track", area.ToString()), ("level", r.Level));
    }

    // 판을 만든다. 판정은 지금 몸 크기대로 (아기는 작게, 어른은 크게), 길 위 그림은 볼록이 배율을 따른다
    static Runner New(Sprites sp, Rectangle track, double ground, int scale, string stage, Random rng, int level) =>
        new(track, ground, 40 * scale, BodyOf(stage), rng, Measure(sp), level);

    static RunBody BodyOf(string stage)
    {
        var (rx, ry) = PetArt.Embedded.Size(stage);
        return RunBody.Of(rx, ry);
    }

    // 길 위 그림을 잰다: 폭, 높이, 칸 왼쪽 위에서 그림까지. 찍는 그림(Stamp: 그려진 부분만 잘라 둔 것)과 같은 상자다
    static Func<string, int, (int, int, int, int)> Measure(Sprites sp)
    {
        var stamps = Stamps.Of(sp);
        return (name, scale) =>
        {
            var s = stamps.Get(name, 0, 1);
            int x0 = s.Dx + sp.Cell / 2, y0 = s.Dy + sp.Cell / 2;
            return (s.Bmp.Width * scale, s.Bmp.Height * scale, x0 * scale, (y0 + s.Bmp.Height) * scale);
        };
    }

    void Begin(Runner r, Session s)
    {
        (run, ses, auto) = (r, s, false);
        (bar, note) = (default, default);
        guide = new[] { L.T("note.run_level", r.Level, Runner.GoalOf(r.Level)), L.T("note.run_jump"), L.T("note.run_duck"), L.T("note.esc") };
        DropGuide();
        var b = host.Pet;
        r.OnEvent = e =>
        {
            Snd.Play(Sounds, e);
            if (e == "hit") b.Emote("bang", .5);
            else if (e == "heart") b.Emote("heart", .8);
            if (e is "hit" or "heart" or "over" or "level") Dbg.Log("run", ("event", e), ("energy", (int)r.Energy), ("score", r.Score));
        };
        Hold();
    }

    // 볼록이를 잡는다: 자리와 자세는 판이 정한다
    void Hold()
    {
        var b = host.Pet;
        b.StartPuppet();
        b.PuppetBlink = blink;
        Pose(run!);
    }

    void Pose(Runner r)
    {
        var b = host.Pet;
        (b.X, b.Y, b.Dir) = (r.PetX, r.Ground - r.Lift, 1);
        b.Pose = r.Clock < r.HurtUntil ? "hurt" : r.Lift > 0 ? (r.Jumps >= 2 ? "roll" : "happy") : r.Sliding ? "land" : "walk";
        b.PuppetMs = b.Pose == "walk" ? 60 : 0; // 달리는 발은 빠르게, 다른 자세는 평소대로
    }

    // 프레임마다 한 번: 막대·안내가 바뀌었는지 (그림 창이 모니터마다 있어도 한 번만 센다)
    void Track()
    {
        var r = run!;
        bar.Set(BarFill(r) + (r.Energy < 25 ? 1 << 20 : 0));
        note.Set(r.Clock < Session.GuideSec ? 1 : 0);
    }

    public void Frame(double dt)
    {
        if (run is not { } r) // 끝난 점수만 떠 있다
        {
            wipe = default;
            if ((scoreLeft -= dt) <= 0) EndScore();
            return;
        }
        var (s, b) = (ses!, host.Pet);
        s.Tick();
        bool quit = false;
        if (auto) Bot(r);
        else quit = r.Keys(s.Ctrl, s.Shift, dt); // Ctrl 톡 점프, Shift 누르는 동안 앉기, 둘을 0.5초 함께 누르면 끝
        r.Update(dt);
        Track();
        // 손으로 잡아 옮기거나 던지는 동안은 가는 대로 둔다 (길은 계속 흐른다). 내려앉으면 다시 달린다
        if (b.Mode == Mode.Puppet) Pose(r);
        else if (b.Mode is not (Mode.Held or Mode.Thrown)) Hold();
        if (r.Over || quit) End(quiet: false, keep: !auto);
    }

    // quit(보스 키): 점수·표정 없이 바로 끝낸다. 기록은 조용히 남긴다. 끝난 점수만 떠 있을 때면 그것을 바로 지운다
    public void Stop(bool quit)
    {
        if (run != null) End(quit, keep: !auto);
        else EndScore();
    }

    void End(bool quiet, bool keep)
    {
        var r = run!;
        run = null;
        var b = host.Pet;
        bool newBest = keep && r.Score > best;
        if (keep)
        {
            (last, best, level) = (r.Score, Math.Max(best, r.Score), r.Level); // 다음 판은 오른 레벨에서
            scores.Add(new Played(DateTime.Now, r.Level, r.Score, r.Seeds));
            if (scores.Count > Keep) scores.RemoveRange(0, scores.Count - Keep);
        }
        var log = new (string, object?)[] { ("score", r.Score), ("seeds", r.Seeds), ("level", r.Level), ("over", r.Over), ("record", keep) };
        if (b.Mode != Mode.Puppet) Dbg.Log(quiet ? "run-quit" : "run-end", log); // 손에 들려 있다: 그대로 둔다
        else if (quiet) { b.LeavePuppet(); Dbg.Log("run-quit", log); }
        else
        {
            b.EndPuppet(true, "run", log);  // 한 판은 놀아주기 한 번, 별
            if (r.Over) b.Pose = "dizzy";   // 지쳐서 끝났으면 어질어질
        }
        DropGuide();
        if (r.Over && !quiet) // 끝까지 달렸으면 머리 위에 점수를 잠깐 (그동안은 녹화 숨김도 그대로). 숨길 땐 아무것도 안 띄운다
        {
            b.EmoteUntil = 0; // 별 말풍선 자리에 점수가 뜬다
            (scoreShown, scoreBest, scoreLeft, wipe) = (r.Score, newBest, ScoreSec, r.Track); // 길 위의 것은 이 프레임에 다 지운다
            host.Repaint();
        }
        else EndScore();
        Dbg.Log("mode-end", ("id", Id));
        auto = false;
        if (keep) host.Save();
    }

    // 점수를 지우고 녹화 숨김을 푼다 (Session.Close 가 그림 창도 통째로 다시 칠한다)
    void EndScore()
    {
        (scoreLeft, wipe) = (0, default);
        ses?.Close();
        ses = null;
    }

    // ── 그리기: 길 위의 것, 발밑 체력 막대, 처음 4초 안내 말풍선, 레벨이 오르면 머리 위 별 + 숫자, 끝나면 머리 위 점수 ──
    // 그림에는 글자를 쓰지 않는다 (글자는 안내 말풍선뿐)
    Sprites? artOf;
    Runner? artRun;
    Stamp[] pics = Array.Empty<Stamp>();    // 길 위 그림 (Runner.Arts 순서): 프레임마다 이름으로 찾지 않는다
    Stamp star = null!;
    Overlay? guideOn;
    Rectangle guideRect;
    Bitmap? guidePic;                       // 안내 말풍선: 4초 동안 안 바뀌니 한 번만 그려 둔다

    void Art(Overlay o, Runner r)
    {
        if (ReferenceEquals(artOf, o.Art) && ReferenceEquals(artRun, r) && pics.Length >= r.Arts.Count) return;
        // 판이 열릴 때 한꺼번에 만들어 둔다 (달리는 중에 처음 나오는 그림을 만드느라 멈칫하지 않게): 길 위 그림, 별, 숫자, 왕관
        var stamps = Stamps.Of(o.Art);
        (artOf, artRun) = (o.Art, r);
        pics = new Stamp[r.Arts.Count];
        for (int i = 0; i < pics.Length; i++) pics[i] = stamps.Get(r.Arts[i].Sprite, 0, r.Arts[i].Scale);
        star = stamps.Get("game/star", 0, r.PetScale);
        stamps.Digits(r.PetScale);
        stamps.Digits(ScoreK);
        stamps.Get("game/crown", 0, ScoreK);
    }

    // 달리는 모니터의 그림 창만 그린다 (옆 모니터에는 들어오기 전·나간 뒤의 것이 보이면 안 된다)
    static bool Here(Overlay o, Runner r) => o.Bounds.IntersectsWith(r.Track);

    static Rectangle BarRect(Runner r)
    {
        int w = 20 * r.PetScale;
        return new Rectangle((int)r.PetX - w / 2, (int)r.Ground + 4, w, 2 * r.PetScale);
    }

    static int BarFill(Runner r) => (int)(20 * r.PetScale * Math.Clamp(r.Energy / 100, 0, 1)); // GameHud.Bar 가 채우는 폭과 같다

    Rectangle GuideRect(Overlay o, Runner r)
    {
        if (!ReferenceEquals(guideOn, o)) { DropGuide(); (guideOn, guideRect) = (o, o.NoteRect(r.PetX, r.Ground, r.Track, guide)); }
        return guideRect;
    }

    Bitmap GuidePic(Overlay o, Runner r)
    {
        var rect = o.Client(GuideRect(o, r));
        if (guidePic == null)
        {
            guidePic = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(guidePic);
            g.TranslateTransform(-rect.X, -rect.Y);
            o.DrawNoteAt(g, r.PetX, r.Ground, r.Track, guide);
        }
        return guidePic;
    }

    void DropGuide()
    {
        guidePic?.Dispose();
        (guidePic, guideOn) = (null, null);
    }

    // 그림 하나(없어도 된다) + 숫자 한 줄: 레벨(별), 끝난 점수(새 기록이면 왕관). (cx, cy) 는 가운데 (화면 좌표), k 는 그림 배율
    static int BadgeWidth(Stamp? icon, int n, int k) => (icon == null ? 0 : icon.Bmp.Width + k) + GameHud.Digits(n) * 5 * k;

    static Rectangle BadgeRect(Stamp? icon, int n, double cx, double cy, int k)
    {
        int w = BadgeWidth(icon, n, k), h = Math.Max(icon?.Bmp.Height ?? 0, 7 * k);
        return new Rectangle((int)cx - w / 2 - k, (int)cy - h / 2 - k, w + 2 * k, h + 2 * k);
    }

    static void DrawBadge(Overlay o, Graphics g, Stamp? icon, int n, double cx, double cy, int k)
    {
        int w = BadgeWidth(icon, n, k), cell = o.Art.Cell * k;
        var at = o.ToClient(cx - w / 2, cy);
        int x = at.X;
        if (icon != null) // 그려진 부분을 세로 가운데에 맞춘다
        {
            int top = icon.Dy + cell / 2;
            g.DrawImageUnscaled(icon.Bmp, x, at.Y - (2 * top + icon.Bmp.Height) / 2 + top);
            x += icon.Bmp.Width + k;
        }
        var glyphs = Stamps.Of(o.Art).Digits(k);
        for (int i = GameHud.Digits(n) - 1, v = Math.Max(0, n); i >= 0; i--, v /= 10) // 숫자 칸은 5칸px 간격, 그림은 칸 가운데에 있다
            glyphs[v % 10].DrawCell(g, x + i * 5 * k + (5 * k - cell) / 2, at.Y - cell / 2, cell);
    }

    // 레벨이 오르면 머리 위에 별 + 레벨 숫자 (뛰면 같이 올라간다)
    static bool LevelOn(Runner r) => r.Clock >= Session.GuideSec && r.Clock - r.LevelAt < LevelSec;
    static (double X, double Y) LevelAt(Runner r) => (r.PetX, r.Ground - r.Lift - r.StandPx - 9 * r.PetScale);

    // 끝난 점수: 머리 위. 말풍선 그림이 떠 있으면 그 위로
    (double X, double Y) ScoreAt() { var b = host.Pet; return (b.X, b.HeadY - (b.Emoting ? 26 : 6) * b.Scale); }
    int ScoreK => GameHud.Small(host.Pet.Scale);
    Stamp? Crown(Overlay o) => scoreBest ? Stamps.Of(o.Art).Get("game/crown", 0, ScoreK) : null;

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (run is not { } r)
        {
            if (scoreLeft > 0) { var (x, y) = ScoreAt(); rs.Add(o.Client(BadgeRect(Crown(o), scoreShown, x, y, ScoreK))); }
            if (!wipe.IsEmpty) rs.Add(o.Client(wipe));
            return;
        }
        if (!Here(o, r)) return;
        Art(o, r);
        int right = r.Track.Right;
        foreach (var t in r.Things) if (!t.Gone && t.X < right) rs.Add(o.Client(Rectangle.Inflate(t.Box, 1, 1))); // 지난 자리는 지난 프레임 목록이 지운다
        if (bar.Changed) rs.Add(o.Client(GameHud.BarRect(BarRect(r), r.PetScale)));
        if (note.Changed) rs.Add(o.Client(GuideRect(o, r)));
        if (LevelOn(r)) { var (x, y) = LevelAt(r); rs.Add(o.Client(BadgeRect(star, r.Level, x, y, r.PetScale))); }
    }

    public void Draw(Overlay o, Graphics g)
    {
        if (run is not { } r)
        {
            if (scoreLeft > 0) { var (x, y) = ScoreAt(); DrawBadge(o, g, Crown(o), scoreShown, x, y, ScoreK); }
            return;
        }
        if (!Here(o, r)) return;
        Art(o, r);
        var clip = Rectangle.Ceiling(g.ClipBounds);
        int right = r.Track.Right;
        foreach (var t in r.Things)
        {
            if (t.Gone || t.X >= right) continue; // 아직 화면 밖
            var box = o.Client(t.Box);
            if (!box.IntersectsWith(clip)) continue; // 다시 칠하는 곳 밖은 건너뛴다
            g.DrawImageUnscaled(pics[t.Art].Bmp, box.X, box.Y);
        }
        GameHud.Bar(o, g, BarRect(r), r.PetScale, r.Energy / 100, r.Energy < 25 ? Pal.Red : Pal.Gold);
        if (r.Clock < Session.GuideSec)
        {
            var at = o.Client(GuideRect(o, r));
            if (at.IntersectsWith(clip)) g.DrawImageUnscaled(GuidePic(o, r), at.X, at.Y);
        }
        else if (LevelOn(r))
        {
            var (x, y) = LevelAt(r);
            DrawBadge(o, g, star, r.Level, x, y, r.PetScale);
        }
    }

    // 디버그: run / run auto (알아서 달린다, 기록 안 남김) / run stop (바로 끝, 기록 안 남김)
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != Id) return false;
        if (a.Length > 1 && a[1] == "stop") { if (run != null) End(quiet: true, keep: false); else EndScore(); }
        else
        {
            if (run == null && (Running || !host.Busy)) Start();
            if (a.Length > 1 && a[1] == "auto") auto = run != null;
        }
        return true;
    }

    public IEnumerable<GameStat> Stats()
    {
        yield return new GameStat(L.T("game.run"), last > 0 ? L.T("game.run_val", level, best.ToString("N0", L.Culture)) : L.T("game.never"), last > 0);
    }

    // 기록: 옛 저장 칸(RunBest·RunLast·RunLevel·RunScores)도 이 모양으로 넘어온다 (GameModes.WithLegacy)
    sealed record Played(DateTime At, int Level, int Score, int Seeds);
    sealed record Record(int Best, int Last, int Level, List<Played>? Scores);

    public string? SaveState() => best > 0 || last > 0 || level > 1 ? JsonSerializer.Serialize(new Record(best, last, level, scores)) : null;

    public void LoadState(string? json)
    {
        if (json == null) return;
        try
        {
            if (JsonSerializer.Deserialize<Record>(json) is not { } r) return;
            (best, last, level, scores) = (r.Best, r.Last, Math.Max(1, r.Level), r.Scores ?? new());
        }
        catch (JsonException) { /* 깨진 기록은 버린다 */ }
    }

    // 잘하는 사람 (자가 검사·속도 재기·디버그 auto): 장애물 한가운데서 가장 높이 뜨게 뛰고, 구름이 오면 엎드리고, 구름이 머리 위에 있으면 기다린다.
    // 사이에 내려설 틈이 없는 장애물들은 한 번에 넘는다 (길 위의 것은 앞에서부터 차례로 들어 있다).
    // 늦게 뛰어서 다음 장애물 앞에 내려설 틈이 없으면 닿기 직전에 한 번 더 뛴다 (더블 점프) — 어려운 레벨에서 연달아 올 때 쓴다
    static void Bot(Runner r)
    {
        var me = r.PetBox;
        bool cloud = false, any = false;
        double from = 0, to = 0, top = 0;
        foreach (var x in r.Things)
        {
            if (x.X + x.W <= me.Left) continue;
            if (x.Kind == ThingKind.Fly && x.X < me.Right + r.Speed * .1) cloud = true;
            if (x.Kind != ThingKind.Block) continue;
            if (!any) (from, to, top, any) = (x.X, x.X + x.W, x.H, true);
            else if (x.X >= from && x.X - to < r.Speed * .45) (to, top) = (Math.Max(to, x.X + x.W), Math.Max(top, x.H));
        }
        r.Slide(cloud);
        if (cloud || !any) return;
        if (r.Lift == 0) { if (from - me.Right <= r.Speed * .395 - (to - from + me.Width) / 2.0) r.Jump(); return; }
        if (r.Jumps != 1 || r.Vy <= 0) return;
        double sf = r.PetScale / 3.0, g = Runner.Gravity * sf, v1 = Runner.JumpSpeed * sf, v2 = v1 * .85;
        double land = (-r.Vy + Math.Sqrt(r.Vy * r.Vy + 2 * g * r.Lift)) / g;
        double front = (from - me.Right) / r.Speed, back = (to - me.Left) / r.Speed;
        bool noTime = back > land && front < land + Rise(v1, top, g) + .035;  // 내려서 다시 뛰기엔 늦다
        if (noTime && front <= Rise(v2, top - r.Lift, g) + .035) r.Jump();
    }

    // 속도 v 로 뛰어 h 만큼 오르는 데 걸리는 시간
    static double Rise(double v, double h, double g) => h <= 0 ? 0 : v * v < 2 * g * h ? 9 : (v - Math.Sqrt(v * v - 2 * g * h)) / g;
}
