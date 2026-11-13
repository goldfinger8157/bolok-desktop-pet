namespace Bolok;

// 네잎클로버·새싹 (YELL 2026-11-12): 돌아다니다 가끔(하루 한 번까지) 근처 바닥에 새싹이나(70%) 네잎클로버가(30%, 행운이라 드물게) 돋는다.
// 볼록이가 보고 "!" 하고 다가가 주워서 머리에 꽂고 다닌다 (머리 왼쪽 — 애교쟁이 꽃은 오른쪽 귀라서 안 겹친다).
// 던져지면 그 자리에 떨어지고, 내려와서 다시 주워 꽂는다. 날이 바뀌면 시들어 없어진다.
// 규칙만 여기 (화면과 무관해서 --selfcheck 로 검사한다). 그리기·걷기는 PetForm·Overlay 가 이 상태를 보고 한다
public sealed class Clover
{
    public enum Phase { None, Ground, Worn, Falling } // Falling: 낙엽이 하늘에서 팔랑팔랑 내려오는 중
    public const double ChancePerMin = 1 / 90.0; // 한가하게 돌아다니는 1분마다 (평균 한 시간 반에 한 번, 하루 한 번까지)
    public const double GiveUpSec = 1800;        // 바닥에 30분 넘게 못 주우면 (다른 화면에 갔다든가) 없어진다

    // 그림 배율: 볼록이 배율(k)보다 한 단계 작게 (배율 3이면 2). 1.5배로 키워 봤더니 하트처럼 보여서 알아보기 어려웠다(YELL) →
    // 크기는 되돌리고 그림을 하트 잎 넷이 십자로 놓인 11칸짜리로 바꿨다 (옛 그림보다 칸이 많아 같은 배율에서 조금 크다)
    public static int Scale(int k) => Math.Max(1, k - 1);

    public Phase State { get; private set; }
    public double X, Y;     // 바닥에 있을 때 자리 (화면 좌표, 땅 높이)
    public string Day = ""; // 돋은 날 (하루 한 번)
    public string Kind = "clover"; // clover(네잎클로버) | sprout(새싹) | sunflower(해바라기) | fallen·ginkgo·maple(낙엽: 잎맥 잎·은행잎·단풍잎). 그림은 item/<Kind>
    public static readonly string[] Kinds = { "clover", "sprout", "sunflower", "fallen", "ginkgo", "maple", "redmaple" };
    public static bool Falls(string kind) => kind is "fallen" or "ginkgo" or "maple" or "redmaple"; // 낙엽: 땅에 안 돋고 하늘에서 내려와 머리에 얹힌다 (선물의 초록 잎 item/leaf 와는 다른 그림)
    public const double FallSec = 3.2; // 낙엽이 머리까지 내려오는 시간
    public int Look; // 머리에 누운 낙엽의 모양 0~5 (누움·비스듬히 둘 × 좌우) — 내려앉거나 주울 때마다 무작위 (YELL 2026-11-13)
    void NewLook() => Look = Random.Shared.Next(6);
    double groundAt, fallAt, fallX0, fallY0;

    // 무엇이 돋을지 — 계절마다 다르게 (YELL 2026-11-13: 해바라기·낙엽 추가). 가을엔 낙엽, 여름엔 해바라기가 잦다
    public static string PickKind(int month, double roll)
    {
        var w = month is >= 9 and <= 11 ? new[] { ("sprout", .30), ("clover", .15), ("sunflower", .15), ("fallen", .10), ("ginkgo", .10), ("maple", .10), ("redmaple", .10) }
              : month is >= 6 and <= 8 ? new[] { ("sprout", .35), ("clover", .20), ("sunflower", .35), ("fallen", .03), ("ginkgo", .03), ("maple", .02), ("redmaple", .02) }
              : new[] { ("sprout", .45), ("clover", .25), ("sunflower", .15), ("fallen", .04), ("ginkgo", .04), ("maple", .04), ("redmaple", .03) }; // 낙엽 네 가지는 거의 같은 몫
        foreach (var (k, p) in w) { if (roll < p) return k; roll -= p; }
        return "sprout";
    }

