namespace Bolok;

// 미니건 (스트레스 풀기) 규칙: 볼록이가 모니터 맨 아래에서 미니건을 들고 위쪽 창모드 창들을 쏜다. 점수는 없고 Esc 로 끝.
// 왼쪽 누르기: 총신이 잠깐 돌고 나서 탄창 없이 계속 쏜다. 총알이 닿은 자리는 큼직한 칸으로 뜯겨 나가 창이 아래부터 갉아먹힌다.
// 열 발에 세 발꼴로 큰 덩어리가 한꺼번에 떨어지고 그 둘레에 금이 간다. 한 번씩(시작 때 3분의 1, 하는 동안 20~40초마다) 10초 동안 쌍 미니건.
// 큰 무기 둘: 미사일은 총구에서 곧게 나가며 빨라지다가 처음 닿는 창 칸에서 터진다 (연기 꼬리).
// 수류탄은 포물선으로 날아가 찍은 자리에 도착하는 순간 터진다 (가는 길의 창에는 안 부딪힌다 — 아래에서 던지니 창 안쪽을 노려도 늘 창 아랫변에서 터졌다).
// 구멍은 모두 들쭉날쭉하다 (Kit/Ragged). 창은 칸(6 × 볼록이 배율 px)으로 기억한다: 창이던 칸, 파인 칸, 금 간 자리(반 칸).
// 화면과 무관한 규칙이라 --selfcheck 로 검사한다. 하는 동안 메모리를 새로 잡지 않는다 (총알·파편은 미리 잡아 둔 자리에)
sealed class Minigun
{
    public struct Bullet { public double X, Y, Vx, Vy; }
    // 큰 무기 한 발. 미사일: (Vx, Vy) 는 가는 방향(길이 1), Speed 가 빨라진다. 수류탄: (Vx, Vy) 는 속도, T 는 찍은 자리, Fuse 는 도착까지 남은 시간
    public struct Shell { public double X, Y, Vx, Vy, Speed, Fuse, TX, TY, PuffAt; public bool Missile; }
    public enum BitKind : byte { Chip, Brass, Spark, Smoke }   // 파편, 탄피, 맞은 자리 불꽃, 미사일 연기
    public struct Bit { public double X, Y, Vx, Vy, Life; public int Size; public BitKind Kind; }
    public struct Blast { public double X, Y, R, At; }

    // 칸과 구멍을 키워 화끈하게 깨진다 (YELL: 4px·반지름 6 → 6px·11 → 12px·15 → 2026-11-13 "스트레스 해소용인데 막 깨져야지": 칸 6×배율, 구멍 24·덩어리 40)
    // 반지름·속도는 배율 3 기준이고 배율을 따라 커진다
    public const double FireRate = 25, BulletSpeed = 2600, SpreadDeg = 2.5, HoleR = 24, ChunkR = 40, ChunkChance = .3, NadeR = 100, MissileR = 130,
        MissileStart = 300, MissileTop = 1400, MissileAccel = 2600, SpinUpSec = .35, WalkSpeed = 420, Gravity = 1400, NadeCooldown = .5, MissileCooldown = .8,
        DualSec = 10, DualStartChance = 1 / 3.0;
    public const int MaxBits = 480, HitBits = 6, ChunkBits = 14, BlastBits = 70, BlastRoom = 90, MaxPuffs = 120;

