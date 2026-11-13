namespace Bolok;

// 버그 습격 (YELL 2026-11-12: 갤러그 같은 고전 고정 화면 슈팅을 볼록이식으로. 이름·그림·장치는 우리 것):
// 볼록이 근처 작은 판에서 작은 볼록이가 아래를 좌우로 다니며 위로 쏜다. 버그 편대가 휘어 날아들어 줄을 서고,
// 가끔 한두 마리(보스는 버그 둘을 거느리고)가 휘어져 내려와 들이받거나 침을 쏜다. 다 잡으면 다음 판(더 빠르게).
// 보스 버그는 두 번 맞아야 하고, 잡으면 가끔 하트(목숨)·별(쌍발)을 떨어뜨린다. 화면과 무관한 규칙이라 --selfcheck 로 검사한다
public sealed class Raid
{
    public enum Phase { Play, Hit, Clear, Over, Done }
    public enum Move { Enter, Hold, Dive, Back }
    public sealed class Foe { public bool Boss; public int Hp, Row, Col, Spits; public double X, Y, T, Dur, Hit = -9; public Move Mode; public PointF A, B, C, D; }
    public sealed class Shot { public double X, Y, Vx, Vy; public bool Enemy; }
    public sealed class Drop { public double X, Y; public bool Heart; }
    public sealed class Pop { public double X, Y, At, R; }

    // 배율 3 기준 px. 판은 앱이 볼록이 근처에 120×160 칸(배율 3 이면 360×480)으로 잡는다
    public const double MoveSpeed = 420, ShotSpeed = 760, SpitSpeed = 250, DropSpeed = 140, FireSec = .18,
        HitSec = 1.4, SafeSec = 2, ClearSec = 1.6, OverSec = 3.5, StarSec = 10;
    public const int Cols = 8, MaxShots = 2, MaxLives = 5;

    public readonly Rectangle Field;
    public readonly int K;
    public readonly List<Foe> Foes = new();
    public readonly List<Shot> Shots = new();
    public readonly List<Drop> Drops = new();
    public readonly List<Pop> Pops = new();
    public double X, Clock, SafeUntil, StarUntil;
    public int Lives = 3, Score, Stage, Kills, Dir = 1;
    public Phase State = Phase.Play;
    public Action<string>? OnEvent;
    public bool Done => State == Phase.Done;
    public bool Moving { get; private set; }
    public double PhaseFor => Clock - phaseAt;         // 지금 단계(맞음·판 깸·끝)에 들어선 지

    readonly Random rng;
    readonly double sf;
    double phaseAt, diveAt, fireCool, targetX;
    bool fire;

    public Raid(Rectangle field, int k, Random rng, int stage = 1)
    {
        (Field, K, this.rng, Stage) = (field, k, rng, stage);
        sf = k / 3.0;
        X = targetX = field.Left + field.Width / 2.0;
        Spawn();
    }

    public double Ground => Field.Bottom - 4 * sf;     // 작은 볼록이 발
    public double ShipY => Ground - 16 * sf;           // 맞는 자리 (몸 가운데)
    public double ShipR => 9 * sf;
    public double FoeR(Foe f) => (f.Boss ? 13 : 10) * sf;
    double Space => (Field.Width - 40 * sf) / (Cols - 1);
    double Sway => Math.Sin(Clock * .8) * 10 * sf; // 줄 선 편대가 천천히 좌우로
    public PointF Slot(int row, int col) => new((float)(Field.Left + 20 * sf + col * Space + Sway), (float)(Field.Top + 34 * sf + row * 24 * sf));
    public bool Shown(Foe f) => f.Mode != Move.Enter || f.T >= 0; // 아직 출발 전이면 판 밖

