namespace Bolok;

// 버그 떼 타격감 손잡이 (docs/swarm-feel.md 6장). 맛은 그리는 쪽만 건드린다: 규칙은 죽음 기록(Swarm.Deaths)에 적기만 하고, 같은 씨는 맛을 켜든 끄든 같은 판이다
static class Feel
{
    public static bool On = true;                 // 끄면 옛 모습 (맞으면 걸러 안 그리기, 죽으면 그냥 사라짐, 멈춤·흔들림·팡 소리 없음)
    public static double FlashSec = .05;          // 맞은 벌레가 흰 판으로 보이는 시간
    public static double NudgeSec = .06;          // 맞은 쪽 반대로 한 칸 밀려 그리는 시간
    public static double ChainGap = .025, ChainMax = .2;   // 연쇄: 3마리 넘게 죽으면 가까운 순서로 이만큼씩 늦춘다, 다 터지는 데 걸리는 가장 긴 시간
    public static double StopElite = 0;           // 엘리트·강철 처치 멈춤 (0 = 끔, 해 보고 정한다: 30ms)
    public static double StopBoss = .05, StopCool = 1.5, StopTotal = 1.0, StopSlow = .1;   // 보스 처치 멈춤 (0 = 끔): 보스 때만, 1.5초에 한 번, 한 판 합쳐 1초까지, 그 동안 시간은 0.1배로
    public static double Shake = 1, ShakeSec = .12;        // 흔들림 (칸 수, 0 = 끔): 판 안 그림만, 보스 처치·큰 폭발(6마리 넘게)에서
    public static int ShakeBoom = 7;               // 폭발이 이만큼 이상 잡으면 흔들린다
    public static double PopSoundGap = .045, FireGuard = .04, BoomGuard = .15;   // 팡 소리는 45ms 에 한 번, 팡 뒤 40ms 는 쏘는 소리가 못 자르고, 쾅 뒤 150ms 는 팡이 못 자른다
    public const int MaxBits = 256, MaxPops = 64;  // 조각·터짐 그림 상한 (미리 잡은 배열: 넘치면 가장 오래된 것을 덮는다)
}

// 버그 떼의 맛: 맞음(흰 판·한 칸 밀림) · 죽음(찌그러짐 → 팡/톡) · 연쇄 · 보석 튐 · 보스 멈춤·흔들림 · 팡 소리
sealed partial class SwarmGame
{
    // 터짐 하나 (죽은 벌레 하나): 자리는 화면 좌표. Due 까지는 찌그러진 흰 판으로 기다린다 (연쇄)
    struct FPop
    {
        public bool Live, Big, Spawned, Sounded, Chain, Dirt, Elite;
        public int X, Y;
        public FeelArt.Row Row;
        public double Due, Share, Dx, Dy;
    }

    // 조각 하나 (칩 또는 껍질 조각): 칩 칸(작은 배율 한 칸) 단위 포물선. 높이(Z)는 위에서 본 판에서 떠오른 만큼
    struct FBit
    {
        public bool Live, Stick;
        public int Cx, Cy, W, H, Ci, Sx, Sy, Ext;   // 터진 자리(화면), 칩 크기·색, 지금 그리는 자리(화면)와 반쯤 크기(px)
        public byte Shape;                          // 0 네모, 1 세모 (2×2 에서 한 칸 빠짐)
        public double Born, T0, X0, Y0, Z0, Vx, Vy, Vz, Drag;
        public FeelArt.RowArt? Art;
        public FeelArt.Piece? Piece;
    }

