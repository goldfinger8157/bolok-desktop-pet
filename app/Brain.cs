namespace Bolok;

public enum Mode { Wander, Anim, GoBite, Chase, Held, Thrown, GoHide, SlipIn, Hiding, Emerge, GoNap, Perch, Prey, Run, Paddle, Puppet }

public sealed class Hole { public int Cx, Cy; public readonly List<Point> Cells = new(); }
public sealed class Bit { public double X, Y, Vx, Vy, G, Life; public Color C; }
public sealed class Heart { public double X, Y, T; }
public sealed class Seed { public double X, Y, Sx, Sy, T; public bool Reject; } // S 는 던진 자리, T 는 날아간 비율

// 행동: 모델을 돌리고, 그 결과를 화면 위의 움직임으로 바꾼다. 그리기는 PetForm·Overlay 가 맡는다
public sealed class Brain
{
    public Pet P;
    public Tuning K;
    public World World;                 // 모니터 배치
    public Rectangle Area;              // 지금 있는 모니터의 작업 영역
    public int Scale = 3;
    public int CellPx => 40 * Scale;
    double S => Scale / 3.0;            // 배율 3 을 기준으로 잡은 거리·속도에 곱한다

    public double X, Y;                 // 발 위치 (화면 좌표)
    public int Dir = 1;
    public Mode Mode = Mode.Wander;
    public string Pose = "idle";
    public double Speed = 1;            // 시간 배속. 디버그 전용
    public double Now;                  // 앱이 켜진 뒤 흐른 초
    public Point Mouse;
    public double ShakeUntil;
    public string EmoteName = "";       // 머리 위에 잠깐 뜨는 그림 (emo/이름)
    public double EmoteAt, EmoteUntil;
    public bool Emoting => Now < EmoteUntil;

    public readonly List<Hole> Holes = new();
    public readonly List<Bit> Bits = new();
    public readonly List<Heart> Hearts = new();
    public readonly List<Seed> Foods = new(); // 날아오는 중인 씨앗
    public bool PlayingAlone => solo;
    public int HolesVersion;

    public bool Behind;                 // true 면 PetForm 이 HideWnd 바로 아래로 내려 준다
    public IntPtr HideWnd;
    public Func<List<Win>> Windows = () => new();
    public Func<Win?> Foreground = () => null;
    public Win? Active;                 // 지금 활성인 창
    public Func<IntPtr, WinState> Probe = Native.Probe;
    public Func<int, int, int, Color[]> Sample = Native.SampleScreen;

    double until, next, tx, ty, nextPet, nextToss;
    double vx, vy, throwSpeed, squashUntil;
    Peek peek;
    Rectangle hideRect;
    double nextCall, comboAt, nextNap, jukeAt, hurtUntil, preyVx, preyVy, coverUntil, nextCover;
    public Rectangle Arena;             // 총쏘기 게임에서 도망 다니는 모니터
    List<Cover> covers = new();         // 숨을 수 있는 창들
    (double X, double Y, Cover C)? cover;
    bool ducking;
    public Cover? CoverProp;            // 숨은 창. 그 안쪽 몸은 안 그리고 안 맞는다
    Peek perch;
    double watchAt, bobAt, runUntil, nextHide, activeAt, activeSeen, startleOk, rollV, flipAt, lastPoke = -99, pokeShown;
    int combo;
    string? lastKind;
    bool ducked, fleeing, knocked, solo, tailSpin;
    Action? then;
    readonly List<(double T, Point P)> trail = new(); // 들고 있는 동안의 마우스 자취
    readonly List<(double T, Point P)> shakeTrail = new();
    double shakenFor;
    bool spinning, dizzyNext;
    (double X, double Y)? spot, pending;
    readonly Random rng = new();

    public Brain(Pet p, Tuning k, Rectangle area) : this(p, k, new World(new[] { area }), area) { }

    public Brain(Pet p, Tuning k, World world, Rectangle area)
    {
        P = p; K = k; World = world; Area = area;
        X = area.Left + area.Width / 2.0;
        Y = MaxY;
        nextHide = Rand(k.HideMinSec, k.HideMaxSec);
    }

    double Rand(double a, double b) => a + rng.NextDouble() * (b - a);
    double MinX => Area.Left + CellPx / 2.0;
    double MaxX => Area.Right - CellPx / 2.0;
    double MinY => Area.Top + CellPx;
    double MaxY => Area.Bottom - K.FloorGap;

    bool Holding => Now < lastPoke + K.ClickHoldSec;                      // 눌린 직후라 제자리에 있는 중
    public bool ClickPadOn => Now < lastPoke + 1.6 && Mode is Mode.Anim or Mode.Wander;

    // 글자 말풍선: 안내가 꼭 필요할 때만 (보통은 이모지). 그동안 제자리에서 갸웃한다
    public string[]? SayLines;
    public double SayUntil;
    public bool Saying => Now < SayUntil && SayLines != null;

    public void Say(double sec, params string[] lines)
    {
        (SayLines, SayUntil) = (lines, Now + sec);
        if (Mode is Mode.Wander or Mode.Anim) Play("think", sec);
        Dbg.Log("say", ("text", string.Join(" / ", lines)));
    }

    public void Emote(string name, double sec = 1.3)
    {
        (EmoteName, EmoteAt, EmoteUntil) = (name, Now, Now + sec);
        Dbg.Log("emote", ("name", name));
    }

    // 그림이 뜰 머리 위 높이. 작을수록 머리가 낮다
    public double HeadY => Y - CellPx * PetArt.Embedded.HeadRatio(P.Stage) - 6 * S; // 몸 크기는 그림 데이터에서

    void Play(string pose, double sec, Action? after = null)
    {
        if (Mode is Mode.Prey or Mode.Run or Mode.Paddle or Mode.Puppet) return; // 게임 중에는 다른 동작이 끼어들지 않는다
        Mode = Mode.Anim; Pose = pose; until = Now + sec; then = after;
        solo = false; tailSpin = false; fleeing = false;
    }

    void Refuse(string emote) { ShakeUntil = Now + .5; Emote(emote); }

    public string SpriteName()
    {
        if (P.Stage == "egg") return P.AgeMin >= K.EggMin * .6 ? "egg/hatch" : "egg/idle";
        return $"{P.Stage}/{P.Cheek(K)}/{Pose}";
    }

    public void Care(string what)
    {
        Dbg.Log("care", ("what", what));
        if (Mode == Mode.Thrown) return;
        if (what == "pet" && Mode is Mode.Hiding or Mode.SlipIn or Mode.Emerge) { React("poked"); return; }
        if (P.Stage == "egg") { Refuse("dots"); return; }
        if (P.Asleep && what != "wake") { Refuse("zzz"); return; }
        switch (what)
        {
            case "feed":
                if (!P.Act(K, "feed")) { TossSeed(true); return; } // 배불러도 던지기는 보이고, 닿으면 고개를 젓는다
                TossSeed(false);
                if (Mode == Mode.Anim && Pose == "eat" && !solo) until = Math.Min(Now + 2.5, until + .7); // 연달아 주면 더 오물오물
                else Play("happy", K.TossSec, () => Play("eat", 1.1)); // 씨앗이 입에 닿으면 먹기 시작한다
                break;
            case "pet":
                Poke(20.5, 26);
                break;
            case "play":
                if (!P.Act(K, "play")) { Refuse("sweat"); return; }
                Mode = Mode.Chase; until = Now + 8;
                Emote("note");
                break;
            case "sleep":
                if (P.Energy > 80) { Refuse("no"); return; }
                if (TryNap()) break; // 올라갈 창이 없으면 그 자리에서 잔다
                P.Act(K, "sleep");
                Mode = Mode.Wander;
                Emote("zzz");
                break;
            case "wake":
                P.Act(K, "wake");
                Emote("vein");
                break;
        }
    }

