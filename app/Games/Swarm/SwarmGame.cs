using System.Text.Json;

namespace Bolok;

// 버그 떼 모듈: 앱에 붙이기(판·조작·볼록이), 그리기, 기록, 자가 검사, 디버그 명령. 규칙은 Swarm.cs
// 판은 볼록이를 가운데 두고 220×150 칸(배율 3 이면 660×450)만 쓴다. 모서리 표시만 그리고 바탕은 작업 화면 그대로
// 조작: 방향키(WASD)로 움직이기만 한다. 총·칼은 자동. Esc 끝. 본체 창은 숨기고 작은 볼록이를 판 위에 그린다
sealed class SwarmGame : IGameMode
{
    readonly IGameHost host;
    Swarm? swarm;
    int bestSec, bestKills, held;
    bool auto;
    double guideUntil;

    public SwarmGame(IGameHost host) => this.host = host;
    public string Id => "swarm";
    public string MenuKey => "menu.swarm";
    public bool Running => swarm != null;

    static int Bit(Keys c) => c switch { Keys.Left or Keys.A => 1, Keys.Right or Keys.D => 2, Keys.Up or Keys.W => 4, Keys.Down or Keys.S => 8, _ => 0 };
    int Axis(int minus, int plus) => ((held & plus) != 0 ? 1 : 0) - ((held & minus) != 0 ? 1 : 0);

    public void Start()
    {
        var b = host.Pet;
        var area = b.Area;
        int k = b.Scale, w = Math.Min(area.Width, 220 * k), h = Math.Min(area.Height, 150 * k);
        int x = (int)Math.Clamp(b.X - w / 2.0, area.Left, area.Right - w), y = (int)Math.Clamp(b.Y - 20 * k / 3.0 - h / 2.0, area.Top, area.Bottom - h);
        var field = new Rectangle(x, y, w, h);
        var s = swarm = new Swarm(field, k, new Random());
        s.OnEvent = e =>
        {
            switch (e)
            {
                case "fire": Sfx.Play("pistol"); break;
                case "kill": Sfx.Play("hit"); break;
                case "bigkill": case "hurt": case "over": Sfx.Play("boom"); break;
                case "level": case "heart": case "win": Sfx.Play("reload"); break;
            }
            if (e is "level" or "hurt" or "big" or "over" or "win") Dbg.Log("swarm", ("event", e), ("sec", (int)s.Time), ("level", s.Level), ("kills", s.Kills), ("lives", s.Lives), ("foes", s.Foes.Count));
        };
        (held, guideUntil) = (0, 4);
        var layer = host.OpenLayer(field, (_, _) => { }, (_, _) => { }, () => Stop(true));
        layer.KeyDown += (_, e) => held |= Bit(e.KeyCode);
        layer.KeyUp += (_, e) => held &= ~Bit(e.KeyCode);
        layer.Deactivate += (_, _) => held = 0; // 다른 창으로 넘어가면 누르던 키는 뗀 걸로
        b.StartPuppet();
        host.ShowPet(false);
        Dbg.Log("mode-start", ("id", Id), ("field", field.ToString()));
    }

    public void Frame(double dt)
    {
        var s = swarm!;
        if (auto) { var (dx, dy) = s.BotSteer(); s.Steer(dx, dy); }
        else s.Steer(Axis(1, 2), Axis(4, 8));
        s.Update(dt);
        (host.Pet.X, host.Pet.Y) = (s.X, s.Feet); // 본체는 숨었지만 끝나면 여기서 나타난다
        if (s.Done) Stop(false);
    }

    public void Stop(bool quit)
    {
        if (swarm is not { } s) return;
        swarm = null;
        host.CloseLayer();
        host.ShowPet(true);
        if (!auto) (bestSec, bestKills) = (Math.Max(bestSec, (int)s.Time), Math.Max(bestKills, s.Kills));
        host.Pet.EndPuppet(s.State == Swarm.Phase.Win || s.Time >= 60, "swarm", ("sec", (int)s.Time), ("kills", s.Kills), ("level", s.Level), ("esc", quit));
        Dbg.Log("mode-end", ("id", Id));
        auto = false;
        host.Save();
    }

