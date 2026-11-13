namespace Bolok;

public enum ThingKind { Block, Fly, Seed, Heart, Floor, Pad, Wasp, Rock, Pit, Cave }

// 레벨마다 하나씩 열리는 기믹 (YELL 2026-11-13). 차례가 곧 열리는 레벨이다: 버섯 점프대 2, 말벌 3, 굴러오는 돌 4, 구덩이 5, 동굴 6
public enum Gimmick { Pad, Wasp, Rock, Pit, Cave }

// 길 위의 것: 장애물(땅·공중), 씨앗, 하트, 2층 발판, 기믹. X 는 왼쪽, Y 는 바닥선(화면 좌표)
public sealed class Thing
{
    public ThingKind Kind;
    public string Sprite = "";
    public int Art;                     // Runner.Arts 의 몇 번째 그림인지 (그리는 쪽이 이름을 다시 찾지 않게)
    public int Scale, W, H, Ox, Oy;     // Scale: 그림 배율(화면 px). Ox·Oy: 칸 왼쪽 위에서 실제 그림까지 (그릴 때 쓴다)
    public double X, Y;
    public bool Gone;
    public int Way;                     // 말벌·돌: 볼록이에게 닿을 때 0 낮게(뛰어넘는다) · 1 높게(엎드려 지나간다). 동굴: 천장 무늬 씨
    public double Fast = 1;             // 길보다 몇 배 빨리 다가오는지 (말벌·돌. 가만히 있는 것은 1)
    public double Hot = -9;             // 버섯 점프대: 밟힌 때 (눌린 그림을 잠깐 보인다)
    public int Bunch;                   // 몇 번째 기믹 묶음으로 나왔는지 (기믹이 아니면 0)
    public bool Fresh;                  // 이 판에서 처음 보이는 기믹 (혼자, 앞뒤를 넉넉히 비워 나온다)
    public Rectangle Box => new((int)X, (int)(Y - H), W, H);
}

// 볼록이 몸 치수 (칸 px: 그림 한 칸이 40). 귀는 뺀 몸통이다. 붙이는 쪽이 그림 데이터(PetArt.Size)로 계산해 넘긴다
public readonly record struct RunBody(double W, double Stand, double Duck)
{
    public const double DuckDrop = 5;   // 엎드린 자세(land, sq 5)는 몸 꼭대기가 5칸px 낮다
    // 몸 반지름 rx·ry 에서: 폭 2·rx, 선 키 2·ry + 4 (발이 칸의 37번째 줄, 칸 바닥이 땅)
    public static RunBody Of(double rx, double ry) => new(2 * rx, 2 * ry + 4, 2 * ry + 4 - DuckDrop);
}

// 길 위에 나오는 그림 하나 (이름 + 배율)와 잰 크기
public readonly record struct RunArt(string Sprite, int Scale, int W, int H, int Ox, int Oy);

