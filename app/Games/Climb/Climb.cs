namespace Bolok;

// 클라이밍 규칙 (화면 없음, docs/climb.md 4장): 덩굴 만들기·오르기·시간 막대·기믹·떨어짐·점수.
// 고정 박자 1/60초로 돈다 (Update 가 쌓아서 나눠 돌림). 조작은 Press(방향) 한 가지 → 씨와 「몇 틱에 어느 쪽」 기록이 같으면 같은 판.
// 잎 i 의 방향·종류는 씨와 i 까지의 뽑기로만 정해진다 (조작·프레임 빠르기와 무관). 잎은 고리 배열 64칸 (앞 20칸을 미리 만든다), 도는 동안 새로 만드는 것 없음.
// 방향 Dir: 잎 i 가 앞 잎보다 왼쪽(−1)·오른쪽(+1)에 있다 (옮겨 간 쪽 = 누를 키). 모양 A2 는 잎이 지그재그로 오르고(Col 이 누적 자리), 모양 A4 는 줄기 왼쪽·오른쪽에 번갈아 붙는다 (Dir 이 줄기 쪽)
// 콤보 (docs/climb-combo.md 1장): 바르게 오른 칸이 앞 칸에서 0.5초(30틱) 안이면 이어진다. 5·15·30 에서 단계가 오르고 5마다 막대가 조금 찬다 (점수는 안 바꾼다)
// 카페 (5장): 꺾이는 발판 바깥쪽에 가끔. 그쪽 키로 들어가 쉬고(막대가 찬다, 콤보는 끊긴다) 길 쪽 키로 같은 발판에 돌아온다
enum ClimbKind : byte { Leaf, Berry, Puff, Wilt, Flower }

struct ClimbLeaf
{
    public sbyte Dir;
    public ClimbKind Kind;
    public bool Fog, Taken;      // 구름 띠 안인가, 열매·홀씨를 주웠나
    public sbyte Cafe;           // 이 발판 옆 카페가 선 쪽 (0 없음, −1·+1: 다음 발판의 반대쪽)
    public bool Visited;         // 카페에 다녀왔나 (불이 꺼진다)
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
    public double Clock, RunClock, EndedFor, StepAt = -9, FallAt = -9, SaveAt = -9, MileAt = -9, BerryAt = -9, WrongAt = -9;
    public double SaveLeft, GraceLeft, WiltLeft = -1;
    public int Ticks;                    // 지난 규칙 틱 수 (조작 기록의 때)
    public int Steps, Berries, Saves, Wrongs, Presses;
    public int LastPress, PressTick;     // 마지막으로 받은 키(−1·+1)와 그때의 틱 (조작 기록을 뽑는 데 쓴다)
    public int Best;                     // 지난 최고 (넘는 순간 best 사건). 0 이면 없다
    public Action<string>? OnEvent;
    double acc;
    bool bestFired;

    // 콤보: 지금 이어진 칸 수, 한 판의 가장 긴 것, 끊긴 순간의 값·때 (그림이 회색 쪽지를 떨어뜨린다), 5의 배수를 넘은 때
    public int Combo, MaxCombo, ComboBroken;
    public readonly int[] TierSteps = new int[4];   // 단계별로 오른 칸 수 (봇 표·로그)
    public double ComboEndAt = -9, Combo5At = -9;
    int stepTick, lastTier;
    public int Tier => Combo >= T.Tier3 ? 3 : Combo >= T.Tier2 ? 2 : Combo >= T.Tier1 ? 1 : 0;

    // 카페: 0 없음 · 1 걸어 들어감 · 2 안에서 쉼 · 3 걸어 나옴. 안에 있는 동안 N 은 그대로 (카페가 붙은 발판)
    public int CafePhase, Cafes, CafeSideNow;
    public double CafeLeft, CafeStay, CafeSec, CafeAt = -9, CafeFullAt = -9;
    public bool InCafe => CafePhase != 0;

    // 만들기 상태
    readonly bool edge;
    readonly Random rd, rg, rc, cr;      // cr: 카페 전용 (길·기믹 뽑기 차례를 안 건드린다)
    int genTo = -1, runDir, runLeft, zig, col, longestZig, curZig;
    int nextBerry, nextPuff, nextWilt, nextCafe, lastG = -99, bandLeft, bandNext;

