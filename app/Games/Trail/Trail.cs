namespace Bolok;

// 눈길 마중 규칙 (화면 없음, docs/ice-run.md 4장): 길 만들기·움직임·닿음·시간·쉼터·점수.
// 고정 박자 1/60초로 돈다 (Update 가 쌓아서 나눠 돌림) → 프레임 빠르기와 무관하게 씨가 같으면 같은 판.
// 길 위의 것은 고정 배열 하나 (판을 만들 때 한 번), 도는 동안 새로 만드는 것 없음.
enum TrailKind : byte { Pile, Hole, Snowman, Ball, Ice, Wind, Berry, Glass, Star }

// 길 위의 것 하나. P: 지금 걸음 자리(눈덩이만 바뀐다), P0: 처음 자리(순서를 정한다), U: 좌우 자리(바람은 방향 ±1), W: 좌우 폭, Len: 길이
struct TrailThing
{
    public TrailKind Kind;
    public float U, W, Len;
    public double P, P0;
    public bool Gone, Hit, On;
    public byte Frame;
    public readonly bool Harm => Kind <= TrailKind.Ball;
    public readonly bool Pick => Kind >= TrailKind.Berry;
}

// 묶음 한 칸: 한 가지 종류가 길을 가로질러 서고 지나갈 틈이 하나 있다 (자가 검사가 공정함을 본다)
struct TrailBundle
{
    public TrailKind Kind;
    public double Start, End, Gap, GapHalf;   // 걸음 자리 범위, 틈 가운데(좌우 자리)와 반 폭
    public bool Fresh;                         // 이 판에서 처음 보이는 종류 (혼자 가운데에)
}

sealed class Trail
{
    public enum Phase { Count, Run, Win, Over }
    public const double Tick = 1.0 / 60;

    public readonly TrailTune T;
    public readonly int Stage, Seed;
    public readonly double Body;                 // 몸 폭 (좌우 자리 단위, 판정에 쓰는 85%)
    public TrailThing[] Things = Array.Empty<TrailThing>();
    public List<TrailBundle> Bundles = new();
    public readonly double[] Gates;              // 쉼터 걸음 자리
    double[] knotAt = Array.Empty<double>(), knotV = Array.Empty<double>();

    // 지금
    public Phase State = Phase.Count;
    public double Clock, RunClock, CountLeft, EndedFor;
    public double Pos, Speed, X, Vx, Time;
    public double JumpT = -1;                    // 뛰는 중이면 0..JumpSec
    public double HaltLeft, SlowLeft, InvLeft, StarLeft, WindLeft, WindDir, HurtAt = -9, GateAt = -9, ItemAt = -9;
    public bool OnIce, Dashing, TimeOut;
    public int Leg;                              // 지난 쉼터 수
    public int Score, Berries, Hits, Glasses, Stars, Smashes;
    public double DashSec, RunSec;               // 질주한 시간, 달린 시간 (질주 비율)
    public TrailKind LastHit;
    public double Curve;                         // 지금 길의 굽이 (그림용, -0.6~0.6)
    public Action<string>? OnEvent;
    int lo;                                      // 아직 지나지 않은 첫 것
    double acc;
    int axis; bool jumpPress, dash, slow;

    public double Air => JumpT < 0 ? 0 : T.JumpCells * 4 * (JumpT / T.JumpSec) * (1 - JumpT / T.JumpSec);
    public bool Airborne => Air > T.AirCells;
    public bool Halted => HaltLeft > 0;
    public bool Safe => InvLeft > 0;
    public double GateBonus => GateBonusOf(T, Stage);
    public static double GateBonusOf(TrailTune t, int stage) => Math.Max(t.GateSecMin, t.GateSec - (stage - 1) * t.GateSecStep - Math.Pow(stage - 1, t.GateSecPow) * t.GateSecQuad);
    public bool Done => State is Phase.Win or Phase.Over;
    public int LegsDone => Leg;

