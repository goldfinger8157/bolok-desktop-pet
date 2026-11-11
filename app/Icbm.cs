namespace Bolok;

// ICBM (일시키기): 명령하면 볼록이가 빨간 버튼을 누르고, 미사일이 화면 위로 날아갔다가 아무 데나 떨어져 버섯구름을 내며 터진다.
// 활성 창에는 안 떨어진다. 파인 자리는 볼록이가 달려가 망치로 고친다 (가까운 것부터). 고치기 전에 또 쏠 수 있다 (구멍 다섯까지).
// 판을 깔지 않아 일하던 창은 그대로 쓴다. 화면과 무관한 규칙이라 --selfcheck 로 검사한다
public sealed class Icbm
{
    public sealed class Crater { public double X, Y, R0, R, At; }
    public sealed class Peek { public Crater Hole = null!; public string Species = ""; public double At; }
    public sealed class Blast { public double X, Y, At; }
    public sealed class Bit { public double X, Y, Vx, Vy, Life; public int Size; public bool Smoke; }
    public enum Phase { ToButton, Press, Rise, Away, Fall, Boom, ToCrater, Repair, Done }

    // 고치러 갈 땐 뛰지 않는다: 평소 걸음(70)보다 조금 빠르게 (YELL)
    public const double ButtonSec = .5, PressSec = .7, AwaySec = 1.2, BoomSec = 1.5, RepairSec = 3, GoSpeed = 100, CraterR = 108, Gravity = 1400;
    // 떨어질 땐 콰콰콰콰 울리며 천천히 (YELL: 너무 빨랐다). 거리에 따라 1.6~2.6초. 글자 없이 떨림·불꽃·소리로만
    public const double FallSpeed = 650, FallMinSec = 1.6, FallMaxSec = 2.6;
    // 구멍에서 가끔 떠난 친구가 빼꼼 (YELL). 구멍 하나에 평균 8초에 한 번, 버섯구름이 걷힌 뒤부터, 한 번에 하나만. 몸은 1/3 만 나온다 (그리는 쪽)
    public const double PeekSec = 2.8, PeekEvery = 8, PeekAfter = 2;
    public const int MaxCraters = 5, Cell = 12;
    public const double NearChance = .25;   // 가끔은 볼록이 근처에 떨어져서 볼록이도 날아간다 (YELL)
    // 버섯구름 그림(40칸, 배율 3배, 바닥 가운데가 터진 자리): 위로 38칸, 옆으로 17칸까지 퍼진다. 화면에서 잘리지 않게 그만큼 띄워 떨어뜨린다
    public const int CloudScale = 3, CloudUp = 38 * CloudScale, CloudHalf = 17 * CloudScale;

    public readonly List<Rectangle> Screens;
    public readonly List<Crater> Craters = new();
    public readonly List<Blast> Blasts = new();
    public readonly List<Bit> Bits = new();
    public readonly List<Peek> Peeks = new();
    public IReadOnlyList<string> Friends = Array.Empty<string>();   // 자연으로 떠난 친구들 (빼꼼 나오는 아이)
    public readonly int K;
    public double X, Y, Clock, ButtonX, ButtonY, MX, MY, MVx, MVy, TX, TY, BlownVx, BlownVy;   // X,Y: 볼록이 발. M: 미사일, T: 떨어질 자리, Blown: 폭발에 날아가는 속도
    public int Dir = 1, Version, Queued, Launched;
    public string Pose = "idle";
    public bool ButtonDown, HasTarget, MissileOn;
    public Phase Stage = Phase.Done;
    public Action<string>? OnEvent;
    public bool Finished => Stage == Phase.Done && Craters.Count == 0 && Queued == 0;
    readonly Random rng;
    readonly double sf, cell;
    double phaseAt, puffAt, fallSec = 1, nextPeek;
    Crater? job;
    readonly World world;                              // 모니터 사이 빈 곳으로는 안 간다: 맞닿은 곳(문)으로 건넌다
    readonly List<(double X, double Y)> path = new();

    public Icbm(IReadOnlyList<Rectangle> screens, double petX, double petY, int k, Random rng)
    {
        (Screens, X, Y, K, this.rng) = (screens.ToList(), petX, petY, k, rng);
        sf = k / 3.0;
        cell = 40 * k;
        world = new World(Screens);
    }