    public readonly Rectangle Field;
    public readonly List<Rectangle> Targets;
    public readonly int Gw, Gh, K, Cell;
    readonly bool[] land, gone;
    readonly byte[] look, crack;    // 칸마다 보이는 모습(0 그대로 · 1 뚫림 · 2 그을린 가장자리)과 금 간 자리(반 칸 넷: 비트 0~3)
    public readonly List<Rectangle> Carved = new();   // 이번 프레임에 모습이 바뀐 자리 (화면이 그 자리만 다시 칠한다)
    public readonly Bag<Bullet> Bullets = new(64);
    public readonly Bag<Shell> Shells = new(8);
    public readonly Bag<Bit> Bits = new(MaxBits);
    public readonly Bag<Bit> Puffs = new(MaxPuffs);
    public readonly Bag<Blast> Blasts = new(8);
    public readonly double Ground;                    // 볼록이 발
    public double X, Spin, Kick, Clock, LastShot = -9, WalkAt = -9, ThrowAt = -9, LaunchAt = -9, DualUntil;
    public int Shots, Booms, GoneCells, CrackCells;
    public bool Firing;
    public Action<string>? OnEvent;
    public bool Dual => Clock < DualUntil;
    readonly Random rng;
    readonly double sf, cell, hold, hand;
    readonly List<(int X, int Y)>[] chunkCracks;
    double shotDebt, nextDual, mx, my;
    int launches;

    // 총알 구멍·덩어리 모양: 미리 뽑아 둔 열여섯 가지에서 고른다 (맞을 때마다 모양을 새로 만들지 않게). 폭발은 터진 자리로 정한다 (Ragged.At)
    static readonly Ragged[] Shapes = Enumerable.Range(0, 16).Select(i => Ragged.Seeded(7919 * (i + 1))).ToArray();
    static readonly Dictionary<int, List<(int X, int Y)>[]> crackCache = new();

    // 총 쥐는 자리: 몸 가운데 높이 (ry + 4)칸px, 쌍이면 몸 양옆 (rx + 2.3)칸px. (rx, ry) 는 그 모습의 몸 반지름 (PetArt.Size)
    public static (double Hold, double Hand) Grip(double rx, double ry, int k) => ((ry + 4) * k, (rx + 2.3) * k);

    public Minigun(Rectangle field, IReadOnlyList<Rectangle> targets, double ground, int k, Random rng, (double Hold, double Hand) grip, double dualChance = DualStartChance)
    {
        (Field, Ground, K, Cell, this.rng) = (field, ground, k, 6 * k, rng);
        (hold, hand) = grip;
        sf = k / 3.0;
        cell = 40 * k;
        Targets = targets.Select(t => Rectangle.Intersect(t, field)).Where(t => !t.IsEmpty).ToList();
        (Gw, Gh) = (field.Width / Cell + 1, field.Height / Cell + 1);
        (land, gone, look, crack) = (new bool[Gw * Gh], new bool[Gw * Gh], new byte[Gw * Gh], new byte[Gw * Gh]);
        foreach (var t in Targets) Mark(t, true);
        (X, mx, my) = (field.Left + field.Width / 2.0, field.Left + field.Width / 2.0, field.Top);
        if (rng.NextDouble() < dualChance) DualUntil = DualSec;
        nextDual = DualUntil + 20 + rng.NextDouble() * 20;
        lock (crackCache) // 덩어리 둘레의 금도 모양마다 한 번만 뽑아 둔다
        {
            if (!crackCache.TryGetValue(k, out var cracks))
                crackCache[k] = cracks = Shapes.Select(s => s.Cracks(ChunkR * sf, ChunkR * sf, Cell / 2)).ToArray();
            chunkCracks = cracks;
        }
    }

    void Mark(Rectangle t, bool on)
    {
        for (int gx = Math.Max(0, (t.Left - Field.Left + Cell - 1) / Cell); gx < Math.Min(Gw, (t.Right - Field.Left) / Cell); gx++)
            for (int gy = Math.Max(0, (t.Top - Field.Top + Cell - 1) / Cell); gy < Math.Min(Gh, (t.Bottom - Field.Top) / Cell); gy++)
            {
                int i = gy * Gw + gx;
                land[i] = on;
                if (!on) { gone[i] = false; look[i] = crack[i] = 0; }
            }
    }

    // 창이 닫히거나 움직이면 그 창은 대상에서 뺀다 (파인 자리·금도 지운다)
    public void RemoveTarget(Rectangle t)
    {
        if (!Targets.Remove(t)) return;
        Mark(t, false);
        foreach (var o in Targets) Mark(o, true);
        (GoneCells, CrackCells) = (0, 0);
        for (int i = 0; i < gone.Length; i++) { if (gone[i]) GoneCells++; CrackCells += System.Numerics.BitOperations.PopCount(crack[i]); }
        Carved.Add(t);
    }

