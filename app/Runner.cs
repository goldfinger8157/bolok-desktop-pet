namespace Bolok;

// 길 위의 것: 장애물(땅·공중), 씨앗, 하트. X 는 왼쪽, Y 는 바닥선(화면 좌표)
public sealed class Thing
{
    public string Kind = "", Sprite = "";
    public int Scale, W, H, Ox, Oy;     // Ox·Oy: 칸 왼쪽 위에서 실제 그림까지 (그릴 때 쓴다)
    public double X, Y;
    public bool Gone;
    public Rectangle Box => new((int)X, (int)(Y - H), W, H);
}

// 쿠키런 모드: 볼록이는 제자리에서 달리고 길이 왼쪽으로 흐른다. Ctrl 톡 점프(두 번이면 더블), Shift 누르면 슬라이드,
// Ctrl+Shift 를 0.5초 누르면 끝 (Esc 는 일하던 창에도 가서 대화상자를 닫아 버린다).
// 체력은 저절로 닳고 부딪히면 크게 닳는다. 레벨마다 목표 점수가 있고, 넘기면 그 판 안에서 바로 다음 레벨로 올라 더 어려워진다.
// 레벨은 저장해서 다음 판은 그 레벨에서 시작한다. 화면과 무관한 규칙이라 --selfcheck 로 검사한다
public sealed class Runner
{
    public readonly Rectangle Track;    // 달리는 모니터
    public readonly double Ground;      // 볼록이 발이 닿는 선
    public readonly double PetX;        // 볼록이 자리 (화면 왼쪽에서 22%)
    readonly int cell;
    double Sf => cell / 120.0;

    public double Lift, Vy, Speed, Energy = 100, Distance, Clock, HurtUntil, InvUntil, LevelAt = -9;
    public int Jumps, Seeds, Level;
    public bool Sliding, Over;
    public int Score => Seeds * 10 + (int)(Distance / (100 * Sf));
    public int LevelScore => Score - levelFrom;     // 이번 레벨에서 번 점수
    public readonly List<Thing> Things = new();
    public Action<string>? OnEvent;
    readonly Random rng;
    readonly Func<string, int, (int W, int H, int Ox, int Oy)> measure;
    double nextSpawn;
    int levelFrom;

    // 0.8초 가까이 떠 있어서 넘을 타이밍에 여유가 있다
    public const double Gravity = 2400, JumpSpeed = 950, Drain = 2, HeartGain = 15, QuitHold = .5;
    bool ctrlWas;
    double bothHeld;
    static readonly string[] Ground1 = { "prop/acorn", "prop/mushroom", "prop/cactus", "prop/cheese", "prop/strawberry", "prop/cupcake", "prop/apple" };

    // 레벨별 난이도. 12레벨부터는 더 어려워지지 않는다
    static int Step(int level) => Math.Clamp(level, 1, 12) - 1;
    public static double StartSpeedOf(int level) => 380 + 22 * Step(level);
    public static double MaxSpeedOf(int level) => 660 + 28 * Step(level);
    static double AccelOf(int level) => 10 + Step(level);
    public static double HitCostOf(int level) => 22 + Step(level);
    static double HardShareOf(int level) => .25 + .04 * Step(level);   // 어려운 묶음(키 큰 것·연달아 둘·구름 뒤 장애물)이 나올 몫
    static double TimeGapOf(int level) => 1 - .04 * Step(level);       // 묶음 사이 틈(초): 내려와서 다음 것을 볼 시간. 빨라져도 시간은 같다
    static double HeartChanceOf(int level) => .35 - .02 * Step(level);
    public static int GoalOf(int level) => 700 + 40 * (level - 1);     // 이번 레벨에서 이만큼 벌면 다음 레벨. 실수 없이 달리면 한 판에 1200점쯤

