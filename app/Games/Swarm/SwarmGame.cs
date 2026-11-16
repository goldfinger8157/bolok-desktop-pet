using System.Text.Json;

namespace Bolok;

// 버그 떼 모듈: 앱에 붙이기(판·조작·볼록이), 그리기, 기록, 디버그 명령. 규칙은 Swarm.cs, 무기 값은 SwarmArms.cs, 무기 그림은 SwarmArt.cs, 자가 검사·장면·속도는 SwarmCheck.cs
// 판은 볼록이를 가운데 두고 220×150 칸(배율 3 이면 660×450)만 쓴다. 모서리 표시만 그리고 바탕은 작업 화면 그대로
// 조작: 방향키로 움직이기만 한다 (판이 앞 창일 때만 읽는다). 무기는 자동 (판마다 하나를 무작위로 받는다). Esc 끝. 본체 창은 숨기고 작은 볼록이를 판 위에 그린다
// 표시·효과·소리·조작은 공통 도구(app/Games/Kit)를 쓴다
// 단계 (docs/swarm-levels.md): 5:00 을 버티면 다음 판은 한 단계 어려워진다. 시작 깃발·기록 줄 깃발·결과 화면 깃발(↑)과 왕관 색이 깬 가장 높은 단계(등급)를 보여 준다
sealed partial class SwarmGame : IGameMode
{
    readonly IGameHost host;
    Swarm? swarm;
    Session? ses;
    int bestSec, bestKills, wins, clears;      // 최고 기록, 5:00 을 버틴 판 수, 보스 넷을 다 잡고 버틴 판 수
    Arm.Id? lastWeapon, pick;                  // 지난 판 무기 (다음 판에 같은 무기를 안 뽑는다), 디버그로 정한 다음 판 무기
    Dictionary<string, int> best = new();      // 무기 이름 → 그 무기로 버틴 최고 초
    int stage = 1, maxStage;                   // 다음 판에 할 단계, 깬 가장 높은 단계 (0 = 아직 없음)
    Dictionary<string, int> stageBest = new(); // 무기 이름 → 그 무기로 깬 가장 높은 단계
    int? forceStage;                           // 디버그로 정한 다음 판 단계
    bool auto, practice;                       // practice: 디버그로 단계를 정한 판 (자동 조종 판처럼 기록·단계에 안 남는다)
    Watch hud, bossHud, rushHud, flagHud;
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
        (practice, int play) = (forceStage != null, forceStage ?? stage);
        forceStage = null;
        Begin(new Swarm(field, k, new Random(), weapon, play));
        ses = FakeOpen?.Invoke(field) ?? Session.Open(host, field, () => Stop(true));
        ses.Guide("note.swarm_move", "note.swarm_auto", "note.esc");
        b.StartPuppet();
        host.ShowPet(false);
        Dbg.Log("mode-start", ("id", Id), ("field", field.ToString()), ("weapon", Arm.Name(weapon)), ("stage", play));
    }

    // 새 판을 건다 (처음이거나, 하는 중에 디버그로 무기를 바꿔 같은 자리에서 다시)
    void Begin(Swarm s)
    {
        swarm = s;
        s.OnEvent = e =>
        {
            if (e == "fire") Snd.Play(FireSound[(int)s.Weapon.Which]);
            else Snd.Play(Sounds, e);
            if (e is "level" or "hurt" or "big" or "bigkill" or "magnet" or "shoe" or "over" or "win" or "rush")
                Dbg.Log("swarm", ("event", e), ("stage", s.Stage), ("sec", (int)s.Time), ("weapon", Arm.Name(s.Weapon.Which)), ("level", s.Level), ("kills", s.Kills), ("hp", s.Hp), ("foes", s.Foes.Count), ("shoes", s.Shoes), ("magnets", s.Magnets), ("boss", s.BossesKilled));
        };
        hud = bossHud = rushHud = flagHud = default;
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
        if (!auto) Keep(s, quit);
        ses?.Close(host.Pet, quit, s.Won || s.Time >= 100, "swarm", ("stage", s.Stage), ("sec", (int)s.Time), ("kills", s.Kills), ("level", s.Level), ("weapon", Arm.Name(s.Weapon.Which)), ("boss", s.BossesKilled), ("esc", quit));
        ses = null;
        host.ShowPet(true);
        Dbg.Log("mode-end", ("id", Id));
        auto = practice = false;
        host.Save();
    }

    // 기록: 최고 시간·마릿수, 버틴 판·다 잡은 판, 무기별 최고 초, 지난 판 무기. 5:00 을 버텼으면 깬 단계(왕관 색)·무기별 최고 단계, 그 판이 지금 단계였으면 다음 단계로.
    // 디버그로 단계를 정한 판(practice)은 아무것도 안 남기고, 보스 키(Esc)로 끝낸 판은 단계를 안 바꾼다
    void Keep(Swarm s, bool quit = false)
    {
        if (practice) return;
        string name = Arm.Name(s.Weapon.Which);
        (bestSec, bestKills, lastWeapon) = (Math.Max(bestSec, (int)s.Time), Math.Max(bestKills, s.Kills), s.Weapon.Which);
        if (s.Won) wins++;
        if (s.Cleared) clears++;
        best[name] = Math.Max(best.GetValueOrDefault(name), (int)s.Time);
        if (!s.Won) return;   // 5:00 을 버텼으면 결과 화면에서 Esc 를 눌러도 단계는 오른다 (Esc 로 판 도중에 끝내면 Won 이 아니다)
        maxStage = Math.Max(maxStage, s.Stage);
        stageBest[name] = Math.Max(stageBest.GetValueOrDefault(name), s.Stage);
        if (s.Stage == stage) stage = Math.Min(MaxStageNo, s.Stage + 1);
    }

    const int MaxStageNo = 999;
    int Honor(Swarm s) => Math.Max(maxStage, s.Won && !practice && !auto ? s.Stage : 0);          // 등급을 정하는 단계: 깬 가장 높은 단계 (방금 이긴 판 포함)
    bool Advances(Swarm s) => s.Won && !practice && !auto && s.Stage == stage;                     // 이 판을 이겨 다음 단계로 올라간다

    // ── 그리기: 판 모서리, 보석·아이템, 벌레·보스, 탄·불길, 칼 자국, 터짐, 작은 볼록이와 손에 든 무기, 표시 (글자 없이 숫자·그림만) ──
    static bool Ended(Swarm s) => s.State is Swarm.Phase.Win or Swarm.Phase.Over;
    static int SecLeft(Swarm s) => (int)Math.Ceiling(Swarm.WinSec - s.Time);
    const double IntroSec = 1.5, RingSec = .6, FeetBarSec = 1.2, FlakeSec = .35;

    // 자주 찍는 그림은 한 번만 찾아 둔다 (개발 중 그림이 바뀌면 다시)
    Sprites? artOf;
    int artK;
    readonly Stamp[][] foe = new Stamp[4][];   // Swarm.Kind 순서
    readonly Stamp[][] elite = new Stamp[3][]; // 엘리트(금색 테두리): 보통·말벌·딱정벌레
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
        for (int kind = 0; kind < elite.Length; kind++)
        {
            elite[kind] = new Stamp[foe[kind].Length];
            for (int i = 0; i < elite[kind].Length; i++) elite[kind][i] = SwarmArt.Elite(foe[kind][i], kind * 2 + i, small);
        }
    }

    Stamp FoeArt(Swarm s, Swarm.Foe f)
    {
        var row = f.Tier >= Swarm.Bosses ? queen : f.Elite ? elite[(int)f.Kind] : foe[(int)f.Kind];
        return row[((int)(s.Clock * (f.Kind == Swarm.Kind.Wasp ? 12 : 5)) + (int)f.Speed) % row.Length];
    }

    // 벌레가 그려지는 자리: 돌진 예고로 떨 때는 좌우로 한 칸씩, 여왕은 머리 위 작은 왕관까지
    static int FoeX(Swarm s, Swarm.Foe f) => (int)f.X + (s.Shaking(f) ? (((int)(s.Clock * 30) & 1) == 0 ? 1 : -1) * GameHud.Small(s.K) : 0);
    static int CrownY(Swarm s, Swarm.Foe f) => (int)(f.Y - f.R) - 3 * GameHud.Small(s.K);
    bool Sparkle(Swarm s, Swarm.Foe f) => f.Elite && ((((int)(s.Clock * 4)) + (int)f.Speed) & 1) == 0;   // 엘리트는 머리 위 반짝이가 깜빡
    Rectangle FoeRect(Swarm s, Swarm.Foe f)
    {
        int small = GameHud.Small(s.K), x = FoeX(s, f);
        var r = Rectangle.Inflate(FoeArt(s, f).Box(x, (int)f.Y), 2 + small, 2 + small);
        if (f.Elite) return Rectangle.Union(r, Rectangle.Inflate(spark.Box(x, CrownY(s, f)), 1, 1));
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
    // 단계 표시: 기록 줄은 남은 시간 바로 아래(작은 깃발 + 숫자), 시작 1.5초는 위쪽 가운데에 깃발 + 숫자를 크게 (가운데 볼록이·머리 위 무기와 안 겹치게)
    static (int X, int Y) RecordAt(Rectangle f, int k) => (f.Left + 8 * k, f.Top + 16 * k);
    static Rectangle RecordRect(Rectangle f, int k, int digits) { var (x, y) = RecordAt(f, k); int sm = GameHud.Small(k); return new(x - 6 * sm, y - 6 * sm, (9 + 5 * digits + 1) * sm, 12 * sm); }
    static (int X, int Y) StartAt(Rectangle f) => (f.Left + f.Width / 2, f.Top + (int)(f.Height * .27));
    static Rectangle StartRect(Rectangle f, int k, int digits) { var (x, y) = StartAt(f); int big = k + 1; return Overlay.Around(x, y, (7 + 5 * digits / 2 + 2) * big, 7 * big); }
    static Swarm.Foe? Boss(Swarm s) { foreach (var f in s.Foes) if (f.Kind == Swarm.Kind.Boss) return f; return null; }

    // 예고: 들어올 변 안쪽에 빨간 세모 (변 위 u 자리). 보스는 그 자리로, 러시는 변 가운데(.5)
    static (int X, int Y) WarnAt(Rectangle f, int k, int side, double u)
    {
        int along = side < 2 ? f.Top + (int)(u * f.Height) : f.Left + (int)(u * f.Width);
        return side switch { 0 => (f.Left + 8 * k, along), 1 => (f.Right - 8 * k, along), 2 => (along, f.Top + 8 * k), _ => (along, f.Bottom - 8 * k) };
    }

    // 러시 예고 (35~40초): 판 안쪽 가장자리에 흐린 주황 느낌표 여섯이 제각각 깜빡. 첫 물결이 들어올 변 쪽에만 (둘러싸기 Ring 이면 사방).
    // YELL 2026-11-17: 붉은 테두리 띠·붉은 모서리는 너무 눈에 띈다 → 느낌표 시안 2번, 모서리는 원래 색. 자리는 러시마다 한 번 정한다 (rng 를 안 쓴다: 같은 씨 = 같은 판)
    const int Marks = 6;
    readonly (int X, int Y)[] marks = new (int, int)[Marks];
    int marksFor = -1;

    void PlaceMarks(Swarm s)
    {
        int key = (int)(s.Clock / 60) * 64 + s.RushSide * 8 + (int)s.RushShape;
        if (marksFor == key) return;
        marksFor = key;
        var r = new Random(key * 7919 + 17);   // 러시마다 한 번 (프레임마다 만들지 않는다)
        var f = s.Field;
        int k = s.K;
        for (int i = 0; i < Marks; i++)
        {
            int side = s.RushShape == Swarm.Shape.Ring ? r.Next(4) : s.RushSide;
            double u = .1 + .8 * r.NextDouble();
            int inset = (5 + r.Next(9)) * k + (side == 2 ? 22 * k : 0);   // 위 변은 기록 줄 아래로
            marks[i] = side switch
            {
                0 => (f.Left + inset, f.Top + (int)(u * f.Height)),
                1 => (f.Right - inset, f.Top + (int)(u * f.Height)),
                2 => (f.Left + (int)(u * f.Width), f.Top + inset),
                _ => (f.Left + (int)(u * f.Width), f.Bottom - inset),
            };
        }
    }

    // 느낌표마다 다른 박자로 깜빡 (켜진 것의 비트)
    static int MarkMask(Swarm s)
    {
        if (!s.Warning) return 0;
        int mask = 0;
        for (int i = 0; i < Marks; i++) if ((((int)(s.Clock * 3.2 + i * .53)) & 1) == 0) mask |= 1 << i;
        return mask;
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
        if (s.Warning) PlaceMarks(s);
        rushHud.Set((s.Warning ? 1 : 0) | (MarkMask(s) << 1));
        flagHud.Set(SwarmArt.Tier(maxStage) == SwarmArt.RainbowTier ? (int)(s.Clock * 2) % 6 : -1);   // 무지개 깃발은 0.5초마다 색이 바뀐다

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
        if (s.BossWarning) { var (wx, wy) = WarnAt(f0, k, s.BossSide, s.BossU); rs.Add(o.Client(Overlay.Around(wx, wy, 8 * k, 8 * k))); }
        if (rushHud.Changed) // 러시 예고 느낌표: 깜빡일 때만
            for (int i = 0; i < Marks; i++) rs.Add(o.Client(Rectangle.Inflate(SwarmArt.Bang(k).Box(marks[i].X, marks[i].Y), 1, 1)));
        if (s.Clock < IntroSec + .1) rs.Add(o.Client(StartRect(f0, k, GameHud.Digits(s.Stage))));   // 시작 깃발 (사라질 때까지)
        if (flagHud.Changed || hud.Fresh) rs.Add(o.Client(RecordRect(f0, k, GameHud.Digits(s.Stage))));   // 기록 줄 깃발: 처음 한 번 (무지개는 색이 바뀔 때마다)
        if (Ended(s))
        {
            rs.Add(o.Client(ResultFlagRect(s)));                                                       // 결과의 깃발 줄 (↑·반짝이가 바뀐다)
            if (SwarmArt.Tier(Honor(s)) == SwarmArt.RainbowTier) rs.Add(o.Client(GameHud.CenterRect(f0, k)));   // 무지개 왕관
        }
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
        if (s.Warning)   // 러시 예고: 판 안쪽 가장자리의 느낌표가 제각각 깜빡
        {
            int mask = MarkMask(s);
            var bang = SwarmArt.Bang(k);
            for (int i = 0; i < Marks; i++) if ((mask & 1 << i) != 0) bang.Put(o, g, marks[i].X, marks[i].Y);
        }

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
            if (Sparkle(s, f)) spark.Put(o, g, fx, CrownY(s, f));                 // 엘리트는 머리 위 반짝이
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
        if (s.BossWarning && ((int)((s.Clock - s.BossWarnAt) * 6) & 1) == 0) { var (bwx, bwy) = WarnAt(fl, k, s.BossSide, s.BossU); DrawWarn(o, g, k, s.BossSide, bwx, bwy); }

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
        DrawStage(o, g, s);
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

    // 단계 표시: 기록 줄(남은 시간 아래, 작은 깃발 + 숫자: 한 판 내내)과 시작 1.5초의 큰 깃발 + 숫자. 깃발 색은 깬 가장 높은 단계(등급)
    void DrawStage(Overlay o, Graphics g, Swarm s)
    {
        int k = s.K, small = GameHud.Small(k), big = k + 1, tier = SwarmArt.Tier(maxStage), phase = (int)(s.Clock * 2);
        var (rx, ry) = RecordAt(s.Field, k);
        SwarmArt.Flag(tier, phase, small).Put(o, g, rx, ry);
        GameHud.Number(o, g, s.Stage, rx + 9 * small, ry, small, -1);
        if (s.Clock >= IntroSec || s.State != Swarm.Phase.Play) return;
        var (cx, cy) = StartAt(s.Field);
        int digits = GameHud.Digits(s.Stage), w = (12 + 5 * digits) * big, left = cx - w / 2;
        SwarmArt.Flag(tier, phase, big).Put(o, g, left + 5 * big, cy);
        GameHud.Number(o, g, s.Stage, left + 12 * big + 5 * big / 2, cy, big, -1);
    }

    // 결과 화면의 깃발 줄: 상자 바로 아래 (왼쪽부터 깃발, 단계 숫자, 이겨서 올라가면 위 화살표)
    (int X, int Y, int Scale) ResultFlagAt(Swarm s)
    {
        int k = s.K, big = k + 1, small = GameHud.Small(k);
        var fl = s.Field;
        int cy = (int)(fl.Top + fl.Height * .45), row2 = cy + 5 * big + 2 * k + 4 * small;
        bool second = !(s.Kills > bestKills || s.Cleared) && bestKills > 0;   // GameHud.Result 가 최고 기록 줄을 그리는 판
        int bottom = (second ? row2 + 4 * small : cy + 5 * big) + 3 * k;
        return (fl.Left + fl.Width / 2, bottom + 3 * k + 5 * k, k);
    }

    Rectangle ResultFlagRect(Swarm s)
    {
        var (x, y, sc) = ResultFlagAt(s);
        return Overlay.Around(x, y, 26 * sc, 9 * sc);
    }

    // 끝: 벌레 × 잡은 수. 새 기록이거나 보스 넷을 다 잡고 버텼으면 큰 왕관(다 잡았으면 반짝이까지), 5:00 을 버티기만 했으면 상자 위에 작은 왕관.
    // 왕관은 등급 색 (깬 가장 높은 단계: 동·은·금·보라·무지개). 상자 아래에 깃발 + 단계: 이겼으면 0.6초 뒤 위 화살표와 함께 다음 단계로 바뀌며 반짝인다
    void DrawResult(Overlay o, Graphics g, Swarm s)
    {
        int k = s.K, big = k + 1, small = GameHud.Small(k), tier = SwarmArt.Tier(Honor(s)), phase = (int)(s.Clock * 2);
        var fl = s.Field;
        bool top = s.Kills > bestKills || s.Cleared;
        GameHud.Result(o, g, fl, k, s.Kills, bestKills, top, "game/bug");
        int cx = fl.Left + fl.Width / 2, cy = (int)(fl.Top + fl.Height * .45), boxTop = cy - 5 * big - 3 * k;
        if (top && s.Won) SwarmArt.Crown(bigCrown, tier, phase, big).Put(o, g, cx, boxTop - 7 * big);   // 금색 큰 왕관 위에 같은 자리 등급 색
        if (s.Won && !top) SwarmArt.Crown(crown, tier, phase, small).Put(o, g, cx, boxTop - 4 * small);
        var (fx, fy, sc) = ResultFlagAt(s);
        bool up = Advances(s) && s.EndedFor >= .6;
        int shown = up ? s.Stage + 1 : s.Stage, digits = GameHud.Digits(shown), w = (12 + 5 * digits + (up ? 9 : 0)) * sc, left = fx - w / 2;
        SwarmArt.Flag(tier, phase, sc).Put(o, g, left + 5 * sc, fy);
        GameHud.Number(o, g, shown, left + 12 * sc + 5 * sc / 2, fy, sc, -1);
        if (up)
        {
            SwarmArt.Arrow(sc).Put(o, g, left + (14 + 5 * digits) * sc + 4 * sc, fy);
            if (s.EndedFor < 1.4 && ((int)(s.EndedFor * 10) & 1) == 0)   // 올라간 순간 숫자 둘레가 반짝
            {
                spark.Put(o, g, left + (21 + 5 * digits) * sc, fy - 6 * sc);
                spark.Put(o, g, left + 11 * sc, fy + 6 * sc);
            }
        }
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

    // 예고: 판 안쪽을 가리키는 빨간 세모 (갈색 테두리). side: 세모가 서 있는 변
    static void DrawWarn(Overlay o, Graphics g, int k, int side, int x, int y)
    {
        var c = o.ToClient(x, y);
        int ax = side == 0 ? 1 : side == 1 ? -1 : 0, ay = side == 2 ? 1 : side == 3 ? -1 : 0;
        for (int pass = 0; pass < 2; pass++) // 테두리(한 칸 큰 세모) 다음에 속
            for (int i = 0; i < (pass == 0 ? 7 : 5); i++)
            {
                int along = (pass == 0 ? 4 : 3) - i; // 꼭짓점에서 i 칸 뒤의 줄 (폭 2i+1 칸)
                int w = (2 * i + 1) * k, px = c.X + ax * along * k - k / 2, py = c.Y + ay * along * k - k / 2;
                var r = ax != 0 ? new Rectangle(px, c.Y - w / 2, k, w) : new Rectangle(c.X - w / 2, py, w, k);
                g.FillRectangle(pass == 0 ? Pal.Line : Pal.Red, r);
            }
    }

    // 디버그: swarm / swarm weapon <id> / swarm auto [id] (알아서 한다, 기록 안 남김) / swarm at <초> / swarm boss [1~4] / swarm up [n] / swarm hp <n> / swarm item <heart|magnet|shoe> / swarm stop /
    //   swarm level <n> (n 단계로, 하는 중이면 같은 무기로 새 판: 연습 판이라 기록·단계가 안 바뀐다) / swarm level set <n> (저장된 단계를 n 으로, 깬 단계는 n-1) /
    //   swarm rush [wall|ring] (지금 예고를 띄우고 1초 뒤 그 모양으로, 안 주면 차례대로) / swarm elite [bug|wasp|beetle] (볼록이 옆에 엘리트 하나)
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
            case "level":
                if (arg == "set") { if (int.TryParse(a.Length > 3 ? a[3] : "", out int set)) { stage = Math.Clamp(set, 1, MaxStageNo); maxStage = stage - 1; host.Save(); } }
                else if (n >= 1) StartLevel(Math.Min(n, MaxStageNo));
                break;
            case "rush": s?.CallRush(arg switch { "wall" => Swarm.Shape.Wall, "ring" => Swarm.Shape.Ring, _ => null }); break;
            case "elite": s?.CallElite(arg switch { "wasp" => Swarm.Kind.Wasp, "beetle" => Swarm.Kind.Beetle, _ => Swarm.Kind.Bug }); break;
            case "up": for (int i = 0; s != null && i < Math.Clamp(n, 1, 60); i++) s.LevelUp(); break;
            case "hp": if (s != null && n > 0) { s.Hp = Math.Min(Swarm.MaxHp, n); s.Heal(0); } break;
            case "item":
                if (s != null && arg is "heart" or "magnet" or "shoe") s.Drop(arg == "heart" ? Swarm.Item.Heart : arg == "magnet" ? Swarm.Item.Magnet : Swarm.Item.Shoe);
                break;
            default: StartWith(null); break;
        }
        return true;
    }

    // 그 무기로 시작한다 (null 이면 무작위). 하는 중이면 그 무기로 같은 자리에서 새 판 (단계는 그대로)
    void StartWith(Arm.Id? weapon)
    {
        if (swarm is { } s) { if (weapon is { } w) Begin(new Swarm(s.Field, s.K, new Random(), w, s.Stage)); }
        else if (!host.Busy) { pick = weapon; Start(); }
    }

    // n 단계 연습 판: 하는 중이면 같은 자리·같은 무기로 새 판, 아니면 무작위 무기로 시작. 기록·단계는 안 바뀐다
    void StartLevel(int n)
    {
        if (swarm is { } s) { practice = true; Begin(new Swarm(s.Field, s.K, new Random(), s.Weapon.Which, n)); }
        else if (!host.Busy) { (pick, forceStage) = (null, n); Start(); }
    }

    public IEnumerable<GameStat> Stats()
    {
        bool played = bestSec > 0 || bestKills > 0;
        yield return new GameStat(L.T("game.swarm"), played ? L.T("game.swarm_val2", $"{bestSec / 60}:{bestSec % 60:00}", bestKills, stage) : L.T("game.never"), played);
    }

    // 기록. 옛 기록 {"Sec":120,"Kills":300} 도 그대로 읽힌다 (없는 칸은 0·null). 무기는 enum 번호가 아니라 글 이름으로 (무기를 넣고 빼도 안 깨진다)
    // 단계 칸(Stage·MaxStage·StageBest)은 옛 기록에 없으면 0·null → 1단계부터. 옛 Wins 는 쉬운 규칙에서 딴 것이라 단계로 바꿔 주지 않는다
    sealed record Record(int Sec, int Kills, int Wins = 0, int Clears = 0, string? Last = null, Dictionary<string, int>? Best = null,
        int Stage = 0, int MaxStage = 0, Dictionary<string, int>? StageBest = null);
    public string? SaveState() => bestSec > 0 || bestKills > 0 || lastWeapon != null || stage > 1 || maxStage > 0
        ? JsonSerializer.Serialize(new Record(bestSec, bestKills, wins, clears, lastWeapon is { } w ? Arm.Name(w) : null, best.Count > 0 ? best : null, stage, maxStage, stageBest.Count > 0 ? stageBest : null)) : null;

    public void LoadState(string? json)
    {
        if (json == null) return;
        try
        {
            if (JsonSerializer.Deserialize<Record>(json) is not { } r) return;
            (bestSec, bestKills, wins, clears) = (r.Sec, r.Kills, r.Wins, r.Clears);
            lastWeapon = Arm.Find(r.Last, out var last) ? last : null;   // 모르는 무기 이름은 버린다
            best = r.Best != null ? new(r.Best) : new();                 // 무기별 기록의 모르는 이름은 그냥 둔다
            (stage, maxStage) = (Math.Clamp(r.Stage, 1, MaxStageNo), Math.Clamp(r.MaxStage, 0, MaxStageNo));
            stageBest = r.StageBest != null ? new(r.StageBest) : new();
        }
        catch (JsonException) { /* 깨진 기록은 버린다 */ }
    }
}
