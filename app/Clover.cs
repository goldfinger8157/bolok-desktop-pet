namespace Bolok;

// 네잎클로버·새싹 (YELL 2026-11-12): 돌아다니다 가끔(하루 한 번까지) 근처 바닥에 새싹이나(70%) 네잎클로버가(30%, 행운이라 드물게) 돋는다.
// 볼록이가 보고 "!" 하고 다가가 주워서 머리에 꽂고 다닌다 (머리 왼쪽 — 애교쟁이 꽃은 오른쪽 귀라서 안 겹친다).
// 던져지면 그 자리에 떨어지고, 내려와서 다시 주워 꽂는다. 날이 바뀌면 시들어 없어진다.
// 규칙만 여기 (화면과 무관해서 --selfcheck 로 검사한다). 그리기·걷기는 PetForm·Overlay 가 이 상태를 보고 한다
public sealed class Clover
{
    public enum Phase { None, Ground, Worn }
    public const double ChancePerMin = 1 / 90.0; // 한가하게 돌아다니는 1분마다 (평균 한 시간 반에 한 번, 하루 한 번까지)
    public const double GiveUpSec = 1800;        // 바닥에 30분 넘게 못 주우면 (다른 화면에 갔다든가) 없어진다

    // 그림 배율: 볼록이 배율(k)보다 한 단계 작게 (배율 3이면 2). 1.5배로 키워 봤더니 하트처럼 보여서 알아보기 어려웠다(YELL) →
    // 크기는 되돌리고 그림을 하트 잎 넷이 십자로 놓인 11칸짜리로 바꿨다 (옛 그림보다 칸이 많아 같은 배율에서 조금 크다)
    public static int Scale(int k) => Math.Max(1, k - 1);

    public Phase State { get; private set; }
    public double X, Y;     // 바닥에 있을 때 자리 (화면 좌표, 땅 높이)
    public string Day = ""; // 돋은 날 (하루 한 번)
    public string Kind = "clover"; // clover(네잎클로버) | sprout(새싹). 그림은 item/<Kind>
    public static readonly string[] Kinds = { "clover", "sprout" };
    public const double CloverShare = .3;
    double groundAt;

    public sealed record Saved(string State, double X, double Y, string Day, string? Kind = null);
    public Saved Snapshot() => new(State.ToString(), X, Y, Day, Kind);

    public void Load(Saved? s)
    {
        if (s == null) return;
        (X, Y, Day, Kind) = (s.X, s.Y, s.Day, s.Kind is { } k && Kinds.Contains(k) ? k : "clover");
        State = Enum.TryParse<Phase>(s.State, out var p) ? p : Phase.None;
        groundAt = double.NegativeInfinity; // 다시 켠 뒤 바닥에 있던 건 곧 주우러 간다 (시간은 새로 센다)
    }

    // 날이 바뀌었으면 시든다. 시들었으면 true
    public bool Wilt(string today, double now)
    {
        if (State == Phase.None || Day == today) return false;
        State = Phase.None;
        return true;
    }

    // 오늘 아직 안 돋았으면 가끔 볼록이 앞쪽 2~3.5칸에 돋는다 (화면 안쪽으로)
    public bool TrySprout(string today, double now, double petX, int dir, double ground, double cell, Rectangle area, Random rng, double chance = ChancePerMin)
    {
        if (State != Phase.None || Day == today || rng.NextDouble() >= chance) return false;
        double x = petX + dir * cell * (2 + rng.NextDouble() * 1.5);
        if (x < area.Left + cell || x > area.Right - cell) x = petX - dir * cell * 2.5; // 앞이 막혔으면 뒤쪽에
        Put(Math.Clamp(x, area.Left + cell, area.Right - cell), ground, now);
        Day = today;
        Kind = rng.NextDouble() < CloverShare ? "clover" : "sprout";
        return true;
    }

    void Put(double x, double y, double now) => (State, X, Y, groundAt) = (Phase.Ground, x, y, now);

    // 줍기: 바닥에 있고 발이 닿았으면
    public bool TryPick(double petX, double petY, double cell)
    {
        if (State != Phase.Ground || Math.Abs(petX - X) > cell * .45 || Math.Abs(petY - Y) > cell * .6) return false;
        State = Phase.Worn;
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

    public void Force(Phase p, double x, double y, string today, double now, string? kind = null) { (X, Y, Day, groundAt) = (x, y, today, now); State = p; if (kind != null && Kinds.Contains(kind)) Kind = kind; } // 디버그

    // ── 자가 검사 ──
    public static string Check()
    {
        static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: 네잎클로버 — " + what); }
        var area = new Rectangle(0, 0, 1920, 1040);
        var c = new Clover();
        var rng = new Random(1);
        int minutes = 0;
        while (!c.TrySprout("2026-11-12", minutes * 60, 900, 1, 1000, 120, area, rng)) minutes++;
        Expect(minutes < 600 && c.State == Phase.Ground && c.X >= 900 + 240 && c.X <= 900 + 420 && c.Y == 1000, $"돌아다니면 가끔 앞쪽 바닥에 돋는다 ({minutes}분)");
        Expect(!c.TryPick(500, 1000, 120) && c.TryPick(c.X + 10, 1000, 120) && c.State == Phase.Worn, "발이 닿아야 줍고, 주우면 머리에");
        var again = new Clover { Day = "2026-11-12" };
        Expect(!again.TrySprout("2026-11-12", 0, 900, 1, 1000, 120, area, new Random(1), 1), "하루 한 번까지");
        Expect(c.Drop(300, 1000, 100) && c.State == Phase.Ground && c.X == 300 && c.TryPick(300, 1000, 120), "던져지면 그 자리에 떨어지고 다시 줍는다");
        Expect(!c.Wilt("2026-11-12", 0) && c.Wilt("2026-11-13", 0) && c.State == Phase.None, "날이 바뀌면 시든다");
        var edge = new Clover();
        edge.TrySprout("d", 0, 1900, 1, 1000, 120, area, new Random(2), 1);
        Expect(edge.X <= area.Right - 120 && edge.X < 1900, "화면 끝이면 뒤쪽에 돋는다");
        Expect(!edge.Lost(GiveUpSec - 1) && edge.Lost(GiveUpSec + 1) && edge.State == Phase.None, "30분 넘게 못 주우면 없어진다");
        var saved = new Clover();
        saved.Load(new Saved("Worn", 0, 0, "d", "sprout"));
        Expect(saved.State == Phase.Worn && saved.Day == "d" && saved.Kind == "sprout", "다시 켜도 머리에 그대로 (새싹은 새싹)");
        saved.Load(new Saved("Worn", 0, 0, "d"));
        Expect(saved.Kind == "clover", "예전 기록(종류 없음)은 네잎클로버");
        int clovers = 0, rngs = 0;
        var pick = new Random(3);
        for (int i = 0; i < 1000; i++)
        {
            var one = new Clover();
            if (one.TrySprout("d" + i, 0, 900, 1, 1000, 120, area, pick, 1)) { rngs++; if (one.Kind == "clover") clovers++; }
        }
        Expect(rngs == 1000 && clovers > 230 && clovers < 370, $"네잎클로버는 30%쯤, 나머지는 새싹 ({clovers}/1000)");
        return $"네잎클로버·새싹: 돌아다닌 지 {minutes}분에 돋음, 하루 한 번, 네잎클로버 {clovers / 10}%, 떨어지면 다시 줍고, 다음 날 시듦";
    }
}
