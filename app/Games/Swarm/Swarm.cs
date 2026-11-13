namespace Bolok;

// 버그 떼 (YELL 2026-11-13: 사방에서 몰려오는 적을 총·칼로 물리치며 버티는 게임. 이름·그림·장치는 우리 것):
// 볼록이 둘레 작은 판에서 작은 볼록이가 방향키로 다닌다. 벌레가 네 변에서 줄줄이 들어와 볼록이에게 걸어오고, 갈수록 많고 빨라진다.
// 총은 가까운 벌레를 알아서 쏘고, 칼은 벌레가 바짝 붙으면 알아서 휘두른다. 잡으면 보석이 떨어지고, 주우면 레벨이 올라 하나씩 세진다.
// 가끔 자석이 떨어진다: 주우면 한동안 판 위의 보석이 다 볼록이에게 날아와 주워진다 (YELL 2026-11-13)
// 신발도 가끔 떨어진다: 주우면 그 판 내내 걸음이 빨라진다, 세 켤레까지 (YELL 2026-11-13)
// 하트가 다 떨어지면 끝, 3분을 버티면 이긴다. 화면과 무관한 규칙이라 --selfcheck 로 검사한다
public sealed class Swarm
{
    public enum Phase { Play, Win, Over, Done }
    public enum Kind { Bug, Wasp, Beetle, Boss }   // 보통, 빠른 말벌, 느리고 단단한 딱정벌레, 가끔 오는 큰 놈
    public enum Up { Rate, Multi, Arc, Heart }     // 총이 빨라짐, 총알 하나 더, 칼이 커짐, 하트
    public enum Item { Gem, Heart, Magnet, Shoe }  // 판에 떨어지는 것: 보석, 하트, 자석(한동안 다 끌어온다), 신발(빨라진다)
    public sealed class Foe { public Kind Kind; public int Hp; public double X, Y, Speed, Hit = -9; }
    public sealed class Shot { public double X, Y, Vx, Vy, Until; }
    public sealed class Gem { public Item Item; public double X, Y; public int Xp; public bool ShownTrail; public double ShownX = double.NaN, ShownY; } // Shown: 그리는 쪽이 마지막으로 그린 자리·꼬리
    public sealed class Pop { public double X, Y, At, R; }

    // 배율 3 기준 px·초. 판은 앱이 볼록이 둘레에 220×150 칸(배율 3 이면 660×450)으로 잡는다
    public const double WinSec = 180, MoveSpeed = 170, ShotSpeed = 520, GunRange = 230, PickRange = 56,
        SafeSec = 2, SwingSec = .2, EndSec = 3.5, BossEvery = 40,
        MagnetSec = 8, MagnetFirst = 20, MagnetGap = 90, MagnetPull = 480,   // 자석: 끄는 시간, 처음 나올 수 있는 때, 끝난 뒤 다시 나올 때까지 쉬는 시간, 보석이 날아오는 빠르기
        ShoeBoost = .15, ShoeFirst = 15, ShoeGap = 30;                       // 신발: 한 켤레에 빨라지는 만큼, 처음 나올 수 있는 때, 하나 떨어진 뒤 다음 것까지 쉬는 시간
    public const int MaxLives = 5, MaxFoes = 90, MaxBullets = 4, MaxGems = 150, MaxShoes = 3;

