namespace Bolok;

// 포트리스 (웜즈·포트리스를 볼록이 식으로): 창모드 창들이 섬이 되고, 볼록이와 라이벌 햄스터가 번갈아 대포를 쏜다.
// 대포마다 체력이 다르다 (멀티캐논 9발/200, 왕대포/300, 일반대포/400). 바람은 턴마다 바뀌고 모든 포탄에 분다.
// 폭발하면 땅(창 전체)이 깎이고, 다 뚫리면 떨어져 화면 아래로 퐁당. 라이벌은 바람을 어림하고 흔들려 쏘다가 조금씩 좁혀 온다.
// 화면과 무관한 규칙이라 --selfcheck 로 검사한다
public sealed class Worms
{
    // 대포: 한 번에 몇 발, 퍼짐(도), 폭발 반지름, 최대 피해, 그 대포를 고른 햄스터의 체력
    public sealed record Gun(string Name, string KoLabel, int Hp, int Shells, double Spread, double Radius, int Damage)
    {
        public string Label => L.T("gun." + Name); // 언어별 이름 (KoLabel 은 기록용)
    }
    public static readonly Gun[] Guns =
    {
        new("multi", "멀티캐논", 200, 9, 3, 28, 22),   // 체력은 처음 정한 것의 두 배 (YELL: 판이 너무 빨리 끝났다)
        new("giant", "왕대포", 300, 1, 0, 85, 90),
        new("normal", "일반대포", 400, 1, 0, 50, 60),
    };
    public static Gun GunOf(string? name) => Guns.FirstOrDefault(g => g.Name == name) ?? Guns[2];

    public sealed class Hog
    {
        public double X, Y, Vx, Vy, HurtUntil, WalkAt = -9;   // X,Y: 발
        public int Hp, Dir = 1;
        public bool Air, Out;
        public Gun Gun = Guns[2];
        public bool Dead => Hp <= 0 || Out;
    }
    public sealed class Shell { public string Kind = ""; public double X, Y, Vx, Vy, R, Damage; }
    public sealed class Blast { public double X, Y, R, At; }
    public enum Phase { Turn, Flying, Settle, Won, Lost }

    public const int Cell = 4;          // 땅 칸 (px)
    public const double Gravity = 1100, MaxShot = 1250, WindAccel = 260, TurnSec = 25, ChargeSec = 1.2, WalkSpeed = 90;

    public readonly Rectangle Field;
    public readonly List<Rectangle> Lands;
    readonly bool[,] solid;
    readonly int gw, gh;
    public readonly Hog Me = new(), Cpu = new() { Dir = -1 };
    public readonly List<Shell> Shells = new();
    public readonly List<Blast> Blasts = new();
    public readonly int K;
    public readonly double BodyR;
    public bool MyTurn = true;
    public double Wind, Clock, TurnAt, EndAt, Aim = -Math.PI / 4, CpuAim = -Math.PI * .75, ChargeFrom = -1;
    public Phase Stage = Phase.Turn;
    public Action<string>? OnEvent;
    public Hog Active => MyTurn ? Me : Cpu;
    public bool Done => Stage is Phase.Won or Phase.Lost && Clock - EndAt > 2.5;
    public double Power => ChargeFrom < 0 ? 0 : Math.Min(1, (Clock - ChargeFrom) / ChargeSec);
    public double TimeLeft => Math.Max(0, TurnSec - (Clock - TurnAt));
    readonly Random rng;
    readonly double sf;
    double settleFor, cpuAt;
    int cpuStep;
    (double A, double P) cpuPlan;

    // 내 대포를 고르면 라이벌은 셋 중 아무거나 (cpuGun 으로 정할 수도 있다)
    public Worms(Rectangle field, IReadOnlyList<Rectangle> lands, int k, Random rng, string? myGun = null, string? cpuGun = null)
    {
        (Field, K, this.rng) = (field, k, rng);
        sf = k / 3.0;
        BodyR = 40 * k * .22;
        (Me.Gun, Cpu.Gun) = (GunOf(myGun), cpuGun != null ? GunOf(cpuGun) : Guns[rng.Next(Guns.Length)]);
        (Me.Hp, Cpu.Hp) = (Me.Gun.Hp, Cpu.Gun.Hp);
        // 땅은 창 전체 (YELL: "바닥은 창 높이로 해도 좋아"). 끝까지 파면 떨어진다
        Lands = lands.Select(l => Rectangle.Intersect(l, field)).Where(l => !l.IsEmpty).OrderBy(l => l.Left).ToList();
        (gw, gh) = (field.Width / Cell + 1, field.Height / Cell + 1);
        solid = new bool[gw, gh];
        foreach (var l in Lands) // 창 안쪽 칸만 땅 (윗변을 올림해서 창보다 높이 서지 않게)
            for (int gx = (l.Left - field.Left + Cell - 1) / Cell; gx < (l.Right - field.Left) / Cell; gx++)
                for (int gy = (l.Top - field.Top + Cell - 1) / Cell; gy < (l.Bottom - field.Top) / Cell; gy++) solid[gx, gy] = true;
        // 볼록이는 왼쪽 섬, 라이벌은 오른쪽 섬 (섬이 하나면 양 끝)
        var a = Lands[0];
        var b = Lands[^1];
        Place(Me, a.Left + a.Width * .2);
        Place(Cpu, b.Left + b.Width * .8);
        NewTurn(true);
    }

