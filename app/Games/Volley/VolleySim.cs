using System.Drawing;

namespace Bolok;

// 볼록이 배구: 핵심 시뮬레이션·물리·규칙 (화면·앱과 무관한 순수 계산)
// 피카츄 배구 / Arcade Volleyball (1988) 식 물리와 랠리 규칙
// 손잡이는 VolleyTune.cs, 돌부리·블로킹·스파이크 보조는 VolleyRules.cs, 봇은 VolleyBot.cs (같은 클래스를 나눠 둠)
sealed partial class VolleySim
{
    public enum Phase { Serving, InPlay, PointScored, GameOver }
    public enum Actor { Player, Rival }

    // 마지막으로 공에 닿은 방식 · 득점 종류 (득점 표용, VolleyTune 5장)
    public enum Touch { None, Normal, Spike, Block, BlockMiss, Fumble, Dig }
    public enum PointKind { Spike, Block, Normal, Fumble, BlockMiss, Trip, Bird, Own }
    public const int PointKinds = 8;

    static int Dir(Actor a) => a == Actor.Player ? 1 : -1;        // 상대 쪽 (플레이어 +1, 라이벌 -1)
    static Actor Other(Actor a) => a == Actor.Player ? Actor.Rival : Actor.Player;

    // 공 종류 (YELL 2026-11-16: 과일 공 — 판마다 무작위, 가끔 배구공). 그림은 VolleyFruits
    public enum BallKind { Volleyball, Gyul, Orange, Apple, Coconut, Melon }
    public BallKind Ball { get; set; } = BallKind.Volleyball;
    public static bool HasStem(BallKind k) => k is BallKind.Gyul or BallKind.Orange or BallKind.Apple or BallKind.Melon; // 꼭지·잎이 위에 달린 것
    public static BallKind PickBall(Random rng) => rng.NextDouble() < .2 ? BallKind.Volleyball : (BallKind)(1 + rng.Next(5));

    // 스파이크 너그러움 (YELL 2026-11-16: 「스파이크가 생각보다 어렵다」): 닿기 직전 이만큼 안에 누른 Ctrl 도 친다
    public const double SpikeGraceSec = .18;

    // 선수 한 명의 몸 (플레이어·라이벌이 같은 규칙·같은 물리를 쓰도록 한 곳에 모은다)
    sealed class Body
    {
        public double X, Y, Vx, Vy;
        public bool OnGround = true;
        public string Pose = "idle";
        public double FumbleUntil = -1;
        public double SpikePressedAt = -9;
        public bool SpikeWas, SpikeReq, SmartSpike;  // SmartSpike: 봇이 「넘길 수 있을 때만」 스파이크한다고 알림 (닿는 순간 다시 계산)
        public int Axis, JumpSerial;
        public bool BlockJump;                       // 이번 점프가 네트 가까이에서 뛴 블로킹 점프인가
        public int BlockedSerial = -1;               // 이 점프(JumpSerial)에서 이미 막았다 (자동 스파이크 판: 한 점프에 한 번만 — 막고 치고 막고 치는 네트 실랑이를 끊는다)
        public double RunSec; public int RunDir; public bool RunRolled; // 땅에서 같은 방향으로 달린 시간, 이 달리기에서 돌을 뽑았나
        public double TripUntil = -9, TripDustX, TripDustTimer;          // 넘어져 있는 동안 · 먼지
        public bool Bot, SlideOk = true;             // Bot: 봇이 움직이는 몸 (BotInput 이 알린다 — 사람·검사용은 뛰면 늘 스파이크). SlideOk: 봇이 「이 공에는 몸을 던진다」고 한 것 (사람은 늘 참)
        public double SlideLeft, SlideRecoverUntil = -9;                 // 슬라이드: 더 갈 거리(>0 이면 미끄러지는 중) · 누워 있는 때까지
        public int SlideDir;                                             // 던진 방향 (+1 오른쪽 · -1 왼쪽)
        public double SlideX0, SlideX1, SlideTrailLeft;                  // 먼지 꼬리: 던진 자리 · 멈춘 자리(미끄러지는 동안은 지금 자리) · 남은 시간
    }
    readonly Body P = new(), R = new();
    Body Of(Actor a) => a == Actor.Player ? P : R;

    // 날아가는 새 (YELL 2026-11-16): 경기 중 가끔 시합장을 가로지른다. 공이 맞으면 공은 튕겨 나가고 새는 휘청 날아간다
    public bool Birds { get; set; }                 // 실제 경기만 켠다 (검사는 따로 켠다)
    public Random BirdRng { get; set; } = new();
    public const double BirdEverySec = 14;          // 경기 중 평균 이만큼에 한 마리
    public int BirdR => 6 * K;
    public bool BirdOn { get; private set; }
    public bool BirdHit { get; private set; }       // 공에 맞아 휘청 날아가는 중
    public double BirdX { get; set; }
    public double BirdY { get; set; }
    public double BirdVx { get; set; }
    public double BirdVy { get; set; }
    public double BirdTurn { get; private set; }    // 맞으면 빙글 (라디안)
    double birdBaseY, birdAge;
    bool birdSinceTouch;                            // 마지막 타구 뒤에 공이 새에 맞았다 (득점 종류 「새」)