    public readonly Rectangle Field;
    public readonly int K;
    public readonly List<Foe> Foes = new();
    public readonly List<Shot> Shots = new();
    public readonly List<Gem> Gems = new();
    public readonly List<Pop> Pops = new();
    public readonly List<Gem> Gone = new();        // 이번 프레임에 사라진 보석 (그리는 쪽이 그 자리를 지운다)
    public readonly int[] FromSide = new int[4];   // 변마다 들어온 수 (왼쪽·오른쪽·위·아래)
    public double X, Y, Clock, SafeUntil, HurtAt = -9, SwingAt = -9, SwingDir, FireAt = -9, UpAt = -9;
    public double GunSec = .8, SwordSec = 1.1, Reach = 44, ArcHalf = Math.PI / 3;   // 크면서 바뀌는 무기 값
    public double MagnetUntil = -9, MagnetChance = .05, ShoeChance = .05;           // 자석이 끄는 끝 시각, 벌레를 잡을 때 자석·신발이 나올 확률
    public int Lives = 3, Level = 1, Xp, Kills, Bullets = 1, Dir = 1, AimDir = 1, Magnets, Shoes;
    public Item LastItem;                          // 마지막으로 주운 자석·신발과 그때 (머리 위에 잠깐 뜬다)
    public double ItemAt = -9;
    public Up? LastUp;
    public bool Spawning = true;                   // 자가 검사에서 끄고 벌레를 직접 놓는다
    public Phase State = Phase.Play;
    public Action<string>? OnEvent;
    public bool Done => State == Phase.Done;
    public bool Moving { get; private set; }
    public bool MagnetOn => State == Phase.Play && Clock < MagnetUntil;
    public double MagnetLeft => MagnetOn ? (MagnetUntil - Clock) / MagnetSec : 0;   // 남은 만큼 1 → 0
    public double Speed => MoveSpeed * (1 + ShoeBoost * Shoes);                     // 신발 한 켤레에 15% 씩
    public double Time => State == Phase.Play ? Clock : endAt;   // 버틴 시간
    public int Need => 2 + 4 * Level;                            // 다음 레벨까지 보석
    public double EndedFor => State == Phase.Play ? 0 : Clock - endAt; // 끝난 지

    readonly Random rng;
    readonly int[] ups = new int[4];
    readonly double sf;
    double endAt, spawnDue, bossAt = BossEvery, gunCool, swordCool, steerX, steerY, magnetFrom = MagnetFirst;
    bool magnetOut;                                // 자석이 판 위에 떨어져 있다 (한 번에 하나만)
    int shoesOut;                                  // 판 위에 떨어져 있는 신발
    double shoeFrom = ShoeFirst;

    public Swarm(Rectangle field, int k, Random rng)
    {
        (Field, K, this.rng) = (field, k, rng);
        sf = k / 3.0;
        (X, Y) = (field.Left + field.Width / 2.0, field.Top + field.Height / 2.0);
    }

    public double Feet => Y + 20 * sf;             // (X, Y) 는 몸 가운데
    public double BodyR => 9 * sf;
    public double ReachPx => Reach * sf;
    public double FoeR(Foe f) => (f.Kind switch { Kind.Boss => 24, Kind.Beetle => 12, Kind.Wasp => 8, _ => 10 }) * sf;
    double Dist(Foe f) => Dist(f.X, f.Y, X, Y);
    static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    public void Steer(double dx, double dy) => (steerX, steerY) = (dx, dy);
    (double, double) Inside(double x, double y) => (Math.Clamp(x, Field.Left + 12 * sf, Field.Right - 12 * sf), Math.Clamp(y, Field.Top + 26 * sf, Field.Bottom - 22 * sf));

    public void Update(double dt)
    {
        Clock += dt;
        Gone.Clear();
        for (int i = Pops.Count - 1; i >= 0; i--) if (Clock - Pops[i].At > BlastArt.Sec) Pops.RemoveAt(i);
        if (State != Phase.Play)
        {
            if (State != Phase.Done && Clock - endAt >= EndSec) State = Phase.Done;
            return;
        }
        double len = Math.Sqrt(steerX * steerX + steerY * steerY);
        Moving = len > .01;
        if (Moving)
        {
            double step = Speed * sf * dt / len;
            (X, Y) = Inside(X + steerX * step, Y + steerY * step);
            if (Math.Abs(steerX) > .01) Dir = steerX > 0 ? 1 : -1;
        }
        Spawn(dt);
        MoveFoes(dt);
        Sword(dt);
        Gun(dt);
        MoveShots(dt);
        Touch();
        if (State != Phase.Play) return;
        Pick(dt);
        if (Clock >= WinSec) // 끝까지 버텼다: 남은 벌레는 다 터진다
        {
            foreach (var f in Foes) Pops.Add(new Pop { X = f.X, Y = f.Y, At = Clock, R = FoeR(f) * 1.5 });
            Foes.Clear();
            Shots.Clear();
            Go(Phase.Win, "win");
        }
    }

