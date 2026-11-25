namespace Bolok;

// 클라이밍 규칙 (화면 없음, docs/climb.md 4장): 덩굴 만들기·오르기·시간 막대·기믹·떨어짐·점수.
// 고정 박자 1/60초로 돈다 (Update 가 쌓아서 나눠 돌림). 조작은 Press(방향) 한 가지 → 씨와 「몇 틱에 어느 쪽」 기록이 같으면 같은 판.
// 잎 i 의 방향·종류는 씨와 i 까지의 뽑기로만 정해진다 (조작·프레임 빠르기와 무관). 잎은 고리 배열 64칸 (앞 20칸을 미리 만든다), 도는 동안 새로 만드는 것 없음.
// 방향 Dir: 잎 i 가 앞 잎보다 왼쪽(−1)·오른쪽(+1)에 있다 (옮겨 간 쪽 = 누를 키). 모양 A2 는 잎이 지그재그로 오르고(Col 이 누적 자리), 모양 A4 는 줄기 왼쪽·오른쪽에 번갈아 붙는다 (Dir 이 줄기 쪽)
enum ClimbKind : byte { Leaf, Berry, Puff, Wilt, Curl, Flower }

struct ClimbLeaf
{
    public sbyte Dir;
    public ClimbKind Kind;
    public bool Fog, Taken;      // 구름 띠 안인가, 열매·홀씨를 주웠나
    public int Col;              // 방향의 누적 (그림 자리 = Col × 칸)
}

sealed class Climb
{
    public enum Phase { Wait, Run, Fall, Over }
    public enum Cause { None, Wrong, Time, Wilt }
    public const double Tick = 1.0 / 60;
    public const int Ring = 64, Ahead = 20;

    public readonly ClimbTune T;
    public readonly int Seed;
    readonly ClimbLeaf[] ring = new ClimbLeaf[Ring];

    // 지금
    public Phase State = Phase.Wait;
    public Cause Why;
    public int N, PrevN;                 // 지금 선 잎 (= 점수), 한 칸 전 (그림이 따라올 때)
    public int LastDir = 1;              // 마지막으로 간 쪽
    public int WrongDir;                 // 틀리게 누른 쪽
    public double Bar;                   // 시간 막대 0~1
    public bool Puff;                    // 홀씨를 들고 있다
    public double Clock, RunClock, EndedFor, StepAt = -9, FallAt = -9, SaveAt = -9, LiftAt = -9, MileAt = -9, BerryAt = -9, WrongAt = -9;
    public double SaveLeft, LiftLeft, GraceLeft, WiltLeft = -1;
    public int Ticks;                    // 지난 규칙 틱 수 (조작 기록의 때)
    public int Steps, Berries, Saves, Lifts, Wrongs, Presses;
    public int LastPress, PressTick;     // 마지막으로 받은 키(−1·+1)와 그때의 틱 (조작 기록을 뽑는 데 쓴다)
    public int Best;                     // 지난 최고 (넘는 순간 best 사건). 0 이면 없다
    public int Friends;                  // 지난 100잎 수 (한 판에서 만난 친구)
    public Action<string>? OnEvent;
    double acc;
    bool bestFired;
    int lastZone;

    // 만들기 상태
    readonly Random rd, rg, rc;
    int genTo = -1, runDir, runLeft, zig, col, longestZig, curZig;
    int nextBerry, nextPuff, nextWilt, nextCurl, lastG = -99, quietUntil = -1, bandLeft, bandNext;

    public bool Done => State is Phase.Fall or Phase.Over;
    public double Pace(int n) => Math.Min(T.PaceMax, T.PaceBase + n / T.PaceSlope);
    public double Drain(int n) => T.Gain * Pace(n);
    public double SinceFall => Clock - FallAt;
    public int Zone(int leaf) => Math.Min(5, leaf / T.Zone);
    public bool Standing => State is Phase.Wait or Phase.Run;

    // start: 몇 잎에서 시작하나 (연습 판). 0 이 보통
    public Climb(int seed, ClimbTune? tune = null, int start = 0)
    {
        T = tune ?? ClimbTune.Now;
        Seed = seed;
        rd = new Random(seed); rg = new Random(seed * 31 + 7); rc = new Random(seed * 17 + 3);
        nextBerry = T.BerryFrom + rg.Next(0, T.BerryGapHi - T.BerryGapLo + 1);
        nextPuff = T.PuffFrom + rg.Next(0, 21);
        nextWilt = T.WiltFrom + rg.Next(0, T.WiltGapHi - T.WiltGapLo + 1);
        nextCurl = T.CurlFrom + rg.Next(0, 31);
        bandNext = T.CloudFrom + rc.Next(0, 6);
        Bar = T.StartBar;
        Gen(0); Gen(1);
        ring[0].Dir = ring[1].Dir;       // 첫 잎은 다음 잎 쪽으로 향한다
        N = PrevN = Math.Max(0, start);
        Ensure(N + Ahead);
        lastZone = N / T.Zone;
    }

