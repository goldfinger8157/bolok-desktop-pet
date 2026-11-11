namespace Bolok;

// 벽돌깨기: 창모드 창(작으면 여러 개)을 가로세로 조각으로 나누고, 볼록이가 머리 위로 받친 쟁반으로 쇠공을 튕겨 조각을 깬다.
// 공은 그 모니터 테두리에서 튕기고, 공이 모두 아래로 빠지면 하나를 잃는다(셋). 깨진 조각에서 가끔 아이템이 떨어진다.
// 화면과 무관한 규칙이라 --selfcheck 로 검사한다
public sealed class Breakout
{
    public sealed class Tile { public Rectangle R; public bool Broken; }
    public sealed class Ball { public double X, Y, Vx, Vy, StuckDx, StuckAt; public bool Stuck, Lost; }
    public sealed class Item { public string Kind = ""; public double X, Y; }
    public sealed class Shard { public double X, Y, Vx, Vy, Angle, Spin, Born; public int Size; }
    public enum Phase { Play, Won, Lost }

    // 아이템 (알카노이드 캡슐에서 클릭만으로 되는 것 + 불공·왕볼): 공 나누기, 쟁반 늘이기, 왕볼(커져서 튕기지 않고 지나가는 자리를 몽땅 부순다),
    // 불공(튕기지 않고 뚫고 간다), 느리게, 공 하나 더. 빠르게 진행하도록 공 나누기가 가장 자주, 느리게는 드물게 (YELL).
    // 공을 붙잡는 끈끈이는 뺐다 (YELL)
    public static readonly string[] Kinds = { "split", "wide", "giant", "fire", "slow", "life" };
    static readonly double[] KindWeights = { 40, 16, 14, 10, 6, 14 };
    public const double ItemChance = .18, WideSec = 15, GiantSec = 8, FireSec = 8, SlowSec = 8, AutoLaunch = 3, ShardSec = 1.1;
    public const int MaxBalls = 12, MaxLives = 5, MaxShards = 40;

    public readonly Rectangle Field;                 // 모니터 작업 영역
    public readonly List<Rectangle> Boards;          // 조각낸 창들
    public readonly List<Tile> Tiles = new();
    public readonly List<Tile> BrokenOrder = new();  // 깨진 차례. 화면은 새로 깨진 조각 자리만 다시 칠한다
    public readonly List<Ball> Balls = new();
    public readonly List<Item> Items = new();
    public readonly List<Shard> Shards = new();
    public readonly int K, BallR, PlateH, ItemR;
    readonly int plateBase;
    public double Speed, Clock, PlateX, PlateTop, EndAt, PlateHitAt = -9, WideUntil, SlowUntil, GiantUntil, FireUntil;
    public int Lives = 3, Broken;
    public Phase Stage = Phase.Play;
    public Action<string>? OnEvent;
    public bool Done => Stage != Phase.Play && Clock - EndAt > 2;
    public bool Wide => Clock < WideUntil;
    public bool Slow => Clock < SlowUntil;
    public bool Giant => Clock < GiantUntil;
    public bool Fire => Clock < FireUntil;
    public int PlateW => Wide ? plateBase * 8 / 5 : plateBase;
    public int Radius => Giant ? BallR * 2 : BallR;
    readonly Random rng;
    readonly double baseSpeed, sf;

    // 그림 크기에 맞춘다: 쇠공은 테두리까지 7칸, 쟁반은 36칸 × 5칸, 아이템은 13칸 × 9칸
    public Breakout(Rectangle field, IReadOnlyList<Rectangle> boards, int k, Random rng)
    {
        (Field, Boards, K, this.rng) = (field, boards.ToList(), k, rng);
        (BallR, plateBase, PlateH, ItemR) = ((int)(3.5 * k), 36 * k, 5 * k, 5 * k);
        sf = k / 3.0;
        foreach (var board in Boards)
        {
            // 조각은 대략 90×30 (배율 3 기준), 납작한 벽돌 모양 (YELL: "가로로 나누는걸 더 쪼개줘")
            int cols = Math.Clamp((int)Math.Round(board.Width / (90 * sf)), 3, 18);
            int rows = Math.Clamp((int)Math.Round(board.Height / (30 * sf)), 2, 30);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    Tiles.Add(new Tile
                    {
                        R = Rectangle.FromLTRB(board.Left + board.Width * c / cols, board.Top + board.Height * r / rows,
                            board.Left + board.Width * (c + 1) / cols, board.Top + board.Height * (r + 1) / rows)
                    });
        }
        Speed = baseSpeed = 620 * sf;
        PlateX = field.Left + field.Width / 2.0;
        Balls.Add(new Ball { Stuck = true });
    }

