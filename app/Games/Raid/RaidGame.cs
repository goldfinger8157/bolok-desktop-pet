using System.Text.Json;

namespace Bolok;

// 버그 습격 모듈: 앱에 붙이기(판·조작·볼록이), 그리기, 기록, 자가 검사, 디버그 명령. 규칙은 Raid.cs
// 판은 볼록이가 있는 모니터 아래쪽, 볼록이 근처에 120×160 칸(배율 3 이면 360×480)만 쓴다 (볼록이 부대가 너무 크고 눈에 띄었다)
// 조작: 마우스·← → 로 움직이기만 한다 (방향키는 판이 앞 창일 때만). 쏘는 건 자동 (YELL: 누르지 않게). Esc 끝. 본체 창은 숨기고 작은 볼록이를 판 위에 그린다
// 표시·효과·소리·조작은 공통 도구(app/Games/Kit)를 쓴다
sealed class RaidGame : IGameMode
{
    readonly IGameHost host;
    Raid? raid;
    Session? ses;
    int best, bestStage;
    bool auto, keyMove;
    Watch hud;

    static readonly Dictionary<string, Sound> Sounds = new()
    {
        ["fire"] = Sound.Shot, ["hit"] = Sound.Hit, ["kill"] = Sound.Hit, ["boss"] = Sound.Boom, ["hurt"] = Sound.Hurt,
        ["heart"] = Sound.Pick, ["star"] = Sound.Pick, ["stage"] = Sound.Pick,
    };

    public RaidGame(IGameHost host) => this.host = host;
    public string Id => "raid";
    public string MenuKey => "menu.raid";
    public bool Running => raid != null;

    public void Start()
    {
        if (Running) return;
        var b = host.Pet;
        var area = b.Area;
        int k = b.Scale, w = Math.Min(area.Width, 120 * k), h = Math.Min(area.Height, 160 * k);
        int x = (int)Math.Clamp(b.X - w / 2.0, area.Left, area.Right - w);
        var field = new Rectangle(x, area.Bottom - h, w, h);
        var r = raid = new Raid(field, k, new Random());
        r.OnEvent = e =>
        {
            Snd.Play(Sounds, e);
            if (e is "stage" or "hurt" or "over") Dbg.Log("raid", ("event", e), ("score", r.Score), ("stage", r.Stage), ("lives", r.Lives));
        };
        r.Fire(true); // 자동 발사
        (keyMove, hud) = (false, default);
        ses = Session.Open(host, field, () => Stop(true));
        ses.Guide("note.raid_move", "note.raid_fire", "note.esc");
        b.StartPuppet();
        host.ShowPet(false);
        Dbg.Log("mode-start", ("id", Id), ("field", field.ToString()));
    }

    public void Frame(double dt)
    {
        var (r, s) = (raid!, ses!);
        s.Tick();
        if (auto) r.SetTarget(r.BotTarget());
        else if (s.AxisX != 0) { r.SetTarget(r.X + s.AxisX * r.Field.Width); keyMove = true; }
        else if (keyMove) { r.SetTarget(r.X); keyMove = false; } // 키를 떼면 그 자리에 선다
        else if (s.MouseMoved) r.SetTarget(s.Mouse.X);
        r.Update(dt);
        Track();
        (host.Pet.X, host.Pet.Y) = (r.X, r.Ground); // 본체는 숨었지만 끝나면 여기서 나타난다
        if (r.Done || r.State == Raid.Phase.Over && r.PhaseFor > .8 && s.LeftHit) Stop(false); // 끝 화면은 0.8초 뒤부터 눌러서 닫을 수 있다
    }

    // quit(보스 키·디버그 stop): 결과 화면·표정 없이 바로 끝낸다. 기록은 조용히 남긴다
    public void Stop(bool quit)
    {
        if (raid is not { } r) return;
        raid = null;
        if (!auto) (best, bestStage) = (Math.Max(best, r.Score), Math.Max(bestStage, r.Stage));
        ses?.Close(host.Pet, quit, r.Stage > 1 || r.Score >= 1000, "raid", ("score", r.Score), ("stage", r.Stage), ("kills", r.Kills), ("esc", quit));
        ses = null;
        host.ShowPet(true);
        Dbg.Log("mode-end", ("id", Id));
        auto = false;
        host.Save();
    }

    // ── 그리기: 판 모서리, 버그, 탄, 선물, 터짐, 작은 볼록이, 점수·목숨·판 번호 (글자 없이 숫자·그림만) ──
    // 자주 찍는 그림은 한 번만 찾아 둔다 (개발 중 그림이 바뀌면 다시)
    Sprites? artOf;
    int artK;
    Stamp[] bug = null!, boss = null!;
    Stamp star = null!, heart = null!;