    FeelArt? feelArt;
    readonly FPop[] fpops = new FPop[Feel.MaxPops];
    readonly FBit[] fbits = new FBit[Feel.MaxBits];
    int fpopNext, fbitNext;
    readonly int[] chainOrder = new int[Swarm.MaxDeaths];
    readonly double[] chainDist = new double[Swarm.MaxDeaths];
    Random fxRng = new(20261126);                   // 그림 전용 Random: 규칙의 rng 를 안 쓴다 (같은 씨 = 같은 판)
    double stopLeft, lastStopAt = -9, stopSpent, shakeUntil = -9;   // 멈춤: 남은 (실제) 초, 지난 멈춤, 한 판 합친 멈춤, 흔들림이 끝나는 때
    bool shakeWas;                                  // 지난 프레임이 흔들림이었다 (끝난 뒤 한 프레임은 통째로 다시 칠한다)
    double lastPopAt = -9, lastHighAt = -9;         // 팡 소리를 낸 때, 쾅·다침·레벨 소리를 낸 때 (소리 우선순위)
    int popRun;                                     // 연쇄 소리 차례 (0 → 5)
    internal int PopSounds;                         // 낸 팡 소리 수 (검사용)
    internal int PopRequests;                       // 이번 프레임까지 요청한 수 (검사용)
    internal Action<int, bool>? PopSink;            // 팡 소리를 내는 곳 (검사가 대신 받는다)
    internal Action<double>? DtTap;                 // 규칙에 넘긴 시간 (검사: 같은 시간 열로 맛 없이 다시 돌려 본다)
    internal int StopFrames;                        // 멈춘 프레임 수 (검사용)
    internal double StopSeconds => stopSpent;
    internal int LivePops { get { int n = 0; foreach (var p in fpops) if (p.Live) n++; return n; } }
    internal int LiveBits { get { int n = 0; foreach (var b in fbits) if (b.Live) n++; return n; } }

    // 새 판: 조각·터짐·멈춤·흔들림·소리 상태를 비운다
    void FeelReset()
    {
        Array.Clear(fpops);
        Array.Clear(fbits);
        (fpopNext, fbitNext, stopLeft, lastStopAt, stopSpent, shakeUntil, shakeWas) = (0, 0, 0, -9, 0, -9, false);
        (lastPopAt, lastHighAt, popRun, PopSounds, PopRequests, StopFrames) = (-9, -9, 0, 0, 0, 0);
        fxRng = new Random(20261126);
    }

    // 맞은 벌레를 흰 판으로 그릴 때인가 (맛이 켜져 있고 번쩍이는 동안)
    static bool Flashing(Swarm s, Swarm.Foe f) => Feel.On && s.Clock - f.Hit < Feel.FlashSec;
    static bool Nudging(Swarm s, Swarm.Foe f) => Feel.On && s.Clock - f.Hit < Feel.NudgeSec;

    FeelArt.Row RowOf(Swarm.Foe f) => FeelArt.RowOf(f.Kind, f.Tier, f.Elite, f.Heavy);

    // 흰 판: 그 줄(벌레 그림)의 같은 장
    Stamp FlashArt(Swarm s, Swarm.Foe f)
    {
        var row = feelArt!.Arts[(int)RowOf(f)];
        var src = f.Tier >= Swarm.Bosses ? queen : f.Heavy ? (f.Elite ? eliteSteel : steel) : f.Elite ? elite[(int)f.Kind] : foe[(int)f.Kind];
        return row.Flash[((int)(s.Clock * (f.Kind == Swarm.Kind.Wasp ? 12 : 5)) + (int)f.Speed) % src.Length];
    }

    // 벌레를 그릴 자리와 그림: 맞았으면 흰 판 + 맞은 쪽 반대로 한 칸
    void DrawFoe(Overlay o, Graphics g, Swarm s, Swarm.Foe f, int fx, int small)
    {
        int y = (int)f.Y + RiseY(s, f);
        if (!Feel.On)
        {
            if (!Fx.Flash(s.Clock, f.Hit)) FoeArt(s, f).Put(o, g, fx, y);   // 옛 모습: 맞으면 걸러 안 그린다
            return;
        }
        if (Nudging(s, f)) (fx, y) = (fx + f.NudgeX * small, y + f.NudgeY * small);
        (Flashing(s, f) ? FlashArt(s, f) : FoeArt(s, f)).Put(o, g, fx, y);
    }