    public bool Done => State is Phase.Fall or Phase.Over;
    public double Pace(int n) => Math.Min(T.PaceMax, T.PaceBase + n / T.PaceSlope);
    public double Drain(int n) => T.Gain * Pace(n);
    public double SinceFall => Clock - FallAt;
    public int Zone(int leaf) => Math.Min(5, leaf / T.Zone);
    public bool Standing => State is Phase.Wait or Phase.Run;

    // start: 몇 잎에서 시작하나 (연습 판). 0 이 보통
    // edge: 창 테두리 판(A4)이다 — 줄기 한쪽에 발판이 쌓여 카페 자리가 더 까다롭다 (CafeMark)
    public Climb(int seed, ClimbTune? tune = null, int start = 0, bool edge = false)
    {
        T = tune ?? ClimbTune.Now;
        this.edge = edge;
        Seed = seed;
        rd = new Random(seed); rg = new Random(seed * 31 + 7); rc = new Random(seed * 17 + 3); cr = new Random(seed * 13 + 11);
        nextBerry = T.BerryFrom + rg.Next(0, T.BerryGapHi - T.BerryGapLo + 1);
        nextPuff = T.PuffFrom + rg.Next(0, 21);
        nextWilt = T.WiltFrom + rg.Next(0, T.WiltGapHi - T.WiltGapLo + 1);
        nextCafe = T.CafeFrom + cr.Next(0, T.CafeFromSpan + 1);
        bandNext = T.CloudFrom + rc.Next(0, 6);
        Bar = T.StartBar;
        Gen(0); Gen(1);
        ring[0].Dir = ring[1].Dir;       // 첫 잎은 다음 잎 쪽으로 향한다
        N = PrevN = Math.Max(0, start);
        Ensure(N + Ahead);
    }

    // ── 덩굴 만들기 ──
    // 고리는 64칸이라 지금 잎에서 멀리 앞(N+56 넘게)·뒤(만든 끝에서 62칸 넘게)를 읽으면 다른 잎 자리를 덮어쓴다 (잎 108 이 잎 300 으로 바뀐다: 길이 끊겨 보이고 틀린 쪽으로 눌린다).
    // Guard 를 켜 두면(게임) 그런 읽기는 고리를 안 건드리고 빈 잎을 주며, 처음 한 번 어디서 불렀는지 로그에 남긴다 (RingTrips). 검사·도구는 처음부터 끝까지 읽어야 해서 끈다
    public bool Guard;
    public int RingTrips;
    static ClimbLeaf stray;

    public ref ClimbLeaf At(int i)
    {
        if (Guard && (i > N + Ring - 8 || i < genTo - (Ring - 2)))
        {
            if (RingTrips++ == 0) Dbg.Log("climb-ring", ("leaf", i), ("n", N), ("gen", genTo), ("seed", Seed), ("from", new System.Diagnostics.StackTrace(1, false).ToString().Replace("\r", "").Replace("\n", " | ")));
            stray = default;
            return ref stray;
        }
        if (i > genTo) Ensure(i);
        return ref ring[i & (Ring - 1)];
    }

    void Ensure(int upTo) { while (genTo < upTo) Gen(genTo + 1); }

    // 한 줄(같은 쪽으로 L 칸) 길이 뽑기: 높이에 따라 L=1 비율과 짧은 쪽으로 기우는 정도가 달라진다
    int DrawL(int i)
    {
        double p1 = i < 30 ? 0 : i < 150 ? T.OneLo * (i - 30) / 120.0 : i < 300 ? T.OneLo + (T.OneMid - T.OneLo) * (i - 150) / 150.0 : i < T.OneTopAt ? T.OneHi + (T.OneTop - T.OneHi) * (i - 300) / (double)(T.OneTopAt - 300) : T.OneTop;
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
        else if (T.Gimmicks && i - lastG >= 3 && !MileWithin(i - 2, i + 2, T.Mile))
        {
            if (i >= T.PuffFrom && i >= nextPuff) { lf.Kind = ClimbKind.Puff; nextPuff = i + rg.Next(T.PuffGapLo, T.PuffGapHi + 1); }
            else if (i >= T.WiltFrom && i >= nextWilt) { lf.Kind = ClimbKind.Wilt; nextWilt = i + rg.Next(T.WiltGapLo, T.WiltGapHi + 1); }
            else if (i >= T.BerryFrom && i >= nextBerry) { lf.Kind = ClimbKind.Berry; nextBerry = i + rg.Next(T.BerryGapLo, T.BerryGapHi + 1); }
            if (lf.Kind != ClimbKind.Leaf) lastG = i;
        }
        if (T.Gimmicks && i >= T.CloudFrom)   // 구름 띠: 8~12잎, 구름 구역(150~299)에서는 잦고 그 뒤에는 가끔
        {
            if (bandLeft > 0 || i >= bandNext)
            {
                if (bandLeft == 0) bandLeft = rc.Next(T.CloudLenLo, T.CloudLenHi + 1);
                lf.Fog = true;
                if (--bandLeft == 0) bandNext = i + 1 + (i < T.CloudFrom + T.CloudZone ? rc.Next(T.CloudGapLo, T.CloudGapHi + 1) : rc.Next(T.CloudLateLo, T.CloudLateHi + 1));
            }
        }
        if (T.Gimmicks && i >= 5) CafeMark(i - 4);   // 앞뒤 잎이 다 만들어진 뒤에 본다 (구름 띠·이정표 둘레까지)
    }