    public Runner(Rectangle track, double ground, int cell, Random rng, Func<string, int, (int, int, int, int)> measure, int level = 1)
    {
        (Track, Ground, this.cell, this.rng, this.measure) = (track, ground, cell, rng, measure);
        Level = Math.Max(1, level);
        PetX = track.Left + track.Width * .22;
        Speed = StartSpeedOf(Level) * Sf;
        nextSpawn = 500 * Sf; // 처음엔 조금 달리다가
    }

    // 볼록이 몸. 슬라이드하면 낮아진다
    public Rectangle PetBox
    {
        get
        {
            int w = (int)(cell * .5), h = (int)(cell * (Sliding && Lift == 0 ? .36 : .72));
            return new Rectangle((int)(PetX - w / 2.0), (int)(Ground - Lift - h), w, h);
        }
    }

    public void Jump()
    {
        if (Over || Jumps >= 2) return;
        Vy = -JumpSpeed * Sf * (Jumps == 0 ? 1 : .85);
        Jumps++;
        Sliding = false;
        OnEvent?.Invoke(Jumps == 1 ? "jump" : "jump2");
    }

    public void Slide(bool on) => Sliding = on && Lift == 0;

    // 키 상태를 받는다. Ctrl 누르는 순간 점프, Shift 누르는 동안 슬라이드, 둘을 0.5초 함께 누르면 끝 — 끝내려면 true.
    // 슬라이드 중에 Ctrl 을 톡 쳐서 뛰는 건 짧아서 안 끝난다
    public bool Keys(bool ctrl, bool shift, double dt)
    {
        if (ctrl && !ctrlWas) Jump();
        ctrlWas = ctrl;
        Slide(shift);
        bothHeld = ctrl && shift ? bothHeld + dt : 0;
        return bothHeld >= QuitHold;
    }

    public Thing Add(string kind, string sprite, int scale, double x, double bottom)
    {
        var (w, h, ox, oy) = measure(sprite, scale);
        var t = new Thing { Kind = kind, Sprite = sprite, Scale = scale, W = w, H = h, Ox = ox, Oy = oy, X = x, Y = bottom };
        Things.Add(t);
        return t;
    }

    void Spawn()
    {
        double x = Track.Right + 40;
        Thing Block(double at, int scale) => Add("block", Ground1[rng.Next(Ground1.Length)], scale, at, Ground);
        Thing Cloud(double at) => Add("fly", "prop/cloud", 2, at, Ground - 58 * Sf);
        void Arch(Thing o) { for (int i = 0; i < 3; i++) Add("seed", "icon/seed", 1, o.X + o.W / 2.0 - 34 * Sf + i * 34 * Sf, Ground - o.H - (i == 1 ? 70 : 50) * Sf); }
        double end = x; // 이번 묶음 끝: 마지막 장애물의 오른쪽
        if (rng.NextDouble() < .12) // 씨앗 무지개, 가끔 하트
        {
            for (int i = 0; i < 6; i++) Add("seed", "icon/seed", 1, x + i * 34 * Sf, Ground - (20 + 40 * Math.Sin(i / 5.0 * Math.PI)) * Sf);
            end = x + 6 * 34 * Sf;
            if (rng.NextDouble() < HeartChanceOf(Level)) Add("heart", "icon/heart", 1, x + 100 * Sf, Ground - 110 * Sf);
        }
        else if (rng.NextDouble() < HardShareOf(Level))
        {
            switch (rng.Next(3))
            {
                case 0: // 키 큰 장애물: 일찍 뛰거나 더블 점프
                    var t = Block(x + 120 * Sf, 3);
                    Arch(t);
                    end = t.X + t.W;
                    break;
                case 1: // 연달아 둘: 한 번에 넘기엔 멀고, 착지하자마자 뛰면 넘는다. 빨라지면 사이도 함께 벌어진다
                    var a = Block(x + 100 * Sf, rng.Next(1, 3));
                    var b = Block(a.X + a.W + Speed * Rand(.55, .65), rng.Next(1, 3));
                    Arch(a); Arch(b);
                    end = b.X + b.W;
                    break;
                default: // 구름 밑으로 미끄러진 직후 땅 장애물: 일어나서 뛸 틈은 0.3~0.4초
                    var o = Cloud(x + 120 * Sf);
                    var c = Block(o.X + o.W + 60 * Sf + Speed * Rand(.3, .42), 1);
                    Arch(c);
                    end = c.X + c.W;
                    break;
            }
        }
        else if (rng.NextDouble() < .6) // 땅 장애물 하나: 점프로 넘는다. 앞에 씨앗 줄, 위에 씨앗 아치
        {
            var o = Block(x + 160 * Sf, rng.Next(1, 3));
            for (int i = 0; i < 4; i++) Add("seed", "icon/seed", 1, x + i * 34 * Sf - 40 * Sf, Ground - 18 * Sf);
            Arch(o);
            end = o.X + o.W;
        }
        else // 공중 장애물: 슬라이드로 밑을 지난다
        {
            var o = Cloud(x + 120 * Sf);
            for (int i = 0; i < 5; i++) Add("seed", "icon/seed", 1, o.X - 30 * Sf + i * 34 * Sf, Ground - 10 * Sf);
            end = o.X + o.W;
        }
        // 다음 묶음은 이번 묶음이 지나가고 틈(초) 뒤에. 레벨이 오를수록 틈이 짧다
        nextSpawn = Distance + (end - x) + Speed * TimeGapOf(Level) * Rand(1, 1.4);
    }

