namespace Bolok;

// 배구 규칙 한 덩어리 (VolleySim 의 나머지): 스파이크 보조(AimOver) · 돌부리 · 블로킹.
// 규칙 함수 하나가 양쪽 선수를 같은 숫자로 다룬다 (공평함). 무작위는 Rnd 만 쓴다 (같은 씨 = 같은 판)
sealed partial class VolleySim
{
    public const double StrikeGuardSec = .15;   // 스파이크·블로킹 직후 같은 선수와 다시 닿기 막기
    public const double TripGraceSec = .5;      // 일어난 지 이만큼 안에 진 점수도 「넘어짐」
    double lastStrikeAt = -9;
    Actor lastStrikeBy;

    // ── 스파이크 보조 (4장) ──
    // (x0, y0) 에서 d 쪽(플레이어 +1, 라이벌 -1)으로 치는 공이 캡 위를 넘고 상대 코트 안에 떨어지는 속도를 고른다.
    // 속도 s 는 sMax 에서 sMin 까지 일곱 번(할당 없이), 세기 vy 는 vyWant 와 「딱 넘을 만큼」 가운데 작은 쪽 (화면 좌표라 아래가 +)
    public bool AimOver(double x0, double y0, int d, double sMax, double sMin, double vyWant, out double vx, out double vy)
    {
        double g = BallGravity, rc = BallR + CapR;
        double yc = NetTopY - CapR - BallR - T.ClearMarginCells * K;      // 공 가운데가 이 선 위로 넘어야 한다
        double upMax = T.SpikeUpMax * ScaleRatio;
        double xEnd = (d > 0 ? WallRight : WallLeft) - d * (BallR + T.LandMarginCells * K);
        double wallVx = 0, wallVy = 0;
        bool haveWall = false;
        (vx, vy) = (0, 0);
        for (int i = 0; i < 7; i++)
        {
            double s = sMax - (sMax - sMin) * i / 6;
            // 캡 앞·뒤 가장자리 두 점만 보면 된다: 포물선은 구간 양 끝이 가장 낮다
            double clear = double.PositiveInfinity;
            for (int e = 0; e < 2; e++)
            {
                double xp = e == 0 ? NetX - d * rc : NetX + d * rc;
                double a = (xp - x0) * d;
                if (a <= 0) continue;                                      // 이미 지난 자리
                double t = a / s;
                clear = Math.Min(clear, (yc - y0 - .5 * g * t * t) / t);
            }
            double vyTry = Math.Min(vyWant, clear);
            if (vyTry < -upMax) continue;                                  // 이 속도로는 못 넘긴다
            double h = FloorY - BallR - y0;
            double fall = (-vyTry + Math.Sqrt(vyTry * vyTry + 2 * g * h)) / g;
            double xLand = x0 + d * s * fall;
            if (d > 0 ? xLand <= xEnd : xLand >= xEnd) { vx = d * s; vy = vyTry; return true; }
            (haveWall, wallVx, wallVy) = (true, d * s, vyTry);              // 넘기지만 벽에 닿는 값 (더 느린 쪽이 뒤에 온다)
        }
        if (!haveWall) return false;
        (vx, vy) = (wallVx, wallVy);
        return true;
    }

    // ── 돌부리 (6장) ──
    public const double StoneRiseSec = .12, StoneStaySec = 1.4, StoneSinkAfterTripSec = .25, TripDustSec = .4;
    public bool StoneOn { get; private set; }
    public Actor StoneOwner { get; private set; }     // 돌이 솟은 쪽 (이 선수만 걸린다)
    public double StoneX { get; private set; }
    public int StoneSerial { get; private set; }      // 솟을 때마다 +1 (봇이 새 돌을 알아본다)
    double stoneBornAt, stoneEndAt;
    bool stoneThisRally, stoneTripped;
    int lastStoneSide = -1;                           // 마지막으로 돌이 솟은 쪽 (판 안에서 번갈아). ResetMatch 에서만 지운다

    // 한쪽 코트를 달려 건너는 시간 (바탕화면 판 배율 3 은 0.89초). 좁은 코트는 달리기·예고를 이 길이에 비례해 줄인다 (YELL 2026-11-18)
    public double StoneSpanSec => Math.Max(.05, (NetX - WallLeft - 2 * PetR - 2) / WalkSpeed);
    public double StoneRunNeed => Math.Min(T.StoneRunSec, T.StoneRunSpan * StoneSpanSec);
    public double StoneWarnTime => Math.Min(T.StoneWarnSec, T.StoneWarnSpan * StoneSpanSec);

    // 그릴 프레임 0..3 (솟을 때 아래부터 ¼·½·¾·전부, 가라앉을 때는 거꾸로), 없으면 -1
    public int StoneFrame
    {
        get
        {
            double age = Clock - stoneBornAt, left = stoneEndAt - Clock, q = StoneRiseSec / 4;
            if (!StoneOn || left <= 0) return -1;
            if (left < StoneRiseSec) return Math.Clamp((int)(left / q), 0, 3);
            return age < StoneRiseSec ? Math.Clamp((int)(age / q), 0, 3) : 3;
        }
    }

    public bool IsTripped(Actor a) => Clock < Of(a).TripUntil;
    public double TripDustLeft(Actor a) => Of(a).TripDustTimer;
    public double TripDustX(Actor a) => Of(a).TripDustX;

    void ClearStone()
    {
        StoneOn = false;
        stoneThisRally = false;
    }