    // 편대: 맨 위 보스 넷, 그 아래 버그 여덟씩 두 줄 (세 판마다 한 줄 더, 세 줄까지). 넷씩 묶어 왼쪽·오른쪽 위에서 번갈아 휘어 들어온다
    void Spawn()
    {
        Foes.Clear();
        int bugRows = Math.Min(3, 2 + (Stage - 1) / 3);
        var list = new List<Foe>();
        for (int c = 2; c < 6; c++) list.Add(new Foe { Boss = true, Hp = 2, Row = 0, Col = c });
        for (int r = 1; r <= bugRows; r++)
            for (int c = 0; c < Cols; c++) list.Add(new Foe { Hp = 1, Row = r, Col = c });
        for (int i = 0; i < list.Count; i++)
        {
            var f = list[i];
            int group = i / 4;
            bool left = group % 2 == 0;
            f.Mode = Move.Enter;
            f.T = -(group * .9 + i % 4 * .15) / 1.6; // 차례가 올 때까지 기다린다
            f.Dur = 1.6;
            f.A = new PointF((float)(left ? Field.Left - 20 * sf : Field.Right + 20 * sf), (float)(Field.Top + 40 * sf));
            f.B = new PointF(Field.Left + Field.Width * (left ? .8f : .2f), Field.Top + Field.Height * .6f);
            f.C = new PointF(Field.Left + Field.Width * (left ? .2f : .8f), Field.Top + Field.Height * .3f);
            (f.X, f.Y) = (f.A.X, f.A.Y);
            Foes.Add(f);
        }
        diveAt = Clock + 3 + list.Count * .12;
    }

    static PointF Bez(PointF a, PointF b, PointF c, PointF d, double t)
    {
        double u = 1 - t;
        return new PointF((float)(u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X),
                          (float)(u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y));
    }

    static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    public void SetTarget(double x) => targetX = Math.Clamp(x, Field.Left + 12 * sf, Field.Right - 12 * sf);
    public void Fire(bool on) => fire = on;

    public void Update(double dt)
    {
        Clock += dt;
        for (int i = Pops.Count - 1; i >= 0; i--) if (Clock - Pops[i].At > BlastArt.Sec) Pops.RemoveAt(i);
        switch (State)
        {
            case Phase.Hit when Clock - phaseAt >= HitSec:
                if (Lives <= 0) Go(Phase.Over, "over");
                else { State = Phase.Play; SafeUntil = Clock + SafeSec; }
                break;
            case Phase.Clear when Clock - phaseAt >= ClearSec:
                Stage++;
                Spawn();
                State = Phase.Play;
                OnEvent?.Invoke("stage");
                break;
            case Phase.Over when Clock - phaseAt >= OverSec:
                State = Phase.Done;
                break;
        }
        if (State is Phase.Over or Phase.Done) { MoveShots(dt); return; }

        Moving = false;
        if (State == Phase.Play)
        {
            double step = MoveSpeed * sf * dt, d = Math.Clamp(targetX - X, -step, step);
            if (Math.Abs(d) > .1) { X += d; Moving = true; Dir = d > 0 ? 1 : -1; }
        }
        fireCool -= dt;
        bool star = Clock < StarUntil;
        if (State == Phase.Play && fire && fireCool <= 0 && Mine() < (star ? MaxShots * 2 : MaxShots))
        {
            fireCool = FireSec;
            double v = -ShotSpeed * sf;
            if (star) { Shots.Add(new Shot { X = X - 6 * sf, Y = ShipY - 8 * sf, Vy = v }); Shots.Add(new Shot { X = X + 6 * sf, Y = ShipY - 8 * sf, Vy = v }); }
            else Shots.Add(new Shot { X = X, Y = ShipY - 8 * sf, Vy = v });
            OnEvent?.Invoke("fire");
        }
        MoveFoes(dt);
        MoveShots(dt);
        Collide();
        if (State == Phase.Play && Foes.Count == 0) Go(Phase.Clear, "clear");
    }

    int Mine() { int n = 0; foreach (var s in Shots) if (!s.Enemy) n++; return n; } // 날아가는 내 탄 수

    void Go(Phase p, string ev) { (State, phaseAt) = (p, Clock); OnEvent?.Invoke(ev); }

