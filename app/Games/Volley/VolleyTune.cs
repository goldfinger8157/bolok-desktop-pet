namespace Bolok;

// 배구 손잡이 묶음 (docs/volley-tune.md 2장). 숫자를 한 곳에 모아 두고, 「고치기 전(Old)」과 「지금(Now)」을 한 실행 파일에서 견준다.
// Old 로 돌리면 고치기 전과 같은 판이 나와야 한다 (VolleyCheck 가 같은 씨의 옛 값으로 확인). 실제 경기는 Now
sealed record VolleyTune
{
    // 점프·네트·꼭지 (3장)
    public double JumpV { get; init; } = 400;            // 점프 속도 (×ScaleRatio). 높이는 속도의 제곱
    public double NetRatio { get; init; } = .36;         // 네트 높이 = 코트 높이 × 이 값 (최소·최대 칸 안으로)
    public int NetMaxCells { get; init; } = 50;
    public int NetMinCells { get; init; } = 24;
    public double StemFumbleDeg { get; init; } = 25;     // 꼭지가 머리 쪽 ± 이 각도 안이면 헤딩 실수

    // 스파이크 세기 (×ScaleRatio): 보조가 켜져 있으면 이 값을 처음 시도하는 속도·내리꽂는 세기로 쓴다
    public double SpikeSpeed { get; init; } = 640;
    public double SpikeVy { get; init; } = 200;

    // 스파이크 보조 (4장)
    public bool SpikeAssist { get; init; }               // 켜면: 캡을 넘고 상대 코트에 떨어지는 속도 계산 · 뒤의 공은 스파이크 아님 · 0.15초 다시 닿기 막기 · 라이벌도 Ctrl 너그러움
    public double SpikeMinSpeed { get; init; } = 420;    // 보조가 고를 수 있는 가장 느린 속도 (×ScaleRatio)
    public double SpikeUpMax { get; init; } = 180;       // 보조가 위로 들어도 되는 가장 큰 세기 (×ScaleRatio)
    public double ClearMarginCells { get; init; } = 2;   // 캡 위로 넘길 여유
    public double LandMarginCells { get; init; } = 6;    // 먼 벽에서 떨어질 자리까지 여유

    // 돌부리 (6장): 달리기 시간·예고는 코트 길이에 비례해 줄어든다 (넓은 코트에서는 아래 값 그대로)
    public bool Stones { get; init; }
    public double StoneChance { get; init; } = .10;      // 긴 달리기 하나가 돌을 뽑을 확률
    public double StoneRunSec { get; init; } = .5;       // 이만큼 달리면 한 번 뽑는다 (최대)
    public double StoneWarnSec { get; init; } = .4;      // 솟은 돌까지 달려서 걸리는 시간 (최대)
    public double StoneRunSpan { get; init; } = .33;     // 한쪽 코트를 달려 건너는 시간 × 이 값 (좁은 코트 쪽 상한)
    public double StoneWarnSpan { get; init; } = .45;
    public double TripSec { get; init; } = .6;           // 넘어져 있는 시간

    // 블로킹 (7장)
    public bool Blocks { get; init; }
    public double BlockZoneBodies { get; init; } = 1.5;  // 네트에서 이만큼(몸 크기) 안에서 뛰면 블로킹 자세
    public double BlockWindowSec { get; init; } = .15;   // 꼭대기 ± 이 시간 안에 닿아야 맞은 블로킹
    public double BlockSpeed { get; init; } = 300;       // 막은 공이 가는 가로 속도 (×ScaleRatio)
    public double BlockVy { get; init; } = 240;          // 막은 공이 가는 아래 방향 세기 (×ScaleRatio)
    public bool BlockMissOnlySpike { get; init; }        // 켜면 어긋난 블로킹의 벌은 상대 스파이크일 때만 (YELL 2026-11-18)

    // 라이벌 (오른쪽)
    public BotSkill Rival { get; init; } = BotSkill.Old;

    public static readonly VolleyTune Old = new();

    public static readonly VolleyTune Now = new()
    {
        JumpV = 435,
        NetRatio = .30,
        NetMaxCells = 40,
        StemFumbleDeg = 15,
        SpikeSpeed = 1300,
        SpikeVy = 450,
        SpikeAssist = true,
        Stones = true,
        Blocks = true,
        BlockMissOnlySpike = true,
        Rival = BotSkill.Rival,
    };

    // 고치기 전부터 하나씩 넣어 가며 견주는 표용 (VolleyCheck.Table 의 steps)
    public static VolleyTune Numbers => Old with { JumpV = Now.JumpV, NetRatio = Now.NetRatio, NetMaxCells = Now.NetMaxCells, StemFumbleDeg = Now.StemFumbleDeg, Rival = BotSkill.Rival with { StoneHopProb = 0, BlockProb = 0 } };
    public static VolleyTune WithAssist => Numbers with { SpikeAssist = true };
    public static VolleyTune WithBlocks => WithAssist with { Blocks = true, BlockMissOnlySpike = true, Rival = BotSkill.Rival with { StoneHopProb = 0 } };
}

// 봇 실력 (8장). 오른쪽 라이벌과 표의 왼쪽 봇이 같은 함수(VolleySim.BotInput)로 움직이고, 실력 숫자만 다르다
// Legacy: 고치기 전 방식 (스파이크를 닿는 프레임마다 뽑는다 · 돌·블로킹 없음) — VolleyTune.Old 를 그대로 되살리는 용도
// SmartSpike: 공이 네트 위에 있고 캡을 넘길 수 있을 때만 Ctrl (낮은 공을 네트에 꽂지 않는다) · JumpCells: 공이 네트 꼭대기 위 이만큼(칸) 안으로 내려오면 뛴다
readonly record struct BotSkill(double ErrorCells, double ReactionSec, double SpikeProb, double StoneHopProb, double BlockProb, double BlockLateSec, bool Legacy = false, bool SmartSpike = false, double JumpCells = 15)
{
    public static readonly BotSkill Old = new(8, .06, .30, 0, 0, 0, true);
    public static readonly BotSkill Rival = new(14, 1.0, .45, .5, .6, .25, false, true, 40);
    public static readonly BotSkill Human = Rival with { SpikeProb = .55, StoneHopProb = .9, BlockProb = .6, BlockLateSec = 0 }; // 표의 왼쪽 「사람」
}

// 봇의 기억 (값 형식: 프레임마다 새로 잡는 메모리 없음). 경기마다 새것으로
struct BotState
{
    public double ReactionTimer, TargetX;
    public bool WasAir, SpikePlan;     // SpikePlan: 이번 점프에서 스파이크를 하기로 했나 (뛰는 순간 한 번만 뽑는다)
    public int StoneSeen, ServeSeen; public bool Hop;
    public int BlockSeen; public bool BlockPlan; public double BlockJumpAt;
}