    public void SpawnBird()
    {
        bool fromLeft = BirdRng.NextDouble() < .5;
        double lo = CeilingY + BirdR + 8 * K, hi = Math.Max(lo + 1, NetTopY - BirdR - 14 * K);
        (BirdOn, BirdHit, BirdTurn, birdAge) = (true, false, 0, 0);
        BirdX = fromLeft ? WallLeft - BirdR : WallRight + BirdR;
        birdBaseY = BirdY = lo + BirdRng.NextDouble() * (hi - lo);
        BirdVx = (fromLeft ? 1 : -1) * (85 + BirdRng.NextDouble() * 55) * ScaleRatio;
        BirdVy = 0;
        OnEvent?.Invoke("bird-in", fromLeft);
    }

    void UpdateBird(double dt)
    {
        if (!BirdOn)
        {
            if (Birds && Stage == Phase.InPlay && BirdRng.NextDouble() < dt / BirdEverySec) SpawnBird();
            return;
        }
        birdAge += dt;
        if (!BirdHit)
        {
            BirdX += BirdVx * dt;
            BirdY = birdBaseY + Math.Sin(birdAge * 5) * 3 * K; // 날갯짓에 맞춰 살짝 오르내림
            double dx = BallX - BirdX, dy = BallY - BirdY, d = Math.Sqrt(dx * dx + dy * dy), min = BallR + BirdR;
            if (d < min)
            {
                double nx = d > .001 ? dx / d : 0, ny = d > .001 ? dy / d : -1, dot = BallVx * nx + BallVy * ny;
                if (dot < 0) { BallVx -= 2 * dot * nx; BallVy -= 2 * dot * ny; } // 새에 맞고 튕긴다
                BallVx *= .92; BallVy *= .92;
                BallX = BirdX + nx * min; BallY = BirdY + ny * min;
                BirdHit = true;
                birdSinceTouch = true;
                spikeWatchBy = -1;
                BirdVx = -nx * 240 * ScaleRatio + BirdVx * .3; // 맞은 반대쪽으로 휘청
                BirdVy = -Math.Abs(ny) * 120 * ScaleRatio - 160 * ScaleRatio;
                OnEvent?.Invoke("bird", null);
            }
        }
        else
        {
            BirdVy += Gravity * .35 * dt;
            BirdX += BirdVx * dt;
            BirdY += BirdVy * dt;
            BirdTurn += 9 * dt * Math.Sign(BirdVx == 0 ? 1 : BirdVx);
        }
        if (BirdX < WallLeft - 3 * BirdR || BirdX > WallRight + 3 * BirdR || BirdY > FloorY + 4 * BirdR || BirdY < Court.Top - 30 * K) BirdOn = false;
    }

    // 꼭지 헤딩 실수 (YELL 2026-11-16): 머리에 닿는 순간 꼭지가 머리 쪽 ±T.StemFumbleDeg 안을 향하면 공이 힘없이 툭 떨어지고 펫은 움찔
    public const double FumbleSec = .6;
    public double PlayerFumbleUntil { get => P.FumbleUntil; set => P.FumbleUntil = value; }
    public double RivalFumbleUntil { get => R.FumbleUntil; set => R.FumbleUntil = value; }
    double lastFumbleAt = -9;

    public readonly int K;
    public readonly double ScaleRatio;
    public readonly Random Rnd;
    public VolleyTune T { get; }

    // 시합장 영역 (화면 좌표)
    public Rectangle Court { get; private set; }
    public bool IsDesktop { get; private set; }
    public int TitleH { get; private set; }

    public int WallLeft => Court.Left + 1;
    public int WallRight => Court.Right - 1;
    public int CeilingY => Court.Top + TitleH;
    public int FloorY => Court.Bottom - 1;

    // 네트
    public int NetX => (WallLeft + WallRight) / 2;
    public int NetH { get; private set; }
    public int NetTopY => FloorY - NetH;
    public int NetW => Math.Max(3, K + 1); // 그물 기둥 굵기 (YELL 2026-11-16: 조금 더 두껍게 — 배율 3에서 2 → 4px)
    public int CapR => Math.Max(3, 3 * K / 2);

    // 선수 크기 및 물리 상수
    public int PetR => 14 * K;
    public double WalkSpeed => 240 * ScaleRatio;
    public double JumpVy => -T.JumpV * ScaleRatio; // YELL 2026-11-17: 점프 높이 ×1.18 (높이는 속도의 제곱: 400 → 435)
    public double Gravity => 850 * ScaleRatio;

    // 공 크기 및 물리 상수
    public const int BallCells = 8; // 공 반지름 (칸). YELL 2026-11-16: 공을 두 배쯤 (4 → 8)
    public int BallR => BallCells * K;
    public double BallGravity => 680 * ScaleRatio;
    public double HitSpeed => 380 * ScaleRatio;
    public double SpikeSpeed => T.SpikeSpeed * ScaleRatio;
    public double SpikeVy => T.SpikeVy * ScaleRatio;

    // 선수 상태 (Feet Y = PlayerY, RivalY)
    public double PlayerX { get => P.X; set => P.X = value; }
    public double PlayerY { get => P.Y; set => P.Y = value; }
    public double PlayerVx { get => P.Vx; set => P.Vx = value; }
    public double PlayerVy { get => P.Vy; set => P.Vy = value; }
    public bool PlayerOnGround { get => P.OnGround; set => P.OnGround = value; }
    public string PlayerPose { get => P.Pose; set => P.Pose = value; }

    public double RivalX { get => R.X; set => R.X = value; }
    public double RivalY { get => R.Y; set => R.Y = value; }
    public double RivalVx { get => R.Vx; set => R.Vx = value; }
    public double RivalVy { get => R.Vy; set => R.Vy = value; }
    public bool RivalOnGround { get => R.OnGround; set => R.OnGround = value; }
    public string RivalPose { get => R.Pose; set => R.Pose = value; }

