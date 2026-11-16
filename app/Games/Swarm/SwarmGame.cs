using System.Text.Json;

namespace Bolok;

// 버그 떼 모듈: 앱에 붙이기(판·조작·볼록이), 그리기, 기록, 디버그 명령. 규칙은 Swarm.cs, 무기 값은 SwarmArms.cs, 무기 그림은 SwarmArt.cs, 자가 검사·장면·속도는 SwarmCheck.cs
// 판은 볼록이를 가운데 두고 220×150 칸(배율 3 이면 660×450)만 쓴다. 모서리 표시만 그리고 바탕은 작업 화면 그대로
// 조작: 방향키로 움직이기만 한다 (판이 앞 창일 때만 읽는다). 무기는 자동 (판마다 하나를 무작위로 받는다). Esc 끝. 본체 창은 숨기고 작은 볼록이를 판 위에 그린다
// 표시·효과·소리·조작은 공통 도구(app/Games/Kit)를 쓴다
sealed partial class SwarmGame : IGameMode
{
    readonly IGameHost host;
    Swarm? swarm;
    Session? ses;
    int bestSec, bestKills, wins, clears;      // 최고 기록, 5:00 을 버틴 판 수, 보스 넷을 다 잡고 버틴 판 수
    Arm.Id? lastWeapon, pick;                  // 지난 판 무기 (다음 판에 같은 무기를 안 뽑는다), 디버그로 정한 다음 판 무기
    Dictionary<string, int> best = new();      // 무기 이름 → 그 무기로 버틴 최고 초
    bool auto;
    Watch hud, bossHud;
    readonly Random pickRng = new();
    internal Func<Rectangle, Session>? FakeOpen;   // 자가 검사: 판을 열지 않고 Start 를 돌려 본다

    static readonly Dictionary<string, Sound> Sounds = new()
    {
        ["kill"] = Sound.Hit, ["bigkill"] = Sound.Boom, ["boom"] = Sound.Boom, ["swing"] = Sound.Tap, ["hurt"] = Sound.Hurt, ["over"] = Sound.Hurt,
        ["level"] = Sound.Pick, ["heart"] = Sound.Pick, ["magnet"] = Sound.Pick, ["shoe"] = Sound.Pick, ["win"] = Sound.Pick,
    };
    // 쏘는 소리는 무기마다 (Arm.Id 순서. 근접은 swing, 바주카는 쏠 때 톡·터질 때 쾅, 화염은 톡)
    static readonly Sound[] FireSound = { Sound.Tap, Sound.Tap, Sound.Shot, Sound.ShotGun, Sound.ShotMg, Sound.Tap, Sound.Tap };

    public SwarmGame(IGameHost host) => this.host = host;
    public string Id => "swarm";
    public string MenuKey => "menu.swarm";
    public bool Running => swarm != null;

    // 무기 뽑기: 지난 판과 다른 무기를 무작위로
    static Arm.Id PickWeapon(Random rng, Arm.Id? last)
    {
        int n = rng.Next(last == null ? Arm.Kinds : Arm.Kinds - 1);
        return (Arm.Id)(last != null && n >= (int)last ? n + 1 : n);
    }

    public void Start()
    {
        if (Running) return;
        var b = host.Pet;
        var area = b.Area;
        int k = b.Scale, w = Math.Min(area.Width, 220 * k), h = Math.Min(area.Height, 150 * k);
        int x = (int)Math.Clamp(b.X - w / 2.0, area.Left, area.Right - w), y = (int)Math.Clamp(b.Y - 20 * k / 3.0 - h / 2.0, area.Top, area.Bottom - h);
        var field = new Rectangle(x, y, w, h);
        var weapon = pick ?? PickWeapon(pickRng, lastWeapon);
        pick = null;
        Begin(new Swarm(field, k, new Random(), weapon));
        ses = FakeOpen?.Invoke(field) ?? Session.Open(host, field, () => Stop(true));
        ses.Guide("note.swarm_move", "note.swarm_auto", "note.esc");
        b.StartPuppet();
        host.ShowPet(false);
        Dbg.Log("mode-start", ("id", Id), ("field", field.ToString()), ("weapon", Arm.Name(weapon)));
    }

    // 새 판을 건다 (처음이거나, 하는 중에 디버그로 무기를 바꿔 같은 자리에서 다시)
    void Begin(Swarm s)
    {
        swarm = s;
        s.OnEvent = e =>
        {
            if (e == "fire") Snd.Play(FireSound[(int)s.Weapon.Which]);
            else Snd.Play(Sounds, e);
            if (e is "level" or "hurt" or "big" or "bigkill" or "magnet" or "shoe" or "over" or "win")
                Dbg.Log("swarm", ("event", e), ("sec", (int)s.Time), ("weapon", Arm.Name(s.Weapon.Which)), ("level", s.Level), ("kills", s.Kills), ("hp", s.Hp), ("foes", s.Foes.Count), ("shoes", s.Shoes), ("magnets", s.Magnets), ("boss", s.BossesKilled));
        };
        hud = bossHud = default;
        gemRects.Clear();
        flakes.Trim(0);
        flameRect = Rectangle.Empty;
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
        if (!auto) Keep(s);
        ses?.Close(host.Pet, quit, s.Won || s.Time >= 100, "swarm", ("sec", (int)s.Time), ("kills", s.Kills), ("level", s.Level), ("weapon", Arm.Name(s.Weapon.Which)), ("boss", s.BossesKilled), ("esc", quit));
        ses = null;
        host.ShowPet(true);
        Dbg.Log("mode-end", ("id", Id));
        auto = false;
        host.Save();
    }