    void Go(Phase p, string ev) { (State, endAt, Moving) = (p, Math.Min(Clock, WinSec), false); OnEvent?.Invoke(ev); }

    // ── 벌레: 네 변 바로 밖에서 나와 볼록이에게 걸어온다. 1초에 한 마리로 시작해 끝 무렵 다섯 마리, 40초마다 큰 놈 ──
    void Spawn(double dt)
    {
        if (!Spawning) return;
        spawnDue += dt * (1 + 4 * Clock / WinSec);
        for (; spawnDue >= 1; spawnDue--)
            if (Foes.Count < MaxFoes)
            {
                double pick = rng.NextDouble();
                Add(Clock > 60 && pick < .15 ? Kind.Beetle : Clock > 25 && pick < .4 ? Kind.Wasp : Kind.Bug);
            }
        if (Clock >= bossAt) { bossAt += BossEvery; Add(Kind.Boss); OnEvent?.Invoke("big"); }
    }

    public Foe Add(Kind kind)
    {
        int side = 0;
        double m = 14 * sf, x = 0, y = 0;
        for (int tries = 0; tries < 8 && (tries == 0 || Dist(x, y, X, Y) < 150 * sf); tries++) // 볼록이 코앞에서는 나오지 않는다
        {
            side = rng.Next(4);
            double u = rng.NextDouble();
            (x, y) = side switch
            {
                0 => (Field.Left - m, Field.Top + u * Field.Height), 1 => (Field.Right + m, Field.Top + u * Field.Height),
                2 => (Field.Left + u * Field.Width, Field.Top - m), _ => (Field.Left + u * Field.Width, Field.Bottom + m),
            };
        }
        FromSide[side]++;
        return Add(kind, x, y);
    }

    public Foe Add(Kind kind, double x, double y)
    {
        var f = new Foe
        {
            Kind = kind, X = x, Y = y,
            Hp = kind switch { Kind.Boss => 20 + (int)(Clock / 5), Kind.Beetle => 4 + (int)(Clock / 40), Kind.Wasp => 1 + (int)(Clock / 100), _ => 1 + (int)(Clock / 80) },
            Speed = kind switch { Kind.Boss => 44 + Clock * .1, Kind.Beetle => 36 + Clock * .12, Kind.Wasp => 90 + Clock * .13, _ => 45 + Clock * .13 },
        };
        Foes.Add(f);
        return f;
    }

    void MoveFoes(double dt)
    {
        foreach (var f in Foes)
        {
            double d = Dist(f);
            if (d < 1 || Clock - f.Hit < .1) continue; // 맞으면 잠깐 멈칫
            double step = Math.Min(d, f.Speed * sf * dt);
            f.X += (X - f.X) / d * step;
            f.Y += (Y - f.Y) / d * step;
        }
        for (int i = 0; i < Foes.Count; i++) // 한 덩어리로 겹치지 않게 서로 조금 밀어낸다
            for (int j = i + 1; j < Foes.Count; j++)
            {
                Foe a = Foes[i], b = Foes[j];
                double want = (FoeR(a) + FoeR(b)) * .8, d = Dist(a.X, a.Y, b.X, b.Y);
                if (d >= want || d < .01) continue;
                double push = (want - d) / 2, ux = (a.X - b.X) / d, uy = (a.Y - b.Y) / d;
                (a.X, a.Y, b.X, b.Y) = (a.X + ux * push, a.Y + uy * push, b.X - ux * push, b.Y - uy * push);
            }
    }

