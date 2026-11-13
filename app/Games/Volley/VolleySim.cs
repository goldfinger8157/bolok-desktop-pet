using System.Drawing;

namespace Bolok;

// 볼록이 배구: 핵심 시뮬레이션·물리·규칙·라이벌 AI (화면·앱과 무관한 순수 계산)
// 피카츄 배구 / Arcade Volleyball (1988) 식 물리와 랠리 규칙
sealed class VolleySim
{
    public enum Phase { Serving, InPlay, PointScored, GameOver }
    public enum Actor { Player, Rival }

    public readonly int K;
    public readonly double ScaleRatio;
    public readonly Random Rnd;

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
    public int NetW => Math.Max(2, 2 * K / 3);
    public int CapR => Math.Max(3, 3 * K / 2);

    // 선수 크기 및 물리 상수
    public int PetR => 14 * K;
    public double WalkSpeed => 240 * ScaleRatio;
    public double JumpVy => -390 * ScaleRatio;
    public double Gravity => 850 * ScaleRatio;

    // 공 크기 및 물리 상수
    public int BallR => 4 * K;
    public double BallGravity => 680 * ScaleRatio;
    public double HitSpeed => 380 * ScaleRatio;
    public double SpikeSpeed => 640 * ScaleRatio;
    public double SpikeVy => 200 * ScaleRatio;

    // 선수 상태 (Feet Y = PlayerY, RivalY)
    public double PlayerX { get; set; }
    public double PlayerY { get; set; }
    public double PlayerVx { get; set; }
    public double PlayerVy { get; set; }
    public bool PlayerOnGround { get; set; }
    public string PlayerPose { get; set; } = "idle";

    public double RivalX { get; set; }
    public double RivalY { get; set; }
    public double RivalVx { get; set; }
    public double RivalVy { get; set; }
    public bool RivalOnGround { get; set; }
    public string RivalPose { get; set; } = "idle";

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
    public int RallyHits { get; set; }
    public int TotalRallies { get; set; }

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

    public VolleySim(Rectangle court, bool isDesktop, int scale, Random? rnd = null)
    {
        K = scale;
        ScaleRatio = scale / 3.0;
        Rnd = rnd ?? new Random();
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
        NetH = Math.Clamp((int)(Court.Height * 0.36), 24 * K, 50 * K);

        ClampPlayersToCourt();
    }

    public void ResetMatch(Actor firstServer)
    {
        ScorePlayer = 0;
        ScoreRival = 0;
        CurrentServer = firstServer;
        LastScorer = null;
        TotalRallies = 0;

        ResetPositionsForServe(firstServer);
    }

    public void ResetPositionsForServe(Actor server)
    {
        Stage = Phase.Serving;
        CurrentServer = server;
        LastHitActor = null;
        RallyHits = 0;

        // 선수 기본 위치
        PlayerX = (WallLeft + NetX) / 2.0;
        PlayerY = FloorY;
        PlayerVx = 0;
        PlayerVy = 0;
        PlayerOnGround = true;
        PlayerPose = "idle";

        RivalX = (NetX + WallRight) / 2.0;
        RivalY = FloorY;
        RivalVx = 0;
        RivalVy = 0;
        RivalOnGround = true;
        RivalPose = "idle";

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

        // 1. 플레이어 조작 & 물리
        UpdatePlayer(dt, inputAxisX, inputJump, inputSpike);

        // 2. 라이벌 AI & 물리
        UpdateRival(dt);

        // 3. 공 물리 및 충돌
        UpdateBall(dt, inputSpike);

        // 4. 상태 및 포즈 동기화
        SyncPoses();
    }

    void UpdatePlayer(double dt, int axisX, bool jump, bool spike)
    {
        // 좌우 이동
        PlayerVx = axisX * WalkSpeed;
        PlayerX += PlayerVx * dt;

        // 점프
        if (jump && PlayerOnGround)
        {
            PlayerVy = JumpVy;
            PlayerOnGround = false;
            OnEvent?.Invoke("jump", Actor.Player);
        }

        // 공중 중력
        if (!PlayerOnGround)
        {
            PlayerVy += Gravity * dt;
            PlayerY += PlayerVy * dt;

            if (PlayerY >= FloorY)
            {
                PlayerY = FloorY;
                PlayerVy = 0;
                PlayerOnGround = true;
            }
        }

        // 코트 경계 제한 (그물 왼쪽을 넘을 수 없음)
        PlayerX = Math.Clamp(PlayerX, WallLeft + PetR, NetX - PetR - 2);
    }