    // 기록: 최고 시간·마릿수, 버틴 판·다 잡은 판, 무기별 최고 초, 지난 판 무기
    void Keep(Swarm s)
    {
        string name = Arm.Name(s.Weapon.Which);
        (bestSec, bestKills, lastWeapon) = (Math.Max(bestSec, (int)s.Time), Math.Max(bestKills, s.Kills), s.Weapon.Which);
        if (s.Won) wins++;
        if (s.Cleared) clears++;
        best[name] = Math.Max(best.GetValueOrDefault(name), (int)s.Time);
    }

    // ── 그리기: 판 모서리, 보석·아이템, 벌레·보스, 탄·불길, 칼 자국, 터짐, 작은 볼록이와 손에 든 무기, 표시 (글자 없이 숫자·그림만) ──
    static bool Ended(Swarm s) => s.State is Swarm.Phase.Win or Swarm.Phase.Over;
    static int SecLeft(Swarm s) => (int)Math.Ceiling(Swarm.WinSec - s.Time);
    const double IntroSec = 1.5, RingSec = .6, FeetBarSec = 1.2, FlakeSec = .35;

    // 자주 찍는 그림은 한 번만 찾아 둔다 (개발 중 그림이 바뀌면 다시)
    Sprites? artOf;
    int artK;
    readonly Stamp[][] foe = new Stamp[4][];   // Swarm.Kind 순서
    Stamp[] queen = null!;                     // 여왕: 보스 그림을 한 배율 크게
    readonly Stamp[] rocket = new Stamp[2];    // 오른쪽·왼쪽
    Stamp gem = null!, bigGem = null!, heart = null!, magnet = null!, shoe = null!, flash = null!, plus = null!, crown = null!, bigCrown = null!, spark = null!, bossIcon = null!;
    readonly Stamp[] itemIcon = new Stamp[4];  // Swarm.Item 순서 (주웠을 때 머리 위에 뜨는 그림: 자석·신발만)
    readonly Stamp[] upIcon = new Stamp[4];    // Swarm.Up 순서
    readonly Stamp[] hand = new Stamp[2];      // 이번 판 무기: 오른쪽·왼쪽
    Stamp weaponIcon = null!, weaponBig = null!;
    Arm.Id artWeapon;

    void Art(Overlay o, Swarm s)
    {
        int k = s.K;
        var id = s.Weapon.Which;
        if (ReferenceEquals(artOf, o.Art) && artK == k && artWeapon == id) return;
        var st = Stamps.Of(o.Art);
        int small = GameHud.Small(k), hs = GameHud.HeartScale(k);
        (artOf, artK, artWeapon) = (o.Art, k, id);
        (foe[(int)Swarm.Kind.Bug], foe[(int)Swarm.Kind.Wasp]) = (st.Row("game/bug", small), st.Row("game/wasp", small));
        (foe[(int)Swarm.Kind.Beetle], foe[(int)Swarm.Kind.Boss], queen) = (st.Row("game/beetle", small), st.Row("game/bugboss", small), st.Row("game/bugboss", small + 1));
        (gem, bigGem, heart, flash) = (st.Get("game/gem", 0, small), st.Get("game/gem", 0, k), st.Get("icon/heart", 0, hs), st.Get("game/flash", 0, small));
        (plus, magnet, shoe) = (st.Get("num/+", 0, k), st.Get("game/magnet", 0, small), st.Get("game/shoe", 0, small));
        (crown, bigCrown, spark, bossIcon) = (st.Get("game/crown", 0, small), st.Get("game/crown", 0, k + 1), st.Get("game/spark", 0, small), st.Get("game/bugboss", 0, Math.Max(1, small - 1)));
        (rocket[0], rocket[1]) = (st.Get("game/rocket", 0, small), st.Get("game/rocket", 0, small, true));
        (itemIcon[(int)Swarm.Item.Magnet], itemIcon[(int)Swarm.Item.Shoe]) = (st.Get("game/magnet", 0, k), st.Get("game/shoe", 0, k));
        for (var u = Swarm.Up.Fast; u <= Swarm.Up.Power; u++) upIcon[(int)u] = SwarmArt.UpIcon(u, k);
        upIcon[(int)Swarm.Up.Heal] = heart;
        (hand[0], hand[1], weaponIcon, weaponBig) = (SwarmArt.Hand(id, small, false), SwarmArt.Hand(id, small, true), SwarmArt.Icon(id, small), SwarmArt.Icon(id, k + 1));
    }

    Stamp FoeArt(Swarm s, Swarm.Foe f)
    {
        var row = f.Tier >= Swarm.Bosses ? queen : foe[(int)f.Kind];
        return row[((int)(s.Clock * (f.Kind == Swarm.Kind.Wasp ? 12 : 5)) + (int)f.Speed) % row.Length];
    }

