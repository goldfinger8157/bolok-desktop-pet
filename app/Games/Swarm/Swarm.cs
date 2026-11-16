namespace Bolok;

// 버그 떼 (YELL 2026-11-13: 사방에서 몰려오는 적을 물리치며 버티는 게임. 이름·그림·장치는 우리 것):
// 볼록이 둘레 작은 판에서 작은 볼록이가 방향키로 다닌다. 벌레가 네 변에서 줄줄이 들어와 볼록이에게 걸어오고, 갈수록 많고 단단해진다.
// 판마다 무기 하나를 무작위로 받는다 (YELL 2026-11-16, 무기 값은 SwarmArms.cs). 무기는 알아서 친다. 잡으면 보석이 떨어지고, 주우면 레벨이 올라 무기의 한 갈래가 자란다.
// 생명은 게이지 100: 닿은 벌레 종류만큼 깎이고 1초 동안은 안 맞는다. 하트를 주우면 30 이 찬다.
// 자석은 영구 아이템: 주울 때마다 보석을 줍는 둘레가 넓어진다 (다섯 개까지). 신발은 그 판 내내 걸음이 빨라진다, 세 켤레까지.
// 보스는 1분마다 넷(마지막 4:00 은 여왕). 5:00 이 되면 끝·승리. 화면과 무관한 규칙이라 --selfcheck 로 검사한다
// 난이도 (YELL 2026-11-16, 계획 docs/swarm-levels.md): 판 안은 1분 주기(잔잔 → 예고 → 러시 → 숨 고르기 → 보스, 마지막 분은 여왕 + 마무리 러시),
// 판 밖은 5:00 을 버틸 때마다 +1 되는 「단계」(Stage). 단계가 오르면 체력·수·빠르기·엘리트가 오른다 (값은 Tune.Of)
sealed class Swarm
{
    public enum Phase { Play, Win, Over, Done }
    public enum Kind { Bug, Wasp, Beetle, Boss }   // 보통, 빠른 말벌, 느리고 단단한 딱정벌레, 1분마다 오는 큰 놈
    public enum Up { Fast, Wide, Power, Heal }     // 무기의 세 갈래(빠르게·넓게·세게)와 생명
    public enum Item { Gem, Heart, Magnet, Shoe }  // 판에 떨어지는 것: 보석, 하트, 자석(줍는 둘레가 넓어진다), 신발(빨라진다)
    public enum Ammo { Bullet, Pellet, Round, Rocket }   // 날아가는 것: 권총알, 산탄 알, 소총탄, 로켓

    public sealed class Foe
    {
        public Kind Kind;
        public int Tier, Hp, MaxHp;                 // Tier: 보스 차례 1~4 (4 = 여왕)
        public double X, Y, R, Speed, Hit = -9, StunUntil = -9, BurnUntil = -9, BurnTick;   // Hit 은 번쩍임 전용, 멈칫은 StunUntil
        public double DashAt = 1e9, DashX, DashY, SpawnDue = 1e9;   // 돌진(3번 보스·여왕): 떨기 시작하는 때와 방향. 여왕이 벌레를 낳는 때
        public bool Winding, Dead;
        public bool Elite;                          // 엘리트 (2단계부터): 체력 ×2.5, 닿는 피해 ×1.5, 걸음 ×0.9. 금색 테두리
    }

    public struct Shot
    {
        public double X, Y, Vx, Vy, Until, Knock, Stun, Blast, Tx, Ty;
        public int Dmg, Pierce;
        public Ammo Ammo;
        Foe? hit0, hit1, hit2, hit3;   // 이 탄이 이미 뚫고 지나간 벌레 (뚫는 탄이 같은 놈을 두 번 치지 않게. 관통은 넷까지)
        public readonly bool Seen(Foe f) => ReferenceEquals(f, hit0) || ReferenceEquals(f, hit1) || ReferenceEquals(f, hit2) || ReferenceEquals(f, hit3);
        public void Mark(Foe f) => (hit0, hit1, hit2, hit3) = (f, hit0, hit1, hit2);
    }
    public sealed class Gem { public Item Item; public double X, Y; public int Xp; public bool Pull; public double ShownX = double.NaN, ShownY; } // Pull: 보스를 잡아 둘레 밖에서도 끌려온다, Shown: 그리는 쪽이 마지막으로 그린 자리
    public sealed class Pop { public double X, Y, At, R; }

    // 배율 3 기준 px·초. 판은 앱이 볼록이 둘레에 220×150 칸(배율 3 이면 660×450)으로 잡는다
    public const double WinSec = 300, MoveSpeed = 170, PickRange = 56, SafeSec = 1, SwingSec = .2, EndSec = 3.5, HurtPush = 60, HpTrailSec = .4,
        BossEvery = 60, BossWarn = 2,                                        // 보스: 1분마다, 2초 전에 들어올 자리를 알린다
        DashEvery = 5, DashWind = .6, DashSec = .5, DashSpeed = 260, QueenEvery = 6,   // 돌진: 5초마다 0.6초 떨다가 0.5초 내달린다. 여왕은 6초마다 벌레 넷
        MagnetStep = 22, MagnetFirst = 20, MagnetGap = 35, PullSpeed = 420,  // 자석: 하나마다 넓어지는 둘레, 처음 나올 수 있는 때, 주운 뒤 쉬는 시간, 끌려오는 빠르기
        ShoeBoost = .15, ShoeFirst = 15, ShoeGap = 30,                       // 신발: 한 켤레에 빨라지는 만큼, 처음 나올 수 있는 때, 하나 떨어진 뒤 다음 것까지 쉬는 시간
        FlameTurn = 200 * Math.PI / 180, BurnEvery = .5;   // 화염 겨눔이 도는 빠르기, 불붙은 벌레가 타는 간격
    public const int MaxHp = 100, HeartHp = 30, MaxFoes = 110, FinaleCap = 90, MaxGems = 150, MaxShoes = 3, MaxMagnets = 5, MaxShots = 200, Bosses = 4;   // MaxFoes: 한 화면 상한 (여왕이 살아 있는 마무리는 FinaleCap)

    // ── 1분 주기 (분 안의 초): 잔잔 0~35 · 예고 35~40 · 러시 40~55(물결 5번, 3초마다) · 숨 고르기 55~60 · 보스 60.
    //    마지막 분은 4:00 여왕 → 4:35 예고 → 4:40~5:00 마무리 러시(물결 5번, 4초마다) ──
    public const double WarnFrom = 35, RushFrom = 40, RushTo = 55, WaveGap = 3, FinaleGap = 4, FinaleFrom = 280, RushLen = 15,
        BaseFrom = 1.4, BaseGrow = 8.2,           // 잔잔 기준 초당 수 base1(t) = 1.4 + 8.2·t/300 (손잡이 ②: 계획은 3.4, 봇 표에 맞춰 8.2)
        CalmWasp = .28, CalmBeetle1 = .16, CalmBeetle2 = .24, RushWasp = .30, RushBeetle = .10,   // 구성: 말벌은 20초부터, 딱정벌레는 60·180초부터
        RingHole = .25, FinaleHole = .33, WallGap = 26, WallRow = 18, KeepOut = 150;       // 대열: 구멍 25%(마무리 33%), 한쪽 줄 간격·줄 사이, 볼록이 둘레 비우는 거리
    public const int Waves = 5;
    public const double RushHp = .5, RushWalk = 1.3;   // 러시 벌레 체력·걸음 배율 (YELL 검토 2026-11-16: 러시가 잔잔보다 2.5배 넘게 몰려와야 「러시」로 느껴진다)
    // 엘리트 (2단계부터, 30초 뒤부터): 체력·피해·걸음 배율, 보석, 하트, 동시에 나올 수 있는 수
    public const double EliteFrom = 30, EliteHp = 2.5, EliteTouch = 1.5, EliteWalk = .9, GemBoost = 1;
    public const int EliteGems = 5, EliteBeetleGems = 8;
    public static int EliteMax(int stage) => stage >= 10 ? 8 : 6;

