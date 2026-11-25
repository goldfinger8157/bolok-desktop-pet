namespace Bolok;

// 눈길 마중 자동 조종 (docs/ice-run.md 7장): 규칙이 주는 값만 본다 (화면을 보지 않는다).
// 몇 틱마다 「지금부터 Look 초 안에 어느 줄이 막혔나」를 보고 가려는 자리·뛸지를 정하고, React 초 늦게 그대로 한다.
// 자가 검사·속도 재기·봇 표·디버그 auto 가 쓴다.
// Bird: 새를 알아보는 몫 (새마다 한 번 정한다 — 못 알아본 새는 그냥 맞는다). 2026-11-25
readonly record struct TrailSkill(string Name, double Look, double React, double Noise, bool Dash, bool Glass, double Bird)
{
    public static readonly TrailSkill Easy = new("느긋한 봇", .9, .45, .12, false, false, .4);   // 처음 해 보는 사람
    public static readonly TrailSkill Good = new("잘하는 봇", 2.5, .15, .02, true, true, .92);     // 익숙한 사람
}

sealed class TrailBotState
{
    const int Room = 16;
    readonly double[] due = new double[Room], target = new double[Room];
    readonly bool[] jump = new bool[Room];
    int head, tail, ticks;
    public double Aim, Offset;          // 지금 따르는 자리, 사람처럼 어긋난 몫 (줄을 바꿀 때마다 다시 뽑는다)
    public bool Jumping, Dashing;
    public int LastLane = -99;
    public readonly Random Rng;
    public byte[] Noticed = Array.Empty<byte>();   // 새마다: 0 아직, 1 알아봤다, 2 못 알아봤다
    public TrailBotState(int seed) => Rng = new Random(seed);

    public void Push(double at, double x, bool j) { due[head % Room] = at; target[head % Room] = x; jump[head % Room] = j; head++; }

    // 때가 된 결정 중 가장 새것을 따른다
    public bool Pop(double now)
    {
        bool any = false;
        while (tail < head && due[tail % Room] <= now) { Aim = target[tail % Room]; Jumping = jump[tail % Room]; tail++; any = true; }
        return any;
    }

    public bool Decide() => (ticks++ & 3) == 0;
}

static class TrailBot
{
    const int Lanes = 21;
    const double JumpLead = .2;       // 막히기 이만큼(초) 앞에서 뛴다
    static readonly double[] first = new double[Lanes];
    static readonly bool[] soft = new bool[Lanes];     // 그 줄의 첫 막힘을 뛰어넘을 수 있다

    // 한 틱 (Trail.Update 바로 앞에 부른다)
    public static void Play(Trail t, in TrailSkill k, TrailBotState s)
    {
        if (t.State != Trail.Phase.Run) { t.Hold(false, false); return; }
        if (s.Decide()) { var (x, j) = Think(t, k, s); s.Push(t.Clock + k.React, x, j); }
        s.Pop(t.Clock);
        double dx = s.Aim + s.Offset - t.X;
        t.Steer(Math.Abs(dx) < .035 ? 0 : Math.Sign(dx));
        if (s.Jumping && !t.Airborne && !t.Halted) t.Jump();
        t.Hold(s.Dashing, false);
    }

