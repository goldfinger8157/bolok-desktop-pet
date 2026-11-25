namespace Bolok;

// 눈길 마중 손잡이 묶음 (docs/ice-run.md 4장). 숫자를 한곳에 모아 둔다. 거리는 「걸음」, 좌우 자리는 길 가운데 0 · 가장자리 ±1.
// 넘치거나 모자라면 이 순서로 만진다: GateSec → 묶음 틈 → 넘어짐 손해 초 → 몸 폭 봐주기
sealed record TrailTune
{
    public static readonly TrailTune Now = new();

    // 한 판 (4-1)
    public int Legs { get; init; } = 5;                 // 쉼터 수 = 구간 수
    public double LegLen { get; init; } = 3915;         // 구간 길이 (걸음). 빠르기를 올리며 같은 비율로 늘려 한 판 길이는 그대로 (2026-11-24, 11-25 ×1.5)
    public double Speed { get; init; } = 130;           // 기본 빠르기 (걸음/초). YELL 2026-11-24 「조금 느리다, 답답해」 → 50 에서 64, 다시 「아직도 느려」 → 87, 2026-11-25 「달리기/기본속도 50% 향상」 → 130 (질주 177). 구간·보이는 거리도 ×1.5 로 늘려 시간 값은 그대로
    public double Dash { get; init; } = 177;             // Shift 질주
    public double Slow { get; init; } = 92;             // ↓ 천천히
    public double Accel { get; init; } = 120;           // 빠르기 되찾기 (초당)
    public double StartSec { get; init; } = 42;         // 시작 시간
    public double GateSec { get; init; } = 31.5;        // 1단계 쉼터 보너스 (남은 시간에 더한다). 2026-11-25 32 → 31.5 (느긋한 봇 1단계 64 → 52% 로 낮추는 난이도)
    public double GateSecStep { get; init; }       // 단계마다 줄어드는 보너스
    public double GateSecQuad { get; init; } = .9;      // 단계가 오를수록 더 가파르게: 줄어드는 몫 = Quad × (단계−1)^Pow
    public double GateSecPow { get; init; } = 1.8;
    public double GateSecMin { get; init; } = 20;
    public double TimeCap { get; init; } = 60;          // 시간은 이 이상 안 쌓인다
    public double SeeDist { get; init; } = 1060;         // 보이기 시작하는 거리 (기본 빠르기로 8초 앞, 12% 크기 점)
    public double CountSec { get; init; } = .8;         // 3·2·1 한 칸

    // 움직임 (4-2)
    public double Steer { get; init; } = 1.9;           // 좌우 (초당 자리). 앞 빠르기를 87 로 올리며 1.6 → 1.9 (장애물이 더 빨리 다가온다)
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
    public double WindWarn { get; init; } = 261;        // 예고 눈발이 보이는 거리 (걸음, 2초 앞)
    public double BallRoll { get; init; } = .6;         // 굴러오는 눈덩이가 길 쪽으로 오는 빠르기 (기본 빠르기 몫)

    // 줍는 것 (4-4)
    public double GlassSec { get; init; } = 3;
    public double StarSec { get; init; } = 4;
    public int BerryScore { get; init; } = 10;
    public int GateScore { get; init; } = 100;

    // 길 만들기 (4-3 공정하게)
    public double MoreBase { get; init; } = .42;        // 한 묶음에 하나 더 나올 확률 (1단계 첫 구간)
    public double MoreStage { get; init; } = .035;      // 단계마다 늘어난다
    public double MoreLeg { get; init; } = .03;         // 구간마다 늘어난다
    public double MoreDecay { get; init; } = .55;       // 셋·넷째는 이만큼씩 어렵게
    public double GapMin { get; init; } = .5;           // 묶음마다 지나갈 틈 (좌우 자리 폭)
    public double GapBody { get; init; } = .22;         // 틈 = 몸 폭 + 이만큼 (더 넓은 몸은 틈도 넓다)
    public double BundleGap1 { get; init; } = 1.4;      // 묶음 사이 (초, 1단계)
    public double BundleGapStep { get; init; } = .09;  // 단계마다 줄어든다
    public double BundleGapMin { get; init; } = .6;
    public double BundleGapLeg { get; init; } = .03;    // 구간이 갈수록 조금 잦아진다 (몫)
    // 아래 걸음 값들은 빠르기 87 → 130 에 맞춰 ×1.5 (시간으로는 그대로)
    public double GateClear { get; init; } = 62;        // 쉼터 앞뒤 빈 자리 (걸음)
    public double FirstClear { get; init; } = 225;      // 첫 묶음까지 (몸풀기)
    public double NewKindClear { get; init; } = 81;     // 처음 보이는 종류 앞뒤 더 비움
    public double BallLead { get; init; } = 321;        // 굴러오는 눈덩이가 처음 서 있는 자리 (만나는 곳보다 얼마나 앞)
    public double BallPad { get; init; } = 81;          // 눈덩이 묶음 앞뒤 더 비움 (만나는 곳이 빠르기에 따라 달라진다)

    // 새 (2026-11-25 YELL 「가끔 세로로 날라가는 새를 등장시키고, 맞으면 감점」): 길 한 줄을 따라 멀리서 볼록이 쪽으로 날아온다.
    // 만나는 자리 M 은 길 위 한 곳에 박혀 있다 (볼록이 빠르기와 무관 → 쉼터·묶음과 겹치지 않게 미리 비울 수 있다). 보이는 거리만 BirdRush 배로 빨리 줄어든다
    public double BirdW { get; init; } = .26;           // 새 폭 (좌우 자리). 닿음 = (BirdW + 몸 폭) / 2 안
    public double BirdNear { get; init; } = 4;          // M 앞뒤 이만큼(걸음) 안이면 닿는다 (한 틱에 가는 거리의 한참 위라 뚫고 지나지 않는다)
    public int BirdPenalty { get; init; } = 50;         // 맞으면 점수 −50 (0 밑으로는 안 내려간다). 시간·멈춤 손해 없음
    public double BirdRush { get; init; } = 1.8;        // 보이는 거리가 줄어드는 배율 (그림에서 새는 땅 것보다 이만큼 빨리 다가온다)
    public double BirdHeight { get; init; } = 18;       // 날아오는 높이 (칸, 볼록이 머리 높이: 점프해도 못 피한다)
    public double BirdFlap { get; init; } = 7;          // 날갯짓 (초당)
    public double BirdFirst { get; init; } = 1300;      // 첫 새의 만나는 자리 (걸음, 기본 빠르기로 10초 → 질주로도 4초 뒤부터 보인다)
    public double BirdGapLo { get; init; } = 10;        // 새 사이 (초, 1단계 10~20초). 단계마다 BirdGapStep 씩 (위쪽은 두 배) 줄어든다
    public double BirdGapHi { get; init; } = 20;
    public double BirdGapStep { get; init; } = .8;
    public double BirdScan { get; init; } = 360;        // 자리를 못 찾으면 이만큼 앞에서 다시 (걸음)
    public double BirdPad { get; init; } = 24;          // 앞뒤 묶음에서 이만큼 떨어진 자리에서만 (걸음)
    public double BirdGateClear { get; init; } = 120;   // 쉼터와는 (보이는 동안 + 이만큼) 떨어진다 (걸음)
    public double BirdReact { get; init; } = .35;       // 길 만들 때 사람이 알아채는 데 걸리는 시간 (초): 피하는 길이 이 시간을 빼고도 닿는지 본다
    public double BirdMargin { get; init; } = .05;      // 피할 자리는 닿는 폭보다 이만큼 더 바깥
}