    // bodyHalfCells: 지금 단계의 몸 반폭 (PetArt.Size 의 rx, 칸)
    public Trail(int seed, int stage, double bodyHalfCells, TrailTune? tune = null)
    {
        T = tune ?? TrailTune.Now;
        (Seed, Stage) = (seed, Math.Max(1, stage));
        Body = bodyHalfCells * 2 * T.BodyFit / T.HalfRoad;
        Gates = new double[T.Legs];
        for (int i = 0; i < Gates.Length; i++) Gates[i] = T.LegLen * (i + 1);
        Time = T.StartSec;
        CountLeft = T.CountSec * 3;
        Build();
    }

    // ── 조작 (프레임마다 Update 앞에) ──
    public void Steer(int ax) => axis = Math.Clamp(ax, -1, 1);
    public void Jump() => jumpPress = true;
    public void Hold(bool dashKey, bool slowKey) => (dash, slow) = (dashKey, slowKey);

    public void Update(double dt)
    {
        acc += dt;
        while (acc >= Tick) { acc -= Tick; Step(); }
    }

    public void Fire(string ev) => OnEvent?.Invoke(ev);

    void Step()
    {
        Clock += Tick;
        if (State == Phase.Count)
        {
            jumpPress = false;
            int before = (int)Math.Ceiling(CountLeft / T.CountSec);
            CountLeft -= Tick;
            if (CountLeft <= 0) { State = Phase.Run; Fire("go"); }
            else if ((int)Math.Ceiling(CountLeft / T.CountSec) != before) Fire("count");
            return;
        }
        if (Done) { EndedFor += Tick; jumpPress = false; return; }
        RunClock += Tick;
        Time = Math.Max(0, Time - Tick);
        HurtLeftTick();
        // 빠르기
        double target = StarLeft > 0 || dash ? T.Dash : slow ? T.Slow : T.Speed;
        if (HaltLeft > 0) Speed = 0;
        else
        {
            if (SlowLeft > 0) target = Math.Min(target, T.Speed * T.PileSlowTo);
            Speed += Math.Clamp(target - Speed, -T.Accel * 2 * Tick, T.Accel * Tick);
            if (SlowLeft > 0) Speed = Math.Min(Speed, target);
        }
        Pos += (Math.Abs(X) > 1 ? Speed * T.OffRoad : Speed) * Tick;
        RunSec += Tick;
        Dashing = HaltLeft <= 0 && (dash || StarLeft > 0);
        if (Dashing) DashSec += Tick;
        // 뛰기
        if (JumpT >= 0) { JumpT += Tick; if (JumpT >= T.JumpSec) JumpT = -1; }
        else if (jumpPress && HaltLeft <= 0) { JumpT = 0; Fire("jump"); }
        jumpPress = false;
        // 좌우
        double steer = HaltLeft > 0 ? 0 : axis * T.Steer * (OnIce ? T.IceGrip : 1);
        double rate = T.Steer / T.SteerStop * Tick;
        Vx += Math.Clamp(steer - Vx, -rate, rate);
        double push = WindLeft > 0 ? WindDir * T.WindPush : 0;
        X = Math.Clamp(X + (Vx + push) * Tick, -T.OffLimit, T.OffLimit);
        Curve = CurveAt(Pos);
        Things_();
        Gate_();
        if (Time <= 0 && State == Phase.Run && !Airborne && HaltLeft <= 0) { TimeOut = true; State = Phase.Over; EndedFor = 0; Fire("over"); }
    }

    void HurtLeftTick()
    {
        if (HaltLeft > 0) HaltLeft = Math.Max(0, HaltLeft - Tick);
        if (SlowLeft > 0) SlowLeft = Math.Max(0, SlowLeft - Tick);
        if (InvLeft > 0) InvLeft = Math.Max(0, InvLeft - Tick);
        if (StarLeft > 0) StarLeft = Math.Max(0, StarLeft - Tick);
        if (WindLeft > 0) WindLeft = Math.Max(0, WindLeft - Tick);
    }