    // 마우스 클릭의 입구. 돌봄과 같은 조건을 거친다
    public void Click(double x, double y)
    {
        Dbg.Log("care", ("what", "click"));
        if (Mode == Mode.Thrown) return;
        lastPoke = Now;
        if (Mode is Mode.Hiding or Mode.SlipIn or Mode.Emerge) { React("poked"); return; }
        if (P.Stage == "egg") { Refuse("dots"); return; }
        Poke(x, y);
    }

    // 마우스 손짓 (PetForm 이 커서 움직임에서 알아낸다, Gestures.cs): 쓰다듬기·툭툭은 쓰다듬기로 쳐 주고, 찌르기는 깜짝
    public void Gesture(string kind)
    {
        if (P.Stage == "egg" || Mode is Mode.Thrown or Mode.Held or Mode.Puppet or Mode.Hiding or Mode.SlipIn or Mode.Emerge or Mode.Run or Mode.Paddle or Mode.Prey) return;
        Dbg.Log("gesture", ("kind", kind), ("asleep", P.Asleep));
        if (kind != "poke" && Now >= nextPet) { P.Act(K, "pet"); nextPet = Now + K.PetCooldownSec; }
        if (P.Asleep) { if (kind != "poke") Emote("heart", 1.2); return; } // 자다가 쓰다듬으면 배시시 (깨지는 않는다)
        lastPoke = Now;
        switch (kind)
        {
            case "stroke": Play("nuzzle", 1.4); Heart(); break;
            case "pat": Play("giggle", 1.1); Emote("note"); break;
            case "poke": Play("squish", .8); Emote("bang", .8); break;
        }
    }

    void Heart()
    {
        if (Hearts.Count < 3) Hearts.Add(new Heart { X = X + Rand(-30, 30) * S, Y = Y - CellPx * .95 });
    }

    // 클릭: x·y 는 칸(40×40) 안에서 누른 자리. 누른 곳과 연타 횟수에 따라 반응이 달라진다
    public void Poke(double x, double y)
    {
        combo = Now - comboAt < 1.4 ? combo + 1 : 1;
        comboAt = lastPoke = Now;
        string kind = Clicks.Pick(Clicks.Zone(P.Stage, x, y), combo, P.Stage, P.Mood, lastKind, rng);
        bool annoyed = kind is "pout" or "nope" or "nip";

        if (!annoyed && Now >= nextPet) { P.Act(K, "pet"); nextPet = Now + K.PetCooldownSec; }
        // 빠르게 누르면 지금 동작을 조금은 보여 준 뒤에 바꾼다. 안 그러면 파닥거리기만 한다
        if (Mode == Mode.Anim && !solo && Now < pokeShown && !annoyed) { if (kind != "squish") Heart(); return; }

        Dbg.Log("poke", ("kind", kind), ("combo", combo));
        lastKind = kind;
        pokeShown = Now + .6;
        switch (kind)
        {
            case "nuzzle": Play("nuzzle", 1.3); Heart(); break;
            case "squish": Play("squish", .8); break;
            case "giggle": Play("giggle", 1.2); Emote("note"); break;
            case "flop": Play("flop", 1.8); Heart(); break;
            case "hop": Play("hop", 1); break;
            case "love": Play("love", 1.4); Heart(); Heart(); break;
            case "wink": Play("wink", 1); break;
            case "spin":
                Play("walk", 1.2);
                tailSpin = true;
                flipAt = Now + .15;
                Emote("note");
                break;
            case "pout": Play("pout", 1.4); Emote("vein"); break;
            case "nip": Play("chomp", .8); Emote("bang"); break;
            case "nope": // 제자리에서 도리도리. 도망가면 뒤 창을 누르게 된다
                Play("sad", 1.1);
                ShakeUntil = Now + .7;
                Emote("no");
                break;
            default: Play("happy", 1.1); Heart(); break;
        }
    }

    public void Lift()
    {
        Mode = Mode.Held;
        Pose = "held";
        trail.Clear();
        shakeTrail.Clear();
        shakenFor = 0;
        spinning = false;
        Dbg.Log("lift");
    }

    // 마우스 방향이 몇 번 뒤집혔는지. 한쪽으로 쭉 끄는 건 흔드는 게 아니다
    public static int Reversals(IReadOnlyList<(double T, Point P)> samples, int minStep)
    {
        if (samples.Count == 0) return 0;
        int count = 0, dirX = 0, dirY = 0;
        var (ax, ay) = (samples[0].P.X, samples[0].P.Y);
        foreach (var (_, p) in samples)
        {
            if (Math.Abs(p.X - ax) >= minStep)
            {
                int d = Math.Sign(p.X - ax);
                if (dirX != 0 && d != dirX) count++;
                dirX = d;
                ax = p.X;
            }
            if (Math.Abs(p.Y - ay) >= minStep)
            {
                int d = Math.Sign(p.Y - ay);
                if (dirY != 0 && d != dirY) count++;
                dirY = d;
                ay = p.Y;
            }
        }
        return count;
    }

    // 손을 놓는 순간의 마우스 속도. 마지막 0.12초만 본다 — 멈췄다 놓으면 0 이다
    public static (double X, double Y) Fling(IReadOnlyList<(double T, Point P)> samples)
    {
        if (samples.Count < 2) return (0, 0);
        var (a, b) = (samples[0], samples[^1]);
        double dt = b.T - a.T;
        return dt < .02 ? (0, 0) : ((b.P.X - a.P.X) / dt, (b.P.Y - a.P.Y) / dt);
    }

    public void Drop()
    {
        var (fx, fy) = Fling(trail);
        trail.Clear();
        dizzyNext = shakenFor >= K.ShakeDizzySec;
        Dbg.Log("drop", ("x", (int)X), ("y", (int)Y), ("vx", (int)fx), ("vy", (int)fy), ("shaken", Math.Round(shakenFor, 1)));
        if (Math.Sqrt(fx * fx + fy * fy) >= K.ThrowMinSpeed * S) { Throw(fx, fy); return; }
        if (dizzyNext && P.Stage != "egg") { dizzyNext = false; Feel(true); Play("dizzy", 2.4); return; }
        dizzyNext = false;
        Mode = Mode.Wander;
        next = Now + 1.5;
    }

    // hard: 속도와 상관없이 세게 던진 것처럼 (내려앉으면 어지러워한다). ICBM 폭발에 날아갈 때
    public void Throw(double fx, double fy, bool hard = false)
    {
        if (hard) dizzyNext = true;
        double speed = Math.Sqrt(fx * fx + fy * fy), max = K.ThrowMaxSpeed * S;
        if (speed > max) { fx *= max / speed; fy *= max / speed; speed = max; }
        if (P.Asleep) P.Act(K, "wake");
        vx = fx; vy = fy; throwSpeed = speed;
        if (Math.Abs(fx) > 1) Dir = fx > 0 ? 1 : -1;
        Mode = Mode.Thrown;
        Pose = "fly";
        Emote("bang");
        Dbg.Log("throw", ("speed", (int)speed), ("vx", (int)fx), ("vy", (int)fy));
    }