    // ── 그리기: 판 모서리, 보석, 벌레, 총알, 칼 자국, 터짐, 작은 볼록이, 하트·남은 시간·레벨 (글자 없이 숫자·그림만) ──
    string[] Guide => new[] { L.T("note.swarm_move"), L.T("note.swarm_auto"), L.T("note.esc") };
    static int Small(int k) => Math.Max(1, k - 1);
    static bool Ended(Swarm s) => s.State is Swarm.Phase.Win or Swarm.Phase.Over;

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (swarm is not { } s) return;
        int k = s.K, cell = 40 * Small(k);
        foreach (var f in s.Foes) { int half = (f.Kind == Swarm.Kind.Boss ? 20 : 11) * k; rs.Add(o.Client(Overlay.Around(f.X, f.Y, half, half))); }
        foreach (var t in s.Shots) rs.Add(o.Client(Overlay.Around(t.X, t.Y, 3 * k, 3 * k)));
        foreach (var m in s.Gems) rs.Add(o.Client(Overlay.Around(m.X, m.Y, 8 * k, 8 * k)));
        foreach (var p in s.Pops) rs.Add(o.Client(Overlay.Around(p.X, p.Y, p.R + 8 * k, p.R + 8 * k)));
        rs.Add(o.Client(new Rectangle((int)(s.X - cell / 2.0) - 8 * k, (int)(s.Feet - cell) - 12 * k, cell + 16 * k, cell + 14 * k))); // 볼록이·총·머리 위 그림
        if (s.Clock - s.SwingAt < Swarm.SwingSec) rs.Add(o.Client(Overlay.Around(s.X, s.Y, s.ReachPx + 3 * k, s.ReachPx + 3 * k)));
        var f0 = s.Field;
        rs.Add(o.Client(new Rectangle(f0.Left, f0.Top, f0.Width, 18 * k)));                                     // 하트·남은 시간·레벨
        if (Ended(s)) rs.Add(o.Client(Overlay.Around(f0.Left + f0.Width / 2.0, f0.Top + f0.Height * .4 - 6 * k, 44 * k, 22 * k))); // 끝: 왕관·잡은 수
        foreach (var c in Corners(f0, k)) rs.Add(o.Client(c));
        if (s.Clock < guideUntil) rs.Add(o.Client(o.NoteRect(s.X, s.Feet, f0, Guide)));
    }

    // 판 모서리 네 군데에 작은 ㄱ자 (판이 어디까지인지만 알려 준다)
    static IEnumerable<Rectangle> Corners(Rectangle f, int k)
    {
        int len = 8 * k, t = Math.Max(1, k);
        yield return new Rectangle(f.Left, f.Top, len, t); yield return new Rectangle(f.Left, f.Top, t, len);
        yield return new Rectangle(f.Right - len, f.Top, len, t); yield return new Rectangle(f.Right - t, f.Top, t, len);
        yield return new Rectangle(f.Left, f.Bottom - t, len, t); yield return new Rectangle(f.Left, f.Bottom - len, t, len);
        yield return new Rectangle(f.Right - len, f.Bottom - t, len, t); yield return new Rectangle(f.Right - t, f.Bottom - len, t, len);
    }

    public void Draw(Overlay o, Graphics g)
    {
        if (swarm is not { } s) return;
        var sp = o.Art;
        int k = s.K, small = Small(k), hs = Math.Max(1, k / 3);
        using var dark = new SolidBrush(Color.FromArgb(90, 50, 40));
        using (var edge = new SolidBrush(Color.FromArgb(150, 165, 190)))
            foreach (var c in Corners(s.Field, k)) g.FillRectangle(edge, o.Client(c));

        foreach (var m in s.Gems) o.Put(g, m.Heart ? sp.Get("icon/heart", 0, hs) : sp.Get("game/gem", 0, m.Xp > 1 ? k : small), (int)m.X, (int)m.Y);
        foreach (var f in s.Foes.OrderBy(f => f.Y)) // 아래쪽 벌레가 위에 겹치게
        {
            if (s.Clock - f.Hit < .08 && (int)(s.Clock * 40) % 2 == 0) continue; // 맞으면 번쩍
            string art = f.Kind switch { Swarm.Kind.Boss => "game/bugboss", Swarm.Kind.Beetle => "game/beetle", Swarm.Kind.Wasp => "game/wasp", _ => "game/bug" };
            o.Put(g, sp.Get(art, ((int)(s.Clock * (f.Kind == Swarm.Kind.Wasp ? 12 : 5)) + (int)f.Speed) % 2, small), (int)f.X, (int)f.Y);
        }
        using (var gold = new SolidBrush(Color.FromArgb(255, 220, 90)))
            foreach (var t in s.Shots)
            {
                var p = o.ToClient(t.X, t.Y);
                g.FillRectangle(dark, p.X - k - 1, p.Y - k - 1, 2 * k + 2, 2 * k + 2);
                g.FillRectangle(gold, p.X - k, p.Y - k, 2 * k, 2 * k);
            }
        foreach (var p in s.Pops) o.Put(g, sp.Get("game/boom", Math.Min(3, (int)((s.Clock - p.At) / .11)), Math.Max(1, (int)(p.R / 14))), (int)p.X, (int)p.Y);

        // 작은 볼록이: 움직이면 걷고, 맞으면 아파하고 잠깐 깜빡인다. 옆에 총을 들고, 칼은 휘두를 때 자국만 보인다
        bool blink = s.State == Swarm.Phase.Play && s.Clock < s.SafeUntil && (int)(s.Clock * 10) % 2 == 0;
        string stage = host.Pet.P.Stage, pose = s.State switch
        {
            Swarm.Phase.Win => "happy", Swarm.Phase.Over => "sad", _ => s.Clock - s.HurtAt < .5 ? "hurt" : s.Moving ? "walk" : "idle",
        };
        string name = $"{stage}/0/{pose}";
        int cell = 40 * small;
        if (!blink && host.Sprites.Has(name) && s.State != Swarm.Phase.Done)
        {
            var at = o.ToClient(s.X - cell / 2.0, s.Feet - cell);
            g.DrawImageUnscaled(host.Sprites.Get(name, (int)(s.Clock * 1000 / (pose == "walk" ? 130 : 400)), small, s.Dir < 0), at.X, at.Y);
            if (s.State == Swarm.Phase.Play)
            {
                o.Put(g, sp.Get("game/pistol", 0, small, s.AimDir < 0), (int)(s.X + s.AimDir * 8 * k), (int)(s.Y + 2 * k));
                if (s.Clock - s.FireAt < .07) o.Put(g, sp.Get("game/flash", 0, small), (int)(s.X + s.AimDir * 15 * k), (int)(s.Y + k));
            }
        }
        DrawSwing(o, g, s);
        if (s.LastUp is { } up && s.Clock - s.UpAt < 1.4 && s.State == Swarm.Phase.Play) // 레벨이 오르면 머리 위에 + 와 세진 것 그림이 잠깐 뜬다
        {
            double rise = Math.Min(1, (s.Clock - s.UpAt) / .25) * 4 * k;
            int ux = (int)s.X + 4 * k, uy = (int)(s.Feet - cell + 5 * k - rise);
            o.Label(g, "+", ux - 10 * k, uy, k);
            o.Put(g, up switch
            {
                Swarm.Up.Rate => sp.Get("game/pistol", 0, k), Swarm.Up.Multi => sp.Get("game/bullet", 0, k),
                Swarm.Up.Arc => sp.Get("game/sword", 0, k), _ => sp.Get("icon/heart", 0, hs),
            }, ux, uy);
        }

        var fl = s.Field;
        int top = fl.Top + 7 * k, mid = fl.Left + fl.Width / 2;
        for (int i = 0; i < s.Lives; i++) o.Put(g, sp.Get("icon/heart", 0, hs), fl.Left + (14 + i * 22) * hs + 2 * k, top); // 하트 (왼쪽 위)
        o.Label(g, Math.Ceiling(Swarm.WinSec - s.Time).ToString(), mid, top, small);                                         // 남은 시간 (가운데 위, 초)
        o.Put(g, sp.Get("game/star", 0, small), fl.Right - 24 * k, top);                                                     // 레벨: 별 + 숫자, 아래에 보석 막대
        o.Label(g, s.Level.ToString(), fl.Right - 11 * k, top, small);
        var bar = o.Client(new Rectangle(fl.Right - 30 * k, fl.Top + 13 * k, 26 * k, 2 * k));
        using var cyan = new SolidBrush(Color.FromArgb(80, 214, 226));
        using var back = new SolidBrush(Color.FromArgb(255, 243, 224));
        g.FillRectangle(dark, Rectangle.Inflate(bar, Math.Max(1, k / 2), Math.Max(1, k / 2)));
        g.FillRectangle(back, bar);
        g.FillRectangle(cyan, bar.X, bar.Y, bar.Width * Math.Min(s.Xp, s.Need) / s.Need, bar.Height);
        if (Ended(s)) // 끝: 상자 안에 벌레 × 잡은 수, 끝까지 버텼으면 그 위에 왕관
        {
            string kills = "×" + s.Kills;
            bool won = s.State == Swarm.Phase.Win;
            int cy = (int)(fl.Top + fl.Height * .4), big = k + 1, text = kills.Length * 5 * big, left = mid - (14 * k + text) / 2;
            var box = o.Client(Rectangle.FromLTRB(left - 6 * k, cy - (won ? 24 : 10) * k, left + 14 * k + text + 6 * k, cy + 10 * k));
            g.FillRectangle(back, Rectangle.Inflate(box, k, k));
            g.FillRectangle(dark, box);
            if (won) o.Put(g, sp.Get("game/crown", 0, big), mid, cy - 15 * k);
            o.Put(g, sp.Get("game/bug", 0, small), left + 7 * k, cy - k);
            o.Label(g, kills, left + 14 * k + text / 2.0, cy, big);
        }
        if (s.Clock < guideUntil) o.DrawNoteAt(g, s.X, s.Feet, fl, Guide);
    }

    // 칼 자국: 겨눈 쪽 부채꼴 가장자리를 따라 가운데가 두툼한 초승달이 한쪽 끝에서 다른 끝으로 지나간다 (배율 칸에 맞춘 네모들)
    static void DrawSwing(Overlay o, Graphics g, Swarm s)
    {
        double t = (s.Clock - s.SwingAt) / Swarm.SwingSec;
        if (t is < 0 or >= 1) return;
        int k = s.K, n = Math.Max(8, (int)(4 * s.ArcHalf * s.ReachPx / k));
        using var edge = new SolidBrush(Color.FromArgb(110, 170, 240));
        using var core = new SolidBrush(Color.White);
        for (int i = (int)(n * Math.Max(0, t * 2 - 1)); i <= n * Math.Min(1, t * 2); i++)
        {
            double u = (double)i / n, a = s.SwingDir - s.ArcHalf + 2 * s.ArcHalf * u;
            int deep = 1 + (int)Math.Round(3 * Math.Sin(Math.PI * u)); // 끝은 두 칸, 가운데는 다섯 칸
            for (int d = 0; d <= deep; d++)
            {
                var p = o.ToClient(s.X + Math.Cos(a) * (s.ReachPx - d * k), s.Y + Math.Sin(a) * (s.ReachPx - d * k));
                g.FillRectangle(d == 0 || d == deep ? edge : core, p.X / k * k, p.Y / k * k, k, k);
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
    sealed class SceneHost : IGameHost
    {
        public SceneHost(Brain b, Sprites s) => (Pet, Sprites) = (b, s);
        public Brain Pet { get; }
        public Sprites Sprites { get; }
        public bool Busy => false;
        public GameLayer OpenLayer(Rectangle field, MouseEventHandler down, MouseEventHandler up, Action quit) => throw new NotSupportedException();
        public void CloseLayer() { }
        public void ShowPet(bool show) { }
        public Sprites? SpritesOf(string species) => null;
        public void Save() { }
    }

    static readonly Rectangle TestField = new(630, 315, 660, 450);

    public static void Scene(string path)
    {
        var sp = Sprites.Embedded();
        var area = new Rectangle(0, 0, 1920, 1080);
        var brain = new Brain(new Pet { Stage = "child" }, new Tuning(), area) { Scale = 3 };
        var game = new SwarmGame(new SceneHost(brain, sp));
        var s = game.swarm = new Swarm(TestField, 3, new Random(5));
        var view = Rectangle.Inflate(TestField, 20, 20);
        using var ov = new Overlay(brain, () => sp, () => null, area) { Modes = () => new IGameMode[] { game } };
        using var bmp = new Bitmap((view.Width + 10) * 3 + 10, view.Height + 20);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        for (int shot = 0, i = 0; shot < 3 && i < 60 * 600; i++)
        {
            var (dx, dy) = s.BotSteer();
            s.Steer(dx, dy);
            s.Update(1 / 60.0);
            double swing = (s.Clock - s.SwingAt) / Swarm.SwingSec;
            bool now = shot switch { 0 => s.Level > 1 && s.Clock - s.UpAt > .4, 1 => s.Clock >= 100 && swing is > .4 and < .6 || Ended(s), _ => Ended(s) && s.Clock - s.Time > 1 };
            if (!now) continue;
            using var frame = new Bitmap(area.Width, area.Height);
            using (var fg = Graphics.FromImage(frame)) { fg.Clear(Color.FromArgb(150, 160, 170)); ov.Render(fg); }
            g.DrawImage(frame, new Rectangle(shot * (view.Width + 10) + 10, 10, view.Width, view.Height), view, GraphicsUnit.Pixel);
            shot++;
        }
        bmp.Save(path);
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
        return $"버그 떼: 자동 조종 평균 {total / seeds:0}초 버팀, {wins}/{seeds} 끝까지, 가장 높은 레벨 {maxLevel}";
    }
}