    // 길 위의 것과 닿음·줍기·바람·눈덩이 굴리기
    void Things_()
    {
        while (lo < Things.Length && Things[lo].P0 + Things[lo].Len + 30 < Pos - 8) lo++;
        OnIce = false;
        bool air = Airborne;
        for (int i = lo; i < Things.Length; i++)
        {
            ref var t = ref Things[i];
            if (t.P0 > Pos + T.SeeDist + 5) break;
            if (t.Gone) continue;
            if (t.Kind == TrailKind.Ball)
            {
                if (!t.On && t.P0 - Pos <= T.SeeDist) t.On = true;
                if (t.On) t.P -= T.Speed * T.BallRoll * Tick;
                t.Frame = (byte)((int)(RunClock * 8) & 3);
            }
            double c = t.P + t.Len / 2, near = t.Len / 2 + 2.5;
            if (t.Kind == TrailKind.Wind)
            {
                if (!t.On && Pos >= t.P) { t.On = true; WindLeft = T.WindSec; WindDir = t.U; Fire("wind"); }
                continue;
            }
            if (Math.Abs(c - Pos) >= near) continue;
            if (Math.Abs(X - t.U) >= (t.W + Body) / 2) continue;
            if (t.Kind == TrailKind.Ice) { if (!air) OnIce = true; continue; }
            if (t.Pick) { Collect(ref t); continue; }
            if (t.Hit) continue;
            if (air && t.Kind != TrailKind.Snowman) continue;   // 눈사람은 못 넘는다
            if (Safe && StarLeft <= 0) continue;
            if (StarLeft > 0) { t.Gone = true; Smashes++; Fire("smash"); continue; }
            Strike(ref t);
        }
    }

    void Collect(ref TrailThing t)
    {
        t.Gone = true;
        ItemAt = Clock;
        switch (t.Kind)
        {
            case TrailKind.Berry: Berries++; Score += T.BerryScore; break;
            case TrailKind.Glass: Glasses++; Time = Math.Min(T.TimeCap, Time + T.GlassSec); break;
            default: Stars++; StarLeft = T.StarSec; break;
        }
        Fire("item");
    }

    void Strike(ref TrailThing t)
    {
        t.Hit = true;
        if (t.Kind == TrailKind.Ball) t.Gone = true;   // 눈덩이는 부서진다
        Hits++;
        (HurtAt, LastHit) = (Clock, t.Kind);
        double halt = t.Kind switch { TrailKind.Hole => T.HoleHalt, TrailKind.Snowman => T.SnowmanHalt, TrailKind.Ball => T.BallHalt, _ => 0 };
        if (halt > 0) { HaltLeft = halt; Speed = 0; JumpT = -1; Vx = 0; InvLeft = halt + T.SafeAfter; }
        else { SlowLeft = T.PileSlowSec; InvLeft = 1.5; }
        Fire("hit");
    }

    void Gate_()
    {
        if (State != Phase.Run || Leg >= Gates.Length || Pos < Gates[Leg]) return;
        Score += T.GateScore + (int)Time * 10;
        Time = Math.Min(T.TimeCap, Time + GateBonus);
        Leg++;
        GateAt = Clock;
        Fire("gate");
        if (Leg >= Gates.Length)
        {
            Score += (int)Time * 20;
            State = Phase.Win;
            EndedFor = 0;
            Fire("win");
        }
    }

    // ── 길 만들기 ──
    internal static (double W, double Len) Dim(TrailKind k) => k switch
    {
        TrailKind.Pile => (.35, 3), TrailKind.Hole => (.4, 12), TrailKind.Snowman => (.3, 3), TrailKind.Ball => (.24, 2.5),
        TrailKind.Ice => (.6, 30), TrailKind.Berry => (.16, 3), TrailKind.Glass => (.16, 3), TrailKind.Star => (.2, 3), _ => (0, 0),
    };

