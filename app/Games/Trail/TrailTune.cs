namespace Bolok;

// 눈길 마중 손잡이 묶음 (docs/ice-run.md 4장). 숫자를 한곳에 모아 둔다. 거리는 「걸음」, 좌우 자리는 길 가운데 0 · 가장자리 ±1.
// 넘치거나 모자라면 이 순서로 만진다: GateSec → 묶음 틈 → 넘어짐 손해 초 → 몸 폭 봐주기
sealed record TrailTune
{
    public static readonly TrailTune Now = new();

    // 한 판 (4-1)
    public int Legs { get; init; } = 5;                 // 쉼터 수 = 구간 수
    public double LegLen { get; init; } = 1500;         // 구간 길이 (걸음)
    public double Speed { get; init; } = 50;            // 기본 빠르기 (걸음/초)
    public double Dash { get; init; } = 68;             // Shift 질주
    public double Slow { get; init; } = 35;             // ↓ 천천히
    public double Accel { get; init; } = 46;            // 빠르기 되찾기 (초당)
    public double StartSec { get; init; } = 42;         // 시작 시간
    public double GateSec { get; init; } = 32;          // 1단계 쉼터 보너스 (남은 시간에 더한다)
    public double GateSecStep { get; init; }       // 단계마다 줄어드는 보너스
    public double GateSecQuad { get; init; } = .95;     // 단계가 오를수록 더 가파르게: 줄어드는 몫 = Quad × (단계−1)^Pow
    public double GateSecPow { get; init; } = 1.8;
    public double GateSecMin { get; init; } = 20;
    public double TimeCap { get; init; } = 60;          // 시간은 이 이상 안 쌓인다
    public double SeeDist { get; init; } = 420;         // 보이기 시작하는 거리 (기본 빠르기로 8초 앞, 12% 크기 점)
    public double CountSec { get; init; } = .8;         // 3·2·1 한 칸

    // 움직임 (4-2)
    public double Steer { get; init; } = 1.6;           // 좌우 (초당 자리)
    public double SteerStop { get; init; } = .08;       // 떼면 멈추는 시간
    public double OffRoad { get; init; } = .7;          // 길 밖 눈밭 빠르기 배율
    public double OffLimit { get; init; } = 1.15;       // 더는 못 나간다
    public double JumpSec { get; init; } = .55;
    public double JumpCells { get; init; } = 26;        // 그림에서 뛰는 높이 (칸)
    public double AirCells { get; init; } = 3;          // 이 높이를 넘으면 장애물 위 (공중)
    public double BodyFit { get; init; } = .85;         // 닿음 판정 = 몸 폭의 85% (봐주기)
    public double HalfRoad { get; init; } = 70;         // 길 반폭 (칸, 볼록이 줄에서)

    // 닿으면 (4-3): 멈추는 시간과 닿은 뒤 안 닿는 시간
    public double PileSlowSec { get; init; } = .7;     // 눈더미: 빠르기 40% 로 이만큼
    public double PileSlowTo { get; init; } = .4;
    public double HoleHalt { get; init; } = .9;        // 물구멍
    public double SnowmanHalt { get; init; } = .6;
    public double BallHalt { get; init; } = .7;
    public double SafeAfter { get; init; } = 1.3;       // 멈춤이 끝난 뒤에도 이만큼은 안 닿는다
    public double IceGrip { get; init; } = .3;          // 빙판 위 좌우가 먹는 몫
    public double WindPush { get; init; } = .5;         // 옆바람 (초당 자리)
    public double WindSec { get; init; } = 3;
    public double WindWarn { get; init; } = 100;        // 예고 눈발이 보이는 거리 (걸음, 2초 앞)
    public double BallRoll { get; init; } = .6;         // 굴러오는 눈덩이가 길 쪽으로 오는 빠르기 (기본 빠르기 몫)

    // 줍는 것 (4-4)
    public double GlassSec { get; init; } = 3;
    public double StarSec { get; init; } = 4;
    public int BerryScore { get; init; } = 10;
    public int GateScore { get; init; } = 100;

    // 길 만들기 (4-3 공정하게)
    public double MoreBase { get; init; } = .42;        // 한 묶음에 하나 더 나올 확률 (1단계 첫 구간)
    public double MoreStage { get; init; } = .07;       // 단계마다 늘어난다
    public double MoreLeg { get; init; } = .03;         // 구간마다 늘어난다
    public double MoreDecay { get; init; } = .55;       // 셋·넷째는 이만큼씩 어렵게
    public double GapMin { get; init; } = .5;           // 묶음마다 지나갈 틈 (좌우 자리 폭)
    public double GapBody { get; init; } = .22;         // 틈 = 몸 폭 + 이만큼 (더 넓은 몸은 틈도 넓다)
    public double BundleGap1 { get; init; } = 1.4;      // 묶음 사이 (초, 1단계)
    public double BundleGapStep { get; init; } = .1;   // 단계마다 줄어든다
    public double BundleGapMin { get; init; } = .6;
    public double BundleGapLeg { get; init; } = .03;    // 구간이 갈수록 조금 잦아진다 (몫)
    public double GateClear { get; init; } = 30;        // 쉼터 앞뒤 빈 자리 (걸음)
    public double FirstClear { get; init; } = 110;      // 첫 묶음까지 (몸풀기)
    public double NewKindClear { get; init; } = 40;     // 처음 보이는 종류 앞뒤 더 비움
    public double BallLead { get; init; } = 157;        // 굴러오는 눈덩이가 처음 서 있는 자리 (만나는 곳보다 얼마나 앞)
    public double BallPad { get; init; } = 40;          // 눈덩이 묶음 앞뒤 더 비움 (만나는 곳이 빠르기에 따라 달라진다)
}