    // ── 칼: 벌레가 닿을 만큼 붙으면 그쪽으로 부채꼴을 한 번 휘두른다. 안에 든 벌레는 다 맞는다 (두 칸) ──
    void Sword(double dt)
    {
        swordCool -= dt;
        if (swordCool > 0) return;
        Foe? near = null;
        double nearD = double.MaxValue;
        foreach (var f in Foes) { double d = Dist(f); if (d < ReachPx + FoeR(f) && d < nearD) (near, nearD) = (f, d); }
        if (near == null) return;
        (swordCool, SwingAt, SwingDir) = (SwordSec, Clock, Math.Atan2(near.Y - Y, near.X - X));
        for (int i = 0; i < Foes.Count; i++)
        {
            var f = Foes[i];
            double d = Dist(f);
            if (d >= ReachPx + FoeR(f) || Math.Abs(Math.IEEERemainder(Math.Atan2(f.Y - Y, f.X - X) - SwingDir, 2 * Math.PI)) > ArcHalf + .01) continue;
            if (d > 1) { f.X += (f.X - X) / d * 12 * sf; f.Y += (f.Y - Y) / d * 12 * sf; } // 살짝 밀려난다
            int before = Foes.Count;
            Damage(f, 2);
            if (Foes.Count < before) i--; // 잡혀서 빠졌다
        }
        OnEvent?.Invoke("swing");
    }

    // ── 총: 닿는 거리 안에서 가장 가까운 벌레부터 총알 수만큼 하나씩 겨눠 쏜다. 벌레가 모자라면 남는 총알은 옆으로 벌려 쏜다 ──
    readonly Foe[] aim = new Foe[MaxBullets];
    readonly double[] aimD = new double[MaxBullets];

    void Gun(double dt)
    {
        gunCool -= dt;
        if (gunCool > 0) return;
        int want = Math.Min(Bullets, MaxBullets), count = 0;
        foreach (var f in Foes) // 가까운 순서로 want 마리 (거리가 같으면 먼저 온 벌레가 앞)
        {
            double d = Dist(f);
            if (d >= GunRange * sf) continue;
            int at = count;
            while (at > 0 && aimD[at - 1] > d) at--;
            if (at >= want) continue;
            if (count < want) count++;
            for (int i = count - 1; i > at; i--) (aim[i], aimD[i]) = (aim[i - 1], aimD[i - 1]);
            (aim[at], aimD[at]) = (f, d);
        }
        if (count == 0) return;
        (gunCool, FireAt, AimDir) = (GunSec, Clock, aim[0].X >= X ? 1 : -1);
        for (int i = 0; i < Bullets; i++)
        {
            var f = aim[i % count];
            int extra = i / count;
            double a = Math.Atan2(f.Y - Y, f.X - X) + (extra + 1) / 2 * (extra % 2 == 0 ? -.2 : .2);
            Shots.Add(new Shot { X = X, Y = Y, Vx = Math.Cos(a) * ShotSpeed * sf, Vy = Math.Sin(a) * ShotSpeed * sf, Until = Clock + GunRange * 1.25 / ShotSpeed });
        }
        Array.Clear(aim); // 잡힌 벌레를 붙들고 있지 않게
        OnEvent?.Invoke("fire");
    }

    void MoveShots(double dt)
    {
        for (int i = 0; i < Shots.Count; i++)
        {
            var s = Shots[i];
            s.X += s.Vx * dt;
            s.Y += s.Vy * dt;
            Foe? hit = null;
            foreach (var f in Foes) if (Dist(f.X, f.Y, s.X, s.Y) < FoeR(f) + 3 * sf) { hit = f; break; }
            if (hit != null) { Shots.RemoveAt(i--); Damage(hit, 1); }
            else if (Clock > s.Until) Shots.RemoveAt(i--);
        }
    }

