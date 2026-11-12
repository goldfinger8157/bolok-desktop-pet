namespace Bolok;

// 볼록이 부대: 화면 가운데 세로 띠에서 볼록이가 좌우로만 움직이며 위로 자동으로 쏜다 (라스트 워 광고의 러너 장면을 볼록이식으로, YELL 2026-11-12).
// 위에서 「화면 버그」 떼가 내려오고, 「+N」「×2」 문을 지나면 도감 친구가 부대에 들어오고, 선물 상자를 쏴서 0 을 만들면 무기가 세진다
// (콩알탄 → 미니건 → + 수류탄 → + 헬기 친구 → 그 뒤 상자는 ICBM 한 방). 끝에는 거대 버그 보스.
// 버그에 닿으면 부대원이 빠지고, 다 빠지거나 보스가 내려오면 진다. 글자 없이 숫자·기호만. 화면과 무관한 규칙이라 --selfcheck 로 검사한다
public sealed class Squad
{
    public enum Phase { Play, Won, Lost, Done }
    public sealed class Bug { public double X, Y, Hit = -9; public int Hp, Max; public bool Boss; }
    public sealed class Gate { public double X, Y, W; public int Add, Mul = 1; public bool Used; }
    public sealed class Box { public double X, Y, Hit = -9; public int Hp, Max; }
    public sealed class Shot { public double X, Y, Life; public int Dmg; public bool Nade; }
    public sealed class Pop { public double X, Y, At, R; }

    // 배율 3 기준. 한 판은 보스 전까지 34줄(약 50초) + 보스
    public const double Speed = 95, MoveSpeed = 1100, RowSec = 1.5, ShotSpeed = 1000, EndSec = 2.5, NadeSec = 1.4, HeliSec = .15, NadeR = 60;
    public const int MaxMembers = 60, Rows = 34, MaxWeapon = 3, MaxShots = 300, MaxBugs = 250;
    public static readonly double[] FireSec = { .32, .12, .12, .12 }; // 무기 단계별 쏘는 간격

    public readonly Rectangle Field;
    public readonly double Ground;
    public readonly int K, Level;
    public readonly List<Bug> Bugs = new();
    public readonly List<Gate> Gates = new();
    public readonly List<Box> Boxes = new();
    public readonly List<Shot> Shots = new();
    public readonly List<Pop> Pops = new();
    public double X, TargetX, Clock, IcbmAt = -9;
    public int Members = 1, Weapon, Kills, Volleys;
    public Phase Stage = Phase.Play;
    public Bug? Boss;
    public Action<string>? OnEvent;
    public bool Done => Stage == Phase.Done;

    readonly Random rng;
    readonly double sf;
    readonly bool script;
    int row, lastGate;
    double rowAt, fireAt, nadeAt, heliAt, endAt;

    public Squad(Rectangle field, double ground, int k, Random rng, int level = 1, bool script = true)
    {
        (Field, Ground, K, this.rng, Level, this.script) = (field, ground, k, rng, Math.Max(1, level), script);
        sf = k / 3.0;
        X = TargetX = field.Left + field.Width / 2.0;
        rowAt = 1;
    }

    // 크기들 (그리는 쪽과 같게): 작은 버그 그림 반지름 7칸 × BugScale, 보스 15칸 × (K+1), 선물 상자 20칸 × K
    public int BugScale => Math.Max(2, K - 1);
    public double BugR => 7 * BugScale;
    public double BossR => 15 * (K + 1);
    public double BoxHalf => 10 * K;
    public double GateH => 9 * K;
    public double LaneW => Field.Width / 2.0;
    public double Lane(int i) => Field.Left + LaneW * (i + .5);
    double Fall => Speed * sf * (1 + (Level - 1) * .04);

    // 부대 모양: 볼록이(대장)는 바닥 가운데, 부대원은 그 위로 일곱 명씩 줄을 선다
    public double Space => 8 * K;
    public int RowsOfMembers => (Members - 1 + 6) / 7;
    public double SquadHalf => (Math.Min(Math.Max(Members - 1, 1), 7) - 1) * Space / 2 + 7 * K;
    public double SquadTop => Ground - 40 * K * .55 - RowsOfMembers * 7 * K - 6 * K;
    public double HeliX => Math.Clamp(X + SquadHalf + 18 * K, Field.Left + 12 * K, Field.Right - 12 * K);