    double Rand(double a, double b) => a + rng.NextDouble() * (b - a);

    public void Update(double dt)
    {
        if (Over) return;
        Clock += dt;
        Distance += Speed * dt;
        Speed = Math.Min(MaxSpeedOf(Level) * Sf, Speed + AccelOf(Level) * Sf * dt); // 점점 빨라진다
        Energy -= Drain * dt;
        if (Distance >= nextSpawn) Spawn();

        if (Lift > 0 || Vy < 0) // 점프: 중력으로 내려온다
        {
            Vy += Gravity * Sf * dt;
            Lift -= Vy * dt;
            if (Lift <= 0) { Lift = 0; Vy = 0; Jumps = 0; OnEvent?.Invoke("land"); }
        }

        var me = PetBox;
        foreach (var t in Things)
        {
            t.X -= Speed * dt;
            if (t.Gone) continue;
            var box = t.Box;
            if (t.Kind is "block" or "fly")
            {
                if (Clock < InvUntil) continue;
                var hard = Rectangle.Inflate(box, -box.Width / 6, -box.Height / 6); // 살짝 봐준다
                if (!hard.IntersectsWith(me)) continue;
                Energy -= HitCostOf(Level);
                (HurtUntil, InvUntil) = (Clock + .35, Clock + .8); // 0.8초 동안은 안 다친다
                OnEvent?.Invoke("hit");
            }
            else if (box.IntersectsWith(Rectangle.Inflate(me, (int)(8 * Sf), (int)(8 * Sf))))
            {
                t.Gone = true;
                if (t.Kind == "seed") { Seeds++; OnEvent?.Invoke("seed"); }
                else { Energy = Math.Min(100, Energy + HeartGain); OnEvent?.Invoke("heart"); }
            }
        }
        Things.RemoveAll(t => t.X + t.W < Track.Left - 20 || (t.Gone && t.Kind != "block"));
        if (Energy <= 0) { Energy = 0; Over = true; OnEvent?.Invoke("over"); return; }

        if (LevelScore >= GoalOf(Level)) // 목표를 넘기면 그 자리에서 다음 레벨
        {
            Level++;
            levelFrom = Score;
            LevelAt = Clock;
            Speed = Math.Max(Speed, StartSpeedOf(Level) * Sf);
            OnEvent?.Invoke("level");
        }
    }
}