// 쿠키런 모드: 볼록이는 제자리에서 달리고 길이 왼쪽으로 흐른다. Ctrl 톡 점프(두 번이면 더블), Shift 누르면 슬라이드,
// 끝은 Esc (보스 키, PetForm.BossKey — YELL 2026-11-13: 게임에서 Esc 는 언제나 빠져나가기). Ctrl+Shift 를 0.5초 누르는 옛 끝내기도 남겨 둔다.
// 체력은 저절로 닳고 부딪히면 크게 닳는다. 레벨마다 목표 점수가 있고, 넘기면 그 판 안에서 바로 다음 레벨로 올라 더 어려워진다.
// 레벨은 저장해서 다음 판은 그 레벨에서 시작한다. 화면과 무관한 규칙이라 --selfcheck 로 검사한다.
// 판정은 몸 크기를 따른다 (YELL 2026-11-13): 아기는 낮고 좁게, 어른은 크게. 구름·장애물·씨앗 높이도 선 키(S)에 맞춘다
//
// 2층 (YELL 2026-11-13 「달리기는 중간에 2층도 나오게」): 가끔 길 위에 발판이 뜬다. 꼭 올라가야 하는 길이 아니라 더 좋은 길이다
//   - 한쪽으로만 막힌다: 내려오다가 윗면에 닿을 때만 선다. 밑에서 뛰면 뚫고 올라가 그 위에 선다. 발판 밑면은 선 볼록이 머리보다 높아 밑으로 그냥 달려도 된다
//   - 윗면 높이는 FloorLow~FloorHigh: 선 키 + 머리 위 11칸px(귀가 가장 긴 캐릭터도 안 닿게) + 두께 7칸px 에서 3칸px 더까지.
//     한 번 뛰어 오르는 높이(JumpUp, 62.7칸px)의 0.8배를 넘지 않는다: 가장 큰 통통이도 윗면 위에 0.35초 넘게 떠 있다
//   - 위에는 씨앗 줄·씨앗 무지개, 가끔 하트만 있다 (장애물 없음). 밑으로는 보통 땅 장애물이 계속 온다 (구름·키 큰 것은 안 온다)
//   - 발판이 끝나면 땅으로 떨어진다. 끝난 뒤 (떨어지는 시간 + 0.75초) 거리에는 장애물을 두지 않는다: 가만히 떨어져도 다음 것을 보고 뛸 틈이 있다
//   - 발판 앞도 비어 있다 (앞 묶음과의 틈). 연달아 둘이 나오면 사이는 0.3초 거리: 뛰어 건너거나, 떨어져도 그 밑은 비어 있다
//   - 조작은 그대로다. 발판 위에서 Shift 를 새로 누르면 뚫고 내려간다 (누른 채로 올라서면 안 내려간다). 발판 끝에서 떨어질 때도 두 번 뛸 수 있다
//   - 첫 2층은 6~12초에. 그 뒤로 레벨 1 은 22~32초에 한 번, 레벨이 오를 때마다 0.9초씩 잦아져 12레벨부터 12~22초에 한 번 (1분에 두세 번)
//
// 기믹 (YELL 2026-11-13 확정): 레벨마다 하나씩 열린다. 레벨 1 은 전과 똑같다 (난수도 한 번 더 뽑지 않는다). 조작은 그대로 Ctrl·Shift 뿐
//   - Lv2 버섯 점프대 (도움, 안 다친다): 밟거나 그 위에 내려서면 높이 튄다 (Bounce: 뛰는 높이의 1.47배). 튄 길을 따라 씨앗이 놓이고 꼭대기에 가끔 하트.
//     2층 앞에 놓이면 튀어서 그 발판에 내려선다. 튀어 올라 내려설 자리까지는 아무것도 없다 (구름·말벌로 튀어 들어가지 않는다)
//   - Lv3 말벌: 머리 높이에서 물결치며 길보다 빨리 날아온다. 높이는 볼록이까지 남은 거리로만 정해져서 (물결 길이 WaveLen 칸px)
//     닿을 때는 언제나 물결의 맨 아래(낮게: 뛰어넘는다)이거나 맨 위(높게: 구름 바닥 높이, 엎드려 지나간다)다 — 뛰어도 엎드려도 맞는 어중간한 높이로는 오지 않는다
//   - Lv4 굴러오는 돌: 길보다 빨리 온다. 낮게 통통 튀는 돌은 볼록이 자리에서 땅에 닿아 있고(뛰어넘는다),
//     높게 튀는 돌은 볼록이 자리가 꼭대기다 (밑면이 구름 바닥 높이: 엎드려 지나간다). 튀는 자리도 남은 거리로만 정해져 볼 때마다 같다
//   - Lv5 구덩이: 땅에 서서(엎드려서도) 몸 가운데가 구멍 위에 오면 빠진다 = 맞은 것 (튕겨 올라온다). 뛰어넘는다. 폭은 0.16~0.26초 거리
//   - Lv6 동굴: 천장 밑면이 구름 바닥 높이인 긴 바위. Shift 를 누른 채 기어간다. 서 있거나 안에서 뛰면 머리를 찧는다 = 맞은 것 (천장을 뚫고 올라가지 못한다).
//     천장 위는 평평해서 미리 뛰어 올라서면 그 위로 달릴 수 있다 (씨앗은 안에만 있다). 박쥐·고드름돌은 그림일 뿐
//   공정하게: 묶음 하나에 기믹 한 가지만. 묶음과 묶음은 볼록이에게 닿는 차례대로 틈(TimeGap 초 이상)을 두고 온다 — 동굴 안·구덩이 위에 2층이 없고,
//     발판 끝·동굴 끝(떨어지는 시간 + Runway)·버섯으로 튀어 내려설 자리에 다른 것이 없다. 길보다 빠른 말벌·돌은 「같은 때 닿는 가만히 있는 것」의 자리로 셈해서
//     (화면에서는 그만큼 더 멀리서 출발한다) 앞 묶음을 볼록이 앞에서 앞지르지 않고, 뒤 묶음은 그것이 화면에 들어온 뒤에야 나온다 (겹쳐 지나가지 않는다)
//     틈·길이를 시간으로 정한 것은 「닿을 때」의 빠르기로 거리를 잰다 (오는 동안에도 길이 빨라진다: 4K 처럼 넓은 화면에서도 닿을 때의 틈이 그 시간이다)
//   처음: 이 판에서 처음 보이는 기믹은 혼자, 앞(FreshLead 초 더)·뒤(FreshAfter 초 이상)를 비워 나온다. 그 레벨에 새로 열린 기믹은 시작하고(레벨이 오르고) 곧 한 번 나온다.
//     2층 앞의 버섯도 혼자 나온 버섯을 한 번 본 뒤에만 놓인다
//   그 뒤: 섞여 나온다. 레벨이 오를수록 기믹 묶음의 몫이 는다 (GimmickShareOf: Lv2 10% → Lv6 38% → Lv12 50%). 어려운 묶음 몫만큼 말벌·돌·구덩이가 둘씩 온다
public sealed class Runner
{
    public readonly Rectangle Track;    // 달리는 모니터
    public readonly double Ground;      // 볼록이 발이 닿는 선
    public readonly double PetX;        // 볼록이 자리 (화면 왼쪽에서 22%)
    public readonly RunBody Body;
    public readonly int PetScale;       // 볼록이 배율: 칸 px 하나가 화면 몇 px 인지
    public readonly int BoxW, StandPx, DuckPx;  // 판정 상자 (화면 px): 폭은 몸 폭의 0.8, 높이는 선 키·엎드린 키
    public readonly double CloudUp;     // 구름 바닥 높이: 선 키와 엎드린 키의 한가운데 — 서면 맞고 엎드리면 지나가는 여유가 위아래 같다
    public readonly double NormalMax, TallMax;  // 땅 장애물 높이 한도: 보통은 선 키의 0.85배, 키 큰 것은 1.3배
    public readonly double JumpUp;      // 한 번 뛰어 오르는 높이 (화면 px)
    public readonly int FloorLow, FloorHigh, FloorThick;    // 2층 발판: 윗면 높이 범위(땅에서), 두께
    readonly double sf;                 // 속도·거리 배율 (배율 3 이 1)

    public double Lift, Vy, Speed, Energy = 100, Distance, Clock, HurtUntil, InvUntil, LevelAt = -9;
    public double Rest;                 // 서 있는 곳의 높이: 땅이면 0, 발판이면 그 윗면
    public bool Landed => Vy == 0 && Lift == Rest;  // 땅이나 발판을 딛고 있다
    public Thing? Under;                // 딛고 선 발판 (땅·공중이면 null)
    Thing? through;                     // Shift 로 뚫고 내려가는 발판: 발이 윗면 밑으로 갈 때까지 없는 셈 친다
    public readonly List<Thing> Floors = new();     // 2층 발판 (앞에서부터 차례로). W 는 길이, H 는 두께
    public readonly List<Thing> Pits = new();       // 구덩이. W 는 폭, Y 는 땅 밑 그림 끝
    public readonly List<Thing> Caves = new();      // 동굴 천장. W 는 길이, H 는 두께, Y 는 천장 밑면 (땅에서 CloudUp)
    public int Jumps, Seeds, Level;
    public bool Sliding, Over;
    public int Score => Seeds * 10 + (int)(Distance / (100 * sf));
    public int LevelScore => Score - levelFrom;     // 이번 레벨에서 번 점수
    public readonly List<Thing> Things = new();
    public Action<string>? OnEvent;
    readonly Random rng;
    readonly Func<string, int, (int W, int H, int Ox, int Oy)> measure;
    double nextSpawn;
    int levelFrom;