    public bool Land(int gx, int gy) => gx >= 0 && gy >= 0 && gx < Gw && gy < Gh && land[gy * Gw + gx];
    public bool Gone(int gx, int gy) => Land(gx, gy) && gone[gy * Gw + gx];
    public bool Solid(double x, double y)
    {
        int gx = (int)Math.Floor((x - Field.Left) / Cell), gy = (int)Math.Floor((y - Field.Top) / Cell);
        return Land(gx, gy) && !gone[gy * Gw + gx];
    }
    public ReadOnlySpan<byte> Looks => look;        // [gy * Gw + gx]: 0 그대로 · 1 뚫림 · 2 그을린 가장자리 (아직 남은 칸과 맞닿은 파인 칸)
    public ReadOnlySpan<byte> Cracks => crack;      // 금: 반 칸 넷 (비트 0 왼쪽 위 · 1 오른쪽 위 · 2 왼쪽 아래 · 3 오른쪽 아래)

    // 총: 볼록이 몸 가운데 높이에 쥐고(쌍이면 양손), 저마다 마우스를 겨눈다 (아래로는 못 겨눈다). 그림은 볼록이 배율의 5/3 배, 손잡이에서 총구까지 29칸
    public int GunK => Math.Max(2, (int)Math.Round(K * 5 / 3.0));
    public int GunCount => Dual ? 2 : 1;
    public (PointF Pivot, double Aim) Gun(int i)
    {
        var p = new PointF((float)(X + (Dual ? (i == 0 ? -hand : hand) : 0)), (float)(Ground - hold));
        return (p, AimAt(p, mx, my));
    }
    static double AimAt(PointF p, double tx, double ty) =>
        ty > p.Y ? (tx >= p.X ? -.12 : -Math.PI + .12) : Math.Clamp(Math.Atan2(ty - p.Y, tx - p.X), -Math.PI + .12, -.12);
    public PointF Pivot => new((float)X, (float)(Ground - hold));
    public double Aim => Gun(0).Aim;
    PointF MuzzleOf(PointF p, double a) => new((float)(p.X + Math.Cos(a) * 29 * GunK), (float)(p.Y + Math.Sin(a) * 29 * GunK));

