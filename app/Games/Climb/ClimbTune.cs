namespace Bolok;

// 클라이밍 손잡이 묶음 (docs/climb.md 4장, docs/climb-combo.md 2장). 숫자를 한곳에 모아 둔다. 거리는 「잎」, 시간은 초.
// 넘치거나 모자라면 이 순서로 만진다: PaceMax(잘하는 봇의 시간 몫) → PaceSlope(느긋한 봇의 시간 몫) → PuffGap(높이) → OneTop·구름(잘하는 봇의 긴 꼬리) → ComboBar
sealed record ClimbTune
{
    public static readonly ClimbTune Now = new();

    // 시간 막대 (4-3): Drain(n) = Gain × Pace(n), Pace(n) = min(PaceMax, PaceBase + n / PaceSlope) — 「버티려면 초당 몇 칸」
    public double Gain { get; init; } = .12;            // 한 칸 오를 때 차는 막대
    public double PaceBase { get; init; } = 1.4;
    public double PaceSlope { get; init; } = 110;          // 막대가 느리게 빨라진다 (시간보다 높이: 느긋한 봇이 시간으로 끝나는 판이 줄어든다)
    public double PaceMax { get; init; } = 6.8;
    public double StartBar { get; init; } = 1;

    // 콤보 (1장): 0.5초 안에 이어 오르면 이어진다 (틱으로 센다). 5·15·30 에서 단계가 오르고 5마다 막대가 단계 값만큼 찬다. 점수는 안 바꾼다
    public int ComboTicks { get; init; } = 30;
    public int Tier1 { get; init; } = 5;
    public int Tier2 { get; init; } = 15;
    public int Tier3 { get; init; } = 30;
    public int ComboEvery { get; init; } = 5;
    public double[] ComboBar { get; init; } = { .04, .06, .08 };
    public int[] HopArc { get; init; } = { 6, 4, 3, 2 };   // 한 칸 폴짝의 높이 (칸): 콤보 단계가 오를수록 낮아져 빠르게 쳐도 다음 발판이 잘 읽힌다

    // 카페 (5장): 꺾이는 발판 바깥쪽에 가끔. 안에서 막대가 초당 CafeFill 만큼 차고, 콤보는 끊긴다. 머무는 한도·벌 없음
    public int CafeFrom { get; init; } = 60;            // 첫 카페는 CafeFrom ~ CafeFrom+CafeFromSpan 잎 뒤 첫 맞는 꺾임
    public int CafeFromSpan { get; init; } = 30;
    public int CafeGapLo { get; init; } = 100;
    public int CafeGapHi { get; init; } = 150;
    public double CafeInSec { get; init; } = .4;        // 걸어 들어가는 동안 (키 안 먹음)
    public double CafeOutSec { get; init; } = .3;       // 걸어 나오는 동안 (키 안 먹음)
    public double CafeFill { get; init; } = .5;         // 안에서 초당 차는 막대 (2초면 가득)
    public double CafeSipSec { get; init; } = 6;        // 이만큼 지나면 컵이 비고 김이 멎는다 (그림 신호일 뿐)
    public double CafeGrace { get; init; } = .5;        // 나온 뒤 막대가 안 주는 시간

    // 몸짓·시간 (4-1, 4-6)
    public double HopSec { get; init; } = .08;          // 그림이 한 칸을 따라오는 시간 (규칙은 키를 누른 순간 올라가 있다)
    public double FallSec { get; init; } = .9;          // 떨어짐
    public double SaveSec { get; init; } = .5;          // 홀씨로 돌아옴 (막대·조작 멈춤)
    public double WiltSec { get; init; } = .9;          // 시든 잎을 밟고 버티는 시간
    public double ResultSec { get; init; } = 2.5;       // 결과 화면
    public double ResultMin { get; init; } = .6;        // 이 뒤 클릭하면 닫는다

    // 줍는 것·기믹 (4-4): 열리는 높이와 간격 (잎)
    public bool Gimmicks { get; init; } = true;         // false 면 기믹 없는 맨 덩굴 (봇 표 견줌)
    public int BerryFrom { get; init; } = 10;
    public int BerryGapLo { get; init; } = 12;
    public int BerryGapHi { get; init; } = 20;
    public double BerryBar { get; init; } = .2;
    public int PuffFrom { get; init; } = 30;
    public int PuffGapLo { get; init; } = 50;
    public int PuffGapHi { get; init; } = 70;
    public int WiltFrom { get; init; } = 60;
    public int WiltGapLo { get; init; } = 14;
    public int WiltGapHi { get; init; } = 26;
    public int CloudFrom { get; init; } = 150;
    public int CloudZone { get; init; } = 150;          // 구름 구역 길이 (CloudFrom ~ +CloudZone: 띠가 잦다)
    public int CloudLenLo { get; init; } = 8;
    public int CloudLenHi { get; init; } = 12;
    public int CloudGapLo { get; init; } = 6;           // 구름 구역(150~299) 안 띠 사이
    public int CloudGapHi { get; init; } = 14;
    public int CloudLateLo { get; init; } = 14;         // 그 뒤에도 자주
    public int CloudLateHi { get; init; } = 28;
    public int CloudHide { get; init; } = 4;            // 지금 잎에서 이만큼 위 잎부터 구름이 가린다 (다음 3잎은 늘 보인다)
    public int Mile { get; init; } = 50;                // 이정표 (큰 꽃잎, 막대 가득)
    public int Zone { get; init; } = 100;               // 하늘 구역 (덩굴 테마의 높이 구역 잎 색·곁 장식)

    // 덩굴 만들기 (4-2): 같은 쪽으로 L 칸 → 꺾임. L 뽑기는 높이에 따라
    public int MaxRun { get; init; } = 6;               // 가장 긴 줄
    public int MaxZig { get; init; } = 6;               // 한 칸씩 번갈아 꺾기 (좌우좌우) 최대
    public int EasyLeaves { get; init; } = 20;          // 처음 이만큼은 L≥2
    public double OneLo { get; init; } = .20;           // 30~149: L=1 이 0 → OneLo
    public double OneMid { get; init; } = .40;          // 150~299: OneLo → OneMid
    public double OneHi { get; init; } = .40;           // 300~: OneTopAt 까지 OneHi → OneTop (높을수록 읽기 어렵다)
    public double OneTop { get; init; } = .55;
    public int OneTopAt { get; init; } = 600;
}