    // AI 난이도 손잡이 상수
    public const double AiReactionInterval = 0.06;
    public const double AiSpikeProbability = 0.30;
    public double AiErrorPx => 8 * ScaleRatio;

    double aiReactionTimer;
    double aiTargetX;
    bool rivalSpikeRequested;

    void UpdateRival(double dt)
    {
        aiReactionTimer -= dt;
        bool wantJump = false;
        rivalSpikeRequested = false;

        // 공이 라이벌 쪽으로 넘어오고 있거나 라이벌 쪽에 있을 때
        if (BallX >= NetX - 30 * K || BallVx > 0)
        {
            if (aiReactionTimer <= 0)
            {
                aiReactionTimer = AiReactionInterval;
                double predX = PredictBallLandingX(RivalY - PetR, isRightSide: true);
                double error = (Rnd.NextDouble() - 0.5) * 2 * AiErrorPx;
                aiTargetX = Math.Clamp(predX + error, NetX + PetR + 4, WallRight - PetR - 4);
            }

            double dx = BallX - RivalX;
            double dy = BallY - (RivalY - PetR);

            // 점프 조건: 공이 하강 중이고 적절한 높이이며 가까울 때
            if (RivalOnGround && BallVy > 0 && Math.Abs(dx) < PetR * 1.2 && BallY < FloorY - PetR * 1.8 && BallY > NetTopY - 15 * K)
            {
                wantJump = true;
            }

            // 스파이크 조건: 공중에서 공과 닿을 수 있고 공이 네트 위쪽일 때
            if (!RivalOnGround && Math.Sqrt(dx * dx + dy * dy) <= BallR + PetR + 8 * K && BallY < NetTopY + 10 * K)
            {
                if (Rnd.NextDouble() < AiSpikeProbability)
                {
                    rivalSpikeRequested = true;
                }
            }
        }
        else
        {
            aiTargetX = (NetX + WallRight) / 2.0;
        }

        // AI 수평 이동
        double diff = aiTargetX - RivalX;
        if (Math.Abs(diff) > 4 * ScaleRatio)
            RivalVx = Math.Sign(diff) * WalkSpeed;
        else
            RivalVx = 0;

        RivalX += RivalVx * dt;

        // AI 점프
        if (wantJump && RivalOnGround)
        {
            RivalVy = JumpVy;
            RivalOnGround = false;
            OnEvent?.Invoke("jump", Actor.Rival);
        }

        // 공중 중력
        if (!RivalOnGround)
        {
            RivalVy += Gravity * dt;
            RivalY += RivalVy * dt;

            if (RivalY >= FloorY)
            {
                RivalY = FloorY;
                RivalVy = 0;
                RivalOnGround = true;
            }
        }

        // 코트 경계 제한 (그물 오른쪽을 넘을 수 없음)
        RivalX = Math.Clamp(RivalX, NetX + PetR + 2, WallRight - PetR);
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

    void UpdateBall(double dt, bool playerSpikeRequested)
    {
        // 공 중력 적용
        BallVy += BallGravity * dt;
        BallX += BallVx * dt;
        BallY += BallVy * dt;
        BallSpin += BallVx * dt * 0.08;

        // 서브 후 첫 타구면 InPlay 로 전환
        if (Stage == Phase.Serving && LastHitActor != null)
        {
            Stage = Phase.InPlay;
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
        CheckPlayerCollision(playerSpikeRequested);
        CheckRivalCollision();

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

                OnEvent?.Invoke("bounce", "net_pole");
            }
        }
    }

    void CheckPlayerCollision(bool spikeRequested)
    {
        double bodyCx = PlayerX;
        double bodyCy = PlayerY - PetR;
        double dx = BallX - bodyCx;
        double dy = BallY - bodyCy;
        double distSq = dx * dx + dy * dy;
        double minDist = BallR + PetR;

        if (distSq <= minDist * minDist)
        {
            double dist = Math.Max(0.001, Math.Sqrt(distSq));
            double nx = dx / dist;
            double ny = dy / dist;

            BallX = bodyCx + nx * minDist;
            BallY = bodyCy + ny * minDist;

            // 공중 강스파이크 판정 (공중에 떠 있고 Ctrl 을 눌렀을 때)
            if (!PlayerOnGround && spikeRequested)
            {
                ExecuteSpike(Actor.Player);
            }
            else
            {
                // 일반 몸통 반사 (오른쪽 상대로 넘어가도록 유도)
                double speed = HitSpeed;
                double targetVx = Math.Max(140 * ScaleRatio, nx * speed + PlayerVx * 0.5);
                double targetVy = -Math.Max(280 * ScaleRatio, Math.Abs(ny) * speed - PlayerVy * 0.4);

                BallVx = targetVx;
                BallVy = targetVy;

                LastHitActor = Actor.Player;
                RallyHits++;
                OnEvent?.Invoke("hit", Actor.Player);
            }
        }
    }

