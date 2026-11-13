using System.Drawing;

namespace Bolok;

// 모래성 게임 시뮬레이션 및 룰 (차례, 깃발 쓰러짐, 라이벌 AI, 승패)
sealed class Sandcastle
{
    public enum Phase
    {
        Ready,
        PlayerTurn,         // 내 차례 (동그라미 충전 대기)
        CpuThinking,        // 라이벌 생각 중 (0.6~1.2초)
        Scooping,           // 삽으로 퍼내는 중 (애니메이션)
        Falling,            // 모래가 흘러내리는 중
        FlagCollapsing,     // 깃발이 쓰러지고 성 전체가 무너지는 중
        Won,                // 내가 이김 (라이벌이 깃발을 쓰러뜨림)
        Lost,               // 라이벌이 이김 (내가 깃발을 쓰러뜨림)
    }

    public enum TurnActor { Player, Cpu }

    // 설정 및 상수 (평균 12~20차례 결착을 위한 균형)
    public const double MinRadius = 40;     // 최소 퍼내기 반지름 (px)
    public const double MaxRadius = 100;    // 최대 퍼내기 반지름 (px)
    public const double ChargeRate = 50;    // 초당 반지름 증가 속도 (px/s)
    public const double CpuThinkMin = 0.6;  // 라이벌 생각 최소 시간 (초)
    public const double CpuThinkMax = 1.2;  // 라이벌 생각 최대 시간 (초)

    public readonly Rectangle Field;        // 시합장 전체 화면 영역
    public readonly Rectangle CastleRect;   // 창(모래성) 사각형
    public readonly int K;                  // 볼록이 배율
    public readonly Random Rnd;
    public readonly SandGrid Grid;

    // 플레이어 & 라이벌 위치 (화면 좌표)
    public double PetX, PetY;               // 내 볼록이 (성 왼쪽 땅 위)
    public double RivalX, RivalY;           // 라이벌 (성 오른쪽 땅 위)
    public int RivalDir = -1;               // 라이벌 바라보는 방향 (-1: 왼쪽 모래성을 봄)

    // 깃발 (창 중앙 꼭대기)
    public readonly double FlagBaseX, FlagBaseY; // 깃발 기둥 바닥 (화면 좌표)
    public double FlagCurrentY => (Stage is Phase.FlagCollapsing or Phase.Won or Phase.Lost)
        ? FlagBaseY + (CastleRect.Bottom - FlagBaseY - 12 * K) * Math.Min(1.0, DefeatTimer / 1.0)
        : FlagBaseY;
    public double FlagAngle { get; private set; } // 깃발 기울기 (0: 직립, 1: 완전히 쓰러짐)
    public bool FlagFallen { get; private set; }
    readonly int flagAnchorGx, flagAnchorGy;
    readonly int initialBaseSolidCount;

    // 게임 상태
    public Phase Stage { get; private set; } = Phase.Ready;
    public TurnActor ActiveTurn { get; private set; }
    public TurnActor LastScooper { get; private set; }
    public double Clock { get; private set; }
    public double TurnStartTime { get; private set; }
    public int TurnsCount { get; private set; }
    public double EndAt { get; private set; }
    public bool Done => Stage is Phase.Won or Phase.Lost;
    double fallingTimer;

    // 조작 중인 퍼내기 원
    public bool Charging { get; private set; }
    public double ChargeRadius { get; private set; }
    public double ChargeCenterX, ChargeCenterY;

    // 삽 퍼내기 연출 (Scoop Animation)
    public bool ScoopAnimActive { get; private set; }
    public double ScoopAnimX, ScoopAnimY;
    public double ScoopAnimTimer;
    public bool ScoopAnimByPlayer;

    // 라이벌 AI 목표
    double cpuTargetX, cpuTargetY, cpuTargetR;
    double cpuThinkDuration;

    // 지는 순간 연출
    public double DefeatTimer { get; private set; }

    // 이벤트 알림 (효과음·표정 연출용)
    public Action<string>? OnEvent;