    void Feel(bool hard)
    {
        if (Now < nextToss) return;
        P.Act(K, hard ? "crash" : "toss");
        nextToss = Now + K.TossCooldownSec;
    }

    void Land()
    {
        bool hard = throwSpeed >= K.DizzySpeed * S || dizzyNext;
        bool hit = knocked;
        dizzyNext = false;
        knocked = false;
        Dbg.Log("land", ("hard", hard), ("knocked", hit), ("x", (int)X));
        if (P.Stage == "egg") { Mode = Mode.Wander; next = Now + 1; return; }
        Feel(hard || hit);
        if (hit) { Play(hard ? "dizzy" : "angry", 2); return; }
        if (hard) { Play("dizzy", 2.4); }
        else { Play("happy", 1.2); Emote("note"); }
    }

    double Shown => P.Stage == "baby" ? .72 : .64; // 창 위로 내민 몸의 비율. 작을수록 눈이 아래쪽에 있다

    PeekPos HidePos() => HideSpots.At(hideRect, peek, CellPx, Shown);

    public bool TryHide()
    {
        if (P.Stage == "egg" || P.Asleep) return false;
        var wins = Windows();
        var spots = HideSpots.Find(wins, World.Areas, CellPx, Shown, rng);
        spots.RemoveAll(p => p.H == Active?.H); // 일하는 창 뒤는 금방 들킨다
        Dbg.Log("hide-try", ("windows", wins.Count), ("spots", spots.Count),
            ("rects", string.Join(" ", wins.Select(w => $"{w.R.X},{w.R.Y},{w.R.Width}x{w.R.Height}{(w.Topmost ? "T" : "")}"))));
        if (spots.Count == 0) return false;
        Aim(spots[rng.Next(spots.Count)], wins);
        nextHide = Now + Rand(K.HideMinSec, K.HideMaxSec);
        return true;
    }

    void Aim(Peek p, List<Win> wins)
    {
        peek = p;
        HideWnd = p.H;
        hideRect = wins.First(w => w.H == p.H).R;
        Mode = Mode.GoHide;
        Dbg.Log("hide-go", ("edge", p.E.ToString()), ("rect", hideRect.ToString()));
    }

    // 창을 치웠는지 본다. 가장자리가 그대로면 크기를 바꿔도 안 들킨 것이다
    bool TargetChanged()
    {
        if (Now < watchAt) return false;
        watchAt = Now + .15;
        var s = Probe(HideWnd);
        string? why = !s.Alive ? "closed" : s.Minimized ? "minimized" : EdgeMoved(s.R) ? "moved" : null;
        if (why == null) return false;
        React(why);
        return true;
    }

    bool EdgeMoved(Rectangle r)
    {
        const int tolerance = 24;
        var (a, b) = (r, hideRect);
        return peek.E switch
        {
            Edge.Left => Math.Abs(a.Left - b.Left) > tolerance || Y - CellPx < a.Top - CellPx || Y > a.Bottom + tolerance,
            Edge.Right => Math.Abs(a.Right - b.Right) > tolerance || Y - CellPx < a.Top - CellPx || Y > a.Bottom + tolerance,
            _ => Math.Abs(a.Top - b.Top) > tolerance || X < a.Left || X > a.Right,
        };
    }

    void Flee()
    {
        PickRoam();
        next = -1;
        runUntil = Now + 2.5;
    }

    void React(string why)
    {
        string kind = HideSpots.Reaction(why, P.Stage, P.Mood, rng.NextDouble());
        Dbg.Log("found", ("why", why), ("kind", kind));
        Behind = false;
        if (P.Asleep) P.Act(K, "wake");
        bool activated = why == "activated", poked = why == "poked"; // 눌렀을 때는 달아나지 않는다
        if (!activated) P.Act(K, "found");
        switch (kind)
        {
            case "shy":
                Play("shy", 2.4, activated ? Leave : poked ? null : () => { if (!TryHide()) Flee(); });
                break;
            case "angry":
                Play("angry", 2.2, activated ? Leave : null);
                break;
            case "freeze":
                Play("freeze", 2.6, () => { Play("look", 1.6, activated ? Leave : null); });
                break;
            case "chase":
                var wins = Windows();
                var spots = HideSpots.Find(wins, World.Areas, CellPx, Shown, rng, HideWnd);
                if (spots.Count > 0) { Emote("bang"); Aim(spots[rng.Next(spots.Count)], wins); }
                else { Play("look", 2.6); }
                break;
            case "look":
                Play("look", 2.6);
                break;
            default:
                Play("shock", 1.1, activated ? Leave : poked ? null : Flee);
                break;
        }
    }

    // 활성 창 밖 가장 가까운 곳으로 재빨리 빠져나간다. 나갈 곳이 없으면 false — 모니터 하나를 창이 꽉 채운 경우다
    bool Escape()
    {
        if (Active is not { } w || World.NearestFree(X, Y, w.R, CellPx, K.FloorGap) is not { } p) return false;
        tx = p.X; ty = p.Y;
        next = -1;
        fleeing = true;
        Mode = Mode.Wander;
        return true;
    }

    void Leave()
    {
        if (!Escape()) Flee();
    }

    Rectangle Body(double x, double y) =>
        new((int)(x - CellPx * .35), (int)(y - CellPx * .8), (int)(CellPx * .7), (int)(CellPx * .8));

    void OnActivated(Win w)
    {
        if (P.Stage == "egg" || Mode is Mode.Held or Mode.Thrown or Mode.Chase or Mode.GoBite or Mode.Prey or Mode.Run or Mode.Paddle or Mode.Puppet) return;
        if (Mode == Mode.Anim && !solo) return;
        if (Mode == Mode.Perch && w.H == perch.H) return; // 누워 있는 창을 누른 것뿐이다
        bool hiding = Mode is Mode.GoHide or Mode.SlipIn or Mode.Hiding or Mode.Emerge;
        if (!(hiding && HideWnd == w.H) && !w.R.IntersectsWith(Body(X, Y))) return;
        Dbg.Log("startle", ("hiding", hiding));
        if (Now < startleOk) { Behind = false; Leave(); return; }
        startleOk = Now + K.StartleCooldownSec;
        React("activated");
    }

    // 활성 창이 볼록이를 치면 창이 온 쪽의 반대로 날아간다. 세게 쳐도 KnockMax 까지만
    void Knock(double dx, double dy, double windowSpeed)
    {
        double v = Math.Clamp(K.KnockMin + windowSpeed * K.KnockGain, K.KnockMin, K.KnockMax) * S;
        Dbg.Log("knock", ("dx", dx), ("dy", dy), ("window", (int)windowSpeed), ("speed", (int)v));
        knocked = true;
        Throw(dx * v, dy * v - v * .45);
    }

