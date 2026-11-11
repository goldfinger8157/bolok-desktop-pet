namespace Bolok;

// 미니건 (스트레스 풀기): 볼록이가 모니터 맨 아래에서 미니건을 들고 위쪽 창모드 창들을 쏜다. 점수는 없고 Esc 로 끝.
// 왼쪽 누르기: 총신이 잠깐 돌고 나서 탄창 없이 계속 쏜다. 총알이 닿은 자리는 큼직한 칸으로 뜯겨 나가 창이 아래부터 갉아먹힌다.
// 다섯 발에 한 발꼴로 큰 덩어리가 한꺼번에 떨어진다. 한 번씩(시작 때 3분의 1, 하는 동안 20~40초마다) 10초 동안 쌍 미니건.
// 오른쪽 클릭: 수류탄이 포물선으로 날아가 찍은 자리에 도착하는 순간 크게 터진다 (가는 길의 창에는 안 부딪힌다 —
// 아래에서 던지니 창 안쪽을 노려도 늘 창 아랫변에서 터졌다).
// 창은 12px 칸으로 기억한다 (land: 창이던 칸, gone: 파인 칸). 화면과 무관한 규칙이라 --selfcheck 로 검사한다
public sealed class Minigun
{
    public sealed class Bullet { public double X, Y, Vx, Vy; }
    public sealed class Nade { public double X, Y, Vx, Vy, Fuse, TX, TY; }   // T: 찍은 자리, Fuse: 도착까지 남은 시간
    public sealed class Bit { public double X, Y, Vx, Vy, Life; public int Size; public bool Brass, Spark; }   // 파편, 탄피, 불꽃
    public sealed class Blast { public double X, Y, R, At; }

    // 칸과 구멍을 키워 화끈하게 깨진다 (YELL: 4px·반지름 6 → 6px·11 → "깨지는 픽셀을 두배쯤" 12px·15)
    public const int Cell = 12;
    public const double FireRate = 25, BulletSpeed = 2600, SpreadDeg = 2.5, HoleR = 15, ChunkR = 26, ChunkChance = .2, NadeR = 90,
        SpinUpSec = .35, WalkSpeed = 420, Gravity = 1400, NadeCooldown = .5, DualSec = 10, DualStartChance = 1 / 3.0;
    public const int MaxBits = 320;

    public readonly Rectangle Field;
    public readonly List<Rectangle> Targets;
    public readonly int Gw, Gh, K;
    readonly bool[,] land, gone;
    public readonly List<Rectangle> Carved = new();   // 파인 자리 (화면이 그 자리만 다시 칠한다)
    public readonly List<Bullet> Bullets = new();
    public readonly List<Nade> Nades = new();
    public readonly List<Bit> Bits = new();
    public readonly List<Blast> Blasts = new();
    public readonly double Ground;                    // 볼록이 발
    public double X, Spin, Kick, Clock, LastShot = -9, WalkAt = -9, ThrowAt = -9, DualUntil;
    public int Shots;
    public bool Firing;
    public Action<string>? OnEvent;
    public bool Dual => Clock < DualUntil;
    readonly Random rng;
    readonly double sf, cell;
    double shotDebt, nextDual, mx, my;

    public Minigun(Rectangle field, IReadOnlyList<Rectangle> targets, double ground, int k, Random rng, double dualChance = DualStartChance)
    {
        (Field, Ground, K, this.rng) = (field, ground, k, rng);
        sf = k / 3.0;
        cell = 40 * k;
        Targets = targets.Select(t => Rectangle.Intersect(t, field)).Where(t => !t.IsEmpty).ToList();
        (Gw, Gh) = (field.Width / Cell + 1, field.Height / Cell + 1);
        (land, gone) = (new bool[Gw, Gh], new bool[Gw, Gh]);
        foreach (var t in Targets) Mark(t, true);
        (X, mx, my) = (field.Left + field.Width / 2.0, field.Left + field.Width / 2.0, field.Top);
        if (rng.NextDouble() < dualChance) DualUntil = DualSec;
        nextDual = DualUntil + 20 + rng.NextDouble() * 20;
    }

    void Mark(Rectangle t, bool on)
    {
        for (int gx = Math.Max(0, (t.Left - Field.Left + Cell - 1) / Cell); gx < Math.Min(Gw, (t.Right - Field.Left) / Cell); gx++)
            for (int gy = Math.Max(0, (t.Top - Field.Top + Cell - 1) / Cell); gy < Math.Min(Gh, (t.Bottom - Field.Top) / Cell); gy++)
                land[gx, gy] = on;
    }