    public Sandcastle(Rectangle field, Rectangle castle, int k, Random rnd, TurnActor? firstTurn = null)
    {
        Field = field;
        CastleRect = castle;
        K = k;
        Rnd = rnd;

        // 격자 셀 크기 (큰 창에서도 가로세로 상한을 두어 4ms 내 처리)
        int cell = Math.Max(k, Math.Max((castle.Width + 240) / 240, (castle.Height + 160) / 160));

        // 격자 영역: 성 좌우로 볼록이와 라이벌이 설 수 있는 땅 여백을 둠
        int marginX = Math.Max(60 * k, 120);
        int flagH = 22 * k;

        // 깃발 위치 (창이 화면 맨 위 y=0 근처에 붙어 있을 때 화면 밖으로 잘리지 않게 조정)
        FlagBaseX = castle.Left + castle.Width / 2.0;
        double topRoom = castle.Top - field.Top;
        double flagShiftY = topRoom < flagH ? Math.Min(flagH - topRoom, castle.Height * 0.35) : 0;
        FlagBaseY = castle.Top + flagShiftY;

        int flagSpaceY = Math.Max(flagH, (int)(castle.Top - field.Top));
        int gridLeft = castle.Left - marginX;
        int gridTop = Math.Max(field.Top, (int)(FlagBaseY - flagH));
        int gridRight = castle.Right + marginX;
        int gridBottom = castle.Bottom + Math.Max(12 * k, 24);

        int gw = (gridRight - gridLeft + cell - 1) / cell;
        int gh = (gridBottom - gridTop + cell - 1) / cell;

        var fieldBounds = new Rectangle(gridLeft, gridTop, gw * cell, gh * cell);
        Grid = new SandGrid(gw, gh, cell, fieldBounds, castle);
        Grid.BuildCastle(rnd);

        flagAnchorGx = (int)Math.Round((FlagBaseX - Grid.FieldBounds.Left) / Grid.CellSize);
        flagAnchorGy = (int)Math.Round((FlagBaseY - Grid.FieldBounds.Top) / Grid.CellSize);

        initialBaseSolidCount = CountFlagBaseSolids(Grid);

        // 볼록이와 라이벌 위치
        double floorY = castle.Bottom; // 땅 바닥 선
        PetX = castle.Left - marginX * 0.55;
        PetY = floorY;
        RivalX = castle.Right + marginX * 0.55;
        RivalY = floorY;

        // 첫 턴 결정 (지정 없으면 50% 확률)
        ActiveTurn = firstTurn ?? (rnd.Next(2) == 0 ? TurnActor.Player : TurnActor.Cpu);
        StartTurn(ActiveTurn);
    }

    // ── 고속 판 시뮬레이션용 (Bench / AI 시험용) ──
    public bool StepTurnSim(Random rnd, double cx, double cy, double cr)
    {
        int scooped = Grid.Scoop(cx, cy, cr);
        if (scooped < SandGrid.MinScoopGrains) return false;

        LastScooper = ActiveTurn;
        TurnsCount++;
        Grid.SimulateToRest(rnd, maxSteps: 40);

        if (!IsFlagSupported(Grid))
        {
            Stage = (LastScooper == TurnActor.Player) ? Phase.Lost : Phase.Won;
            FlagFallen = true;
            FlagAngle = 1.0;
            return true;
        }

        ActiveTurn = (ActiveTurn == TurnActor.Player) ? TurnActor.Cpu : TurnActor.Player;
        Stage = (ActiveTurn == TurnActor.Player) ? Phase.PlayerTurn : Phase.CpuThinking;
        return true;
    }

    int CountFlagBaseSolids(SandGrid grid)
    {
        int count = 0;
        for (int dy = 0; dy <= 8; dy++)
        {
            int gy = flagAnchorGy + dy;
            if (gy < 0 || gy >= grid.Gh) continue;
            for (int dx = -4; dx <= 4; dx++)
            {
                int gx = flagAnchorGx + dx;
                if (gx < 0 || gx >= grid.Gw) continue;
                if (grid.Cells[grid.Index(gx, gy)] == SandGrid.CellType.Solid)
                    count++;
            }
        }
        return count;
    }

    public bool IsFlagSupported(SandGrid grid)
    {
        int count = CountFlagBaseSolids(grid);
        // 받침 셀이 초기의 77% 미만으로 줄어들거나 20개 미만이면 지지 불가 (평균 12~20차례 결착)
        return count >= Math.Max(20, (int)(initialBaseSolidCount * 0.77));
    }