    // ── 덩굴 만들기 ──
    public ref ClimbLeaf At(int i)
    {
        if (i > genTo) Ensure(i);
        return ref ring[i & (Ring - 1)];
    }

    void Ensure(int upTo) { while (genTo < upTo) Gen(genTo + 1); }

    // 한 줄(같은 쪽으로 L 칸) 길이 뽑기: 높이에 따라 L=1 비율과 짧은 쪽으로 기우는 정도가 달라진다
    int DrawL(int i)
    {
        double p1 = i < 30 ? 0 : i < 150 ? T.OneLo * (i - 30) / 120.0 : i < 300 ? T.OneLo + (T.OneMid - T.OneLo) * (i - 150) / 150.0 : T.OneHi;
        bool easy = i < T.EasyLeaves || zig >= T.MaxZig;
        if (!easy && rd.NextDouble() < p1) return 1;
        int max = i < 150 ? 5 : T.MaxRun;
        double r = i < 30 ? 1 : i < 150 ? .9 - .3 * (i - 30) / 120.0 : i < 300 ? .6 - .25 * (i - 150) / 150.0 : .35;   // 2~max 의 무게: 길수록 r 배씩
        double sum = 0, w = 1;
        for (int l = 2; l <= max; l++, w *= r) sum += w;
        double x = rd.NextDouble() * sum;
        w = 1;
        for (int l = 2; l <= max; l++, w *= r) { x -= w; if (x < 0) return l; }
        return max;
    }

    static bool MileWithin(int a, int b, int mile) => b / mile * mile >= Math.Max(a, mile);

    void Gen(int i)
    {
        genTo = i;
        ref var lf = ref ring[i & (Ring - 1)];
        lf = default;
        if (i == 0) return;
        if (runLeft == 0)
        {
            runDir = i == 1 ? (rd.Next(2) == 0 ? -1 : 1) : -runDir;
            int l = DrawL(i);
            runLeft = l;
            zig = l == 1 ? zig + 1 : 0;
            if (l == 1) { if (++curZig > longestZig) longestZig = curZig; } else curZig = 0;
        }
        runLeft--;
        col += runDir;
        (lf.Dir, lf.Col) = ((sbyte)runDir, col);
        if (i % T.Mile == 0) { lf.Kind = ClimbKind.Flower; lastG = i; }
        else if (T.Gimmicks && i > quietUntil && i - lastG >= 3 && !MileWithin(i - 2, i + 2, T.Mile))
        {
            if (i >= T.CurlFrom && i >= nextCurl && !MileWithin(i, i + T.CurlUp + 3, T.Mile) && i % T.Zone >= T.CurlQuiet)
            {
                lf.Kind = ClimbKind.Curl; nextCurl = i + rg.Next(T.CurlGapLo, T.CurlGapHi + 1); quietUntil = i + T.CurlUp + 3;   // 던져 올려 내려놓는 잎과 그 뒤 3잎은 보통 잎
            }
            else if (i >= T.PuffFrom && i >= nextPuff) { lf.Kind = ClimbKind.Puff; nextPuff = i + rg.Next(T.PuffGapLo, T.PuffGapHi + 1); }
            else if (i >= T.WiltFrom && i >= nextWilt) { lf.Kind = ClimbKind.Wilt; nextWilt = i + rg.Next(T.WiltGapLo, T.WiltGapHi + 1); }
            else if (i >= T.BerryFrom && i >= nextBerry) { lf.Kind = ClimbKind.Berry; nextBerry = i + rg.Next(T.BerryGapLo, T.BerryGapHi + 1); }
            if (lf.Kind != ClimbKind.Leaf) lastG = i;
        }
        if (T.Gimmicks && i >= T.CloudFrom)   // 구름 띠: 8~12잎, 구름 구역(200~299)에서는 잦고 그 뒤에는 가끔
        {
            if (bandLeft > 0 || i >= bandNext)
            {
                if (bandLeft == 0) bandLeft = rc.Next(T.CloudLenLo, T.CloudLenHi + 1);
                lf.Fog = true;
                if (--bandLeft == 0) bandNext = i + 1 + (i < T.CloudFrom + 100 ? rc.Next(T.CloudGapLo, T.CloudGapHi + 1) : rc.Next(T.CloudLateLo, T.CloudLateHi + 1));
            }
        }
    }