    // (x, y) 둘레를 들쭉날쭉한 모양으로 판다. 가운데가 든 칸은 반드시 파인다 (같은 칸에 계속 맞고도 안 파이는 일이 없게)
    public bool Carve(double x, double y, double r, in Ragged shape)
    {
        double reach = r * 1.36;
        int x0 = Math.Max(0, (int)((x - reach - Field.Left) / Cell)), x1 = Math.Min(Gw - 1, (int)((x + reach - Field.Left) / Cell));
        int y0 = Math.Max(0, (int)((y - reach - Field.Top) / Cell)), y1 = Math.Min(Gh - 1, (int)((y + reach - Field.Top) / Cell));
        int hx = (int)Math.Floor((x - Field.Left) / Cell), hy = (int)Math.Floor((y - Field.Top) / Cell);
        int ax = int.MaxValue, ay = int.MaxValue, bx = -1, by = -1;
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0; gx <= x1; gx++)
            {
                int i = gy * Gw + gx;
                if (!land[i] || gone[i]) continue;
                double cx = Field.Left + (gx + .5) * Cell - x, cy = Field.Top + (gy + .5) * Cell - y;
                if (!(gx == hx && gy == hy) && !shape.Inside(cx, cy, r)) continue;
                gone[i] = true;
                GoneCells++;
                if (crack[i] != 0) { CrackCells -= System.Numerics.BitOperations.PopCount(crack[i]); crack[i] = 0; }
                (ax, ay, bx, by) = (Math.Min(ax, gx), Math.Min(ay, gy), Math.Max(bx, gx), Math.Max(by, gy));
            }
        if (bx < 0) return false;
        (ax, ay, bx, by) = (Math.Max(0, ax - 1), Math.Max(0, ay - 1), Math.Min(Gw - 1, bx + 1), Math.Min(Gh - 1, by + 1)); // 맞닿은 칸의 가장자리 색도 바뀐다
        for (int gy = ay; gy <= by; gy++)
            for (int gx = ax; gx <= bx; gx++)
            {
                int i = gy * Gw + gx;
                look[i] = !gone[i] ? (byte)0 : Whole(gx - 1, gy) || Whole(gx + 1, gy) || Whole(gx, gy - 1) || Whole(gx, gy + 1) ? (byte)2 : (byte)1;
            }
        Carved.Add(Rectangle.FromLTRB(Field.Left + ax * Cell, Field.Top + ay * Cell, Field.Left + (bx + 1) * Cell, Field.Top + (by + 1) * Cell));
        return true;
    }

    bool Whole(int gx, int gy) => gx >= 0 && gy >= 0 && gx < Gw && gy < Gh && land[gy * Gw + gx] && !gone[gy * Gw + gx];

    // 금: (x, y) 에서 본 반 칸 자리들(Ragged.Cracks)을 아직 남은 칸에만 새긴다
    void Crack(double x, double y, List<(int X, int Y)> marks)
    {
        int half = Cell / 2, ax = int.MaxValue, ay = int.MaxValue, bx = int.MinValue, by = int.MinValue;
        foreach (var (ox, oy) in marks)
        {
            int hx = (int)Math.Floor((x + ox - Field.Left) / half), hy = (int)Math.Floor((y + oy - Field.Top) / half);
            if (!Whole(hx >> 1, hy >> 1)) continue;
            int i = (hy >> 1) * Gw + (hx >> 1), bit = 1 << ((hx & 1) | (hy & 1) << 1);
            if ((crack[i] & bit) != 0) continue;
            crack[i] |= (byte)bit;
            CrackCells++;
            (ax, ay, bx, by) = (Math.Min(ax, hx), Math.Min(ay, hy), Math.Max(bx, hx), Math.Max(by, hy));
        }
        if (bx >= ax) Carved.Add(Rectangle.FromLTRB(Field.Left + ax * half, Field.Top + ay * half, Field.Left + (bx + 1) * half, Field.Top + (by + 1) * half));
    }

    public void StartDual(double sec = DualSec)
    {
        DualUntil = Clock + sec;
        nextDual = DualUntil + 20 + rng.NextDouble() * 20;
        OnEvent?.Invoke("dual");
    }

    // 큰 무기가 다시 준비된 정도 0~1 (1 이면 쏠 수 있다)
    public double Ready(bool grenade) => Math.Clamp((Clock - (grenade ? ThrowAt : LaunchAt)) / (grenade ? NadeCooldown : MissileCooldown), 0, 1);
    public double BigAt => Math.Max(ThrowAt, LaunchAt);

    // 미사일: 총구에서 (tx, ty) 쪽으로 곧게 나간다 (총과 같이 아래로는 못 쏜다). 쌍이면 양손에서 번갈아
    public bool Launch(double tx, double ty)
    {
        if (Clock - LaunchAt < MissileCooldown) return false;
        LaunchAt = Clock;
        var p = Gun(launches++ % GunCount).Pivot;
        double a = AimAt(p, tx, ty);
        var m = MuzzleOf(p, a);
        Shells.Add() = new Shell { X = m.X, Y = m.Y, Vx = Math.Cos(a), Vy = Math.Sin(a), Speed = MissileStart * sf, Missile = true };
        Kick = 1;
        OnEvent?.Invoke("launch");
        return true;
    }

    // 수류탄: 마우스 자리에 떨어지는 포물선. 멀수록 오래(0.5~1.2초) 높이 날아간다 (YELL: 마우스 쪽으로만 던지니 위쪽 창까지 못 갔다)
    public static double FlightSec(double dist, double sf) => Math.Clamp(.45 + dist / (1800 * sf), .5, 1.2);
    public bool Throw(double tx, double ty)
    {
        if (Clock - ThrowAt < NadeCooldown) return false;
        ThrowAt = Clock;
        var p = Pivot;
        ty = Math.Min(ty, Ground - 10); // 바닥 아래는 못 찍는다
        double dx = tx - p.X, dy = ty - p.Y, t = FlightSec(Math.Sqrt(dx * dx + dy * dy), sf), g = Gravity * sf;
        Shells.Add() = new Shell { X = p.X, Y = p.Y, Vx = dx / t, Vy = dy / t - .5 * g * t, Fuse = t, TX = tx, TY = ty };
        OnEvent?.Invoke("throw");
        return true;
    }

    // 볼록이는 마우스 X 쪽으로 걷고, 총은 마우스를 겨눈다
    public void Update(double dt, double mouseX, double mouseY, bool fire)
    {
        Carved.Clear();
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
                for (int i = 0, n = GunCount; i < n; i++) { var (p, a) = Gun(i); Shoot(p, a); }
        }
        else shotDebt = 0;

        int keep = 0;
        for (int n = 0; n < Bullets.Count; n++) // 총알: 빠르니 칸의 절반씩 나눠 움직인다
        {
            ref var b = ref Bullets[n];
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy) * dt / (Cell / 2.0)));
            bool alive = true;
            for (int i = 0; i < steps && alive; i++)
            {
                b.X += b.Vx * dt / steps;
                b.Y += b.Vy * dt / steps;
                if (Solid(b.X, b.Y))
                {
                    bool chunk = rng.NextDouble() < ChunkChance; // 큰 덩어리가 한꺼번에, 둘레에 금
                    int shape = rng.Next(Shapes.Length);
                    Carve(b.X, b.Y, (chunk ? ChunkR : HoleR) * sf, Shapes[shape]);
                    if (chunk) Crack(b.X, b.Y, chunkCracks[shape]);
                    Debris(b.X, b.Y, chunk ? ChunkBits : HitBits, chunk, 520, 1, BlastRoom);
                    Spark(b.X, b.Y, chunk ? 2 : 1.4);
                    alive = false;
                    OnEvent?.Invoke(chunk ? "chunk" : "hit");
                }
                else if (b.Y < Field.Top - 20 || b.X < Field.Left - 20 || b.X > Field.Right + 20) alive = false;
            }
            if (alive) Bullets[keep++] = b;
        }
        Bullets.Trim(keep);

        keep = 0;
        for (int n = 0; n < Shells.Count; n++)
        {
            ref var s = ref Shells[n];
            bool boom = false;
            if (s.Missile) // 미사일: 곧게 나가며 빨라진다. 처음 닿는 창 칸에서 터지고, 아무것도 못 만나면 화면 끝에서 터진다
            {
                s.Speed = Math.Min(MissileTop * sf, s.Speed + MissileAccel * sf * dt);
                int steps = Math.Max(1, (int)Math.Ceiling(s.Speed * dt / (Cell / 2.0)));
                for (int i = 0; i < steps && !boom; i++)
                {
                    s.X += s.Vx * s.Speed * dt / steps;
                    s.Y += s.Vy * s.Speed * dt / steps;
                    boom = Solid(s.X, s.Y) || s.X < Field.Left || s.X > Field.Right || s.Y < Field.Top;
                }
                if (!boom && Clock >= s.PuffAt) // 꼬리에 연기
                {
                    s.PuffAt = Clock + .02;
                    if (!Puffs.Full)
                        Puffs.Add() = new Bit { X = s.X - s.Vx * 8 * K, Y = s.Y - s.Vy * 8 * K, Vx = (rng.NextDouble() - .5) * 40 * sf, Vy = (rng.NextDouble() - .5) * 40 * sf,
                            Life = .75, Size = (int)(Cell * (.7 + rng.NextDouble() * .6)), Kind = BitKind.Smoke };
                }
                if (boom) (s.X, s.Y) = (Math.Clamp(s.X, Field.Left, Field.Right), Math.Max(s.Y, Field.Top));
            }
            else // 수류탄: 찍은 자리에 도착하면 터진다 (만일 바닥에 먼저 닿으면 거기서)
            {
                s.Fuse -= dt;
                if (s.Fuse <= 0) { (s.X, s.Y) = (s.TX, s.TY); boom = true; }
                else
                {
                    s.Vy += Gravity * sf * dt;
                    s.X += s.Vx * dt;
                    s.Y += s.Vy * dt;
                    boom = s.Y >= Ground;
                }
            }
            if (boom) Explode(s.X, s.Y, (s.Missile ? MissileR : NadeR) * sf);
            else Shells[keep++] = s;
        }
        Shells.Trim(keep);

        keep = 0;
        for (int n = 0; n < Bits.Count; n++)
        {
            ref var bit = ref Bits[n];
            if (bit.Kind != BitKind.Spark) bit.Vy += Gravity * sf * dt;
            bit.X += bit.Vx * dt;
            bit.Y += bit.Vy * dt;
            bit.Life -= dt;
            if (bit.Life > 0 && bit.Y <= Ground + 10) Bits[keep++] = bit;
        }
        Bits.Trim(keep);

        keep = 0;
        for (int n = 0; n < Puffs.Count; n++)
        {
            ref var puff = ref Puffs[n];
            puff.X += puff.Vx * dt;
            puff.Y += puff.Vy * dt;
            puff.Life -= dt;
            if (puff.Life > 0) Puffs[keep++] = puff;
        }
        Puffs.Trim(keep);

        keep = 0;
        for (int n = 0; n < Blasts.Count; n++)
            if (Clock - Blasts[n].At <= BlastArt.Sec) Blasts[keep++] = Blasts[n];
        Blasts.Trim(keep);
    }

    void Shoot(PointF p, double aim)
    {
        var m = MuzzleOf(p, aim);
        double a = aim + (rng.NextDouble() - .5) * 2 * SpreadDeg * Math.PI / 180, v = BulletSpeed * sf;
        Bullets.Add() = new Bullet { X = m.X, Y = m.Y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v };
        Shots++;
        LastShot = Clock;
        Kick = 1;
        if (!Bits.Full) // 탄피는 총 옆으로 튀어 떨어진다
            Bits.Add() = new Bit { X = p.X, Y = p.Y, Vx = -Math.Cos(aim) * 120 * sf + (rng.NextDouble() - .5) * 120 * sf, Vy = -250 * sf * rng.NextDouble(), Life = .6, Size = 2 * K, Kind = BitKind.Brass };
        OnEvent?.Invoke("shot");
    }

    // 파편은 칸만 하거나(11~20px) 덩어리·폭발이면 칸보다 큰(18~32px) 네모: 클수록 멀리, 오래 날아간다.
    // room: 남겨 둘 자리 (총알 파편이 자리를 다 써서 폭발 파편이 못 나오는 일이 없게)
    void Debris(double x, double y, int n, bool big, double speed, double life, int room)
    {
        for (int i = 0; i < n && Bits.Count < MaxBits - room; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2, v = (big ? speed : speed / 2) * sf * (.4 + rng.NextDouble());
            int size = (int)(Cell * (big ? 1 + rng.NextDouble() * .8 : .6 + rng.NextDouble() * .5));
            Bits.Add() = new Bit { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v + 60 * sf, Life = big ? life : .6, Size = size };
        }
    }

    void Spark(double x, double y, double size)
    {
        if (!Bits.Full) Bits.Add() = new Bit { X = x, Y = y, Life = .07, Size = (int)(Cell * size), Kind = BitKind.Spark };
    }

    void Explode(double x, double y, double r)
    {
        var shape = Ragged.At(x, y);
        Carve(x, y, r, shape);
        Crack(x, y, shape.Cracks(r, r, Cell / 2));
        Blasts.Add() = new Blast { X = x, Y = y, R = r, At = Clock };
        Debris(x, y, BlastBits, true, 640, 1.2, 0);
        Booms++;
        OnEvent?.Invoke("boom");
    }
}