    // 공 상태
    public double BallX { get; set; }
    public double BallY { get; set; }
    public double BallVx { get; set; }
    public double BallVy { get; set; }
    public double BallSpin { get; set; }

    // 점수 및 랠리 진행
    public const int TargetScore = 5;
    public int ScorePlayer { get; set; }
    public int ScoreRival { get; set; }
    public Phase Stage { get; set; } = Phase.Serving;
    public Actor CurrentServer { get; set; } = Actor.Player;
    public Actor? LastScorer { get; set; }
    public Actor? LastHitActor { get; set; }
    public Touch LastTouch { get; private set; }
    public int RallyHits { get; set; }
    public int TotalRallies { get; set; }

    // 득점 표 (VolleyCheck.Table 이 읽는다): [득점한 쪽 * PointKinds + 종류], 생성자에서 한 번만 만든다
    public PointKind LastPoint { get; private set; }
    public readonly int[] Points = new int[2 * PointKinds];
    public readonly int[] StoneCount = new int[2], TripCount = new int[2], BlockHit = new int[2], BlockMissCount = new int[2], SpikeCount = new int[2], SpikeOver = new int[2];
    public readonly int[] SlideCount = new int[2], DigCount = new int[2], DigOver = new int[2];   // 슬라이드 · 디그 · 디그한 공이 상대 코트로 넘어간 것
    public int PointTotal, HitSum;       // 지금까지 난 점수 · 그 랠리들의 타구 수 합 (0.25초 안에 같은 선수가 몸에 계속 닿은 것은 한 번)
    int RallyTouches;
    double lastTouchAt = -9;
    public double RallySecSum;           // 랠리 길이(초) 합: 첫 타구부터 바닥에 닿을 때까지
    double rallyStart;
    int spikeWatchBy = -1;               // 방금 친 스파이크가 네트를 넘었나 지켜보는 중 (치는 쪽, 아니면 -1)
    int digWatchBy = -1;                 // 방금 디그한 공이 같은 편 손으로 네트를 넘었나 지켜보는 중 (디그한 쪽, 아니면 -1)
    public int TouchSerial { get; private set; }   // 공이 몸에 닿을 때마다 +1 (봇이 새 공을 알아본다)

    public double Clock { get; set; }
    public double PauseUntil { get; set; }
    public double EndAt { get; set; }
    public bool Done => Stage == Phase.GameOver;
    public bool Won => Done && ScorePlayer >= TargetScore;

    // 연출 효과
    public double ShakeUntil { get; set; }
    public bool ShakeActive => Clock < ShakeUntil;

    public bool SpikeEffectActive { get; set; }
    public double SpikeX { get; set; }
    public double SpikeY { get; set; }
    public int SpikeDir { get; set; } // 1: 오른쪽으로, -1: 왼쪽으로
    public double SpikeTimer { get; set; }

    public bool DustActive { get; set; }
    public double DustX { get; set; }
    public double DustY { get; set; }
    public double DustTimer { get; set; }

    public int NewlyLitTeam { get; set; } = -1; // 0: Player, 1: Rival
    public int NewlyLitIndex { get; set; } = -1;
    public double SparkleTimer { get; set; }

    // 이벤트 콜백 (소리·로그)
    public Action<string, object?>? OnEvent;

    double curDt = 1 / 60.0;
    public int ServeSerial { get; private set; }   // 서브 때마다 +1 (봇이 새 랠리를 알아본다)
    BotState rivalBot;
    internal bool BotsOff;               // 검사 전용: 라이벌 봇을 멈춘다 (공이 가는 길을 방해하지 않게). 그동안 라이벌은 아래 값을 누른 것처럼 움직인다
    internal int ForceAxis;
    internal bool ForceJump;
    internal void SetLast(Actor? who, Touch how) { LastHitActor = who; LastTouch = how; } // 검사 전용: 마지막 닿음을 정한다

    public VolleySim(Rectangle court, bool isDesktop, int scale, Random? rnd = null, VolleyTune? tune = null)
    {
        K = scale;
        ScaleRatio = scale / 3.0;
        Rnd = rnd ?? new Random();
        T = tune ?? VolleyTune.Now;
        IsDesktop = isDesktop;
        TitleH = isDesktop ? 0 : Math.Max(28, 9 * K);
        SetCourt(court, isDesktop);

        ResetMatch(Actor.Player);
    }

    public void SetCourt(Rectangle court, bool isDesktop)
    {
        Court = court;
        IsDesktop = isDesktop;
        TitleH = isDesktop ? 0 : Math.Max(28, 9 * K);
        NetH = Math.Clamp((int)(Court.Height * T.NetRatio), T.NetMinCells * K, T.NetMaxCells * K);

        ClampPlayersToCourt();
    }

    public void ResetMatch(Actor firstServer)
    {
        ScorePlayer = 0;
        ScoreRival = 0;
        CurrentServer = firstServer;
        LastScorer = null;
        TotalRallies = 0;
        lastStoneSide = -1;
        Array.Clear(Points); Array.Clear(StoneCount); Array.Clear(TripCount); Array.Clear(BlockHit); Array.Clear(BlockMissCount); Array.Clear(SpikeCount); Array.Clear(SpikeOver);
        Array.Clear(SlideCount); Array.Clear(DigCount); Array.Clear(DigOver);
        PointTotal = HitSum = 0;
        RallySecSum = 0;

        ResetPositionsForServe(firstServer);
    }

