namespace Bolok;

public enum ThingKind { Block, Fly, Seed, Heart }

// 길 위의 것: 장애물(땅·공중), 씨앗, 하트. X 는 왼쪽, Y 는 바닥선(화면 좌표)
public sealed class Thing
{
    public ThingKind Kind;
    public string Sprite = "";
    public int Art;                     // Runner.Arts 의 몇 번째 그림인지 (그리는 쪽이 이름을 다시 찾지 않게)
    public int Scale, W, H, Ox, Oy;     // Scale: 그림 배율(화면 px). Ox·Oy: 칸 왼쪽 위에서 실제 그림까지 (그릴 때 쓴다)
    public double X, Y;
    public bool Gone;
    public Rectangle Box => new((int)X, (int)(Y - H), W, H);
}

// 볼록이 몸 치수 (칸 px: 그림 한 칸이 40). 귀는 뺀 몸통이다. 붙이는 쪽이 그림 데이터(PetArt.Size)로 계산해 넘긴다
public readonly record struct RunBody(double W, double Stand, double Duck)
{
    public const double DuckDrop = 5;   // 엎드린 자세(land, sq 5)는 몸 꼭대기가 5칸px 낮다
    // 몸 반지름 rx·ry 에서: 폭 2·rx, 선 키 2·ry + 4 (발이 칸의 37번째 줄, 칸 바닥이 땅)
    public static RunBody Of(double rx, double ry) => new(2 * rx, 2 * ry + 4, 2 * ry + 4 - DuckDrop);
}

// 길 위에 나오는 그림 하나 (이름 + 배율)와 잰 크기
public readonly record struct RunArt(string Sprite, int Scale, int W, int H, int Ox, int Oy);

// 쿠키런 모드: 볼록이는 제자리에서 달리고 길이 왼쪽으로 흐른다. Ctrl 톡 점프(두 번이면 더블), Shift 누르면 슬라이드,
// 끝은 Esc (보스 키, PetForm.BossKey — YELL 2026-11-13: 게임에서 Esc 는 언제나 빠져나가기). Ctrl+Shift 를 0.5초 누르는 옛 끝내기도 남겨 둔다.
// 체력은 저절로 닳고 부딪히면 크게 닳는다. 레벨마다 목표 점수가 있고, 넘기면 그 판 안에서 바로 다음 레벨로 올라 더 어려워진다.
// 레벨은 저장해서 다음 판은 그 레벨에서 시작한다. 화면과 무관한 규칙이라 --selfcheck 로 검사한다.
// 판정은 몸 크기를 따른다 (YELL 2026-11-13): 아기는 낮고 좁게, 어른은 크게. 구름·장애물·씨앗 높이도 선 키(S)에 맞춘다
public sealed class Runner
{
    public readonly Rectangle Track;    // 달리는 모니터
    public readonly double Ground;      // 볼록이 발이 닿는 선
    public readonly double PetX;        // 볼록이 자리 (화면 왼쪽에서 22%)
    public readonly RunBody Body;
    public readonly int PetScale;       // 볼록이 배율: 칸 px 하나가 화면 몇 px 인지
    public readonly int BoxW, StandPx, DuckPx;  // 판정 상자 (화면 px): 폭은 몸 폭의 0.8, 높이는 선 키·엎드린 키
    public readonly double CloudUp;     // 구름 바닥 높이: 선 키와 엎드린 키의 한가운데 — 서면 맞고 엎드리면 지나가는 여유가 위아래 같다
    public readonly double NormalMax, TallMax;  // 땅 장애물 높이 한도: 보통은 선 키의 0.85배, 키 큰 것은 1.3배
    readonly double sf;                 // 속도·거리 배율 (배율 3 이 1)

    public double Lift, Vy, Speed, Energy = 100, Distance, Clock, HurtUntil, InvUntil, LevelAt = -9;
    public int Jumps, Seeds, Level;
    public bool Sliding, Over;
    public int Score => Seeds * 10 + (int)(Distance / (100 * sf));
    public int LevelScore => Score - levelFrom;     // 이번 레벨에서 번 점수
    public readonly List<Thing> Things = new();
    public Action<string>? OnEvent;
    readonly Random rng;
    readonly Func<string, int, (int W, int H, int Ox, int Oy)> measure;
    double nextSpawn;
    int levelFrom;

    // 길 위에 나올 수 있는 그림들. 잰 값은 한 번만 재서 여기 둔다
    readonly List<RunArt> arts = new();
    readonly Dictionary<(string, int), int> artIndex = new();
    public IReadOnlyList<RunArt> Arts => arts;
    readonly List<int> small = new(), normal = new(), tall = new();     // 땅 장애물 후보 (arts 번호): 가장 작은 배율 / 보통 / 키 큰 것
    public IReadOnlyList<int> NormalBlocks => normal;
    public IReadOnlyList<int> TallBlocks => tall;
    readonly int seedArt, heartArt, cloudArt;

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