    void WatchActive()
    {
        if (Now < activeAt) return;
        activeAt = Now + .08;
        var was = Active;
        double gap = Now - activeSeen;
        activeSeen = Now;
        Active = Foreground();
        if (Active is not { } w) return;
        if (was is not { } old || old.H != w.H) { OnActivated(w); return; }
        if (old.R == w.R || Mode is Mode.Held or Mode.Thrown or Mode.Prey or Mode.Run or Mode.Paddle or Mode.Puppet) return;
        if (HideSpots.HitBy(old.R, w.R, Body(X, Y)) is { } hit) Knock(hit.Dx, hit.Dy, hit.Moved / Math.Max(gap, .01));
    }

    // 총쏘기 게임: 시작하면 그 모니터 안에서 커서를 피해 도망 다닌다
    public void StartGame(Rectangle arena, IEnumerable<Cover>? windows = null)
    {
        Arena = arena;
        covers = (windows ?? Enumerable.Empty<Cover>()).ToList();
        (cover, ducking, CoverProp, nextCover) = (null, false, null, Now + 1);
        (dashing, phaseUntil, freezeUntil) = (false, Now + .3, 0);
        Behind = false;
        fleeing = false;
        Foods.Clear();
        P.Asleep = false;
        (preyVx, preyVy, jukeAt) = (0, 0, 0);
        X = Math.Clamp(X, arena.Left + CellPx / 2.0, arena.Right - CellPx / 2.0);
        Y = Math.Clamp(Y, arena.Top + CellPx, arena.Bottom - K.FloorGap);
        Mode = Mode.Prey;
        Pose = "walk";
        Dbg.Log("game-start", ("arena", arena.ToString()));
    }

    public double FlashUntil;           // 맞으면 하얗게 번쩍
    double freezeUntil, phaseUntil, dashX, dashY;
    bool dashing;

    // 맞음: 0.06초 멈칫(손맛), 하얗게 번쩍, 총알 방향으로 밀려나고, 곧바로 달아난다
    public void GameHit(double x, double y, double fromX, double fromY)
    {
        hurtUntil = Now + .35;
        freezeUntil = Now + .06;
        FlashUntil = Now + .08;
        double dx = X - fromX, dy = Y - CellPx * .45 - fromY, d = Math.Sqrt(dx * dx + dy * dy) + 1;
        X = Math.Clamp(X + dx / d * 26 * S, Arena.Left + CellPx / 2.0, Arena.Right - CellPx / 2.0);
        Y = Math.Clamp(Y + dy / d * 26 * S, Arena.Top + CellPx, Arena.Bottom - K.FloorGap);
        (cover, ducking, CoverProp) = (null, false, null);
        phaseUntil = Now + .06; // 멈칫이 끝나면 바로 다음 달리기
        dashing = false;
        for (int i = 0; i < 10; i++)
            Bits.Add(new Bit { X = x, Y = y, Vx = Rand(-220, 220) * S, Vy = Rand(-280, -40) * S, G = 700 * S, Life = Rand(.25, .5), C = Color.FromArgb(250, 185, 40) });
    }

    // 아깝게 빗나가면 가끔 놀린다
    public void NearMiss(double x, double y)
    {
        double dx = X - x, dy = Y - CellPx * .45 - y;
        if (dx * dx + dy * dy < Math.Pow(150 * S, 2) && !dashing && rng.NextDouble() < .35) { Emote("note", .6); Pose = "giggle"; phaseUntil = Math.Max(phaseUntil, Now + .35); }
    }

    public bool Frozen => Now < freezeUntil;

    // 게임 한 판은 놀아주기 한 번이다. 맞은 만큼 어지러워한다
    public bool Shielded(int x, int y) => CoverProp?.Contains(x, y) == true;

    // 커서와 반대쪽 창 가장자리. 창이 커서와 볼록이 사이에 오게 한다
    (double X, double Y, Cover C)? PickCover()
    {
        (double X, double Y, Cover C)? best = null;
        double bestScore = double.MinValue, half = CellPx / 2.0;
        foreach (var c in covers)
        {
            var r = c.R;
            if (r.Contains(Mouse)) continue;
            double y = Math.Clamp(Y, r.Top + CellPx * .9, Math.Min(r.Bottom - 4, Arena.Bottom - K.FloorGap));
            double x = Math.Clamp(X, r.Left + half, r.Right - half);
            foreach (var (sx, sy) in new[] { (r.Left + CellPx * .12, y), (r.Right - CellPx * .12, y), (x, r.Top + CellPx * .55) })
            {
                if (sx - half < Arena.Left || sx + half > Arena.Right || sy - CellPx < Arena.Top) continue;
                double away = Math.Sqrt(Math.Pow(sx - Mouse.X, 2) + Math.Pow(sy - Mouse.Y, 2)), trip = Math.Sqrt(Math.Pow(sx - X, 2) + Math.Pow(sy - Y, 2));
                double score = away - trip * .6;
                if (score > bestScore) { bestScore = score; best = (sx, sy, c); }
            }
        }
        return best;
    }

    public void EndGame(int hits)
    {
        (cover, ducking, CoverProp, covers) = (null, false, null, new());
        Mode = Mode.Wander;
        next = Now + 2;
        P.Act(K, "play");
        if (hits == 0) { Play("happy", 1.4); Emote("note"); }
        else if (hits >= 6) Play("dizzy", 2);
        else Play("giggle", 1.4);
        Dbg.Log("game-end", ("hits", hits));
    }

    static double Repel(double dist, double margin) => dist < margin ? (1 - Math.Max(0, dist) / margin) * 2.2 : 0;

    // Doom 의 몬스터처럼 후다닥 달리고 멈추기를 되풀이한다. 멈춘 틈이 쏠 기회다. 달리기 직전엔 움츠려 예고한다
    void Prey(double dt)
    {
        P.Asleep = false;
        if (Now < freezeUntil) return; // 맞은 순간 멈칫
        double cy = Y - CellPx * .45, ax = X - Mouse.X, ay = cy - Mouse.Y, d = Math.Sqrt(ax * ax + ay * ay) + 1;
        double minX = Arena.Left + CellPx / 2.0, maxX = Arena.Right - CellPx / 2.0, minY = Arena.Top + CellPx, maxY = Arena.Bottom - K.FloorGap;

        // 엄폐: 창 가장자리 뒤에서 빼꼼
        if (cover is { } cv)
        {
            if (!ducking)
            {
                Pose = Now < hurtUntil ? "hurt" : "walk";
                if (Walk(cv.X, cv.Y, K.PreySpeed * 1.8, dt))
                {
                    (ducking, CoverProp, coverUntil) = (true, cv.C, Now + Rand(.6, 1.4));
                    Dir = cv.X <= cv.C.R.Left + CellPx ? -1 : cv.X >= cv.C.R.Right - CellPx ? 1 : Dir; // 창 바깥을 내다본다
                }
                return;
            }
            Pose = Now < hurtUntil ? "hurt" : cv.Y < cv.C.R.Top + CellPx ? "idle" : "peek";
            if (Now < coverUntil) return;
            (cover, ducking, CoverProp) = (null, false, null);
            (nextCover, phaseUntil) = (Now + Rand(1.5, 3), Now);
        }

        if (dashing)
        {
            Pose = Now < hurtUntil ? "hurt" : "walk";
            if (Walk(dashX, dashY, K.PreySpeed * (Now < hurtUntil ? 2.1 : 1.7), dt) || Now > phaseUntil)
            {
                dashing = false;
                phaseUntil = Now + Rand(.4, .9) * (Now < hurtUntil ? .4 : 1); // 멈춤 — 쏠 틈
            }
            return;
        }

        if (Pose != "giggle" || Now > phaseUntil) Pose = Now < hurtUntil ? "hurt" : "idle";
        bool close = d < 170 * S;
        if (Now < phaseUntil - .08 && !close) return; // 쉬는 중
        if (Now < phaseUntil) { Pose = "land"; return; } // 0.08초 움츠림: 곧 달린다는 예고
        if (covers.Count > 0 && Now > nextCover && d < 320 * S && PickCover() is { } spot) { cover = spot; nextCover = Now + 1; return; }

        // 커서에서 먼 곳 몇 군데 중 하나로 달린다
        (double X, double Y) best = (X, Y);
        double bestScore = double.MinValue;
        for (int i = 0; i < 8; i++)
        {
            double tx = Rand(minX, maxX), ty = Rand(minY, maxY), trip = Math.Sqrt((tx - X) * (tx - X) + (ty - Y) * (ty - Y));
            double away = Math.Sqrt((tx - Mouse.X) * (tx - Mouse.X) + (ty - CellPx * .45 - Mouse.Y) * (ty - CellPx * .45 - Mouse.Y));
            double score = away - Math.Abs(trip - 280 * S) * .5; // 너무 멀지도 가깝지도 않게
            if (score > bestScore) (bestScore, best) = (score, (tx, ty));
        }
        (dashX, dashY, dashing) = (best.X, best.Y, true);
        phaseUntil = Now + 1.2;
        Dir = dashX > X ? 1 : -1;
    }