    public IEnumerable<(double X, double Y)> Formation()
    {
        for (int i = 0; i < Members - 1; i++)
        {
            int r = i / 7, inRow = Math.Min(7, Members - 1 - r * 7), c = i % 7;
            yield return (X + (c - (inRow - 1) / 2.0) * Space, Ground - 40 * K * .55 - r * 7 * K);
        }
    }

    public void SetTarget(double x) => TargetX = Math.Clamp(x, Field.Left + SquadHalf, Field.Right - SquadHalf);

    public void Update(double dt)
    {
        Clock += dt;
        Pops.RemoveAll(p => Clock - p.At > .5);
        if (Stage is Phase.Won or Phase.Lost)
        {
            if (Clock >= endAt) Stage = Phase.Done;
            MoveThings(dt);
            return;
        }
        if (Stage != Phase.Play) return;

        // 움직이기: 목표 x 로 빠르게 (마우스·키보드·봇)
        TargetX = Math.Clamp(TargetX, Field.Left + SquadHalf, Field.Right - SquadHalf);
        double step = MoveSpeed * sf * dt;
        X += Math.Clamp(TargetX - X, -step, step);

        if (script) Script();
        MoveThings(dt);
        Fire();
        Collide();
        if (Stage == Phase.Play && Members <= 0) Lose();
    }

    // 줄마다 버그 떼, 네 줄에 한 번은 문(한 차선) + 선물 상자(다른 차선). 다 나오면 보스
    void Script()
    {
        if (Clock < rowAt) return;
        rowAt = Clock + RowSec * Math.Max(.7, 1 - (Level - 1) * .02);
        double top = Field.Top - 40 * K;
        if (row >= Rows)
        {
            if (Boss == null && row == Rows)
            {
                int hp = 800 + Level * 400;
                Boss = new Bug { X = Field.Left + Field.Width / 2.0, Y = Field.Top - BossR, Hp = hp, Max = hp, Boss = true };
                OnEvent?.Invoke("boss");
                row++;
            }
            return;
        }
        if (row < 2 || row % 3 == 2) // 처음 두 줄은 문부터 (혼자서는 버그를 못 버틴다), 그 뒤 세 줄에 한 번
        {
            int lane = row == 1 ? 1 - lastGate : rng.Next(2);
            lastGate = lane;
            var gate = new Gate { X = Lane(lane), Y = top, W = LaneW - 10 * K };
            if (row > 5 && rng.NextDouble() < .3) gate.Mul = 2;
            else gate.Add = 3 + rng.Next(Math.Min(6, 3 + row / 6)); // +3 ~ +8: 부대가 금방 불어나는 맛
            Gates.Add(gate);
            if (row != 1)
            {
                int hp = 10 + Level * 5 + row * 4;
                if (Weapon >= MaxWeapon) hp = hp * 5 / 2; // 무기를 다 모은 뒤 상자(ICBM)는 단단하게: 드물게 터진다
                Boxes.Add(new Box { X = Lane(1 - lane), Y = top, Hp = hp, Max = hp });
            }
        }
        else
        {
            int n = Math.Min(16, 2 + Level / 3 + row / 3), hp = 1 + (Level - 1) / 4 + row / 14;
            bool both = rng.NextDouble() < .35;
            int lane = rng.Next(2), cols = Math.Min(4, Math.Max(2, n / 3));
            for (int i = 0; i < n; i++)
            {
                int l = both ? i % 2 : lane, j = both ? i / 2 : i;
                double x = Lane(l) + (j % cols - (cols - 1) / 2.0) * BugR * 2.4 + (rng.NextDouble() - .5) * 4 * K;
                double y = top - j / cols * BugR * 2.2;
                if (Bugs.Count < MaxBugs) Bugs.Add(new Bug { X = x, Y = y, Hp = hp, Max = hp });
            }
        }
        row++;
    }