    void Art(Overlay o, int k)
    {
        if (ReferenceEquals(artOf, o.Art) && artK == k) return;
        var st = Stamps.Of(o.Art);
        (artOf, artK) = (o.Art, k);
        int small = GameHud.Small(k);
        (bug, boss) = (st.Row("game/bug", small), st.Row("game/bugboss", (small + 1) / 2)); // 보스 그림은 칸에 두 배로 크게 그려져 있다: 버그보다 조금만 크게
        (star, heart) = (st.Get("game/star", 0, small), st.Get("icon/heart", 0, GameHud.HeartScale(k))); // 떨어지는 하트·별은 작게
    }

    static int Wing(Raid r, int n) => ((int)(r.Clock * 5) + n) % 2; // 날갯짓: 버그마다 엇갈리게
    static string Pose(Raid r) => r.State switch { Raid.Phase.Hit => "hurt", Raid.Phase.Clear => "happy", Raid.Phase.Over => "sad", _ => r.Moving ? "walk" : "idle" };

    // 판 번호(별 + 숫자)를 보일 때면 그 번호, 아니면 0: 첫 판 시작, 판을 깼을 때, 다음 편대가 아직 안 나왔을 때
    static int Banner(Raid r)
    {
        if (r.State == Raid.Phase.Clear) return r.Stage + 1;
        if (r.Clock < 2.2 && r.Stage == 1) return r.Stage;
        if (r.State != Raid.Phase.Play) return 0;
        foreach (var f in r.Foes) if (r.Shown(f)) return 0;
        return r.Stage;
    }