    public void ResetPositionsForServe(Actor server)
    {
        Stage = Phase.Serving;
        CurrentServer = server;
        LastHitActor = null;
        LastTouch = Touch.None;
        birdSinceTouch = false;
        spikeWatchBy = -1;
        digWatchBy = -1;
        RallyHits = 0;
        RallyTouches = 0;
        ServeSerial++;

        // 선수 기본 위치
        ResetBody(P);
        ResetBody(R);
        PlayerX = (WallLeft + NetX) / 2.0;
        RivalX = (NetX + WallRight) / 2.0;
        ClearStone();
        BlockFlashUntil = -1;
        rivalBot.BlockPlan = false;
        rivalBot.Hop = false;

        // 서브하는 쪽 머리 위에서 공이 떨어짐
        if (server == Actor.Player)
        {
            BallX = PlayerX;
            BallY = CeilingY + 25 * K;
            BallVx = 40 * ScaleRatio;
            BallVy = 20 * ScaleRatio;
        }
        else
        {
            BallX = RivalX;
            BallY = CeilingY + 25 * K;
            BallVx = -40 * ScaleRatio;
            BallVy = 20 * ScaleRatio;
        }
        BallSpin = 0;

        OnEvent?.Invoke("serve", server);
    }

    void ResetBody(Body b)
    {
        b.Y = FloorY;
        b.Vx = 0;
        b.Vy = 0;
        b.OnGround = true;
        b.Pose = "idle";
        b.BlockJump = false;
        b.RunSec = 0; b.RunDir = 0; b.RunRolled = false;
        b.TripUntil = -9; b.TripDustTimer = 0;
        b.SlideLeft = 0; b.SlideRecoverUntil = -9; b.SlideTrailLeft = 0;
    }

    public void ForceEnd(bool win)
    {
        if (win)
        {
            ScorePlayer = TargetScore;
            ScoreRival = Math.Min(ScoreRival, TargetScore - 1);
        }
        else
        {
            ScoreRival = TargetScore;
            ScorePlayer = Math.Min(ScorePlayer, TargetScore - 1);
        }

        Stage = Phase.GameOver;
        EndAt = Clock;
        PlayerPose = win ? "happy" : "dizzy";
        RivalPose = win ? "dizzy" : "happy";
        OnEvent?.Invoke("end", win);
    }

    public void Update(double dt, int inputAxisX = 0, bool inputJump = false, bool inputSpike = false)
    {
        Clock += dt;
        curDt = dt;

        // 타이머 업데이트
        if (SpikeTimer > 0)
        {
            SpikeTimer -= dt;
            if (SpikeTimer <= 0) SpikeEffectActive = false;
        }
        if (DustTimer > 0)
        {
            DustTimer -= dt;
            if (DustTimer <= 0) DustActive = false;
        }
        if (SparkleTimer > 0)
        {
            SparkleTimer -= dt;
        }
        if (P.TripDustTimer > 0) P.TripDustTimer -= dt;
        if (R.TripDustTimer > 0) R.TripDustTimer -= dt;
        if (P.SlideTrailLeft > 0) P.SlideTrailLeft -= dt;
        if (R.SlideTrailLeft > 0) R.SlideTrailLeft -= dt;

        // 득점 후 서브 대기 일시 정지
        if (Stage == Phase.PointScored)
        {
            if (Clock >= PauseUntil)
            {
                TotalRallies++;
                ResetPositionsForServe(CurrentServer);
            }
            return;
        }

        if (Stage == Phase.GameOver)
        {
            return;
        }

        // 1. 플레이어 조작 & 물리 (Ctrl 은 누른 순간부터 SpikeGraceSec 동안 유효). 자동 스파이크면 사람은 공중에 있는 동안 늘 스파이크하려 하고 Ctrl 은 읽지 않는다
        if (T.AutoSpike && !P.Bot) (P.SpikeReq, P.SmartSpike) = (true, true);
        else
        {
            if (inputSpike && !P.SpikeWas) P.SpikePressedAt = Clock;
            P.SpikeWas = inputSpike;
            P.SpikeReq = inputSpike || Clock - P.SpikePressedAt <= SpikeGraceSec;
        }
        MoveActor(Actor.Player, inputAxisX, inputJump, dt);

        // 2. 라이벌 봇 & 물리: 플레이어와 같은 이동·점프 함수. 보조가 켜져 있으면 Ctrl 너그러움도 같다
        int axis = 0; bool jump = false, spike = false;
        if (!BotsOff) BotInput(Actor.Rival, T.Rival, ref rivalBot, Rnd, out axis, out jump, out spike);
        else (R.Bot, axis, jump, spike) = (false, ForceAxis, ForceJump, false);
        if (T.AutoSpike && !R.Bot) (R.SpikeReq, R.SmartSpike) = (true, true);   // 봇이 아닌 라이벌(검사용)도 사람과 같다
        else if (T.SpikeAssist)
        {
            if (spike && !R.SpikeWas) R.SpikePressedAt = Clock;
            R.SpikeWas = spike;
            R.SpikeReq = spike || Clock - R.SpikePressedAt <= SpikeGraceSec;
        }
        else R.SpikeReq = spike;
        MoveActor(Actor.Rival, axis, jump, dt);

        // 2-1. 돌부리 (선수 이동 뒤·공 앞)
        UpdateStone(dt);

        // 3. 공 물리 및 충돌
        UpdateBall(dt);

        // 3-1. 날아가는 새
        UpdateBird(dt);

        // 4. 상태 및 포즈 동기화
        SyncPoses();
    }