    // ── 보석이 톡 튄다: 죽은 자리에서 0.2초 포물선으로 X·Y 에 떨어진다 (그림만. 규칙의 자리는 처음부터 X·Y) ──
    (double X, double Y) GemAt(Swarm s, Swarm.Gem m)
    {
        double x = m.X, y = ShowY(s, m);
        if (!Feel.On || m.Item != Swarm.Item.Gem) return (x, y);
        double age = s.Clock - m.BornAt;
        if (age < 0 || age >= Swarm.GemHopSec) return (x, y);
        int small = GameHud.Small(s.K);
        double u = age / Swarm.GemHopSec, rest = 1 - u, peak = (m.HopX != 0 || m.HopY != 0 ? 6 : 4.5) * small;
        return (x + m.HopX * rest, y + m.HopY * rest - 4 * peak * u * rest);
    }

    // ── 한 프레임: 새 죽음 → 터짐·조각, 멈춤·흔들림 신호, 소리 ──
    void FeelTrack(Swarm s)
    {
        if (feelArt == null) return;
        double now = s.Clock;
        int small = GameHud.Small(s.K);
        for (int i = 0; i < fpops.Length; i++)
            if (fpops[i].Live && now - fpops[i].Due > feelArt.Arts[(int)fpops[i].Row].Spawn + .32) fpops[i].Live = false;
        int n = s.Deaths.Count;
        if (Feel.On && n > 0) NewDeaths(s, n, now);
        s.Deaths.Trim(0);   // 읽었다 (맛을 끄면 그냥 비운다)
        if (!Feel.On) { for (int i = 0; i < fpops.Length; i++) fpops[i].Live = false; for (int i = 0; i < fbits.Length; i++) fbits[i].Live = false; }
        bool sound = false, soundBig = false, soundChain = false;
        for (int i = 0; i < fpops.Length; i++)
        {
            ref var p = ref fpops[i];
            if (!p.Live) continue;
            var ra = feelArt.Arts[(int)p.Row];
            if (!p.Spawned && now >= p.Due + ra.Spawn) { p.Spawned = true; SpawnBits(ref p, ra, s, small); }
            if (!p.Sounded && now >= p.Due) { p.Sounded = true; sound = true; soundBig |= p.Big || p.Elite; soundChain |= p.Chain; }
        }
        if (sound) PopSound(s, soundChain, soundBig);
        for (int i = 0; i < fbits.Length; i++)
        {
            ref var b = ref fbits[i];
            if (!b.Live) continue;
            double age = now - b.Born;
            if (age + b.T0 >= .64) { b.Live = false; continue; }
            if (age < 0) { b.Sx = int.MinValue; continue; }
            Ballistic(in b, age, out double x, out double y, out double z, out _);
            (b.Sx, b.Sy) = (b.Cx + (int)Math.Round(x) * small, b.Cy + (int)Math.Round(y - z) * small);
        }
    }