    void CheckRivalCollision()
    {
        double bodyCx = RivalX;
        double bodyCy = RivalY - PetR;
        double dx = BallX - bodyCx;
        double dy = BallY - bodyCy;
        double distSq = dx * dx + dy * dy;
        double minDist = BallR + PetR;

        if (distSq <= minDist * minDist)
        {
            double dist = Math.Max(0.001, Math.Sqrt(distSq));
            double nx = dx / dist;
            double ny = dy / dist;

            BallX = bodyCx + nx * minDist;
            BallY = bodyCy + ny * minDist;

            if (!RivalOnGround && rivalSpikeRequested)
            {
                ExecuteSpike(Actor.Rival);
            }
            else
            {
                // 일반 몸통 반사 (왼쪽 플레이어로 넘어가도록 유도)
                double speed = HitSpeed;
                double targetVx = Math.Min(-140 * ScaleRatio, nx * speed + RivalVx * 0.5);
                double targetVy = -Math.Max(280 * ScaleRatio, Math.Abs(ny) * speed - RivalVy * 0.4);

                BallVx = targetVx;
                BallVy = targetVy;

                LastHitActor = Actor.Rival;
                RallyHits++;
                OnEvent?.Invoke("hit", Actor.Rival);
            }
        }
    }

    public bool TryExecuteSpike(Actor actor)
    {
        bool isPlayer = (actor == Actor.Player);
        double bodyCx = isPlayer ? PlayerX : RivalX;
        double bodyCy = isPlayer ? PlayerY - PetR : RivalY - PetR;
        bool onGround = isPlayer ? PlayerOnGround : RivalOnGround;

        if (onGround) return false;

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
        bool isPlayer = (actor == Actor.Player);

        if (isPlayer)
        {
            BallVx = SpikeSpeed;
            BallVy = SpikeVy;
            SpikeDir = 1;
            PlayerPose = "angry";
        }
        else
        {
            BallVx = -SpikeSpeed;
            BallVy = SpikeVy;
            SpikeDir = -1;
            RivalPose = "angry";
        }

        // 스파이크 연출 발동 (0.35초 유지)
        SpikeEffectActive = true;
        SpikeX = BallX;
        SpikeY = BallY;
        SpikeTimer = 0.35;

        // 화면 테두리 0.2초 흔들림 효과
        ShakeUntil = Clock + 0.2;

        LastHitActor = actor;
        RallyHits++;

        OnEvent?.Invoke("spike", actor);
    }

    void HandlePointScored()
    {
        // 바닥 득점 판정: 중앙 네트(NetX) 기준으로 어느 쪽에 떨어졌는가?
        bool leftSide = BallX < NetX;
        Actor scorer = leftSide ? Actor.Rival : Actor.Player;

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

        // 경기 중 포즈 (공중 점프는 hop, 스파이크 직후는 강하게 내리치는 angry)
        if (SpikeTimer > 0 && LastHitActor == Actor.Player)
            PlayerPose = "angry";
        else if (!PlayerOnGround)
            PlayerPose = "hop";
        else if (Math.Abs(PlayerVx) > 10)
            PlayerPose = "walk";
        else
            PlayerPose = "idle";

        if (SpikeTimer > 0 && LastHitActor == Actor.Rival)
            RivalPose = "angry";
        else if (!RivalOnGround)
            RivalPose = "hop";
        else if (Math.Abs(RivalVx) > 10)
            RivalPose = "walk";
        else
            RivalPose = "idle";
    }

    void ClampPlayersToCourt()
    {
        PlayerX = Math.Clamp(PlayerX, WallLeft + PetR, NetX - PetR - 2);
        PlayerY = FloorY;
        RivalX = Math.Clamp(RivalX, NetX + PetR + 2, WallRight - PetR);
        RivalY = FloorY;
    }
}
