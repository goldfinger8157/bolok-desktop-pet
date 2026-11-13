using System.Text.Json;

namespace Bolok;

// 버그 떼 모듈: 앱에 붙이기(판·조작·볼록이), 그리기, 기록, 자가 검사, 디버그 명령. 규칙은 Swarm.cs
// 판은 볼록이를 가운데 두고 220×150 칸(배율 3 이면 660×450)만 쓴다. 모서리 표시만 그리고 바탕은 작업 화면 그대로
// 조작: 방향키로 움직이기만 한다 (판이 앞 창일 때만 읽는다). 총·칼은 자동. Esc 끝. 본체 창은 숨기고 작은 볼록이를 판 위에 그린다
// 표시·효과·소리·조작은 공통 도구(app/Games/Kit)를 쓴다
sealed class SwarmGame : IGameMode
{
    readonly IGameHost host;
    Swarm? swarm;
    Session? ses;
    int bestSec, bestKills;
    bool auto;
    Watch hud;

    static readonly Dictionary<string, Sound> Sounds = new()
    {
        ["fire"] = Sound.Shot, ["kill"] = Sound.Hit, ["bigkill"] = Sound.Boom, ["hurt"] = Sound.Hurt, ["over"] = Sound.Hurt,
        ["level"] = Sound.Pick, ["heart"] = Sound.Pick, ["win"] = Sound.Pick,
    };

    public SwarmGame(IGameHost host) => this.host = host;
    public string Id => "swarm";
    public string MenuKey => "menu.swarm";
    public bool Running => swarm != null;

    public void Start()
    {
        if (Running) return;
        var b = host.Pet;
        var area = b.Area;
        int k = b.Scale, w = Math.Min(area.Width, 220 * k), h = Math.Min(area.Height, 150 * k);
        int x = (int)Math.Clamp(b.X - w / 2.0, area.Left, area.Right - w), y = (int)Math.Clamp(b.Y - 20 * k / 3.0 - h / 2.0, area.Top, area.Bottom - h);
        var field = new Rectangle(x, y, w, h);
        var s = swarm = new Swarm(field, k, new Random());
        s.OnEvent = e =>
        {
            Snd.Play(Sounds, e);
            if (e is "level" or "hurt" or "big" or "over" or "win") Dbg.Log("swarm", ("event", e), ("sec", (int)s.Time), ("level", s.Level), ("kills", s.Kills), ("lives", s.Lives), ("foes", s.Foes.Count));
        };
        hud = default;
        gemRects.Clear();
        ses = Session.Open(host, field, () => Stop(true));
        ses.Guide("note.swarm_move", "note.swarm_auto", "note.esc");
        b.StartPuppet();
        host.ShowPet(false);
        Dbg.Log("mode-start", ("id", Id), ("field", field.ToString()));
    }

    public void Frame(double dt)
    {
        var (s, io) = (swarm!, ses!);
        io.Tick();
        if (auto) { var (dx, dy) = s.BotSteer(); s.Steer(dx, dy); }
        else s.Steer(io.AxisX, io.AxisY); // 다른 창으로 넘어가면 방향키는 뗀 걸로 읽힌다
        s.Update(dt);
        Track();
        (host.Pet.X, host.Pet.Y) = (s.X, s.Feet); // 본체는 숨었지만 끝나면 여기서 나타난다
        if (s.Done || Ended(s) && s.EndedFor > .8 && io.LeftHit) Stop(false); // 끝 화면은 0.8초 뒤부터 눌러서 닫을 수 있다
    }

    // quit(보스 키·디버그 stop): 결과 화면·표정 없이 바로 끝낸다. 기록은 조용히 남긴다
    public void Stop(bool quit)
    {
        if (swarm is not { } s) return;
        swarm = null;
        if (!auto) (bestSec, bestKills) = (Math.Max(bestSec, (int)s.Time), Math.Max(bestKills, s.Kills));
        ses?.Close(host.Pet, quit, s.State == Swarm.Phase.Win || s.Time >= 60, "swarm", ("sec", (int)s.Time), ("kills", s.Kills), ("level", s.Level), ("esc", quit));
        ses = null;
        host.ShowPet(true);
        Dbg.Log("mode-end", ("id", Id));
        auto = false;
        host.Save();
    }