    void MoveThings(double dt)
    {
        double v = Fall * dt;
        foreach (var bg in Bugs) bg.Y += v;
        foreach (var gt in Gates) gt.Y += v;
        foreach (var bx in Boxes) bx.Y += v;
        if (Boss != null && Stage == Phase.Play)
        {
            Boss.Y += v * .45;
            Boss.X = Field.Left + Field.Width / 2.0 + Math.Sin(Clock * .7) * (Field.Width / 2.0 - BossR); // 좌우로 천천히 흔들린다: 따라가며 쏜다
        }
        foreach (var s in Shots) { s.Y -= ShotSpeed * sf * dt; s.Life -= dt; }
        Shots.RemoveAll(s => s.Y < Field.Top - 20 || s.Life <= 0 && !s.Nade);
        foreach (var s in Shots.Where(s => s.Nade && s.Life <= 0).ToList()) Explode(s); // 수류탄은 날다가 터진다
        Bugs.RemoveAll(bg => bg.Y - BugR > Field.Bottom);
        Gates.RemoveAll(gt => gt.Y > Field.Bottom + 20);
        Boxes.RemoveAll(bx => bx.Y - BoxHalf > Field.Bottom);
    }

    // 부대 전체가 한 번에 쏜다: 탄 수는 여덟 개까지, 피해는 부대원 수만큼 나눠 싣는다
    void Fire()
    {
        if (Clock >= fireAt && Shots.Count < MaxShots)
        {
            fireAt = Clock + FireSec[Math.Min(Weapon, MaxWeapon)];
            int n = Math.Clamp(Members, 3, 8); // 혼자여도 세 갈래로 (옆 칸 버그도 맞게)
            double spread = Math.Max(SquadHalf - 6 * K, 10 * K);
            for (int i = 0; i < n; i++)
                Shots.Add(new Shot { X = X - spread + spread * 2 * i / (n - 1), Y = SquadTop, Dmg = Math.Max(1, Members / n + (i < Members % n ? 1 : 0)), Life = 3 });
            Volleys++;
            OnEvent?.Invoke("fire");
        }
        if (Weapon >= 2 && Clock >= nadeAt)
        {
            nadeAt = Clock + NadeSec;
            Shots.Add(new Shot { X = X, Y = SquadTop, Dmg = 3 + Members / 3, Nade = true, Life = .55 });
        }
        if (Weapon >= 3 && Clock >= heliAt)
        {
            heliAt = Clock + HeliSec;
            Shots.Add(new Shot { X = HeliX, Y = SquadTop - 10 * K, Dmg = 1 + Members / 8, Life = 3 });
        }
    }

    void Explode(Shot s)
    {
        Shots.Remove(s);
        double r = NadeR * sf;
        Pops.Add(new Pop { X = s.X, Y = s.Y, At = Clock, R = r });
        foreach (var bg in Bugs.Where(b => Dist(b.X, b.Y, s.X, s.Y) < r + BugR).ToList()) Hurt(bg, s.Dmg);
        if (Boss != null && Dist(Boss.X, Boss.Y, s.X, s.Y) < r + BossR) Hurt(Boss, s.Dmg);
        OnEvent?.Invoke("boom");
    }

    static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    void Hurt(Bug bg, int dmg)
    {
        bg.Hp -= dmg;
        bg.Hit = Clock;
        if (bg.Hp > 0) return;
        Kills++;
        if (bg.Boss) { Pops.Add(new Pop { X = bg.X, Y = bg.Y, At = Clock, R = BossR }); Boss = null; Win(); return; }
        Bugs.Remove(bg);
        Pops.Add(new Pop { X = bg.X, Y = bg.Y, At = Clock, R = BugR * 1.4 });
    }

    void Collide()
    {
        foreach (var s in Shots.ToList())
        {
            if (Boss is { } boss && Math.Abs(s.X - boss.X) < BossR && Math.Abs(s.Y - boss.Y) < BossR * .9) { Hit(s, () => Hurt(boss, s.Dmg)); continue; }
            var bg = Bugs.FirstOrDefault(b => Math.Abs(b.X - s.X) < BugR && Math.Abs(b.Y - s.Y) < BugR);
            if (bg != null) { Hit(s, () => Hurt(bg, s.Dmg)); continue; }
            var bx = Boxes.FirstOrDefault(b => Math.Abs(b.X - s.X) < BoxHalf && s.Y < b.Y + BoxHalf && s.Y > b.Y - BoxHalf);
            if (bx != null) Hit(s, () => Open(bx, s.Dmg));
        }
        if (Stage != Phase.Play) return;

        double top = SquadTop, half = SquadHalf;
        foreach (var gt in Gates) // 문: 부대 앞줄이 닿으면 한 번만
            if (!gt.Used && gt.Y + GateH / 2 >= top && gt.Y - GateH / 2 <= Ground && Math.Abs(X - gt.X) < gt.W / 2)
            {
                gt.Used = true;
                int before = Members;
                Members = Math.Min(MaxMembers, gt.Mul > 1 ? Members * gt.Mul : Members + gt.Add);
                Pops.Add(new Pop { X = gt.X, Y = gt.Y, At = Clock, R = 16 * K });
                OnEvent?.Invoke("join:" + (Members - before));
            }
        foreach (var bg in Bugs.ToList()) // 버그가 부대에 닿으면 그 버그 체력만큼 부대원이 빠진다
            if (bg.Y + BugR >= top && bg.Y - BugR <= Ground && Math.Abs(bg.X - X) < half + BugR * .3)
            {
                Members -= Math.Max(1, bg.Hp);
                Bugs.Remove(bg);
                Pops.Add(new Pop { X = bg.X, Y = bg.Y, At = Clock, R = BugR * 1.4 });
                OnEvent?.Invoke("hurt");
                if (Members <= 0) break;
            }
        if (Boss != null && Boss.Y + BossR * .7 >= top) { Members = 0; Lose(); } // 보스가 부대까지 내려왔다
    }