    static int MonthOf(string day) => day.Length >= 7 && int.TryParse(day.AsSpan(5, 2), out int m) ? m : 1;

    public sealed record Saved(string State, double X, double Y, string Day, string? Kind = null);
    public Saved Snapshot() => new(State.ToString(), X, Y, Day, Kind);

    public void Load(Saved? s)
    {
        if (s == null) return;
        (X, Y, Day, Kind) = (s.X, s.Y, s.Day, s.Kind == "leaf" ? "maple" : s.Kind is { } k && Kinds.Contains(k) ? k : "clover"); // leaf: 낙엽이 한 가지였을 때 이름
        State = Enum.TryParse<Phase>(s.State, out var p) ? p : Phase.None;
        groundAt = fallAt = double.NegativeInfinity; // 다시 켠 뒤 바닥에 있던 건 곧 주우러 간다 (시간은 새로 센다), 내려오던 낙엽은 곧 머리에
    }

    // 날이 바뀌었으면 시든다. 시들었으면 true
    public bool Wilt(string today, double now)
    {
        if (State == Phase.None || Day == today) return false;
        State = Phase.None;
        return true;
    }

    // 오늘 아직 안 돋았으면 가끔 볼록이 앞쪽 2~3.5칸에 돋는다 (화면 안쪽으로).
    // 낙엽은 땅에 돋지 않고 볼록이 앞 위쪽 하늘에서 팔랑팔랑 내려와 머리 위에 얹힌다 (headY: 머리 위 높이)
    public bool TrySprout(string today, double now, double petX, int dir, double ground, double cell, Rectangle area, Random rng, double chance = ChancePerMin, double headY = double.NaN, string? kind = null)
    {
        if (State != Phase.None || Day == today || rng.NextDouble() >= chance) return false;
        double x = petX + dir * cell * (2 + rng.NextDouble() * 1.5);
        if (x < area.Left + cell || x > area.Right - cell) x = petX - dir * cell * 2.5; // 앞이 막혔으면 뒤쪽에
        Day = today;
        Kind = kind != null && Kinds.Contains(kind) ? kind : PickKind(MonthOf(today), rng.NextDouble());
        if (Falls(Kind))
        {
            double top = double.IsNaN(headY) ? ground - cell : headY;
            (State, fallAt, X, Y) = (Phase.Falling, now, petX, top);
            (fallX0, fallY0) = (Math.Clamp(petX + dir * cell * 1.2, area.Left + cell, area.Right - cell), Math.Max(area.Top + cell * .3, top - cell * 3.5));
            return true;
        }
        Put(Math.Clamp(x, area.Left + cell, area.Right - cell), ground, now);
        return true;
    }

    // 내려오는 낙엽의 자리: 처음 자리에서 볼록이 머리 위로 다가가며 좌우로 팔랑 (내려올수록 덜 흔들린다)
    public (double X, double Y) FallPos(double now, double petX, double headY, double cell)
    {
        double t = now - fallAt;
        if (double.IsInfinity(t)) return (petX, headY);
        double p = Math.Clamp(t / FallSec, 0, 1), e = p * p * (3 - 2 * p);
        return (fallX0 + (petX - fallX0) * e + Math.Sin(t * 4.2) * cell * .35 * (1 - p), fallY0 + (headY - fallY0) * p);
    }

    public bool Flip(double now) => !double.IsInfinity(now - fallAt) && (int)((now - fallAt) / .35) % 2 == 1; // 팔랑: 좌우로 뒤집힌다

    // 다 내려왔으면 머리에 얹힌다 (볼록이가 날아가는 중이면 그 자리 바닥에). 내려앉았으면 true
    public bool Land(double now, bool onHead, double x, double ground)
    {
        if (State != Phase.Falling || now - fallAt < FallSec) return false;
        if (onHead) { State = Phase.Worn; NewLook(); }
        else Put(x, ground, now);
        return true;
    }