    // 게임할 창: 메뉴를 열기 전 쓰던 창 → 볼록이 모니터에서 가장 큰 창 → 다른 모니터에서 가장 큰 창. 창모드(최대화 아님)만.
    // 그 창이 작으면 같은 모니터의 다른 작은 창도 보탠다 (다른 창에 가리지 않고 서로 겹치지 않는 것, 모니터의 35% 또는 4개까지).
    // 조각은 쟁반 위로 칸 3개 높이까지만 낸다 (쟁반 바로 위까지 조각이 있으면 칠 틈이 없다). 없으면 null
    public static (Rectangle Field, List<(Win Win, Rectangle Board)> Targets)? Pick(Win? used, IReadOnlyList<Win> wins, Rectangle petArea,
        IReadOnlyList<Rectangle> screens, int cell)
    {
        long Area(Rectangle r) => r.Width * (long)r.Height;
        Rectangle FieldOf(Win w) => screens.OrderByDescending(s => Area(Rectangle.Intersect(s, w.R))).FirstOrDefault();
        Rectangle? BoardOf(Win w, Rectangle field, double minW, double minH)
        {
            if (w.Topmost) return null;
            var r = Rectangle.Intersect(w.R, field);
            if (Area(r) >= Area(field) * 85 / 100) return null; // 최대화한 창은 창모드가 아니다
            var board = Rectangle.FromLTRB(r.Left, r.Top, r.Right, Math.Min(r.Bottom, field.Bottom - cell * 3));
            return board.Width >= minW && board.Height >= minH ? board : null;
        }
        bool Covered(Win w, Rectangle board) => wins.TakeWhile(x => x.H != w.H).Any(x => x.R.IntersectsWith(board)); // 위에 다른 창이 걸쳐 있다

        (Win Win, Rectangle Field, Rectangle Board)? first = null;
        if (used is { } u && BoardOf(u, FieldOf(u), cell * 3, cell) is { } mine) first = (u, FieldOf(u), mine);
        first ??= wins.Select(w => (w, f: FieldOf(w))).Select(x => (x.w, x.f, b: BoardOf(x.w, x.f, cell * 3, cell)))
            .Where(x => x.b != null).Select(x => (Win: x.w, Field: x.f, Board: x.b!.Value))
            .OrderByDescending(x => x.Field == petArea).ThenBy(x => Covered(x.Win, x.Board)).ThenByDescending(x => Area(x.Board))
            .Select(x => ((Win, Rectangle, Rectangle)?)x).FirstOrDefault();
        if (first is not { } one) return null;

        var targets = new List<(Win Win, Rectangle Board)> { (one.Win, one.Board) };
        long total = Area(one.Board);
        foreach (var w in wins)
        {
            if (targets.Count >= 4 || total >= Area(one.Field) * 35 / 100) break;
            if (targets.Any(t => t.Win.H == w.H) || FieldOf(w) != one.Field) continue;
            if (BoardOf(w, one.Field, cell * 1.5, cell * .8) is not { } board || Covered(w, board)) continue;
            if (targets.Any(t => Rectangle.Inflate(t.Board, 4, 4).IntersectsWith(board))) continue;
            targets.Add((w, board));
            total += Area(board);
        }
        return (one.Field, targets);
    }

    // 쟁반에 붙은 공을 던진다. 붙은 자리가 끝일수록 비스듬히
    public void Launch()
    {
        bool any = false;
        foreach (var b in Balls.Where(b => b.Stuck))
        {
            double a = b.StuckDx == 0 ? (rng.NextDouble() * 2 - 1) * 20 * Math.PI / 180 : Angle(b.StuckDx);
            (b.Vx, b.Vy, b.Stuck) = (Speed * Math.Sin(a), -Speed * Math.Cos(a), false);
            any = true;
        }
        if (any) OnEvent?.Invoke("launch");
    }

    double Angle(double dx) => Math.Clamp(dx / (PlateW / 2.0), -1, 1) * 60 * Math.PI / 180; // 가운데는 똑바로, 끝은 60도