    // 창이 닫히거나 움직이면 그 창은 대상에서 뺀다 (파인 자리도 지운다)
    public void RemoveTarget(Rectangle t)
    {
        if (!Targets.Remove(t)) return;
        Mark(t, false);
        foreach (var o in Targets) Mark(o, true);
        Carved.Add(t);
    }

    public bool Land(int gx, int gy) => gx >= 0 && gy >= 0 && gx < Gw && gy < Gh && land[gx, gy];
    public bool Gone(int gx, int gy) => Land(gx, gy) && gone[gx, gy];
    public bool Solid(double x, double y)
    {
        int gx = (int)Math.Floor((x - Field.Left) / Cell), gy = (int)Math.Floor((y - Field.Top) / Cell);
        return Land(gx, gy) && !gone[gx, gy];
    }
    public int GoneCount() { int n = 0; for (int x = 0; x < Gw; x++) for (int y = 0; y < Gh; y++) if (land[x, y] && gone[x, y]) n++; return n; }

    // 총: 볼록이 몸 가운데쯤 쥐고(쌍이면 양손), 저마다 마우스를 겨눈다 (아래로는 못 겨눈다). 그림은 볼록이 배율의 5/3 배, 손잡이에서 총구까지 29칸
    public int GunK => Math.Max(2, (int)Math.Round(K * 5 / 3.0));
    public IEnumerable<(PointF Pivot, double Aim)> Guns()
    {
        double y = Ground - cell * .38;
        foreach (double dx in Dual ? new[] { -cell * .32, cell * .32 } : new[] { 0.0 })
        {
            var p = new PointF((float)(X + dx), (float)y);
            double a = Math.Clamp(Math.Atan2(my - p.Y, mx - p.X), -Math.PI + .12, -.12);
            if (my > p.Y) a = mx >= p.X ? -.12 : -Math.PI + .12;
            yield return (p, a);
        }
    }
    public PointF Pivot => new((float)X, (float)(Ground - cell * .38));
    public double Aim => Guns().First().Aim;
    PointF MuzzleOf(PointF p, double a) => new((float)(p.X + Math.Cos(a) * 29 * GunK), (float)(p.Y + Math.Sin(a) * 29 * GunK));

    public void Carve(double x, double y, double r)
    {
        int x0 = Math.Max(0, (int)((x - r - Field.Left) / Cell)), x1 = Math.Min(Gw - 1, (int)((x + r - Field.Left) / Cell));
        int y0 = Math.Max(0, (int)((y - r - Field.Top) / Cell)), y1 = Math.Min(Gh - 1, (int)((y + r - Field.Top) / Cell));
        bool any = false;
        for (int gx = x0; gx <= x1; gx++)
            for (int gy = y0; gy <= y1; gy++)
            {
                if (!land[gx, gy] || gone[gx, gy]) continue;
                double cx = Field.Left + (gx + .5) * Cell - x, cy = Field.Top + (gy + .5) * Cell - y;
                if (cx * cx + cy * cy <= r * r) gone[gx, gy] = any = true;
            }
        if (any) Carved.Add(Rectangle.FromLTRB(Field.Left + x0 * Cell - Cell, Field.Top + y0 * Cell - Cell, Field.Left + (x1 + 2) * Cell, Field.Top + (y1 + 2) * Cell));
    }

    public void StartDual(double sec = DualSec)
    {
        DualUntil = Clock + sec;
        nextDual = DualUntil + 20 + rng.NextDouble() * 20;
        OnEvent?.Invoke("dual");
    }

    // 수류탄: 마우스 자리에 떨어지는 포물선. 멀수록 오래(0.5~1.2초) 높이 날아간다 (YELL: 마우스 쪽으로만 던지니 위쪽 창까지 못 갔다)
    public static double FlightSec(double dist, double sf) => Math.Clamp(.45 + dist / (1800 * sf), .5, 1.2);
    public void Throw(double tx, double ty)
    {
        if (Clock - ThrowAt < NadeCooldown) return;
        ThrowAt = Clock;
        var p = Pivot;
        ty = Math.Min(ty, Ground - 10); // 바닥 아래는 못 찍는다
        double dx = tx - p.X, dy = ty - p.Y, t = FlightSec(Math.Sqrt(dx * dx + dy * dy), sf), g = Gravity * sf;
        Nades.Add(new Nade { X = p.X, Y = p.Y, Vx = dx / t, Vy = dy / t - .5 * g * t, Fuse = t, TX = tx, TY = ty });
        OnEvent?.Invoke("throw");
    }