    // 길 위에 나올 수 있는 그림들. 잰 값은 한 번만 재서 여기 둔다
    readonly List<RunArt> arts = new();
    readonly Dictionary<(string, int), int> artIndex = new();
    public IReadOnlyList<RunArt> Arts => arts;
    readonly List<int> small = new(), normal = new(), tall = new();     // 땅 장애물 후보 (arts 번호): 가장 작은 배율 / 보통 / 키 큰 것
    public IReadOnlyList<int> NormalBlocks => normal;
    public IReadOnlyList<int> TallBlocks => tall;
    readonly int seedArt, heartArt, cloudArt;
    public readonly (int L, int M, int R) LedgeArt;     // 발판 그림: 왼쪽 끝, 이어 붙이는 가운데, 오른쪽 끝
    double floorAt;                     // 다음 2층이 나올 때 (Clock)
    readonly int padArt, waspArt, rockArt;
    public readonly (int L, int M, int R) PitArt, CaveArt;  // 구덩이·동굴 그림: 왼쪽 끝, 이어 붙이는 가운데, 오른쪽 끝
    public readonly int PitEdge, PitDeep;   // 구덩이: 끝 조각 폭(여기까지는 안 빠진다), 땅 밑으로 그려지는 깊이
    public readonly int CaveThick, CaveEdge;    // 동굴 천장 두께, 옆으로 봐주는 폭
    int seen, bunch, sinceNew;          // 이 판에서 본 기믹(비트), 기믹 묶음 번호, 레벨이 열린 뒤 나온 묶음 수
    public bool Seen(Gimmick g) => (seen >> (int)g & 1) != 0;
    public Action<Gimmick, bool>? OnGimmick;    // 기믹 묶음이 나왔다 (처음인지)
    public bool Mute;                   // 길에 저절로 나오지 않는다 (자가 검사·장면: 놓아둔 것만 본다)
    // 기믹 치수는 칸px, 시간은 초
    public const double Bounce = 1150, PadSquash = .18, PadLead = .35, PadFloor = .5,   // 버섯: 튕기는 속도, 눌린 그림 시간, 2층 앞머리보다 얼마나 앞에, 2층 앞에 놓일 몫
        WaspFast = .25, RockFast = .4, MinSee = .9,     // 말벌·돌이 길보다 빠른 정도. 화면에 들어와 닿기까지 MinSee 초는 보이게 (좁은 화면에서는 덜 빠르다)
        WaveLen = 220, WaspLow = 2,                     // 말벌 물결 길이, 맨 아래일 때 땅에서 뜬 높이
        RockSpan = 160, HopSpan = 36, HopUp = 3,        // 높게 튀는 돌의 한 번 튀는 거리, 낮게 튀는 돌의 거리·높이
        PitMin = 30, PitPop = .55,                      // 구덩이 가장 좁은 폭, 빠졌다 튕겨 나오는 속도 (JumpSpeed 의 몫)
        CaveLead = .35,                                 // 동굴 앞을 더 비우는 시간
        FreshLead = .8, FreshAfter = 1.4;               // 처음 보는 기믹: 앞을 더 비우는 시간, 뒤 틈
    public const int Debut = 2;                         // 새 레벨에서 몇 묶음 뒤에 새 기믹이 나오는지
    public const double FloorHead = 11, FloorVary = 3, FloorReach = .8, FloorGap = .3, Runway = .75, FloorHeart = .4;

    // 0.8초 가까이 떠 있어서 넘을 타이밍에 여유가 있다
    public const double Gravity = 2400, JumpSpeed = 950, Drain = 2, HeartGain = 15, QuitHold = .5;
    bool ctrlWas, shiftWas;
    double bothHeld;
    static readonly string[] Ground1 = { "prop/acorn", "prop/mushroom", "prop/cactus", "prop/cheese", "prop/strawberry", "prop/cupcake", "prop/apple" };

    // 레벨별 난이도. 12레벨부터는 더 어려워지지 않는다
    static int Step(int level) => Math.Clamp(level, 1, 12) - 1;
    public static double StartSpeedOf(int level) => 380 + 22 * Step(level);
    public static double MaxSpeedOf(int level) => 660 + 28 * Step(level);
    static double AccelOf(int level) => 10 + Step(level);
    public static double HitCostOf(int level) => 22 + Step(level);
    static double HardShareOf(int level) => .25 + .04 * Step(level);   // 어려운 묶음(키 큰 것·연달아 둘·구름 뒤 장애물)이 나올 몫
    static double TimeGapOf(int level) => 1 - .04 * Step(level);       // 묶음 사이 틈(초): 내려와서 다음 것을 볼 시간. 빨라져도 시간은 같다
    static double HeartChanceOf(int level) => .35 - .02 * Step(level);
    public static int GoalOf(int level) => 700 + 40 * (level - 1);     // 이번 레벨에서 이만큼 벌면 다음 레벨. 실수 없이 달리면 한 판에 1200점쯤
    public static (double From, double To) FloorEveryOf(int level) => (22 - .9 * Step(level), 32 - .9 * Step(level));  // 2층이 나오는 사이(초)
    public static int UnlockOf(Gimmick g) => 2 + (int)g;
    public static int OpenAt(int level) => Math.Clamp(level - 1, 0, 5);    // 그 레벨에 열려 있는 기믹 수
    // 묶음 가운데 기믹이 차지하는 몫: 하나 열릴 때마다 7%씩, 다 열린 뒤(Lv6)에는 레벨마다 2%씩 더
    public static double GimmickShareOf(int level) => OpenAt(level) == 0 ? 0 : .10 + .07 * (OpenAt(level) - 1) + .02 * Math.Max(0, Step(level) - 5);
    static readonly double[] Weight = { .6, 1, 1, 1, .7 };  // 고를 때의 무게: 버섯(도움)과 동굴(길다)은 조금 덜