    // ── 그리기: 판 모서리, 보석, 벌레, 총알, 칼 자국, 터짐, 작은 볼록이, 남은 시간·레벨·하트 (글자 없이 숫자·그림만) ──
    static bool Ended(Swarm s) => s.State is Swarm.Phase.Win or Swarm.Phase.Over;
    static int SecLeft(Swarm s) => (int)Math.Ceiling(Swarm.WinSec - s.Time);

    // 자주 찍는 그림은 한 번만 찾아 둔다 (개발 중 그림이 바뀌면 다시)
    Sprites? artOf;
    int artK;
    readonly Stamp[][] foe = new Stamp[4][];   // Swarm.Kind 순서
    Stamp gem = null!, bigGem = null!, heart = null!, flash = null!, plus = null!;
    readonly Stamp[] pistol = new Stamp[2];    // 오른쪽·왼쪽
    readonly Stamp[] upIcon = new Stamp[4];    // Swarm.Up 순서

    void Art(Overlay o, int k)
    {
        if (ReferenceEquals(artOf, o.Art) && artK == k) return;
        var st = Stamps.Of(o.Art);
        int small = GameHud.Small(k), hs = GameHud.HeartScale(k);
        (artOf, artK) = (o.Art, k);
        (foe[(int)Swarm.Kind.Bug], foe[(int)Swarm.Kind.Wasp]) = (st.Row("game/bug", small), st.Row("game/wasp", small));
        (foe[(int)Swarm.Kind.Beetle], foe[(int)Swarm.Kind.Boss]) = (st.Row("game/beetle", small), st.Row("game/bugboss", small));
        (gem, bigGem, heart, flash) = (st.Get("game/gem", 0, small), st.Get("game/gem", 0, k), st.Get("icon/heart", 0, hs), st.Get("game/flash", 0, small));
        (pistol[0], pistol[1], plus) = (st.Get("game/pistol", 0, small), st.Get("game/pistol", 0, small, true), st.Get("num/+", 0, k));
        (upIcon[(int)Swarm.Up.Rate], upIcon[(int)Swarm.Up.Multi], upIcon[(int)Swarm.Up.Arc], upIcon[(int)Swarm.Up.Heart]) =
            (st.Get("game/pistol", 0, k), st.Get("game/bullet", 0, k), st.Get("game/sword", 0, k), heart);
    }

    Stamp FoeArt(Swarm s, Swarm.Foe f)
    {
        var row = foe[(int)f.Kind];
        return row[((int)(s.Clock * (f.Kind == Swarm.Kind.Wasp ? 12 : 5)) + (int)f.Speed) % row.Length];
    }

    Stamp GemArt(Swarm.Gem m) => m.Heart ? heart : m.Xp > 1 ? bigGem : gem;

    // 레벨(별 + 숫자)과 그 아래 보석 막대: 위쪽 가운데
    static (int X, int Y) LevelAt(Rectangle f, int k) => (f.Left + f.Width / 2 - 8 * k, f.Top + 7 * k);
    static Rectangle XpBar(Rectangle f, int k) => new(f.Left + f.Width / 2 - 13 * k, f.Top + 13 * k, 26 * k, 2 * k);

    // 프레임마다 한 번: 표시 값이 바뀌었는지, 보석이 움직이거나 사라졌는지 (가만히 있는 것은 다시 칠하지 않는다)
    readonly List<Rectangle> gemRects = new();