    // ── 쿠키런 모드 ──
    public Runner? Run;
    public bool Blink => Run is { } r && r.Clock < r.InvUntil && (int)(r.Clock * 12) % 2 == 0; // 다친 뒤 1초는 깜빡

    public void StartRun(Runner r)
    {
        Run = r;
        Behind = false;
        fleeing = false;
        Foods.Clear();
        P.Asleep = false;
        Mode = Mode.Run;
        Dir = 1;
        Dbg.Log("run-start", ("track", r.Track.ToString()));
    }

    public void EndRun()
    {
        if (Run is not { } r) return;
        Run = null;
        Mode = Mode.Wander;
        next = Now + 2;
        P.Act(K, "play"); // 한 판은 놀아주기 한 번
        Play(r.Over ? "dizzy" : "happy", 1.6);
        Emote("star", 1.6);
        Dbg.Log("run-end", ("score", r.Score), ("seeds", r.Seeds), ("over", r.Over));
    }

    void Running()
    {
        var r = Run!;
        P.Asleep = false;
        X = r.PetX;
        Y = r.Ground - r.Lift;
        Dir = 1;
        Pose = r.Clock < r.HurtUntil ? "hurt" : r.Lift > 0 ? (r.Jumps >= 2 ? "roll" : "happy") : r.Sliding ? "land" : "walk";
    }

    // ── 포트리스: 게임이 볼록이 자리와 자세를 정한다 ──
    public void StartPuppet()
    {
        Behind = false;
        fleeing = false;
        Foods.Clear();
        P.Asleep = false;
        Mode = Mode.Puppet;
    }

    // 표정·수치 없이 그냥 돌려놓는다 (디버그 도구: 캐릭터 확인)
    public void LeavePuppet()
    {
        Mode = Mode.Wander;
        next = Now + 1;
    }

    public void EndPuppet(bool won, string what, params (string, object?)[] log)
    {
        Mode = Mode.Wander;
        next = Now + 2;
        P.Act(K, "play"); // 한 판은 놀아주기 한 번
        Play(won ? "happy" : "sad", 1.6);
        Emote(won ? "star" : "tear", 1.6);
        Dbg.Log(what + "-end", log.Append(("won", won)).ToArray());
    }

    // ── 벽돌깨기: 모니터 맨 아래에서 쟁반을 머리 위로 받치고 마우스를 따라 좌우로 ──
    public Breakout? Break;

    public void StartBreak(Breakout bo)
    {
        Break = bo;
        Behind = false;
        fleeing = false;
        Foods.Clear();
        P.Asleep = false;
        Mode = Mode.Paddle;
        (X, Y) = (bo.Field.Left + bo.Field.Width / 2.0, bo.Field.Bottom - K.FloorGap);
        Pose = "carry";
    }

    public void EndBreak()
    {
        if (Break is not { } bo) return;
        Break = null;
        Mode = Mode.Wander;
        next = Now + 2;
        bool won = bo.Stage == Breakout.Phase.Won;
        P.Act(K, "play"); // 한 판은 놀아주기 한 번
        Play(won ? "happy" : "sad", 1.6);
        Emote(won ? "star" : "tear", 1.6);
        Dbg.Log("break-end", ("broken", bo.Broken), ("tiles", bo.Tiles.Count), ("won", won), ("sec", Math.Round(bo.Clock)));
    }

    void Paddling(double dt)
    {
        var bo = Break!;
        double half = bo.PlateW / 2.0;
        double to = Math.Clamp(Mouse.X, bo.Field.Left + half, bo.Field.Right - half);
        X += (to - X) * Math.Min(1, dt * 30); // 거의 바로 따라가되 살짝 미끄러진다
        Y = bo.Field.Bottom - K.FloorGap;
        Dir = 1;
        P.Asleep = false;
        Pose = "carry";
    }

    // 창 윗변에 올라가 누워 잔다
    public bool TryNap()
    {
        if (P.Stage == "egg" || P.Asleep) return false;
        var wins = Windows();
        var spots = HideSpots.FindPerch(wins, World.Areas, CellPx, rng);
        Dbg.Log("nap-try", ("spots", spots.Count));
        if (spots.Count == 0) return false;
        perch = spots[rng.Next(spots.Count)];
        hideRect = wins.First(w => w.H == perch.H).R;
        Mode = Mode.GoNap;
        return true;
    }

    double PerchY(Rectangle r) => r.Top + CellPx * .075; // 스프라이트 아랫단 여백만큼 내려야 윗변에 딱 붙는다

    bool Perchable(Rectangle r) => r.Width >= CellPx * 1.6 && r.Top - CellPx * .5 >= World.At(r.Left + r.Width / 2.0, r.Top + 1).Top;

    // 깨면 폴짝 뛰어내린다
    void JumpDown()
    {
        vx = (rng.Next(2) == 0 ? -1 : 1) * Rand(150, 260) * S;
        vy = -Rand(300, 420) * S;
        throwSpeed = 0;
        Dir = vx > 0 ? 1 : -1;
        Mode = Mode.Thrown;
        Pose = "fly";
        Dbg.Log("jump-down");
    }

    // 자던 창이 사라지면 떨어진다
    void Fall()
    {
        Dbg.Log("fall");
        knocked = true;
        Throw(Rand(-80, 80) * S, 100 * S);
    }