    public Runner(Rectangle track, double ground, int cell, RunBody body, Random rng, Func<string, int, (int, int, int, int)> measure, int level = 1)
    {
        (Track, Ground, Body, this.rng, this.measure) = (track, ground, body, rng, measure);
        PetScale = Math.Max(1, cell / 40);
        sf = cell / 120.0;
        double u = cell / 40.0;
        BoxW = Math.Max(2, (int)Math.Round(body.W * .8 * u));
        StandPx = Math.Max(2, (int)Math.Round(body.Stand * u));
        DuckPx = Math.Clamp((int)Math.Round(body.Duck * u), 1, StandPx - 1);
        CloudUp = (StandPx + DuckPx) / 2.0;
        (NormalMax, TallMax) = (.85 * StandPx, 1.3 * StandPx);
        Level = Math.Max(1, level);
        PetX = track.Left + track.Width * .22;
        Speed = StartSpeedOf(Level) * sf;
        nextSpawn = 500 * sf; // 처음엔 조금 달리다가

        (seedArt, heartArt, cloudArt) = (ArtOf("icon/seed", Px(1)), ArtOf("icon/heart", Px(1)), ArtOf("prop/cloud", Px(2)));
        // 땅 장애물을 키로 나눈다. 같은 그림도 몸이 작으면 키 큰 장애물이 되고, 너무 크면 안 나온다
        int lowest = -1, last = 0;
        for (int n = 1; n <= 3; n++)
        {
            int px = Px(n);
            if (px == last) continue; // 작은 배율에서는 1·2 가 같은 크기가 된다
            last = px;
            foreach (var name in Ground1)
            {
                int id = ArtOf(name, px), h = arts[id].H;
                if (lowest < 0 || h < arts[lowest].H) lowest = id;
                if (h <= NormalMax) { normal.Add(id); if (n == 1) small.Add(id); }
                else if (h <= TallMax) tall.Add(id);
            }
        }
        if (normal.Count == 0) normal.Add(lowest);  // 몸이 아주 작아도 넘을 것은 있어야 한다
        if (small.Count == 0) small.AddRange(normal);
        if (tall.Count == 0) { int top = normal[0]; foreach (int id in normal) if (arts[id].H > arts[top].H) top = id; tall.Add(top); }

        // 2층 발판: 그림은 볼록이 배율 그대로 (그림 1px = 칸 1px). 밑면이 머리 위 FloorHead 칸px, 윗면은 뛰어 닿는 높이 안
        LedgeArt = (ArtOf("prop/ledge_l", Px(3)), ArtOf("prop/ledge_m", Px(3)), ArtOf("prop/ledge_r", Px(3)));
        FloorThick = arts[LedgeArt.M].H;
        JumpUp = JumpSpeed * JumpSpeed * sf / (2 * Gravity);
        int reach = (int)(FloorReach * JumpUp);
        FloorLow = Math.Min(StandPx + (int)Math.Round(FloorHead * u) + FloorThick, reach);
        FloorHigh = Math.Min(FloorLow + (int)Math.Round(FloorVary * u), reach);
        floorAt = Rand(6, 12);  // 첫 2층은 조금 달린 뒤에

        // 기믹 그림: 볼록이 배율 그대로 (말벌만 구름처럼 2/3)
        (padArt, waspArt, rockArt) = (ArtOf("run/shroom", Px(3)), ArtOf("run/wasp", Px(2)), ArtOf("run/rock", Px(3)));
        PitArt = (ArtOf("run/pit_l", Px(3)), ArtOf("run/pit_m", Px(3)), ArtOf("run/pit_r", Px(3)));
        CaveArt = (ArtOf("run/cave_l", Px(3)), ArtOf("run/cave_m", Px(3)), ArtOf("run/cave_r", Px(3)));
        (PitEdge, PitDeep) = (arts[PitArt.L].W, arts[PitArt.M].H * 4 / 5);   // 그림 열 줄 가운데 여덟 줄이 땅 밑
        (CaveThick, CaveEdge) = (arts[CaveArt.M].H, arts[CaveArt.L].W / 4);
    }

    // 그림 배율: 배율 3 에서 n 이던 것을 볼록이 배율에 맞춘다 (모니터 배율이 커지면 장애물도 같이 커진다)
    public int Px(int n) => Math.Max(1, (int)Math.Round(n * PetScale / 3.0));

    int ArtOf(string sprite, int scale)
    {
        if (artIndex.TryGetValue((sprite, scale), out int id)) return id;
        var (w, h, ox, oy) = measure(sprite, scale);
        arts.Add(new RunArt(sprite, scale, w, h, ox, oy));
        return artIndex[(sprite, scale)] = arts.Count - 1;
    }

    // 볼록이 몸. 슬라이드하면 낮아진다
    public Rectangle PetBox
    {
        get
        {
            int h = Sliding && Lift == 0 ? DuckPx : StandPx;
            return new Rectangle((int)(PetX - BoxW / 2.0), (int)(Ground - Lift) - h, BoxW, h);
        }
    }

    public void Jump()
    {
        if (Over || Jumps >= 2) return;
        Vy = -JumpSpeed * sf * (Jumps == 0 ? 1 : .85);
        Jumps++;
        Sliding = false;
        OnEvent?.Invoke(Jumps == 1 ? "jump" : "jump2");
    }

    public void Slide(bool on) => Sliding = on && Lift == 0 && Vy == 0;    // 땅에서만 엎드린다 (발판 위에는 구름이 없다)

    // 발판 위에서 뚫고 내려간다 (동굴 천장 위에서는 못 내려간다: 바위다)
    public void Drop()
    {
        if (Over || Under is not { Kind: ThingKind.Floor } || !Landed) return;
        (through, Under) = (Under, null);
        OnEvent?.Invoke("drop");
    }

    // 발판 윗면 높이 (땅에서)
    public double TopOf(Thing floor) => Ground - floor.Y + floor.H;

    // 발판 윗면에서 땅까지 떨어지는 시간(초)
    public double FallSec(double top) => Math.Sqrt(2 * top / (Gravity * sf));

    // 키 상태를 받는다. Ctrl 누르는 순간 점프, Shift 누르는 동안 슬라이드(발판 위에서는 누르는 순간 뚫고 내려가기),
    // 둘을 0.5초 함께 누르면 끝 — 끝내려면 true. 슬라이드 중에 Ctrl 을 톡 쳐서 뛰는 건 짧아서 안 끝난다
    public bool Keys(bool ctrl, bool shift, double dt)
    {
        if (ctrl && !ctrlWas) Jump();
        if (shift && !shiftWas) Drop();
        (ctrlWas, shiftWas) = (ctrl, shift);
        Slide(shift);
        bothHeld = ctrl && shift ? bothHeld + dt : 0;
        return bothHeld >= QuitHold;
    }

    // scale 은 그림 배율(화면 px). 길에 저절로 나오는 것은 Px(n) 으로 볼록이 배율을 따른다
    public Thing Add(ThingKind kind, string sprite, int scale, double x, double bottom) => Put(kind, ArtOf(sprite, scale), x, bottom);

    Thing Put(ThingKind kind, int art, double x, double bottom)
    {
        var a = arts[art];
        var t = new Thing { Kind = kind, Sprite = a.Sprite, Art = art, Scale = a.Scale, W = a.W, H = a.H, Ox = a.Ox, Oy = a.Oy, X = x, Y = bottom };
        Things.Add(t);
        return t;
    }