    static int Unlock(TrailKind k) => k switch { TrailKind.Snowman => 2, TrailKind.Ball => 3, TrailKind.Ice => 4, TrailKind.Wind => 5, _ => 1 };
    static readonly double[] Weight = { 3, 2.2, 2, 1.8, 1.2 };

    // 이 묶음에 속한 것들 (길 위의 것은 자리 순으로 정렬돼 있어 자리 범위로 찾는다. 눈덩이는 처음 자리가 만나는 곳 + BallLead)
    public IEnumerable<TrailThing> Members(TrailBundle b)
    {
        double off = b.Kind == TrailKind.Ball ? T.BallLead : 0;
        return Things.Where(t => t.Kind == b.Kind && t.P0 >= b.Start + off - 1e-6 && t.P0 <= b.End + off + 1e-6);
    }

    public double GapHalf => Math.Max(T.GapMin, Body + T.GapBody) / 2;
    public double BundleGap(int leg) => Math.Max(T.BundleGapMin, T.BundleGap1 - T.BundleGapStep * (Stage - 1)) * (1 - T.BundleGapLeg * leg);

    void Build()
    {
        var rng = new Random(Seed * 7919 + 13);
        var list = new List<TrailThing>(600);
        var seen = new HashSet<TrailKind>();
        double gapHalf = GapHalf, prevG = rng.Next(2) == 0 ? .55 : -.55, prevEnd = 0;
        TrailKind prev = TrailKind.Pile; int repeat = 0;
        for (int leg = 0; leg < T.Legs; leg++)
        {
            double end = (leg + 1) * T.LegLen - T.GateClear;
            double p = leg * T.LegLen + (leg == 0 ? T.FirstClear : T.GateClear);   // 다음 묶음 앞의 빈 자리가 끝나는 곳
            double timeGap = BundleGap(leg);
            while (true)
            {
                // 종류: 열린 것 가운데 무게대로, 같은 것이 셋 연달아 나오지 않게
                var kind = PickKind(rng, prev, repeat);
                bool fresh = !seen.Contains(kind);
                double lead = (fresh ? T.NewKindClear : 0) + (kind == TrailKind.Ball ? T.BallPad : 0);
                double bStart = p + lead, bEnd = bStart + (kind == TrailKind.Ice ? 30 : kind == TrailKind.Hole ? 16 : 6);
                if (bEnd + (fresh ? T.NewKindClear : 0) > end) break;
                seen.Add(kind);
                repeat = kind == prev ? repeat + 1 : 1;
                prev = kind;
                // 틈 가운데: 앞 묶음 틈에서 질주 빠르기로도 옮길 수 있는 거리 안 (처음 보이는 종류는 가운데에 혼자 서고 틈은 한쪽)
                double dist = Math.Max(10, bStart - prevEnd), reach = Math.Max(0, dist / T.Dash - .3) * T.Steer * .92;
                double g = fresh ? (rng.Next(2) == 0 ? .5 : -.5) : Math.Clamp(prevG + (rng.NextDouble() * 2 - 1) * Math.Min(1.1, reach), -.72, .72);
                int first = list.Count;
                Place(rng, list, kind, g, gapHalf, fresh ? 1 : PerBundle(rng, leg), bStart, bEnd, fresh);
                if (list.Count == first) { p = bEnd; continue; }
                Bundles.Add(new TrailBundle { Kind = kind, Start = bStart, End = bEnd, Gap = g, GapHalf = gapHalf, Fresh = fresh });
                prevG = g;
                prevEnd = bEnd + (kind == TrailKind.Ball ? T.BallPad : 0) + (fresh ? T.NewKindClear : 0);
                p = prevEnd + timeGap * T.Speed * (1 + rng.NextDouble() * .3);
            }
        }
        // 열매: 묶음 사이를 틈에서 틈으로 이어 주는 길 (세 알) · 모래시계·별은 조금 돌아가는 자리
        for (int i = 0; i + 1 < Bundles.Count; i++)
        {
            var (a, b) = (Bundles[i], Bundles[i + 1]);
            int leg = LegOf(a.End);
            if (leg != LegOf(b.Start)) continue;
            double from = a.End + (a.Kind == TrailKind.Ball ? T.BallPad : 0) + (a.Fresh ? T.NewKindClear : 0);
            double to = b.Start - (b.Fresh ? T.NewKindClear : 0) - (b.Kind == TrailKind.Ball ? T.BallPad : 0);
            if (to - from < 30) continue;
            for (int j = 1; j <= 3; j++)
            {
                double u = j / 4.0;
                Add(list, TrailKind.Berry, a.Gap + (b.Gap - a.Gap) * u, from + (to - from) * u);
            }
            if (rng.NextDouble() < (2.5 + (leg >= 2 ? .5 : 0)) / 17)
            {
                double side = rng.Next(2) == 0 ? -1 : 1, mid = (a.Gap + b.Gap) / 2, lat = Math.Clamp(mid + side * .55, -.85, .85);
                if (Math.Abs(lat - mid) < .3) lat = Math.Clamp(mid - side * .55, -.85, .85);
                Add(list, leg >= 2 && rng.NextDouble() < .3 ? TrailKind.Star : TrailKind.Glass, lat, (from + to) / 2 + 8);
            }
        }
        // 옆바람: 5단계부터 구간마다 한 번, 예고 눈발이 먼저 (묶음이 없는 자리에)
        if (Stage >= 5)
            for (int leg = 0; leg < T.Legs; leg++)
                for (int tries = 0; tries < 30; tries++)
                {
                    double z = leg * T.LegLen + 250 + rng.NextDouble() * (T.LegLen - 500);
                    if (Bundles.Any(b => z > b.Start - 160 && z < b.End + 90)) continue;
                    list.Add(new TrailThing { Kind = TrailKind.Wind, U = rng.Next(2) == 0 ? -1 : 1, P = z, P0 = z });
                    break;
                }
        Things = list.OrderBy(t => t.P0).ToArray();
        // 굽이: 220 걸음마다 새 값, 쉼터 가까이에서는 곧게
        int knots = (int)(T.LegLen * T.Legs / 220) + 3;
        knotAt = new double[knots]; knotV = new double[knots];
        var cr = new Random(Seed * 104729 + 7);
        for (int i = 0; i < knots; i++) (knotAt[i], knotV[i]) = (i * 220.0, i < 1 ? 0 : (cr.NextDouble() * 2 - 1) * .55);
    }