    void Damage(Foe f, int n)
    {
        f.Hit = Clock;
        if ((f.Hp -= n) > 0) { OnEvent?.Invoke("hit"); return; }
        Foes.Remove(f);
        Kills++;
        Pops.Add(new Pop { X = f.X, Y = f.Y, At = Clock, R = FoeR(f) * 1.5 });
        Gems.Add(new Gem { X = f.X, Y = f.Y, Xp = f.Kind switch { Kind.Boss => 10, Kind.Beetle => 3, _ => 1 } });
        if (f.Kind == Kind.Boss) Gems.Add(new Gem { X = f.X + 14 * sf, Y = f.Y, Item = Item.Heart }); // 큰 놈은 하트도 남긴다
        // 신발: 보통 벌레를 잡으면 가끔. 처음 15초와 하나 떨어진 뒤 30초는 나오지 않고, 신은 것과 떨어져 있는 것을 합쳐 세 켤레까지만
        if (f.Kind != Kind.Boss && Shoes + shoesOut < MaxShoes && Clock >= shoeFrom && rng.NextDouble() < ShoeChance)
        {
            var (hx, hy) = Inside(f.X + 14 * sf, f.Y);
            Gems.Add(new Gem { X = hx, Y = hy, Item = Item.Shoe });
            (shoesOut, shoeFrom) = (shoesOut + 1, Clock + ShoeGap);
        }
        // 자석: 보통 벌레를 잡으면 가끔. 판 위에 하나만, 처음 20초·끄는 동안·끝난 뒤 90초는 나오지 않는다 (한 판에 두 번쯤.
        // 자주 나오면 흘린 보석을 다 주워 판이 훨씬 쉬워진다: 자동 조종 600판에서 쉬는 시간 60초면 88%, 90초면 80%, 없을 때 55% 가 끝까지 버텼다)
        if (f.Kind != Kind.Boss && !magnetOut && Clock >= magnetFrom && rng.NextDouble() < MagnetChance)
        {
            var (mx, my) = Inside(f.X - 14 * sf, f.Y);
            Gems.Add(new Gem { X = mx, Y = my, Item = Item.Magnet });
            magnetOut = true;
        }
        if (Gems.Count > MaxGems) // 가장 오래된 보석·하트가 사라진다 (자석·신발은 밀려나지 않는다)
        {
            int old = 0;
            while (Gems[old].Item >= Item.Magnet) old++;
            Gone.Add(Gems[old]);
            Gems.RemoveAt(old);
        }
        OnEvent?.Invoke(f.Kind == Kind.Boss ? "bigkill" : "kill");
    }

    // ── 닿으면 하트 하나. 둘레 벌레를 밀어내고 잠깐은 안 맞는다 ──
    void Touch()
    {
        if (Clock < SafeUntil) return;
        bool touched = false;
        foreach (var f in Foes) if (Dist(f) < FoeR(f) + BodyR * .8) { touched = true; break; }
        if (!touched) return;
        Lives--;
        (HurtAt, SafeUntil) = (Clock, Clock + SafeSec);
        Pops.Add(new Pop { X = X, Y = Y, At = Clock, R = 18 * sf });
        foreach (var f in Foes)
        {
            double d = Dist(f), away = 80 * sf + FoeR(f);
            if (d < away && d > .01) (f.X, f.Y) = (X + (f.X - X) / d * away, Y + (f.Y - Y) / d * away);
        }
        if (Lives <= 0) Go(Phase.Over, "over");
        else OnEvent?.Invoke("hurt");
    }

    // ── 보석: 가까이 가면 끌려와 주워진다. 다 차면 레벨이 오르고 하나가 세진다 ──
    // 자석을 주우면 MagnetSec 동안은 어디 있든 (그사이 떨어진 것도) 다 날아온다. 끝나면 날아오던 것도 그 자리에 선다
    void Pick(double dt)
    {
        for (int i = 0; i < Gems.Count; i++)
        {
            var g = Gems[i];
            bool all = Clock < MagnetUntil;
            double d = Dist(g.X, g.Y, X, Y);
            if (!all && d >= PickRange * sf) continue;
            if (d > 10 * sf)
            {
                double step = Math.Min(d, (all ? MagnetPull : 420) * sf * dt);
                (g.X, g.Y) = (g.X + (X - g.X) / d * step, g.Y + (Y - g.Y) / d * step);
                continue;
            }
            Gems.RemoveAt(i--);
            Gone.Add(g);
            switch (g.Item)
            {
                case Item.Magnet:
                    (MagnetUntil, magnetFrom, magnetOut, LastItem, ItemAt) = (Clock + MagnetSec, Clock + MagnetSec + MagnetGap, false, g.Item, Clock);
                    Magnets++;
                    OnEvent?.Invoke("magnet");
                    break;
                case Item.Shoe:
                    (Shoes, shoesOut, LastItem, ItemAt) = (Math.Min(MaxShoes, Shoes + 1), Math.Max(0, shoesOut - 1), g.Item, Clock);
                    OnEvent?.Invoke("shoe");
                    break;
                case Item.Heart: Lives = Math.Min(MaxLives, Lives + 1); OnEvent?.Invoke("heart"); break;
                default: Xp += g.Xp; break;
            }
        }
        for (; Xp >= Need; LevelUp()) Xp -= Need;
    }