    void Spawn()
    {
        double x = Track.Right + 40;
        if (Level >= 2 && Pick() is { } pick) { SpawnGimmick(pick, x); return; }   // 레벨 1 은 전과 똑같이 (난수도 더 뽑지 않는다)
        Thing Block(double at, List<int> pool) => Put(ThingKind.Block, pool[rng.Next(pool.Count)], at, Ground);
        Thing Cloud(double at) => Put(ThingKind.Fly, cloudArt, at, Ground - CloudUp);
        void Seed(double at, double up) => Put(ThingKind.Seed, seedArt, at, Ground - up);
        void Arch(Thing o) { for (int i = 0; i < 3; i++) Seed(o.X + o.W / 2.0 - 34 * sf + i * 34 * sf, o.H + (i == 1 ? 70 : 50) * sf); }
        double end = x; // 이번 묶음 끝: 마지막 장애물의 오른쪽
        if (rng.NextDouble() < .12) // 씨앗 무지개(선 키의 0.3~1배 높이), 가끔 하트(선 키의 2배 높이: 뛰어야 닿는다)
        {
            for (int i = 0; i < 6; i++) Seed(x + i * 34 * sf, (.3 + .7 * Math.Sin(i / 5.0 * Math.PI)) * StandPx);
            end = x + 6 * 34 * sf;
            if (rng.NextDouble() < HeartChanceOf(Level)) Put(ThingKind.Heart, heartArt, x + 100 * sf, Ground - 2 * StandPx);
        }
        else if (rng.NextDouble() < HardShareOf(Level))
        {
            switch (rng.Next(3))
            {
                case 0: // 키 큰 장애물: 일찍 뛰거나 더블 점프
                    var t = Block(x + 120 * sf, tall);
                    Arch(t);
                    end = t.X + t.W;
                    break;
                case 1: // 연달아 둘: 한 번에 넘기엔 멀고, 착지하자마자 뛰면 넘는다. 빨라지면 사이도 함께 벌어진다
                    var a = Block(x + 100 * sf, normal);
                    var b = Block(a.X + a.W + Speed * Rand(.55, .65), normal);
                    Arch(a); Arch(b);
                    end = b.X + b.W;
                    break;
                default: // 구름 밑으로 미끄러진 직후 땅 장애물: 일어나서 뛸 틈은 0.3~0.4초
                    var o = Cloud(x + 120 * sf);
                    var c = Block(o.X + o.W + 60 * sf + Speed * Rand(.3, .42), small);
                    Arch(c);
                    end = c.X + c.W;
                    break;
            }
        }
        else if (rng.NextDouble() < .6) // 땅 장애물 하나: 점프로 넘는다. 앞에 씨앗 줄, 위에 씨앗 아치
        {
            var o = Block(x + 160 * sf, normal);
            for (int i = 0; i < 4; i++) Seed(x + i * 34 * sf - 40 * sf, 18 * sf);
            Arch(o);
            end = o.X + o.W;
        }
        else // 공중 장애물: 슬라이드로 밑을 지난다
        {
            var o = Cloud(x + 120 * sf);
            for (int i = 0; i < 5; i++) Seed(o.X - 30 * sf + i * 34 * sf, 10 * sf);
            end = o.X + o.W;
        }
        // 다음 묶음은 이번 묶음이 지나가고 틈(초) 뒤에. 레벨이 오를수록 틈이 짧다
        nextSpawn = Distance + (end - x) + Speed * TimeGapOf(Level) * Rand(1, 1.4);
    }

    // 이번 묶음이 기믹이면 어느 것인지. 그 레벨에 새로 열린 기믹을 아직 못 봤으면 Debut 묶음 뒤에 그것부터 보여 준다
    Gimmick? Pick()
    {
        int open = OpenAt(Level), newest = Level <= 6 ? Level - 2 : -1;
        if (newest >= 0 && (seen >> newest & 1) == 0 && sinceNew++ >= Debut) return (Gimmick)newest;
        if (rng.NextDouble() >= GimmickShareOf(Level)) return null;
        double total = 0;
        for (int i = 0; i < open; i++) total += Weight[i] * (i == newest ? 2 : 1);
        double at = rng.NextDouble() * total;
        for (int i = 0; i < open; i++) if ((at -= Weight[i] * (i == newest ? 2 : 1)) < 0) return (Gimmick)i;
        return (Gimmick)(open - 1);
    }

    // 말벌·돌이 길보다 몇 배 빨리 오는지: 화면에 들어와 닿기까지 MinSee 초는 보이게 줄인다 (그래도 길보다는 빠르다)
    public double FastOf(double extra, double speed = 0) => 1 + Math.Clamp((Track.Right - PetX) / (MinSee * (speed > 0 ? speed : Speed)) - 1, .1, extra);

    // 말벌·돌의 밑면 높이(땅에서). 볼록이까지 남은 거리로만 정해진다: 닿을 때의 높이가 언제나 같다
    public double UpOf(Thing t)
    {
        double d = t.X + t.W / 2.0 - PetX, u = PetScale;
        if (t.Kind == ThingKind.Wasp)
        {
            double lo = WaspLow * u, c = Math.Cos(2 * Math.PI * d / (WaveLen * u));
            return lo + (CloudUp - lo) * (1 + (t.Way == 1 ? c : -c)) / 2;
        }
        double p = t.Way == 1 ? d / (RockSpan * u) + .5 : d / (HopSpan * u);
        p -= Math.Floor(p);
        return (t.Way == 1 ? CloudUp : HopUp * u) * 4 * p * (1 - p);
    }

    // 낮게 오는 말벌·돌을 넘으려면 발이 이 높이보다 위에 있어야 한다 (몸에 걸치는 동안 가장 높을 때)
    public double LowTop(Thing t) => t.H + (t.Kind == ThingKind.Wasp ? WaspLow + 2 : HopUp) * PetScale;

    // 말벌(Wasp)·돌(Rock)을 놓는다. center: 「가만히 있는 것이 여기 있을 때와 같은 때」 볼록이에게 닿는 자리 — 화면에서는 fast 배 더 멀리서 출발한다
    public Thing AddMover(ThingKind kind, int way, double center, double fast)
    {
        var t = Put(kind, kind == ThingKind.Wasp ? waspArt : rockArt, 0, Ground);
        (t.Way, t.Fast) = (way, fast);
        t.X = PetX + (center - PetX) * fast - t.W / 2.0;
        t.Y = Ground - UpOf(t);
        return t;
    }

    public Thing AddPad(double left) => Put(ThingKind.Pad, padArt, left, Ground);