    void Track()
    {
        var s = swarm!;
        hud.Set(HashCode.Combine(SecLeft(s), s.Lives, s.Level, s.Xp, (int)s.State));
        gemRects.Clear();
        if (artOf == null) return; // 아직 한 번도 안 그렸다: 첫 프레임에 판을 통째로 칠한다
        foreach (var m in s.Gone) if (!double.IsNaN(m.ShownX)) gemRects.Add(Rectangle.Inflate(GemArt(m).Box((int)m.ShownX, (int)m.ShownY), 1, 1));
        foreach (var m in s.Gems)
        {
            if (m.ShownX == m.X && m.ShownY == m.Y) continue;
            var art = GemArt(m);
            if (!double.IsNaN(m.ShownX)) gemRects.Add(Rectangle.Inflate(art.Box((int)m.ShownX, (int)m.ShownY), 1, 1));
            gemRects.Add(Rectangle.Inflate(art.Box((int)m.X, (int)m.Y), 1, 1));
            (m.ShownX, m.ShownY) = (m.X, m.Y);
        }
    }

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (swarm is not { } s) return;
        int k = s.K, small = GameHud.Small(k), cell = o.Art.Cell * small;
        var f0 = s.Field;
        Art(o, k);
        if (hud.Fresh) rs.Add(o.Client(f0)); // 판이 막 열렸다: 한 번은 통째로
        foreach (var f in s.Foes) rs.Add(o.Client(Rectangle.Inflate(FoeArt(s, f).Box((int)f.X, (int)f.Y), 2, 2)));
        foreach (var t in s.Shots) rs.Add(o.Client(Overlay.Around(t.X, t.Y, k + 3, k + 3)));
        foreach (var r in gemRects) rs.Add(o.Client(r));
        foreach (var p in s.Pops) rs.Add(o.Client(BlastArt.PopRect(p.X, p.Y, p.R * 1.2, small)));
        rs.Add(o.Client(new Rectangle((int)(s.X - cell / 2.0) - 8 * k, (int)(s.Feet - cell) - 12 * k, cell + 16 * k, cell + 14 * k))); // 볼록이·총·머리 위 그림
        if (s.Clock - s.SwingAt < Swarm.SwingSec + .05) rs.Add(o.Client(Overlay.Around(s.X, s.Y, s.ReachPx + 3 * k, s.ReachPx + 3 * k)));
        if (hud.Changed)
        {
            var (lx, ly) = LevelAt(f0, k);
            rs.Add(o.Client(GameHud.ScoreRect(f0, k)));                                   // 남은 시간
            rs.Add(o.Client(GameHud.HeartsRect(f0, k)));
            rs.Add(o.Client(GameHud.StarRect(lx, ly, small)));                            // 레벨
            rs.Add(o.Client(GameHud.BarRect(XpBar(f0, k), k)));
            if (Ended(s)) rs.Add(o.Client(GameHud.CenterRect(f0, k)));                    // 끝: 잡은 수·왕관
        }
        ses?.DirtyGuide(o, rs, s.Clock, s.X, s.Feet);
    }

    // 아래쪽 벌레가 위에 겹치게 그리는 차례 (프레임마다 목록을 새로 만들지 않는다)
    readonly List<Swarm.Foe> order = new();

    public void Draw(Overlay o, Graphics g)
    {
        if (swarm is not { } s) return;
        int k = s.K, small = GameHud.Small(k);
        var fl = s.Field;
        Art(o, k);
        GameHud.Corners(o, g, fl, k);

        foreach (var m in s.Gems) GemArt(m).Put(o, g, (int)m.X, (int)m.Y);
        order.Clear();
        order.AddRange(s.Foes);
        for (int i = 1; i < order.Count; i++) // 끼워 넣기 정렬: 같은 높이면 먼저 온 벌레가 아래 (차례가 바뀌어 깜빡이지 않는다)
        {
            var f = order[i];
            int j = i - 1;
            for (; j >= 0 && order[j].Y > f.Y; j--) order[j + 1] = order[j];
            order[j + 1] = f;
        }
        foreach (var f in order)
            if (!Fx.Flash(s.Clock, f.Hit)) FoeArt(s, f).Put(o, g, (int)f.X, (int)f.Y); // 맞으면 번쩍
        order.Clear();
        foreach (var t in s.Shots)
        {
            var p = o.ToClient(t.X, t.Y);
            g.FillRectangle(Pal.Line, p.X - k - 1, p.Y - k - 1, 2 * k + 2, 2 * k + 2);
            g.FillRectangle(Pal.Shot, p.X - k, p.Y - k, 2 * k, 2 * k);
        }
        foreach (var p in s.Pops) BlastArt.Pop(o, g, p.X, p.Y, p.R * 1.2, s.Clock - p.At, small);

        // 작은 볼록이: 움직이면 걷고, 맞으면 아파하고 잠깐 깜빡인다. 옆에 총을 들고, 칼은 휘두를 때 자국만 보인다
        bool blink = s.State == Swarm.Phase.Play && Fx.Blink(s.Clock, s.SafeUntil);
        string pose = s.State switch { Swarm.Phase.Win => "happy", Swarm.Phase.Over => "sad", _ => Fx.Hurting(s.Clock, s.HurtAt) ? "hurt" : s.Moving ? "walk" : "idle" };
        int cell = o.Art.Cell * small;
        if (!blink && s.State != Swarm.Phase.Done)
        {
            Fx.Pet(o, g, host.Sprites, host.Pet.P.Stage, pose, s.Clock, small, s.X, s.Feet, s.Dir < 0);
            if (s.State == Swarm.Phase.Play)
            {
                pistol[s.AimDir < 0 ? 1 : 0].Put(o, g, (int)(s.X + s.AimDir * 8 * k), (int)(s.Y + 2 * k));
                if (s.Clock - s.FireAt < .07) flash.Put(o, g, (int)(s.X + s.AimDir * 15 * k), (int)(s.Y + k));
            }
        }
        DrawSwing(o, g, s);
        if (s.LastUp is { } up && s.Clock - s.UpAt < 1.4 && s.State == Swarm.Phase.Play) // 레벨이 오르면 머리 위에 + 와 세진 것 그림이 잠깐 뜬다
        {
            double rise = Math.Min(1, (s.Clock - s.UpAt) / .25) * 4 * k;
            int ux = (int)s.X + 4 * k, uy = (int)(s.Feet - cell + 5 * k - rise);
            plus.Put(o, g, ux - 10 * k, uy);
            upIcon[(int)up].Put(o, g, ux, uy);
        }

        GameHud.Score(o, g, fl, k, SecLeft(s));                                              // 남은 시간 (왼쪽 위, 초)
        GameHud.Hearts(o, g, fl, k, s.Lives);                                                // 하트 (오른쪽 위)
        var (lx, ly) = LevelAt(fl, k);
        GameHud.Star(o, g, lx, ly, small, s.Level);                                          // 레벨: 별 + 숫자, 아래에 보석 막대
        GameHud.Bar(o, g, XpBar(fl, k), k, Math.Min(s.Xp, s.Need) / (double)s.Need, Pal.Cyan);
        if (Ended(s)) GameHud.Result(o, g, fl, k, s.Kills, bestKills, s.Kills > bestKills, "game/bug"); // 끝: 벌레 × 잡은 수, 새 기록이면 큰 왕관
        ses?.DrawGuide(o, g, s.Clock, s.X, s.Feet);
    }

    // 칼 자국: 겨눈 쪽 부채꼴 가장자리를 따라 가운데가 두툼한 초승달이 한쪽 끝에서 다른 끝으로 지나간다 (배율 칸에 맞춘 네모들)
    static void DrawSwing(Overlay o, Graphics g, Swarm s)
    {
        double t = (s.Clock - s.SwingAt) / Swarm.SwingSec;
        if (t is < 0 or >= 1) return;
        int k = s.K, n = Math.Max(8, (int)(4 * s.ArcHalf * s.ReachPx / k));
        for (int i = (int)(n * Math.Max(0, t * 2 - 1)); i <= n * Math.Min(1, t * 2); i++)
        {
            double u = (double)i / n, a = s.SwingDir - s.ArcHalf + 2 * s.ArcHalf * u;
            int deep = 1 + (int)Math.Round(3 * Math.Sin(Math.PI * u)); // 끝은 두 칸, 가운데는 다섯 칸
            for (int d = 0; d <= deep; d++)
            {
                var p = o.ToClient(s.X + Math.Cos(a) * (s.ReachPx - d * k), s.Y + Math.Sin(a) * (s.ReachPx - d * k));
                g.FillRectangle(d == 0 || d == deep ? Pal.Slash : Pal.White, p.X / k * k, p.Y / k * k, k, k);
            }
        }
    }

    // 디버그: swarm / swarm auto (알아서 한다, 기록 안 남김) / swarm stop
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
        bool played = bestSec > 0 || bestKills > 0;
        yield return new GameStat(L.T("game.swarm"), played ? L.T("game.swarm_val", $"{bestSec / 60}:{bestSec % 60:00}", bestKills) : L.T("game.never"), played);
    }

    sealed record Record(int Sec, int Kills);
    public string? SaveState() => bestSec > 0 || bestKills > 0 ? JsonSerializer.Serialize(new Record(bestSec, bestKills)) : null;

    public void LoadState(string? json)
    {
        if (json == null) return;
        try { if (JsonSerializer.Deserialize<Record>(json) is { } r) (bestSec, bestKills) = (r.Sec, r.Kills); }
        catch (JsonException) { /* 깨진 기록은 버린다 */ }
    }

    // ── 화면 없이 그려 보기: 자동 조종으로 돌리며 세 장면 (첫 레벨 업, 한창 칼을 휘두를 때, 끝) ──
    static readonly Rectangle TestField = FieldAt(3);
    static Rectangle FieldAt(int k) => new(960 - 110 * k, 540 - 75 * k, 220 * k, 150 * k); // 1920×1080 모니터 한가운데

    public static void Scene(string path, int k)
    {
        var host = new SceneHost(scale: k);
        var game = new SwarmGame(host);
        var s = game.swarm = new Swarm(FieldAt(k), k, new Random(5));
        using var ov = host.Overlay(game);
        SceneKit.Shots(path, ov, Rectangle.Inflate(s.Field, 20, 20), 3, shot =>
        {
            var (dx, dy) = s.BotSteer();
            s.Steer(dx, dy);
            s.Update(1 / 60.0);
            double swing = (s.Clock - s.SwingAt) / Swarm.SwingSec;
            return shot switch { 0 => s.Level > 1 && s.Clock - s.UpAt > .4, 1 => s.Clock >= 100 && swing is > .4 and < .6 || Ended(s), _ => Ended(s) && s.Clock - s.Time > 1 };
        });
    }

    // 속도 재기: 같은 씨로 한 판을 자동 조종으로 (--mode-bench swarm)
    public static string BenchRun()
    {
        var host = new SceneHost();
        var game = new SwarmGame(host);
        var s = game.swarm = new Swarm(TestField, 3, new Random(5));
        game.ses = Session.Fake(TestField);
        game.ses.Guide("note.swarm_move", "note.swarm_auto", "note.esc");
        using var ov = host.Overlay(game);
        return Bench.Run("swarm", ov, game, s.Field, () => { if (s.Done) return false; var (dx, dy) = s.BotSteer(); s.Steer(dx, dy); return true; }, () => { s.Update(1 / 60.0); game.Track(); });
    }

    // ── 자가 검사 (화면 없이 규칙만) ──
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    public static string Check()
    {
        const double tick = 1 / 60.0;
        void Run(Swarm s, double sec) { for (int i = 0; i < sec * 60 && !s.Done; i++) s.Update(tick); }
        Swarm Empty(int seed) => new(TestField, 3, new Random(seed)) { Spawning = false };

        // 벌레는 네 변에서 다 들어와 볼록이에게 걸어오고, 갈수록 많고 빨라진다
        var early = new Swarm(TestField, 3, new Random(1)) { SafeUntil = 1e9 };
        var walker = early.Add(Swarm.Kind.Bug);
        double far0 = Math.Abs(walker.X - early.X) + Math.Abs(walker.Y - early.Y);
        Expect(!TestField.Contains((int)walker.X, (int)walker.Y), "벌레는 판 밖에서 나온다");
        Run(early, 1);
        Expect(!early.Foes.Contains(walker) || Math.Abs(walker.X - early.X) + Math.Abs(walker.Y - early.Y) < far0, "벌레는 볼록이에게 다가온다");
        Run(early, 24);
        Expect(early.FromSide.All(c => c > 0), $"네 변에서 다 들어온다: {string.Join("·", early.FromSide)}");
        var late = new Swarm(TestField, 3, new Random(1)) { SafeUntil = 1e9, Clock = 140 };
        Run(late, 25);
        Expect(late.FromSide.Sum() > early.FromSide.Sum() * 2, $"갈수록 많아진다: {early.FromSide.Sum()} → {late.FromSide.Sum()}");
        Expect(late.Add(Swarm.Kind.Bug).Speed > walker.Speed && late.Foes.Any(f => f.Kind != Swarm.Kind.Bug), "갈수록 빨라지고 다른 벌레도 섞인다");

        // 총: 닿는 거리 안에서 가장 가까운 벌레를 쏜다. 총알이 둘이면 둘을 하나씩
        var gun = Empty(2);
        gun.Add(Swarm.Kind.Bug, gun.X + 300, gun.Y);
        gun.Update(tick);
        Expect(gun.Shots.Count == 0, "멀리 있는 벌레는 쏘지 않는다");
        Swarm.Foe far = gun.Add(Swarm.Kind.Beetle, gun.X - 200, gun.Y), near = gun.Add(Swarm.Kind.Beetle, gun.X + 120, gun.Y + 20);
        gun.Update(tick);
        Expect(gun.Shots.Count == 1 && gun.Shots[0].Vx > 0 && gun.AimDir == 1, "총은 가장 가까운 벌레를 겨눈다");
        int hp = far.Hp;
        Run(gun, .5);
        Expect(near.Hp == hp - 1 && far.Hp == hp && gun.Shots.Count == 0, $"총알은 겨눈 벌레만 맞힌다: {near.Hp}·{far.Hp}");
        gun.Bullets = 2;
        double fired = gun.FireAt;
        for (int i = 0; i < 120 && gun.FireAt == fired; i++) gun.Update(tick);
        Expect(gun.Shots.Count == 2 && gun.Shots.Any(t => t.Vx > 0) && gun.Shots.Any(t => t.Vx < 0), "총알이 둘이면 가까운 둘을 하나씩 겨눈다");

        // 칼: 바짝 붙은 벌레 쪽으로 휘둘러 부채꼴 안의 벌레를 한꺼번에 벤다. 등 뒤와 먼 벌레는 그대로
        var cut = Empty(3);
        Swarm.Foe a = cut.Add(Swarm.Kind.Bug, cut.X + 30, cut.Y), b = cut.Add(Swarm.Kind.Bug, cut.X + 34, cut.Y + 18),
            back = cut.Add(Swarm.Kind.Bug, cut.X - 40, cut.Y), away = cut.Add(Swarm.Kind.Bug, cut.X + 90, cut.Y);
        cut.Update(tick);
        Expect(cut.SwingAt > 0 && cut.Kills == 2 && !cut.Foes.Contains(a) && !cut.Foes.Contains(b), $"칼은 가까운 벌레를 한꺼번에 벤다: {cut.Kills}");
        Expect(cut.Foes.Contains(back) && cut.Foes.Contains(away) && cut.Gems.Count == 2 && cut.Lives == 3, "등 뒤·먼 벌레는 그대로, 잡은 자리에 보석");

        // 보석: 가까이 가야 주워지고, 다 차면 레벨이 올라 하나가 세진다 (총 빠르게·총알 하나 더·칼 크게·하트)
        var grow = Empty(4);
        grow.Gems.Add(new Swarm.Gem { X = grow.X + 150, Y = grow.Y, Xp = grow.Need });
        Run(grow, .3);
        Expect(grow.Level == 1 && grow.Gems.Count == 1, "먼 보석은 그대로 있다");
        grow.Steer(1, 0);
        Run(grow, 1);
        Expect(grow.Level == 2 && grow.Gems.Count == 0 && grow.LastUp != null, "보석을 주우면 레벨이 오른다");
        var seen = new HashSet<Swarm.Up>();
        for (int i = 0; i < 40; i++)
        {
            var (sec, bullets, arc, lives) = (grow.GunSec, grow.Bullets, grow.ArcHalf, grow.Lives);
            grow.Gems.Add(new Swarm.Gem { X = grow.X, Y = grow.Y, Xp = grow.Need });
            grow.Update(tick);
            var up = grow.LastUp!.Value;
            seen.Add(up);
            Expect(up switch { Swarm.Up.Rate => grow.GunSec < sec, Swarm.Up.Multi => grow.Bullets == bullets + 1, Swarm.Up.Arc => grow.ArcHalf > arc, _ => grow.Lives == Math.Min(Swarm.MaxLives, lives + 1) }, $"레벨이 오르면 세진다: {up}");
        }
        Expect(seen.Count == 4 && grow.Level == 42, $"네 가지가 다 나온다: {seen.Count}가지, 레벨 {grow.Level}");
        Expect(grow.Bullets == Swarm.MaxBullets && grow.Lives == Swarm.MaxLives && grow.ArcHalf <= Math.PI + 1e-9 && grow.GunSec > .25, "세지는 데는 끝이 있다");

        // 닿으면 하트 하나, 잠깐은 안 맞는다. 하트가 다 떨어지면 끝난다
        var hurt = Empty(5);
        hurt.Add(Swarm.Kind.Boss, hurt.X + 5, hurt.Y).Hp = 99;
        hurt.Update(tick);
        Expect(hurt.Lives == 2 && hurt.SafeUntil > hurt.Clock && hurt.State == Swarm.Phase.Play, "벌레에 닿으면 하트 하나");
        hurt.Add(Swarm.Kind.Boss, hurt.X, hurt.Y + 3).Hp = 99;
        Run(hurt, .5);
        Expect(hurt.Lives == 2, "맞은 뒤 잠깐은 안 맞는다");
        Run(hurt, Swarm.SafeSec);
        Expect(hurt.Lives == 1, $"시간이 지나면 다시 맞는다: {hurt.Lives}");
        hurt.Add(Swarm.Kind.Boss, hurt.X, hurt.Y - 3).Hp = 99;
        Run(hurt, Swarm.SafeSec + .2);
        Expect(hurt.Lives == 0 && hurt.State == Swarm.Phase.Over && !hurt.Done, "하트가 다 떨어지면 끝");
        double lasted = hurt.Time;
        Run(hurt, Swarm.EndSec + .1);
        Expect(hurt.Done && hurt.Time == lasted, "끝나면 잠깐 보여 주고 닫는다");

        // 끝까지 버티면 이긴다
        var win = new Swarm(TestField, 3, new Random(6)) { SafeUntil = 1e9, Clock = Swarm.WinSec - 5 };
        Run(win, 5.1);
        Expect(win.State == Swarm.Phase.Win && win.Foes.Count == 0 && win.Time == Swarm.WinSec, "3분을 버티면 이긴다");
        Run(win, Swarm.EndSec + .1);
        Expect(win.Done, "이긴 뒤에도 닫힌다");

        // 자동 조종: 한동안 버티고, 끝은 반드시 온다
        int wins = 0, maxLevel = 0, held60 = 0;
        double total = 0;
        const int seeds = 5;
        for (int seed = 0; seed < seeds; seed++)
        {
            var bot = new Swarm(TestField, 3, new Random(seed));
            for (int i = 0; i < 60 * (Swarm.WinSec + 10) && !bot.Done; i++)
            {
                var (dx, dy) = bot.BotSteer();
                bot.Steer(dx, dy);
                bot.Update(tick);
                Expect(TestField.Contains((int)bot.X, (int)bot.Y) && bot.Foes.Count <= Swarm.MaxFoes + 4 && bot.Shots.Count < 100 && bot.Gems.Count <= Swarm.MaxGems, "판 안에서, 벌레·총알·보석 수 한도");
            }
            Expect(bot.Done, $"판은 끝난다 (seed {seed})");
            if (bot.State == Swarm.Phase.Done && bot.Time >= Swarm.WinSec) wins++;
            if (bot.Time >= 60) held60++;
            (total, maxLevel) = (total + bot.Time, Math.Max(maxLevel, bot.Level));
        }
        Expect(held60 >= seeds - 1, $"자동 조종이 1분은 버틴다: {held60}/{seeds}");

        // 조작: 판이 앞 창일 때만 방향키로 움직인다. 글자 키(WASD)나 다른 창에서 누른 키로는 안 움직인다. 보스 키로 끝내면 티가 나지 않는다
        var keys = new HashSet<Keys> { Keys.W, Keys.A, Keys.S, Keys.D, Keys.Space };
        bool front = true;
        var host = new SceneHost();
        var game = new SwarmGame(host) { swarm = Empty(7), ses = Session.Fake(TestField, keys.Contains, () => front) };
        var play = game.swarm;
        host.Pet.StartPuppet();
        void Frames(int n) { for (int i = 0; i < n; i++) game.Frame(tick); }
        var (x0, y0) = (play.X, play.Y);
        Frames(20);
        Expect(play.X == x0 && play.Y == y0, "글자 키로는 안 움직인다");
        keys.UnionWith(new[] { Keys.Left, Keys.Up });
        Frames(20);
        Expect(play.X < x0 - 20 && play.Y < y0 - 20, $"← ↑ 로 움직인다: {play.X - x0:0}, {play.Y - y0:0}");
        (front, x0, y0) = (false, play.X, play.Y);
        Frames(20);
        Expect(play.X == x0 && play.Y == y0 && !play.Moving, "다른 창을 쓰는 동안 누른 방향키로는 안 움직인다");
        game.Stop(true);
        Expect(!game.Running && host.Pet.Mode == Mode.Wander && !host.Pet.Emoting, "보스 키로 끝내면 결과 화면·표정 없이 바로 끝난다");

        // 다시 칠할 자리: 벌레가 가장 많은 마지막 30초를 자동 조종으로 끝 화면까지. 바뀐 자리만 다시 그려도 화면에 지나간 그림이 남지 않는다
        var paint = game.swarm = new Swarm(TestField, 3, new Random(5)) { Clock = Swarm.WinSec - 30 };
        game.ses = Session.Fake(TestField);
        game.ses.Guide("note.swarm_move", "note.swarm_auto", "note.esc");
        game.ses.GuideUntil = paint.Clock + 2;
        game.hud = default;
        using var ov = host.Overlay(game);
        var (bad, first) = SceneKit.Repaint(ov, game, TestField, () =>
        {
            if (paint.Done) return false;
            var (dx, dy) = paint.BotSteer();
            paint.Steer(dx, dy);
            paint.Update(tick);
            game.Track();
            return true;
        }, 60 * 60);
        Expect(paint.Done && bad == 0, $"바뀐 자리만 다시 칠해도 화면이 맞다: {bad}번 다름 (처음 {first}프레임), 끝남 {paint.Done}");
        game.swarm = null;
        return $"버그 떼: 자동 조종 평균 {total / seeds:0}초 버팀, {wins}/{seeds} 끝까지, 가장 높은 레벨 {maxLevel}";
    }
}