    public Runner(Rectangle track, double ground, int cell, RunBody body, Random rng, Func<string, int, (int, int, int, int)> measure, int level = 1)
    {
        (Track, Ground, Body, this.rng, this.measure) = (track, ground, body, rng, measure);
        PetScale = Math.Max(1, cell / 40);
        sf = cell / 120.0;
        double u = cell / 40.0;
        BoxW = Math.Max(2, (int)Math.Round(body.W * .8 * u));
        StandPx = Math.Max(2, (int)Math.Round(body.Stand * u));
        DuckPx = Math.Clamp((int)Math.Round(body.Duck * u), 1, StandPx - 1);
        CloudUp = (StandPx + DuckPx) / 2.0;
        (NormalMax, TallMax) = (.85 * StandPx, 1.3 * StandPx);
        Level = Math.Max(1, level);
        PetX = track.Left + track.Width * .22;
        Speed = StartSpeedOf(Level) * sf;
        nextSpawn = 500 * sf; // 처음엔 조금 달리다가

        (seedArt, heartArt, cloudArt) = (ArtOf("icon/seed", Px(1)), ArtOf("icon/heart", Px(1)), ArtOf("prop/cloud", Px(2)));
        // 땅 장애물을 키로 나눈다. 같은 그림도 몸이 작으면 키 큰 장애물이 되고, 너무 크면 안 나온다
        int lowest = -1, last = 0;
        for (int n = 1; n <= 3; n++)
        {
            int px = Px(n);
            if (px == last) continue; // 작은 배율에서는 1·2 가 같은 크기가 된다
            last = px;
            foreach (var name in Ground1)
            {
                int id = ArtOf(name, px), h = arts[id].H;
                if (lowest < 0 || h < arts[lowest].H) lowest = id;
                if (h <= NormalMax) { normal.Add(id); if (n == 1) small.Add(id); }
                else if (h <= TallMax) tall.Add(id);
            }
        }
        if (normal.Count == 0) normal.Add(lowest);  // 몸이 아주 작아도 넘을 것은 있어야 한다
        if (small.Count == 0) small.AddRange(normal);
        if (tall.Count == 0) { int top = normal[0]; foreach (int id in normal) if (arts[id].H > arts[top].H) top = id; tall.Add(top); }
    }

    // 그림 배율: 배율 3 에서 n 이던 것을 볼록이 배율에 맞춘다 (모니터 배율이 커지면 장애물도 같이 커진다)
    public int Px(int n) => Math.Max(1, (int)Math.Round(n * PetScale / 3.0));

    int ArtOf(string sprite, int scale)
    {
        if (artIndex.TryGetValue((sprite, scale), out int id)) return id;
        var (w, h, ox, oy) = measure(sprite, scale);
        arts.Add(new RunArt(sprite, scale, w, h, ox, oy));
        return artIndex[(sprite, scale)] = arts.Count - 1;
    }

    // 볼록이 몸. 슬라이드하면 낮아진다
    public Rectangle PetBox
    {
        get
        {
            int h = Sliding && Lift == 0 ? DuckPx : StandPx;
            return new Rectangle((int)(PetX - BoxW / 2.0), (int)(Ground - Lift) - h, BoxW, h);
        }
    }