    // 기믹 묶음 하나를 지금 내보낸다 (자가 검사: 길에 나오는 그대로의 꼴을 본다)
    public void AddBunch(Gimmick what) => SpawnGimmick(what, Track.Right + 40);

    // 길게 이어 붙이는 것(구덩이·동굴)의 가운데 조각 수: 전체 폭이 w 를 넘게
    public int PitTiles(double w) => Tiles(w, PitArt);
    public int CaveTiles(double w) => Tiles(w, CaveArt);
    int Tiles(double w, (int L, int M, int R) art) => Math.Max(1, (int)Math.Ceiling((w - arts[art.L].W - arts[art.R].W) / arts[art.M].W));

    // 구덩이 하나: left 는 왼쪽 끝, tiles 는 가운데 조각 수
    public Thing AddPit(double left, int tiles)
    {
        var (l, m, r) = (arts[PitArt.L], arts[PitArt.M], arts[PitArt.R]);
        var p = new Thing { Kind = ThingKind.Pit, Sprite = m.Sprite, Art = PitArt.M, Scale = m.Scale, W = l.W + Math.Max(1, tiles) * m.W + r.W, H = m.H, X = left, Y = Ground + PitDeep };
        Pits.Add(p);
        return p;
    }

    // 동굴 천장 하나: 밑면이 구름 바닥 높이, 윗면은 올라설 수 있다. look 은 천장 무늬(박쥐·고드름돌) 씨
    public Thing AddCave(double left, int tiles, int look = 0)
    {
        var (l, m, r) = (arts[CaveArt.L], arts[CaveArt.M], arts[CaveArt.R]);
        var c = new Thing { Kind = ThingKind.Cave, Sprite = m.Sprite, Art = CaveArt.M, Scale = m.Scale, W = l.W + Math.Max(1, tiles) * m.W + r.W, H = m.H, X = left, Y = Ground - CloudUp, Way = look };
        Caves.Add(c);
        return c;
    }

    // 버섯으로 튀어 떠 있는 시간(초)과 꼭대기 높이(땅에서)
    public const double BounceSec = 2 * Bounce / Gravity;
    public double BounceUp => Bounce * Bounce * sf / (2 * Gravity);

    // 기믹 묶음 하나. 묶음 안에는 그 기믹(과 씨앗·하트)만 있다. fresh: 이 판에서 처음 — 가장 쉬운 꼴로 혼자, 앞뒤를 넉넉히 비운다
    void SpawnGimmick(Gimmick what, double x)
    {
        bool fresh = !Seen(what);
        seen |= 1 << (int)what;
        bunch++;
        bool hard = !fresh && rng.NextDouble() < HardShareOf(Level);
        // 시간으로 정한 틈·길이는 「닿을 때」의 빠르기 sp 로 거리를 잰다: 오는 동안에도 길이 빨라져서 (넓은 화면일수록 더), 나올 때 빠르기로 재면 닿을 때는 그보다 짧은 시간이 된다.
        // 앞 묶음과의 틈(나올 때 빠르기로 잰 것)도 그만큼 더 벌린다
        double sp = Math.Min(MaxSpeedOf(Level) * sf, Speed + AccelOf(Level) * sf * (x - PetX) / Speed);
        double step = 34 * sf, at = x + (sp - Speed) * 1.4 + (fresh ? sp * FreshLead : 0), end = at, gap = sp * TimeGapOf(Level) * Rand(1, 1.4), enter = 0;
        int first = Things.Count, firstPit = Pits.Count, firstCave = Caves.Count;
        void Seed(double sx, double up) => Put(ThingKind.Seed, seedArt, sx, Ground - up);
        void Arch(double cx, double h) { for (int i = 0; i < 3; i++) Seed(cx + (i - 1) * step - arts[seedArt].W / 2.0, h + (i == 1 ? 70 : 50) * sf); }
        void Row(double cx) { for (int i = 0; i < 4; i++) Seed(cx + (i - 1.5) * step - arts[seedArt].W / 2.0, 10 * sf); }
        switch (what)
        {
            case Gimmick.Pad: // 버섯 점프대: 튄 길을 따라 씨앗, 꼭대기에 가끔 하트. 내려설 자리까지가 이 묶음이다
            {
                var p = AddPad(at + 60 * sf);
                double from = p.X + p.W / 6.0 - BoxW / 2.0, v = Bounce * sf, g = Gravity * sf;   // from: 달려와 밟는 순간 몸 가운데가 있는 자리
                bool heart = !fresh && rng.NextDouble() < HeartChanceOf(Level);
                for (int i = 1; i <= 7; i++)
                {
                    double t = BounceSec * i / 8, up = v * t - g * t * t / 2 + .4 * StandPx;
                    if (i == 4 && heart) Put(ThingKind.Heart, heartArt, from + sp * t - arts[heartArt].W / 2.0, Ground - up);
                    else Seed(from + sp * t - arts[seedArt].W / 2.0, up);
                }
                end = from + sp * BounceSec + BoxW;
                gap = Math.Max(gap, sp * Runway);
                break;
            }
            case Gimmick.Wasp:
            case Gimmick.Rock: // 하나, 또는 (어려운 묶음 몫만큼) 둘. 낮게 오면 위에 씨앗 아치(뛰어라), 높게 오면 땅에 씨앗 줄(엎드려라)
            {
                bool wasp = what == Gimmick.Wasp;
                double fast = FastOf(wasp ? WaspFast : RockFast, sp), c = at + 120 * sf;
                int way = rng.Next(2);
                for (int n = 0; n < (hard ? 2 : 1); n++)
                {
                    var t = AddMover(wasp ? ThingKind.Wasp : ThingKind.Rock, way, c, fast);
                    if (way == 0) Arch(c, LowTop(t)); else Row(c);
                    end = c + t.W / 2.0;
                    enter = Distance + (end - PetX) - (x - PetX) / fast + t.W;   // 이것이 화면에 들어오는 때
                    c = end + sp * Rand(.9, 1.1);
                    way = wasp ? rng.Next(2) : 1 - way;     // 돌 둘은 낮게 하나·높게 하나
                }
                break;
            }
            case Gimmick.Pit: // 하나, 또는 사이에 내려설 섬을 두고 둘
            {
                double left = at + 120 * sf;
                for (int n = 0; n < (hard ? 2 : 1); n++)
                {
                    var p = AddPit(left, Tiles(Math.Max(PitMin * PetScale, sp * Rand(.16, .26)), PitArt));
                    Arch(p.X + p.W / 2.0, 0);
                    end = p.X + p.W;
                    left = end + sp * Rand(.6, .75);
                }
                break;
            }
            default: // 동굴: 안에 엎드린 높이로 씨앗 넷씩. 나온 뒤(지붕 끝에서 떨어진 뒤)에는 발판 끝처럼 비운다
            {
                double sec = fresh ? 1.2 : Rand(1.3, 1.5 + .08 * Step(Level));
                var c = AddCave(at + sp * CaveLead, Tiles(sp * sec, CaveArt), rng.Next(1000));
                for (double sx = c.X + sp * .3; sx + 2 * step < c.X + c.W - sp * .15; sx += sp * .55) Row(sx);
                end = c.X + c.W;
                gap = Math.Max(gap, sp * (FallSec(TopOf(c)) + Runway));
                break;
            }
        }
        for (int i = first; i < Things.Count; i++) (Things[i].Bunch, Things[i].Fresh) = (bunch, fresh);
        for (int i = firstPit; i < Pits.Count; i++) (Pits[i].Bunch, Pits[i].Fresh) = (bunch, fresh);
        for (int i = firstCave; i < Caves.Count; i++) (Caves[i].Bunch, Caves[i].Fresh) = (bunch, fresh);
        if (fresh) gap = Math.Max(gap, sp * FreshAfter);
        nextSpawn = Math.Max(Distance + (end - x) + gap, enter);
        OnGimmick?.Invoke(what, fresh);
    }