    void StartTurn(TurnActor actor)
    {
        ActiveTurn = actor;
        TurnStartTime = Clock;
        TurnsCount++;
        Charging = false;
        ChargeRadius = 0;

        Dbg.Log("sandcastle-turn", ("actor", actor == TurnActor.Player ? "player" : "cpu"), ("turns", TurnsCount));

        if (actor == TurnActor.Player)
        {
            Stage = Phase.PlayerTurn;
        }
        else
        {
            Stage = Phase.CpuThinking;
            cpuThinkDuration = CpuThinkMin + Rnd.NextDouble() * (CpuThinkMax - CpuThinkMin);
            (cpuTargetX, cpuTargetY, cpuTargetR) = PlanCpuMove();
        }
    }

    // ── 플레이어 입력 ──
    public void StartCharging(double x, double y)
    {
        if (Stage != Phase.PlayerTurn) return;
        Charging = true;
        ChargeRadius = MinRadius * K / 3.0;
        ChargeCenterX = x;
        ChargeCenterY = y;
    }

    public void UpdateCharge(double x, double y, double dt)
    {
        if (!Charging || Stage != Phase.PlayerTurn) return;
        ChargeCenterX = x;
        ChargeCenterY = y;
        double maxR = MaxRadius * K / 3.0;
        ChargeRadius = Math.Min(maxR, ChargeRadius + ChargeRate * (K / 3.0) * dt);
    }

    public void CancelCharge()
    {
        Charging = false;
        ChargeRadius = 0;
    }

    public bool ReleaseCharge(out bool valid)
    {
        valid = false;
        if (!Charging || Stage != Phase.PlayerTurn) return false;
        Charging = false;

        double cx = ChargeCenterX, cy = ChargeCenterY, r = ChargeRadius;
        ChargeRadius = 0;

        return TryExecuteScoop(cx, cy, r, TurnActor.Player, out valid);
    }

    // ── 퍼내기 실행 시도 ──
    public bool TryExecuteScoop(double cx, double cy, double r, TurnActor actor, out bool valid)
    {
        valid = false;
        if (Done) return false;

        // 자신의 차례이자 입력 대기 상태(PlayerTurn / CpuThinking)가 아닐 때(모래가 가라앉는 중 등)는 어떤 퍼내기도 거부
        if (actor != ActiveTurn || (actor == TurnActor.Player && Stage != Phase.PlayerTurn) || (actor == TurnActor.Cpu && Stage != Phase.CpuThinking))
        {
            Dbg.Log("sandcastle-scoop", ("who", actor == TurnActor.Player ? "player" : "cpu"),
                ("x", (int)cx), ("y", (int)cy), ("r", (int)r), ("grains", 0), ("valid", false));
            return false;
        }

        int scooped = Grid.Scoop(cx, cy, r);

        if (scooped < SandGrid.MinScoopGrains)
        {
            // 너무 적음 (허공): 무효! 차례 안 넘어가고 볼록이 고개 젓기
            OnEvent?.Invoke("invalid_scoop");
            Dbg.Log("sandcastle-scoop", ("who", actor == TurnActor.Player ? "player" : "cpu"),
                ("x", (int)cx), ("y", (int)cy), ("r", (int)r), ("grains", scooped), ("valid", false));
            return false;
        }

        valid = true;
        LastScooper = actor;
        Stage = Phase.Scooping;
        ScoopAnimActive = true;
        ScoopAnimX = cx;
        ScoopAnimY = cy;
        ScoopAnimTimer = 0.35;
        ScoopAnimByPlayer = (actor == TurnActor.Player);

        OnEvent?.Invoke("scoop");
        Dbg.Log("sandcastle-scoop", ("who", actor == TurnActor.Player ? "player" : "cpu"),
            ("x", (int)cx), ("y", (int)cy), ("r", (int)r), ("grains", scooped), ("valid", true));
        return true;
    }