    void NewDeaths(Swarm s, int n, double now)
    {
        bool chain = n > 3;
        double ox = 0, oy = 0;
        for (int i = 0; i < n; i++) { ox += s.Deaths[i].Ox; oy += s.Deaths[i].Oy; }
        (ox, oy) = (ox / n, oy / n);
        for (int i = 0; i < n; i++)   // 친 곳에서 가까운 순서 (끼워 넣기 정렬: 목록을 새로 안 만든다)
        {
            double dx = s.Deaths[i].X - ox, dy = s.Deaths[i].Y - oy, d = dx * dx + dy * dy;
            int j = i - 1;
            for (; j >= 0 && chainDist[j] > d; j--) (chainOrder[j + 1], chainDist[j + 1]) = (chainOrder[j], chainDist[j]);
            (chainOrder[j + 1], chainDist[j + 1]) = (i, d);
        }
        double share = n <= 4 ? 1 : n <= 12 ? .5 : .25;   // 한 프레임에 많이 죽으면 한 마리 조각을 줄인다 (조각 상한 안에서)
        int booms = 0;
        for (int r = 0; r < n; r++)
        {
            ref var d = ref s.Deaths[chain ? chainOrder[r] : r];
            ref var p = ref fpops[fpopNext];
            fpopNext = (fpopNext + 1) % fpops.Length;
            p = new FPop
            {
                Live = true, Chain = chain, Dirt = d.Dirt, Elite = d.Elite || d.Heavy, Big = chain && r == n - 1,   // 연쇄의 마지막은 한 칸 크게
                X = (int)d.X, Y = (int)d.Y, Row = FeelArt.RowOf(d.Kind, d.Tier, d.Elite, d.Heavy),
                Due = chain ? now + Math.Min(Feel.ChainMax, r * Feel.ChainGap) : now, Share = share, Dx = d.Dx, Dy = d.Dy,
            };
            if (d.Boom) booms++;
            if (d.Kind == Swarm.Kind.Boss && !d.Win) BossDown(now, Feel.StopBoss);
            else if ((d.Elite || d.Heavy) && !d.Win && Feel.StopElite > 0) BossDown(now, Feel.StopElite);
        }
        if (booms >= Feel.ShakeBoom && Feel.Shake > 0) shakeUntil = Math.Max(shakeUntil, now + Feel.ShakeSec);
    }

    // 보스를 잡았다: 아주 짧은 멈춤(보스만, 1.5초에 한 번, 한 판 합쳐 1초까지)과 흔들림
    void BossDown(double now, double sec)
    {
        if (sec > 0 && now - lastStopAt >= Feel.StopCool && stopSpent + sec <= Feel.StopTotal)
            (stopLeft, lastStopAt, stopSpent) = (sec, now, stopSpent + sec);
        if (Feel.Shake > 0) shakeUntil = Math.Max(shakeUntil, now + Feel.ShakeSec);
    }

    // ── 조각 만들기 (그림 전용 Random) ──
    ref FBit NewBit()
    {
        ref var b = ref fbits[fbitNext];
        fbitNext = (fbitNext + 1) % fbits.Length;
        b = default;
        b.Live = true;
        b.Sx = int.MinValue;
        return ref b;
    }

    void Aim(ref FBit b, ref FPop p, double ang, double speed, double vz, double r0)
    {
        double cx = Math.Cos(ang), cy = Math.Sin(ang) * .7;
        (b.X0, b.Y0, b.Z0) = (cx * r0, cy * r0 * .8, 3);
        (b.Vx, b.Vy, b.Vz) = (cx * speed + p.Dx * 12, cy * speed + p.Dy * 8, vz);
    }