    // 점프를 시작한다 (바닥에서). 플레이어·라이벌·디버그가 같은 길
    void StartJump(Actor who, Body b)
    {
        b.Vy = JumpVy;
        b.OnGround = false;
        b.BlockJump = T.Blocks && Math.Abs(b.X - NetX) <= T.BlockZoneBodies * 2 * PetR;
        b.JumpSerial++;
        OnEvent?.Invoke("jump", who);
    }

    // 좌우 이동·점프·중력·코트 제한: 양쪽이 같은 함수 (넘어져 있으면 입력을 버린다)
    void MoveActor(Actor who, int axis, bool jump, double dt)
    {
        var b = Of(who);
        if (Clock < b.TripUntil) (axis, jump) = (0, false);

        // 슬라이드 (docs/volley-slide.md 3장): 미끄러지는 중이거나 누워 있으면 입력을 버린다. 그렇지 않고 WantSlide 면 지금 던진다
        bool sliding = false;
        if (T.Slides)
        {
            if (b.SlideLeft > 0) (axis, jump, sliding) = (0, false, true);
            else if (Clock < b.SlideRecoverUntil) (axis, jump) = (0, false);
            else if (WantSlide(who, b, axis, jump)) { StartSlide(who, b, axis); (axis, jump, sliding) = (0, false, true); }
        }
        b.Axis = axis;
        double slideEdge = 0;
        if (sliding)
        {
            double step = Math.Min(b.SlideLeft, SlideSpeed * dt);
            b.Vx = b.SlideDir * SlideSpeed;
            b.X += b.SlideDir * step;
            b.SlideLeft -= step;
            slideEdge = b.X;
        }
        else
        {
            b.Vx = axis * WalkSpeed;
            b.X += b.Vx * dt;
        }

        // 달린 시간 (돌부리 뽑기용): 땅에서 같은 방향으로 움직이면 쌓이고, 멈춤·방향 바꿈·점프·넘어짐·슬라이드면 0
        if (b.OnGround && axis != 0 && !jump)
        {
            if (axis == b.RunDir) b.RunSec += dt;
            else (b.RunSec, b.RunDir, b.RunRolled) = (0, axis, false);
        }
        else (b.RunSec, b.RunDir, b.RunRolled) = (0, 0, false);

        // 점프
        if (jump && b.OnGround) StartJump(who, b);

        // 공중 중력
        if (!b.OnGround)
        {
            b.Vy += Gravity * dt;
            b.Y += b.Vy * dt;

            if (b.Y >= FloorY)
            {
                b.Y = FloorY;
                b.Vy = 0;
                b.OnGround = true;
                b.BlockJump = false;
            }
        }

        // 코트 경계 제한 (그물을 넘을 수 없음)
        b.X = who == Actor.Player ? Math.Clamp(b.X, WallLeft + PetR, NetX - PetR - 2) : Math.Clamp(b.X, NetX + PetR + 2, WallRight - PetR);

        if (sliding)
        {
            if (b.X != slideEdge) b.SlideLeft = 0;                     // 벽·네트에 닿으면 거기서 멈춘다
            b.SlideX1 = b.X;
            b.SlideTrailLeft = SlideTrailSec;
            if (b.SlideLeft <= 1e-9) (b.SlideLeft, b.SlideRecoverUntil, b.Vx) = (0, Clock + T.SlideRecoverSec, 0);   // 다 갔다: 누워 있기로
        }
    }

    public double PredictBallLandingX(double targetY, bool isRightSide)
    {
        double simX = BallX;
        double simY = BallY;
        double simVx = BallVx;
        double simVy = BallVy;
        double simDt = 0.02;

        for (int i = 0; i < 90; i++)
        {
            simVy += BallGravity * simDt;
            simX += simVx * simDt;
            simY += simVy * simDt;

            // 벽 튕김
            if (simX - BallR <= WallLeft)
            {
                simX = WallLeft + BallR;
                simVx = Math.Abs(simVx) * 0.92;
            }
            else if (simX + BallR >= WallRight)
            {
                simX = WallRight - BallR;
                simVx = -Math.Abs(simVx) * 0.92;
            }

            // 네트 기둥 튕김
            if (simY + BallR > NetTopY && simY - BallR < FloorY)
            {
                if (Math.Abs(simX - NetX) <= BallR + NetW / 2)
                {
                    simVx = -simVx * 0.85;
                }
            }

            if (simVy > 0 && simY >= targetY)
            {
                if (isRightSide && simX >= NetX) return simX;
                if (!isRightSide && simX <= NetX) return simX;
            }
            if (simVy > 0 && simY >= FloorY)
            {
                if (isRightSide && simX >= NetX) return simX;
                if (!isRightSide && simX <= NetX) return simX;
            }
        }

        return isRightSide ? (NetX + WallRight) / 2.0 : (WallLeft + NetX) / 2.0;
    }