    // 혼자 놀기: 주인이 안 봐도 스스로 논다. 수치는 바뀌지 않는다
    void Solo()
    {
        if (P.Energy <= 80 && rng.NextDouble() < K.NapChance && TryNap()) return;
        string kind = new[] { "groom", "stretch", "hop", "roll", "dig", "think", "eat", "spin" }[rng.Next(8)];
        Dbg.Log("solo", ("kind", kind));
        switch (kind)
        {
            case "groom": Play("groom", Rand(2.5, 4.5)); break;
            case "stretch": Play("stretch", 1.6); break;
            case "hop": Play("hop", Rand(1.5, 3)); break;
            case "roll":
                rollV = (rng.Next(2) == 0 ? -1 : 1) * Rand(60, 130) * S;
                Play("roll", Rand(1.5, 3));
                break;
            case "dig": Play("dig", Rand(2.5, 4)); break;
            case "think": Play("think", Rand(4, 9)); break;
            case "eat": Play("eat", Rand(2, 3.5)); break;
            default:
                Play("walk", Rand(1.5, 2.5));
                tailSpin = true;
                flipAt = Now + .2;
                break;
        }
        solo = true;
    }

    void Dust()
    {
        for (int i = 0; i < 7; i++)
            Bits.Add(new Bit { X = X + Rand(-40, 40) * S, Y = Y - Rand(0, 12) * S, Vx = Rand(-120, 120) * S, Vy = Rand(-170, -30) * S, G = 500 * S,
                Life = Rand(.2, .45), C = Color.FromArgb(236, 222, 196) });
    }

    public void Reset()
    {
        P = new Pet();
        Holes.Clear(); Bits.Clear(); Hearts.Clear();
        Foods.Clear(); Mode = Mode.Wander; Pose = "idle"; pending = spot = null; Behind = false; fleeing = false;
        HolesVersion++;
    }

    public void GiveBack() // 트레이의 비상구: 발표 직전처럼 당장 화면이 필요할 때
    {
        P.Bites = 0;
        P.Mood = Math.Max(P.Mood, K.SpitMood);
    }

    void OnEvents(List<string> list)
    {
        foreach (var e in list)
        {
            Dbg.Log(e, ("stage", P.Stage));
            switch (e)
            {
                case "hatch": Emote("star"); Play("happy", 1.6); break;
                case "grow": Emote("star"); Play("happy", 1.6); break;
                case "miss": Emote("tear"); break;
                case "sleep": Emote("zzz"); break;
                case "wake": Emote("spark"); break;
            }
        }
    }

    public void Advance(double minutes, bool afk)
    {
        while (minutes > 0)
        {
            double m = Math.Min(minutes, .5);
            minutes -= m;
            OnEvents(P.Tick(K, m, afk));
        }
    }

    // 있는 모니터를 다시 잡는다. 모니터 사이 빈 공간에 있으면 그대로 둔다
    void TrackArea()
    {
        if (World.Inside(X, Y)) Area = World.At(X, Y);
    }

    // 저장된 자리나 모니터 구성이 바뀌어 화면 밖이 되면 가장 가까운 모니터 안으로
    public void Settle()
    {
        Area = World.At(X, Y);
        X = Math.Clamp(X, MinX, MaxX);
        Y = Math.Clamp(Y, MinY, MaxY);
    }

    // 볼록이 앞에서 씨앗을 톡 던져 준다. 입에 들어가면 사라져서 화면에 남지 않는다. 앞이 화면 끝이면 뒤돌아 받는다
    void TossSeed(bool reject)
    {
        double reach = Rand(150, 220) * S, sx = X + Dir * reach;
        if (sx < Area.Left + 20 || sx > Area.Right - 20) { Dir = -Dir; sx = X + Dir * reach; }
        double sy = Y - CellPx * Rand(.7, 1.1);
        Foods.Add(new Seed { Sx = sx, Sy = sy, X = sx, Y = sy, Reject = reject });
    }

    // 다른 모니터로 가야 하면 맞닿은 곳을 거쳐 간다. 빈 공간으로 들어가지 않는다
    public bool Go(double gx, double gy, double v, double dt)
    {
        TrackArea();
        var to = World.At(gx, gy);
        if (to == Area) return Walk(gx, gy, v, dt);
        if (World.NextArea(Area, to, CellPx) is not { } hop) return true; // 이어지지 않은 모니터: 포기

        var d = World.Door(Area, hop, X, Y, CellPx, K.FloorGap);
        bool onLine = Math.Abs(d.AY - d.BY) < 1
            ? Math.Abs(Y - d.AY) < 4 && X >= Math.Min(d.AX, d.BX) - 4 && X <= Math.Max(d.AX, d.BX) + 4
            : Math.Abs(X - d.AX) < 4 && Y >= Math.Min(d.AY, d.BY) - 4 && Y <= Math.Max(d.AY, d.BY) + 4;
        if (onLine) Walk(d.BX, d.BY, v, dt);
        else Walk(d.AX, d.AY, v, dt);
        return false;
    }

    public void GoTo(double x, double y) { tx = x; ty = y; next = -1; Mode = Mode.Wander; }

    // 아침·밤 인사처럼 잠깐 한 동작 (돌아다니는 중일 때만)
    public void Greet(string pose, double sec) { if (Mode == Mode.Wander && !P.Asleep) Play(pose, sec); }