    // 프레임마다 한 번: 표시 값이 바뀌었는지 (바뀐 프레임에만 그 자리를 다시 칠한다)
    void Track()
    {
        var r = raid!;
        hud.Set(HashCode.Combine(r.Score, r.Lives, (int)r.State, Banner(r)));
    }

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (raid is not { } r) return;
        int k = r.K, small = GameHud.Small(k), n = 0;
        var f0 = r.Field;
        Art(o, k);
        if (hud.Fresh) rs.Add(o.Client(f0)); // 판이 막 열렸다: 한 번은 통째로
        foreach (var f in r.Foes)
        {
            n++;
            if (r.Shown(f)) rs.Add(o.Client(Rectangle.Inflate((f.Boss ? boss : bug)[Wing(r, n)].Box((int)f.X, (int)f.Y), 2, 2)));
        }
        foreach (var s in r.Shots) rs.Add(o.Client(s.Enemy ? Overlay.Around(s.X, s.Y, 2 * k + 2, 2 * k + 2) : Overlay.Around(s.X, s.Y, k + 3, 3 * k + 3)));
        foreach (var d in r.Drops) rs.Add(o.Client(Rectangle.Inflate((d.Heart ? heart : star).Box((int)d.X, (int)d.Y), 2, 2)));
        foreach (var p in r.Pops) rs.Add(o.Client(BlastArt.PopRect(p.X, p.Y, p.R * 1.2, small)));
        rs.Add(o.Client(Fx.PetRect(r.X, r.Ground, o.Art.Cell * small)));
        if (hud.Changed)
        {
            rs.Add(o.Client(GameHud.ScoreRect(f0, k)));
            rs.Add(o.Client(GameHud.HeartsRect(f0, k)));
            rs.Add(o.Client(GameHud.CenterRect(f0, k)));
        }
        ses?.DirtyGuide(o, rs, r.Clock, r.X, r.Ground - 30 * k);
    }

    public void Draw(Overlay o, Graphics g)
    {
        if (raid is not { } r) return;
        int k = r.K, small = GameHud.Small(k), n = 0;
        var fl = r.Field;
        Art(o, k);
        GameHud.Corners(o, g, fl, k);
        foreach (var f in r.Foes)
        {
            n++;
            if (!r.Shown(f) || Fx.Flash(r.Clock, f.Hit)) continue; // 맞으면 번쩍
            (f.Boss ? boss : bug)[Wing(r, n)].Put(o, g, (int)f.X, (int)f.Y);
        }
        foreach (var s in r.Shots)
        {
            var p = o.ToClient(s.X, s.Y);
            if (s.Enemy) { g.FillRectangle(Pal.Line, p.X - 2 * k, p.Y - 2 * k, 4 * k, 4 * k); g.FillRectangle(Pal.Spit, p.X - k - k / 2, p.Y - k - k / 2, 3 * k, 3 * k); }
            else { g.FillRectangle(Pal.Line, p.X - k - 1, p.Y - 3 * k - 1, 2 * k + 2, 6 * k + 2); g.FillRectangle(Pal.Shot, p.X - k, p.Y - 3 * k, 2 * k, 6 * k); }
        }
        foreach (var d in r.Drops) (d.Heart ? heart : star).Put(o, g, (int)d.X, (int)d.Y);
        foreach (var p in r.Pops) BlastArt.Pop(o, g, p.X, p.Y, p.R * 1.2, r.Clock - p.At, small);

        // 작은 볼록이: 움직이면 걷고, 맞으면 아파하고, 다시 나오면 잠깐 깜빡인다
        bool blink = r.State == Raid.Phase.Play && Fx.Blink(r.Clock, r.SafeUntil);
        if (!blink && r.State != Raid.Phase.Done) Fx.Pet(o, g, host.Sprites, host.Pet.P.Stage, Pose(r), r.Clock, small, r.X, r.Ground, r.Dir < 0);

        GameHud.Score(o, g, fl, k, r.Score);
        GameHud.Hearts(o, g, fl, k, r.Lives);
        if (Banner(r) is > 0 and var stage) GameHud.Stage(o, g, fl, k, stage);
        if (r.State is Raid.Phase.Over) GameHud.Result(o, g, fl, k, r.Score, best, r.Score > best); // 끝: 점수 크게, 새 기록이면 큰 왕관
        ses?.DrawGuide(o, g, r.Clock, r.X, r.Ground - 30 * k);
    }

    // 디버그: raid / raid auto (알아서 한다, 기록 안 남김) / raid stop
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != Id) return false;
        if (a.Length > 1 && a[1] == "stop") Stop(true);
        else if (a.Length > 1 && a[1] == "auto") auto = true;
        else if (!Running && !host.Busy) Start();
        return true;
    }

    public IEnumerable<GameStat> Stats()
    {
        yield return new GameStat(L.T("game.raid"), best > 0 ? L.T("game.raid_val", best.ToString("N0", L.Culture), bestStage) : L.T("game.never"), best > 0);
    }

    sealed record Record(int Best, int Stage);
    public string? SaveState() => best > 0 ? JsonSerializer.Serialize(new Record(best, bestStage)) : null;

    public void LoadState(string? json)
    {
        if (json == null) return;
        try { if (JsonSerializer.Deserialize<Record>(json) is { } r) (best, bestStage) = (r.Best, r.Stage); }
        catch (JsonException) { /* 깨진 기록은 버린다 */ }
    }

    // ── 화면 없이 그려 보기: 자동 조종으로 돌리며 세 장면 (날아드는 중, 한창, 끝) ──
    static readonly Rectangle TestField = FieldAt(3);
    static Rectangle FieldAt(int k) => new(960 - 60 * k, 1080 - 160 * k, 120 * k, 160 * k); // 1920×1080 모니터 아래 가운데

    public static void Scene(string path, int k)
    {
        var host = new SceneHost(scale: k);
        var game = new RaidGame(host);
        var r = game.raid = new Raid(FieldAt(k), k, new Random(5));
        r.Fire(true);
        using var ov = host.Overlay(game);
        double[] at = { 1.6, 14 };
        SceneKit.Shots(path, ov, r.Field, 3, shot =>
        {
            r.SetTarget(r.BotTarget());
            r.Update(1 / 60.0);
            return shot < 2 ? r.Clock >= at[shot] : r.State == Raid.Phase.Over && r.Clock > 0;
        });
    }

    // 속도 재기: 같은 씨로 한 판을 자동 조종으로 (--mode-bench raid)
    public static string BenchRun()
    {
        var host = new SceneHost();
        var game = new RaidGame(host);
        var r = game.raid = new Raid(TestField, 3, new Random(5));
        game.ses = Session.Fake(TestField);
        game.ses.Guide("note.raid_move", "note.raid_fire", "note.esc");
        r.Fire(true);
        using var ov = host.Overlay(game);
        return Bench.Run("raid", ov, game, r.Field, () => { if (r.Done) return false; r.SetTarget(r.BotTarget()); return true; }, () => { r.Update(1 / 60.0); game.Track(); });
    }

    // ── 자가 검사 (화면 없이 규칙만) ──
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    public static string Check()
    {
        var field = TestField;
        void Run(Raid r, double sec) { for (int i = 0; i < sec * 60 && !r.Done; i++) r.Update(1 / 60.0); }

        var r = new Raid(field, 3, new Random(1));
        Run(r, 12);
        Expect(r.Foes.Count == 20 && r.Foes.All(f => f.Mode != Raid.Move.Enter), $"편대가 다 날아들어 줄을 선다: {r.Foes.Count(f => f.Mode == Raid.Move.Enter)} 남음");
        Expect(r.Foes.Any(f => f.Mode is Raid.Move.Dive or Raid.Move.Back) || r.Foes.All(f => f.Mode == Raid.Move.Hold), "줄 선 뒤 가끔 내려온다");

        var shot = new Raid(field, 3, new Random(2));
        Run(shot, 12);
        var target = shot.Foes.Where(f => f.Mode == Raid.Move.Hold && !f.Boss).OrderByDescending(f => f.Y).First();
        shot.SetTarget(target.X);
        Run(shot, .4);
        shot.Fire(true);
        int score0 = shot.Score, kills0 = shot.Kills;
        Run(shot, .2);
        Expect(shot.Shots.Count(s => !s.Enemy) <= Raid.MaxShots, "내 탄은 한 번에 둘까지");
        Run(shot, 1.5);
        Expect(shot.Kills > kills0 && shot.Score > score0, $"쏘면 버그를 잡고 점수가 오른다: {shot.Kills - kills0}마리");

        var hurt = new Raid(field, 3, new Random(3));
        Run(hurt, 12);
        hurt.Shots.Add(new Raid.Shot { X = hurt.X, Y = hurt.ShipY - 2, Vy = 10, Enemy = true });
        int lives = hurt.Lives;
        Run(hurt, .1);
        Expect(hurt.Lives == lives - 1 && hurt.State == Raid.Phase.Hit, "침에 맞으면 목숨 하나");
        Run(hurt, Raid.HitSec + .1);
        hurt.Shots.Add(new Raid.Shot { X = hurt.X, Y = hurt.ShipY - 2, Vy = 10, Enemy = true });
        Run(hurt, .1);
        Expect(hurt.Lives == lives - 1, "다시 나온 뒤 잠깐은 안 맞는다");

        // 자동 조종: 첫 판은 거의 깨고, 판이 오르면 빨라지며, 끝은 반드시 온다
        int cleared = 0, maxStage = 0;
        for (int seed = 0; seed < 5; seed++)
        {
            var bot = new Raid(field, 3, new Random(seed));
            bot.Fire(true);
            for (int i = 0; i < 60 * 400 && !bot.Done; i++)
            {
                bot.SetTarget(bot.BotTarget());
                bot.Update(1 / 60.0);
                Expect(bot.X >= field.Left && bot.X <= field.Right && bot.Shots.Count < 60, "판 안에서, 탄 수 한도");
            }
            if (bot.Stage > 1) cleared++;
            maxStage = Math.Max(maxStage, bot.Stage);
            Expect(bot.Done, $"판은 끝난다 (seed {seed})");
        }
        Expect(cleared >= 4, $"첫 판은 거의 깬다: {cleared}/5");

        // 조작: 판이 앞 창일 때만 → 로 움직인다. 글자 키나 다른 창에서 누른 키로는 안 움직인다. 보스 키로 끝내면 티가 나지 않는다
        var keys = new HashSet<Keys> { Keys.A, Keys.D, Keys.Space };
        bool front = true;
        var host = new SceneHost();
        var game = new RaidGame(host) { raid = new Raid(field, 3, new Random(4)), ses = Session.Fake(field, keys.Contains, () => front) };
        var play = game.raid;
        host.Pet.StartPuppet();
        void Frames(int n) { for (int i = 0; i < n; i++) game.Frame(1 / 60.0); }
        double x0 = play.X;
        Frames(20);
        Expect(play.X == x0, "글자 키로는 안 움직인다");
        keys.Add(Keys.Right);
        Frames(20);
        Expect(play.X > x0 + 50 * 3 / 3.0, $"→ 로 움직인다: {play.X - x0:0}");
        (front, x0) = (false, play.X);
        Frames(20);
        Expect(play.X == x0, "다른 창을 쓰는 동안 누른 → 로는 안 움직인다");
        game.Start();
        Expect(ReferenceEquals(game.raid, play), "하는 중에 또 시작해도 판은 그대로다");
        game.Stop(true);
        Expect(!game.Running && host.Pet.Mode == Mode.Wander && !host.Pet.Emoting, "보스 키로 끝내면 결과 화면·표정 없이 바로 끝난다");

        // 다시 칠할 자리: 가만히 서서 한 판(목숨 하나)을 끝 화면까지. 바뀐 자리만 다시 그려도 화면에 지나간 그림이 남지 않는다
        var paint = game.raid = new Raid(field, 3, new Random(5)) { Lives = 1 };
        game.ses = Session.Fake(field);
        game.ses.Guide("note.raid_move", "note.raid_fire", "note.esc");
        game.hud = default;
        paint.Fire(true);
        using var ov = host.Overlay(game);
        var (bad, first) = SceneKit.Repaint(ov, game, field, () => { if (paint.Done) return false; paint.Update(1 / 60.0); game.Track(); return true; }, 60 * 120);
        Expect(paint.Done && bad == 0, $"바뀐 자리만 다시 칠해도 화면이 맞다: {bad}번 다름 (처음 {first}프레임), 끝남 {paint.Done}");
        game.raid = null;
        return $"버그 습격: 첫 판 {cleared}/5 깸, 가장 멀리 {maxStage}판";
    }
}