    // ── 단계 (2장): 8단계까지는 고르게, 그 뒤로는 체력·수는 거의 멈추고 빠르기·엘리트·모양으로 ──
    public readonly struct Tune
    {
        public const double RushBase = 2.5, RushStep = .1,            // 러시 세기 바탕과 단계마다 (손잡이 ①: 계획 2.2 → 1차 1.0 → 검토 2.5, 러시 벌레를 약하고 빠르게 해서 맞춤)
            HpA = .07, HpB = .02, SpawnA = .06, SpawnB = .015, BossA = .10, BossB = .04, SpeedA = .015, SpeedB = .01, EliteA = .01, EliteB = .015;   // 기울기 (손잡이 ④)
        public readonly double HpX, SpawnX, BossX, SpeedX, EliteP, RushX;   // 벌레 체력, 나오는 수(잔잔만), 보스 체력, 빠르기(걸음만), 엘리트 비율, 러시 세기

        Tune(int stage)
        {
            double a = Math.Min(stage, 8) - 1, b = Math.Max(0, stage - 8);
            HpX = Math.Min(1.8, 1 + HpA * a + HpB * b);
            SpawnX = Math.Min(1.6, 1 + SpawnA * a + SpawnB * b);
            BossX = Math.Min(2.5, 1 + BossA * a + BossB * b);
            SpeedX = Math.Min(1.25, 1 + SpeedA * a + SpeedB * b);
            EliteP = stage <= 1 ? 0 : Math.Min(.20, .03 + EliteA * (a - 1) + EliteB * b);
            RushX = Math.Min(3.0, RushBase + RushStep * (stage - 1));
        }

        public static Tune Of(int stage) => new(Math.Max(1, stage));
    }

    // 1분 안의 때: 잔잔 · 예고 · 러시 · 숨 고르기
    public enum Beat { Calm, Warn, Rush, Rest }
    // 러시 대열 (2차에 말벌 돌진 Dart·양쪽 줄 Pincer·겹침이 더해진다 — ShapesOpen 이 고를 수 있는 가짓수)
    public enum Shape { Wall, Ring }

    // 보스 네 차례 (1:00 · 2:00 · 3:00 · 4:00 여왕): 체력(1단계), 빠르기, 남기는 보석
    static readonly int[] BossHp = { 400, 800, 1250, 1900 }, BossGems = { 15, 20, 25, 30 };
    static readonly double[] BossSpeed = { 44, 48, 52, 56 };

    public readonly Rectangle Field;
    public readonly int K;
    public readonly int Stage;                     // 이번 판 단계 (1부터)
    public readonly Tune T;                        // 그 단계의 값
    public readonly Arm Weapon;                    // 이번 판 무기 (지금 값)
    public readonly List<Foe> Foes = new();
    public readonly Bag<Shot> Shots = new(64);
    public readonly List<Gem> Gems = new();
    public readonly List<Pop> Pops = new();
    public readonly List<Gem> Gone = new();        // 이번 프레임에 사라진 보석 (그리는 쪽이 그 자리를 지운다)
    public readonly int[] FromSide = new int[4];   // 변마다 들어온 수 (왼쪽·오른쪽·위·아래)
    public double X, Y, Clock, SafeUntil, HurtAt = -9, FireAt = -9, UpAt = -9, BoomAt = -9;
    public double SwingAt = -9, SwingDir, SwingArc, SwingLen = SwingSec;   // 마지막 휘두름: 때, 쪽, 부채꼴 절반 각, 자국이 보이는 시간
    public int SwingHand = -1, SwingCount;         // 쌍검: 이번에 휘두른 손(1 오른쪽·-1 왼쪽), 휘두른 횟수
    public bool SwingSpin;                         // 이번 휘두름이 한 바퀴 회전 베기였다
    public double AimAngle;                        // 화염: 겨눈 쪽 (천천히 돈다)
    public bool FlameOn;                           // 불길을 뿜는 중 (닿는 거리 안에 벌레가 있다)
    public double MagnetChance = .03, ShoeChance = .05, HeartChance = .04;   // 벌레를 잡을 때 자석·신발이, 딱정벌레를 잡을 때 하트가 나올 확률
    public int Hp = MaxHp, HpFrom = MaxHp, Level = 1, Xp, Kills, Dir = 1, AimDir = 1, Magnets, Shoes, BossesKilled, DamageTaken, Born, Level180 = -1;   // Born: 지금까지 나온 벌레 수, Level180: 3:00 때 레벨
    public double HpAt = -9, MagnetAt = -9;        // 생명이 마지막으로 바뀐 때 (HpFrom: 그 전 값, 잠깐 흰 잔상), 자석을 주운 때 (새 둘레를 잠깐 보여 준다)
    public double BossWarnAt = -9, BossU, FirstBossSec = -1;   // 보스 예고를 시작한 때와 들어올 자리(변 위 0~1), 첫 보스를 잡는 데 걸린 초
    public int BossSide, BossTier;                 // 예고 중인 보스가 들어올 변(왼쪽·오른쪽·위·아래)과 차례
    public Shape RushShape;                        // 예고 중인(또는 방금 들어온) 러시 물결의 모양과 한쪽 줄이 들어올 변, 구멍 자리(둘레 0~1)
    public int RushSide;
    public double RushHole;
    public double EliteHeartChance = .2;           // 엘리트를 잡을 때 하트가 나올 확률
    public Item LastItem;                          // 마지막으로 주운 자석·신발과 그때 (머리 위에 잠깐 뜬다)
    public double ItemAt = -9;
    public Up? LastUp;
    public bool Spawning = true;                   // 자가 검사에서 끄고 벌레를 직접 놓는다
    public Phase State = Phase.Play;
    public Action<string>? OnEvent;
    public bool Done => State == Phase.Done;
    public bool Moving { get; private set; }
    public bool Won => State is Phase.Win || State == Phase.Done && endAt >= WinSec;   // 5:00 을 버텼다
    public bool Cleared => Won && BossesKilled >= Bosses;                              // 보스 넷을 다 잡고 버텼다
    public bool BossWarning => State == Phase.Play && bossWarned > bossSeen && Clock - BossWarnAt < BossWarn + .1;
    public double Speed => MoveSpeed * (1 + ShoeBoost * Shoes);                     // 신발 한 켤레에 15% 씩
    public double Time => State == Phase.Play ? Clock : endAt;   // 버틴 시간
    public int Need => 2 + 4 * Level;                            // 다음 레벨까지 보석
    public double EndedFor => State == Phase.Play ? 0 : Clock - endAt; // 끝난 지

    // 2차에 풀리는 것의 열쇠 (단계 n 부터): 엘리트 2, 말벌 돌진 3, 양쪽 줄 4, 강철 딱정벌레 5, 모든 보스 돌진 6, 보스 둘 7, 겹친 대열 10
    public bool Elites => Stage >= 2;
    public int ShapesOpen => 2;                    // 고를 수 있는 대열 가짓수 (지금 Wall·Ring)
    public bool Rushing => Rhythm == Beat.Rush;
    public bool Warning => State == Phase.Play && (Rhythm == Beat.Warn || forceAt < 1e8);   // 예고 5초 (디버그로 부른 러시는 1초)

    // 1분 안의 때 (판이 끝났으면 늘 잔잔)
    public Beat Rhythm
    {
        get
        {
            if (State != Phase.Play) return Beat.Calm;
            double s = Clock - 60 * MinuteOf(Clock);
            return s < WarnFrom ? Beat.Calm : s < RushFrom ? Beat.Warn : s < RushTo || Clock >= 240 ? Beat.Rush : Beat.Rest;
        }
    }

    static int MinuteOf(double clock) => Math.Min(4, (int)(clock / 60));
    public double Base1(double t) => BaseFrom + BaseGrow * t / WinSec;    // 잔잔 기준 초당 수
    public int RushN(int minute) => (int)Math.Round(RushLen * Base1(RushFrom + 60 * Math.Clamp(minute, 0, 3)) * T.RushX, MidpointRounding.AwayFromZero);   // 그 분(0~3) 러시 마릿수 (spawnX 를 곱하지 않는다)