    void MoveFoes(double dt)
    {
        foreach (var f in Foes)
        {
            switch (f.Mode)
            {
                case Move.Enter:
                {
                    f.T += dt / f.Dur;
                    if (f.T < 0) { (f.X, f.Y) = (f.A.X, f.A.Y); break; }
                    var p = Bez(f.A, f.B, f.C, Slot(f.Row, f.Col), Math.Min(1, f.T));
                    (f.X, f.Y) = (p.X, p.Y);
                    if (f.T >= 1) f.Mode = Move.Hold;
                    break;
                }
                case Move.Hold:
                {
                    var p = Slot(f.Row, f.Col);
                    (f.X, f.Y) = (p.X, p.Y);
                    break;
                }
                case Move.Dive:
                {
                    f.T += dt / f.Dur;
                    var p = Bez(f.A, f.B, f.C, f.D, Math.Min(1, f.T));
                    (f.X, f.Y) = (p.X, p.Y);
                    if (State == Phase.Play && f.Y < ShipY - 70 * sf && (f.Spits == 0 && f.T > .3 || f.Spits == 1 && f.T > .5 && Stage >= 2)) Spit(f);
                    if (f.T >= 1) { f.Mode = Move.Back; (f.X, f.Y) = (Slot(f.Row, f.Col).X, Field.Top - 20 * sf); } // 아래로 빠지면 위에서 돌아온다
                    break;
                }
                case Move.Back:
                {
                    var p = Slot(f.Row, f.Col);
                    double d = Dist(f.X, f.Y, p.X, p.Y), step = 220 * sf * dt;
                    if (d <= step) { (f.X, f.Y) = (p.X, p.Y); f.Mode = Move.Hold; }
                    else { f.X += (p.X - f.X) / d * step; f.Y += (p.Y - f.Y) / d * step; }
                    break;
                }
            }
        }
        // 가끔 줄 선 버그 하나가 내려온다. 보스면 바로 아래 버그 둘을 거느리고
        if (State != Phase.Play || Clock < diveAt) return;
        diveAt = Clock + Math.Max(.9, 2.6 - Stage * .15) * (.7 + rng.NextDouble() * .6);
        var hold = Foes.Where(f => f.Mode == Move.Hold).ToList();
        if (hold.Count == 0) return;
        var lead = hold[rng.Next(hold.Count)];
        Dive(lead, 0);
        if (lead.Boss)
            foreach (var (e, i) in hold.Where(f => !f.Boss && f.Row == 1 && Math.Abs(f.Col - lead.Col) <= 1 && f != lead).Take(2).Select((e, i) => (e, i)))
                Dive(e, (i == 0 ? -1 : 1) * 22 * sf);
    }

    void Dive(Foe f, double offset)
    {
        int side = f.X < Field.Left + Field.Width / 2.0 ? -1 : 1; // 바깥쪽으로 한 번 휘었다가
        (f.Mode, f.T, f.Spits) = (Move.Dive, 0, 0);
        f.Dur = Math.Max(1.5, 2.6 - Stage * .08);
        f.A = new PointF((float)f.X, (float)f.Y);
        f.B = new PointF((float)(f.X + side * 50 * sf), (float)(f.Y - 30 * sf));
        f.C = new PointF((float)(X + offset - side * 70 * sf), (float)(Ground - 110 * sf));
        f.D = new PointF((float)(X + offset + (rng.NextDouble() - .5) * 60 * sf), (float)(Field.Bottom + 30 * sf));
        OnEvent?.Invoke("dive");
    }

    void Spit(Foe f) // 침: 볼록이 쪽으로 조금 기울여 아래로
    {
        f.Spits++;
        double v = SpitSpeed * sf * (1 + (Stage - 1) * .06);
        Shots.Add(new Shot { X = f.X, Y = f.Y + 8 * sf, Vx = Math.Clamp((X - f.X) * .6, -70 * sf, 70 * sf), Vy = v, Enemy = true });
    }

    void MoveShots(double dt)
    {
        foreach (var s in Shots) { s.X += s.Vx * dt; s.Y += s.Vy * dt; }
        for (int i = Shots.Count - 1; i >= 0; i--) if (Shots[i].Y < Field.Top - 10 || Shots[i].Y > Field.Bottom + 10) Shots.RemoveAt(i);
        foreach (var d in Drops) d.Y += DropSpeed * sf * dt;
        for (int i = Drops.Count - 1; i >= 0; i--) if (Drops[i].Y > Field.Bottom + 10) Drops.RemoveAt(i);
    }

