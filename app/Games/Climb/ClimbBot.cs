namespace Bolok;

// 클라이밍 자동 조종 (docs/climb.md 7장): 규칙이 주는 값만 본다 (화면을 보지 않는다).
// 한 칸마다 「누르기까지 걸리는 시간」과 「틀릴 확률」을 뽑는다. 꺾이는 칸은 더 늦고 더 자주 틀린다. 구름 띠 안(앞이 3잎만 보인다)에서는 꺾임 읽기가 더 어렵다.
// 자가 검사·속도 재기·봇 표·디버그 auto 가 쓴다.
// 멈칫(Pause): 가끔 앞길을 다시 보느라 다음 칸이 PauseX 배 늦다 (콤보가 끊긴다, 시든 잎 위면 떨어진다). 서두름(Rush): 콤보 1단부터 이으려 빨라지고(×Rush) 그만큼 더 틀린다(×RushErr).
// 카페: 발판에 카페가 붙었을 때 막대가 CafeBar 아래이고 콤보가 CafeCombo 아래면 들어간다 (CafeCombo 0 이면 콤보는 안 본다), 아니면 CafeJust 확률로 그냥 들른다. 막대가 가득 찬 뒤 CafeExtra 범위만큼 더 쉰다.
readonly record struct ClimbSkill(string Name, double Same, double Flip, double Noise, double ErrSame, double ErrFlip, double FogErr = 1, double Warm = .3,
    double Pause = 0, double PauseX = 2.2, double Rush = 1, double RushErr = 1, double CafeBar = -1, int CafeCombo = 0, double CafeJust = 0, double CafeExtraLo = 0, double CafeExtraHi = 0)
{
    // 틀릴 확률은 계획(.4%·2.5% / .05%·.5%)에서 봇 표로 올렸다: 계획 값이면 잘하는 봇이 거의 시간으로만 끝나고 느긋한 봇이 100잎을 넘는다
    public static readonly ClimbSkill Easy = new("느긋한 봇", .30, .48, .25, .006, .038, 1.6, .3, .04, 2.2, .95, 1.15, .5, 0, .3, .5, 1.5);       // 처음 해 보는 사람
    public static readonly ClimbSkill Good = new("잘하는 봇", .15, .22, .15, .001, .012, 1.6, .05, .01, 2.2, .92, 1.15, .3, 15, 0, 0, 0);     // 익숙한 사람
    public static readonly ClimbSkill Probe = new("검사용 봇", .10, .10, 0, 0, 0);                  // 속도 재기·공정함 검사 (틀리지 않는다, 카페에 안 들어간다)
}

sealed class ClimbBotState
{
    public readonly Random Rng;
    public double Due = -1;
    public double Stay;          // 카페 안에서 들어가는 걸음 뒤 더 쉬는 시간
    public ClimbBotState(int seed) => Rng = new Random(seed);
}

static class ClimbBot
{
    // 한 틱 (Climb.Update 바로 앞에 부른다)
    public static void Play(Climb t, in ClimbSkill k, ClimbBotState s)
    {
        if (t.Done) return;
        if (t.SaveLeft > 0 || t.CafePhase is 1 or 3) { s.Due = Math.Max(s.Due, t.Clock + k.Flip); return; }   // 멈춤이 끝난 뒤 한 박자 보고 다시
        if (t.CafePhase == 2)                                                                                // 카페 안: 정한 만큼 쉬고 길 쪽 키로 나온다
        {
            if (t.CafeStay < t.T.CafeInSec + s.Stay) return;
            t.Press(t.At(t.N + 1).Dir);
            s.Due = t.Clock + t.T.CafeOutSec + k.Flip;
            return;
        }
        if (s.Due < 0) s.Due = t.Clock + .5;                                                           // 판이 열리고 첫 키까지
        if (t.Clock < s.Due) return;
        int dir = t.At(t.N + 1).Dir;
        ref var cur = ref t.At(t.N);
        bool intend = false;
        if (cur.Cafe != 0 && !cur.Visited && (t.Bar < k.CafeBar && (k.CafeCombo == 0 || t.Combo < k.CafeCombo) || k.CafeJust > 0 && s.Rng.NextDouble() < k.CafeJust))
        {
            dir = cur.Cafe; intend = true;                                                         // 일부러 들른다 (막대가 가득 찰 때까지 + 조금)
            s.Stay = (1 - t.Bar) / t.T.CafeFill + (k.CafeExtraHi > 0 ? k.CafeExtraLo + (k.CafeExtraHi - k.CafeExtraLo) * s.Rng.NextDouble() : 0);
        }
        else
        {
            bool flip = dir != t.At(t.N).Dir;                                                   // 지금 선 잎이 온 쪽과 다음 쪽이 다른가
            double err = (flip ? k.ErrFlip : k.ErrSame) * (t.At(t.N + t.T.CloudHide).Fog ? k.FogErr : 1) * (t.N < t.T.EasyLeaves ? k.Warm : 1)   // 앞이 구름에 가렸으면 더 틀린다, 처음 잎은 조심해서 덜 틀린다
                * (t.Tier >= 1 ? k.RushErr : 1);                                                // 콤보를 잇느라 서두르면 더 틀린다
            if (s.Rng.NextDouble() < err) dir = -dir;
        }
        t.Press(dir);
        if (t.Done) return;
        if (t.CafePhase == 1)                                                                     // 카페에 들어갔다 (틀려서 들어갔으면 한 박자 뒤 나온다)
        {
            if (!intend) s.Stay = k.Flip;
            s.Due = t.Clock;
            return;
        }
        // 다음 칸까지: 다음 잎이 지금 누른 쪽과 같은가 꺾이는가
        bool nextFlip = t.At(t.N + 1).Dir != t.At(t.N).Dir;
        double d = (nextFlip ? k.Flip : k.Same) * (1 + k.Noise * (s.Rng.NextDouble() * 2 - 1));
        if (k.Pause > 0 && s.Rng.NextDouble() < k.Pause) d *= k.PauseX;                          // 가끔 앞길을 다시 본다
        else if (t.Tier >= 1) d *= k.Rush;                                                      // 콤보가 이어지면 서두른다
        s.Due = t.Clock + Math.Max(.04, d);
    }
}