    bool Walk(double gx, double gy, double v, double dt)
    {
        double dx = gx - X, dy = gy - Y, d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 3) return true;
        double s = Math.Min(v * S * dt, d);
        X += dx / d * s;
        Y += dy / d * s;
        if (Math.Abs(dx) > 2) Dir = dx > 0 ? 1 : -1;
        return false;
    }

    bool OnActive(double x, double y) => Active is { } w && w.R.IntersectsWith(Body(x, y));

    (double, double) PickSpot()
    {
        for (int i = 0; ; i++)
        {
            var a = i < 12 ? Area : World.Areas[rng.Next(World.Areas.Count)]; // 있는 모니터가 활성 창으로 꽉 차면 다른 모니터로
            double x = Rand(a.Left + 100 * S, a.Right - 100 * S), y = Rand(a.Top + 100 * S, a.Bottom - 100 * S);
            double dx = x - Mouse.X, dy = y - Mouse.Y;
            if (i >= 40 || (dx * dx + dy * dy > 240 * S * 240 * S && !OnActive(x, y))) return (x, y); // 커서 밑과 일하는 창은 피해 준다
        }
    }

    // 돌아다닐 곳: 비활성 창 쪽을 고르고, 활성 창 위는 고르지 않는다
    public (double X, double Y) NextRoam()
    {
        var wins = Windows().Where(w => w.H != Active?.H && w.R.Width > CellPx && w.R.Height > CellPx).ToList();
        for (int i = 0; i < 16; i++)
        {
            var a = i >= 8 || (World.Areas.Count > 1 && rng.NextDouble() < K.RoamOtherScreen) ? World.Areas[rng.Next(World.Areas.Count)] : Area;
            double minX = a.Left + CellPx / 2.0, maxX = a.Right - CellPx / 2.0, minY = a.Top + CellPx, maxY = a.Bottom - K.FloorGap;
            double low = a.Bottom - a.Height * .24, x, y;
            var on = wins.Where(w => w.R.IntersectsWith(a)).ToList();
            if (on.Count > 0 && rng.NextDouble() < K.RoamOnWindow)
            {
                var r = Rectangle.Intersect(on[rng.Next(on.Count)].R, a);
                x = Math.Clamp(Rand(r.Left, r.Right), minX, maxX);
                y = Math.Clamp(Rand(r.Top + CellPx, r.Bottom), minY, maxY);
            }
            else
            {
                x = Rand(minX, maxX);
                y = rng.NextDouble() < K.RoamLow ? Rand(Math.Max(low, minY), maxY) : Rand(minY, maxY);
            }
            if (!OnActive(x, y)) return (x, y);
        }
        return Active is { } w2 && World.NearestFree(X, Y, w2.R, CellPx, K.FloorGap) is { } p ? p : (X, Y);
    }

    void PickRoam() => (tx, ty) = NextRoam();

    public void AddHole(double cx, double cy, bool crumbs)
    {
        int cell = Math.Max(4, (int)(8 * S));
        var h = new Hole { Cx = (int)(cx / cell) * cell, Cy = (int)(cy / cell) * cell }; // 칸을 격자에 맞춰야 칸 사이가 안 비친다
        double turn = Rand(0, Math.PI);
        var blobs = new List<(double X, double Y, double R)> { (0, 0, 40 * S) };
        for (int i = 0; i < 6; i++) // 둘레를 이빨 자국처럼 오톨도톨하게
        {
            double a = turn + i * Math.PI / 3;
            blobs.Add((Math.Cos(a) * 36 * S, Math.Sin(a) * 36 * S, Rand(20, 26) * S));
        }
        int span = (int)(72 * S) / cell;
        for (int y = -span; y <= span; y++)
            for (int x = -span; x <= span; x++)
                if (blobs.Any(b => Math.Pow(x * cell - b.X, 2) + Math.Pow(y * cell - b.Y, 2) < b.R * b.R))
                    h.Cells.Add(new Point(h.Cx + x * cell, h.Cy + y * cell));
        Holes.Add(h);
        HolesVersion++;
        if (crumbs) Spray(h.Cx, h.Cy, Sample(h.Cx, h.Cy, (int)(60 * S)));
        Dbg.Log("hole", ("x", h.Cx), ("y", h.Cy), ("count", Holes.Count));
    }

    void Spray(double x, double y, Color[] colors) // 뜯긴 화면 조각이 흩어진다 — 색은 실제 그 자리의 화면색
    {
        foreach (var c in colors)
        {
            double life = Rand(.45, .9);
            Bits.Add(new Bit { X = x + Rand(-44, 44) * S, Y = y + Rand(-44, 44) * S, Vx = Rand(-110, 110) * S, Vy = Rand(-150, 0) * S, G = 520 * S, Life = life, C = c });
        }
    }

    void SpitBack(Hole h)
    {
        double mx = X + 30 * S * Dir, my = Y - 70 * S, life = .5;
        for (int i = 0; i < 12; i++)
            Bits.Add(new Bit { X = mx, Y = my, Vx = (h.Cx - mx) / life + Rand(-50, 50) * S, Vy = (h.Cy - my) / life + Rand(-50, 50) * S, Life = life,
                C = Color.FromArgb(rng.Next(120, 255), rng.Next(120, 255), rng.Next(120, 255)) });
    }

    public void Update(double dt, bool afk)
    {
        Now += dt;
        Advance(dt * Speed / 60, afk);
        TrackArea();
        WatchActive();
        if (Behind && Mode is not (Mode.SlipIn or Mode.Hiding or Mode.Emerge)) Behind = false;

        if (Mode == Mode.Anim && Now > until)
        {
            if (pending is { } s)
            {
                pending = null; // 먼저 비운다: 아래에서 실패해도 매 프레임 다시 시도하며 멈추지 않게
                AddHole(s.X, s.Y, true);
            }
            Mode = Mode.Wander;
            next = Math.Max(Now + Rand(.6, 2), lastPoke + K.ClickHoldSec);
            solo = false;
            tailSpin = false;
            var after = then;
            then = null;
            after?.Invoke();
        }
        if (Mode == Mode.Wander && !P.Asleep && P.Stage != "egg")
        {
            if (Holes.Count > P.Bites)
            {
                var h = Holes[^1];
                Holes.RemoveAt(Holes.Count - 1);
                HolesVersion++;
                SpitBack(h);
                Play("spit", .7);
            }
            else if (Holes.Count < P.Bites)
            {
                spot = PickSpot();
                Mode = Mode.GoBite;
            }
        }

        switch (Mode)
        {
            case Mode.Wander:
                if (P.Asleep || P.Stage == "egg") { Pose = P.RestPose(K); break; }
                if (!fleeing && !Holding && OnActive(X, Y)) Escape(); // 어쩌다 활성 창 위에 있으면 곧장 빠져나간다
                if (next >= 0 && Now > next)
                {
                    if (Now > nextHide && P.Need(K) == null)
                    {
                        if (TryHide()) break;
                        nextHide = Now + 30; // 숨을 창이 없으면 조금 있다 다시 본다
                    }
                    if (P.Energy <= K.NapAt && Now > nextNap)
                    {
                        nextNap = Now + 60;
                        if (TryNap()) break;
                    }
                    if (P.Need(K) == null && rng.NextDouble() < K.SoloChance) { Solo(); break; }
                    PickRoam();
                    next = -1;
                }
                if (next < 0)
                {
                    Pose = "walk";
                    double speed = K.WalkSpeed * (Now < runUntil ? 4 : 1);
                    if (fleeing || OnActive(X, Y)) speed = Math.Max(speed, K.EscapeSpeed); // 활성 창 위는 빠르게 지나간다
                    if (Go(tx, ty, speed, dt)) { next = Now + Rand(2, 7); fleeing = false; }
                }
                else Pose = P.RestPose(K);
                break;

            case Mode.Anim:
                if (solo && OnActive(X, Y)) until = Now; // 혼자 놀다 활성 창에 닿으면 그만두고 빠져나간다
                if (Pose == "roll")
                {
                    X = Math.Clamp(X + rollV * dt, MinX, MaxX);
                    Dir = rollV > 0 ? 1 : -1;
                }
                if (tailSpin && Now > flipAt) { Dir = -Dir; flipAt = Now + .2; } // 제 꼬리를 쫓아 뱅글뱅글
                break;

            case Mode.GoBite:
                Pose = "walk";
                var (sx, sy) = spot!.Value;
                if (Walk(Math.Clamp(sx - CellPx * .6, MinX, MaxX), Math.Clamp(sy + CellPx * .5, MinY, MaxY), 170, dt))
                {
                    Dir = 1;
                    pending = spot;
                    Play("chomp", 1.3);
                }
                break;

            case Mode.Chase:
                Pose = "walk";
                var m = World.At(Mouse.X, Mouse.Y);
                Go(Math.Clamp(Mouse.X, m.Left + CellPx / 2.0, m.Right - CellPx / 2.0),
                    Math.Clamp(Mouse.Y + CellPx * .4, m.Top + CellPx, m.Bottom - K.FloorGap), 190, dt);
                if (Now > until) { Play("happy", 1.2); Emote("note"); }
                break;

            case Mode.Held:
                Area = World.At(Mouse.X, Mouse.Y);
                X = Math.Clamp(Mouse.X, MinX, MaxX);
                Y = Math.Clamp(Mouse.Y + CellPx * .55, MinY, MaxY);
                trail.Add((Now, Mouse));
                trail.RemoveAll(t => Now - t.T > .12);
                shakeTrail.Add((Now, Mouse));
                shakeTrail.RemoveAll(t => Now - t.T > .8);
                bool shaking = Reversals(shakeTrail, (int)(14 * S)) >= K.ShakeReversals;
                if (shaking)
                {
                    if (!spinning) { Dbg.Log("shake"); }
                    shakenFor += dt;
                }
                spinning = shaking;
                Pose = shaking ? "fly" : "held";
                break;

            case Mode.GoHide:
            {
                Pose = "walk";
                if (Now >= watchAt)
                {
                    watchAt = Now + .15;
                    var s = Probe(HideWnd);
                    if (!s.Alive || s.Minimized) { Mode = Mode.Wander; next = Now + 1; break; }
                    hideRect = s.R; // 달려가는 동안 창이 움직이면 따라간다
                    if (!hideRect.IntersectsWith(peek.A)) // 다른 모니터로 가 버렸으면 거기서 자리를 다시 찾는다
                    {
                        var wins = Windows();
                        var again = HideSpots.Find(wins, World.Areas, CellPx, Shown, rng, HideWnd);
                        if (again.Count == 0) { Mode = Mode.Wander; next = Now + 1; break; }
                        Aim(again[rng.Next(again.Count)], wins);
                    }
                }
                var at = HidePos();
                if (Go(at.OutX, at.OutY, 190, dt)) { Behind = true; Mode = Mode.SlipIn; }
                break;
            }

            case Mode.SlipIn:
            {
                Pose = "walk";
                if (TargetChanged()) break;
                var at = HidePos();
                if (Walk(at.X, at.Y, 80, dt))
                {
                    Mode = Mode.Hiding;
                    until = Now + Rand(K.HideStayMinSec, K.HideStayMaxSec);
                    bobAt = Now + Rand(1.5, 4);
                    ducked = false;
                    Dbg.Log("hiding", ("edge", peek.E.ToString()));
                }
                break;
            }

            case Mode.Hiding:
            {
                if (TargetChanged()) break;
                if (P.Asleep) { Pose = "sleep"; break; }
                Pose = peek.E == Edge.Top ? "idle" : "peek";
                if (Now > until || P.Need(K) != null || Holes.Count != P.Bites) { Mode = Mode.Emerge; break; }
                if (Now > bobAt) { ducked = !ducked; bobAt = Now + (ducked ? Rand(.6, 1.6) : Rand(2, 7)); }
                var at = HidePos();
                Walk(ducked ? at.DuckX : at.X, ducked ? at.DuckY : at.Y, 70, dt);
                if (peek.E != Edge.Top) Dir = peek.E == Edge.Left ? -1 : 1; // 창 바깥을 내다본다
                break;
            }

            case Mode.Emerge:
            {
                Pose = "walk";
                if (TargetChanged()) break;
                var at = HidePos();
                if (Walk(at.OutX, at.OutY, 110, dt)) { Behind = false; Mode = Mode.Wander; next = Now + Rand(.5, 2); }
                break;
            }

            case Mode.Prey:
                Prey(dt);
                break;

            case Mode.Run:
                Running();
                break;

            case Mode.Paddle:
                Paddling(dt);
                break;

            case Mode.Puppet: // 포트리스: 자리와 자세는 게임이 정한다
                P.Asleep = false;
                break;

            case Mode.GoNap:
            {
                Pose = "walk";
                if (Now >= watchAt)
                {
                    watchAt = Now + .15;
                    var s = Probe(perch.H);
                    if (!s.Alive || s.Minimized || !Perchable(s.R)) { Mode = Mode.Wander; next = Now + 1; break; }
                    hideRect = s.R; // 가는 동안 창이 움직이면 따라간다
                }
                if (Go(HideSpots.PerchX(hideRect, perch.T, CellPx), PerchY(hideRect), 190, dt))
                {
                    P.Asleep = true;
                    Mode = Mode.Perch;
                    Pose = "sleep";
                    Emote("zzz");
                    Dbg.Log("perch", ("rect", hideRect.ToString()));
                }
                break;
            }

            case Mode.Perch:
            {
                Pose = "sleep";
                if (!P.Asleep) { JumpDown(); break; }
                if (Now < watchAt) break;
                watchAt = Now + .05;
                var s = Probe(perch.H);
                if (!s.Alive || s.Minimized || !Perchable(s.R)) { Fall(); break; }
                hideRect = s.R; // 창을 옮기면 누운 채로 실려 간다
                X = HideSpots.PerchX(hideRect, perch.T, CellPx);
                Y = PerchY(hideRect);
                break;
            }

            case Mode.Thrown:
                vy += K.Gravity * S * dt;
                X += vx * dt;
                Y += vy * dt;
                bool hit = false;
                // 옆 모니터와 맞닿은 곳은 벽이 아니다. 그대로 날아서 넘어간다
                if ((X < MinX && !World.Inside(Area.Left - 2, Y)) || (X > MaxX && !World.Inside(Area.Right + 2, Y)))
                {
                    X = Math.Clamp(X, MinX, MaxX);
                    vx = -vx * K.Bounce;
                    Dir = vx > 0 ? 1 : -1;
                    hit = true;
                }
                if (Y < MinY && !World.Inside(X, Area.Top - 2)) { Y = MinY; vy = -vy * K.Bounce; hit = true; }
                if (Y >= MaxY && !World.Inside(X, Area.Bottom + 2))
                {
                    Y = MaxY;
                    if (vy < K.RestSpeed * S) { Land(); break; }
                    vy = -vy * K.Bounce;
                    vx *= .8;
                    hit = true;
                }
                if (hit) { squashUntil = Now + .09; Dust(); }
                Pose = Now < squashUntil ? "land" : "fly";
                break;
        }

        if (Mode == Mode.Wander && !fleeing && Now > nextCall && P.Need(K) is { } need)
        {
            Emote(need switch { "hungry" => "seed", "bored" => "heart", _ => "zzz" }, 2);
            nextCall = Now + K.CallEverySec;
        }

        foreach (var seed in Foods) // 낮은 포물선을 그리며 입으로 들어간다
        {
            bool arriving = seed.T < 1;
            seed.T += dt / K.TossSec;
            double t = Math.Min(1, seed.T), mouthX = X + Dir * CellPx * (seed.Reject ? .5 : .12), mouthY = Y - CellPx * .4;
            seed.X = seed.Sx + (mouthX - seed.Sx) * t;
            seed.Y = seed.Sy + (mouthY - seed.Sy) * t - Math.Sin(t * Math.PI) * 70 * S;
            if (seed.Reject && arriving && seed.T >= 1) { ShakeUntil = Now + .5; Emote("no"); }
            if (seed.T > 1) seed.Y += (seed.T - 1) * 160 * S; // 거절당한 씨앗은 옆으로 떨어진다
        }
        Foods.RemoveAll(f => f.T >= (f.Reject ? 1.8 : 1));

        foreach (var b in Bits) { b.X += b.Vx * dt; b.Y += b.Vy * dt; b.Vy += b.G * dt; b.Life -= dt; }
        Bits.RemoveAll(b => b.Life <= 0);
        foreach (var h in Hearts) { h.T += dt; h.Y -= 50 * S * dt; }
        Hearts.RemoveAll(h => h.T >= 1);
    }
}

public static class Names
{
    public static string Form(string stage) => L.Stage(stage); // 알·아기·어린이·통통이·애교쟁이·심술이 (언어별)

    // 받침이 있으면 b, 없으면 a (를/을, 가/이가)
    public static string Josa(string word, string a, string b) =>
        word.Length > 0 && word[^1] is >= '가' and <= '힣' && (word[^1] - '가') % 28 != 0 ? b : a;
}
