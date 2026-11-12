using System.Text.Json;

namespace Bolok;

// 버그 습격 모듈: 앱에 붙이기(판·조작·볼록이), 그리기, 기록, 자가 검사, 디버그 명령. 규칙은 Raid.cs
// 판은 볼록이가 있는 모니터 아래쪽, 볼록이 근처에 120×160 칸(배율 3 이면 360×480)만 쓴다 (볼록이 부대가 너무 크고 눈에 띄었다)
// 조작: 마우스·← → 로 움직이기만 한다. 쏘는 건 자동 (YELL: 누르지 않게). Esc 끝. 본체 창은 숨기고 작은 볼록이를 판 위에 그린다
sealed class RaidGame : IGameMode
{
    readonly IGameHost host;
    Raid? raid;
    int best, bestStage, keys;
    bool auto;
    Point mouse;
    double guideUntil;

    public RaidGame(IGameHost host) => this.host = host;
    public string Id => "raid";
    public string MenuKey => "menu.raid";
    public bool Running => raid != null;

    public void Start()
    {
        var b = host.Pet;
        var area = b.Area;
        int k = b.Scale, w = Math.Min(area.Width, 120 * k), h = Math.Min(area.Height, 160 * k);
        int x = (int)Math.Clamp(b.X - w / 2.0, area.Left, area.Right - w);
        var field = new Rectangle(x, area.Bottom - h, w, h);
        var r = raid = new Raid(field, k, new Random());
        r.OnEvent = e =>
        {
            switch (e)
            {
                case "fire": Sfx.Play("pistol"); break;
                case "hit": case "kill": Sfx.Play("hit"); break;
                case "boss": case "hurt": Sfx.Play("boom"); break;
                case "heart": case "star": case "stage": Sfx.Play("reload"); break;
            }
            if (e is "stage" or "hurt" or "over") Dbg.Log("raid", ("event", e), ("score", r.Score), ("stage", r.Stage), ("lives", r.Lives));
        };
        (keys, mouse, guideUntil) = (0, Control.MousePosition, 4);
        r.Fire(true); // 자동 발사
        var layer = host.OpenLayer(field, (_, _) => { }, (_, _) => { }, () => Stop(true));
        layer.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Left) keys = -1;
            else if (e.KeyCode == Keys.Right) keys = 1;
        };
        layer.KeyUp += (_, e) => { if (e.KeyCode is Keys.Left or Keys.Right) { keys = 0; raid?.SetTarget(raid.X); } };
        b.StartPuppet();
        host.ShowPet(false);
        Dbg.Log("mode-start", ("id", Id), ("field", field.ToString()));
    }

    public void Frame(double dt)
    {
        var r = raid!;
        if (auto) r.SetTarget(r.BotTarget());
        else if (keys != 0) r.SetTarget(r.X + keys * r.Field.Width);
        else if (Control.MousePosition != mouse) { mouse = Control.MousePosition; r.SetTarget(mouse.X); }
        r.Update(dt);
        (host.Pet.X, host.Pet.Y) = (r.X, r.Ground); // 본체는 숨었지만 끝나면 여기서 나타난다
        if (r.Done) Stop(false);
    }

    public void Stop(bool quit)
    {
        if (raid is not { } r) return;
        raid = null;
        host.CloseLayer();
        host.ShowPet(true);
        if (!auto) (best, bestStage) = (Math.Max(best, r.Score), Math.Max(bestStage, r.Stage));
        host.Pet.EndPuppet(r.Stage > 1 || r.Score >= 1000, "raid", ("score", r.Score), ("stage", r.Stage), ("kills", r.Kills), ("esc", quit));
        Dbg.Log("mode-end", ("id", Id));
        auto = false;
        host.Save();
    }

    // ── 그리기: 판 모서리, 버그, 탄, 선물, 터짐, 작은 볼록이, 점수·목숨·판 번호 (글자 없이 숫자·그림만) ──
    string[] Guide => new[] { L.T("note.raid_move"), L.T("note.raid_fire"), L.T("note.esc") };

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (raid is not { } r) return;
        int k = r.K;
        foreach (var f in r.Foes) if (r.Shown(f)) rs.Add(o.Client(Overlay.Around(f.X, f.Y, 18 * k, 18 * k)));
        foreach (var s in r.Shots) rs.Add(o.Client(Overlay.Around(s.X, s.Y, 4 * k, 6 * k)));
        foreach (var d in r.Drops) rs.Add(o.Client(Overlay.Around(d.X, d.Y, 10 * k, 10 * k)));
        foreach (var p in r.Pops) rs.Add(o.Client(Overlay.Around(p.X, p.Y, p.R + 6, p.R + 6)));
        rs.Add(o.Client(Overlay.Around(r.X, r.Ground - 14 * k, 22 * k, 16 * k)));
        var f0 = r.Field;
        rs.Add(o.Client(new Rectangle(f0.Left, f0.Top, f0.Width, 16 * k)));                      // 점수·목숨
        rs.Add(o.Client(Overlay.Around(f0.Left + f0.Width / 2.0, f0.Top + f0.Height * .45, 40 * k, 14 * k))); // 판 번호·끝 점수
        foreach (var c in Corners(f0, k)) rs.Add(o.Client(c));
        if (r.Clock < guideUntil) rs.Add(o.Client(o.NoteRect(r.X, r.Ground - 30 * k, f0, Guide)));
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
        if (raid is not { } r) return;
        var sp = o.Art;
        int k = r.K, small = Math.Max(1, k - 1);
        using (var edge = new SolidBrush(Color.FromArgb(150, 165, 190)))
            foreach (var c in Corners(r.Field, k)) g.FillRectangle(edge, o.Client(c));
        int n = 0;
        foreach (var f in r.Foes)
        {
            n++;
            if (!r.Shown(f) || r.Clock - f.Hit < .08 && (int)(r.Clock * 40) % 2 == 0) continue; // 맞으면 번쩍
            int frame = ((int)(r.Clock * 5) + n) % 2;
            o.Put(g, f.Boss ? sp.Get("game/bugboss", frame, 1) : sp.Get("game/bug", frame, small), (int)f.X, (int)f.Y);
        }
        using (var mine = new SolidBrush(Color.FromArgb(255, 220, 90)))
        using (var spit = new SolidBrush(Color.FromArgb(200, 110, 230)))
        using (var line = new SolidBrush(Color.FromArgb(90, 50, 40)))
            foreach (var s in r.Shots)
            {
                var p = o.ToClient(s.X, s.Y);
                if (s.Enemy) { g.FillRectangle(line, p.X - 2 * k, p.Y - 2 * k, 4 * k, 4 * k); g.FillRectangle(spit, p.X - k - k / 2, p.Y - k - k / 2, 3 * k, 3 * k); }
                else { g.FillRectangle(line, p.X - k - 1, p.Y - 3 * k - 1, 2 * k + 2, 6 * k + 2); g.FillRectangle(mine, p.X - k, p.Y - 3 * k, 2 * k, 6 * k); }
            }
        foreach (var d in r.Drops) o.Put(g, d.Heart ? sp.Get("icon/heart", 0, 1) : sp.Get("game/star", 0, small), (int)d.X, (int)d.Y); // 떨어지는 하트·별은 작게
        foreach (var p in r.Pops) o.Put(g, sp.Get("game/boom", Math.Min(3, (int)((r.Clock - p.At) / .11)), Math.Max(1, (int)(p.R / 14))), (int)p.X, (int)p.Y);

        // 작은 볼록이: 움직이면 걷고, 맞으면 아파하고, 다시 나오면 잠깐 깜빡인다
        bool blink = r.State == Raid.Phase.Play && r.Clock < r.SafeUntil && (int)(r.Clock * 10) % 2 == 0;
        string stage = host.Pet.P.Stage, pose = r.State switch
        {
            Raid.Phase.Hit => "hurt", Raid.Phase.Clear => "happy", Raid.Phase.Over => "sad", _ => r.Moving ? "walk" : "idle",
        };
        string name = $"{stage}/0/{pose}";
        if (!blink && host.Sprites.Has(name) && r.State != Raid.Phase.Done)
        {
            int ms = pose == "walk" ? 130 : 400, cell = 40 * small;
            var at = o.ToClient(r.X - cell / 2.0, r.Ground - cell);
            g.DrawImageUnscaled(host.Sprites.Get(name, (int)(r.Clock * 1000 / ms), small, r.Dir < 0), at.X, at.Y);
        }

        var fl = r.Field;
        o.Label(g, r.Score.ToString(), fl.Left + 6 * k + r.Score.ToString().Length * 5 * small / 2.0, fl.Top + 7 * k, small); // 점수 (왼쪽 위)
        for (int i = 0; i < r.Lives; i++) o.Put(g, sp.Get("icon/heart", 0, 1), fl.Right - (8 + i * 10) * k / 3 * 3 - 6 * k, fl.Top + 7 * k); // 목숨 (오른쪽 위)
        if (r.State == Raid.Phase.Clear || r.Clock < 2.2 && r.Stage == 1 || r.State == Raid.Phase.Play && r.Foes.All(f => !r.Shown(f)))
        {
            o.Put(g, sp.Get("game/star", 0, k), fl.Left + fl.Width / 2 - 12 * k, (int)(fl.Top + fl.Height * .45)); // 판 번호: 별 + 숫자
            o.Label(g, (r.State == Raid.Phase.Clear ? r.Stage + 1 : r.Stage).ToString(), fl.Left + fl.Width / 2.0 + 6 * k, fl.Top + fl.Height * .45, k);
        }
        if (r.State is Raid.Phase.Over) // 끝: 점수 크게, 새 최고 점수면 왕관
        {
            if (r.Score > best) o.Put(g, sp.Get("game/crown", 0, k), fl.Left + fl.Width / 2, (int)(fl.Top + fl.Height * .45 - 14 * k));
            o.Label(g, r.Score.ToString(), fl.Left + fl.Width / 2.0, fl.Top + fl.Height * .45, k + 1);
        }
        if (r.Clock < guideUntil) o.DrawNoteAt(g, r.X, r.Ground - 30 * k, fl, Guide);
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
    sealed class SceneHost : IGameHost
    {
        public SceneHost(Brain b, Sprites s) => (Pet, Sprites) = (b, s);
        public Brain Pet { get; }
        public Sprites Sprites { get; }
        public bool Busy => false;
        public GameLayer OpenLayer(Rectangle field, MouseEventHandler down, MouseEventHandler up, Action quit) => throw new NotSupportedException();
        public void CloseLayer() { }
        public void ShowPet(bool show) { }
        public void Save() { }
    }

    public static void Scene(string path)
    {
        var sp = Sprites.Embedded();
        var area = new Rectangle(0, 0, 1920, 1080);
        var brain = new Brain(new Pet { Stage = "child" }, new Tuning(), area) { Scale = 3 };
        var game = new RaidGame(new SceneHost(brain, sp));
        var r = game.raid = new Raid(new Rectangle(780, 600, 360, 480), 3, new Random(5));
        game.guideUntil = 0;
        r.Fire(true);
        using var ov = new Overlay(brain, () => sp, () => null, area) { Modes = () => new IGameMode[] { game } };
        using var bmp = new Bitmap(380 * 3, 500);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        double[] at = { 1.6, 14, 0 };
        for (int shot = 0, i = 0; shot < 3 && i < 60 * 600; i++)
        {
            r.SetTarget(r.BotTarget());
            r.Update(1 / 60.0);
            bool now = shot < 2 ? r.Clock >= at[shot] : r.State == Raid.Phase.Over && r.Clock > 0;
            if (!now) continue;
            using var frame = new Bitmap(1920, 1080);
            using (var fg = Graphics.FromImage(frame)) { fg.Clear(Color.FromArgb(150, 160, 170)); ov.Render(fg); }
            g.DrawImage(frame, new Rectangle(shot * 380 + 10, 10, 360, 480), new Rectangle(780, 600, 360, 480), GraphicsUnit.Pixel);
            shot++;
        }
        bmp.Save(path);
    }

    // ── 자가 검사 (화면 없이 규칙만) ──
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    public static string Check()
    {
        var field = new Rectangle(780, 600, 360, 480);
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
        return $"버그 습격: 첫 판 {cleared}/5 깸, 가장 멀리 {maxStage}판";
    }
}