    // 벌레가 그려지는 자리: 돌진 예고로 떨 때는 좌우로 한 칸씩, 여왕은 머리 위 작은 왕관까지
    static int FoeX(Swarm s, Swarm.Foe f) => (int)f.X + (s.Shaking(f) ? (((int)(s.Clock * 30) & 1) == 0 ? 1 : -1) * GameHud.Small(s.K) : 0);
    static int CrownY(Swarm s, Swarm.Foe f) => (int)(f.Y - f.R) - 3 * GameHud.Small(s.K);
    Rectangle FoeRect(Swarm s, Swarm.Foe f)
    {
        int small = GameHud.Small(s.K), x = FoeX(s, f);
        var r = Rectangle.Inflate(FoeArt(s, f).Box(x, (int)f.Y), 2 + small, 2 + small);
        return f.Tier >= Swarm.Bosses ? Rectangle.Union(r, Rectangle.Inflate(crown.Box(x, CrownY(s, f)), 1, 1)) : r;
    }

    Stamp GemArt(Swarm.Gem m) => m.Item switch { Swarm.Item.Magnet => magnet, Swarm.Item.Shoe => shoe, Swarm.Item.Heart => heart, _ => m.Xp > 1 ? bigGem : gem };

    // 떨어져 있는 자석·신발은 눈에 띄게 한 칸씩 까딱인다 (그려지는 높이)
    static double ShowY(Swarm s, Swarm.Gem m) => m.Item >= Swarm.Item.Magnet && ((int)(s.Clock * 2.5) & 1) == 1 ? m.Y - GameHud.Small(s.K) : m.Y;
    static Rectangle GemRect(Stamp art, double x, double y) => Rectangle.Inflate(art.Box((int)x, (int)y), 1, 1);

    // 레벨(별 + 숫자)과 그 아래 보석 막대: 위쪽 가운데. 별 왼쪽에 이번 판 무기
    static (int X, int Y) LevelAt(Rectangle f, int k) => (f.Left + f.Width / 2 - 8 * k, f.Top + 7 * k);
    static Rectangle XpBar(Rectangle f, int k) => new(f.Left + f.Width / 2 - 13 * k, f.Top + 13 * k, 26 * k, 2 * k);
    static (int X, int Y) WeaponAt(Rectangle f, int k) => (f.Left + f.Width / 2 - 30 * k, f.Top + 7 * k);

    // 신발: 레벨 숫자 오른쪽에 주운 켤레만큼 작은 신발 (세 켤레까지)
    static (int X, int Y) ShoeAt(Rectangle f, int k, int i) => (f.Left + f.Width / 2 + (12 + 9 * i) * k, f.Top + 7 * k);

    // 자석: 보석 막대 바로 아래에 자석 그림과 주운 수
    static (int X, int Y) MagnetAt(Rectangle f, int k) => (f.Left + f.Width / 2 - 5 * k, f.Top + 21 * k);

    // 생명: 오른쪽 위에 하트 그림 하나 + 막대. 싸우는 중엔 구석을 못 보니 맞거나 회복한 뒤 잠깐은 발밑에도 작은 막대
    static Rectangle HpBar(Rectangle f, int k) => new(f.Right - 42 * k, f.Top + 6 * k, 36 * k, 3 * k);
    static (int X, int Y) HpHeartAt(Rectangle f, int k) => (f.Right - 44 * k - 10 * GameHud.HeartScale(k), f.Top + 7 * k);
    static Rectangle FeetBar(Swarm s) => new((int)s.X - 7 * s.K, (int)s.Feet + 2 * s.K, 14 * s.K, 2 * s.K);

    // 보스 생명: 판 아래 가운데에 보스 그림 + 막대 (여럿이면 가장 오래된 보스)
    static Rectangle BossBar(Rectangle f, int k) => new(f.Left + f.Width / 2 - 20 * k, f.Bottom - 9 * k, 50 * k, 3 * k);
    static (int X, int Y) BossIconAt(Rectangle f, int k) => (f.Left + f.Width / 2 - 28 * k, f.Bottom - 8 * k);
    static Swarm.Foe? Boss(Swarm s) { foreach (var f in s.Foes) if (f.Kind == Swarm.Kind.Boss) return f; return null; }

    // 보스 예고: 들어올 변 안쪽에 빨간 세모 (가운데 자리)
    static (int X, int Y) WarnAt(Swarm s)
    {
        var f = s.Field;
        int k = s.K, along = s.BossSide < 2 ? f.Top + (int)(s.BossU * f.Height) : f.Left + (int)(s.BossU * f.Width);
        return s.BossSide switch { 0 => (f.Left + 8 * k, along), 1 => (f.Right - 8 * k, along), 2 => (along, f.Top + 8 * k), _ => (along, f.Bottom - 8 * k) };
    }

    static int HpPx(Rectangle bar, int hp) => bar.Width * Math.Clamp(hp, 0, Swarm.MaxHp) / Swarm.MaxHp;
    static bool HpTrail(Swarm s) => s.Clock - s.HpAt < Swarm.HpTrailSec && s.HpFrom > s.Hp;
    static bool HeartHidden(Swarm s) => s.State == Swarm.Phase.Play && s.Hp < Swarm.MaxHp * 3 / 10 && ((int)(s.Clock * 8) & 1) == 0;   // 30% 아래면 4Hz 로 깜빡