    void Put(double x, double y, double now) => (State, X, Y, groundAt) = (Phase.Ground, x, y, now);

    // 줍기: 바닥에 있고 발이 닿았으면
    public bool TryPick(double petX, double petY, double cell)
    {
        if (State != Phase.Ground || Math.Abs(petX - X) > cell * .45 || Math.Abs(petY - Y) > cell * .6) return false;
        State = Phase.Worn;
        NewLook();
        return true;
    }

    // 던져지면 머리에서 떨어져 그 자리 바닥에
    public bool Drop(double x, double ground, double now)
    {
        if (State != Phase.Worn) return false;
        Put(x, ground, now);
        return true;
    }

    // 너무 오래 못 주웠으면 없어진다
    public bool Lost(double now)
    {
        if (State != Phase.Ground || now - groundAt < GiveUpSec) return false;
        if (double.IsNegativeInfinity(groundAt)) { groundAt = now; return false; }
        State = Phase.None;
        return true;
    }

    public void Force(Phase p, double x, double y, string today, double now, string? kind = null) { (X, Y, Day, groundAt, fallAt, fallX0, fallY0) = (x, y, today, now, now, x, y); State = p; NewLook(); if (kind != null && Kinds.Contains(kind)) Kind = kind; } // 디버그

    // ── 자가 검사 ──
    public static string Check()
    {
        static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: 네잎클로버 — " + what); }
        var area = new Rectangle(0, 0, 1920, 1040);
        var c = new Clover();
        var rng = new Random(1);
        int minutes = 0;
        while (!c.TrySprout("2026-11-12", minutes * 60, 900, 1, 1000, 120, area, rng, kind: "sprout")) minutes++;
        Expect(minutes < 600 && c.State == Phase.Ground && c.X >= 900 + 240 && c.X <= 900 + 420 && c.Y == 1000, $"돌아다니면 가끔 앞쪽 바닥에 돋는다 ({minutes}분)");
        Expect(!c.TryPick(500, 1000, 120) && c.TryPick(c.X + 10, 1000, 120) && c.State == Phase.Worn, "발이 닿아야 줍고, 주우면 머리에");
        var again = new Clover { Day = "2026-11-12" };
        Expect(!again.TrySprout("2026-11-12", 0, 900, 1, 1000, 120, area, new Random(1), 1), "하루 한 번까지");
        Expect(c.Drop(300, 1000, 100) && c.State == Phase.Ground && c.X == 300 && c.TryPick(300, 1000, 120), "던져지면 그 자리에 떨어지고 다시 줍는다");
        Expect(!c.Wilt("2026-11-12", 0) && c.Wilt("2026-11-13", 0) && c.State == Phase.None, "날이 바뀌면 시든다");
        var edge = new Clover();
        edge.TrySprout("d", 0, 1900, 1, 1000, 120, area, new Random(2), 1, kind: "sprout");
        Expect(edge.X <= area.Right - 120 && edge.X < 1900, "화면 끝이면 뒤쪽에 돋는다");
        Expect(!edge.Lost(GiveUpSec - 1) && edge.Lost(GiveUpSec + 1) && edge.State == Phase.None, "30분 넘게 못 주우면 없어진다");
        var saved = new Clover();
        saved.Load(new Saved("Worn", 0, 0, "d", "sprout"));
        Expect(saved.State == Phase.Worn && saved.Day == "d" && saved.Kind == "sprout", "다시 켜도 머리에 그대로 (새싹은 새싹)");
        saved.Load(new Saved("Worn", 0, 0, "d"));
        Expect(saved.Kind == "clover", "예전 기록(종류 없음)은 네잎클로버");
        int Count(string month, string kind)
        {
            var pick = new Random(3);
            int n = 0;
            for (int i = 0; i < 2000; i++) { var one = new Clover(); one.TrySprout($"2026-{month}-{i % 28 + 1:00}x{i}", 0, 900, 1, 1000, 120, area, pick, 1); if (one.Kind == kind) n++; }
            return n;
        }
        int clovers = Count("03", "clover"), summerSun = Count("07", "sunflower"), ginkgo = Count("10", "ginkgo"), maple = Count("10", "maple"), red = Count("10", "redmaple");
        int autumnLeaf = Count("10", "fallen") + ginkgo + maple + red, springLeaf = Count("04", "fallen") + Count("04", "ginkgo") + Count("04", "maple") + Count("04", "redmaple");
        Expect(new[] { ginkgo, maple, red }.All(n => n is > 130 and < 270), $"가을 낙엽은 잎맥 잎·은행잎·단풍잎·빨간 단풍잎이 고루 ({autumnLeaf - ginkgo - maple - red}·{ginkgo}·{maple}·{red})");
        Expect(clovers is > 400 and < 600 && autumnLeaf is > 700 and < 900 && summerSun is > 600 and < 800 && springLeaf is > 200 and < 400,
            $"계절마다 다르게: 봄 네잎클로버 {clovers}·낙엽 {springLeaf}, 가을 낙엽 {autumnLeaf}, 여름 해바라기 {summerSun} (2000번 중)");
        // 낙엽: 하늘에서 팔랑팔랑 내려와 머리에 얹힌다. 날아가는 중이면 바닥에 떨어지고 다시 주워 얹는다
        var leaf = new Clover();
        Expect(leaf.TrySprout("2026-11-13", 100, 900, 1, 1000, 120, area, new Random(5), 1, headY: 860, kind: "ginkgo") && leaf.State == Phase.Falling, "낙엽은 땅에 안 돋고 하늘에서 내려온다");
        var (fx, fy) = leaf.FallPos(100, 900, 860, 120);
        var (mx, my) = leaf.FallPos(100 + FallSec / 2, 950, 860, 120);
        var (lx, ly) = leaf.FallPos(100 + FallSec, 950, 860, 120);
        Expect(fy < 860 - 120 * 3 && my > fy && my < 860 && Math.Abs(lx - 950) < 1 && Math.Abs(ly - 860) < 1, $"위에서 내려와 걷는 볼록이 머리로 ({fx:F0},{fy:F0}) → ({mx:F0},{my:F0}) → ({lx:F0},{ly:F0})");
        Expect(!leaf.Land(100 + FallSec - .1, true, 950, 1000) && leaf.Land(100 + FallSec, true, 950, 1000) && leaf.State == Phase.Worn, "다 내려오면 머리에 얹힌다");
        var tossed = new Clover();
        tossed.TrySprout("2026-11-13", 0, 900, 1, 1000, 120, area, new Random(5), 1, headY: 860, kind: "maple");
        Expect(tossed.Land(FallSec, false, 700, 1000) && tossed.State == Phase.Ground && tossed.X == 700 && tossed.TryPick(700, 1000, 120), "날아가는 중이면 바닥에 떨어지고, 주우면 다시 머리에");
        var reload = new Clover();
        reload.Load(new Saved("Falling", 0, 0, "d", "leaf"));
        Expect(reload.Land(5, true, 0, 1000) && reload.State == Phase.Worn && reload.Kind == "maple", "내려오다 앱을 껐다 켜면 곧 머리에 (옛 이름 leaf 는 단풍잎)");
        return $"머리 장식(네잎클로버·새싹·해바라기·낙엽): 돌아다닌 지 {minutes}분에 돋음, 하루 한 번, 계절마다 비율(가을 낙엽 {autumnLeaf / 20}%·여름 해바라기 {summerSun / 20}%), 낙엽은 {FallSec}초 팔랑 내려와 머리에, 떨어지면 다시 줍고, 다음 날 시듦";
    }
}