    void LevelUp()
    {
        Level++;
        var can = new List<Up>(); // 덜 키운 것부터 고르게 (판마다 운이 너무 갈리지 않게). 하트가 처음보다 적으면 둘에 한 번은 하트
        if (GunSec > .32) can.Add(Up.Rate);
        if (Bullets < MaxBullets) can.Add(Up.Multi);
        if (ArcHalf < Math.PI) can.Add(Up.Arc);
        can = can.Where(u => ups[(int)u] == can.Min(v => ups[(int)v])).ToList();
        var up = can.Count == 0 || Lives < 3 && rng.Next(2) == 0 ? Up.Heart : can[rng.Next(can.Count)]; // 다 큰 뒤엔 하트만
        ups[(int)up]++;
        switch (up)
        {
            case Up.Rate: GunSec *= .85; break;
            case Up.Multi: Bullets++; break;
            case Up.Arc: (ArcHalf, Reach, SwordSec) = (Math.Min(Math.PI, ArcHalf + Math.PI / 9), Reach + 5, SwordSec * .93); break;
            case Up.Heart: Lives = Math.Min(MaxLives, Lives + 1); break;
        }
        (LastUp, UpAt) = (up, Clock);
        OnEvent?.Invoke("level");
    }

    // 시험·디버그용 자동 조종: 0.1초마다 열여섯 방향(과 제자리)으로 0.8초 앞을 내다보고 가장 나은 쪽을 고른다.
    // 가는 길에 벌레와 가장 가까워지는 틈이 넓을수록 좋고, 가장자리는 나쁘고, 보석에 가까워지면 좋다 (자석이 끄는 동안은 보석을 쫓지 않는다)
    (double Dx, double Dy) bot;
    double botAt = -9;

    public (double Dx, double Dy) BotSteer()
    {
        if (Clock - botAt < .1) return bot;
        botAt = Clock;
        double hx = Field.Width / 2.0, hy = Field.Height / 2.0, bestScore = double.MinValue;
        var (old, best) = (bot, (0.0, 0.0));
        for (int i = -1; i < 16; i++)
        {
            double dx = i < 0 ? 0 : Math.Cos(i * Math.PI / 8), dy = i < 0 ? 0 : Math.Sin(i * Math.PI / 8);
            double px = X, py = Y, gap = 90 * sf;
            for (int step = 1; step <= 8; step++)
            {
                (px, py) = Inside(px + dx * Speed * sf * .1, py + dy * Speed * sf * .1);
                if (Clock + step * .1 < SafeUntil) continue; // 안 맞는 동안은 뚫고 지나가도 된다
                foreach (var f in Foes) gap = Math.Min(gap, Dist(f.X, f.Y, px, py) - f.Speed * sf * .1 * step - FoeR(f) - BodyR);
            }
            double cx = (px - Field.Left - hx) / hx, cy = (py - Field.Top - hy) / hy;
            double score = gap / (30 * sf) + Math.Min(0, gap - 20 * sf) / (3 * sf) - (cx * cx * cx * cx + cy * cy * cy * cy) * 5 + ((dx, dy) == old ? .15 : 0);
            if (Gems.Count > 0 && !MagnetOn) score += 2 / (1 + Gems.Min(g => Dist(g.X, g.Y, px, py) - (g.Item != Item.Gem ? 60 * sf : 0)) / (80 * sf));
            if (score > bestScore) (bestScore, best) = (score, (dx, dy));
        }
        return bot = best;
    }
}