    void UpdateBall(double dt)
    {
        // 공 중력 적용
        BallVy += BallGravity * dt;
        BallX += BallVx * dt;
        BallY += BallVy * dt;
        BallSpin += BallVx * dt / BallR; // 굴러가듯 돈다 (라디안, 시계 방향 +). 0 이면 꼭지가 위

        // 서브 후 첫 타구면 InPlay 로 전환
        if (Stage == Phase.Serving && LastHitActor != null)
        {
            Stage = Phase.InPlay;
            rallyStart = Clock;
        }

        // 방금 친 스파이크가 네트 가운데를 지났나 (캡·기둥에 닿기 전에)
        if (spikeWatchBy >= 0 && (BallX - NetX) * (spikeWatchBy == 0 ? 1 : -1) > 0)
        {
            SpikeOver[spikeWatchBy]++;
            spikeWatchBy = -1;
        }

        // 방금 디그한 공이 네트 가운데를 지났나
        if (digWatchBy >= 0 && (BallX - NetX) * (digWatchBy == 0 ? 1 : -1) > 0)
        {
            DigOver[digWatchBy]++;
            digWatchBy = -1;
        }

        // 1. 벽 충돌 (좌/우)
        if (BallX - BallR <= WallLeft)
        {
            BallX = WallLeft + BallR;
            BallVx = Math.Abs(BallVx) * 0.92;
            OnEvent?.Invoke("bounce", "wall");
        }
        else if (BallX + BallR >= WallRight)
        {
            BallX = WallRight - BallR;
            BallVx = -Math.Abs(BallVx) * 0.92;
            OnEvent?.Invoke("bounce", "wall");
        }

        // 2. 천장 충돌
        if (BallY - BallR <= CeilingY)
        {
            BallY = CeilingY + BallR;
            BallVy = Math.Abs(BallVy) * 0.88;
            OnEvent?.Invoke("bounce", "ceiling");
        }

        // 3. 네트 충돌 (기둥 및 상단 캡)
        CheckNetCollision();

        // 4. 선수 충돌 (플레이어 & 라이벌)
        // 공에 더 가까운 선수부터 (한 프레임에 둘이 다 닿을 때 어느 한쪽이 늘 먼저가 아니게). 같으면 프레임 짝·홀로
        double dp = (BallX - P.X) * (BallX - P.X) + (BallY - (P.Y - PetR)) * (BallY - (P.Y - PetR));
        double dr = (BallX - R.X) * (BallX - R.X) + (BallY - (R.Y - PetR)) * (BallY - (R.Y - PetR));
        bool playerFirst = Math.Abs(dp - dr) < 1e-9 ? ((long)Math.Round(Clock / curDt) & 1) == 0 : dp < dr;
        CheckBodyCollision(playerFirst ? Actor.Player : Actor.Rival);
        CheckBodyCollision(playerFirst ? Actor.Rival : Actor.Player);

        // 5. 바닥 충돌 -> 득점 처리
        if (BallY + BallR >= FloorY)
        {
            BallY = FloorY - BallR;
            BallVx = 0;
            BallVy = 0;

            HandlePointScored();
        }
    }

    void CheckNetCollision()
    {
        // A. 상단 캡 (원형) 충돌
        double capCx = NetX;
        double capCy = NetTopY;
        double cdx = BallX - capCx;
        double cdy = BallY - capCy;
        double capDistSq = cdx * cdx + cdy * cdy;
        double minCapDist = BallR + CapR;

        if (capDistSq <= minCapDist * minCapDist)
        {
            double dist = Math.Max(0.001, Math.Sqrt(capDistSq));
            double nx = cdx / dist;
            double ny = cdy / dist;

            BallX = capCx + nx * minCapDist;
            BallY = capCy + ny * minCapDist;

            // 반사 속도 계산
            double dot = BallVx * nx + BallVy * ny;
            BallVx = (BallVx - 1.8 * dot * nx) * 0.88;
            BallVy = (BallVy - 1.8 * dot * ny) * 0.88;
            if (BallVy > 0) BallVy = -Math.Max(100 * ScaleRatio, Math.Abs(BallVy)); // 캡에 맞으면 튀어오름

            spikeWatchBy = -1;
            OnEvent?.Invoke("bounce", "net_cap");
            return;
        }

        // B. 기둥 수직 막대 충돌
        if (BallY + BallR > NetTopY && BallY - BallR < FloorY)
        {
            int halfW = NetW / 2;
            if (Math.Abs(BallX - NetX) <= BallR + halfW)
            {
                if (BallX < NetX)
                {
                    BallX = NetX - (BallR + halfW);
                    BallVx = -Math.Abs(BallVx) * 0.85;
                }
                else
                {
                    BallX = NetX + (BallR + halfW);
                    BallVx = Math.Abs(BallVx) * 0.85;
                }

                spikeWatchBy = -1;
                OnEvent?.Invoke("bounce", "net_pole");
            }
        }
    }