    // 가려는 자리·뛸지·질주할지
    static (double X, bool Jump) Think(Trail t, in TrailSkill k, TrailBotState s)
    {
        var tune = t.T;
        double sp = Math.Max(t.Speed, tune.Slow), look = k.Look;
        Array.Fill(first, double.MaxValue);
        Array.Clear(soft);
        double bw = t.Body, margin = .07;
        TrailThing? glass = null;
        double gz = 0;
        for (int i = t.Lo; i < t.Things.Length; i++)
        {
            ref var th = ref t.Things[i];
            if (th.P0 - t.Pos > look * tune.Speed * 1.3 + tune.BallLead + 20) break;
            if (th.Gone || th.Hit) continue;
            double ahead = t.Ahead(th);
            if (th.Pick) { if (k.Glass && th.Kind != TrailKind.Berry && ahead > 6 && ahead < 70 && (glass == null || ahead < gz)) { glass = th; gz = ahead; } continue; }
            if (!th.Harm) continue;
            if (th.Kind == TrailKind.Ball && !th.On && th.P0 - t.Pos > tune.SeeDist) continue;
            double close = sp + (th.Kind == TrailKind.Ball ? tune.Speed * tune.BallRoll : 0);
            double tb = Math.Max(0, ahead - th.Len / 2 - 2.5) / close;
            if (ahead < -(th.Len / 2 + 3) || tb > look) continue;
            for (int j = 0; j < Lanes; j++)
            {
                double x = -1 + j * .1;
                if (Math.Abs(x - th.U) >= (th.W + bw) / 2 + margin) continue;
                if (tb < first[j]) { first[j] = tb; soft[j] = th.Kind != TrailKind.Snowman; }
            }
        }
        // 새: 날아오는 줄이 만나는 때(tb)에 막힌다. 어떤 새는 못 알아본다 (그냥 맞는다)
        if (s.Noticed.Length != t.Birds.Length) s.Noticed = new byte[t.Birds.Length];
        double bhw = (tune.BirdW + bw) / 2 + margin;
        for (int i = t.BirdLo; i < t.Birds.Length; i++)
        {
            ref var b = ref t.Birds[i];
            double a = b.M - t.Pos;
            if (a > t.BirdSeen || a / sp > look) break;
            if (b.Gone || a < -tune.BirdNear) continue;
            if (s.Noticed[i] == 0) s.Noticed[i] = (byte)(s.Rng.NextDouble() < k.Bird ? 1 : 2);
            if (s.Noticed[i] == 2) continue;
            double tb = Math.Max(0, a - tune.BirdNear) / sp;
            for (int j = 0; j < Lanes; j++)
                if (Math.Abs(-1 + j * .1 - b.U) < bhw && tb < first[j]) { first[j] = tb; soft[j] = false; }
        }
        int cur = Math.Clamp((int)Math.Round((t.X + 1) / .1), 0, Lanes - 1);
        int best = -1; double cost = double.MaxValue;
        for (int j = 0; j < Lanes; j++)    // 막히지 않았고 막히기 전에 닿을 수 있는 줄 가운데 가장 가까운 줄
        {
            double x = -1 + j * .1, move = Math.Abs(x - t.X) / tune.Steer + .1;
            if (first[j] != double.MaxValue && first[j] <= move) continue;
            double c = Math.Abs(x - t.X) + .12 * Math.Abs(x) + (first[j] != double.MaxValue ? 1.5 : 0);
            if (c < cost) (best, cost) = (j, c);
        }
        bool jump = false;
        if (best < 0)   // 갈 수 있는 빈 줄이 없다: 가장 늦게 막히는 줄로, 뛰어넘을 수 있으면 뛴다
        {
            double far = -1;
            for (int j = 0; j < Lanes; j++) if (first[j] > far + 1e-9) (best, far) = (j, first[j]);
            if (best < 0) best = cur;
        }
        if (first[best] != double.MaxValue && soft[best] && first[best] <= JumpLead + k.React * .6) jump = true;
        // 모래시계·별: 조금 돌아가도 그 줄이 비어 있으면 간다
        double aim = -1 + best * .1;
        if (glass is { } g && first[best] == double.MaxValue)
        {
            int gl = Math.Clamp((int)Math.Round((g.U + 1) / .1), 0, Lanes - 1);
            if (Math.Abs(g.U - aim) <= .75 && first[gl] == double.MaxValue && Math.Abs(g.U - t.X) / tune.Steer < gz / sp) aim = g.U;
        }
        if (best != s.LastLane) { s.LastLane = best; s.Offset = (s.Rng.NextDouble() * 2 - 1) * k.Noise; }
        aim = Math.Clamp(aim, -tune.OffLimit + .05, tune.OffLimit - .05);
        // 질주: 앞으로 1.2초 안에 막힌 데가 없고 가려는 줄도 비었을 때
        s.Dashing = k.Dash && first[cur] > 1.2 && first[best] > 1.2;
        return (aim, jump);
    }
}