    int LegOf(double p) => Math.Min(T.Legs - 1, (int)(p / T.LegLen));

    TrailKind PickKind(Random rng, TrailKind prev, int repeat)
    {
        double total = 0;
        for (int k = 0; k < Weight.Length; k++) if (Unlock((TrailKind)k) <= Stage && !(repeat >= 2 && (TrailKind)k == prev)) total += Weight[k];
        double r = rng.NextDouble() * total;
        for (int k = 0; k < Weight.Length; k++)
        {
            if (Unlock((TrailKind)k) > Stage || repeat >= 2 && (TrailKind)k == prev) continue;
            if ((r -= Weight[k]) < 0) return (TrailKind)k;
        }
        return TrailKind.Pile;
    }

    // 한 묶음에 몇 개: 1단계 1~2, 단계·구간이 오를수록 둘·셋이 는다
    int PerBundle(Random rng, int leg)
    {
        int n = 1;
        double more = T.MoreBase + T.MoreStage * Math.Min(Stage - 1, 9) + T.MoreLeg * leg;
        while (n < 4 && rng.NextDouble() < more) { n++; more *= T.MoreDecay; }
        return n;
    }

    static void Add(List<TrailThing> list, TrailKind k, double u, double p, double len = -1)
    {
        var (w, l) = Dim(k);
        list.Add(new TrailThing { Kind = k, U = (float)u, W = (float)w, Len = (float)(len > 0 ? len : l), P = p, P0 = p });
    }