    // ── 프레임 갱신 ──
    public void Update(double dt)
    {
        Clock += dt;
        Grid.ResetChangeBounds();

        // 1. 삽 퍼내기 연출 (Scooping)
        if (Stage == Phase.Scooping)
        {
            ScoopAnimTimer -= dt;
            ScoopAnimY -= 30 * dt; // 위로 살짝 떠오름
            if (ScoopAnimTimer <= 0)
            {
                ScoopAnimActive = false;
                Stage = Phase.Falling;
                fallingTimer = 0;
            }
            return;
        }

        // 2. 모래 낙하 및 무너짐 물리 시뮬레이션 (Falling)
        if (Stage == Phase.Falling)
        {
            fallingTimer += dt;
            // 프레임당 2회 스텝으로 모래가 우르르 쏟아짐
            for (int s = 0; s < 2; s++)
            {
                Grid.StepFalling(Rnd);
            }

            // 모래알이 다 가라앉았거나 0.8초 경과 시 정착 처리
            if (Grid.ActiveFallingCount == 0 || fallingTimer > 0.8)
            {
                if (fallingTimer > 0.8 && Grid.ActiveFallingCount > 0)
                {
                    Grid.SettleAllFalling();
                }

                // 추가 무너짐이 있는지 검사 (변화가 발생한 열 범위만 검사)
                int moreCollapse = Grid.HasChanges ? Grid.CheckStability(Grid.MinChangeX, Grid.MaxChangeX) : 0;
                if (moreCollapse == 0 && Grid.ActiveFallingCount == 0)
                {
                    fallingTimer = 0;
                    int baseSolids = CountFlagBaseSolids(Grid);
                    int pct = initialBaseSolidCount > 0 ? (int)Math.Round((double)baseSolids / initialBaseSolidCount * 100) : 0;
                    bool supported = IsFlagSupported(Grid);

                    Dbg.Log("sandcastle-flag", ("support", pct), ("fallen", !supported));

                    if (!supported)
                    {
                        TriggerFlagCollapse();
                    }
                    else
                    {
                        // 깃발이 버텼으므로 다음 차례로!
                        StartTurn(ActiveTurn == TurnActor.Player ? TurnActor.Cpu : TurnActor.Player);
                    }
                }
            }
            return;
        }

        // 3. 깃발 쓰러짐 및 성 전체 무너짐 연출 (FlagCollapsing)
        if (Stage == Phase.FlagCollapsing)
        {
            DefeatTimer += dt;
            FlagAngle = Math.Min(1.0, FlagAngle + dt * 2.0); // 0.5초에 걸쳐 완전히 쓰러짐
            if (FlagAngle >= 1.0) FlagFallen = true;

            // 성 전체 모래알 낙하 시뮬레이션 (1~2초 동안 와르르 무너져 언덕을 이룸)
            for (int s = 0; s < 6; s++)
            {
                Grid.StepFalling(Rnd);
            }

            if (DefeatTimer >= 1.6)
            {
                // 지는 순간 연출 완료 -> 결과 확정
                // 마지막에 퍼 간 쪽이 패배!
                EndAt = Clock;
                Stage = (LastScooper == TurnActor.Player) ? Phase.Lost : Phase.Won;
                OnEvent?.Invoke(Stage == Phase.Won ? "won" : "lost");
            }
            return;
        }

        // 4. 라이벌 AI 생각 중 (CpuThinking)
        if (Stage == Phase.CpuThinking)
        {
            if (Clock - TurnStartTime >= cpuThinkDuration)
            {
                if (!TryExecuteScoop(cpuTargetX, cpuTargetY, cpuTargetR, TurnActor.Cpu, out _))
                {
                    (cpuTargetX, cpuTargetY, cpuTargetR) = PlanCpuMove();
                    TurnStartTime = Clock;
                    cpuThinkDuration = 0.1;
                }
            }
        }
    }

    void TriggerFlagCollapse()
    {
        Stage = Phase.FlagCollapsing;
        DefeatTimer = 0;
        FlagFallen = true;
        OnEvent?.Invoke("flag_fall");
        Dbg.Log("sandcastle-flag", ("support", 0), ("fallen", true));
        // 성 전체를 와르르 무너뜨린다!
        Grid.CollapseAll();
    }

    public void ForceEnd(bool win)
    {
        if (Done || Stage == Phase.FlagCollapsing) return;
        LastScooper = win ? TurnActor.Cpu : TurnActor.Player;
        TriggerFlagCollapse();
    }