    void Place(Hog h, double x) => (h.X, h.Y) = (x, Floor(x, Field.Top, Field.Height) ?? Field.Bottom);

    public bool Solid(double x, double y)
    {
        int gx = (int)Math.Floor((x - Field.Left) / Cell), gy = (int)Math.Floor((y - Field.Top) / Cell);
        return gx >= 0 && gy >= 0 && gx < gw && gy < gh && solid[gx, gy];
    }

    // x 에서 y-up ~ y+down 사이에 설 수 있는 가장 높은 자리 (발 아래가 땅, 발 자리는 빈 곳)
    public double? Floor(double x, double y, double up, double down = 0)
    {
        for (double yy = Math.Floor(y - up); yy <= y + Math.Max(down, up); yy++)
            if (!Solid(x, yy) && Solid(x, yy + 1)) return yy;
        return null;
    }

    bool Standing(Hog h) => Solid(h.X, h.Y + 1) || Solid(h.X - 4 * sf, h.Y + 1) || Solid(h.X + 4 * sf, h.Y + 1);
    public double CenterY(Hog h) => h.Y - BodyR;

    void Carve(double x, double y, double r)
    {
        for (int gx = (int)((x - r - Field.Left) / Cell); gx <= (int)((x + r - Field.Left) / Cell); gx++)
            for (int gy = (int)((y - r - Field.Top) / Cell); gy <= (int)((y + r - Field.Top) / Cell); gy++)
            {
                if (gx < 0 || gy < 0 || gx >= gw || gy >= gh) continue;
                double cx = Field.Left + (gx + .5) * Cell - x, cy = Field.Top + (gy + .5) * Cell - y;
                if (cx * cx + cy * cy <= r * r) solid[gx, gy] = false;
            }
    }

    // ── 움직이기 ──
    public void Walk(Hog h, int dir, double dt)
    {
        if (h.Air || h.Dead) return;
        h.Dir = dir;
        h.WalkAt = Clock;
        double nx = Math.Clamp(h.X + dir * WalkSpeed * sf * dt, Field.Left + BodyR, Field.Right - BodyR);
        if (Solid(nx, h.Y - BodyR * 1.5)) return; // 벽
        if (Floor(nx, h.Y, 10 * sf, 10 * sf) is { } f) (h.X, h.Y) = (nx, f);
        else h.X = nx; // 낭떠러지: 다음 프레임부터 떨어진다
    }

    public void Jump(Hog h)
    {
        if (h.Air || h.Dead) return;
        (h.Air, h.Vx, h.Vy) = (true, h.Dir * 170 * sf, -430 * sf);
        h.Y -= 1;
        OnEvent?.Invoke("jump");
    }