    double Top => Screens.Min(s => s.Top);
    void Go(Phase p) => (Stage, phaseAt) = (p, Clock);
    double Since => Clock - phaseAt;
    public double FallT => Stage == Phase.Fall ? Math.Clamp(Since / fallSec, 0, 1) : 0; // 떨어진 정도 0~1

    // 빼꼼 올라온 정도 0~1: 0.5초 올라와서 있다가 0.5초 내려간다
    public static double Rise(double t)
    {
        double u = t < .5 ? t / .5 : t > PeekSec - .5 ? (PeekSec - t) / .5 : 1;
        u = Math.Clamp(u, 0, 1);
        return u * u * (3 - 2 * u);
    }

    // 발사 명령. 쏘는 중이면 줄을 세운다. 구멍이 다섯이면 안 받는다
    public bool Launch()
    {
        if (Craters.Count + Queued + (Stage is >= Phase.ToButton and <= Phase.Fall ? 1 : 0) >= MaxCraters) return false;
        if (Stage is >= Phase.ToButton and <= Phase.Boom) { Queued++; return true; }
        Begin();
        return true;
    }

    void Begin() // 볼록이 옆에 버튼이 솟는다
    {
        var screen = Screens.OrderBy(s => Dist(Math.Clamp(X, s.Left, s.Right), Math.Clamp(Y, s.Top, s.Bottom), X, Y)).First();
        if (X + cell * 1.6 > screen.Right) Dir = -1;
        else if (X - cell * 1.6 < screen.Left) Dir = 1;
        (ButtonX, ButtonY, ButtonDown, MissileOn, HasTarget, job) = (X + Dir * cell * .62, Y, false, false, false, null);
        path.Clear();
        Launched++;
        Go(Phase.ToButton);
    }

    // 떨어질 자리: 어느 모니터든, 폭발이 활성 창과 볼록이에 닿지 않는 곳. 없으면 null
    // near: 볼록이 바로 옆(반지름의 0.5~1.2배)을 노린다. 거기에 알맞은 자리가 없으면 null
    public static PointF? Pick(IReadOnlyList<Rectangle> screens, Rectangle? active, double petX, double petY, double r, Random rng, double headroom = 0, double sideroom = 0, bool near = false)
    {
        double top = Math.Max(r * 1.1, headroom), side = Math.Max(r * 1.1, sideroom);
        if (near)
        {
            var zone = active is { } act ? Rectangle.Inflate(act, (int)r, (int)r) : Rectangle.Empty;
            for (int i = 0; i < 60; i++)
            {
                double ang = rng.NextDouble() * Math.PI * 2, d = r * (.5 + rng.NextDouble() * .7), x = petX + Math.Cos(ang) * d, y = petY + Math.Sin(ang) * d;
                bool fits = screens.Any(s => x >= s.Left + side && x <= s.Right - side && y >= s.Top + top && y <= s.Bottom - r * 1.1);
                if (fits && !zone.Contains((int)x, (int)y)) return new PointF((float)x, (float)y);
            }
            return null;
        }
        var no = active is { } a ? Rectangle.Inflate(a, (int)r, (int)r) : Rectangle.Empty;
        for (int i = 0; i < 300; i++)
        {
            var s = screens[rng.Next(screens.Count)];
            if (s.Width < side * 2 + r * .2 || s.Height < top + r * 1.3) continue;
            double x = s.Left + side + rng.NextDouble() * (s.Width - side * 2), y = s.Top + top + rng.NextDouble() * (s.Height - top - r * 1.1);
            if (no.Contains((int)x, (int)y) || Dist(x, y, petX, petY) < r * 2) continue;
            return new PointF((float)x, (float)y);
        }
        return null;
    }