    // 볼록이는 마우스 X 쪽으로 걷고, 총은 마우스를 겨눈다
    public void Update(double dt, double mouseX, double mouseY, bool fire)
    {
        Clock += dt;
        (mx, my) = (mouseX, mouseY);
        if (Clock >= nextDual) StartDual();
        double to = Math.Clamp(mx, Field.Left + cell * .6, Field.Right - cell * .6), step = WalkSpeed * sf * dt;
        if (Math.Abs(to - X) > 2) { X += Math.Clamp(to - X, -step, step); WalkAt = Clock; }

        // 총신이 돌아야 쏜다. 놓으면 천천히 멈춘다. 쌍이면 총마다 한 발씩
        Firing = fire;
        Spin = Math.Clamp(Spin + (fire ? dt / SpinUpSec : -dt / (SpinUpSec * 2)), 0, 1);
        Kick = Math.Max(0, Kick - dt * 12);
        if (fire && Spin >= 1)
        {
            shotDebt += FireRate * dt;
            for (; shotDebt >= 1; shotDebt--)
                foreach (var (p, a) in Guns().ToList()) Shoot(p, a);
        }
        else shotDebt = 0;

        foreach (var b in Bullets.ToList()) // 총알: 빠르니 칸의 절반씩 나눠 움직인다
        {
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy) * dt / (Cell / 2.0)));
            for (int i = 0; i < steps; i++)
            {
                b.X += b.Vx * dt / steps;
                b.Y += b.Vy * dt / steps;
                if (Solid(b.X, b.Y))
                {
                    bool chunk = rng.NextDouble() < ChunkChance; // 큰 덩어리가 한꺼번에
                    Carve(b.X, b.Y, (chunk ? ChunkR : HoleR) * sf);
                    Debris(b.X, b.Y, chunk ? 10 : 4, chunk);
                    Spark(b.X, b.Y);
                    Bullets.Remove(b);
                    OnEvent?.Invoke(chunk ? "chunk" : "hit");
                    break;
                }
                if (b.Y < Field.Top - 20 || b.X < Field.Left - 20 || b.X > Field.Right + 20) { Bullets.Remove(b); break; }
            }
        }

        foreach (var n in Nades.ToList()) // 수류탄: 찍은 자리에 도착하면 터진다 (만일 바닥에 먼저 닿으면 거기서)
        {
            n.Fuse -= dt;
            if (n.Fuse <= 0) { (n.X, n.Y) = (n.TX, n.TY); Explode(n); continue; }
            n.Vy += Gravity * sf * dt;
            n.X += n.Vx * dt;
            n.Y += n.Vy * dt;
            if (n.Y >= Ground) Explode(n);
        }

        foreach (var bit in Bits)
        {
            if (!bit.Spark) bit.Vy += Gravity * sf * dt;
            bit.X += bit.Vx * dt;
            bit.Y += bit.Vy * dt;
            bit.Life -= dt;
        }
        Bits.RemoveAll(bit => bit.Life <= 0 || bit.Y > Ground + 10);
        Blasts.RemoveAll(bl => Clock - bl.At > .6);
    }

    void Shoot(PointF p, double aim)
    {
        var m = MuzzleOf(p, aim);
        double a = aim + (rng.NextDouble() - .5) * 2 * SpreadDeg * Math.PI / 180, v = BulletSpeed * sf;
        Bullets.Add(new Bullet { X = m.X, Y = m.Y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v });
        Shots++;
        LastShot = Clock;
        Kick = 1;
        if (Bits.Count < MaxBits) // 탄피는 총 옆으로 튀어 떨어진다
            Bits.Add(new Bit { X = p.X, Y = p.Y, Vx = -Math.Cos(aim) * 120 * sf + (rng.NextDouble() - .5) * 120 * sf, Vy = -250 * sf * rng.NextDouble(), Life = .6, Size = 2 * K, Brass = true });
        OnEvent?.Invoke("shot");
    }

    // 파편은 칸만 하거나 조금 작은 네모 (덩어리가 터지면 더 크고 많이, 멀리)
    void Debris(double x, double y, int n, bool big)
    {
        for (int i = 0; i < n && Bits.Count < MaxBits; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2, v = (big ? 520 : 260) * sf * (.4 + rng.NextDouble());
            int size = (int)(Cell * (big ? .7 + rng.NextDouble() * .5 : .4 + rng.NextDouble() * .5));
            Bits.Add(new Bit { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v + 60 * sf, Life = big ? 1 : .6, Size = size });
        }
    }

    void Spark(double x, double y)
    {
        if (Bits.Count >= MaxBits) return;
        Bits.Add(new Bit { X = x, Y = y, Life = .07, Size = (int)(Cell * 1.4), Spark = true });
    }

    void Explode(Nade n)
    {
        Nades.Remove(n);
        double r = NadeR * sf;
        Carve(n.X, n.Y, r);
        Blasts.Add(new Blast { X = n.X, Y = n.Y, R = r, At = Clock });
        Debris(n.X, n.Y, 50, true);
        OnEvent?.Invoke("boom");
    }
}