    SandGrid? simGrid;
    SandGrid GetSimGrid()
    {
        if (simGrid == null || simGrid.Gw != Grid.Gw || simGrid.Gh != Grid.Gh)
            simGrid = new SandGrid(Grid.Gw, Grid.Gh, Grid.CellSize, Grid.FieldBounds, CastleRect);
        return simGrid;
    }

    // AI 후보 목록 버퍼 (프레임당 메모리 할당 0B)
    readonly List<(double X, double Y, double R, int Scooped, bool Safe)> candidateMoves = new(16);
    readonly List<(double X, double Y, double R, int Scooped, bool Safe)> safeCandidateMoves = new(16);

    // ── 라이벌 AI 한 수 계획 ──
    // strictSafe: 자가검사용 (안전한 수가 있으면 100% 안전한 수만 선택)
    public (double X, double Y, double R) PlanCpuMove(bool strictSafe = false)
    {
        candidateMoves.Clear();
        safeCandidateMoves.Clear();

        double minR = (MinRadius + 5) * K / 3.0;
        double maxR = MaxRadius * K / 3.0;

        SandGrid? sim = null;

        // 성 영역 내에서 12개의 후보 추출 (적절한 긴장감과 속도)
        for (int i = 0; i < 12; i++)
        {
            double u = 0.10 + Rnd.NextDouble() * 0.80; // 가로 10%~90%
            double v = 0.12 + Rnd.NextDouble() * 0.82; // 세로 12%~94%
            double cx = CastleRect.Left + u * CastleRect.Width;
            double cy = CastleRect.Top + v * CastleRect.Height;
            double cr = minR + Rnd.NextDouble() * (maxR - minR);

            // 깃발 기둥(중앙 수직선)에서 좌우로 충분히 떨어진 곳은 처마 붕괴 한도(36px)를 감안해도 깃발 기둥에 물리적으로 영향을 줄 수 없음
            bool farFromFlag = Math.Abs(cx - FlagBaseX) > (cr + 45.0 * K / 3.0);

            if (farFromFlag)
            {
                int scooped = Grid.CountSolidGrainsInCircle(cx, cy, cr);
                if (scooped < SandGrid.MinScoopGrains) continue;
                candidateMoves.Add((cx, cy, cr, scooped, true));
            }
            else
            {
                sim ??= GetSimGrid();
                Grid.CopyTo(sim);
                int scooped = sim.Scoop(cx, cy, cr, checkStability: true);
                if (scooped < SandGrid.MinScoopGrains) continue;
                sim.SimulateToRest(Rnd, maxSteps: 6);
                bool safe = IsFlagSupported(sim);
                candidateMoves.Add((cx, cy, cr, scooped, safe));
            }
        }

        for (int i = 0; i < candidateMoves.Count; i++)
        {
            if (candidateMoves[i].Safe) safeCandidateMoves.Add(candidateMoves[i]);
        }

        // 지시서 6장: 자가검사 또는 안전한 수가 있을 때
        if (safeCandidateMoves.Count > 0)
        {
            // 보통 게임에서는 가끔(22%) 욕심내어 가장 많이 파내다 실수하기도 함
            bool greedyMistake = !strictSafe && Rnd.NextDouble() < 0.22;
            if (!greedyMistake)
            {
                // 안전한 수 중에서 파낸 양이 가장 많은 수 선택 (LINQ 없이 순회)
                var chosen = safeCandidateMoves[0];
                for (int i = 1; i < safeCandidateMoves.Count; i++)
                {
                    if (safeCandidateMoves[i].Scooped > chosen.Scooped)
                        chosen = safeCandidateMoves[i];
                }
                return (chosen.X, chosen.Y, chosen.R);
            }
        }

        // 지는 수밖에 없거나 욕심낼 때: 깃발 밑에서 먼 수
        if (candidateMoves.Count > 0)
        {
            var chosen = candidateMoves[0];
            double maxDist = Math.Abs(chosen.X - FlagBaseX);
            for (int i = 1; i < candidateMoves.Count; i++)
            {
                double dist = Math.Abs(candidateMoves[i].X - FlagBaseX);
                if (dist > maxDist)
                {
                    maxDist = dist;
                    chosen = candidateMoves[i];
                }
            }
            return (chosen.X, chosen.Y, chosen.R);
        }

        return (CastleRect.Left + CastleRect.Width * 0.2, CastleRect.Bottom - 20, minR);
    }
}