    void UpdateStone(double dt)
    {
        if (!T.Stones) return;

        if (StoneOn)
        {
            if (Clock >= stoneEndAt) StoneOn = false;
            else if (!stoneTripped && Clock - stoneBornAt >= StoneRiseSec)
            {
                // 걸림: 돌 주인이 땅에 있고 돌 위에 닿았다 (공중이면 안 걸린다)
                var b = Of(StoneOwner);
                if (b.OnGround && Clock >= b.TripUntil && Math.Abs(b.X - StoneX) < 6 * K) Trip(StoneOwner, b);
            }
        }

        // 뽑기: 달린 시간이 기준을 넘는 순간 그 달리기에 한 번만. 둘이 같은 프레임이면 누가 먼저 뽑을지도 제비로 (어느 쪽도 유리하지 않게)
        double need = StoneRunNeed;
        bool qp = P.RunSec > need && !P.RunRolled, qr = R.RunSec > need && !R.RunRolled;
        if (qp && qr)
        {
            bool playerFirst = Rnd.NextDouble() < .5;
            TryRoll(playerFirst ? Actor.Player : Actor.Rival);
            TryRoll(playerFirst ? Actor.Rival : Actor.Player);
        }
        else if (qp) TryRoll(Actor.Player);
        else if (qr) TryRoll(Actor.Rival);
    }

    // 조건을 먼저 본다 (맞지 않으면 Rnd 를 쓰지 않는다)
    void TryRoll(Actor who)
    {
        var b = Of(who);
        b.RunRolled = true;
        if (Stage != Phase.InPlay || stoneThisRally || StoneOn || lastStoneSide == (int)who) return;
        double sx = b.X + b.RunDir * WalkSpeed * StoneWarnTime;
        double lo = who == Actor.Player ? WallLeft + PetR : NetX + PetR + 2, hi = who == Actor.Player ? NetX - PetR - 2 : WallRight - PetR;
        if (sx < lo || sx > hi) return;                                   // 그 선수가 갈 수 없는 자리
        if (Rnd.NextDouble() >= T.StoneChance) return;
        RaiseStone(who, sx);
    }

    void RaiseStone(Actor who, double x)
    {
        (StoneOn, StoneOwner, StoneX, stoneTripped) = (true, who, x, false);
        stoneBornAt = Clock;
        stoneEndAt = Clock + StoneStaySec;
        stoneThisRally = true;
        lastStoneSide = (int)who;
        StoneSerial++;
        StoneCount[(int)who]++;
        OnEvent?.Invoke("stone", who);
    }

    void Trip(Actor who, Body b)
    {
        b.TripUntil = Clock + T.TripSec;                                  // 이 동안 좌우·점프 입력을 버린다 (MoveActor). 몸과 공의 충돌은 그대로
        b.TripDustX = b.X;
        b.TripDustTimer = TripDustSec;
        b.Vx = 0;
        (b.RunSec, b.RunDir, b.RunRolled) = (0, 0, false);
        stoneTripped = true;
        stoneEndAt = Math.Min(stoneEndAt, Clock + StoneSinkAfterTripSec);
        TripCount[(int)who]++;
        OnEvent?.Invoke("trip", who);
    }

    // 디버그·장면용: 그 선수 바로 앞(달리는 방향 = 네트 쪽)에 돌을 솟게 한다
    public void DebugStone(Actor who)
    {
        var b = Of(who);
        double x = b.X + Dir(who) * WalkSpeed * StoneWarnTime;
        RaiseStone(who, Math.Clamp(x, who == Actor.Player ? WallLeft + PetR : NetX + PetR + 2, who == Actor.Player ? NetX - PetR - 2 : WallRight - PetR));
    }

    // 디버그·장면용: 그 선수를 x 자리에서 뛰게 한다 (네트 가까이면 블로킹 점프)
    public void DebugJump(Actor who, double x)
    {
        var b = Of(who);
        b.X = x;
        if (b.OnGround) StartJump(who, b);
    }

    // ── 블로킹 (7장) ──
    public const double BlockFlashSec = .25;
    public double BlockFlashUntil { get; set; } = -1;
    public bool BlockFlashActive => Clock < BlockFlashUntil;
    public bool IsBlocking(Actor who) { var b = Of(who); return !b.OnGround && b.BlockJump; }

    // 블로킹이 되는 닿음 (CheckBodyCollision 이 조건을 맞춰 부른다)
    void Block(Actor who, Body b, int d, double nx, double ny)
    {
        if (Math.Abs(b.Vy) <= Gravity * T.BlockWindowSec && BallY <= NetTopY)
        {
            // 맞은 블로킹: 스파이크와 같은 계산으로 느리게, 네트 바로 뒤에 뚝
            double sp = T.BlockSpeed * ScaleRatio;
            if (AimOver(BallX, BallY, d, sp, 120 * ScaleRatio, T.BlockVy * ScaleRatio, out double vx, out double vy)) { BallVx = vx; BallVy = vy; }
            else { BallVx = d * sp; BallVy = T.BlockVy * ScaleRatio; }
            Touched(who, Touch.Block);
            BlockHit[(int)who]++;
            BlockFlashUntil = Clock + BlockFlashSec;
            lastStrikeAt = Clock;
            lastStrikeBy = who;
            OnEvent?.Invoke("block", who);
        }
        else if (T.BlockMissOnlySpike && LastTouch != Touch.Spike)
        {
            NormalHit(who, b, d, nx, ny);                                  // 평범한 공은 지금처럼 튕긴다 (YELL 2026-11-18)
        }
        else
        {
            // 어긋난 블로킹: 자기 쪽으로 힘없이. 움찔 없음 — 착지해서 다시 받을 수 있다
            BallVx = -d * (40 + Rnd.NextDouble() * 50) * ScaleRatio;
            BallVy = -(120 + Rnd.NextDouble() * 50) * ScaleRatio;
            Touched(who, Touch.BlockMiss);
            BlockMissCount[(int)who]++;
            OnEvent?.Invoke("blockmiss", who);
        }
    }
}