    readonly Random rng;
    readonly double sf;
    double endAt, spawnDue, cool;   // cool: 다음 공격까지 남은 시간 (쏠 때 남은 자투리를 이어 간다: 쿨이 프레임 길이에 맞춰 잘리지 않게)
    double steerX, steerY, magnetFrom = MagnetFirst, fireEvAt = -9, bossSince;
    int magnetsOut, shoesOut, bossWarned = -1, bossSeen = -1;   // 판 위에 떨어져 있는 자석·신발, 예고한·들어온 보스 차례
    double shoeFrom = ShoeFirst, forceAt = 1e9;
    int rushMin = -1, previewMin = -1, waveNext, rushTotal, carry, rushes, shapeRun, called;   // 러시: 그 분·미리 정한 분, 다음 물결, 마릿수, 상한에 막혀 넘어온 수, 지금까지 시작한 러시, 같은 모양이 이어진 수
    Shape lastShape, forced;

    // weapon 이 null 이면 rng 로 뽑는다. 뽑는 rng.Next 는 맨 처음 한 번, 무기를 정해 줘도 똑같이 쓴다 (씨가 같으면 같은 판)
    // stage 는 rng 를 쓰지 않는다 (씨가 같으면 단계가 달라도 같은 무기)
    public Swarm(Rectangle field, int k, Random rng, Arm.Id? weapon = null, int stage = 1)
    {
        var drawn = (Arm.Id)rng.Next(Arm.Kinds);
        (Field, K, this.rng, Weapon) = (field, k, rng, Arm.New(weapon ?? drawn));
        (Stage, T) = (Math.Max(1, stage), Tune.Of(stage));
        sf = k / 3.0;
        (X, Y) = (field.Left + field.Width / 2.0, field.Top + field.Height / 2.0);
    }

    public double Feet => Y + 20 * sf;             // (X, Y) 는 몸 가운데
    public double BodyR => 9 * sf;
    public double RangePx => Weapon.Range * sf;
    public double PickPx => (PickRange + MagnetStep * Magnets) * sf;   // 보석이 끌려오기 시작하는 둘레
    public double FoeR(Foe f) => f.R;
    public static int TouchDmg(Foe f)
    {
        int dmg = f.Kind switch { Kind.Boss => f.Tier >= Bosses ? 40 : 30, Kind.Beetle => 18, Kind.Wasp => 8, _ => 10 };
        return f.Elite ? (int)Math.Round(dmg * EliteTouch, MidpointRounding.AwayFromZero) : dmg;
    }
    public bool Shaking(Foe f) => f.Winding && Clock - f.DashAt < DashWind;   // 돌진 예고: 멈춰서 떤다
    double Dist(Foe f) => Dist(f.X, f.Y, X, Y);
    static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    public void Steer(double dx, double dy) => (steerX, steerY) = (dx, dy);
    (double, double) Inside(double x, double y) => (Math.Clamp(x, Field.Left + 12 * sf, Field.Right - 12 * sf), Math.Clamp(y, Field.Top + 26 * sf, Field.Bottom - 22 * sf));

    public void Update(double dt)
    {
        Clock += dt;
        Gone.Clear();
        for (int i = Pops.Count - 1; i >= 0; i--) if (Clock - Pops[i].At > BlastArt.Sec) Pops.RemoveAt(i);
        if (State != Phase.Play)
        {
            if (State != Phase.Done && Clock - endAt >= EndSec) State = Phase.Done;
            return;
        }
        double len = Math.Sqrt(steerX * steerX + steerY * steerY);
        Moving = len > .01;
        if (Moving)
        {
            double step = Speed * sf * dt / len;
            (X, Y) = Inside(X + steerX * step, Y + steerY * step);
            if (Math.Abs(steerX) > .01) Dir = steerX > 0 ? 1 : -1;
        }
        Spawn(dt);
        MoveFoes(dt);
        Attack(dt);
        MoveShots(dt);
        Burn();
        Sweep();
        Touch();
        if (State != Phase.Play) return;
        Pick(dt);
        if (Level180 < 0 && Clock >= 180) Level180 = Level;
        if (Clock >= WinSec) // 끝까지 버텼다: 남은 벌레·보스는 다 터진다
        {
            foreach (var f in Foes) Pops.Add(new Pop { X = f.X, Y = f.Y, At = Clock, R = f.R * 1.5 });
            Foes.Clear();
            Shots.Trim(0);
            FlameOn = false;
            Go(Phase.Win, "win");
        }
    }

    void Go(Phase p, string ev) { (State, endAt, Moving, FlameOn) = (p, Math.Min(Clock, WinSec), false, false); OnEvent?.Invoke(ev); }

    // ── 벌레: 1분 주기로 나온다 (잔잔 기준 초당 수는 base1: 1.4 → 9.6). 잔잔(낱마리) → 예고 → 러시(물결 5번, 대열로) → 숨 고르기 → 보스. 마지막 분은 여왕 → 마무리 러시 ──
    void Spawn(double dt)
    {
        int m = MinuteOf(Clock);
        double s = Clock - 60 * m;
        bool finale = Clock >= FinaleFrom;
        for (int i = 0, n = Foes.Count; i < n; i++) // 여왕은 6초마다 제 둘레에 벌레 넷을 낳는다 (마무리에는 낳지 않는다)
        {
            var q = Foes[i];
            if (Clock < q.SpawnDue) continue;
            q.SpawnDue = Clock + QueenEvery;
            if (finale) continue;
            for (int j = 0; j < 4 && Foes.Count < MaxFoes + 4; j++) Add(Kind.Bug, q.X + (j % 2 * 2 - 1) * (q.R + 8 * sf), q.Y + (j / 2 * 2 - 1) * (q.R + 8 * sf));
        }
        if (!Spawning) return;
        int cap = Cap();
        // 낱마리: 잔잔 base1 × spawnX, 예고 ×0.5, 러시 0, 숨 고르기 ×0.1 (spawnX 없이). 마지막 분: 여왕 ×0.6, 예고 ×0.3, 마무리 0
        double mul = m < 4 ? (s < WarnFrom ? T.SpawnX : s < RushFrom ? .5 * T.SpawnX : s < RushTo ? 0 : .1) : s < WarnFrom ? .6 * T.SpawnX : s < RushFrom ? .3 * T.SpawnX : 0;
        spawnDue += dt * mul * Base1(Clock);
        for (; spawnDue >= 1; spawnDue--)
            if (Foes.Count < cap)
            {
                double pick = rng.NextDouble(), beetle = Clock >= 180 ? CalmBeetle2 : Clock >= 60 ? CalmBeetle1 : 0;
                Elitify(Add(pick < beetle ? Kind.Beetle : Clock >= 20 && pick < beetle + CalmWasp ? Kind.Wasp : Kind.Bug));
            }
        Rush(m, s, cap);
        // 보스: 2초 전에 들어올 자리를 정해 알리고(가장자리에 빨간 세모), 제때 그 자리로 들어온다. 판을 중간부터 시작하면 이미 지난 차례는 건너뛴다
        if (bossSeen < 0) bossSeen = bossWarned = Math.Min(Bosses, (int)(Clock / BossEvery));
        int due = Math.Min(Bosses, (int)((Clock + BossWarn) / BossEvery));
        if (due > bossWarned)
        {
            (bossWarned, BossTier, BossWarnAt, BossSide, BossU) = (due, due, Clock, rng.Next(4), .2 + .6 * rng.NextDouble());
            OnEvent?.Invoke("big");
        }
        if (bossWarned > bossSeen && Clock >= bossWarned * BossEvery)
        {
            bossSeen = bossWarned;
            AddBoss(bossSeen, BossSide, BossU);
        }
    }

    // 한 화면 상한: 여왕이 살아 있는 마무리는 90, 아니면 110
    int Cap()
    {
        if (Clock >= FinaleFrom) foreach (var f in Foes) if (f.Kind == Kind.Boss && f.Tier >= Bosses) return FinaleCap;
        return MaxFoes;
    }

    bool BossAlive() { foreach (var f in Foes) if (f.Kind == Kind.Boss) return true; return false; }
    bool QueenAlive() { foreach (var f in Foes) if (f.Kind == Kind.Boss && f.Tier >= Bosses) return true; return false; }