    // 발판 하나를 놓는다: left 는 왼쪽 끝, tiles 는 가운데 조각 수, top 은 윗면 높이(땅에서)
    public Thing AddFloor(double left, int tiles, double top)
    {
        var (l, m, r) = (arts[LedgeArt.L], arts[LedgeArt.M], arts[LedgeArt.R]);
        var f = new Thing { Kind = ThingKind.Floor, Sprite = m.Sprite, Art = LedgeArt.M, Scale = m.Scale, W = l.W + Math.Max(1, tiles) * m.W + r.W, H = m.H, X = left, Y = Ground - top + m.H };
        Floors.Add(f);
        return f;
    }

    // 2층 묶음: 발판 하나, 또는 연달아 둘 (어려운 묶음 몫만큼). 위에는 씨앗과 가끔 하트, 밑으로는 보통 장애물이 계속 온다
    void SpawnFloors()
    {
        double x = Track.Right + 40, at = x + 40 * sf, step = 34 * sf;
        int count = rng.NextDouble() < HardShareOf(Level) ? 2 : 1;
        // 버섯 점프대(Lv2 부터): 2층 앞에 가끔 놓인다. 밟으면 튀어서 (첫) 발판에 내려선다. 버섯이 화면 밖에서 들어오게 발판을 그만큼 뒤로.
        // 이 판에서 버섯을 (혼자 나온 것으로) 한 번 본 뒤에만: 처음 보는 버섯이 2층·장애물과 함께 나오지 않게
        bool pad = Level >= 2 && Seen(Gimmick.Pad) && rng.NextDouble() < PadFloor;
        if (pad)
        {
            var p = AddPad(at + Speed * .1);
            at = p.X + p.W / 6.0 + Speed * PadLead;
        }
        double first = at, end = at, fall = 0;
        Span<double> ends = stackalloc double[2];
        for (int n = 0; n < count; n++)
        {
            double sec = count == 1 ? Rand(1.7, 2.4) : Rand(1.2, 1.6);
            int top = rng.Next(FloorLow, FloorHigh + 1);
            var f = AddFloor(at, (int)Math.Ceiling(Speed * sec / arts[LedgeArt.M].W), top);
            ends[n] = end = f.X + f.W;
            fall = FallSec(top);
            double bow = f.X + Speed * (count == 1 ? .6 : .3);
            if (count == 1) for (int i = 0; i < 4; i++) Put(ThingKind.Seed, seedArt, f.X + Speed * .2 + i * step, Ground - top - 18 * sf);   // 올라서자마자 한 줄
            for (int i = 0; i < 6; i++) Put(ThingKind.Seed, seedArt, bow + i * step, Ground - top - (.3 + .7 * Math.Sin(i / 5.0 * Math.PI)) * StandPx);
            // 하트는 뛰어야 닿는다. 뛰고 나서 같은 발판에 다시 내려설 만큼 남았을 때만 둔다
            double heart = bow + 100 * sf;
            if (n == count - 1 && end - heart >= Speed * .95 && rng.NextDouble() < FloorHeart) Put(ThingKind.Heart, heartArt, heart, Ground - top - 2 * StandPx);
            at = end + Speed * FloorGap;
        }
        // 밑으로 오는 땅 장애물. 발판이 끝나는 자리부터 (떨어지는 시간 + Runway) 거리는 비운다
        double clear = Speed * (FallSec(FloorHigh) + Runway);
        for (double b = first + Speed * Rand(.6, .9); ; b += Speed * Rand(1, 1.35))
        {
            int art = normal[rng.Next(normal.Count)], w = arts[art].W;
            for (int n = 0; n < count; n++) if (b + w > ends[n] && b < ends[n] + clear) b = ends[n] + clear;
            if (b + w > end) break;
            for (int i = 0; i < 3; i++) Put(ThingKind.Seed, seedArt, b - (4 - i) * step, Ground - 18 * sf);
            Put(ThingKind.Block, art, b, Ground);
        }
        (double from, double to) = FloorEveryOf(Level);
        floorAt = Clock + Rand(from, to);
        nextSpawn = Distance + (end - x) + Speed * Math.Max(TimeGapOf(Level) * Rand(1, 1.4), fall + Runway);
    }

    double Rand(double a, double b) => a + rng.NextDouble() * (b - a);

    // 부딪히는 자리. 살짝 봐준다: 땅 장애물은 사방 1/6, 구름·말벌·돌은 가로만 (밑면은 눈에 보이는 그대로여야 엎드린 머리 위 틈이 맞다)
    public static Rectangle Hard(Thing t)
    {
        var box = t.Box;
        return Rectangle.Inflate(box, -box.Width / 6, t.Kind == ThingKind.Block ? -box.Height / 6 : 0);
    }

    // 맞았다: 체력이 닳고 0.8초 동안은 안 다친다. 안 다치는 동안이면 false
    bool Hurt()
    {
        if (Clock < InvUntil) return false;
        Energy -= HitCostOf(Level);
        (HurtUntil, InvUntil) = (Clock + .35, Clock + .8);
        OnEvent?.Invoke("hit");
        return true;
    }