    // 프레임마다 도는 곳이라 목록을 새로 만들지 않고 번호로 돈다 (지우면 그 자리부터 다시)
    void Collide()
    {
        for (int i = 0; i < Shots.Count; i++) // 내 탄 → 버그
        {
            var s = Shots[i];
            if (s.Enemy) continue;
            Foe? hit = null;
            foreach (var f in Foes) if (Shown(f) && Dist(f.X, f.Y, s.X, s.Y) < FoeR(f)) { hit = f; break; }
            if (hit == null) continue;
            Shots.RemoveAt(i--);
            hit.Hit = Clock;
            if (--hit.Hp <= 0) Kill(hit, true);
            else OnEvent?.Invoke("hit");
        }
        if (State != Phase.Play) return;
        bool safe = Clock < SafeUntil;
        for (int i = 0; i < Shots.Count; i++) // 침 → 볼록이
        {
            var s = Shots[i];
            if (!s.Enemy || Dist(s.X, s.Y, X, ShipY) >= ShipR + 3 * sf) continue;
            Shots.RemoveAt(i--);
            if (!safe) { Hurt(); return; }
        }
        for (int i = 0; i < Foes.Count; i++) // 내려온 버그와 부딪힘
        {
            var f = Foes[i];
            if (f.Mode != Move.Dive || Dist(f.X, f.Y, X, ShipY) >= FoeR(f) + ShipR * .8) continue;
            Kill(f, false);
            i--;
            if (!safe) { Hurt(); return; }
        }
        for (int i = 0; i < Drops.Count; i++) // 떨어지는 선물 줍기
        {
            var d = Drops[i];
            if (Math.Abs(d.X - X) >= 14 * sf || Math.Abs(d.Y - ShipY) >= 16 * sf) continue;
            Drops.RemoveAt(i--);
            if (d.Heart) Lives = Math.Min(MaxLives, Lives + 1);
            else StarUntil = Clock + StarSec;
            OnEvent?.Invoke(d.Heart ? "heart" : "star");
        }
    }

    void Kill(Foe f, bool byShot)
    {
        Foes.Remove(f);
        Kills++;
        if (byShot) Score += (f.Boss ? 150 : 50) * (f.Mode == Move.Dive ? 2 : 1); // 내려오는 걸 잡으면 두 배
        Pops.Add(new Pop { X = f.X, Y = f.Y, At = Clock, R = FoeR(f) * 1.5 });
        if (f.Boss && byShot && rng.NextDouble() < .4) Drops.Add(new Drop { X = f.X, Y = f.Y, Heart = Lives < 3 || rng.NextDouble() < .35 });
        OnEvent?.Invoke(f.Boss ? "boss" : "kill");
    }

    void Hurt()
    {
        Lives--;
        Pops.Add(new Pop { X = X, Y = ShipY, At = Clock, R = 18 * sf });
        for (int i = Shots.Count - 1; i >= 0; i--) if (Shots[i].Enemy) Shots.RemoveAt(i);
        Go(Phase.Hit, "hurt");
    }

    // 시험·디버그용 자동 조종: 다가오는 침·버그는 피하고, 아니면 가장 낮은 버그 밑으로 (늘 쏜다)
    public double BotTarget()
    {
        foreach (var s in Shots.Where(s => s.Enemy && s.Y < ShipY && s.Y > ShipY - 110 * sf))
            if (Math.Abs(s.X + s.Vx * (ShipY - s.Y) / Math.Max(1, s.Vy) - X) < 22 * sf)
                return X + (X < Field.Left + Field.Width / 2.0 ? 60 : -60) * sf;
        foreach (var f in Foes.Where(f => f.Mode == Move.Dive && f.Y > ShipY - 120 * sf))
            if (Math.Abs(f.X - X) < 28 * sf) return X + (f.X < X ? 60 : -60) * sf;
        var aim = Foes.Where(Shown).OrderByDescending(f => f.Mode == Move.Dive ? f.Y + 1000 : f.Y).FirstOrDefault();
        return aim?.X ?? X;
    }
}