    // 불길 조각 (그림 전용: 규칙과 무관해 Random 을 따로 쓴다)
    struct Flake { public double X, Y, Vx, Vy, Born; }
    readonly Bag<Flake> flakes = new(64);
    readonly Random flakeRng = new(7);
    Rectangle flameRect;

    // 프레임마다 한 번: 표시 값이 바뀌었는지, 보석이 움직이거나 사라졌는지 (가만히 있는 것은 다시 칠하지 않는다), 불길 조각
    readonly List<Rectangle> gemRects = new();

    void Track()
    {
        var s = swarm!;
        int k = s.K;
        var bar = HpBar(s.Field, k);
        hud.Set(HashCode.Combine(SecLeft(s), HpPx(bar, s.Hp), HpTrail(s) ? HpPx(bar, s.HpFrom) : -1, HeartHidden(s), s.Level, s.Xp, (int)s.State, s.Shoes * 8 + s.Magnets));
        var boss = Boss(s);
        bossHud.Set(boss == null ? 0 : 1 + boss.Tier + 8 * (BossBar(s.Field, k).Width * boss.Hp / boss.MaxHp)); // 막대가 한 픽셀 줄 때만

        int keep = 0;
        for (int i = 0; i < flakes.Count; i++) if (s.Clock - flakes[i].Born < FlakeSec) flakes[keep++] = flakes[i];
        flakes.Trim(keep);
        if (s.FlameOn && s.State == Swarm.Phase.Play)
            for (int i = 0; i < 3 && flakes.Count < 64; i++)
            {
                double a = s.AimAngle + (flakeRng.NextDouble() * 2 - 1) * s.Weapon.ArcHalf, v = s.RangePx / FlakeSec * (.65 + .35 * flakeRng.NextDouble());
                double ca = Math.Cos(s.AimAngle), sa = Math.Sin(s.AimAngle);
                flakes.Add() = new Flake { X = s.X + ca * 8 * k, Y = s.Y + 2 * k + sa * 8 * k, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Born = s.Clock };
            }
        flameRect = Rectangle.Empty;
        if (flakes.Count > 0)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            for (int i = 0; i < flakes.Count; i++)
            {
                ref var fl = ref flakes[i];
                double age = s.Clock - fl.Born, x = fl.X + fl.Vx * age, y = fl.Y + fl.Vy * age;
                (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
            }
            flameRect = Rectangle.FromLTRB((int)x0 - 3 * k, (int)y0 - 3 * k, (int)x1 + 3 * k + 2, (int)y1 + 3 * k + 2);
        }

        gemRects.Clear();
        if (artOf == null) return; // 아직 한 번도 안 그렸다: 첫 프레임에 판을 통째로 칠한다
        foreach (var m in s.Gone) if (!double.IsNaN(m.ShownX)) gemRects.Add(GemRect(GemArt(m), m.ShownX, m.ShownY));
        foreach (var m in s.Gems)
        {
            double y = ShowY(s, m);
            if (m.ShownX == m.X && m.ShownY == y) continue;
            var art = GemArt(m);
            if (!double.IsNaN(m.ShownX)) gemRects.Add(GemRect(art, m.ShownX, m.ShownY));
            gemRects.Add(GemRect(art, m.X, y));
            (m.ShownX, m.ShownY) = (m.X, y);
        }
    }