    void SpawnBits(ref FPop p, FeelArt.RowArt ra, Swarm s, int small)
    {
        double now = s.Clock, born = p.Due + ra.Spawn, R = ra.Half * (p.Big ? 1.25 : 1);
        var rnd = fxRng;
        if (!ra.Tok)   // 팡: 몸 색 네모 조각이 통통 튀어 떨어진다
        {
            int n = Math.Max(2, (int)Math.Round((p.Row == FeelArt.Row.Wasp ? 9 : 12) * p.Share));
            for (int i = 0; i < n; i++)
            {
                double roll = rnd.NextDouble(), ang = (i + rnd.NextDouble() * .8) / n * Math.PI * 2, sp = (74 + rnd.NextDouble() * 56) * Math.Min(1.3, .8 + R / 30), vz = 80 + rnd.NextDouble() * 34;
                int ci = i == 0 ? 2 : (rnd.NextDouble() < .6 ? 0 : 1);
                if (p.Dirt) ci = i == 0 ? 6 : (rnd.NextDouble() < .6 ? 5 : 6);
                ref var b = ref NewBit();
                (b.Cx, b.Cy, b.Born, b.T0, b.Ci, b.Art) = (p.X, p.Y, born, ra.Spawn, ci, ra);
                if (i == 0) (b.W, b.H) = (1, 1);
                else if (roll < .4) (b.W, b.H) = (2, 2);
                else if (roll < .8) (b.W, b.H) = (3, 2);
                else if (roll > .9) (b.W, b.H) = (3, 3);
                else (b.W, b.H, b.Shape) = (2, 2, 1);
                Aim(ref b, ref p, ang, sp, vz, R * .5);
                b.Drag = 3.2;
                b.Ext = Math.Max(b.W, b.H) * small + small;
            }
            return;
        }
        // 톡: 벌레 그림이 제 껍질 조각으로 갈라져 날아가고 한 번 뒤집힌다 (보스는 금 조각이 더)
        bool boss = p.Row is FeelArt.Row.Boss or FeelArt.Row.Queen;
        var pieces = ra.Shells[rnd.Next(ra.Shells.Length)];
        int take = Math.Max(Math.Min(3, pieces.Length), (int)Math.Ceiling(pieces.Length * p.Share));
        for (int i = 0; i < take; i++)
        {
            var pc = pieces[i];
            double sp = ((boss ? 22 : 32) + rnd.NextDouble() * (boss ? 16 : 24)) * Math.Min(1.3, .8 + R / 36), vz = 66 + rnd.NextDouble() * 30;
            ref var b = ref NewBit();
            (b.Cx, b.Cy, b.Born, b.T0, b.Art, b.Piece) = (p.X, p.Y, born, ra.Spawn, ra, pc);
            Aim(ref b, ref p, pc.Ang, sp, vz, 0);
            (b.X0, b.Y0, b.Z0) = (pc.Ox, pc.Oy, 3);
            b.Ext = (Math.Max(pc.W, pc.H) + 1) * small;
        }
        if (boss)
            for (int i = 0; i < 6 && p.Share >= .5; i++)
            {
                ref var b = ref NewBit();
                (b.Cx, b.Cy, b.Born, b.T0, b.Art, b.Ci, b.W, b.H) = (p.X, p.Y, born, ra.Spawn, ra, i % 2 == 0 ? 3 : 4, 2, 2);
                Aim(ref b, ref p, i * 1.05 + rnd.NextDouble() * .5, 44 + rnd.NextDouble() * 22, 90 + rnd.NextDouble() * 30, R * .3);
                b.Ext = 3 * small;
            }
    }

    // 조각 하나가 나고 age 초 뒤의 자리 (칩 칸. x·y 는 바닥 자리, z 는 떠오른 높이): 땅에 닿으면 한 번 튀고(세게 닿았을 때만) 선다
    static void Ballistic(in FBit b, double age, out double x, out double y, out double z, out bool rest)
    {
        const double G = 800;
        double D = b.Drag;
        double t1 = (b.Vz + Math.Sqrt(b.Vz * b.Vz + 2 * G * b.Z0)) / G;   // 처음 땅에 닿는 때
        double Hx(double t) => D > 0 ? (1 - Math.Exp(-D * t)) / D : t;
        if (age < t1)
        {
            double h = Hx(age);
            (x, y, z, rest) = (b.X0 + b.Vx * h, b.Y0 + b.Vy * h, b.Z0 + b.Vz * age - G * age * age / 2, false);
            return;
        }
        double vz1 = b.Vz - G * t1, h1 = Hx(t1), x1 = b.X0 + b.Vx * h1, y1 = b.Y0 + b.Vy * h1;
        if (b.Stick || -vz1 < 28) { (x, y, z, rest) = (x1, y1, 0, true); return; }
        double vzB = -vz1 * .38, tau = age - t1, tauEnd = 2 * vzB / G, ek = Math.Exp(-D * t1), tt = Math.Min(tau, tauEnd);
        double h2 = D > 0 ? (1 - Math.Exp(-D * tt)) / D : tt;
        x = x1 + b.Vx * ek * .45 * h2;
        y = y1 + b.Vy * ek * .45 * h2;
        if (tau >= tauEnd) (z, rest) = (0, true);
        else (z, rest) = (vzB * tau - G * tau * tau / 2, false);
    }