    // 카페 자리 (5-1): 발판 c 까지 같은 쪽으로 2칸 이상 올라왔고, 다음 발판 c+1 이 반대쪽이며 그 뒤로도 같은 쪽 2칸 이상 → 카페는 c 의 바깥쪽 (= 다음 발판의 반대쪽).
    // 그러면 지난 발판·다음 발판이 다 한쪽에 있고 반대쪽은 위아래로 비어 있다. 길·기믹 뽑기와 무관하게 씨로만 정해진다
    void CafeMark(int c)
    {
        const int M = Ring - 1;
        if (c < T.EasyLeaves || c < nextCafe) return;
        ref var lf = ref ring[c & M];
        if (lf.Kind != ClimbKind.Leaf) return;
        int d = lf.Dir;
        if (ring[(c - 1) & M].Dir != d || ring[(c + 1) & M].Dir != -d || ring[(c + 2) & M].Dir != -d) return;
        for (int j = c - 2; j <= c + 2; j++) if (ring[j & M].Kind == ClimbKind.Wilt) return;
        for (int j = c - 1; j <= c + 4; j++) if (ring[j & M].Fog) return;
        for (int j = c - 3; j <= c + 3; j++) if (j % T.Mile == 0) return;
        if (ring[(c + 3) & M].Dir == ring[(c + 4) & M].Dir) return;
        if (edge && ring[(c + 3) & M].Dir == d) return;                // 창 테두리 판: 카페 쪽 줄기에 발판 c+3 이 카페 위에 얹히지 않게   // 길이 카페 쪽 위로 돌아오지도(c+4 가 c 바로 위), 한참 멀리 가지도(4칸 줄) 않는다 → 카페 둘레 판 안에 든다
        lf.Cafe = (sbyte)d;
        nextCafe = c + cr.Next(T.CafeGapLo, T.CafeGapHi + 1);
    }

    public int LongestZig => longestZig;

    // 구름에 가린 잎인가 (지금 잎에서 CloudHide 칸 이상 위)
    public bool Hidden(int i) => i - N >= T.CloudHide && At(i).Fog;