    // ── 러시: 35초에 첫 물결의 모양·변을 미리 정해 알리고, 40초부터 물결 다섯 번 (마지막 분은 40·44·48·52·56) ──
    void Rush(int m, double s, int cap)
    {
        double gap = m == 4 ? FinaleGap : WaveGap;
        if (m != rushMin) // 새 분: 물결 차례를 처음(판을 중간부터 시작하면 이미 지난 물결은 건너뛴다)으로
        {
            rushMin = m;
            (waveNext, rushTotal, carry) = (s >= RushFrom ? Math.Clamp((int)Math.Ceiling((s - .1 - RushFrom) / gap), 0, Waves) : 0, -1, 0);   // 0.1초 안에 닿은 물결은 건너뛰지 않는다
        }
        if (forceAt < 1e8 && Clock >= forceAt) // 디버그: 불러 둔 러시
        {
            forceAt = 1e9;
            (RushShape, RushSide, RushHole) = (forced, rng.Next(4), rng.NextDouble());
            Wave(forced, Math.Max(1, RushN(Math.Min(m, 3)) / Waves), RushSide, RushHole, RingHole, cap);
            OnEvent?.Invoke("rush");
        }
        if (s >= WarnFrom && previewMin != m && waveNext == 0) Preview(m);
        if (s < RushFrom || waveNext >= Waves) return;
        while (waveNext < Waves && s >= RushFrom + gap * waveNext)
        {
            if (rushTotal < 0) // 러시가 시작됐다: 마릿수를 정한다 (시작 때 보스가 살아 있으면 ×0.75, 마무리는 여왕이 살아 있으면 ×0.7)
            {
                if (previewMin != m) Preview(m);
                rushTotal = (int)Math.Round(RushN(m == 4 ? 3 : m) * (m == 4 ? QueenAlive() ? .7 : 1 : BossAlive() ? .75 : 1), MidpointRounding.AwayFromZero);
                rushes++;
                OnEvent?.Invoke("rush");
            }
            else if (waveNext > 0) (RushShape, RushSide, RushHole) = (ChooseShape(false), rng.Next(4), rng.NextDouble());
            int share = rushTotal * (waveNext + 1) / Waves - rushTotal * waveNext / Waves;
            double hole = m == 4 ? FinaleHole : RingHole, at = RushHole;
            if (m == 4 && QueenAlive()) at = OppositeQueen(at);   // 여왕과 벌레 떼 사이에 끼지 않게 구멍은 여왕 반대쪽
            carry = Wave(RushShape, share + carry, RushSide, at, hole, cap);
            waveNext++;
            OnEvent?.Invoke("wave");
        }
        if (waveNext >= Waves) carry = 0;   // 러시가 끝나면 상한에 막혀 남은 수는 버린다
    }

    void Preview(int m)
    {
        previewMin = m;
        (RushShape, RushSide, RushHole) = (ChooseShape(true), rng.Next(4), rng.NextDouble());
    }

    // 물결 모양: 마지막 분은 늘 Ring, 판의 첫 러시 첫 물결은 늘 Wall (읽기 가장 쉽다). 그 밖에는 무작위, 바로 앞과 같은 모양은 피한다
    // (고를 모양이 둘뿐인 동안은 같은 모양이 세 번 이어지지만 않게)
    Shape ChooseShape(bool first)
    {
        Shape pick;
        if (Clock >= 240) pick = Shape.Ring;
        else if (first && rushes == 0) pick = Shape.Wall;
        else
        {
            int n = ShapesOpen, at = rng.Next(n);
            if (at == (int)lastShape && (n >= 3 || shapeRun >= 2)) at = (at + 1 + rng.Next(n - 1)) % n;
            pick = (Shape)at;
        }
        shapeRun = pick == lastShape ? shapeRun + 1 : 1;
        lastShape = pick;
        return pick;
    }

    // ── 대열: 자리만 셈해 벌레를 바깥에서 들인다. 볼록이 150px 안쪽 자리는 건너뛴다. 돌려주는 값 = 상한에 막혀 못 들인 수 (다음 물결로 넘긴다) ──
    public int Wave(Shape shape, int n, int side, double holeAt, double holeLen, int cap = MaxFoes)
    {
        int blocked = 0, minute = MinuteOf(Clock);
        for (int i = 0; i < n; i++)
        {
            double pick = rng.NextDouble(), beetle = minute >= 1 ? RushBeetle : 0;
            var kind = pick < beetle ? Kind.Beetle : pick < beetle + RushWasp ? Kind.Wasp : Kind.Bug;
            var (x, y) = shape == Shape.Wall ? WallSpot(side, i, n) : RingSpot(i, n, holeAt, holeLen);
            if (Dist(x, y, X, Y) < KeepOut * sf) continue;
            if (Foes.Count >= cap) { blocked++; continue; }
            FromSide[SideOf(x, y)]++;
            Elitify(Rusher(Add(kind, x, y)));
        }
        return blocked;
    }

    int SideOf(double x, double y) => x < Field.Left ? 0 : x > Field.Right ? 1 : y < Field.Top ? 2 : 3;

    // 한쪽 줄: 그 변을 따라 26px 이상 간격으로 한 줄, 넘치면 18px 바깥에 둘째·셋째 줄
    (double, double) WallSpot(int side, int i, int n)
    {
        double len = side < 2 ? Field.Height : Field.Width;
        int perRow = Math.Max(1, (int)(len / (WallGap * sf))), row = i / perRow, inRow = Math.Min(perRow, n - row * perRow);
        return Edge(side, (i % perRow + .5) / inRow, (14 + WallRow * row) * sf);
    }

    // 구멍 난 원: 판 둘레(14px 밖 네모)를 0~1 로 펴서, 구멍(가운데 holeAt, 길이 holeLen)만 빼고 고르게
    Rectangle RingBox => Rectangle.Inflate(Field, (int)(14 * sf), (int)(14 * sf));

    (double, double) RingSpot(int i, int n, double holeAt, double holeLen)
    {
        double t = holeAt + holeLen / 2 + (i + .5) / n * (1 - holeLen);
        return RingPoint(t - Math.Floor(t));
    }

    (double, double) RingPoint(double t)
    {
        var r = RingBox;
        double w = r.Width, h = r.Height, d = t * 2 * (w + h);
        if (d < w) return (r.Left + d, r.Top);
        if (d < w + h) return (r.Right, r.Top + d - w);
        if (d < 2 * w + h) return (r.Right - (d - w - h), r.Bottom);
        return (r.Left, r.Bottom - (d - 2 * w - h));
    }

    // 둘레 위 한 점의 자리(0~1, 위 왼쪽 모서리에서 시계 방향). 둘레에서 먼 점은 가장 가까운 둘레로 옮겨 센다
    public double RingAt(double x, double y)
    {
        var r = RingBox;
        double w = r.Width, h = r.Height, dl = Math.Abs(x - r.Left), dr = Math.Abs(x - r.Right), dt = Math.Abs(y - r.Top), db = Math.Abs(y - r.Bottom), near = Math.Min(Math.Min(dl, dr), Math.Min(dt, db)), d;
        double cx = Math.Clamp(x, r.Left, r.Right), cy = Math.Clamp(y, r.Top, r.Bottom);
        if (near == dt) d = cx - r.Left;
        else if (near == dr) d = w + cy - r.Top;
        else if (near == db) d = w + h + r.Right - cx;
        else d = 2 * w + h + r.Bottom - cy;
        return d / (2 * (w + h)) % 1;
    }