    static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    public void Update(double dt, Rectangle? active)
    {
        Clock += dt;
        foreach (var b in Bits)
        {
            if (!b.Smoke) b.Vy += Gravity * sf * dt;
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            b.Life -= dt;
        }
        Bits.RemoveAll(b => b.Life <= 0);
        Blasts.RemoveAll(b => Clock - b.At > 1.2);
        PeekOut(dt);

        switch (Stage)
        {
            case Phase.ToButton: // 버튼이 솟는 동안 그쪽을 본다
                Pose = "idle";
                if (Since >= ButtonSec) { Go(Phase.Press); OnEvent?.Invoke("press"); }
                break;
            case Phase.Press: // 깡충 뛰어 꾹
                ButtonDown = Since > .25;
                Pose = Since < .25 ? "hop" : "land";
                if (Since < PressSec) break;
                (MX, MY, MVx, MVy, MissileOn) = (ButtonX + Dir * cell * .5, ButtonY - 12 * K, 0, -150 * sf, true);
                Go(Phase.Rise);
                OnEvent?.Invoke("launch");
                break;
            case Phase.Rise: // 연기를 끌며 점점 빨리 솟는다
                Pose = "shock";
                MVy -= 2600 * sf * dt;
                MY += MVy * dt;
                if (Clock - puffAt > .03) { puffAt = Clock; Smoke(MX + (rng.NextDouble() - .5) * 6 * K, MY + 12 * K); }
                if (MY < Top - 200) { MissileOn = false; Go(Phase.Away); }
                break;
            case Phase.Away: // 화면 밖에 있는 동안 올려다본다
                Pose = "look";
                if (Since < AwaySec) break;
                var spot = rng.NextDouble() < NearChance ? Pick(Screens, active, X, Y - cell * .3, CraterR * sf, rng, CloudUp * K + 6, CloudHalf * K + 6, near: true) : null;
                if ((spot ?? Pick(Screens, active, X, Y, CraterR * sf, rng, CloudUp * K + 6, CloudHalf * K + 6)) is { } t)
                {
                    (TX, TY, HasTarget, MissileOn) = (t.X, t.Y, true, true);
                    double side = rng.NextDouble() < .5 ? -1 : 1;
                    (MX, MY) = (TX + side * 260 * sf, Top - 220);
                    fallSec = Math.Clamp(Dist(MX, MY, TX, TY) / (FallSpeed * sf), FallMinSec, FallMaxSec);
                    (MVx, MVy) = ((TX - MX) / fallSec, (TY - MY) / fallSec);
                    Go(Phase.Fall);
                    OnEvent?.Invoke("fall");
                }
                else // 떨어질 데가 없으면 하늘에서 터지고 만다
                {
                    Blasts.Add(new Blast { X = X, Y = Top + 160, At = Clock });
                    OnEvent?.Invoke("nowhere");
                    Go(Phase.Boom);
                }
                break;
            case Phase.Fall:
                Pose = "shock";
                double left = Dist(MX, MY, TX, TY), step = Math.Sqrt(MVx * MVx + MVy * MVy) * dt;
                if (Clock - puffAt > .02) { puffAt = Clock; Smoke(MX - MVx * .03, MY - MVy * .03); } // 꼬리에 연기를 굵게
                if (left > step) { MX += MVx * dt; MY += MVy * dt; break; }
                (MissileOn, HasTarget) = (false, false);
                Craters.Add(new Crater { X = TX, Y = TY, R0 = CraterR * sf, R = CraterR * sf, At = Clock });
                Blasts.Add(new Blast { X = TX, Y = TY, At = Clock });
                for (int i = 0; i < 40; i++)
                {
                    double a = rng.NextDouble() * Math.PI * 2, v = 600 * sf * (.3 + rng.NextDouble());
                    Bits.Add(new Bit { X = TX, Y = TY, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v - 200 * sf, Life = 1.1, Size = (int)(Cell * (.5 + rng.NextDouble() * .7)) });
                }
                Version++;
                OnEvent?.Invoke("boom");
                double bx = X - TX, by = Y - cell * .3 - TY, bd = Dist(X, Y - cell * .3, TX, TY), reach = CraterR * sf * 1.7;
                if (bd < reach) // 가까이서 터지면 볼록이도 멀리 날아간다 (가까울수록 세게, 위로 치솟게). 세게 던졌을 때만큼 빠르다
                {
                    double push = (2900 + 1100 * (1 - bd / reach)) * sf, ux = bd < 1 ? 0 : bx / bd, uy = (bd < 1 ? -1 : by / bd) - .35, ul = Math.Sqrt(ux * ux + uy * uy);
                    (BlownVx, BlownVy) = (ux / ul * push, uy / ul * push); // 방향(폭발 반대쪽 + 조금 위)을 먼저 정하고 세기를 곱한다
                    OnEvent?.Invoke("blown");
                }
                Go(Phase.Boom);
                break;
            case Phase.Boom: // 깜짝 놀라 굳는다
                Pose = "shock";
                if (Since < BoomSec) break;
                if (Queued > 0) { Queued--; Begin(); }
                else Next();
                break;
            case Phase.ToCrater: // 가장 가까운 구멍으로 달려간다
            {
                double tx = job!.X, ty = job.Y + job.R0 * .35;
                if (path.Count == 0) // 구멍이 다른 모니터에 있으면 맞닿은 곳을 지나서 간다
                {
                    Rectangle here = world.At(X, Y), dest = world.At(tx, ty);
                    if (here != dest && world.NextArea(here, dest, (int)cell) is { } next)
                    {
                        var door = world.Door(here, next, X, Y, (int)cell, 50);
                        path.Add((door.AX, door.AY));
                        path.Add((door.BX, door.BY));
                    }
                }
                var (gx, gy) = path.Count > 0 ? path[0] : (tx, ty);
                double d = Dist(X, Y, gx, gy), run = GoSpeed * sf * dt;
                Pose = "walk";
                if (Math.Abs(gx - X) > 2) Dir = gx > X ? 1 : -1;
                if (d > run) { X += (gx - X) / d * run; Y += (gy - Y) / d * run; break; }
                (X, Y) = (gx, gy);
                if (path.Count > 0) { path.RemoveAt(0); break; }
                Go(Phase.Repair);
                break;
            }
            case Phase.Repair: // 뚝딱뚝딱. 구멍이 한 칸씩 줄어든다
            {
                Pose = "fix";
                double r = Math.Ceiling(job!.R0 * (1 - Since / RepairSec) / Cell) * Cell;
                if (r != job.R) { job.R = Math.Max(0, r); Version++; }
                if (Since < RepairSec) break;
                Craters.Remove(job);
                Version++;
                OnEvent?.Invoke("repaired");
                Next();
                break;
            }
            case Phase.Done:
                Pose = "idle";
                break;
        }
    }