    // ── 조작 (키를 누른 프레임에 Update 앞에) ──
    // dir: −1 왼쪽, +1 오른쪽. 멈춤(홀씨·카페 드나들기)이나 끝난 판에서는 먹지 않는다
    public void Press(int dir)
    {
        if (Done || dir == 0) return;
        if (SaveLeft > 0 || CafePhase is 1 or 3) return;     // 홀씨로 돌아오는 동안, 카페로 걸어 들어가고 나오는 동안 (연타로 들어갔다 바로 튕겨 나오지 않게)
        if (CafePhase == 2)                                   // 카페 안: 길 쪽 키만 먹는다 (안쪽 키는 아무 일도 없다)
        {
            Ensure(N + Ahead);
            if (dir != At(N + 1).Dir) return;
            Presses++;
            (LastPress, PressTick) = (dir, Ticks);
            CafePhase = 3; CafeLeft = T.CafeOutSec;
            Fire("cafe-out");
            return;
        }
        if (State == Phase.Wait) { State = Phase.Run; Fire("go"); }
        Ensure(N + Ahead);
        ref var cur = ref At(N);
        if (cur.Cafe != 0 && dir == cur.Cafe)                 // 카페가 붙은 발판: 카페 쪽 키는 틀린 쪽이 아니다 (떨어지지 않는다)
        {
            if (cur.Visited) return;                          // 다녀온 카페는 아무 일도 없다
            Presses++;
            (LastPress, PressTick) = (dir, Ticks);
            cur.Visited = true;
            CafePhase = 1; CafeLeft = T.CafeInSec; CafeStay = 0; CafeSideNow = cur.Cafe; CafeAt = Clock; CafeFullAt = -9;
            Cafes++;
            BreakCombo();
            Fire("cafe-in");
            return;
        }
        Presses++;
        (LastPress, PressTick) = (dir, Ticks);
        if (At(N + 1).Dir != dir)
        {
            WrongDir = dir; WrongAt = Clock; Wrongs++;
            if (Puff) { Puff = false; SaveLeft = T.SaveSec; SaveAt = Clock; Saves++; BreakCombo(); Fire("save"); }
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
        // 콤보: 앞 칸에서 ComboTicks(0.5초) 안이면 이어진다. 시간은 틱으로 센다 (같은 씨·같은 조작 = 같은 콤보)
        Combo = Combo > 0 && Ticks - stepTick <= T.ComboTicks ? Combo + 1 : 1;
        stepTick = Ticks;
        if (Combo > MaxCombo) MaxCombo = Combo;
        int tier = Tier;
        TierSteps[tier]++;
        if (tier > lastTier) { lastTier = tier; Fire("tier"); }
        if (tier > 0 && Combo % T.ComboEvery == 0) { Bar = Math.Min(1, Bar + T.ComboBar[tier - 1]); Combo5At = Clock; Fire("combo5"); }
        ref var lf = ref At(N);
        switch (lf.Kind)
        {
            case ClimbKind.Berry when !lf.Taken: lf.Taken = true; Bar = Math.Min(1, Bar + T.BerryBar); Berries++; BerryAt = Clock; Fire("berry"); break;
            case ClimbKind.Puff when !lf.Taken && !Puff: lf.Taken = true; Puff = true; Fire("puff"); break;
            case ClimbKind.Wilt: WiltLeft = T.WiltSec; Fire("wilt"); break;
            case ClimbKind.Flower: Bar = 1; MileAt = Clock; Fire("mile"); break;
        }
        if (Best > 0 && !bestFired && N > Best) { bestFired = true; Fire("best"); }
    }

    // 콤보가 끊긴다 (0.5초 넘게 쉼 · 홀씨로 살아남 · 카페에 들어감 · 판이 앞 창이 아니게 됨 · 떨어짐). 열매·큰 꽃·시든 잎은 안 끊는다
    public void BreakCombo()
    {
        if (Combo <= 0) return;
        ComboBroken = Combo; ComboEndAt = Clock;
        Combo = 0; lastTier = 0;
        Fire("combo-end");
    }

    void Drop(Cause why)
    {
        BreakCombo();
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
        if (Combo > 0 && Ticks - stepTick > T.ComboTicks) BreakCombo();                                    // 0.5초가 지나면 다음 키를 안 기다리고 그 틱에 끊긴다 (화면의 숫자가 제때 꺼진다)
        if (CafePhase != 0)                                                                                // 카페: 막대가 안 준다. 안에서 쉬는 동안 찬다
        {
            CafeSec += Tick; CafeStay += Tick;
            if (CafePhase == 2)
            {
                if (Bar < 1) { Bar = Math.Min(1, Bar + T.CafeFill * Tick); if (Bar >= 1) CafeFullAt = Clock; }
            }
            else if ((CafeLeft -= Tick) <= 1e-9)
            {
                if (CafePhase == 1) CafePhase = 2;
                else { CafePhase = 0; GraceLeft = T.CafeGrace; }                                           // 나온 뒤 잠깐은 막대가 안 준다 (숨 고르고 출발)
            }
            return;
        }
        if (SaveLeft > 0) { SaveLeft -= Tick; return; }                                                    // 홀씨로 돌아오는 동안 막대 멈춤
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

    // 디버그·장면: 콤보를 n 으로 (지금 칸에서 이어진 것처럼)
    public void SetCombo(int n)
    {
        Combo = n; stepTick = Ticks; lastTier = Tier;
        if (n > MaxCombo) MaxCombo = n;
    }

    // 디버그·장면: 지금 선 발판 옆에 카페를 (다음 발판이 반대쪽이 되게 이 발판과 앞 둘의 방향을 맞추지는 않는다 — 장면은 길을 직접 맞춘다)
    public void PutCafe(int side) { ref var lf = ref At(N); lf.Cafe = (sbyte)side; lf.Visited = false; }

    public string CauseName => Why switch { Cause.Wrong => "wrong", Cause.Time => "time", Cause.Wilt => "wilt", _ => "" };
}