    public int LongestZig => longestZig;

    // 구름에 가린 잎인가 (지금 잎에서 CloudHide 칸 이상 위)
    public bool Hidden(int i) => i - N >= T.CloudHide && At(i).Fog;

    // ── 조작 (키를 누른 프레임에 Update 앞에) ──
    // dir: −1 왼쪽, +1 오른쪽. 멈춤(홀씨·덩굴손)이나 끝난 판에서는 먹지 않는다
    public void Press(int dir)
    {
        if (Done || dir == 0) return;
        if (SaveLeft > 0 || LiftLeft > 0) return;
        if (State == Phase.Wait) { State = Phase.Run; Fire("go"); }
        Presses++;
        (LastPress, PressTick) = (dir, Ticks);
        Ensure(N + Ahead);
        if (At(N + 1).Dir != dir)
        {
            WrongDir = dir; WrongAt = Clock; Wrongs++;
            if (Puff) { Puff = false; SaveLeft = T.SaveSec; SaveAt = Clock; Saves++; Fire("save"); }
            else Drop(Cause.Wrong);
            return;
        }
        Climb1(dir);
    }

    void Climb1(int dir)
    {
        PrevN = N;
        N++;
        Steps++;
        LastDir = dir;
        StepAt = Clock;
        WiltLeft = -1;
        Bar = Math.Min(1, Bar + T.Gain);
        Ensure(N + Ahead);
        Fire("hop");
        ref var lf = ref At(N);
        switch (lf.Kind)
        {
            case ClimbKind.Berry when !lf.Taken: lf.Taken = true; Bar = Math.Min(1, Bar + T.BerryBar); Berries++; BerryAt = Clock; Fire("berry"); break;
            case ClimbKind.Puff when !lf.Taken && !Puff: lf.Taken = true; Puff = true; Fire("puff"); break;
            case ClimbKind.Wilt: WiltLeft = T.WiltSec; Fire("wilt"); break;
            case ClimbKind.Curl:
                N += T.CurlUp; Steps += T.CurlUp; Lifts++;
                LiftLeft = T.LiftSec; LiftAt = Clock; StepAt = Clock;
                Ensure(N + Ahead);
                Fire("curl");
                break;
            case ClimbKind.Flower: Bar = 1; MileAt = Clock; Fire("mile"); break;
        }
        if (Best > 0 && !bestFired && N > Best) { bestFired = true; Fire("best"); }
        if (N / T.Zone > lastZone) { lastZone = N / T.Zone; Friends = lastZone; MileAt = Clock; Fire("zone"); }
    }

    void Drop(Cause why)
    {
        State = Phase.Fall; Why = why; FallAt = Clock; EndedFor = 0; WiltLeft = -1;
        Fire(why == Cause.Wrong ? "wrong" : "fall");
    }

    public void Update(double dt)
    {
        acc += dt;
        while (acc >= Tick - 1e-9) { acc -= Tick; Step(); }
    }

    public void Fire(string ev) => OnEvent?.Invoke(ev);

    void Step()
    {
        Clock += Tick;
        Ticks++;
        if (State == Phase.Wait) return;
        if (State == Phase.Over) { EndedFor += Tick; return; }
        if (State == Phase.Fall)
        {
            EndedFor += Tick;
            if (EndedFor >= T.FallSec - 1e-9) { State = Phase.Over; EndedFor = 0; Fire("over"); }
            return;
        }
        RunClock += Tick;
        if (SaveLeft > 0) { SaveLeft -= Tick; return; }                                                    // 홀씨로 돌아오는 동안 막대 멈춤
        if (LiftLeft > 0) { LiftLeft -= Tick; if (LiftLeft <= 0) GraceLeft = T.CleanGrace; return; }      // 덩굴손이 던져 올리는 동안, 내려선 뒤 잠깐도
        if (GraceLeft > 0) GraceLeft -= Tick;
        else Bar -= Drain(N) * Tick;
        if (WiltLeft > 0 && (WiltLeft -= Tick) <= 0) { Drop(Cause.Wilt); return; }
        if (Bar <= 0) { Bar = 0; Drop(Cause.Time); }
    }

    // 디버그·장면: 세 칸 앞 잎을 그 종류로 (구름 띠는 Fog 를 켠다)
    public void Put(ClimbKind kind)
    {
        ref var lf = ref At(N + 3);
        (lf.Kind, lf.Taken) = (kind, false);
    }

    public string CauseName => Why switch { Cause.Wrong => "wrong", Cause.Time => "time", Cause.Wilt => "wilt", _ => "" };
}
