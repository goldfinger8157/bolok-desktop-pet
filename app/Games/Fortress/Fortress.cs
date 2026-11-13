namespace Bolok;

// 포트리스 (웜즈·포트리스를 볼록이 식으로): 창모드 창들이 섬이 되고, 볼록이와 라이벌 햄스터가 번갈아 대포를 쏜다.
// 대포마다 체력이 다르다 (멀티캐논 9발/200, 왕대포/300, 일반대포/400). 바람은 턴마다 바뀌고 모든 포탄에 분다.
// 폭발하면 땅(창 전체)이 들쭉날쭉하게 깎이고(Kit/Ragged), 다 뚫리면 떨어져 화면 아래로 퐁당. 피해와 날아가기는 거리(원)로 따진다: 예측할 수 있어야 공정하다.
// 라이벌은 바람을 어림하고 흔들려 쏘다가 조금씩 좁혀 온다. 화면과 무관한 규칙이라 --selfcheck 로 검사한다 (FortressGame.Check)
public sealed class Fortress
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
    public static Gun GunOf(string? name)
    {
        foreach (var g in Guns) if (g.Name == name) return g;
        return Guns[2];
    }

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
    public const double BlastSec = .6;  // 폭발은 이만큼 지나면 목록에서 지운다 (불덩이 그림은 BlastArt.Sec 동안. 깎인 땅은 칸에 남는다)

    public readonly Rectangle Field;
    public readonly List<Rectangle> Lands;
    public readonly int Gw, Gh;         // 땅 칸 수
    readonly bool[] solid, land;        // solid: 지금 땅인 칸, land: 처음에 땅이던 칸 (깎인 자리 = land 이고 solid 아님)
    readonly int fx, fy;                // 판 왼쪽 위 (자주 읽어서 따로 둔다)
    public readonly Hog Me = new(), Cpu = new() { Dir = -1 };
    public readonly List<Shell> Shells = new();
    public readonly List<Blast> Blasts = new();   // 방금 터진 것들 (BlastSec 뒤 지운다). 새 폭발은 늘 맨 뒤에 붙는다
    public int Booms { get; private set; }        // 지금까지 터진 수 (그리는 쪽이 새 폭발을 알아챈다)
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
    public Fortress(Rectangle field, IReadOnlyList<Rectangle> lands, int k, Random rng, string? myGun = null, string? cpuGun = null)
    {
        (Field, K, this.rng) = (field, k, rng);
        (fx, fy) = (field.Left, field.Top);
        sf = k / 3.0;
        BodyR = 40 * k * .22;
        (Me.Gun, Cpu.Gun) = (GunOf(myGun), cpuGun != null ? GunOf(cpuGun) : Guns[rng.Next(Guns.Length)]);
        (Me.Hp, Cpu.Hp) = (Me.Gun.Hp, Cpu.Gun.Hp);
        // 땅은 창 전체 (YELL: "바닥은 창 높이로 해도 좋아"). 끝까지 파면 떨어진다
        Lands = lands.Select(l => Rectangle.Intersect(l, field)).Where(l => !l.IsEmpty).OrderBy(l => l.Left).ToList();
        (Gw, Gh) = (field.Width / Cell + 1, field.Height / Cell + 1);
        (solid, land) = (new bool[Gw * Gh], new bool[Gw * Gh]);
        foreach (var l in Lands) // 창 안쪽 칸만 땅 (윗변을 올림해서 창보다 높이 서지 않게)
            for (int gy = (l.Top - field.Top + Cell - 1) / Cell; gy < (l.Bottom - field.Top) / Cell; gy++)
                for (int gx = (l.Left - field.Left + Cell - 1) / Cell; gx < (l.Right - field.Left) / Cell; gx++) solid[gy * Gw + gx] = land[gy * Gw + gx] = true;
        // 볼록이는 왼쪽 섬, 라이벌은 오른쪽 섬 (섬이 하나면 양 끝)
        var a = Lands[0];
        var b = Lands[^1];
        Place(Me, a.Left + a.Width * .2);
        Place(Cpu, b.Left + b.Width * .8);
        NewTurn(true);
    }

    void Place(Hog h, double x) => (h.X, h.Y) = (x, Floor(x, Field.Top, Field.Height) ?? Field.Bottom);

    public bool SolidCell(int gx, int gy) => (uint)gx < (uint)Gw && (uint)gy < (uint)Gh && solid[gy * Gw + gx];
    public bool LandCell(int gx, int gy) => (uint)gx < (uint)Gw && (uint)gy < (uint)Gh && land[gy * Gw + gx];
    public bool Solid(double x, double y) => SolidCell((int)Math.Floor((x - fx) * (1.0 / Cell)), (int)Math.Floor((y - fy) * (1.0 / Cell)));

    // x 에서 y-up ~ y+down 사이에 설 수 있는 가장 높은 자리 (발 아래가 땅, 발 자리는 빈 곳)
    public double? Floor(double x, double y, double up, double down = 0)
    {
        for (double yy = Math.Floor(y - up); yy <= y + Math.Max(down, up); yy++)
            if (!Solid(x, yy) && Solid(x, yy + 1)) return yy;
        return null;
    }

    bool Standing(Hog h) => Solid(h.X, h.Y + 1) || Solid(h.X - 4 * sf, h.Y + 1) || Solid(h.X + 4 * sf, h.Y + 1);
    public double CenterY(Hog h) => h.Y - BodyR;

    // 들쭉날쭉하게 깎는다: 반지름이 방향마다 0.65~1.35배 (터진 자리가 같으면 늘 같은 모양). 그림(구멍·불덩이)도 같은 모양을 쓴다
    void Carve(double x, double y, double r)
    {
        var shape = Ragged.At(x, y);
        double reach = r * 1.35, sure = r * .65;   // 이보다 멀면 늘 밖, 가까우면 늘 안 (그 사이만 모양을 묻는다)
        int gx0 = Math.Max(0, (int)((x - reach - fx) / Cell)), gx1 = Math.Min(Gw - 1, (int)((x + reach - fx) / Cell));
        int gy0 = Math.Max(0, (int)((y - reach - fy) / Cell)), gy1 = Math.Min(Gh - 1, (int)((y + reach - fy) / Cell));
        for (int gy = gy0; gy <= gy1; gy++)
            for (int gx = gx0; gx <= gx1; gx++)
            {
                int i = gy * Gw + gx;
                if (!solid[i]) continue;
                double cx = fx + (gx + .5) * Cell - x, cy = fy + (gy + .5) * Cell - y, d2 = cx * cx + cy * cy;
                if (d2 < sure * sure || d2 < reach * reach && shape.Inside(cx, cy, r)) solid[i] = false;
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
        if (h.Y > Field.Bottom + 40) { h.Out = true; h.Air = false; OnEvent?.Invoke(h == Me ? "splash:me" : "splash:cpu"); } // 퐁당
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

    // 포탄 한 걸음: 바람에 밀리고, 땅이나 햄스터에 닿으면 터진다. 0 아직 난다 · 1 터졌다(터진 자리는 s.X, s.Y) · 2 화면 밖으로 사라졌다
    // 라이벌 계산이 한 차례에 수십만 번 도는 곳이라 걸음마다 바뀌지 않는 값은 밖에서 한 번만 읽는다
    int Fly(Shell s, double dt, double wind)
    {
        s.Vx += wind * WindAccel * sf * dt;
        s.Vy += Gravity * sf * dt;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy) * dt / 3));
        double left = Field.Left - 50, right = Field.Right + 50, bottom = Field.Bottom + 50, dx = s.Vx * dt / steps, dy = s.Vy * dt / steps, r2 = BodyR * BodyR;
        double meX = Me.X, meY = CenterY(Me), cpuX = Cpu.X, cpuY = CenterY(Cpu), x = s.X, y = s.Y;
        bool me = !Me.Out, cpu = !Cpu.Out;
        int hit = 0;
        for (int i = 0; i < steps; i++)
        {
            x += dx;
            y += dy;
            if (x < left || x > right || y > bottom) { hit = 2; break; }
            int gx = (int)Math.Floor((x - fx) * (1.0 / Cell)), gy = (int)Math.Floor((y - fy) * (1.0 / Cell));
            if ((uint)gx < (uint)Gw && (uint)gy < (uint)Gh && solid[gy * Gw + gx]
                || me && (x - meX) * (x - meX) + (y - meY) * (y - meY) < r2 || cpu && (x - cpuX) * (x - cpuX) + (y - cpuY) * (y - cpuY) < r2) { hit = 1; break; }
        }
        (s.X, s.Y) = (x, y);
        return hit;
    }

    bool Near(Hog h, double x, double y) => !h.Out && (x - h.X) * (x - h.X) + (y - CenterY(h)) * (y - CenterY(h)) < BodyR * BodyR;

    public void Explode(double x, double y, double r = 0, double damage = 60)
    {
        if (r <= 0) r = 50 * sf;
        Carve(x, y, r);
        Blasts.Add(new Blast { X = x, Y = y, R = r, At = Clock });
        Booms++;
        Shake(Me, x, y, r, damage);
        Shake(Cpu, x, y, r, damage);
        OnEvent?.Invoke("boom");
    }

    // 피해와 날아가기는 거리로만 (깎인 모양과 무관하게 동그랗게): 가까울수록 크다
    void Shake(Hog h, double x, double y, double r, double damage)
    {
        if (h.Out) return;
        double dx = h.X - x, dy = CenterY(h) - y, d = Math.Sqrt(dx * dx + dy * dy), reach = r * 1.25;
        if (d >= reach) return;
        double near = 1 - d / reach;
        h.Hp = Math.Max(0, h.Hp - (int)Math.Round(damage * near));
        (double nx, double ny) = d < 1 ? (0, -1) : (dx / d, dy / d);
        (h.Air, h.Vx, h.Vy, h.HurtUntil) = (true, nx * 520 * sf * near, ny * 520 * sf * near - 180 * sf, Clock + .6); // 날아간다
        h.Y -= 1;
        OnEvent?.Invoke(h == Me ? "hurt:me" : "hurt:cpu");
    }

    // ── 라이벌: 제 대포(가운데 발)로 볼록이 근처에 떨어지는 각도·힘을 찾는다. 사람처럼 바람을 어림하고(0.6~1.4배)
    // 각도 ±9도·힘 ±15% 흔들려 쏘다가, 쏠 때마다 흔들림이 12%씩 줄어 절반까지 (YELL: "악당이 너무 잘하는거 아냐") ──
    // 각도 41가지 × 힘 15가지를 다 날려 보는 계산이라 한 번에 하면 한 프레임이 20ms 가까이 걸렸다:
    // 생각하는 동안(차례가 오고 1.2초) 프레임마다 각도 한두 줄씩 나눠서 한다. 그동안 판은 멈춰 있어서 결과는 한 번에 계산한 것과 같다
    const int PlanRows = 41;   // -170° ~ -10°, 4° 씩
    int cpuShots, planRow;
    bool planning;
    double planWind, planX, planY;
    (double A, double P, double D) planBest;
    readonly Shell probe = new();

    void PlanBegin(double miss)
    {
        planWind = miss > 0 ? Wind * (.6 + rng.NextDouble() * .8) : Wind; // 바람 어림
        PlanAgain();
    }

    void PlanAgain() => (planRow, planBest, planX, planY) = (0, (-Math.PI / 2, .5, double.MaxValue), Me.X, CenterY(Me));

    void PlanStep(int rows)
    {
        var s = probe;
        for (; rows > 0 && planRow < PlanRows; rows--, planRow++)
        {
            double deg = -170 + planRow * 4;
            for (double p = .3; p <= 1.001; p += .05)
            {
                double a = deg * Math.PI / 180, v = p * MaxShot * sf;
                (s.X, s.Y, s.Vx, s.Vy) = (Cpu.X + Math.Cos(a) * BodyR * 1.5, CenterY(Cpu) + Math.Sin(a) * BodyR * 1.5, Math.Cos(a) * v, Math.Sin(a) * v);
                int hit = 0;
                for (int i = 0; i < 400 && hit == 0; i++) hit = Fly(s, 1 / 60.0, planWind);
                if (hit != 1) continue;
                double d = Math.Sqrt((s.X - planX) * (s.X - planX) + (s.Y - planY) * (s.Y - planY));
                double self = Math.Sqrt((s.X - Cpu.X) * (s.X - Cpu.X) + (s.Y - CenterY(Cpu)) * (s.Y - CenterY(Cpu)));
                if (self < Cpu.Gun.Radius * sf * 1.6) d += 10000; // 제 발밑은 안 쏜다
                if (d < planBest.D) planBest = (a, p, d);
            }
        }
    }

    (double A, double P) PlanEnd(double miss)
    {
        double shake = miss * Math.Max(.5, Math.Pow(.88, cpuShots));
        return (planBest.A + (rng.NextDouble() - .5) * .32 * shake, Math.Clamp(planBest.P * (1 + (rng.NextDouble() - .5) * .3 * shake), .1, 1));
    }

    // 한 번에 계산하기 (자가 검사·견주기용)
    public (double A, double P) Plan(double miss = 1)
    {
        PlanBegin(miss);
        PlanStep(PlanRows);
        return PlanEnd(miss);
    }

    void CpuTurn(double dt)
    {
        if (Cpu.Air) return;
        if (cpuStep == 0) // 생각하고 조준한다
        {
            if (!planning) { planning = true; PlanBegin(1); }
            PlanStep(Math.Max(1, (int)Math.Ceiling(PlanRows * dt / .9)));
            if (Clock < cpuAt) return;
            if (planX != Me.X || planY != CenterY(Me)) PlanAgain(); // 그사이 볼록이가 움직였으면 처음부터 (보통은 없는 일)
            PlanStep(PlanRows);
            cpuPlan = PlanEnd(1);
            planning = false;
            CpuAim = cpuPlan.A;
            Cpu.Dir = Math.Cos(CpuAim) >= 0 ? 1 : -1;
            (cpuStep, cpuAt) = (1, Clock + .8);
        }
        else if (Clock >= cpuAt) { Fire(Cpu, cpuPlan.A, cpuPlan.P); cpuShots++; }
    }

    void NewTurn(bool mine)
    {
        (MyTurn, TurnAt, ChargeFrom, Stage) = (mine, Clock, -1, Phase.Turn);
        Wind = Math.Round((rng.NextDouble() * 2 - 1) * 10) / 10;
        (cpuStep, cpuAt, planning) = (0, Clock + 1.2, false);
        OnEvent?.Invoke("turn");
    }

    public void Update(double dt)
    {
        Clock += dt;
        for (int i = Blasts.Count - 1; i >= 0; i--) if (Clock - Blasts[i].At > BlastSec) Blasts.RemoveAt(i);
        Fall(Me, dt);
        Fall(Cpu, dt);
        switch (Stage)
        {
            case Phase.Turn:
                if (!MyTurn) CpuTurn(dt);
                if (MyTurn && ChargeFrom >= 0 && Power >= 1) ReleaseCharge(); // 꽉 차면 저절로 쏜다
                if (Active.Dead || Clock - TurnAt > TurnSec) (Stage, settleFor, ChargeFrom) = (Phase.Settle, 0, -1);
                break;
            case Phase.Flying:
                for (int i = 0; i < Shells.Count; i++) // 쏜 차례대로 (지우면 그 자리부터 다시)
                {
                    var s = Shells[i];
                    int hit = Fly(s, dt, Wind);
                    if (hit == 0) continue;
                    Shells.RemoveAt(i--);
                    if (hit == 1) Explode(s.X, s.Y, s.R, s.Damage);
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

    public bool CpuAiming => Stage == Phase.Turn && !MyTurn && cpuStep == 1 && !Cpu.Dead; // 라이벌이 조준을 마치고 힘을 모으는 중

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

    // 시험·디버그·속도 재기용 자동 조종: 볼록이 차례에 라이벌 쪽으로 어림해서 쏠 각도와 힘 (바람·땅은 안 본다. seat 로 조금 흔든다)
    public (double A, double P) BotAim(Random seat)
    {
        double best = double.MaxValue, ba = -Math.PI / 4, bp = .5;
        for (double deg = -170; deg <= -10; deg += 6)
            for (double p = .3; p <= 1; p += .1)
            {
                double ax = Me.X + Math.Cos(deg * Math.PI / 180) * p * MaxShot * .5, d = Math.Abs(ax - Cpu.X);
                if (d < best) (best, ba, bp) = (d, deg * Math.PI / 180, p);
            }
        return (ba + (seat.NextDouble() - .5) * .3, bp);
    }
}
