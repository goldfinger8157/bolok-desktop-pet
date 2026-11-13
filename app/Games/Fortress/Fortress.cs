namespace Bolok;

// 포트리스 (웜즈·포트리스를 볼록이 식으로): 창모드 창들이 섬이 되고, 볼록이와 라이벌 햄스터가 번갈아 대포를 쏜다.
// 대포마다 체력이 다르다 (센 대포일수록 체력이 적다. 아래 Guns). 라이벌은 늘 나와 다른 대포를 쓴다. 바람은 턴마다 바뀌고 모든 포탄에 분다.
// 폭발하면 땅(창 전체)이 들쭉날쭉하게 깎이고(Kit/Ragged), 다 뚫리면 떨어져 화면 아래로 퐁당. 피해와 날아가기는 거리(원)로 따진다: 예측할 수 있어야 공정하다.
// 라이벌은 바람을 어림하고 흔들려 쏘다가 조금씩 좁혀 온다. 화면과 무관한 규칙이라 --selfcheck 로 검사한다 (FortressGame.Check)
public sealed class Fortress
{
    // 대포: 한 번에 몇 발, 퍼짐(도), 폭발 반지름, 최대 피해, 그 대포를 고른 햄스터의 체력
    public sealed record Gun(string Name, string KoLabel, int Hp, int Shells, double Spread, double Radius, int Damage)
    {
        public int Bounces { get; init; }           // 땅에 닿아 튕기는 횟수 (다 튕기고 나서 닿으면 터진다)
        public int Split { get; init; }             // 꼭대기에서 갈라지는 새끼 탄 수 (Radius·Damage 는 새끼 탄 하나의 것)
        public double Drill { get; init; }          // 땅에 닿은 뒤 날던 쪽으로 파고드는 길이 (px, 배율 3 기준)
        public double Crater { get; init; } = 1;    // 땅이 깎이는 반지름 ÷ 폭발 반지름
        public double Knock { get; init; } = 1;     // 밀쳐 내는 세기 (배)
        public int Look { get; init; }              // 포탄 그림: 0 로켓 · 1 큰 로켓 · 2 작은 쇠공 · 3 큰 쇠공
        public string Label => L.T("gun." + Name); // 언어별 이름 (KoLabel 은 기록용)
    }
    // 체력은 대포가 셀수록 적다 (YELL 2026-11-13 네 가지 추가):
    //  멀티 200 = 9발이 다 맞으면 198 · 왕 300 = 한 발 90, 구멍 85 · 일반 400 = 한 발 60 (기준)
    //  튕김 350 = 일반보다 조금 약한 한 발(55)이지만 언덕 너머로 튕겨 넣는다 · 집속 250 = 새끼 탄 넷이 다 맞으면 104, 넓게 덮는다
    //  드릴 350 = 땅속 45px 에서 크게(65) 터져 발밑을 무너뜨린다. 땅속이라 몸까지 멀어 피해(70)는 일반대포보다 덜 닿는다
    //  밀치기 300 = 피해는 20뿐이지만 바로 옆에 맞으면 일반대포의 두 배쯤(200px 안팎) 밀려 섬 끝에 서 있으면 빠진다. 40px 넘게 빗나가면 거의 안 밀린다
    //   (처음엔 반지름 60·세기 1.8배였는데 라이벌이 첫두 발에 볼록이를 빠뜨리는 판이 열에 여섯이라 줄였다)
    public static readonly Gun[] Guns =
    {
        new("multi", "멀티캐논", 200, 9, 3, 28, 22) { Look = 2 },   // 체력은 처음 정한 것의 두 배 (YELL: 판이 너무 빨리 끝났다)
        new("giant", "왕대포", 300, 1, 0, 85, 90) { Look = 1 },
        new("normal", "일반대포", 400, 1, 0, 50, 60),
        new("bounce", "튕김탄", 350, 1, 0, 45, 55) { Bounces = 2, Look = 3 },
        new("cluster", "집속탄", 250, 1, 0, 32, 26) { Split = 4 },
        new("drill", "드릴탄", 350, 1, 0, 65, 70) { Drill = 45 },
        new("push", "밀치기탄", 300, 1, 0, 50, 20) { Crater = .45, Knock = 1.5, Look = 3 },
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
    public sealed class Shell
    {
        public string Kind = "";
        public double X, Y, Vx, Vy, R, Damage, Crater = 1, Knock = 1;
        public int Look, Bounces, Split;
        public double Age, Fuse = -1;               // Fuse: 처음 튕긴 뒤 터질 때까지 남은 시간 (-1 이면 안 잰다)
        public double Drill, DrillLeft = -1, Dug;   // DrillLeft: 땅속을 파고드는 중이면 남은 길이 (-1 이면 날고 있다) · Dug: 마지막으로 굴을 판 자리
        public bool Bounced;                        // 방금 튕겼다 (소리용. 읽은 쪽이 지운다)
        public bool Boost, Powered;                 // Boost: 파워업 폭풍을 지나 세졌다 · Powered: 방금 세졌다 (소리·폭풍 끝내기용. 읽은 쪽이 지운다)
    }
    public sealed class Blast { public double X, Y, R, At; public bool Quiet; } // Quiet: 드릴탄이 판 굴 (불덩이·소리 없이 땅만 깎였다)
    public enum Phase { Turn, Flying, Settle, Won, Lost }

    public const int Cell = 4;          // 땅 칸 (px)
    public const double Gravity = 1100, MaxShot = 1250, WindAccel = 260, TurnSec = 25, ChargeSec = 1.2, WalkSpeed = 90;
    public const double BounceKeep = .6, BounceFuse = 2.5;     // 튕길 때마다 남는 빠르기 · 처음 튕기고 이만큼 지나면 어디서든 터진다
    public const double SplitAge = .12, SplitSpread = 90;      // 쏘자마자 갈라지지 않게 · 새끼 탄이 옆으로 벌어지는 빠르기 (px/초, 배율 3 기준)
    public const double DrillSpeed = 240, TunnelR = 9;         // 파고드는 빠르기 · 굴 반지름 (배율 3 기준)
    // 파워업 폭풍 (YELL 2026-11-13): 가끔 둘 사이 하늘에 노란 폭풍이 뜨고, 그 안을 지난 포탄은 세진다 (누가 쏜 것이든)
    //  · 양쪽 첫 발이 끝난 뒤부터, 폭풍이 없는 차례가 시작될 때 25% 로 뜬다. 두 차례(양쪽 한 번씩) 떠 있다가 사라지고, 한 발이라도 세지게 했으면 그 발이 끝날 때 사라진다
    //  · 세진 포탄: 폭발 반지름·피해 1.5배, 드릴탄은 파고드는 길이도 1.5배, 밀치는 세기 1.3배. 집속탄은 새끼 탄이 물려받고, 멀티캐논은 지나간 발마다 세진다
    public const double StormChance = .25, StormSize = 55, BoostBlast = 1.5, BoostKnock = 1.3;
    public const int StormTurns = 2;
    public const double BlastSec = .6;  // 폭발은 이만큼 지나면 목록에서 지운다 (불덩이 그림은 BlastArt.Sec 동안. 깎인 땅은 칸에 남는다)

    public readonly Rectangle Field;
    public readonly List<Rectangle> Lands;
    public readonly int Gw, Gh;         // 땅 칸 수
    readonly bool[] solid, land;        // solid: 지금 땅인 칸, land: 처음에 땅이던 칸 (깎인 자리 = land 이고 solid 아님)
    readonly int fx, fy;                // 판 왼쪽 위 (자주 읽어서 따로 둔다)
    public readonly Hog Me = new(), Cpu = new() { Dir = -1 };
    public readonly List<Shell> Shells = new();
    public readonly List<Blast> Blasts = new();   // 방금 터진 것들 (BlastSec 뒤 지운다). 새 폭발은 늘 맨 뒤에 붙는다
    public int Booms { get; private set; }        // 지금까지 터진 수
    public int Carves { get; private set; }       // 지금까지 땅이 깎인 수 = Blasts 에 붙은 수 (폭발 + 굴. 그리는 쪽이 새로 깎인 자리를 알아챈다)
    public readonly int K;
    public readonly double BodyR;
    public bool MyTurn = true;
    public double Wind, Clock, TurnAt, EndAt, Aim = -Math.PI / 4, CpuAim = -Math.PI * .75, ChargeFrom = -1;
    public double LastAim, LastPower = -1;        // 내가 지난번에 쏜 각도·힘 (놓은 그대로. 아직 안 쐈으면 힘이 -1). 라이벌이 쏜 것은 적지 않는다
    public bool HasLast => LastPower >= 0;
    public Phase Stage = Phase.Turn;
    public double StormX, StormY, StormR;         // 파워업 폭풍 가운데와 반지름
    public int StormLeft;                         // 폭풍이 떠 있을 남은 차례 수 (0 이면 없다)
    public bool StormUsed;                        // 이번 발을 세지게 했다: 차례가 넘어갈 때 사라진다
    public bool StormOn => StormLeft > 0;
    public int Turns { get; private set; }        // 지금이 몇 번째 차례인가 (첫 차례가 1)
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

    // 내 대포를 고르면 라이벌은 나머지 둘 중 아무거나: 둘이 같은 대포를 쓰지 않는다 (YELL 2026-11-13). cpuGun 으로 정하면 그대로 (같은 것도 된다)
    public Fortress(Rectangle field, IReadOnlyList<Rectangle> lands, int k, Random rng, string? myGun = null, string? cpuGun = null)
    {
        (Field, K, this.rng) = (field, k, rng);
        (fx, fy) = (field.Left, field.Top);
        sf = k / 3.0;
        BodyR = 40 * k * .22;
        (Me.Gun, Cpu.Gun) = (GunOf(myGun), cpuGun != null ? GunOf(cpuGun) : OtherGun(GunOf(myGun), rng));
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

    static Gun OtherGun(Gun mine, Random rng)
    {
        int skip = rng.Next(Guns.Length - 1);
        foreach (var g in Guns) if (!ReferenceEquals(g, mine) && skip-- == 0) return g;
        return mine;
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
        if (h == Me) (LastAim, LastPower) = (angle, Math.Max(0, power)); // 다음 내 차례에 견줘 볼 수 있게 (지난 각도 조준경·힘 막대 위 세모)
        var gun = h.Gun;
        for (int i = 0; i < gun.Shells; i++)
        {
            double off = (i - (gun.Shells - 1) / 2.0) * gun.Spread * Math.PI / 180, a = angle + off;
            double v = Math.Max(.08, power) * MaxShot * sf * (1 - Math.Abs(off) * .6);
            var s = new Shell();
            Load(s, gun, h.X + Math.Cos(angle) * BodyR * 1.5, CenterY(h) + Math.Sin(angle) * BodyR * 1.5, Math.Cos(a) * v, Math.Sin(a) * v);
            Shells.Add(s);
        }
        h.Dir = Math.Cos(angle) >= 0 ? 1 : -1;
        Stage = Phase.Flying;
        OnEvent?.Invoke("fire");
    }

    // 포탄을 그 대포의 것으로 채운다 (쏠 때, 그리고 라이벌이 날려 볼 때 같은 것을 쓴다)
    void Load(Shell s, Gun gun, double x, double y, double vx, double vy)
    {
        (s.Kind, s.X, s.Y, s.Vx, s.Vy, s.R, s.Damage, s.Crater, s.Knock) = (gun.Name, x, y, vx, vy, gun.Radius * sf, gun.Damage, gun.Crater, gun.Knock);
        (s.Look, s.Bounces, s.Split, s.Drill) = (gun.Look, gun.Bounces, gun.Split, gun.Drill * sf);
        (s.Age, s.Fuse, s.DrillLeft, s.Dug, s.Bounced, s.Boost, s.Powered) = (0, -1, -1, 0, false, false, false);
    }

    // 집속탄이 갈라질 때 i 번째 새끼 탄: 어미 자리에서 옆으로 고르게 벌어지고 살짝 튀어 오른다. 작은 쇠공, 더 갈라지지 않는다
    void Bomblet(Shell from, int i, int of, Shell into)
    {
        (into.Kind, into.X, into.Y, into.R, into.Damage, into.Crater, into.Knock) = (from.Kind, from.X, from.Y, from.R, from.Damage, from.Crater, from.Knock);
        (into.Vx, into.Vy) = (from.Vx + (i - (of - 1) / 2.0) * SplitSpread * sf, from.Vy - 40 * sf);
        (into.Look, into.Bounces, into.Split, into.Drill) = (2, 0, 0, 0);
        (into.Age, into.Fuse, into.DrillLeft, into.Dug, into.Bounced, into.Boost, into.Powered) = (from.Age, -1, -1, 0, false, from.Boost, false); // 세진 것은 물려받는다 (R·Damage 에 이미 들어 있다)
    }

    // 폭풍을 지난 포탄이 세진다 (한 번만)
    static void Boost(Shell s) => (s.Boost, s.Powered, s.R, s.Damage, s.Drill, s.Knock) = (true, true, s.R * BoostBlast, s.Damage * BoostBlast, s.Drill * BoostBlast, s.Knock * BoostKnock);

    // ── 파워업 폭풍 ──
    // 그 자리에 폭풍을 둘 수 있나: 판 안이고, 섬·햄스터와 겹치지 않는다
    public bool StormFits(double x, double y, double r)
    {
        if (x - r < Field.Left || x + r > Field.Right || y - r < Field.Top + 4 * sf || y + r > Field.Bottom) return false;
        if (TooClose(Me) || TooClose(Cpu)) return false;
        bool TooClose(Hog h) => !h.Out && Math.Sqrt((h.X - x) * (h.X - x) + (CenterY(h) - y) * (CenterY(h) - y)) < r + BodyR * 3;
        double reach = r + 2 * Cell;
        int gx0 = Math.Max(0, (int)((x - reach - fx) / Cell)), gx1 = Math.Min(Gw - 1, (int)((x + reach - fx) / Cell));
        int gy0 = Math.Max(0, (int)((y - reach - fy) / Cell)), gy1 = Math.Min(Gh - 1, (int)((y + reach - fy) / Cell));
        for (int gy = gy0; gy <= gy1; gy++)
            for (int gx = gx0; gx <= gx1; gx++)
            {
                if (!solid[gy * Gw + gx]) continue;
                double cx = fx + (gx + .5) * Cell - x, cy = fy + (gy + .5) * Cell - y;
                if (cx * cx + cy * cy < reach * reach) return false;
            }
        return true;
    }

    // 둘 사이 하늘, 포탄이 흔히 지나는 높이(높은 쪽 발에서 130~350px 위)에 폭풍을 띄운다. 자리가 없으면 안 띄운다
    public bool SpawnStorm()
    {
        double r = StormSize * sf;
        for (int tries = 0; tries < 8; tries++)
        {
            double x = (Me.X + Cpu.X) / 2 + (rng.NextDouble() - .5) * (Cpu.X - Me.X) * .3;
            double y = Math.Min(Me.Y, Cpu.Y) - (130 + rng.NextDouble() * 220) * sf;
            if (PlaceStorm(x, y)) return true;
        }
        return false;
    }

    // 정한 자리에 (검사·장면·디버그용. 못 두는 자리면 false)
    public bool PlaceStorm(double x, double y)
    {
        double r = StormSize * sf;
        if (!StormFits(x, y, r)) return false;
        (StormX, StormY, StormR, StormLeft, StormUsed) = (x, y, r, StormTurns, false);
        OnEvent?.Invoke("storm");
        return true;
    }

    // 포탄 한 걸음: 바람에 밀리고, 땅이나 햄스터에 닿으면 터진다. 0 아직 난다 · 1 터졌다(터진 자리는 s.X, s.Y) · 2 화면 밖으로 사라졌다 · 3 지금 갈라진다(집속탄)
    // 튕김탄은 땅에 닿으면 튕기고(Bounces 번), 드릴탄은 닿은 뒤 날던 쪽으로 곧게 파고든다. 햄스터에 닿으면 어느 것이든 바로 터진다
    // 라이벌 계산이 한 차례에 수십만 번 도는 곳이라 걸음마다 바뀌지 않는 값은 밖에서 한 번만 읽는다 (땅은 여기서 깎지 않는다: 굴은 Update 가 판다)
    int Fly(Shell s, double dt, double wind)
    {
        s.Age += dt;
        if (s.Fuse >= 0 && (s.Fuse -= dt) < 0) return 1;
        if (s.DrillLeft >= 0) return Bore(s, dt);
        s.Vx += wind * WindAccel * sf * dt;
        s.Vy += Gravity * sf * dt;
        if (s.Split > 0 && s.Vy >= 0 && s.Age >= SplitAge) return 3; // 꼭대기를 넘었다
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
            if (me && (x - meX) * (x - meX) + (y - meY) * (y - meY) < r2 || cpu && (x - cpuX) * (x - cpuX) + (y - cpuY) * (y - cpuY) < r2) { hit = 1; break; }
            if ((uint)gx >= (uint)Gw || (uint)gy >= (uint)Gh || !solid[gy * Gw + gx]) continue;
            if (s.Bounces > 0) // 튕긴다: 닿기 바로 앞 자리로 물러나 땅 겉면에 되튄다 (이 프레임의 남은 걸음은 버린다)
            {
                (x, y) = (x - dx, y - dy);
                Bounce(s, gx, gy);
            }
            else if (s.Drill > 0) // 파고들기 시작: 날던 쪽 그대로, 바람·중력 없이
            {
                double v = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
                (s.Vx, s.Vy, s.DrillLeft, s.Dug) = v < 1e-9 ? (0, DrillSpeed * sf, s.Drill, 0) : (s.Vx / v * DrillSpeed * sf, s.Vy / v * DrillSpeed * sf, s.Drill, 0);
            }
            else hit = 1;
            break;
        }
        if (StormLeft > 0 && !s.Boost) // 이 걸음에 지나온 길이 폭풍에 닿았나
        {
            double ux = x - s.X, uy = y - s.Y, len2 = ux * ux + uy * uy;
            double t = len2 > 0 ? Math.Clamp(((StormX - s.X) * ux + (StormY - s.Y) * uy) / len2, 0, 1) : 0;
            double cx = s.X + ux * t - StormX, cy = s.Y + uy * t - StormY;
            if (cx * cx + cy * cy < StormR * StormR) Boost(s);
        }
        (s.X, s.Y) = (x, y);
        return hit;
    }

    // 땅 겉면에 되튄다: 닿은 칸 둘레(5×5)의 땅이 몰린 쪽 반대가 겉면이 보는 쪽. 빠르기는 BounceKeep 배, 처음 튕길 때부터 시간을 잰다
    void Bounce(Shell s, int gx, int gy)
    {
        double nx = 0, ny = 0;
        for (int j = -2; j <= 2; j++)
            for (int i = -2; i <= 2; i++)
                if (SolidCell(gx + i, gy + j)) { nx -= i; ny -= j; }
        double n = Math.Sqrt(nx * nx + ny * ny), v = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
        (nx, ny) = n > 1e-9 ? (nx / n, ny / n) : v > 1e-9 ? (-s.Vx / v, -s.Vy / v) : (0, -1); // 땅속 한가운데면 온 길로 되돌아간다
        double into = s.Vx * nx + s.Vy * ny;
        if (into < 0) (s.Vx, s.Vy) = (s.Vx - 2 * into * nx, s.Vy - 2 * into * ny);
        (s.Vx, s.Vy) = (s.Vx * BounceKeep, s.Vy * BounceKeep);
        s.Bounces--;
        s.Bounced = true;
        if (s.Fuse < 0) s.Fuse = BounceFuse;
    }

    // 드릴탄이 땅속을 곧게 파고든다: 정해진 길이를 다 가면 터진다 (가다가 햄스터에 닿아도). 중간에 빈 곳이 나와도 곧장 간다
    int Bore(Shell s, double dt)
    {
        double v = DrillSpeed * sf, step = Math.Min(v * dt, s.DrillLeft);
        (s.X, s.Y, s.DrillLeft) = (s.X + s.Vx / v * step, s.Y + s.Vy / v * step, s.DrillLeft - step);
        if (s.X < Field.Left - 50 || s.X > Field.Right + 50 || s.Y > Field.Bottom + 50) return 2;
        return s.DrillLeft <= 1e-9 || Near(Me, s.X, s.Y) || Near(Cpu, s.X, s.Y) ? 1 : 0;
    }

    // 드릴탄이 지나간 자리에 굴을 판다 (굴 반지름의 0.8배마다 한 번). 불덩이도 피해도 없이 땅만 깎인다
    void Tunnel(Shell s)
    {
        double done = s.Drill - s.DrillLeft, r = TunnelR * sf;
        if (done - s.Dug < r * .8 && done > 0) return;
        s.Dug = done;
        Carve(s.X, s.Y, r);
        Blasts.Add(new Blast { X = s.X, Y = s.Y, R = r, At = Clock, Quiet = true });
        Carves++;
    }

    bool Near(Hog h, double x, double y) => !h.Out && (x - h.X) * (x - h.X) + (y - CenterY(h)) * (y - CenterY(h)) < BodyR * BodyR;

    // knock: 밀쳐 내는 세기(배) · crater: 땅이 깎이는 반지름 ÷ 폭발 반지름 (밀치기탄은 크게 밀고 작게 깎는다)
    public void Explode(double x, double y, double r = 0, double damage = 60, double knock = 1, double crater = 1)
    {
        if (r <= 0) r = 50 * sf;
        Carve(x, y, r * crater);
        Blasts.Add(new Blast { X = x, Y = y, R = r, At = Clock });
        Booms++;
        Carves++;
        Shake(Me, x, y, r, damage, knock);
        Shake(Cpu, x, y, r, damage, knock);
        OnEvent?.Invoke("boom");
    }

    // 피해와 날아가기는 거리로만 (깎인 모양과 무관하게 동그랗게): 가까울수록 크다
    void Shake(Hog h, double x, double y, double r, double damage, double knock)
    {
        if (h.Out) return;
        double dx = h.X - x, dy = CenterY(h) - y, d = Math.Sqrt(dx * dx + dy * dy), reach = r * 1.25;
        if (d >= reach) return;
        double near = 1 - d / reach;
        h.Hp = Math.Max(0, h.Hp - (int)Math.Round(damage * near));
        (double nx, double ny) = d < 1 ? (0, -1) : (dx / d, dy / d);
        (h.Air, h.Vx, h.Vy, h.HurtUntil) = (true, nx * 520 * sf * near * knock, ny * 520 * sf * near * knock - 180 * sf, Clock + .6); // 날아간다
        h.Y -= 1;
        OnEvent?.Invoke(h == Me ? "hurt:me" : "hurt:cpu");
    }

    // ── 라이벌: 제 대포(가운데 발)로 볼록이 근처에 떨어지는 각도·힘을 찾는다. 사람처럼 바람을 어림하고(0.6~1.4배)
    // 각도 ±9도·힘 ±15% 흔들려 쏘다가, 쏠 때마다 흔들림이 12%씩 줄어 절반까지 (YELL: "악당이 너무 잘하는거 아냐") ──
    // 각도 41가지 × 힘 15가지를 다 날려 보는 계산이라 한 번에 하면 한 프레임이 20ms 가까이 걸렸다:
    // 생각하는 동안(차례가 오고 1.2초) 프레임마다 각도 한두 줄씩 나눠서 한다. 그동안 판은 멈춰 있어서 결과는 한 번에 계산한 것과 같다
    const int PlanRows = 41;   // -170° ~ -10°, 4° 씩
    const int PlanSteps = 400; // 한 발을 이만큼(6.7초)까지 날려 본다
    int cpuShots, planRow;
    bool planning;
    double planWind, planX, planY, planAimX, planAimY;
    (double A, double P, double D) planBest;
    readonly Shell probe = new(), probeKid = new();

    void PlanBegin(double miss)
    {
        planWind = miss > 0 ? Wind * (.6 + rng.NextDouble() * .8) : Wind; // 바람 어림
        PlanAgain();
    }

    void PlanAgain()
    {
        (planRow, planBest, planX, planY) = (0, (-Math.PI / 2, .5, double.MaxValue), Me.X, CenterY(Me));
        (planAimX, planAimY) = (planX, planY);
        if (Cpu.Gun.Knock <= 1) return;
        // 밀치기탄: 볼록이를 가까운 섬 끝 쪽으로 밀어내게, 그 반대쪽 발치를 노린다
        int edge = 0;
        for (double d = Cell; d < 4000 && edge == 0; d += Cell)
            edge = Floor(Me.X - d, Me.Y, 30 * sf, 30 * sf) == null ? -1 : Floor(Me.X + d, Me.Y, 30 * sf, 30 * sf) == null ? 1 : 0;
        (planAimX, planAimY) = (Me.X - edge * BodyR, Me.Y);
    }

    // 날려 본 포탄이 터진 자리의 점수 (작을수록 좋다): 노리는 자리까지의 거리. 제 발밑이면 못 쓴다
    // 폭풍을 지나 세진 포탄은 폭발이 1.5배 넓으니 그만큼 가까운 것으로 친다: 폭풍을 지나가는 길이 쓸 만하면 그쪽을 고른다
    double PlanScore(Shell s)
    {
        double d = Math.Sqrt((s.X - planAimX) * (s.X - planAimX) + (s.Y - planAimY) * (s.Y - planAimY));
        double self = Math.Sqrt((s.X - Cpu.X) * (s.X - Cpu.X) + (s.Y - CenterY(Cpu)) * (s.Y - CenterY(Cpu)));
        return self < s.R * 1.6 ? d + 10000 : s.Boost ? d / BoostBlast : d;
    }

    void PlanStep(int rows)
    {
        var s = probe;
        for (; rows > 0 && planRow < PlanRows; rows--, planRow++)
        {
            double deg = -170 + planRow * 4;
            for (double p = .3; p <= 1.001; p += .05)
            {
                double a = deg * Math.PI / 180, v = p * MaxShot * sf;
                Load(s, Cpu.Gun, Cpu.X + Math.Cos(a) * BodyR * 1.5, CenterY(Cpu) + Math.Sin(a) * BodyR * 1.5, Math.Cos(a) * v, Math.Sin(a) * v);
                int hit = 0, i = 0;
                for (; i < PlanSteps && hit == 0; i++) hit = Fly(s, 1 / 60.0, planWind); // 튕기고 파고드는 것까지 실제와 같은 걸음으로
                double d = double.MaxValue;
                if (hit == 1) d = PlanScore(s);
                else if (hit == 3) // 집속탄: 새끼 탄을 하나씩 마저 날려 보고 가장 가까이 떨어진 것으로 (하나라도 제 발밑이면 못 쓴다)
                    for (int kid = 0; kid < s.Split; kid++)
                    {
                        Bomblet(s, kid, s.Split, probeKid);
                        int end = 0;
                        for (int j = i - 1; j < PlanSteps && end == 0; j++) end = Fly(probeKid, 1 / 60.0, planWind);
                        if (end != 1) continue;
                        double kd = PlanScore(probeKid);
                        d = kd >= 10000 ? kd : Math.Min(d, kd);
                        if (kd >= 10000) break;
                    }
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
        Turns++;
        if (StormOn) { if (StormUsed || --StormLeft <= 0) (StormLeft, StormUsed) = (0, false); }
        else if (Turns > 2 && rng.NextDouble() < StormChance) SpawnStorm();
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
                    if (s.Bounced) { s.Bounced = false; OnEvent?.Invoke("bounce"); }
                    if (s.Powered) // 폭풍을 지나 세졌다. 소리는 그 발의 첫 포탄에만 (멀티캐논 아홉 발이 줄줄이 울리지 않게)
                    {
                        s.Powered = false;
                        if (!StormUsed) { StormUsed = true; OnEvent?.Invoke("power"); }
                    }
                    if (s.DrillLeft >= 0 && hit != 2) Tunnel(s);
                    if (hit == 0) continue;
                    Shells.RemoveAt(i--);
                    if (hit == 1) Explode(s.X, s.Y, s.R, s.Damage, s.Knock, s.Crater);
                    else if (hit == 3) // 집속탄이 갈라진다: 새끼 탄은 맨 뒤에 붙어 이 프레임부터 난다
                    {
                        for (int kid = 0; kid < s.Split; kid++) { var b = new Shell(); Bomblet(s, kid, s.Split, b); Shells.Add(b); }
                        OnEvent?.Invoke("split");
                    }
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