    // ── 소리: 팡은 45ms 에 한 번 (한 프레임에 몇이 죽어도 한 번), 쾅·다침·레벨 뒤 150ms 는 팡이 못 자르고, 쏘는 소리는 팡 뒤 40ms 못 자른다 ──
    // 차례: 연쇄는 오를 때마다 한 칸씩 (0 → 5), 0.4초 동안 처치가 없으면 0 으로. 혼자 죽으면 0~2 무작위 (그림 전용 Random)
    void PopSound(Swarm s, bool chain, bool big)
    {
        double now = s.Clock;
        PopRequests++;
        if (now - lastPopAt < Feel.PopSoundGap || now - lastHighAt < Feel.BoomGuard) return;
        if (now - lastPopAt > .4) popRun = 0;
        int step = chain ? popRun : fxRng.Next(3);
        if (chain) popRun = Math.Min(5, popRun + 1);
        lastPopAt = now;
        PopSounds++;
        if (PopSink != null) PopSink(step, big);
        else Snd.Pop(step, big);
    }

    // 쏘는 소리를 내도 되나 (팡 직후는 안 자른다)
    bool FireAllowed(Swarm s) => !Feel.On || s.Clock - lastPopAt >= Feel.FireGuard;
    static bool HighSound(string e) => e is "boom" or "nade" or "cluster" or "bigkill" or "hurt" or "over" or "level";

    // ── 흔들림: 판 안 그림만 칸 단위로 (프레임마다 방향이 바뀐다) ──
    bool Shaking(Swarm s) => Feel.On && Feel.Shake > 0 && s.Clock < shakeUntil;
    (int X, int Y) ShakeAt(Swarm s)
    {
        if (!Shaking(s)) return (0, 0);
        int amp = (int)Math.Round(Feel.Shake * GameHud.Small(s.K));
        return ((int)((shakeUntil - s.Clock) / .03) & 3) switch { 0 => (amp, 0), 1 => (0, -amp), 2 => (-amp, 0), _ => (0, amp) };
    }

    // ── 다시 칠할 자리 ──
    void FeelDirty(Overlay o, List<Rectangle> rs, Swarm s)
    {
        if (feelArt == null) return;
        int small = GameHud.Small(s.K);
        for (int i = 0; i < fpops.Length; i++)
        {
            ref var p = ref fpops[i];
            if (!p.Live) continue;
            double pad = (feelArt.Arts[(int)p.Row].Half * 1.9 * (p.Big ? 1.25 : 1) + 5) * small;
            rs.Add(o.Client(Overlay.Around(p.X, p.Y, pad, pad)));
        }
        for (int i = 0; i < fbits.Length; i++)
        {
            ref var b = ref fbits[i];
            if (b.Live && b.Sx != int.MinValue) rs.Add(o.Client(Overlay.Around(b.Sx, b.Sy, b.Ext, b.Ext)));
        }
        bool shaking = Shaking(s);
        if (shaking || shakeWas) rs.Add(o.Client(Rectangle.Inflate(s.Field, 3 * small, 3 * small)));   // 흔들리는 프레임과 끝난 다음 한 프레임은 판을 통째로
        shakeWas = shaking;
    }

    // ── 그리기 ──
    // 찌그러진 흰 판 (죽은 직후 · 연쇄에서 차례를 기다리는 동안): 벌레들 위에
    void DrawSquash(Overlay o, Graphics g, Swarm s)
    {
        if (feelArt == null) return;
        double now = s.Clock;
        for (int i = 0; i < fpops.Length; i++)
        {
            ref var p = ref fpops[i];
            if (!p.Live) continue;
            var ra = feelArt.Arts[(int)p.Row];
            if (now - p.Due >= ra.Spawn) continue;
            (p.Big ? ra.SquashBig : ra.Squash).Put(o, g, p.X, p.Y);
        }
    }