    void Fall(Hog h, double dt)
    {
        if (h.Out) return;
        if (!h.Air && !Standing(h)) (h.Air, h.Vx, h.Vy) = (true, 0, 0);
        if (!h.Air) return;
        h.Vy += Gravity * sf * dt;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(h.Vx * h.Vx + h.Vy * h.Vy) * dt / 2));
        for (int i = 0; i < steps && h.Air; i++)
        {
            double px = h.X;
            h.X = Math.Clamp(h.X + h.Vx * dt / steps, Field.Left + BodyR, Field.Right - BodyR);
            if (Solid(h.X, h.Y - BodyR)) { h.X = px; h.Vx = -h.Vx * .3; } // 옆 벽에 부딪힘
            h.Y += h.Vy * dt / steps;
            if (h.Vy < 0 && Solid(h.X, h.Y - BodyR * 2)) h.Vy = 0; // 머리
            if (h.Vy >= 0 && Solid(h.X, h.Y))
            {
                if (Floor(h.X, h.Y, 24 * sf) is { } f) h.Y = f;
                (h.Air, h.Vx, h.Vy) = (false, 0, 0);
            }
        }
        if (h.Y > Field.Bottom + 40) { h.Out = true; h.Air = false; OnEvent?.Invoke("splash"); } // 퐁당
    }

    // ── 쏘기 ──
    public void StartCharge() { if (Stage == Phase.Turn && MyTurn && !Me.Air && ChargeFrom < 0) ChargeFrom = Clock; }
    public void ReleaseCharge() { if (ChargeFrom >= 0) Fire(Me, Aim, Power); }

    // 멀티캐논은 9발을 부채꼴로 (가운데가 겨눈 쪽, 3도씩, 힘도 조금씩 다르게)
    public void Fire(Hog h, double angle, double power)
    {
        if (Stage != Phase.Turn) return;
        ChargeFrom = -1;
        var gun = h.Gun;
        for (int i = 0; i < gun.Shells; i++)
        {
            double off = (i - (gun.Shells - 1) / 2.0) * gun.Spread * Math.PI / 180, a = angle + off;
            double v = Math.Max(.08, power) * MaxShot * sf * (1 - Math.Abs(off) * .6);
            Shells.Add(new Shell
            {
                Kind = gun.Name, X = h.X + Math.Cos(angle) * BodyR * 1.5, Y = CenterY(h) + Math.Sin(angle) * BodyR * 1.5,
                Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, R = gun.Radius * sf, Damage = gun.Damage,
            });
        }
        h.Dir = Math.Cos(angle) >= 0 ? 1 : -1;
        Stage = Phase.Flying;
        OnEvent?.Invoke("fire");
    }

    // 포탄 한 걸음: 바람에 밀리고, 땅이나 햄스터에 닿으면 터진다. 터지면 터진 자리, 화면 밖으로 사라지면 NaN, 아직 날면 null
    (double X, double Y)? Fly(Shell s, double dt)
    {
        s.Vx += Wind * WindAccel * sf * dt;
        s.Vy += Gravity * sf * dt;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy) * dt / 3));
        for (int i = 0; i < steps; i++)
        {
            s.X += s.Vx * dt / steps;
            s.Y += s.Vy * dt / steps;
            if (s.X < Field.Left - 50 || s.X > Field.Right + 50 || s.Y > Field.Bottom + 50) return (double.NaN, double.NaN);
            if (Solid(s.X, s.Y) || Near(Me, s.X, s.Y) || Near(Cpu, s.X, s.Y)) return (s.X, s.Y);
        }
        return null;
    }

    bool Near(Hog h, double x, double y) => !h.Out && (x - h.X) * (x - h.X) + (y - CenterY(h)) * (y - CenterY(h)) < BodyR * BodyR;

    public void Explode(double x, double y, double r = 0, double damage = 60)
    {
        if (r <= 0) r = 50 * sf;
        Carve(x, y, r);
        Blasts.Add(new Blast { X = x, Y = y, R = r, At = Clock });
        foreach (var h in new[] { Me, Cpu })
        {
            if (h.Out) continue;
            double dx = h.X - x, dy = CenterY(h) - y, d = Math.Sqrt(dx * dx + dy * dy), reach = r * 1.25;
            if (d >= reach) continue;
            double near = 1 - d / reach;
            h.Hp = Math.Max(0, h.Hp - (int)Math.Round(damage * near));
            (double nx, double ny) = d < 1 ? (0, -1) : (dx / d, dy / d);
            (h.Air, h.Vx, h.Vy, h.HurtUntil) = (true, nx * 520 * sf * near, ny * 520 * sf * near - 180 * sf, Clock + .6); // 날아간다
            h.Y -= 1;
            OnEvent?.Invoke(h == Me ? "hurt:me" : "hurt:cpu");
        }
        OnEvent?.Invoke("boom");
    }

    // ── 라이벌: 제 대포(가운데 발)로 볼록이 근처에 떨어지는 각도·힘을 찾는다. 사람처럼 바람을 어림하고(0.6~1.4배)
    // 각도 ±9도·힘 ±15% 흔들려 쏘다가, 쏠 때마다 흔들림이 12%씩 줄어 절반까지 (YELL: "악당이 너무 잘하는거 아냐") ──
    int cpuShots;
    public (double A, double P) Plan(double miss = 1)
    {
        double realWind = Wind;
        if (miss > 0) Wind *= .6 + rng.NextDouble() * .8;
        var target = (X: Me.X, Y: CenterY(Me));
        (double A, double P, double D) best = (-Math.PI / 2, .5, double.MaxValue);
        for (double deg = -170; deg <= -10; deg += 4)
            for (double p = .3; p <= 1.001; p += .05)
            {
                double a = deg * Math.PI / 180, v = p * MaxShot * sf;
                var s = new Shell { Kind = Cpu.Gun.Name, X = Cpu.X + Math.Cos(a) * BodyR * 1.5, Y = CenterY(Cpu) + Math.Sin(a) * BodyR * 1.5, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v };
                (double X, double Y)? hit = null;
                for (int i = 0; i < 400 && hit == null; i++) hit = Fly(s, 1 / 60.0);
                if (hit is not { } at || double.IsNaN(at.X)) continue;
                double d = Math.Sqrt((at.X - target.X) * (at.X - target.X) + (at.Y - target.Y) * (at.Y - target.Y));
                double self = Math.Sqrt((at.X - Cpu.X) * (at.X - Cpu.X) + (at.Y - CenterY(Cpu)) * (at.Y - CenterY(Cpu)));
                if (self < Cpu.Gun.Radius * sf * 1.6) d += 10000; // 제 발밑은 안 쏜다
                if (d < best.D) best = (a, p, d);
            }
        Wind = realWind;
        double shake = miss * Math.Max(.5, Math.Pow(.88, cpuShots));
        return (best.A + (rng.NextDouble() - .5) * .32 * shake, Math.Clamp(best.P * (1 + (rng.NextDouble() - .5) * .3 * shake), .1, 1));
    }

    void CpuTurn()
    {
        if (Cpu.Air || Clock < cpuAt) return;
        if (cpuStep == 0) // 생각하고 조준한다
        {
            cpuPlan = Plan();
            CpuAim = cpuPlan.A;
            Cpu.Dir = Math.Cos(CpuAim) >= 0 ? 1 : -1;
            (cpuStep, cpuAt) = (1, Clock + .8);
        }
        else { Fire(Cpu, cpuPlan.A, cpuPlan.P); cpuShots++; }
    }

    void NewTurn(bool mine)
    {
        (MyTurn, TurnAt, ChargeFrom, Stage) = (mine, Clock, -1, Phase.Turn);
        Wind = Math.Round((rng.NextDouble() * 2 - 1) * 10) / 10;
        (cpuStep, cpuAt) = (0, Clock + 1.2);
        OnEvent?.Invoke("turn");
    }

    public void Update(double dt)
    {
        Clock += dt;
        Fall(Me, dt);
        Fall(Cpu, dt);
        switch (Stage)
        {
            case Phase.Turn:
                if (!MyTurn) CpuTurn();
                if (MyTurn && ChargeFrom >= 0 && Power >= 1) ReleaseCharge(); // 꽉 차면 저절로 쏜다
                if (Active.Dead || Clock - TurnAt > TurnSec) (Stage, settleFor, ChargeFrom) = (Phase.Settle, 0, -1);
                break;
            case Phase.Flying:
                foreach (var s in Shells.ToList())
                    if (Fly(s, dt) is { } at)
                    {
                        Shells.Remove(s);
                        if (!double.IsNaN(at.X)) Explode(at.X, at.Y, s.R, s.Damage);
                    }
                if (Shells.Count == 0) (Stage, settleFor) = (Phase.Settle, 0);
                break;
            case Phase.Settle: // 다 내려앉으면 판정하고 다음 차례
                settleFor = Me.Air || Cpu.Air ? 0 : settleFor + dt;
                if (settleFor < .6) break;
                if (Me.Dead) { (Stage, EndAt) = (Phase.Lost, Clock); OnEvent?.Invoke("lost"); }
                else if (Cpu.Dead) { (Stage, EndAt) = (Phase.Won, Clock); OnEvent?.Invoke("won"); }
                else NewTurn(!MyTurn);
                break;
        }
    }

    public string PoseOf(Hog h)
    {
        if (h.Dead) return h.Out ? "sad" : "dizzy";
        if (Stage == Phase.Won) return h == Me ? "happy" : "sad";
        if (Stage == Phase.Lost) return h == Me ? "sad" : "happy";
        if (Clock < h.HurtUntil) return "hurt";
        if (h.Air) return "hop";
        if (Clock - h.WalkAt < .15) return "walk";
        if (Active == h && Stage == Phase.Turn && (h == Me ? ChargeFrom >= 0 : cpuStep == 1)) return "angry"; // 힘을 모은다
        return "idle";
    }
}