    public void Dirty(Overlay o, List<Rectangle> rs)
    {
        if (swarm is not { } s) return;
        int k = s.K, small = GameHud.Small(k), cell = o.Art.Cell * small;
        var f0 = s.Field;
        Art(o, s);
        if (hud.Fresh) rs.Add(o.Client(f0)); // 판이 막 열렸다: 한 번은 통째로
        foreach (var f in s.Foes) rs.Add(o.Client(FoeRect(s, f)));
        for (int i = 0; i < s.Shots.Count; i++)
        {
            ref var t = ref s.Shots[i];
            int half = t.Ammo == Swarm.Ammo.Rocket ? 12 * k : 2 * k + 3;
            rs.Add(o.Client(Overlay.Around(t.X, t.Y, half, half)));
        }
        if (!flameRect.IsEmpty) rs.Add(o.Client(flameRect));
        foreach (var r in gemRects) rs.Add(o.Client(r));
        foreach (var p in s.Pops) rs.Add(o.Client(BlastArt.PopRect(p.X, p.Y, p.R * 1.2, small)));
        rs.Add(o.Client(new Rectangle((int)(s.X - cell / 2.0) - 8 * k, (int)(s.Feet - cell) - 12 * k, cell + 16 * k, cell + 18 * k))); // 볼록이·무기·머리 위 그림·발밑 막대
        if (s.Clock - s.SwingAt < s.SwingLen + .05) rs.Add(o.Client(Overlay.Around(s.X, s.Y, s.RangePx + 3 * k, s.RangePx + 3 * k)));
        if (s.Clock - s.MagnetAt < RingSec) rs.Add(o.Client(Overlay.Around(s.X, s.Y, s.PickPx + 3 * k, s.PickPx + 3 * k)));   // 새 줍는 둘레 (점선 원)
        if (s.BossWarning) { var (wx, wy) = WarnAt(s); rs.Add(o.Client(Overlay.Around(wx, wy, 8 * k, 8 * k))); }
        if (hud.Changed)
        {
            var (lx, ly) = LevelAt(f0, k);
            var (hx, hy) = HpHeartAt(f0, k);
            var (mx, my) = MagnetAt(f0, k);
            rs.Add(o.Client(GameHud.ScoreRect(f0, k)));                                   // 남은 시간
            rs.Add(o.Client(Rectangle.Union(Rectangle.Inflate(heart.Box(hx, hy), 1, 1), GameHud.BarRect(HpBar(f0, k), k))));   // 생명
            rs.Add(o.Client(GameHud.StarRect(lx, ly, small)));                            // 레벨
            rs.Add(o.Client(GameHud.BarRect(XpBar(f0, k), k)));
            var (sx0, sy0) = ShoeAt(f0, k, 0);
            var (sx1, sy1) = ShoeAt(f0, k, Swarm.MaxShoes - 1);
            rs.Add(o.Client(Rectangle.Inflate(Rectangle.Union(shoe.Box(sx0, sy0), shoe.Box(sx1, sy1)), 1, 1)));  // 신발
            rs.Add(o.Client(Rectangle.Union(Rectangle.Inflate(magnet.Box(mx, my), 1, 1), Overlay.Around(mx + 9 * k, my, 4 * small, 5 * small))));   // 자석 수
            if (Ended(s)) rs.Add(o.Client(GameHud.CenterRect(f0, k)));                    // 끝: 잡은 수·왕관
        }
        else if (Ended(s) && s.Cleared) rs.Add(o.Client(GameHud.CenterRect(f0, k)));      // 큰 왕관 둘레 반짝이
        if (bossHud.Changed)
        {
            var (bx, by) = BossIconAt(f0, k);
            rs.Add(o.Client(Rectangle.Union(Rectangle.Inflate(bossIcon.Box(bx, by), 1, 1), GameHud.BarRect(BossBar(f0, k), k))));
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
        var arm = s.Weapon;
        Art(o, s);
        GameHud.Corners(o, g, fl, k);

        for (int pass = 0; pass < 2; pass++) // 자석·신발은 보석 더미에 묻히지 않게 위에
            foreach (var m in s.Gems)
                if (m.Item >= Swarm.Item.Magnet == (pass == 1)) GemArt(m).Put(o, g, (int)m.X, (int)ShowY(s, m));
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
        {
            int fx = FoeX(s, f);
            if (!Fx.Flash(s.Clock, f.Hit)) FoeArt(s, f).Put(o, g, fx, (int)f.Y); // 맞으면 번쩍
            if (f.Tier >= Swarm.Bosses) crown.Put(o, g, fx, CrownY(s, f));        // 여왕은 왕관
            if (s.Clock < f.BurnUntil)                                           // 불붙은 벌레: 등에 불씨가 깜빡
            {
                var p = o.ToClient(f.X, f.Y - f.R * .5);
                int heat = (int)(s.Clock * 10) & 1;
                Fx.Ember(g, p.X, p.Y, 2 * small, 1 + heat);
                Fx.Ember(g, p.X, p.Y - small, small, heat);
            }
        }
        order.Clear();
        for (int i = 0; i < s.Shots.Count; i++) DrawShot(o, g, s, ref s.Shots[i]);
        for (int i = 0; i < flakes.Count; i++) // 불길: 멀어질수록 크고 붉게
        {
            ref var f = ref flakes[i];
            double age = s.Clock - f.Born, u = age / FlakeSec;
            var p = o.ToClient(f.X + f.Vx * age, f.Y + f.Vy * age);
            Fx.Ember(g, p.X, p.Y, (int)((1.5 + 3 * u) * k), u < .3 ? 0 : u < .65 ? 1 : 2);
        }
        foreach (var p in s.Pops) BlastArt.Pop(o, g, p.X, p.Y, p.R * 1.2, s.Clock - p.At, small);

        // 작은 볼록이: 움직이면 걷고, 맞으면 아파하고 잠깐 깜빡인다. 손에 이번 판 무기를 든다 (양손 무기는 양쪽에)
        bool blink = s.State == Swarm.Phase.Play && Fx.Blink(s.Clock, s.SafeUntil);
        string pose = s.State switch { Swarm.Phase.Win => "happy", Swarm.Phase.Over => "sad", _ => Fx.Hurting(s.Clock, s.HurtAt) ? "hurt" : s.Moving ? "walk" : "idle" };
        int cell = o.Art.Cell * small;
        if (!blink && s.State != Swarm.Phase.Done)
        {
            Fx.Pet(o, g, host.Sprites, host.Pet.P.Stage, pose, s.Clock, small, s.X, s.Feet, s.Dir < 0);
            if (s.State == Swarm.Phase.Play)
            {
                int hy = (int)(s.Y + 2 * k);
                if (SwarmArt.Dual(arm.Which))
                {
                    hand[0].Put(o, g, (int)(s.X + 9 * k), hy);
                    hand[1].Put(o, g, (int)(s.X - 9 * k), hy);
                }
                else hand[s.AimDir < 0 ? 1 : 0].Put(o, g, (int)(s.X + s.AimDir * 8 * k), hy);
                if (arm.Kind is Arm.Sort.Bullets or Arm.Sort.Rocket && s.Clock - s.FireAt < .07)
                    flash.Put(o, g, (int)(s.X + s.AimDir * (8 * k + hand[0].Bmp.Width / 2 + 2 * small)), hy - small);
            }
        }
        DrawSwing(o, g, s);
        if (s.Clock - s.MagnetAt < RingSec && s.State == Swarm.Phase.Play) DrawRing(o, g, s);
        if (s.BossWarning && ((int)((s.Clock - s.BossWarnAt) * 6) & 1) == 0) DrawWarn(o, g, s);

        // 시작할 때 머리 위에 이번 무기를 크게. 레벨이 오르거나 자석·신발을 주우면 + 와 그 그림이 잠깐 뜬다 (겹치면 나중 것)
        double popAt = Math.Max(s.UpAt, s.ItemAt);
        var pop = s.ItemAt > s.UpAt ? itemIcon[(int)s.LastItem] : s.LastUp is { } up ? upIcon[(int)up] : null;
        if (pop != null && s.Clock - popAt < 1.4 && s.State == Swarm.Phase.Play)
        {
            double rise = Math.Min(1, (s.Clock - popAt) / .25) * 4 * k;
            int ux = (int)s.X + 4 * k, uy = (int)(s.Feet - cell + 5 * k - rise);
            plus.Put(o, g, ux - 10 * k, uy);
            pop.Put(o, g, ux, uy);
        }
        else if (s.Clock < IntroSec && s.State == Swarm.Phase.Play)
            weaponBig.Put(o, g, (int)s.X, (int)(s.Feet - cell + 5 * k - Math.Min(1, s.Clock / .25) * 4 * k));
        if (s.Clock - s.HpAt < FeetBarSec && s.State == Swarm.Phase.Play)                      // 발밑 생명 막대
            GameHud.Bar(o, g, FeetBar(s), k, s.Hp / (double)Swarm.MaxHp, Pal.Red);

        GameHud.Score(o, g, fl, k, SecLeft(s));                                              // 남은 시간 (왼쪽 위, 초)
        var bar = HpBar(fl, k);                                                              // 생명 (오른쪽 위): 하트 + 막대, 방금 잃은 만큼은 잠깐 흰색
        var (hx, hy0) = HpHeartAt(fl, k);
        if (!HeartHidden(s)) heart.Put(o, g, hx, hy0);
        GameHud.Bar(o, g, bar, k, HpPx(bar, s.Hp) / (double)bar.Width, Pal.Red);
        if (HpTrail(s))
        {
            var c = o.Client(bar);
            int now = HpPx(bar, s.Hp);
            g.FillRectangle(Pal.White, c.X + now, c.Y, HpPx(bar, s.HpFrom) - now, c.Height);
        }
        var (wx, wy) = WeaponAt(fl, k);
        weaponIcon.Put(o, g, wx, wy);                                                        // 이번 판 무기
        var (lx, ly) = LevelAt(fl, k);
        GameHud.Star(o, g, lx, ly, small, s.Level);                                          // 레벨: 별 + 숫자, 아래에 보석 막대
        for (int i = 0; i < s.Shoes; i++)                                                    // 신발: 주운 켤레만큼
        {
            var (sx, sy) = ShoeAt(fl, k, i);
            shoe.Put(o, g, sx, sy);
        }
        GameHud.Bar(o, g, XpBar(fl, k), k, Math.Min(s.Xp, s.Need) / (double)s.Need, Pal.Cyan);
        if (s.Magnets > 0)                                                                   // 자석: 그림 + 주운 수
        {
            var (mx, my) = MagnetAt(fl, k);
            magnet.Put(o, g, mx, my);
            GameHud.Number(o, g, s.Magnets, mx + 9 * k, my, small, -1);
        }
        if (Boss(s) is { } boss)                                                             // 보스 생명
        {
            var (bx, by) = BossIconAt(fl, k);
            var bb = BossBar(fl, k);
            bossIcon.Put(o, g, bx, by);
            GameHud.Bar(o, g, bb, k, (bb.Width * boss.Hp / boss.MaxHp) / (double)bb.Width, Pal.Spit);
        }
        if (Ended(s)) DrawResult(o, g, s);
        ses?.DrawGuide(o, g, s.Clock, s.X, s.Feet);
    }

    // 끝: 벌레 × 잡은 수. 새 기록이거나 보스 넷을 다 잡고 버텼으면 큰 왕관(다 잡았으면 반짝이까지), 5:00 을 버티기만 했으면 상자 위에 작은 왕관
    void DrawResult(Overlay o, Graphics g, Swarm s)
    {
        int k = s.K, big = k + 1, small = GameHud.Small(k);
        var fl = s.Field;
        bool top = s.Kills > bestKills || s.Cleared;
        GameHud.Result(o, g, fl, k, s.Kills, bestKills, top, "game/bug");
        int cx = fl.Left + fl.Width / 2, boxTop = (int)(fl.Top + fl.Height * .45) - 5 * big - 3 * k;
        if (s.Won && !top) crown.Put(o, g, cx, boxTop - 4 * small);
        if (!s.Cleared) return;
        for (int i = 0; i < 4; i++)
            if ((((int)(s.Clock * 5) + i) & 1) == 0)
                spark.Put(o, g, cx + (i < 2 ? -1 : 1) * ((i & 1) == 0 ? 11 : 8) * big, boxTop - 7 * big + ((i & 1) == 0 ? -3 : 2) * big);
    }

    void DrawShot(Overlay o, Graphics g, Swarm s, ref Swarm.Shot t)
    {
        int k = s.K;
        var p = o.ToClient(t.X, t.Y);
        switch (t.Ammo)
        {
            case Swarm.Ammo.Rocket: // 로켓 그림 + 뒤에 연기 둘
                double v = Math.Sqrt(t.Vx * t.Vx + t.Vy * t.Vy);
                for (int i = 1; i <= 2; i++) Fx.Puff(g, p.X - (int)(t.Vx / v * (3 + 3 * i) * k), p.Y - (int)(t.Vy / v * (3 + 3 * i) * k), (4 - i) * k, .5);
                rocket[t.Vx < 0 ? 1 : 0].Draw(g, p.X, p.Y);
                break;
            case Swarm.Ammo.Pellet: // 산탄 알은 한 칸 작게
                int h = Math.Max(1, k - 1);
                g.FillRectangle(Pal.Line, p.X - h - 1, p.Y - h - 1, 2 * h + 2, 2 * h + 2);
                g.FillRectangle(Pal.Shot, p.X - h, p.Y - h, 2 * h, 2 * h);
                break;
            case Swarm.Ammo.Round: // 소총탄은 날아가는 쪽으로 두 칸
                double sp = Math.Sqrt(t.Vx * t.Vx + t.Vy * t.Vy);
                int bx = p.X - (int)(t.Vx / sp * k), by = p.Y - (int)(t.Vy / sp * k), hk = Math.Max(1, k * 2 / 3);
                g.FillRectangle(Pal.Line, bx - hk - 1, by - hk - 1, 2 * hk + 2, 2 * hk + 2);
                g.FillRectangle(Pal.Line, p.X - hk - 1, p.Y - hk - 1, 2 * hk + 2, 2 * hk + 2);
                g.FillRectangle(Pal.Flame[1], bx - hk, by - hk, 2 * hk, 2 * hk);
                g.FillRectangle(Pal.Shot, p.X - hk, p.Y - hk, 2 * hk, 2 * hk);
                break;
            default:
                g.FillRectangle(Pal.Line, p.X - k - 1, p.Y - k - 1, 2 * k + 2, 2 * k + 2);
                g.FillRectangle(Pal.Shot, p.X - k, p.Y - k, 2 * k, 2 * k);
                break;
        }
    }

    // 칼 자국: 겨눈 쪽 부채꼴 가장자리를 따라 초승달이 한쪽 끝에서 다른 끝으로 지나간다 (배율 칸에 맞춘 네모들).
    // 장검은 가운데가 두툼하고, 쌍검은 얇고 손마다 도는 쪽이 반대다. 회전 베기는 한 바퀴
    static void DrawSwing(Overlay o, Graphics g, Swarm s)
    {
        double t = (s.Clock - s.SwingAt) / s.SwingLen;
        if (t is < 0 or >= 1) return;
        bool twin = s.Weapon.Which == Arm.Id.Twin, thin = twin && !s.SwingSpin;
        double arc = s.SwingArc, reach = s.RangePx;
        int k = s.K, n = Math.Max(8, (int)(4 * arc * reach / k));
        for (int i = (int)(n * Math.Max(0, t * 2 - 1)); i <= n * Math.Min(1, t * 2); i++)
        {
            double u = (double)i / n, a = s.SwingDir + (twin && s.SwingHand < 0 ? 1 : -1) * (arc - 2 * arc * u);
            int deep = thin ? (int)Math.Round(1.4 * Math.Sin(Math.PI * u)) : 1 + (int)Math.Round(3 * Math.Sin(Math.PI * u)); // 장검: 끝은 두 칸, 가운데는 다섯 칸
            for (int d = 0; d <= deep; d++)
            {
                var p = o.ToClient(s.X + Math.Cos(a) * (reach - d * k), s.Y + Math.Sin(a) * (reach - d * k));
                g.FillRectangle(thin ? (d == 0 ? Pal.White : Pal.Slash) : d == 0 || d == deep ? Pal.Slash : Pal.White, p.X / k * k, p.Y / k * k, k, k);
            }
        }
    }

    // 자석을 주운 순간: 새 줍는 둘레를 점선 원으로 한 번 보여 준다 (배율 칸 네모)
    static void DrawRing(Overlay o, Graphics g, Swarm s)
    {
        int k = s.K, n = Math.Max(12, (int)(Math.PI * s.PickPx / (4 * k)) / 2 * 2);
        for (int i = 0; i < n; i += 2)
        {
            double a = i * 2 * Math.PI / n;
            var p = o.ToClient(s.X + Math.Cos(a) * s.PickPx, s.Y + Math.Sin(a) * s.PickPx);
            g.FillRectangle(Pal.Line, p.X - k - 1, p.Y - k - 1, 2 * k + 2, 2 * k + 2);
            g.FillRectangle(Pal.Gold, p.X - k, p.Y - k, 2 * k, 2 * k);
        }
    }

    // 보스 예고: 판 안쪽을 가리키는 빨간 세모 (갈색 테두리)
    static void DrawWarn(Overlay o, Graphics g, Swarm s)
    {
        var (x, y) = WarnAt(s);
        var c = o.ToClient(x, y);
        int k = s.K, ax = s.BossSide == 0 ? 1 : s.BossSide == 1 ? -1 : 0, ay = s.BossSide == 2 ? 1 : s.BossSide == 3 ? -1 : 0;
        for (int pass = 0; pass < 2; pass++) // 테두리(한 칸 큰 세모) 다음에 속
            for (int i = 0; i < (pass == 0 ? 7 : 5); i++)
            {
                int along = (pass == 0 ? 4 : 3) - i; // 꼭짓점에서 i 칸 뒤의 줄 (폭 2i+1 칸)
                int w = (2 * i + 1) * k, px = c.X + ax * along * k - k / 2, py = c.Y + ay * along * k - k / 2;
                var r = ax != 0 ? new Rectangle(px, c.Y - w / 2, k, w) : new Rectangle(c.X - w / 2, py, w, k);
                g.FillRectangle(pass == 0 ? Pal.Line : Pal.Red, r);
            }
    }

    // 디버그: swarm / swarm weapon <id> / swarm auto [id] (알아서 한다, 기록 안 남김) / swarm at <초> / swarm boss [1~4] / swarm up [n] / swarm hp <n> / swarm item <heart|magnet|shoe> / swarm stop
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != Id) return false;
        string sub = a.Length > 1 ? a[1] : "", arg = a.Length > 2 ? a[2] : "";
        int.TryParse(arg, out int n);
        var s = swarm;
        switch (sub)
        {
            case "stop": Stop(true); break;
            case "weapon": if (Arm.Find(arg, out var id)) StartWith(id); break;
            case "auto":
                if (Arm.Find(arg, out var autoId)) StartWith(autoId);
                else if (!Running) StartWith(null);
                auto = Running;
                break;
            case "at": if (s != null && double.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sec)) s.SetClock(sec); break;
            case "boss": s?.CallBoss(n > 0 ? n : Math.Min(Swarm.Bosses, s.BossesKilled + 1)); break;
            case "up": for (int i = 0; s != null && i < Math.Clamp(n, 1, 60); i++) s.LevelUp(); break;
            case "hp": if (s != null && n > 0) { s.Hp = Math.Min(Swarm.MaxHp, n); s.Heal(0); } break;
            case "item":
                if (s != null && arg is "heart" or "magnet" or "shoe") s.Drop(arg == "heart" ? Swarm.Item.Heart : arg == "magnet" ? Swarm.Item.Magnet : Swarm.Item.Shoe);
                break;
            default: StartWith(null); break;
        }
        return true;
    }

    // 그 무기로 시작한다 (null 이면 무작위). 하는 중이면 그 무기로 같은 자리에서 새 판
    void StartWith(Arm.Id? weapon)
    {
        if (swarm is { } s) { if (weapon is { } w) Begin(new Swarm(s.Field, s.K, new Random(), w)); }
        else if (!host.Busy) { pick = weapon; Start(); }
    }

    public IEnumerable<GameStat> Stats()
    {
        bool played = bestSec > 0 || bestKills > 0;
        yield return new GameStat(L.T("game.swarm"), played ? L.T("game.swarm_val", $"{bestSec / 60}:{bestSec % 60:00}", bestKills) : L.T("game.never"), played);
    }

    // 기록. 옛 기록 {"Sec":120,"Kills":300} 도 그대로 읽힌다 (없는 칸은 0·null). 무기는 enum 번호가 아니라 글 이름으로 (무기를 넣고 빼도 안 깨진다)
    sealed record Record(int Sec, int Kills, int Wins = 0, int Clears = 0, string? Last = null, Dictionary<string, int>? Best = null);
    public string? SaveState() => bestSec > 0 || bestKills > 0 || lastWeapon != null
        ? JsonSerializer.Serialize(new Record(bestSec, bestKills, wins, clears, lastWeapon is { } w ? Arm.Name(w) : null, best.Count > 0 ? best : null)) : null;

    public void LoadState(string? json)
    {
        if (json == null) return;
        try
        {
            if (JsonSerializer.Deserialize<Record>(json) is not { } r) return;
            (bestSec, bestKills, wins, clears) = (r.Sec, r.Kills, r.Wins, r.Clears);
            lastWeapon = Arm.Find(r.Last, out var last) ? last : null;   // 모르는 무기 이름은 버린다
            best = r.Best != null ? new(r.Best) : new();                 // 무기별 기록의 모르는 이름은 그냥 둔다
        }
        catch (JsonException) { /* 깨진 기록은 버린다 */ }
    }
}