    public void Jump()
    {
        if (Over || Jumps >= 2) return;
        Vy = -JumpSpeed * sf * (Jumps == 0 ? 1 : .85);
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

    // scale 은 그림 배율(화면 px). 길에 저절로 나오는 것은 Px(n) 으로 볼록이 배율을 따른다
    public Thing Add(ThingKind kind, string sprite, int scale, double x, double bottom) => Put(kind, ArtOf(sprite, scale), x, bottom);

    Thing Put(ThingKind kind, int art, double x, double bottom)
    {
        var a = arts[art];
        var t = new Thing { Kind = kind, Sprite = a.Sprite, Art = art, Scale = a.Scale, W = a.W, H = a.H, Ox = a.Ox, Oy = a.Oy, X = x, Y = bottom };
        Things.Add(t);
        return t;
    }

    void Spawn()
    {
        double x = Track.Right + 40;
        Thing Block(double at, List<int> pool) => Put(ThingKind.Block, pool[rng.Next(pool.Count)], at, Ground);
        Thing Cloud(double at) => Put(ThingKind.Fly, cloudArt, at, Ground - CloudUp);
        void Seed(double at, double up) => Put(ThingKind.Seed, seedArt, at, Ground - up);
        void Arch(Thing o) { for (int i = 0; i < 3; i++) Seed(o.X + o.W / 2.0 - 34 * sf + i * 34 * sf, o.H + (i == 1 ? 70 : 50) * sf); }
        double end = x; // 이번 묶음 끝: 마지막 장애물의 오른쪽
        if (rng.NextDouble() < .12) // 씨앗 무지개(선 키의 0.3~1배 높이), 가끔 하트(선 키의 2배 높이: 뛰어야 닿는다)
        {
            for (int i = 0; i < 6; i++) Seed(x + i * 34 * sf, (.3 + .7 * Math.Sin(i / 5.0 * Math.PI)) * StandPx);
            end = x + 6 * 34 * sf;
            if (rng.NextDouble() < HeartChanceOf(Level)) Put(ThingKind.Heart, heartArt, x + 100 * sf, Ground - 2 * StandPx);
        }
        else if (rng.NextDouble() < HardShareOf(Level))
        {
            switch (rng.Next(3))
            {
                case 0: // 키 큰 장애물: 일찍 뛰거나 더블 점프
                    var t = Block(x + 120 * sf, tall);
                    Arch(t);
                    end = t.X + t.W;
                    break;
                case 1: // 연달아 둘: 한 번에 넘기엔 멀고, 착지하자마자 뛰면 넘는다. 빨라지면 사이도 함께 벌어진다
                    var a = Block(x + 100 * sf, normal);
                    var b = Block(a.X + a.W + Speed * Rand(.55, .65), normal);
                    Arch(a); Arch(b);
                    end = b.X + b.W;
                    break;
                default: // 구름 밑으로 미끄러진 직후 땅 장애물: 일어나서 뛸 틈은 0.3~0.4초
                    var o = Cloud(x + 120 * sf);
                    var c = Block(o.X + o.W + 60 * sf + Speed * Rand(.3, .42), small);
                    Arch(c);
                    end = c.X + c.W;
                    break;
            }
        }
        else if (rng.NextDouble() < .6) // 땅 장애물 하나: 점프로 넘는다. 앞에 씨앗 줄, 위에 씨앗 아치
        {
            var o = Block(x + 160 * sf, normal);
            for (int i = 0; i < 4; i++) Seed(x + i * 34 * sf - 40 * sf, 18 * sf);
            Arch(o);
            end = o.X + o.W;
        }
        else // 공중 장애물: 슬라이드로 밑을 지난다
        {
            var o = Cloud(x + 120 * sf);
            for (int i = 0; i < 5; i++) Seed(o.X - 30 * sf + i * 34 * sf, 10 * sf);
            end = o.X + o.W;
        }
        // 다음 묶음은 이번 묶음이 지나가고 틈(초) 뒤에. 레벨이 오를수록 틈이 짧다
        nextSpawn = Distance + (end - x) + Speed * TimeGapOf(Level) * Rand(1, 1.4);
    }

    double Rand(double a, double b) => a + rng.NextDouble() * (b - a);

    // 부딪히는 자리. 살짝 봐준다: 땅 장애물은 사방 1/6, 구름은 가로만 (구름 바닥은 눈에 보이는 그대로여야 엎드린 머리 위 틈이 맞다)
    public static Rectangle Hard(Thing t)
    {
        var box = t.Box;
        return Rectangle.Inflate(box, -box.Width / 6, t.Kind == ThingKind.Block ? -box.Height / 6 : 0);
    }

    public void Update(double dt)
    {
        if (Over) return;
        Clock += dt;
        Distance += Speed * dt;
        Speed = Math.Min(MaxSpeedOf(Level) * sf, Speed + AccelOf(Level) * sf * dt); // 점점 빨라진다
        Energy -= Drain * dt;
        if (Distance >= nextSpawn) Spawn();

        if (Lift > 0 || Vy < 0) // 점프: 중력으로 내려온다
        {
            Vy += Gravity * sf * dt;
            Lift -= Vy * dt;
            if (Lift <= 0) { Lift = 0; Vy = 0; Jumps = 0; OnEvent?.Invoke("land"); }
        }

        // 한 번 돌면서 옮기고, 부딪힘·줍기를 보고, 지나간 것·먹은 것은 그 자리에서 빼낸다 (프레임마다 새로 잡는 메모리가 없게)
        var me = PetBox;
        var grab = Rectangle.Inflate(me, (int)(8 * sf), (int)(8 * sf));
        double move = Speed * dt, off = Track.Left - 20;
        bool safe = Clock < InvUntil;
        int keep = 0;
        for (int i = 0; i < Things.Count; i++)
        {
            var t = Things[i];
            t.X -= move;
            if (t.X + t.W < off) continue;
            if (t.Kind is ThingKind.Block or ThingKind.Fly)
            {
                if (!safe && Hard(t).IntersectsWith(me))
                {
                    Energy -= HitCostOf(Level);
                    (HurtUntil, InvUntil) = (Clock + .35, Clock + .8); // 0.8초 동안은 안 다친다
                    safe = true;
                    OnEvent?.Invoke("hit");
                }
            }
            else if (t.Box.IntersectsWith(grab))
            {
                t.Gone = true;
                if (t.Kind == ThingKind.Seed) { Seeds++; OnEvent?.Invoke("seed"); }
                else { Energy = Math.Min(100, Energy + HeartGain); OnEvent?.Invoke("heart"); }
                continue;
            }
            Things[keep++] = t;
        }
        if (keep < Things.Count) Things.RemoveRange(keep, Things.Count - keep);
        if (Energy <= 0) { Energy = 0; Over = true; OnEvent?.Invoke("over"); return; }

        if (LevelScore >= GoalOf(Level)) // 목표를 넘기면 그 자리에서 다음 레벨
        {
            Level++;
            levelFrom = Score;
            LevelAt = Clock;
            Speed = Math.Max(Speed, StartSpeedOf(Level) * sf);
            OnEvent?.Invoke("level");
        }
    }
}