    // YELL 이 볼록이를 잡아서 옮기는 동안: 그 자리를 따라가고, 놓으면 거기서부터 다시 걸어간다. 고치던 중이면 처음부터 다시
    public void Hold(double x, double y)
    {
        (X, Y) = (x, y);
        path.Clear();
        if (Stage == Phase.Repair && job != null) { job.R = job.R0; Version++; }
        if (Stage is Phase.ToCrater or Phase.Repair) Go(Phase.ToCrater);
    }

    // 떠난 친구가 구멍에서 빼꼼. 고치는 구멍에서는 쏙 들어간다
    void PeekOut(double dt)
    {
        Peeks.RemoveAll(p => Clock - p.At > PeekSec || !Craters.Contains(p.Hole) || (Stage == Phase.Repair && p.Hole == job));
        if (Peeks.Count > 0 || Friends.Count == 0 || Clock < nextPeek) return;
        var holes = Craters.Where(c => Clock - c.At > PeekAfter && !(Stage == Phase.Repair && c == job)).ToList();
        if (holes.Count == 0 || rng.NextDouble() >= dt * holes.Count / PeekEvery) return;
        Peeks.Add(new Peek { Hole = holes[rng.Next(holes.Count)], Species = Friends[rng.Next(Friends.Count)], At = Clock });
        nextPeek = Clock + PeekSec + 1;
        OnEvent?.Invoke("peek");
    }

    void Next()
    {
        path.Clear();
        job = Craters.OrderBy(c => Dist(c.X, c.Y, X, Y)).FirstOrDefault();
        Go(job == null ? Phase.Done : Phase.ToCrater);
    }

    void Smoke(double x, double y)
    {
        if (Bits.Count > 260) return;
        Bits.Add(new Bit { X = x, Y = y, Vx = (rng.NextDouble() - .5) * 40 * sf, Vy = 30 * sf, Life = .7, Size = (int)(Cell * (.6 + rng.NextDouble() * .6)), Smoke = true });
    }
}