    // 고리·별·반짝이와 조각 (탄 다음 · 불덩이 앞)
    void DrawFeel(Overlay o, Graphics g, Swarm s)
    {
        if (feelArt == null) return;
        double now = s.Clock;
        int small = GameHud.Small(s.K);
        for (int i = 0; i < fpops.Length; i++)
        {
            ref var p = ref fpops[i];
            if (!p.Live) continue;
            var ra = feelArt.Arts[(int)p.Row];
            double tau = now - p.Due - ra.Spawn;
            if (tau < 0) continue;
            int big = p.Big ? 1 : 0;
            if (!ra.Tok)
            {
                if (tau < .033) ra.Ring[big * 2].Put(o, g, p.X, p.Y);
                else if (tau < .077) ra.Ring[big * 2 + 1].Put(o, g, p.X, p.Y);
            }
            else if (tau < .037) ra.Star[big].Put(o, g, p.X, p.Y);
            if (p.Elite && ra.Tok && tau > .027 && tau < .307)   // 엘리트: 금 반짝이가 깜빡
            {
                double t = tau + ra.Spawn, R = ra.Half;
                for (int k = 0; k < 3; k++)
                {
                    if (((int)(t / .04) + k) % 3 != 0) continue;
                    double a = k * 2.1 + 1.0;
                    var c = o.ToClient(p.X + (int)Math.Round(Math.Cos(a) * R * 1.35) * small, p.Y + ((int)Math.Round(Math.Sin(a) * R) - 2) * small);
                    g.FillRectangle(ra.Main[3], c.X - small, c.Y, 3 * small, small);
                    g.FillRectangle(ra.Main[3], c.X, c.Y - small, small, 3 * small);
                    g.FillRectangle(ra.Light[3], c.X, c.Y, small, small);
                }
            }
        }
        for (int i = 0; i < fbits.Length; i++)
        {
            ref var b = ref fbits[i];
            if (!b.Live || b.Sx == int.MinValue) continue;
            double age = now - b.Born, t = age + b.T0;
            int stage = t < .4 ? 0 : t < .52 ? 1 : 2;
            if (b.Piece is { } pc)
            {
                bool flip = pc.Flips && age >= .18, narrow = pc.Flips && age >= .10 && age < .18;
                var st = narrow ? pc.Narrow : stage == 0 ? (flip ? pc.Flip : pc.Norm) : stage == 1 ? (flip ? pc.FlipChk : pc.NormChk) : (flip ? pc.FlipSp : pc.NormSp);
                st.Put(o, g, b.Sx, b.Sy);
                continue;
            }
            var c0 = o.ToClient(b.Sx - b.W * small / 2, b.Sy - b.H * small / 2);
            int gx0 = (b.Sx - b.Cx) / small - b.W / 2, gy0 = (b.Sy - b.Cy) / small - b.H / 2;   // 칩 칸 번호 (바둑판 무늬가 이어지게)
            var main = b.Art!.Main[b.Ci];
            bool hi = b.W >= 2 && b.H >= 2 && b.Shape == 0;
            if (stage == 0 && b.Shape == 0)
            {
                g.FillRectangle(main, c0.X, c0.Y, b.W * small, b.H * small);
                if (hi) g.FillRectangle(b.Art.Light[b.Ci], c0.X, c0.Y, small, small);
                continue;
            }
            for (int j = 0; j < b.H; j++)
                for (int k = 0; k < b.W; k++)
                {
                    if (b.Shape == 1 && j == 0 && k == 0) continue;
                    int gx = gx0 + k, gy = gy0 + j;
                    if (stage == 1 ? ((gx + gy) & 1) != 0 : stage == 2 && ((gx & 1) != 0 || (gy & 1) != 0)) continue;
                    g.FillRectangle(hi && j == 0 && k == 0 ? b.Art.Light[b.Ci] : main, c0.X + k * small, c0.Y + j * small, small, small);
                }
        }
    }
}