    // 쟁반은 볼록이를 따라다닌다. 볼록이 자리를 받는다
    public void Update(double dt, double plateX, double plateTop)
    {
        Clock += dt;
        (PlateX, PlateTop) = (plateX, plateTop);
        foreach (var s in Shards) { s.Vy += 1800 * sf * dt; s.X += s.Vx * dt; s.Y += s.Vy * dt; s.Angle += s.Spin * dt; }
        Shards.RemoveAll(s => Clock - s.Born > ShardSec);
        if (Stage != Phase.Play) return;

        foreach (var it in Items.ToList()) // 아이템은 똑바로 떨어진다. 쟁반으로 받으면 바로 효과
        {
            it.Y += 240 * sf * dt;
            if (Math.Abs(it.X - PlateX) <= PlateW / 2.0 + ItemR && it.Y + ItemR >= PlateTop && it.Y - ItemR <= PlateTop + PlateH)
            {
                Items.Remove(it);
                Apply(it.Kind);
            }
            else if (it.Y - ItemR > Field.Bottom) Items.Remove(it);
        }

        foreach (var b in Balls.Where(b => b.Stuck)) (b.X, b.Y) = (PlateX + b.StuckDx, PlateTop - BallR); // 쟁반 위에 얹혀 따라다닌다
        if (Balls.Any(b => b.Stuck && Clock - b.StuckAt > AutoLaunch)) Launch(); // 안 누르면 3초 뒤 저절로

        double speed = Speed * (Slow ? .65 : 1);
        foreach (var b in Balls.Where(b => !b.Stuck))
        {
            double v = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
            if (v > 0) (b.Vx, b.Vy) = (b.Vx / v * speed, b.Vy / v * speed);
        }
        // 빠른 공이 조각이나 쟁반을 건너뛰지 않게 반지름 절반씩 나눠 움직인다
        int steps = Math.Max(1, (int)Math.Ceiling(speed * dt / (BallR * .5)));
        for (int i = 0; i < steps && Stage == Phase.Play; i++)
            foreach (var b in Balls.Where(b => !b.Stuck && !b.Lost).ToList()) Step(b, dt / steps);
        Balls.RemoveAll(b => b.Lost);
        if (Stage == Phase.Play && Balls.Count == 0) Miss();
    }

    void Step(Ball b, double h)
    {
        int r = Radius;
        b.X += b.Vx * h;
        b.Y += b.Vy * h;
        // 모니터 테두리에서 튕긴다 (아래는 빼고)
        if (b.X - r < Field.Left) { b.X = Field.Left + r; b.Vx = Math.Abs(b.Vx); }
        else if (b.X + r > Field.Right) { b.X = Field.Right - r; b.Vx = -Math.Abs(b.Vx); }
        if (b.Y - r < Field.Top) { b.Y = Field.Top + r; b.Vy = Math.Abs(b.Vy); }

        // 쟁반: 내려오다 윗면에 닿으면 닿은 자리에 따라 튕긴다
        if (b.Vy > 0 && b.Y + r >= PlateTop && b.Y < PlateTop + PlateH && Math.Abs(b.X - PlateX) <= PlateW / 2.0 + r * .5)
        {
            PlateHitAt = Clock;
            double a = Angle(b.X - PlateX), v = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
            (b.Vx, b.Vy, b.Y) = (v * Math.Sin(a), -v * Math.Cos(a), PlateTop - r);
            OnEvent?.Invoke("plate");
            return;
        }
        if (b.Y - r > Field.Bottom) { b.Lost = true; return; }

        foreach (var t in Tiles)
        {
            if (t.Broken) continue;
            double cx = Math.Clamp(b.X, t.R.Left, t.R.Right), cy = Math.Clamp(b.Y, t.R.Top, t.R.Bottom);
            double dx = b.X - cx, dy = b.Y - cy;
            if (dx * dx + dy * dy > r * (double)r) continue;
            if (Giant) { Smash(t); if (Stage != Phase.Play) return; continue; } // 왕볼은 닿는 조각을 한꺼번에 부수며 쭉 간다
            if (!Fire) // 불공은 튕기지 않고 뚫고 간다. 아니면 옆면은 좌우로, 윗면·아랫면은 위아래로
            {
                if (Math.Abs(dx) > Math.Abs(dy)) b.Vx = Math.Sign(dx) * Math.Abs(b.Vx);
                else if (dy != 0) b.Vy = Math.Sign(dy) * Math.Abs(b.Vy);
                else b.Vy = -b.Vy; // 공 가운데가 조각 안에 들어갔을 때
            }
            Smash(t);
            return; // 한 걸음에 하나만
        }
    }