    // 공과 선수 몸이 닿았을 때: 플레이어·라이벌이 같은 규칙 (스파이크 > 블로킹 > 일반 타구)
    void CheckBodyCollision(Actor who)
    {
        var b = Of(who);
        int d = Dir(who);
        // 슬라이드·누워 있는 몸은 서 있는 원 대신 낮고 앞으로 나간 원 (그림의 납작한 몸과 맞춘다)
        bool low = T.Slides && (b.SlideLeft > 0 || Clock < b.SlideRecoverUntil);
        double bodyR = low ? SlideBodyR : PetR;
        double bodyCx = low ? b.X + b.SlideDir * .5 * PetR : b.X;
        double bodyCy = low ? b.Y - bodyR : b.Y - PetR;
        double dx = BallX - bodyCx;
        double dy = BallY - bodyCy;
        double distSq = dx * dx + dy * dy;
        double minDist = BallR + bodyR;

        if (distSq > minDist * minDist) return;

        double dist = Math.Max(0.001, Math.Sqrt(distSq));
        double nx = dx / dist;
        double ny = dy / dist;

        BallX = bodyCx + nx * minDist;
        BallY = bodyCy + ny * minDist;

        // 스파이크·블로킹 직후 0.15초는 같은 선수와 다시 겹쳐도 밀어내기만 (계산한 길이 깨지지 않게)
        if (T.SpikeAssist && lastStrikeBy == who && Clock - lastStrikeAt < StrikeGuardSec) return;

        if (low) { Dig(who, b, d); return; }                         // 몸을 던져 받아 올린다 (꼭지 실수 없음)

        if (StemHitsHead(nx, ny)) { Fumble(who); return; }

        bool spikeReq = b.SpikeReq && !b.OnGround && !(T.SpikeAssist && nx * d < -0.35); // 공이 몸 뒤에 있으면 스파이크가 아니다
        if (spikeReq && b.SmartSpike && !(BallY <= NetTopY && AimOver(BallX, BallY, d, SpikeSpeed, T.SpikeMinSpeed * ScaleRatio, SpikeVy, out _, out _))) spikeReq = false; // 봇은 못 넘길 공을 네트에 꽂지 않는다
        // 블로킹: 블로킹 점프로 상대 공에 닿았다. 스파이크 하려 하지 않았거나, 자동 스파이크일 때는 상대의 스파이크를 막는다 (그 밖의 공은 스파이크가 먼저)
        bool blockOk = T.Blocks && IsBlocking(who) && LastHitActor is { } last && last != who && ny <= 0.3 && !(T.AutoSpike && b.BlockedSerial == b.JumpSerial);
        if (blockOk && (!b.SpikeReq || (T.AutoSpike && LastTouch == Touch.Spike)))
        {
            Block(who, b, d, nx, ny);
        }
        else if (spikeReq)
        {
            ExecuteSpike(who);
        }
        else
        {
            NormalHit(who, b, d, nx, ny);
        }
    }

    // 일반 몸통 반사 (상대 쪽으로 넘어가도록 유도)
    void NormalHit(Actor who, Body b, int d, double nx, double ny)
    {
        double speed = HitSpeed;
        double targetVx = d * Math.Max(140 * ScaleRatio, d * (nx * speed + b.Vx * 0.5));
        double targetVy = -Math.Max(280 * ScaleRatio, Math.Abs(ny) * speed - b.Vy * 0.4);

        BallVx = targetVx;
        BallVy = targetVy;

        Touched(who, Touch.Normal);
        OnEvent?.Invoke("hit", who);
    }

    // 공에 닿았음을 적는다 (마지막 닿음·랠리 타구 수·새·스파이크 지켜보기)
    void Touched(Actor who, Touch how)
    {
        TouchSerial++;
        if (digWatchBy >= 0 && digWatchBy != (int)who) digWatchBy = -1;      // 다른 쪽이 만졌으면 그 디그는 이미 넘어간 것
        if (LastHitActor != who || Clock - lastTouchAt > .25) RallyTouches++; // 한 번 닿은 것을 프레임마다 세지 않는다 (표의 타구 수)
        lastTouchAt = Clock;
        LastHitActor = who;
        LastTouch = how;
        RallyHits++;
        birdSinceTouch = false;
        spikeWatchBy = -1;
    }

    // 꼭지 방향: 돌기 전(0)은 위 (0, -1), 시계 방향으로 BallSpin 만큼. (nx, ny) 는 펫 몸 가운데에서 공으로 가는 방향
    public (double X, double Y) StemDir => (Math.Sin(BallSpin), -Math.Cos(BallSpin));
    public bool StemHitsHead(double nx, double ny)
    {
        if (!HasStem(Ball) || Clock - lastFumbleAt < .35) return false; // 실수 직후 같은 닿음으로 또 실수하지 않게
        var (sx, sy) = StemDir;
        return sx * -nx + sy * -ny >= Math.Cos(T.StemFumbleDeg * Math.PI / 180); // 꼭지가 펫 쪽을 향한다
    }

    // 실수: 공은 힘없이 위로 살짝 튀며 자기 쪽 뒤나 옆으로 툭, 펫은 FumbleSec 동안 움찔 (자세 shock)
    void Fumble(Actor actor)
    {
        bool isPlayer = actor == Actor.Player;
        double back = isPlayer ? -1 : 1; // 그물 반대쪽 = 자기 뒤
        double side = Rnd.NextDouble() < .65 ? back : -back;
        BallVx = side * (50 + Rnd.NextDouble() * 70) * ScaleRatio;
        BallVy = -(130 + Rnd.NextDouble() * 60) * ScaleRatio;
        BallSpin += (Rnd.NextDouble() - .5) * 2; // 툭 맞아 빙글
        lastFumbleAt = Clock;
        if (isPlayer) PlayerFumbleUntil = Clock + FumbleSec; else RivalFumbleUntil = Clock + FumbleSec;
        Touched(actor, Touch.Fumble);
        OnEvent?.Invoke("fumble", actor);
    }

    public bool TryExecuteSpike(Actor actor)
    {
        var b = Of(actor);
        double bodyCx = b.X;
        double bodyCy = b.Y - PetR;

        if (b.OnGround) return false;

        double dx = BallX - bodyCx;
        double dy = BallY - bodyCy;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist <= BallR + PetR + 10 * K)
        {
            ExecuteSpike(actor);
            return true;
        }

