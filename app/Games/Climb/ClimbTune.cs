namespace Bolok;

// 클라이밍 손잡이 묶음 (docs/climb.md 4장). 숫자를 한곳에 모아 둔다. 거리는 「잎」, 시간은 초.
// 넘치거나 모자라면 이 순서로 만진다: PaceSlope → Gain → 꺾임 비율(Flip*) → 열매 간격
sealed record ClimbTune
{
    public static readonly ClimbTune Now = new();

    // 시간 막대 (4-3): Drain(n) = Gain × Pace(n), Pace(n) = min(PaceMax, PaceBase + n / PaceSlope) — 「버티려면 초당 몇 칸」
    public double Gain { get; init; } = .12;            // 한 칸 오를 때 차는 막대
    public double PaceBase { get; init; } = 1.4;
    public double PaceSlope { get; init; } = 70;           // 출발 값 110 → 봇 표로 70 (잘하는 봇 300~450잎, 느긋한 봇 60~100잎)
    public double PaceMax { get; init; } = 6.5;
    public double StartBar { get; init; } = 1;
    public double CleanGrace { get; init; } = .4;       // 덩굴손이 내려놓은 뒤 막대가 안 주는 시간

    // 몸짓·시간 (4-1, 4-6)
    public double HopSec { get; init; } = .08;          // 그림이 한 칸을 따라오는 시간 (규칙은 키를 누른 순간 올라가 있다)
    public double FallSec { get; init; } = .9;          // 떨어짐
    public double SaveSec { get; init; } = .5;          // 홀씨로 돌아옴 (막대·조작 멈춤)
    public double LiftSec { get; init; } = .6;          // 덩굴손이 던져 올림 (막대·조작 멈춤)
    public double WiltSec { get; init; } = .9;          // 시든 잎을 밟고 버티는 시간
    public double ResultSec { get; init; } = 2.5;       // 결과 화면
    public double ResultMin { get; init; } = .6;        // 이 뒤 클릭하면 닫는다

    // 줍는 것·기믹 (4-4): 열리는 높이와 간격 (잎)
    public bool Gimmicks { get; init; } = true;         // false 면 기믹 없는 맨 덩굴 (봇 표 견줌)
    public int BerryFrom { get; init; } = 10;
    public int BerryGapLo { get; init; } = 12;
    public int BerryGapHi { get; init; } = 20;
    public double BerryBar { get; init; } = .2;
    public int PuffFrom { get; init; } = 40;
    public int PuffGapLo { get; init; } = 70;
    public int PuffGapHi { get; init; } = 90;
    public int WiltFrom { get; init; } = 60;
    public int WiltGapLo { get; init; } = 14;
    public int WiltGapHi { get; init; } = 26;
    public int CurlFrom { get; init; } = 100;
    public int CurlGapLo { get; init; } = 80;
    public int CurlGapHi { get; init; } = 120;
    public int CurlUp { get; init; } = 5;               // 건너뛰는 잎 수 (점수도 그만큼)
    public int CurlQuiet { get; init; } = 20;           // 친구가 오는 100잎 이정표 뒤 이만큼은 건너뛰기를 안 둔다 (친구가 손 흔드는 동안 볼록이가 날아가지 않게)
    public int CloudFrom { get; init; } = 200;
    public int CloudLenLo { get; init; } = 8;
    public int CloudLenHi { get; init; } = 12;
    public int CloudGapLo { get; init; } = 6;           // 구름 구역(200~299) 안 띠 사이
    public int CloudGapHi { get; init; } = 14;
    public int CloudLateLo { get; init; } = 25;         // 그 뒤 가끔
    public int CloudLateHi { get; init; } = 45;
    public int CloudHide { get; init; } = 4;            // 지금 잎에서 이만큼 위 잎부터 구름이 가린다 (다음 3잎은 늘 보인다)
    public int Mile { get; init; } = 50;                // 이정표 (큰 꽃잎, 막대 가득)
    public int Zone { get; init; } = 100;               // 하늘 구역 (친구가 손 흔든다)

    // 덩굴 만들기 (4-2): 같은 쪽으로 L 칸 → 꺾임. L 뽑기는 높이에 따라
    public int MaxRun { get; init; } = 6;               // 가장 긴 줄
    public int MaxZig { get; init; } = 6;               // 한 칸씩 번갈아 꺾기 (좌우좌우) 최대
    public int EasyLeaves { get; init; } = 20;          // 처음 이만큼은 L≥2
    public double OneLo { get; init; } = .20;           // 30~149: L=1 이 0 → OneLo
    public double OneMid { get; init; } = .35;          // 150~299: OneLo → OneMid
    public double OneHi { get; init; } = .35;           // 300~: 멈춤
}