    void Smash(Tile t)
    {
        t.Broken = true;
        Broken++;
        BrokenOrder.Add(t);
        int pieces = Math.Clamp(MaxShards - Shards.Count, 0, 4); // 불공·왕볼이 몰아 깨도 파편은 40개까지만
        for (int i = 0; i < pieces; i++) // 조각이 부서져 떨어진다
            Shards.Add(new Shard
            {
                X = t.R.Left + rng.NextDouble() * t.R.Width, Y = t.R.Top + rng.NextDouble() * t.R.Height,
                Vx = (rng.NextDouble() - .5) * 300 * sf, Vy = -rng.NextDouble() * 250 * sf, Spin = (rng.NextDouble() - .5) * 12,
                Size = (int)((3 + rng.Next(5)) * sf), Born = Clock,
            });
        Speed = Math.Min(baseSpeed * 1.5, Speed * 1.006); // 깰수록 조금씩 빨라진다
        if (Items.Count < 3 && rng.NextDouble() < ItemChance)
            Items.Add(new Item { Kind = PickKind(), X = t.R.Left + t.R.Width / 2.0, Y = t.R.Top + t.R.Height / 2.0 });
        OnEvent?.Invoke("break");
        if (Tiles.All(x => x.Broken)) { Stage = Phase.Won; EndAt = Clock; Items.Clear(); OnEvent?.Invoke("won"); }
    }

    string PickKind()
    {
        double roll = rng.NextDouble() * KindWeights.Sum();
        for (int i = 0; i < Kinds.Length; i++) if ((roll -= KindWeights[i]) < 0) return Kinds[i];
        return Kinds[0];
    }

    public void Apply(string kind)
    {
        switch (kind)
        {
            case "split": // 날아가는 공마다 둘로. 모두 쟁반에 붙어 있으면 하나를 먼저 던진다
                if (Balls.All(b => b.Stuck)) { Balls[0].StuckDx = 1; Launch(); }
                foreach (var b in Balls.Where(b => !b.Stuck).ToList())
                {
                    if (Balls.Count >= MaxBalls) break;
                    var (vx, vy) = Turn(b.Vx, b.Vy, 30);
                    Balls.Add(new Ball { X = b.X, Y = b.Y, Vx = vx, Vy = vy });
                    (b.Vx, b.Vy) = Turn(b.Vx, b.Vy, -15);
                }
                break;
            case "wide": WideUntil = Clock + WideSec; break;
            case "slow": SlowUntil = Clock + SlowSec; break;
            case "giant": GiantUntil = Clock + GiantSec; break;
            case "fire": FireUntil = Clock + FireSec; break;
            case "life": Lives = Math.Min(MaxLives, Lives + 1); break;
        }
        OnEvent?.Invoke("item:" + kind);
    }

    // 방향을 돌린다. 너무 눕지 않게(수직에서 70도까지) 잡는다
    static (double, double) Turn(double vx, double vy, double deg)
    {
        double v = Math.Sqrt(vx * vx + vy * vy), a = Math.Atan2(vx, -vy) + deg * Math.PI / 180;
        double up = vy <= 0 ? 1 : -1; // 위로 가던 공은 위로, 아래로 가던 공은 아래로
        if (up < 0) a = Math.Atan2(vx, vy) + deg * Math.PI / 180;
        a = Math.Clamp(a, -70 * Math.PI / 180, 70 * Math.PI / 180);
        return (v * Math.Sin(a), -up * v * Math.Cos(a));
    }

    void Miss()
    {
        Lives--;
        (WideUntil, SlowUntil, GiantUntil, FireUntil) = (0, 0, 0, 0); // 아이템 효과도 사라진다
        Items.Clear();
        OnEvent?.Invoke("miss");
        if (Lives <= 0) { Stage = Phase.Lost; EndAt = Clock; OnEvent?.Invoke("over"); return; }
        Balls.Add(new Ball { Stuck = true, StuckAt = Clock });
    }
}