        return false;
    }

    void ExecuteSpike(Actor actor)
    {
        int d = Dir(actor);

        // 보조: 공이 네트 위쪽에 있으면 캡을 넘고 상대 코트 안에 떨어지는 속도를 계산한다 (낮은 공·계산 실패는 옛 값 그대로)
        if (T.SpikeAssist && BallY <= NetTopY && AimOver(BallX, BallY, d, SpikeSpeed, T.SpikeMinSpeed * ScaleRatio, SpikeVy, out double avx, out double avy))
        {
            BallVx = avx;
            BallVy = avy;
        }
        else
        {
            BallVx = d * SpikeSpeed;
            BallVy = SpikeVy;
        }
        SpikeDir = d;
        Of(actor).Pose = "angry";

        // 스파이크 연출 발동 (0.35초 유지)
        SpikeEffectActive = true;
        SpikeX = BallX;
        SpikeY = BallY;
        SpikeTimer = 0.35;

        // 화면 테두리 0.2초 흔들림 효과
        ShakeUntil = Clock + 0.2;

        Touched(actor, Touch.Spike);
        spikeWatchBy = (int)actor;
        SpikeCount[(int)actor]++;
        lastStrikeAt = Clock;
        lastStrikeBy = actor;

        OnEvent?.Invoke("spike", actor);
    }

    void HandlePointScored()
    {
        // 바닥 득점 판정: 중앙 네트(NetX) 기준으로 어느 쪽에 떨어졌는가?
        bool leftSide = BallX < NetX;
        Actor scorer = leftSide ? Actor.Rival : Actor.Player;
        Actor loser = Other(scorer);

        // 득점 종류: 위에서부터 먼저 맞는 것 (새 > 넘어짐 > 실수 > 스파이크·블로킹 > 자책 > 일반)
        PointKind kind;
        if (birdSinceTouch) kind = PointKind.Bird;
        else if (Clock < Of(loser).TripUntil + TripGraceSec) kind = PointKind.Trip;
        else if (LastHitActor == loser && LastTouch == Touch.Fumble) kind = PointKind.Fumble;
        else if (LastHitActor == loser && LastTouch == Touch.BlockMiss) kind = PointKind.BlockMiss;
        else if (LastHitActor == scorer && LastTouch == Touch.Spike) kind = PointKind.Spike;
        else if (LastHitActor == scorer && LastTouch == Touch.Block) kind = PointKind.Block;
        else if (LastHitActor == loser) kind = PointKind.Own;
        else kind = PointKind.Normal;
        LastPoint = kind;
        Points[(int)scorer * PointKinds + (int)kind]++;
        PointTotal++;
        HitSum += RallyTouches;
        if (Stage == Phase.InPlay) RallySecSum += Clock - rallyStart;

        LastScorer = scorer;
        CurrentServer = scorer;

        if (scorer == Actor.Player)
        {
            ScorePlayer++;
            NewlyLitTeam = 0;
            NewlyLitIndex = ScorePlayer - 1;
        }
        else
        {
            ScoreRival++;
            NewlyLitTeam = 1;
            NewlyLitIndex = ScoreRival - 1;
        }

        SparkleTimer = 0.8;

        // 바닥 먼지 연출
        DustActive = true;
        DustX = BallX;
        DustY = FloorY;
        DustTimer = 0.5;

        digWatchBy = -1;
        if (StoneOn) stoneEndAt = Math.Min(stoneEndAt, Clock + StoneSinkAfterTripSec); // 득점 뒤에는 돌도 가라앉는다
        OnEvent?.Invoke("score", scorer);

        // 5점 선승 종료 판정
        if (ScorePlayer >= TargetScore || ScoreRival >= TargetScore)
        {
            Stage = Phase.GameOver;
            EndAt = Clock;
            OnEvent?.Invoke("end", Won);
        }
        else
        {
            Stage = Phase.PointScored;
            PauseUntil = Clock + 1.2; // 1.2초 후 다음 서브
        }
    }

    // 넘어진 자세 (YELL 2026-11-18: slide — 앞으로 엎어짐)
    public const string TripPose = "slide";
    public const string BlockPose = "block";
    public const string DivePose = "dive";   // 몸을 던진 자세 (YELL 2026-11-26: 넘어짐 slide 와 구분되게 눈 뜨고 납작 — 던질 때 wide·o, 누워 있을 때 ^^ 큰 웃음)

    string PoseOf(Actor who, Body b)
    {
        if (Clock < b.TripUntil) return TripPose;
        if (T.Slides && (b.SlideLeft > 0 || Clock < b.SlideRecoverUntil)) return DivePose;
        if (Clock < b.FumbleUntil) return "shock";
        if (SpikeTimer > 0 && LastHitActor == who && LastTouch == Touch.Spike) return "angry";
        if (T.Blocks && IsBlocking(who)) return BlockPose;
        if (!b.OnGround) return "hop";
        return Math.Abs(b.Vx) > 10 ? "walk" : "idle";
    }

    void SyncPoses()
    {
        if (Stage == Phase.GameOver)
        {
            PlayerPose = Won ? "happy" : "dizzy";
            RivalPose = Won ? "dizzy" : "happy";
            return;
        }

        if (Stage == Phase.PointScored)
        {
            if (LastScorer == Actor.Player)
            {
                PlayerPose = "happy";
                RivalPose = "sad";
            }
            else
            {
                PlayerPose = "sad";
                RivalPose = "happy";
            }
            return;
        }

        // 경기 중 포즈: 넘어짐 > 슬라이드·누움(dive) > 꼭지 움찔(shock) > 스파이크 직후 angry > 블로킹 > 공중 hop > 걷기
        P.Pose = PoseOf(Actor.Player, P);
        R.Pose = PoseOf(Actor.Rival, R);
    }

    void ClampPlayersToCourt()
    {
        PlayerX = Math.Clamp(PlayerX, WallLeft + PetR, NetX - PetR - 2);
        PlayerY = FloorY;
        RivalX = Math.Clamp(RivalX, NetX + PetR + 2, WallRight - PetR);
        RivalY = FloorY;
    }
}