    void Hit(Shot s, Action effect)
    {
        if (s.Nade) { Explode(s); return; }
        Shots.Remove(s);
        effect();
    }

    // 선물 상자: 0 이 되면 무기가 한 단계. 헬기까지 다 모은 뒤엔 ICBM 한 방 (보스 빼고 버그 싹, 보스는 4분의 1)
    void Open(Box bx, int dmg)
    {
        bx.Hp -= dmg;
        bx.Hit = Clock;
        if (bx.Hp > 0) return;
        Boxes.Remove(bx);
        Pops.Add(new Pop { X = bx.X, Y = bx.Y, At = Clock, R = BoxHalf * 1.5 });
        if (Weapon < MaxWeapon) { Weapon++; OnEvent?.Invoke("upgrade:" + Weapon); return; }
        IcbmAt = Clock;
        foreach (var bg in Bugs) Pops.Add(new Pop { X = bg.X, Y = bg.Y, At = Clock, R = BugR * 1.4 });
        Kills += Bugs.Count;
        Bugs.Clear();
        if (Boss != null) Hurt(Boss, Math.Max(1, Boss.Max / 4));
        OnEvent?.Invoke("icbm");
    }

    void Win() { Stage = Phase.Won; endAt = Clock + EndSec; OnEvent?.Invoke("won"); }
    void Lose() { Members = 0; Stage = Phase.Lost; endAt = Clock + EndSec; OnEvent?.Invoke("lost"); }

    // 시험·디버그용 자동 조종: 차선마다 「얻을 것 − 위험」 점수를 매겨 고른다
    //   얻을 것: 다가오는 문으로 늘 부대원, 무기가 덜 셌을 때 선물 상자. 위험: 3초 안에 못 잡을 버그 체력
    public double BotTarget()
    {
        double top = SquadTop, look = 560 * sf, dps = Math.Max(3, Members) / FireSec[Math.Min(Weapon, MaxWeapon)];
        double Score(int lane, out double x)
        {
            x = Lane(lane);
            double gain = 0;
            var gate = Gates.Where(g => !g.Used && g.Y < top && g.Y > top - look && Math.Abs(g.X - Lane(lane)) < LaneW / 2).OrderByDescending(g => g.Y).FirstOrDefault();
            if (gate != null) { gain += gate.Mul > 1 ? Members * (gate.Mul - 1) : gate.Add; x = gate.X; }
            if (Weapon <= MaxWeapon && Boxes.Any(b => b.Y < top && b.Y > top - look && Math.Abs(b.X - Lane(lane)) < LaneW / 2)) gain += 1;
            double hp = Bugs.Where(b => b.Y < top + BugR && b.Y > top - look * .6 && Math.Abs(b.X - Lane(lane)) < LaneW / 2).Sum(b => b.Hp);
            double risk = Math.Max(0, hp - dps * 3);
            return gain * 2 - risk * 1.5 - (Math.Abs(x - X) > LaneW * .6 ? .5 : 0);
        }
        if (Boss != null && Bugs.Count(b => b.Y > top - look * .4) < 3) return Boss.X; // 보스가 나오면 그 밑으로
        double s0 = Score(0, out double x0), s1 = Score(1, out double x1);
        return s0 >= s1 ? x0 : x1;
    }
}
