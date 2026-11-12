namespace Bolok;

// 떠난 친구가 놀러 온다 (YELL 2026-11-12): 하루에 한 번, 한 친구가 5분쯤 있다가 돌아간다.
// 가까운 화면 옆에서 걸어 들어와 볼록이와 인사(하트)하고, 볼록이 옆에 붙어 같이 다닌다. 가끔 둘이 같이 깡충·킥킥.
// 볼록이가 자면 옆에 누워 같이 잔다. 볼록이가 게임 중이거나 잡혀 있으면 제자리에서 기다린다. 시간이 되면 손 흔들고 걸어 나간다.
// 화면과 무관한 규칙이라 --selfcheck 로 검사한다
public sealed class Visitor
{
    public enum Phase { Arrive, Stay, Leave, Gone }
    public const double StaySec = 300, WalkSpeed = 85, RunSpeed = 170, PlayEvery = 9, Side = 1.25;
    static readonly string[] Plays = { "hop", "giggle", "love", "nuzzle", "wink", "happy" };

    public readonly string Species, Form;   // Form: 떠날 때 모습 (adult_chubby 등)
    public readonly int K;
    public double X, Y, Clock, EmoteUntil, BabyX = double.NaN; // BabyX: 아기는 엄마 뒤를 부드럽게 따라온다
    public int Dir = 1;
    public string Pose = "walk", Emote = "";
    public Phase Stage = Phase.Arrive;
    public bool Baby; // 우정 하트가 다섯이 되면 아기를 데리고 온다 (그리는 쪽이 뒤에 따라 그린다)
    public Action<string>? OnEvent;
    public bool Gone => Stage == Phase.Gone;

    readonly Random rng;
    readonly double cell, s;
    double side = -1, hold, nextPlay, arrivedAt, leaveTo;
    bool sleeping, moving; // moving: 한 번 걷기 시작하면 자리에 닿을 때까지 걷는다 (걷기·서기를 매 프레임 오가면 파르르 떨린다)

    public Visitor(string species, string form, double x, double y, int k, Random rng)
    {
        (Species, Form, X, Y, K, this.rng) = (species, form, x, y, k, rng);
        s = k / 3.0;
        cell = 40 * k;
    }

    void Say(string emote, double sec = 1.6) => (Emote, EmoteUntil) = (emote, Clock + sec);

    // main: 볼록이 발 위치. free: 볼록이가 돌아다니거나 쉬는 중(게임·잡힘·숨기 아님). asleep: 볼록이가 자는 중
    public void Update(double dt, double mainX, double mainY, bool free, bool asleep, Rectangle area)
    {
        Clock += dt;
        if (Stage == Phase.Gone) return;
        if (Stage == Phase.Stay && Clock - arrivedAt >= StaySec) // 돌아갈 시간: 가까운 화면 밖으로
        {
            Stage = Phase.Leave;
            leaveTo = X < area.Left + area.Width / 2.0 ? area.Left - cell : area.Right + cell;
            (Pose, hold) = ("happy", Clock + 1.4);
            Say("heart");
            sleeping = false;
            OnEvent?.Invoke("bye");
        }

        // 아기: 엄마 뒤 0.7칸 자리로 부드럽게 (엄마가 돌아서도 순간이동하지 않는다)
        double babyTo = X - Dir * cell * .7;
        if (double.IsNaN(BabyX)) BabyX = babyTo;
        BabyX += Math.Clamp(babyTo - BabyX, -RunSpeed * s * dt, RunSpeed * s * dt);

        switch (Stage)
        {
            case Phase.Arrive:
            {
                double tx = mainX + side * Side * cell;
                Y += Math.Clamp(mainY - Y, -400 * s * dt, 400 * s * dt);
                if (Walk(tx, RunSpeed, dt) && free)
                {
                    Stage = Phase.Stay;
                    arrivedAt = Clock;
                    Y = mainY;
                    (Pose, hold, nextPlay) = ("happy", Clock + 1.4, Clock + PlayEvery);
                    Say("heart", 2);
                    OnEvent?.Invoke("hello");
                }
                break;
            }
            case Phase.Stay:
                if (Clock < hold) break;
                if (!free) { Pose = sleeping ? "sleep" : "idle"; break; } // 볼록이가 바쁘면 기다린다
                if (asleep) // 옆에 누워 같이 잔다
                {
                    if (!Walk(mainX + side * cell * .95, WalkSpeed, dt)) break;
                    Y = mainY;
                    Pose = "sleep";
                    if (!sleeping) { sleeping = true; OnEvent?.Invoke("sleep"); }
                    if (Clock > EmoteUntil + 3) Say("zzz", 2);
                    break;
                }
                if (sleeping) { sleeping = false; (Pose, hold) = ("stretch", Clock + 1.2); break; } // 볼록이가 깨면 같이 기지개
                double target = mainX + side * Side * cell;
                Y += Math.Clamp(mainY - Y, -400 * s * dt, 400 * s * dt); // 높이는 늘 부드럽게
                if (!moving && Math.Abs(target - X) > cell * .8) moving = true; // 0.8칸 넘게 벌어지면 따라간다
                if (moving)
                {
                    if (!Walk(target, Math.Abs(target - X) > cell * 4 ? RunSpeed : WalkSpeed, dt)) break;
                    moving = false; // 자리에 닿았다: 이제 선다
                }
                if (Math.Abs(mainX - X) > cell * .3) Dir = mainX > X ? 1 : -1; // 볼록이가 분명히 한쪽에 있을 때만 돌아본다
                if (Clock >= nextPlay) // 가끔 같이 논다
                {
                    nextPlay = Clock + PlayEvery * (.7 + rng.NextDouble() * .6);
                    (Pose, hold) = (Plays[rng.Next(Plays.Length)], Clock + 1.3);
                    if (rng.NextDouble() < .5) Say("heart");
                    if (rng.NextDouble() < .3) side = -side; // 반대쪽으로 자리 바꾸기
                    OnEvent?.Invoke("play");
                    break;
                }
                Pose = "idle";
                break;
            case Phase.Leave:
                if (Clock < hold) break;
                if (Walk(leaveTo, WalkSpeed, dt)) { Stage = Phase.Gone; OnEvent?.Invoke("gone"); }
                break;
        }
    }

    // 디버그: 지금 돌아가게
    public void GoHome() { if (Stage == Phase.Stay) arrivedAt = Clock - StaySec; else if (Stage == Phase.Arrive) { Stage = Phase.Stay; arrivedAt = Clock - StaySec; } }

    // tx 쪽으로 걷는다. 도착하면 true
    bool Walk(double tx, double speed, double dt)
    {
        double step = speed * s * dt;
        if (Math.Abs(tx - X) <= step) { X = tx; return true; }
        Dir = tx > X ? 1 : -1;
        X += Dir * step;
        Pose = "walk";
        return false;
    }

    // 오늘 놀러 올 친구: 떠난 친구 중 가장 오래 안 온 아이 (같은 종은 하나로). 지금 키우는 종과 같으면 뒤로
    public static ReleasedPet? Pick(IReadOnlyList<ReleasedPet> released, IReadOnlyDictionary<string, DateTime> visits, string current, Random rng)
    {
        var last = released.GroupBy(r => r.Species).Select(g => g.Last()).ToList();
        if (last.Count == 0) return null;
        return last.OrderBy(r => r.Species == current ? 1 : 0)
            .ThenBy(r => visits.TryGetValue(r.Species, out var at) ? at : DateTime.MinValue)
            .ThenBy(_ => rng.Next())
            .First();
    }
}