    // 여왕 → 볼록이 방향으로 뻗은 선이 둘레와 만나는 자리 (여왕이 볼록이와 겹쳐 있으면 fallback)
    double OppositeQueen(double fallback)
    {
        foreach (var q in Foes)
        {
            if (q.Kind != Kind.Boss || q.Tier < Bosses) continue;
            double dx = X - q.X, dy = Y - q.Y, len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) break;
            (dx, dy) = (dx / len, dy / len);
            var r = RingBox;
            double tx = dx > 1e-9 ? (r.Right - X) / dx : dx < -1e-9 ? (r.Left - X) / dx : double.MaxValue, ty = dy > 1e-9 ? (r.Bottom - Y) / dy : dy < -1e-9 ? (r.Top - Y) / dy : double.MaxValue, t = Math.Min(tx, ty);
            return RingAt(X + dx * t, Y + dy * t);
        }
        return fallback;
    }

    // 엘리트 (2단계부터, 30초 뒤부터 나오는 보통·말벌·딱정벌레): eliteP 확률로, 동시에 EliteMax 마리까지
    // 러시 벌레: 몸이 약하고(체력 ×RushHp) 빠르다(×RushWalk) — 우르르 몰려오지만 한 방 무기도 걷어 낼 수 있게
    Foe Rusher(Foe f) { f.Hp = f.MaxHp = Math.Max(1, (int)(f.Hp * RushHp)); f.Speed *= RushWalk; return f; }

    Foe Elitify(Foe f)
    {
        if (!Elites || Clock < EliteFrom || f.Kind == Kind.Boss || rng.NextDouble() >= T.EliteP) return f;
        int have = 0;
        foreach (var o in Foes) if (o.Elite) have++;
        return have < EliteMax(Stage) ? MakeElite(f) : f;
    }

    public Foe MakeElite(Foe f)
    {
        if (f.Elite) return f;
        f.Elite = true;
        f.Hp = f.MaxHp = (int)Math.Round(f.Hp * EliteHp, MidpointRounding.AwayFromZero);
        f.Speed *= EliteWalk;
        return f;
    }

    // 디버그: 볼록이 옆 120px 에 엘리트 하나
    public Foe CallElite(Kind kind)
    {
        var (x, y) = Inside(X + 120 * sf * (Dir >= 0 ? 1 : -1), Y);
        return MakeElite(Add(kind == Kind.Boss ? Kind.Bug : kind, x, y));
    }

    // 디버그: 지금 예고를 띄우고 1초 뒤 그 모양으로 한 물결 (모양을 안 주면 차례대로)
    public void CallRush(Shape? shape)
    {
        forced = shape ?? (Shape)(called++ % ShapesOpen);
        forceAt = Clock + 1;
        (RushShape, RushSide, RushHole) = (forced, rng.Next(4), rng.NextDouble());
    }

    (double, double) Edge(int side, double u, double m = -1)
    {
        if (m < 0) m = 14 * sf;
        return side switch
        {
            0 => (Field.Left - m, Field.Top + u * Field.Height), 1 => (Field.Right + m, Field.Top + u * Field.Height),
            2 => (Field.Left + u * Field.Width, Field.Top - m), _ => (Field.Left + u * Field.Width, Field.Bottom + m),
        };
    }

    public Foe Add(Kind kind)
    {
        int side = 0;
        double x = 0, y = 0;
        for (int tries = 0; tries < 8 && (tries == 0 || Dist(x, y, X, Y) < 150 * sf); tries++) // 볼록이 코앞에서는 나오지 않는다
        {
            side = rng.Next(4);
            (x, y) = Edge(side, rng.NextDouble());
        }
        FromSide[side]++;
        return Add(kind, x, y);
    }

    // 보스를 그 변의 그 자리로 들인다. 2번은 말벌 여섯을 데리고 온다
    public Foe AddBoss(int tier, int side, double u)
    {
        var (x, y) = Edge(side, u);
        var boss = Add(Kind.Boss, x, y, tier);
        if (tier == 2)
            for (int i = 0; i < 6 && Foes.Count < MaxFoes + 4; i++) Add(Kind.Wasp, x + (i % 3 - 1) * 26 * sf, y + (i / 3 * 2 - 1) * 30 * sf);
        if (bossSince <= 0) bossSince = Clock;
        return boss;
    }

    // 디버그: 그 보스를 예고 없이 바로 부른다
    public Foe CallBoss(int tier) => AddBoss(Math.Clamp(tier, 1, Bosses), rng.Next(4), .2 + .6 * rng.NextDouble());

    // 디버그: 시계를 그 초로 (이미 지난 보스는 건너뛴다)
    public void SetClock(double sec) { (Clock, bossSeen, bossWarned, rushMin, previewMin, forceAt) = (Math.Clamp(sec, 0, WinSec - .5), -1, -1, -1, -1, 1e9); }   // 물결 차례도 다시 맞춘다 (Rush 가 새 분으로 본다)

    // 디버그·장면: 볼록이 옆에 아이템을 떨어뜨린다
    public void Drop(Item item, double dx = 40)
    {
        var (x, y) = Inside(X + dx * sf, Y);
        Gems.Add(new Gem { X = x, Y = y, Item = item, Xp = item == Item.Gem ? 1 : 0 });
        if (item == Item.Magnet) magnetsOut++;
        if (item == Item.Shoe) shoesOut++;
    }

    public Foe Add(Kind kind, double x, double y, int tier = 1)
    {
        tier = kind == Kind.Boss ? Math.Clamp(tier, 1, Bosses) : 0;
        double t = Clock;
        var f = new Foe
        {
            Kind = kind, X = x, Y = y, Tier = tier,
            Hp = (int)Math.Round(kind switch { Kind.Boss => BossHp[tier - 1] * T.BossX, Kind.Beetle => (46 + (int)(t / 3)) * T.HpX, Kind.Wasp => (9 + (int)(t / 10)) * T.HpX, _ => (12 + (int)(t / 8)) * T.HpX }, MidpointRounding.AwayFromZero),
            Speed = kind switch { Kind.Boss => BossSpeed[tier - 1], Kind.Beetle => 36 + t * .08, Kind.Wasp => 90 + t * .09, _ => 45 + t * .09 } * T.SpeedX,
            R = (kind switch { Kind.Boss => tier >= Bosses ? 34 : 24, Kind.Beetle => 12, Kind.Wasp => 8, _ => 10 }) * sf,
        };
        f.MaxHp = f.Hp;
        if (tier >= 3) f.DashAt = Clock + DashEvery;
        if (tier >= Bosses) f.SpawnDue = Clock + QueenEvery;
        Foes.Add(f);
        Born++;
        return f;
    }

    void MoveFoes(double dt)
    {
        foreach (var f in Foes)
        {
            double sinceDash = Clock - f.DashAt;
            if (sinceDash >= 0) // 돌진: 멈춰서 떨다가(예고) 그때 볼록이가 있던 쪽으로 내달린다
            {
                double d0 = Dist(f);
                if (!f.Winding) (f.Winding, f.DashX, f.DashY) = (true, d0 > 1 ? (X - f.X) / d0 : 1, d0 > 1 ? (Y - f.Y) / d0 : 0);
                if (sinceDash >= DashWind + DashSec) (f.Winding, f.DashAt) = (false, f.DashAt + DashEvery);
                else if (sinceDash >= DashWind)
                {
                    f.X = Math.Clamp(f.X + f.DashX * DashSpeed * sf * dt, Field.Left - 30 * sf, Field.Right + 30 * sf);
                    f.Y = Math.Clamp(f.Y + f.DashY * DashSpeed * sf * dt, Field.Top - 30 * sf, Field.Bottom + 30 * sf);
                }
                continue;
            }
            if (Clock < f.StunUntil) continue; // 맞으면 무기에 따라 잠깐 멈칫 (보스는 멈칫 없음)
            double d = Dist(f);
            if (d < 1) continue;
            double step = Math.Min(d, f.Speed * sf * dt);
            f.X += (X - f.X) / d * step;
            f.Y += (Y - f.Y) / d * step;
        }
        for (int i = 0; i < Foes.Count; i++) // 한 덩어리로 겹치지 않게 서로 조금 밀어낸다
        {
            Foe a = Foes[i];
            for (int j = i + 1; j < Foes.Count; j++)
            {
                Foe b = Foes[j];
                double want = (a.R + b.R) * .8, dx = a.X - b.X, dy = a.Y - b.Y, d2 = dx * dx + dy * dy;
                if (d2 >= want * want || d2 < .0001) continue;
                double d = Math.Sqrt(d2), push = (want - d) / 2, ux = dx / d, uy = dy / d;
                (a.X, a.Y, b.X, b.Y) = (a.X + ux * push, a.Y + uy * push, b.X - ux * push, b.Y - uy * push);
            }
        }
    }

    // ── 공격: 무기 하나가 알아서 친다 ──
    void Attack(double dt)
    {
        switch (Weapon.Kind)
        {
            case Arm.Sort.Melee: Melee(dt); break;
            case Arm.Sort.Bullets: Bullets(dt); break;
            case Arm.Sort.Rocket: Rocket(dt); break;
            default: Flame(dt); break;
        }
    }

    static double Turn(double a, double b) => Math.IEEERemainder(a - b, 2 * Math.PI);   // b 에서 a 까지 도는 각 (-π ~ π)

    // 칼: 벌레가 닿을 만큼 붙으면 그쪽으로 부채꼴을 한 번 휘두른다. 안에 든 벌레는 다 맞는다.
    // 쌍검은 좌·우 손을 번갈아 쓰고, 세게 3단계부터는 넷째 휘두름마다 한 바퀴 회전 베기 (피해 1.5배, 더 멀리 밀친다)
    void Melee(double dt)
    {
        cool -= dt;
        if (cool > 0) return;
        Foe? near = null;
        double nearD = double.MaxValue;
        foreach (var f in Foes) { double d = Dist(f); if (d < RangePx + f.R && d < nearD) (near, nearD) = (f, d); }
        if (near == null) { cool = 0; return; }
        var w = Weapon;
        SwingCount++;
        SwingHand = -SwingHand;
        SwingSpin = w.Spin && SwingCount % 4 == 0;
        (cool, SwingAt, SwingDir, SwingArc, SwingLen) = (cool + w.Cool, Clock, Math.Atan2(near.Y - Y, near.X - X), SwingSpin ? Math.PI : w.ArcHalf, Math.Min(SwingSec, w.Cool * .8));
        AimDir = near.X >= X ? 1 : -1;
        int dmg = SwingSpin ? w.Dmg * 3 / 2 : w.Dmg;
        double knock = SwingSpin ? Math.Max(16, w.Knock) : w.Knock;
        foreach (var f in Foes)
        {
            if (f.Dead || Dist(f) >= RangePx + f.R) continue;
            if (SwingArc < Math.PI - .01 && Math.Abs(Turn(Math.Atan2(f.Y - Y, f.X - X), SwingDir)) > SwingArc + .01) continue;
            Damage(f, dmg, knock, w.Stun, X, Y);
        }
        OnEvent?.Invoke("swing");
    }

    // 총: 닿는 거리 안에서 가장 가까운 벌레부터 고른다.
    // 쌍권총은 표적 수만큼 하나씩 겨눠 쏘고 (벌레가 모자라면 남는 총알도 같은 놈에게 살짝 벌려 쏜다), 산탄총은 가장 가까운 놈 쪽 부채꼴로 알을 뿌리고, 소총은 살짝 흔들리며 연사한다
    const int MaxAim = 4;
    readonly Foe[] aim = new Foe[MaxAim];
    readonly double[] aimD = new double[MaxAim];

    void Bullets(double dt)
    {
        cool -= dt;
        if (cool > 0) return;
        var w = Weapon;
        int want = w.Which == Arm.Id.Pistols ? Math.Min(w.Count, MaxAim) : 1, count = 0;
        foreach (var f in Foes) // 가까운 순서로 want 마리 (거리가 같으면 먼저 온 벌레가 앞)
        {
            double d = Dist(f);
            if (d >= RangePx) continue;
            int at = count;
            while (at > 0 && aimD[at - 1] > d) at--;
            if (at >= want) continue;
            if (count < want) count++;
            for (int i = count - 1; i > at; i--) (aim[i], aimD[i]) = (aim[i - 1], aimD[i - 1]);
            (aim[at], aimD[at]) = (f, d);
        }
        if (count == 0) { cool = 0; return; }
        (cool, FireAt, AimDir) = (cool + w.Cool, Clock, aim[0].X >= X ? 1 : -1);
        double front = Math.Atan2(aim[0].Y - Y, aim[0].X - X);
        switch (w.Which)
        {
            case Arm.Id.Shotgun: // 알은 닿는 거리까지만 날아간다
                for (int i = 0; i < w.Count; i++) Fire(Ammo.Pellet, front + (w.Count == 1 ? 0 : -w.ArcHalf + 2 * w.ArcHalf * i / (w.Count - 1)), w.Range / w.Speed);
                break;
            case Arm.Id.Rifle:
                Fire(Ammo.Round, front + (rng.NextDouble() * 2 - 1) * w.ArcHalf, w.Range * 1.1 / w.Speed);
                break;
            default:
                for (int i = 0; i < w.Count; i++)
                {
                    var f = aim[i % count];
                    int extra = i / count;
                    Fire(Ammo.Bullet, Math.Atan2(f.Y - Y, f.X - X) + (extra + 1) / 2 * (extra % 2 == 0 ? -.06 : .06), w.Range * 1.25 / w.Speed);
                }
                break;
        }
        Array.Clear(aim); // 잡힌 벌레를 붙들고 있지 않게
        FireEvent(.1);
    }

    void Fire(Ammo ammo, double a, double life)
    {
        if (Shots.Count >= MaxShots) return;
        var w = Weapon;
        ref var s = ref Shots.Add();
        s = new Shot
        {
            Ammo = ammo, X = X, Y = Y, Vx = Math.Cos(a) * w.Speed * sf, Vy = Math.Sin(a) * w.Speed * sf, Until = Clock + life,
            Dmg = w.Dmg, Pierce = w.Pierce, Knock = w.Knock, Stun = w.Stun, Blast = w.Blast,
        };
    }

    // 연사·불길은 소리 사건을 gap 초에 한 번만 내보낸다
    void FireEvent(double gap)
    {
        if (Clock - fireEvAt < gap - 1e-9) return;
        fireEvAt = Clock;
        OnEvent?.Invoke("fire");
    }

    // 바주카: 닿는 거리 안 벌레마다 폭발 반지름 안의 이웃을 세어 가장 몰린 놈을 겨눈다. 처음 닿은 벌레나 겨눈 자리에서 터진다
    void Rocket(double dt)
    {
        cool -= dt;
        if (cool > 0) return;
        var w = Weapon;
        Foe? best = null;
        int bestN = 0;
        double bestD = 0, blast = w.Blast * sf;
        foreach (var a in Foes)
        {
            double d = Dist(a);
            if (d >= RangePx) continue;
            int n = 0;
            foreach (var b in Foes) { double dx = a.X - b.X, dy = a.Y - b.Y, r = blast + b.R; if (dx * dx + dy * dy < r * r) n++; }
            if (n > bestN || n == bestN && d < bestD) (best, bestN, bestD) = (a, n, d);
        }
        if (best == null) { cool = 0; return; }
        (cool, FireAt, AimDir) = (cool + w.Cool, Clock, best.X >= X ? 1 : -1);
        Fire(Ammo.Rocket, Math.Atan2(best.Y - Y, best.X - X), w.Range * 1.3 / w.Speed);
        if (Shots.Count > 0) (Shots[Shots.Count - 1].Tx, Shots[Shots.Count - 1].Ty) = (best.X, best.Y);
        OnEvent?.Invoke("fire");
    }

    // 화염: 겨눔을 가장 가까운 놈 쪽으로 천천히 돌리고, 닿는 거리 안에 벌레가 있으면 틱마다 부채꼴 안 모두를 태운다. 밀침·멈칫이 없다
    void Flame(double dt)
    {
        var w = Weapon;
        Foe? near = null;
        double nearD = double.MaxValue;
        foreach (var f in Foes) { double d = Dist(f) - f.R; if (d < nearD) (near, nearD) = (f, d); }
        FlameOn = near != null && nearD < RangePx;
        if (near != null && nearD < RangePx * 2)
        {
            double turn = Turn(Math.Atan2(near.Y - Y, near.X - X), AimAngle), max = FlameTurn * dt;
            AimAngle = Math.IEEERemainder(AimAngle + Math.Clamp(turn, -max, max), 2 * Math.PI);
            AimDir = Math.Cos(AimAngle) >= 0 ? 1 : -1;
        }
        cool -= dt;
        if (!FlameOn) cool = Math.Max(0, cool);
        if (!FlameOn || cool > 0) return;
        (cool, FireAt) = (cool + w.Cool, Clock);
        foreach (var f in Foes)
        {
            if (f.Dead || Dist(f) >= RangePx + f.R || Math.Abs(Turn(Math.Atan2(f.Y - Y, f.X - X), AimAngle)) > w.ArcHalf + .01) continue;
            if (Clock >= f.BurnUntil) f.BurnTick = Clock + BurnEvery;
            f.BurnUntil = Clock + w.BurnSec;
            Damage(f, w.Dmg, 0, 0, X, Y, flash: false);
        }
        FireEvent(.25);
    }

    // 불붙은 벌레는 불길을 벗어나도 0.5초마다 탄다
    void Burn()
    {
        if (Weapon.BurnDmg <= 0) return;
        foreach (var f in Foes)
        {
            if (f.Dead || Clock >= f.BurnUntil || Clock < f.BurnTick) continue;
            f.BurnTick += BurnEvery;
            Damage(f, Weapon.BurnDmg, 0, 0, X, Y, flash: false);
        }
    }

    void MoveShots(double dt)
    {
        int keep = 0;
        for (int i = 0; i < Shots.Count; i++)
        {
            var s = Shots[i];
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            bool gone = false;
            if (s.Ammo == Ammo.Rocket)
            {
                bool hit = Clock > s.Until || (s.X - s.Tx) * s.Vx + (s.Y - s.Ty) * s.Vy >= 0; // 겨눈 자리에 닿았다
                if (!hit)
                    foreach (var f in Foes)
                    {
                        double dx = f.X - s.X, dy = f.Y - s.Y, r = f.R + 4 * sf;
                        if (!f.Dead && dx * dx + dy * dy < r * r) { hit = true; break; }
                    }
                if (hit) { Explode(s); gone = true; }
            }
            else
            {
                foreach (var f in Foes)
                {
                    if (f.Dead || s.Seen(f)) continue;
                    double dx = f.X - s.X, dy = f.Y - s.Y, r = f.R + 3 * sf;
                    if (dx * dx + dy * dy >= r * r) continue;
                    s.Mark(f);
                    Damage(f, s.Dmg, s.Knock, s.Stun, f.X - s.Vx, f.Y - s.Vy); // 날아가던 쪽으로 밀린다
                    if (s.Pierce-- <= 0) { gone = true; break; }
                }
                if (!gone && Clock > s.Until) gone = true;
            }
            if (!gone) Shots[keep++] = s;
        }
        Shots.Trim(keep);
    }

    // 로켓이 터진다: 반지름 안 모두에게 피해, 바깥으로 밀친다. 볼록이는 다치지 않는다
    void Explode(in Shot s)
    {
        double blast = s.Blast * sf;
        foreach (var f in Foes)
        {
            double dx = f.X - s.X, dy = f.Y - s.Y, r = blast + f.R;
            if (f.Dead || dx * dx + dy * dy >= r * r) continue;
            Damage(f, s.Dmg, s.Knock, s.Stun, s.X, s.Y);
        }
        Pops.Add(new Pop { X = s.X, Y = s.Y, At = Clock, R = blast });
        BoomAt = Clock;
        OnEvent?.Invoke("boom");
    }

    // 피해: 번쩍이고(flash), 무기에 따라 밀리고 멈칫한다. 보스는 밀침 ¼·멈칫 없음. 잡으면 Dead 로 표시해 두고 Sweep 이 치운다
    bool Damage(Foe f, int n, double knock, double stun, double fromX, double fromY, bool flash = true)
    {
        if (f.Dead) return false;
        bool boss = f.Kind == Kind.Boss;
        if (flash) f.Hit = Clock;
        if (stun > 0 && !boss) f.StunUntil = Math.Max(f.StunUntil, Clock + stun);
        if (knock > 0)
        {
            double d = Dist(f.X, f.Y, fromX, fromY), push = knock * sf * (boss ? .25 : 1);
            if (d > .01) (f.X, f.Y) = (f.X + (f.X - fromX) / d * push, f.Y + (f.Y - fromY) / d * push);
        }
        if ((f.Hp -= n) > 0) { if (flash) OnEvent?.Invoke("hit"); return false; }
        Kill(f);
        return true;
    }

    void Kill(Foe f)
    {
        f.Dead = true;
        Kills++;
        bool boss = f.Kind == Kind.Boss;
        Pops.Add(new Pop { X = f.X, Y = f.Y, At = Clock, R = f.R * 1.5 });
        Gems.Add(new Gem { X = f.X, Y = f.Y, Xp = boss ? BossGems[f.Tier - 1] : f.Kind == Kind.Beetle ? f.Elite ? EliteBeetleGems : 3 : f.Elite ? EliteGems : GemValue() });
        if (boss)
        {
            foreach (var g in Gems) if (g.Item == Item.Gem) g.Pull = true;   // 보스를 잡으면 판 위 보석이 모두 날아온다
            BossesKilled++;
            if (FirstBossSec < 0) FirstBossSec = Clock - bossSince;
            Gems.Add(new Gem { X = f.X + 14 * sf, Y = f.Y, Item = Item.Heart }); // 보스는 하트를 꼭 남긴다
            if (f.Tier is 1 or 3 && Magnets + magnetsOut < MaxMagnets)          // 1·3번 보스는 자석도
            {
                Gems.Add(new Gem { X = f.X - 14 * sf, Y = f.Y, Item = Item.Magnet });
                magnetsOut++;
            }
        }
        // 하트: 딱정벌레가 가끔. 생명이 깎여 있고 판 위에 하트가 없을 때만
        else if ((f.Kind == Kind.Beetle || f.Elite) && Hp < MaxHp && rng.NextDouble() < (f.Elite ? EliteHeartChance : HeartChance) && !Lying(Item.Heart))
            Gems.Add(new Gem { X = f.X, Y = f.Y - 12 * sf, Item = Item.Heart });
        // 신발: 보통 벌레를 잡으면 가끔. 처음 15초와 하나 떨어진 뒤 30초는 나오지 않고, 신은 것과 떨어져 있는 것을 합쳐 세 켤레까지만
        if (!boss && Shoes + shoesOut < MaxShoes && Clock >= shoeFrom && rng.NextDouble() < ShoeChance)
        {
            var (hx, hy) = Inside(f.X + 14 * sf, f.Y);
            Gems.Add(new Gem { X = hx, Y = hy, Item = Item.Shoe });
            (shoesOut, shoeFrom) = (shoesOut + 1, Clock + ShoeGap);
        }
        // 자석: 보통 벌레를 잡으면 가끔. 판 위에 하나만, 처음 20초·주운 뒤 35초는 나오지 않고, 다섯 개를 채우면 더 나오지 않는다
        if (!boss && magnetsOut == 0 && Magnets < MaxMagnets && Clock >= magnetFrom && rng.NextDouble() < MagnetChance)
        {
            var (mx, my) = Inside(f.X - 14 * sf, f.Y);
            Gems.Add(new Gem { X = mx, Y = my, Item = Item.Magnet });
            magnetsOut++;
        }
        while (Gems.Count > MaxGems) // 상한: 가장 오래된 보석의 값을 그다음 오래된 보석에 더한다 (잃는 보석이 없다). 보석이 하나뿐이면 가장 오래된 하트가 사라진다 (자석·신발은 밀려나지 않는다)
        {
            int old = -1, next = -1;
            for (int i = 0; i < Gems.Count && next < 0; i++)
                if (Gems[i].Item == Item.Gem) { if (old < 0) old = i; else next = i; }
            if (next >= 0)
            {
                (Gems[next].Xp, Gems[next].ShownX) = (Gems[next].Xp + Gems[old].Xp, double.NaN);   // 그림이 바뀔 수 있어 다시 그리게
                Gems[next].Pull |= Gems[old].Pull;
            }
            else
            {
                old = 0;
                while (Gems[old].Item >= Item.Magnet) old++;
            }
            Gone.Add(Gems[old]);
            Gems.RemoveAt(old);
        }
        OnEvent?.Invoke(boss ? "bigkill" : "kill");
    }

    // 보통 보석(값 1)은 벌레가 단단한 만큼 값 2 로 떨어진다: 넣은 피해만큼 보석이 나오도록 (단계가 올라도 레벨이 멈추지 않게)
    int GemValue() => T.HpX > 1 && rng.NextDouble() < (T.HpX - 1) * GemBoost ? 2 : 1;

    bool Lying(Item item) { foreach (var g in Gems) if (g.Item == item) return true; return false; }

    void Sweep() => Foes.RemoveAll(static f => f.Dead);

    // ── 닿으면 그 벌레만큼 생명이 깎인다 (여럿이 닿으면 가장 큰 것 하나). 둘레 벌레를 밀어내고 1초는 안 맞는다 ──
    void Touch()
    {
        if (Clock < SafeUntil) return;
        int dmg = 0;
        foreach (var f in Foes) if (Dist(f) < f.R + BodyR * .8) dmg = Math.Max(dmg, TouchDmg(f));
        if (dmg == 0) return;
        dmg = Math.Max(1, (int)Math.Round(dmg * Weapon.Guard));
        if (Clock - HpAt >= HpTrailSec) HpFrom = Hp; // 방금 잃은 만큼은 잠깐 흰색으로 남는다
        (Hp, DamageTaken, HpAt) = (Math.Max(0, Hp - dmg), DamageTaken + dmg, Clock);
        (HurtAt, SafeUntil) = (Clock, Clock + SafeSec);
        Pops.Add(new Pop { X = X, Y = Y, At = Clock, R = 18 * sf });
        foreach (var f in Foes)
        {
            double d = Dist(f), away = HurtPush * sf + f.R;
            if (d < away && d > .01) (f.X, f.Y) = (X + (f.X - X) / d * away, Y + (f.Y - Y) / d * away);
        }
        if (Hp <= 0) Go(Phase.Over, "over");
        else OnEvent?.Invoke("hurt");
    }

    public void Heal(int n) => (Hp, HpFrom, HpAt) = (Math.Min(MaxHp, Hp + n), Math.Min(MaxHp, Hp + n), Clock);

    // ── 보석: 줍는 둘레 안에 들면 끌려와 주워진다. 다 차면 레벨이 오르고 무기의 한 갈래가 자란다. 자석을 주우면 둘레가 그 판 내내 넓어진다 ──
    void Pick(double dt)
    {
        double reach = PickPx;
        for (int i = 0; i < Gems.Count; i++)
        {
            var g = Gems[i];
            double d = Dist(g.X, g.Y, X, Y);
            if (d >= reach && !g.Pull) continue;
            if (d > 10 * sf)
            {
                double step = Math.Min(d, PullSpeed * sf * dt);
                (g.X, g.Y) = (g.X + (X - g.X) / d * step, g.Y + (Y - g.Y) / d * step);
                continue;
            }
            Gems.RemoveAt(i--);
            Gone.Add(g);
            switch (g.Item)
            {
                case Item.Magnet:
                    (Magnets, magnetsOut, magnetFrom, MagnetAt, LastItem, ItemAt) = (Math.Min(MaxMagnets, Magnets + 1), Math.Max(0, magnetsOut - 1), Clock + MagnetGap, Clock, g.Item, Clock);
                    OnEvent?.Invoke("magnet");
                    break;
                case Item.Shoe:
                    (Shoes, shoesOut, LastItem, ItemAt) = (Math.Min(MaxShoes, Shoes + 1), Math.Max(0, shoesOut - 1), g.Item, Clock);
                    OnEvent?.Invoke("shoe");
                    break;
                case Item.Heart: Heal(HeartHp); OnEvent?.Invoke("heart"); break;
                default: Xp += g.Xp; break;
            }
        }
        for (; Xp >= Need; LevelUp()) Xp -= Need;
    }

    // 레벨 업: 덜 키운 갈래부터 무작위로 하나 (판마다 운이 너무 갈리지 않게). 생명이 절반 아래면 둘에 한 번은 생명 +20, 갈래가 다 찬 뒤엔 생명 +10 만
    public void LevelUp()
    {
        Level++;
        int least = int.MaxValue, can = 0;
        for (var u = Up.Fast; u <= Up.Power; u++)
        {
            if (Weapon.Full(u)) continue;
            int n = Weapon.Ups[(int)u];
            if (n < least) (least, can) = (n, 1);
            else if (n == least) can++;
        }
        var up = Up.Heal;
        if (can > 0 && !(Hp < MaxHp / 2 && rng.Next(2) == 0))
        {
            int nth = rng.Next(can);
            for (var u = Up.Fast; u <= Up.Power; u++)
                if (!Weapon.Full(u) && Weapon.Ups[(int)u] == least && nth-- == 0) up = u;
        }
        if (up == Up.Heal) Heal(can > 0 ? 20 : 10);
        else Weapon.Grow(up);
        (LastUp, UpAt) = (up, Clock);
        OnEvent?.Invoke("level");
    }

    // 시험·디버그용 자동 조종: 0.1초마다 열여섯 방향(과 제자리)으로 0.8초 앞을 내다보고 가장 나은 쪽을 고른다.
    // 가는 길에 벌레와 가장 가까워지는 틈이 넓을수록 좋고(무기마다 지키고 싶은 틈 BotGap 까지만: 근접은 붙어야 친다), 가장자리는 나쁘고, 보석에 가까워지면 좋다.
    // 붙어야 하는 무기(근접·화염·산탄총)는 내다본 자리에서 닿는 거리 안에 든 벌레 수만큼 점수를 더 준다
    (double Dx, double Dy) bot;
    double botAt = -9;

    public (double Dx, double Dy) BotSteer()
    {
        if (Clock - botAt < .1) return bot;
        botAt = Clock;
        var w = Weapon;
        double hx = Field.Width / 2.0, hy = Field.Height / 2.0, bestScore = double.MinValue, want = w.BotGap * sf, tight = Math.Min(20, w.BotGap * .5) * sf;
        bool hunt = w.Kind is Arm.Sort.Melee or Arm.Sort.Flame || w.Which == Arm.Id.Shotgun;
        var (old, best) = (bot, (0.0, 0.0));
        for (int i = -1; i < 16; i++)
        {
            double dx = i < 0 ? 0 : Math.Cos(i * Math.PI / 8), dy = i < 0 ? 0 : Math.Sin(i * Math.PI / 8);
            double px = X, py = Y, gap = want, nearX = X, nearY = Y;
            for (int step = 1; step <= 8; step++)
            {
                (px, py) = Inside(px + dx * Speed * sf * .1, py + dy * Speed * sf * .1);
                if (step == 3) (nearX, nearY) = (px, py);
                if (Clock + step * .1 < SafeUntil) continue; // 안 맞는 동안은 뚫고 지나가도 된다
                foreach (var f in Foes) gap = Math.Min(gap, Dist(f.X, f.Y, px, py) - f.Speed * sf * .1 * step - f.R - BodyR);
            }
            double cx = (px - Field.Left - hx) / hx, cy = (py - Field.Top - hy) / hy;
            double score = gap / (30 * sf) + Math.Min(0, gap - tight) / (3 * sf) - (cx * cx * cx * cx + cy * cy * cy * cy) * 5 + ((dx, dy) == old ? .15 : 0);
            if (hunt)
            {
                int n = 0;
                foreach (var f in Foes) if (Dist(f.X, f.Y, nearX, nearY) < RangePx + f.R) n++;
                score += Math.Min(n, 4) * .35;
            }
            if (Gems.Count > 0)
            {
                double nearest = double.MaxValue;
                foreach (var g in Gems) nearest = Math.Min(nearest, Dist(g.X, g.Y, px, py) - (g.Item != Item.Gem ? 60 * sf : 0));
                score += 2 / (1 + nearest / (80 * sf));
            }
            if (score > bestScore) (bestScore, best) = (score, (dx, dy));
        }
        return bot = best;
    }
}