    // 틈 양옆에 장애물: 틈 가장자리에서 바깥으로 차례로 (왼쪽·오른쪽 무작위). 굴러오는 눈덩이는 만나는 곳 + BallLead 에 선다
    void Place(Random rng, List<TrailThing> list, TrailKind kind, double g, double gapHalf, int n, double p0, double p1, bool fresh)
    {
        var (w, len) = Dim(kind);
        if (kind == TrailKind.Ball) (p0, p1) = (p0 + T.BallLead, p1 + T.BallLead);
        if (fresh) { PutOne(rng, list, kind, 0, p0, p1, len); return; }
        if (kind == TrailKind.Ice) { PutOne(rng, list, kind, Math.Clamp(g + (rng.Next(2) == 0 ? -1 : 1) * (gapHalf + .1), -.75, .75), p0, p1, len); return; }
        double left = g - gapHalf, right = g + gapHalf;
        for (int i = 0; i < n; i++)
        {
            bool l = rng.Next(2) == 0;
            bool okL = left - w >= -1.06, okR = right + w <= 1.06;
            if (l && !okL) l = false; else if (!l && !okR) l = true;
            if (l ? !okL : !okR) break;
            if (l) { PutOne(rng, list, kind, left - w / 2, p0, p1, len); left -= w + .05; }
            else { PutOne(rng, list, kind, right + w / 2, p0, p1, len); right += w + .05; }
        }
    }

    static void PutOne(Random rng, List<TrailThing> list, TrailKind kind, double u, double p0, double p1, double len)
    {
        double hl = kind == TrailKind.Hole ? 10 + rng.NextDouble() * 6 : len;
        double p = kind == TrailKind.Ball ? p0 + rng.NextDouble() * 4 : p0 + rng.NextDouble() * Math.Max(0, p1 - p0 - hl);
        Add(list, kind, u, p, hl);
    }

    // 굽이: 매끄럽게 이어지는 값, 쉼터 가까이는 곧게 (그림만 — 볼록이는 안 밀린다)
    public double CurveAt(double pos)
    {
        int i = Math.Clamp((int)(pos / 220), 0, knotAt.Length - 2);
        double u = Math.Clamp((pos - knotAt[i]) / 220, 0, 1), s = u * u * (3 - 2 * u);
        double v = knotV[i] + (knotV[i + 1] - knotV[i]) * s;
        double near = double.MaxValue;
        foreach (double gt in Gates) near = Math.Min(near, Math.Abs(pos - gt));
        return v * Math.Clamp((near - 40) / 160, 0, 1) * Math.Clamp(pos / 160, 0, 1);
    }

    // 디버그: 다음 쉼터 before 걸음 앞으로 · 2초 앞에 그 종류 하나 (눈덩이는 바로 구른다)
    public void SkipToGate(double before) { if (Leg < Gates.Length) Pos = Math.Max(Pos, Gates[Leg] - before); }

    public void Put(TrailKind k)
    {
        var list = new List<TrailThing>(Things);
        double p = Pos + (k == TrailKind.Ball ? 150 : T.Speed * 2.2);
        Add(list, k, 0, p);
        if (k == TrailKind.Ball) { var b = list[^1]; b.On = true; list[^1] = b; }
        Things = list.OrderBy(t => t.P0).ToArray();
        lo = 0;
    }

    // 지금 볼록이 앞 D 걸음 (그림이 쓴다): 길 위의 것의 자리
    public double Ahead(in TrailThing t) => t.P + (t.Kind == TrailKind.Ball ? 0 : t.Len / 2) - Pos;
    public int Lo => lo;
}
