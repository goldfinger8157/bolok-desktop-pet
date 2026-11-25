namespace Bolok;

// 클라이밍 자동 조종 (docs/climb.md 7장): 규칙이 주는 값만 본다 (화면을 보지 않는다).
// 한 칸마다 「누르기까지 걸리는 시간」과 「틀릴 확률」을 뽑는다. 꺾이는 칸은 더 늦고 더 자주 틀린다. 구름 띠 안(앞이 3잎만 보인다)에서는 꺾임 읽기가 더 어렵다.
// 자가 검사·속도 재기·봇 표·디버그 auto 가 쓴다.
readonly record struct ClimbSkill(string Name, double Same, double Flip, double Noise, double ErrSame, double ErrFlip, double FogErr = 1, double Warm = .3)
{
    // 틀릴 확률은 계획(.4%·2.5% / .05%·.5%)에서 봇 표로 올렸다: 계획 값이면 잘하는 봇이 거의 시간으로만 끝나고 느긋한 봇이 100잎을 넘는다
    public static readonly ClimbSkill Easy = new("느긋한 봇", .30, .48, .25, .006, .038, 1.6, .3);       // 처음 해 보는 사람
    public static readonly ClimbSkill Good = new("잘하는 봇", .15, .22, .15, .001, .012, 1.6, .05);     // 익숙한 사람
    public static readonly ClimbSkill Probe = new("검사용 봇", .10, .10, 0, 0, 0);                  // 속도 재기·공정함 검사 (틀리지 않는다)
}

sealed class ClimbBotState
{
    public readonly Random Rng;
    public double Due = -1;
    public ClimbBotState(int seed) => Rng = new Random(seed);
}

static class ClimbBot
{
    // 한 틱 (Climb.Update 바로 앞에 부른다)
    public static void Play(Climb t, in ClimbSkill k, ClimbBotState s)
    {
        if (t.Done) return;
        if (t.SaveLeft > 0 || t.LiftLeft > 0) { s.Due = Math.Max(s.Due, t.Clock + k.Flip); return; }   // 멈춤이 끝난 뒤 한 박자 보고 다시
        if (s.Due < 0) s.Due = t.Clock + .5;                                                           // 판이 열리고 첫 키까지
        if (t.Clock < s.Due) return;
        int dir = t.At(t.N + 1).Dir;
        bool flip = dir != t.At(t.N).Dir;                                                   // 지금 선 잎이 온 쪽과 다음 쪽이 다른가
        double err = (flip ? k.ErrFlip : k.ErrSame) * (t.At(t.N + t.T.CloudHide).Fog ? k.FogErr : 1) * (t.N < t.T.EasyLeaves ? k.Warm : 1);   // 앞이 구름에 가렸으면 더 틀린다, 처음 잎은 조심해서 덜 틀린다
        if (s.Rng.NextDouble() < err) dir = -dir;
        t.Press(dir);
        if (t.Done) return;
        // 다음 칸까지: 다음 잎이 지금 누른 쪽과 같은가 꺾이는가
        bool nextFlip = t.At(t.N + 1).Dir != t.At(t.N).Dir;
        double d = (nextFlip ? k.Flip : k.Same) * (1 + k.Noise * (s.Rng.NextDouble() * 2 - 1));
        s.Due = t.Clock + Math.Max(.04, d);
    }
}