    public void Update(double dt)
    {
        if (Over) return;
        Clock += dt;
        Distance += Speed * dt;
        Speed = Math.Min(MaxSpeedOf(Level) * sf, Speed + AccelOf(Level) * sf * dt); // 점점 빨라진다
        Energy -= Drain * dt;
        if (!Mute && Distance >= nextSpawn) { if (Clock >= floorAt) SpawnFloors(); else Spawn(); }
        double move = Speed * dt, off = Track.Left - 20;

        // 발밑: 땅(0)이거나, 몸 밑에 걸친 발판(동굴 천장 윗면) 가운데 발보다 높지 않은 가장 높은 윗면
        double rest = 0, left = PetX - BoxW / 2.0, right = left + BoxW;
        Thing? on = null, roof = null;  // roof: 머리 위(또는 몸이 걸친) 동굴 천장
        if (Floors.Count > 0)
        {
            int kept = 0;
            for (int i = 0; i < Floors.Count; i++)
            {
                var f = Floors[i];
                f.X -= move;
                if (f.X + f.W < off) { if (f == through) through = null; continue; }
                Floors[kept++] = f;
                if (f == through || f.X >= right || f.X + f.W <= left) continue;
                double top = TopOf(f);
                if (top <= Lift && top > rest) (rest, on) = (top, f);
            }
            if (kept < Floors.Count) Floors.RemoveRange(kept, Floors.Count - kept);
        }
        if (Caves.Count > 0)
        {
            int kept = 0;
            for (int i = 0; i < Caves.Count; i++)
            {
                var c = Caves[i];
                c.X -= move;
                if (c.X + c.W < off) continue;
                Caves[kept++] = c;
                if (c.X >= right || c.X + c.W <= left) continue;
                double top = TopOf(c);
                if (top <= Lift) { if (top > rest) (rest, on) = (top, c); }
                else roof = c;
            }
            if (kept < Caves.Count) Caves.RemoveRange(kept, Caves.Count - kept);
        }
        if (Lift > rest || Vy < 0) // 공중: 중력으로 내려온다. 내려오다 발밑에 닿으면 선다 (발판은 위에서 내려올 때만 막힌다)
        {
            Vy += Gravity * sf * dt;
            double next = Lift - Vy * dt;
            if (roof != null && Vy < 0 && next + StandPx > Ground - roof.Y) // 동굴 천장은 밑에서 못 뚫는다: 머리를 찧고 그 자리에서 떨어진다 (맞는 것은 아래에서 본다)
            {
                (Vy, next) = (0, Lift);
                if (Lift <= rest) (Lift, Rest, Jumps, Under) = (rest, rest, 0, on);
            }
            else if (Vy > 0 && next <= rest)
            {
                (Lift, Rest, Vy, Jumps, Under, through) = (rest, rest, 0, 0, on, null);
                OnEvent?.Invoke("land");
            }
            else
            {
                (Lift, Under) = (next, null);
                if (through != null && Lift < TopOf(through)) through = null;
            }
        }
        else (Rest, Under) = (rest, on);

        // 구덩이: 땅을 딛고 있는데 몸 가운데가 구멍 위다 → 빠진다 (맞은 것). 튕겨 올라오고, 그사이 한 번 더 뛸 수 있다
        if (Pits.Count > 0)
        {
            int kept = 0;
            bool fell = false;
            for (int i = 0; i < Pits.Count; i++)
            {
                var p = Pits[i];
                p.X -= move;
                if (p.X + p.W < off) continue;
                Pits[kept++] = p;
                fell |= Lift <= 0 && Vy == 0 && PetX > p.X + PitEdge && PetX < p.X + p.W - PitEdge;
            }
            if (kept < Pits.Count) Pits.RemoveRange(kept, Pits.Count - kept);
            if (fell)
            {
                Hurt();
                (Vy, Jumps, Sliding) = (-PitPop * JumpSpeed * sf, 1, false);
            }
        }

        // 한 번 돌면서 옮기고, 부딪힘·줍기를 보고, 지나간 것·먹은 것은 그 자리에서 빼낸다 (프레임마다 새로 잡는 메모리가 없게)
        var me = PetBox;
        // 동굴 천장에 몸이 걸쳤다 (서 있거나 뛰었다): 머리 콩
        if (roof != null && Lift < TopOf(roof) && me.IntersectsWith(new Rectangle((int)roof.X + CaveEdge, (int)(roof.Y - roof.H), roof.W - 2 * CaveEdge, roof.H))) Hurt();
        var grab = Rectangle.Inflate(me, (int)(8 * sf), (int)(8 * sf));
        bool safe = Clock < InvUntil;
        int keep = 0;
        for (int i = 0; i < Things.Count; i++)
        {
            var t = Things[i];
            t.X -= move * t.Fast;
            if (t.X + t.W < off) continue;
            if (t.Kind is ThingKind.Wasp or ThingKind.Rock) t.Y = Ground - UpOf(t);
            if (t.Kind is ThingKind.Block or ThingKind.Fly or ThingKind.Wasp or ThingKind.Rock)
            {
                if (!safe && Hard(t).IntersectsWith(me)) safe = Hurt();
            }
            else if (t.Kind == ThingKind.Pad) // 버섯 점프대: 내려오다(달리다) 발이 닿으면 튄다. 뛰어오르는 중에는 그냥 지나간다
            {
                if (Vy >= 0 && Lift <= t.H && t.X + t.W / 6 < me.Right && t.X + t.W - t.W / 6 > me.Left)
                {
                    (Vy, Jumps, Sliding, Under, t.Hot) = (-Bounce * sf, 1, false, null, Clock);
                    OnEvent?.Invoke("bounce");
                }
            }
            else if (t.Box.IntersectsWith(grab))
            {
                t.Gone = true;
                if (t.Kind == ThingKind.Seed) { Seeds++; OnEvent?.Invoke("seed"); }
                else { Energy = Math.Min(100, Energy + HeartGain); OnEvent?.Invoke("heart"); }
                continue;
            }
            Things[keep++] = t;
        }
        if (keep < Things.Count) Things.RemoveRange(keep, Things.Count - keep);
        if (Energy <= 0) { Energy = 0; Over = true; OnEvent?.Invoke("over"); return; }

        if (LevelScore >= GoalOf(Level)) // 목표를 넘기면 그 자리에서 다음 레벨
        {
            Level++;
            sinceNew = 0;
            levelFrom = Score;
            LevelAt = Clock;
            Speed = Math.Max(Speed, StartSpeedOf(Level) * sf);
            OnEvent?.Invoke("level");
        }
    }
}
