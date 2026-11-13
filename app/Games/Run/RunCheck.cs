using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace Bolok;

// 달리기 모듈의 자가 검사(--selfcheck), 장면 그려 보기(--mode-scene run[@배율]), 속도 재기(--mode-bench run). 화면은 띄우지 않는다
sealed partial class RunGame
{
    static readonly int[] Scales = { 2, 3, 4, 5, 6 };    // 볼록이 배율: 100% 모니터는 3, 125% 는 4, 200% 는 6. 가장 작게는 2
    static readonly Rectangle TestArea = SceneHost.Area;
    const int TestGround = 1030;

    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    // 그림 데이터(art.json)에 있는 형태 전부. 새 형태가 생기면 달리기 검사도 저절로 그 형태를 본다
    static List<string> Forms()
    {
        using var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("pets/art.json")!;
        using var doc = JsonDocument.Parse(s);
        var root = doc.RootElement;
        bool drop = root.GetProperty("poses").TryGetProperty("land", out var land) && land.GetArrayLength() > 0
            && land[0].TryGetProperty("sq", out var sq) && sq.GetDouble() == RunBody.DuckDrop;
        Expect(drop, $"엎드린 자세(land)는 몸이 {RunBody.DuckDrop}칸px 낮다 — art.json 의 sq 를 바꾸면 RunBody.DuckDrop 도 같이");
        return root.GetProperty("forms").EnumerateObject().Select(p => p.Name).ToList();
    }

    public static string Check()
    {
        var host = new SceneHost();
        var sp = host.Sprites;
        return Rules(sp) + "\n" + Gimmicks(sp) + "\n" + Perf(host) + "\n" + Module(host);
    }

    // ── 규칙 (화면 없이) ──
    static string Rules(Sprites sp)
    {
        played = new();
        var area = TestArea;
        var measure = Measure(sp);
        var forms = Forms();
        Expect(forms.Contains("baby") && forms.Contains("child") && forms.Count >= 5, "그림 데이터에 아기·어린이·어른 형태가 있다");
        var child = BodyOf("child");
        Runner New(int seed = 1, int level = 1, RunBody? body = null, int scale = 3) => new(area, TestGround, 40 * scale, body ?? child, new Random(seed), measure, level);
        Runner Make(RunBody? body = null, int scale = 3, int level = 1) { var r = New(1, level, body, scale); r.Things.Clear(); return r; }
        void Run(Runner r, double sec, Action<Runner, double>? each = null) { for (double t = 0; t < sec; t += 1 / 60.0) { each?.Invoke(r, t); r.Update(1 / 60.0); } }
        // 놓아둔 것이 볼록이를 지나갈 때까지만 (더 달리면 저절로 나온 것들이 끼어든다)
        void Pass(Runner r, Thing last, Action<Runner>? each = null) { for (int i = 0; i < 600 && !last.Gone && last.X + last.W > r.PetBox.Left - 10; i++) { each?.Invoke(r); r.Update(1 / 60.0); } }

        // 가만히 있으면 땅 장애물에 부딪히고, 맞춰 점프하면 넘는다
        var stand = Make();
        stand.Add(ThingKind.Block, "prop/mushroom", 2, stand.PetX + 300, stand.Ground);
        Run(stand, 1.5);
        Expect(stand.Energy < 100 - Runner.HitCostOf(1) + 1, $"가만히 있으면 부딪힌다: 체력 {stand.Energy:F0}");
        var hop = Make();
        var block = hop.Add(ThingKind.Block, "prop/mushroom", 2, hop.PetX + 300, hop.Ground);
        bool jumped = false;
        Run(hop, 1.5, (r, t) => { if (!jumped && block.X - (r.PetX + r.PetBox.Width / 2) < 50) { r.Jump(); jumped = true; } }); // 바로 앞에서 뛴다
        var early = Make();
        var far = early.Add(ThingKind.Block, "prop/mushroom", 2, early.PetX + 300, early.Ground);
        bool jumpedEarly = false;
        Run(early, 1.5, (r, t) => { if (!jumpedEarly && far.X - (r.PetX + r.PetBox.Width / 2) < 120) { r.Jump(); jumpedEarly = true; } });
        Expect(early.Energy > 95, $"조금 일찍 뛰어도 넘는다 (타이밍 여유): 체력 {early.Energy:F0}");
        Expect(jumped && hop.Energy > 95, $"맞춰 점프하면 넘는다: 체력 {hop.Energy:F0}");

        // 공중 장애물은 슬라이드로 밑을 지난다
        var duck = Make();
        duck.Add(ThingKind.Fly, "prop/cloud", 2, duck.PetX + 300, duck.Ground - duck.CloudUp);
        Run(duck, 1.5, (r, t) => r.Slide(true));
        Expect(duck.Energy > 95, $"슬라이드하면 공중 장애물 밑을 지난다: 체력 {duck.Energy:F0}");
        var tall = Make();
        tall.Add(ThingKind.Fly, "prop/cloud", 2, tall.PetX + 300, tall.Ground - tall.CloudUp);
        Run(tall, 1.5);
        Expect(tall.Energy < 85, "서 있으면 공중 장애물에 부딪힌다");

        // 더블 점프는 한 번만, 땅에 닿으면 다시
        var dj = Make();
        dj.Jump(); dj.Update(.05); dj.Jump(); dj.Update(.05); var vy = dj.Vy; dj.Jump();
        Expect(dj.Jumps == 2 && dj.Vy == vy, "공중에선 두 번까지만 뛴다");
        Run(dj, 2);
        Expect(dj.Lift == 0 && dj.Jumps == 0, "땅에 닿으면 다시 뛸 수 있다");

        // 씨앗을 먹으면 점수, 부딪힌 뒤 1초는 안 다친다, 체력이 닳으면 끝
        var eat = Make();
        for (int i = 0; i < 4; i++) eat.Add(ThingKind.Seed, "icon/seed", 1, eat.PetX + 100 + i * 34, eat.Ground - 18);
        Run(eat, 1.5);
        Expect(eat.Seeds == 4 && eat.Score >= 40, $"씨앗을 먹으면 점수: {eat.Seeds}개 {eat.Score}점");
        var safe = Make();
        safe.Add(ThingKind.Block, "prop/mushroom", 2, safe.PetX + 200, safe.Ground);
        safe.Add(ThingKind.Block, "prop/acorn", 2, safe.PetX + 320, safe.Ground);
        Run(safe, 1.5);
        Expect(safe.Energy > 100 - Runner.HitCostOf(1) * 2, "부딪힌 뒤 잠깐은 안 다친다");
        var end = New(3);
        Run(end, 70, (r, t) => { if (r.Things.Any(x => x.Kind == ThingKind.Block && x.X - (r.PetX + 30) is > 0 and < 60) && r.Lift == 0) r.Jump(); r.Slide(r.Things.Any(x => x.Kind == ThingKind.Fly && x.X - r.PetX is > -80 and < 200)); });
        Expect(end.Over && end.Clock < 70, $"체력이 닳으면 끝난다: {end.Clock:F0}초, {end.Score}점");

        // 레벨: 오를수록 빠르고 아프고 목표가 높다. 12레벨부터는 그대로
        var lv = Enumerable.Range(1, 12).ToList();
        Expect(lv.Zip(lv.Skip(1)).All(p => Runner.StartSpeedOf(p.Second) > Runner.StartSpeedOf(p.First) && Runner.MaxSpeedOf(p.Second) > Runner.MaxSpeedOf(p.First)
            && Runner.HitCostOf(p.Second) > Runner.HitCostOf(p.First) && Runner.GoalOf(p.Second) > Runner.GoalOf(p.First)), "레벨이 오를수록 어렵다");
        Expect(Runner.StartSpeedOf(20) == Runner.StartSpeedOf(12) && Runner.HitCostOf(20) == Runner.HitCostOf(12), "12레벨부터는 더 어려워지지 않는다");
        var up = Make();
        string? heard = null;
        up.OnEvent = e => heard ??= e == "level" ? e : null;
        up.Seeds = Runner.GoalOf(1) / 10;
        up.Update(1 / 60.0);
        Expect(up.Level == 2 && heard == "level" && up.LevelAt == up.Clock && up.Speed >= Runner.StartSpeedOf(2) && up.LevelScore == 0, "목표를 넘기면 그 판에서 바로 다음 레벨");
        up.Seeds += (Runner.GoalOf(2) - 20) / 10;
        up.Update(1 / 60.0);
        Expect(up.Level == 2, "다음 레벨 목표는 올라간 뒤 번 점수로 센다");
        Expect(New(1, 5).Speed == Runner.StartSpeedOf(5), "저장된 레벨에서 시작한다");

        // 한 판 해 보기. 길에 저절로 나온 것들이 몸 크기 규칙을 지키는지도 같이 본다
        // ride: 2층을 어떻게 타는지. Seek 찾아 올라간다(잘하는 사람) · Plain 일부러 찾지 않는다 ·
        //   Walk 올라가서 끝까지 걷다 떨어지고, 땅에 내려선 뒤 0.3초는 손을 뗀다 (끝에 함정이 없는지) · Leap 끝에서 뛰어내린다
        (double Sec, int Score, int Hits, int Level) Play(int level, int seed, bool good, RunBody? body = null, int scale = 3, Ride ride = Ride.Seek, FloorStat? stat = null, Rectangle? wide = null)
        {
            // wide: 다른 크기의 모니터 (말벌·돌은 화면 폭에 따라 출발하는 자리와 빠르기가 달라진다)
            var r = wide is { } mon ? new Runner(mon, mon.Bottom - 50, 40 * scale, body ?? child, new Random(seed), measure, level) : New(seed, level, body, scale);
            int hits = 0;
            played.Runs++;
            bool wasOn = false, falling = false;
            double handsOff = -1, leapt = -9;
            Thing? newest = null;
            var rode = new HashSet<Thing>();
            var spans = new List<Span>();
            bool padFloor = false; // 2층 앞 버섯을 밟고 떠 있는 중 (손대지 않으면 그 발판에 내려서야 한다)
            string tag = $"배율 {scale} Lv{level} 씨 {seed} {ride}{(wide is { } m ? $" 화면 {m.Width}×{m.Height}" : "")}";
            r.OnGimmick = (g, fresh) =>
            {
                Expect(r.Level >= Runner.UnlockOf(g), $"{tag}: {g} 는 Lv{Runner.UnlockOf(g)} 부터 나온다 (지금 Lv{r.Level})");
                played.Met[(int)g]++;
                if (fresh) played.Fresh[(int)g]++;
            };
            r.OnEvent = e =>
            {
                if (e == "bounce")
                {
                    played.Bounces++;
                    padFloor = false;
                    foreach (var t in r.Things) if (t.Kind == ThingKind.Pad && t.Hot == r.Clock && t.Bunch == 0) { padFloor = true; played.PadFloor++; }
                    Expect(!padFloor || r.Seen(Gimmick.Pad), $"{tag}: 2층 앞 버섯은 혼자 나온 버섯을 한 번 본 뒤에만 놓인다");
                }
                else if (e is "jump" or "jump2" or "drop") padFloor = false;
                else if (e == "land" && padFloor)
                {
                    padFloor = false;
                    Expect(r.Under is { Kind: ThingKind.Floor }, $"{tag}: 2층 앞 버섯을 밟으면 그 발판에 내려선다 ({r.Clock:F1}초)");
                }
                if (e != "hit") return;
                hits++;
                Spans(r, spans);
                var cause = ThingKind.Block;
                foreach (var x in spans) if (x.Front <= 2 && x.Back >= -2 && x.Kind != ThingKind.Pad) cause = x.Kind;
                played.Hits[Math.Max(0, (int)cause - (int)ThingKind.Pad)]++;
                played.AllHits++;
                if (stat != null && (falling || r.Clock < handsOff || r.Clock < leapt + 1.2)) stat.EndHits++;
            };
            for (int i = 0; i < 60 * 150 && !r.Over; i++)
            {
                bool on = r.Under != null && r.Landed;
                if (!good || falling || r.Clock < handsOff) r.Slide(false);
                else if (ride == Ride.Walk && on) { }
                else if (ride == Ride.Leap && on && r.Under!.X + r.Under.W - r.PetBox.Left <= r.Speed * .05) { r.Jump(); leapt = r.Clock; if (stat != null) stat.Ends++; }
                else Bot(r, ride != Ride.Plain);
                r.Update(1 / 60.0);
                if (ride == Ride.Walk)
                {
                    if (wasOn && r.Under == null && !r.Landed && r.Jumps == 0) falling = true; // 발판 끝에서 떨어지기 시작
                    else if (falling && r.Landed)
                    {
                        Expect(r.Lift == 0, "발판 끝에서 떨어지면 땅에 내려선다");
                        (falling, handsOff) = (false, r.Clock + .3);
                        if (stat != null)
                        {
                            stat.Ends++;
                            Spans(r, spans); // 내려선 자리에서 다음 장애물(기믹도)이 닿을 때까지
                            foreach (var x in spans) if (x.Kind != ThingKind.Pad && x.Back > 0) stat.MinGap = Math.Min(stat.MinGap, x.Front / r.Speed);
                        }
                    }
                }
                wasOn = r.Under != null;
                if (stat != null)
                {
                    stat.Sec += 1 / 60.0;
                    if (r.Under != null && rode.Add(r.Under)) stat.Ridden++;
                    if (r.Floors.Count > 0 && !ReferenceEquals(r.Floors[^1], newest)) // 새로 나온 발판 (연달아 둘은 한 묶음)
                    {
                        int from = newest == null ? -1 : r.Floors.IndexOf(newest);
                        for (int k = from + 1; k < r.Floors.Count; k++)
                        {
                            stat.Floors++;
                            if (k == 0 || r.Floors[k].X - (r.Floors[k - 1].X + r.Floors[k - 1].W) > r.Speed) stat.Bunches++;
                        }
                        newest = r.Floors[^1];
                    }
                }
                played.Sec += 1 / 60.0;
                if (i % 10 == 0) Fair(r, spans, tag);   // 기믹이 공정하게 놓였는지 (묶음 사이 틈, 처음 보는 것은 혼자, 앞지르지 않기)
                if (i % 30 != 0) continue;
                foreach (var t in r.Things)
                {
                    bool ok = t.Kind switch
                    {
                        ThingKind.Fly => t.Y == r.Ground - r.CloudUp && t.Scale == r.Px(2),
                        ThingKind.Block => t.Y == r.Ground && t.H <= r.TallMax && (r.TallBlocks.Contains(t.Art) || r.NormalBlocks.Contains(t.Art)),
                        ThingKind.Heart => t.Y == r.Ground - 2 * r.StandPx || r.Floors.Any(f => t.Y == r.Ground - r.TopOf(f) - 2 * r.StandPx && t.X >= f.X && t.X < f.X + f.W)
                            || t.Bunch > 0 && t.Y < r.Ground - r.JumpUp,    // 버섯으로 튄 꼭대기: 그냥 뛰어서는 안 닿는다
                        ThingKind.Pad => t.Y == r.Ground && t.Scale == r.Px(3) && t.H < r.DuckPx,
                        ThingKind.Wasp => t.Scale == r.Px(2) && t.Fast > 1 && t.Bunch > 0 && r.Ground - t.Y >= Runner.WaspLow * scale - .01 && r.Ground - t.Y <= r.CloudUp + .01,
                        ThingKind.Rock => t.Scale == r.Px(3) && t.Fast > 1 && t.Bunch > 0 && r.Ground - t.Y >= -.01 && r.Ground - t.Y <= r.CloudUp + .01,
                        _ => t.Scale == r.Px(1) && t.Y <= r.Ground,
                    };
                    Expect(ok, $"길에 나온 것은 몸 크기 규칙을 따른다: {t.Kind} {t.Sprite}×{t.Scale} 높이 {t.H} (선 키 {r.StandPx}, 배율 {scale})");
                }
                // 2층 발판: 높이는 뛰어 닿는 범위, 밑면은 선 키보다 높다. 앞머리 밑·끝난 뒤는 비어 있고, 둘레에 구름이 없고, 밑에는 보통 장애물만
                for (int k = 0; k < r.Floors.Count; k++)
                {
                    var f = r.Floors[k];
                    double top = r.TopOf(f), end = f.X + f.W, clear = r.Speed * (r.FallSec(top) + .5), lead = r.Speed * .4;
                    bool chained = k > 0 && f.X - (r.Floors[k - 1].X + r.Floors[k - 1].W) <= r.Speed;
                    string who = $"배율 {scale} Lv{level} 씨 {seed}: 발판(윗면 {top}, 길이 {f.W})";
                    Expect(top >= r.FloorLow && top <= r.FloorHigh && top <= Runner.FloorReach * r.JumpUp && top - f.H >= r.StandPx + (Runner.FloorHead - 1) * scale && f.H == r.FloorThick,
                        $"{who} 높이가 범위 {r.FloorLow}~{r.FloorHigh} 안, 밑면은 선 키 {r.StandPx} 위");
                    Expect(f.W >= r.Speed * .9, $"{who} 는 0.9초 넘게 달릴 만큼 길다");
                    foreach (var t in r.Things)
                    {
                        double left = t.X, right = t.X + t.W;
                        if (t.Kind == ThingKind.Block)
                        {
                            Expect(right <= end || left >= end + clear, $"{who} 끝에서 떨어져 내려선 뒤 0.5초 안에는 장애물이 없다: {t.Sprite} {(left - end) / r.Speed:F2}초 뒤");
                            Expect(chained || right <= f.X - lead || left >= f.X + lead, $"{who} 앞머리 밑은 비어 있다 (맞지 않고 올라갈 수 있다): {t.Sprite} {(left - f.X) / r.Speed:F2}초");
                            Expect(right <= f.X || left >= end || r.NormalBlocks.Contains(t.Art) && Runner.Hard(t).Top > r.Ground - top + f.H, $"{who} 밑에는 보통 장애물만 온다: {t.Sprite} 높이 {t.H}");
                        }
                        else if (t.Kind == ThingKind.Fly)
                            Expect(right <= f.X - lead || left >= end + clear, $"{who} 둘레에는 구름이 없다: {(left - f.X) / r.Speed:F2}초");
                        else if (t.Kind == ThingKind.Heart && left >= f.X && left < end && t.Y < r.Ground - top)
                            Expect(end - left >= r.Speed * .8, $"{who} 위의 하트는 뛰어 먹고도 같은 발판에 내려설 만큼 앞에 있다: 끝까지 {(end - left) / r.Speed:F2}초");
                    }
                }
            }
            return (r.Clock, r.Score, hits, r.Level);
        }
        var lazy = Play(1, 3, false);
        Expect(lazy.Sec < 30 && lazy.Level == 1, $"아무것도 안 하면 금방 끝나고 레벨도 안 오른다: {lazy.Sec:F0}초");
        var rows = new List<string>();
        foreach (int start in new[] { 1, 4, 8, 12 })
        {
            var runs = Enumerable.Range(0, 4).Select(seed => Play(start, 10 + seed, true)).ToList();
            rows.Add($"Lv{start}: {runs.Average(x => x.Sec):F0}초 {runs.Average(x => x.Score):F0}점 부딪힘 {runs.Average(x => x.Hits):F1} → Lv{runs.Average(x => x.Level):F1}");
            // 어느 레벨이든 잘하면 버티고 목표를 넘긴다 (빨라져도 내려설 틈이 있다)
            Expect(runs.Average(x => x.Sec) > 35 && runs.Average(x => x.Hits) <= 1.5, $"Lv{start} 도 잘하면 버틴다: {rows[^1]}");
            Expect(runs.Count(x => x.Level > start) >= 3, $"Lv{start} 도 잘하면 한 판에 레벨이 오른다: {rows[^1]}");
        }

        // 키: 슬라이드 중 점프는 되고 안 끝난다. Ctrl+Shift 를 0.5초 누르면 끝
        var keys = Make();
        bool quit = false;
        for (int i = 0; i < 30; i++) quit |= keys.Keys(false, true, 1 / 60.0);
        Expect(keys.Sliding && !quit, "Shift 누르면 슬라이드");
        for (int i = 0; i < 6; i++) quit |= keys.Keys(true, true, 1 / 60.0); // 0.1초 톡
        for (int i = 0; i < 6; i++) quit |= keys.Keys(false, true, 1 / 60.0);
        Expect(keys.Jumps == 1 && !quit, "슬라이드 중에 Ctrl 을 톡 치면 뛰고, 안 끝난다");
        for (int i = 0; i < 20; i++) quit |= keys.Keys(true, true, 1 / 60.0);
        Expect(!quit, "0.5초 전엔 안 끝난다");
        for (int i = 0; i < 15; i++) quit |= keys.Keys(true, true, 1 / 60.0);
        Expect(quit, "Ctrl+Shift 를 0.5초 누르면 끝");
        Expect(end.Things.All(t => t.X < area.Right + 400), "길 위의 것은 화면 밖에서 와서 사라진다");

        // ── 몸 크기별 (YELL 2026-11-13): 그림 데이터의 모든 형태 × 볼록이 배율. 판정이 눈에 보이는 몸을 따른다 ──
        var table = new List<string>();
        var floorTable = new List<string>();
        // 달리는 그림의 귀 끝 (칸px): 고를 수 있는 모든 캐릭터에서 가장 높은 것. 2층 발판 밑면이 이보다 높아야 귀가 발판에 안 묻힌다
        var earTop = forms.ToDictionary(f => f, f => 0);
        string earWho = "";
        foreach (var species in sp.SpeciesList)
        {
            var one = species == sp.Species ? sp : Sprites.Embedded(species);
            foreach (var f in forms)
                for (int i = 0; i < one.Frames($"{f}/0/walk"); i++)
                {
                    int top = one.Cell - Ui.Opaque(one.Get($"{f}/0/walk", i, 1), 0).Top;
                    if (top > earTop[f]) { earTop[f] = top; if (top >= earTop.Values.Max()) earWho = species; }
                }
        }
        double floorLow = 999, floorHigh = 0, floorRoom = 999; // 칸px
        int floorChecks = 0;
        var floorStat = new FloorStat();
        int combos = 0, jumps = 0;
        double worstSec = 999, worstHits = 0;
        foreach (var form in forms)
        {
            var body = BodyOf(form);
            // 그림과 맞는지: 서 있는 그림(귀 포함)은 선 키보다 낮지 않고, 엎드린 그림은 그만큼 낮다
            int Top(string pose) => sp.Cell - Ui.Opaque(sp.Get($"{form}/0/{pose}", 0, 1), 0).Top;
            int idleTop = Top("idle"), landTop = Top("land");
            int walkTop = Enumerable.Range(0, sp.Frames($"{form}/0/walk")).Max(i => sp.Cell - Ui.Opaque(sp.Get($"{form}/0/walk", i, 1), 0).Top); // 달리는 그림의 귀 끝
            Expect(idleTop >= body.Stand - 1 && idleTop <= body.Stand + 9, $"{form}: 선 키 {body.Stand} 가 그림(귀까지 {idleTop})과 맞는다");
            Expect(idleTop - landTop is >= 4 and <= 6, $"{form}: 엎드리면 그림이 5칸px 낮아진다 ({idleTop} → {landTop})");
            foreach (int scale in Scales)
            {
                combos++;
                var probe = Make(body, scale);
                int stand1 = probe.StandPx, duck1 = probe.DuckPx;
                Expect(probe.PetBox.Height == stand1 && Math.Abs(stand1 - body.Stand * scale) <= .5 && Math.Abs(duck1 - body.Duck * scale) <= .5
                    && Math.Abs(probe.BoxW - body.W * .8 * scale) <= .5, $"{form}×{scale}: 판정 상자는 몸 폭의 0.8, 선 키·엎드린 키");
                Expect(probe.CloudUp == (stand1 + duck1) / 2.0 && stand1 - probe.CloudUp >= 2 * scale && probe.CloudUp - duck1 >= 2 * scale, $"{form}×{scale}: 구름 바닥은 선 키와 엎드린 키 한가운데");
                if (scale == 3)
                    table.Add($"  {form,-13} 폭 {probe.BoxW,2} 선 키 {stand1,2} 엎드린 키 {duck1,2} 구름 바닥 {probe.CloudUp,4:F1} 장애물 보통 ≤{probe.NormalMax,4:F1} ({probe.NormalBlocks.Count}종, 가장 큰 것 {probe.NormalBlocks.Max(a => probe.Arts[a].H)}) 키 큰 것 ≤{probe.TallMax,5:F1} ({probe.TallBlocks.Count}종, 가장 큰 것 {probe.TallBlocks.Max(a => probe.Arts[a].H)})");

                // 찍는 그림(Stamp)은 규칙이 잰 상자와 같은 크기·자리다 (판정과 그림이 어긋나지 않는다)
                foreach (var a in probe.Arts)
                {
                    var stamp = Stamps.Of(sp).Get(a.Sprite, 0, a.Scale);
                    int cell = sp.Cell * a.Scale;
                    Expect(stamp.Bmp.Width == a.W && stamp.Bmp.Height == a.H && stamp.Dx + cell / 2 == a.Ox && stamp.Dy + cell / 2 + a.H == a.Oy, $"{form}×{scale}: {a.Sprite}×{a.Scale} 그림이 잰 상자와 같다");
                }

                // 서면 구름에 맞고 엎드리면 지나간다. 어느 속도에서든
                foreach (int level in new[] { 1, 12 })
                    foreach (bool down in new[] { false, true })
                    {
                        var r = Make(body, scale, level);
                        int hits = 0;
                        r.OnEvent = e => { if (e == "hit") hits++; };
                        double speed = level == 1 ? Runner.StartSpeedOf(1) : Runner.MaxSpeedOf(12); // 가장 느릴 때와 가장 빠를 때
                        r.Speed = speed * scale / 3;
                        var cloud = r.Add(ThingKind.Fly, "prop/cloud", r.Px(2), r.PetX + 300 * scale / 3.0, r.Ground - r.CloudUp);
                        Pass(r, cloud, x => x.Slide(down));
                        Expect(down ? hits == 0 : hits == 1, $"{form}×{scale}: {(down ? "엎드리면 구름 밑을 지나간다" : "서면 구름에 맞는다")} (속도 {speed})");
                    }

                // 맞춰 뛰면 나올 수 있는 모든 땅 장애물을 넘고, 가만히 있으면 부딪힌다
                foreach (int art in probe.NormalBlocks.Concat(probe.TallBlocks).Distinct())
                {
                    var a = probe.Arts[art];
                    bool isTall = !probe.NormalBlocks.Contains(art);
                    Expect(a.H <= (isTall ? probe.TallMax : probe.NormalMax) || probe.NormalBlocks.Count == 1, $"{form}×{scale}: {a.Sprite}×{a.Scale} 높이 {a.H} 가 한도 안");
                    foreach (int level in new[] { 1, 12 })
                        foreach (bool jump in new[] { true, false })
                        {
                            var r = Make(body, scale, level);
                            int hits = 0;
                            r.OnEvent = e => { if (e == "hit") hits++; };
                            double speed = level == 1 ? Runner.StartSpeedOf(1) : Runner.MaxSpeedOf(12);
                            r.Speed = speed * scale / 3;
                            var one = r.Add(ThingKind.Block, a.Sprite, a.Scale, r.PetX + 400 * scale / 3.0, r.Ground);
                            Pass(r, one, x => { if (jump) Bot(x); });
                            Expect(jump ? hits == 0 : hits == 1, $"{form}×{scale}: {a.Sprite}×{a.Scale} (높이 {a.H}, 선 키 {stand1}) 를 {(jump ? "맞춰 뛰면 넘는다" : "가만히 있으면 부딪힌다")} (속도 {speed})");
                            jumps++;
                        }
                }

                // 씨앗 무지개(선 키의 0.3~1배)는 서서 지나가면 다 먹고, 하트(선 키의 2배)는 뛰어야 닿는다
                var bow = Make(body, scale);
                Thing? seed = null;
                for (int i = 0; i < 6; i++) seed = bow.Add(ThingKind.Seed, "icon/seed", bow.Px(1), bow.PetX + (300 + i * 34) * scale / 3.0, bow.Ground - (.3 + .7 * Math.Sin(i / 5.0 * Math.PI)) * stand1);
                Pass(bow, seed!);
                Expect(bow.Seeds == 6, $"{form}×{scale}: 서서 지나가면 씨앗 무지개를 다 먹는다 ({bow.Seeds}/6)");
                foreach (bool jump in new[] { false, true })
                {
                    var r = Make(body, scale);
                    bool got = false;
                    r.OnEvent = e => got |= e == "heart";
                    var heart = r.Add(ThingKind.Heart, "icon/heart", r.Px(1), r.PetX + 300 * scale / 3.0, r.Ground - 2 * stand1);
                    Pass(r, heart, x => { if (jump && x.Lift == 0 && heart.X - x.PetX < x.Speed * .11) x.Jump(); }); // 올라가는 길에 스친다 (작은 몸은 다 뛰면 하트보다 높이 뜬다)
                    Expect(got == jump, $"{form}×{scale}: 하트는 {(jump ? "뛰면 닿는다" : "서서는 안 닿는다")}");
                }

                // ── 2층 발판: 한 번 뛰면 닿고, 선 채로 밑을 지나간다 ──
                int low = probe.FloorLow, high = probe.FloorHigh, thick = probe.FloorThick;
                Expect(thick == 7 * scale && low == stand1 + (int)Runner.FloorHead * scale + thick && high >= low + (int)Runner.FloorVary * scale && high <= Runner.FloorReach * probe.JumpUp && Math.Abs(probe.JumpUp - 188 * scale / 3.0) < scale,
                    $"{form}×{scale}: 발판 윗면 {low}~{high} 는 선 키 {stand1} + 머리 위 {Runner.FloorHead}칸px + 두께 7칸px 부터 {Runner.FloorVary}칸px 더까지, 뛰는 높이 {probe.JumpUp:F0} 의 {Runner.FloorReach}배 안");
                Expect((low - thick) / (double)scale >= walkTop + 2, $"{form}×{scale}: 발판 밑면 {(low - thick) / (double)scale:F1}칸px 는 달리는 그림(귀까지 {walkTop}) 위로 틈이 있다");
                Expect((low - thick) / (double)scale >= earTop[form] + 1, $"{form}×{scale}: 발판 밑면 {(low - thick) / (double)scale:F1}칸px 는 어느 캐릭터의 귀 끝(가장 높게 {earTop[form]}, {earWho})보다도 높다 — 귀가 더 긴 캐릭터가 생기면 Runner.FloorHead 를 올린다 (FloorReach 안에서)");
                (floorLow, floorHigh, floorRoom) = (Math.Min(floorLow, low / (double)scale), Math.Max(floorHigh, high / (double)scale), Math.Min(floorRoom, (probe.JumpUp - high) / scale));
                if (scale == 3) floorTable.Add($"  {form,-13} 윗면 {low,3}~{high,3} 밑면 {low - thick,3}~{high - thick,3} (선 키 {stand1,2}, 귀까지 {walkTop * scale,3}, 귀가 가장 긴 캐릭터 {earTop[form] * scale,3}) 뛰는 높이 {probe.JumpUp:F0} → 여유 {probe.JumpUp - high:F0}~{probe.JumpUp - low:F0}px, 윗면 위에 떠 있는 시간 {2 * Math.Sqrt(2 * (probe.JumpUp - high) / (Runner.Gravity * scale / 3.0)):F2}~{2 * Math.Sqrt(2 * (probe.JumpUp - low) / (Runner.Gravity * scale / 3.0)):F2}초");
                foreach (int top in new[] { low, high })
                    foreach (bool fast in new[] { false, true })
                    {
                        double speed = (fast ? Runner.MaxSpeedOf(12) : Runner.StartSpeedOf(1)) * scale / 3.0, u = scale / 3.0;
                        (Runner R, Thing F) Lay(double from, double sec)
                        {
                            var r = Make(body, scale, fast ? 12 : 1);
                            r.Speed = speed;
                            return (r, r.AddFloor(r.PetX + from, (int)Math.Ceiling(speed * sec / r.Arts[r.LedgeArt.M].W), top));
                        }
                        string at = $"{form}×{scale} 윗면 {top} 속도 {speed:F0}";

                        // 가만히 달리면 밑으로 지나간다: 올라서지도 걸리지도 않는다
                        var (under, uf) = Lay(300 * u, 1);
                        bool clear = true;
                        Pass(under, uf, x => clear &= x.Under == null && x.Lift == 0 && (x.PetBox.Top >= uf.Box.Bottom || !x.PetBox.IntersectsWith(uf.Box)));
                        Expect(clear && under.Energy > 95, $"{at}: 선 채로 발판 밑을 지나간다");

                        // 한 번 뛰면 올라선다 (느린 프레임에서도). 끝까지 가면 땅으로 떨어진다
                        foreach (double dt in new[] { 1 / 60.0, 1 / 30.0 })
                        {
                            var (r, f) = Lay(300 * u, 1);
                            int hopped = 0;
                            double onAt = -1, offAt = -1, downAt = -1;
                            r.OnEvent = e => { if (e is "jump" or "jump2") hopped++; };
                            for (int i = 0; i < 400 && downAt < 0; i++)
                            {
                                if (onAt < 0) Bot(r);
                                r.Update(dt);
                                if (onAt < 0 && r.Under == f) { onAt = r.Clock; Expect(r.Lift == top && r.Landed && r.Jumps == 0 && r.PetBox.Bottom == (int)(r.Ground - top), $"{at}: 발판 윗면에 선다"); }
                                else if (onAt >= 0 && offAt < 0 && r.Under == null) offAt = r.Clock;
                                else if (offAt >= 0 && r.Landed) downAt = r.Clock;
                            }
                            Expect(hopped == 1 && onAt > 0, $"{at}: 한 번 뛰어 발판에 올라선다 (프레임 {dt:F3}초, 점프 {hopped}번)");
                            Expect(downAt > 0 && r.Lift == 0 && Math.Abs(downAt - offAt - r.FallSec(top)) <= 2 * dt + .001, $"{at}: 발판이 끝나면 땅으로 떨어진다 ({downAt - offAt:F2}초, 계산 {r.FallSec(top):F2}초)");
                            floorChecks++;
                        }

                        // 밑에서 뛰면 뚫고 올라가 그 위에 선다 (위에서 내려올 때만 막힌다)
                        var (thru, tf) = Lay(-100 * u, 1);
                        double peak = 0;
                        thru.Jump();
                        for (int i = 0; i < 60 && thru.Under == null; i++) { thru.Update(1 / 60.0); peak = Math.Max(peak, thru.Lift); }
                        Expect(thru.Under == tf && thru.Lift == top && peak > top, $"{at}: 밑에서 뛰면 뚫고 올라가 그 위에 선다 (가장 높이 {peak:F0})");

                        // 발판 위: 뛰면 다시 그 위에 내려서고 두 번 뛰기도 된다. Shift 를 새로 누르면 뚫고 내려가고, 누른 채로 올라서면 안 내려간다
                        Runner Stand(double sec, out Thing f)
                        {
                            var (r, f1) = Lay(-100 * u, sec);
                            r.Lift = top;
                            r.Update(0);
                            Expect(r.Under == f1 && r.Landed && r.Rest == top, $"{at}: 발판 위에 서 있다");
                            f = f1;
                            return r;
                        }
                        if (!fast)
                        {
                            var twice = Stand(2.2, out var hf);
                            twice.Jump(); twice.Update(.05); twice.Jump();
                            Expect(twice.Jumps == 2 && twice.Under == null && !twice.Landed, $"{at}: 발판에서도 두 번 뛴다");
                            for (int i = 0; i < 120 && !twice.Landed; i++) twice.Update(1 / 60.0);
                            Expect(twice.Under == hf && twice.Lift == top && twice.Jumps == 0, $"{at}: 발판에서 뛰면 다시 그 위에 내려선다");

                            var drop = Stand(2.2, out var df);
                            bool ended = false;
                            for (int i = 0; i < 3; i++) { ended |= drop.Keys(false, false, 1 / 60.0); drop.Update(1 / 60.0); }
                            Expect(drop.Under == df && !drop.Sliding, $"{at}: 발판 위를 달린다");
                            ended |= drop.Keys(false, true, 1 / 60.0);
                            drop.Update(1 / 60.0);
                            Expect(drop.Under == null && drop.Lift < top && !drop.Landed, $"{at}: 발판 위에서 Shift 를 누르면 뚫고 내려간다");
                            for (int i = 0; i < 60 && !drop.Landed; i++) { ended |= drop.Keys(false, true, 1 / 60.0); drop.Update(1 / 60.0); }
                            ended |= drop.Keys(false, true, 1 / 60.0);
                            Expect(drop.Lift == 0 && drop.Landed && drop.Sliding && drop.Under == null && !ended, $"{at}: 내려와서도 Shift 를 누르고 있으면 엎드린다");
                            // 엎드린 채(Shift 누른 채) 뛰어 올라서면 그대로 달린다
                            for (int i = 0; i < 3; i++) { ended |= drop.Keys(i == 0, true, 1 / 60.0); drop.Update(1 / 60.0); }
                            for (int i = 0; i < 60 && !drop.Landed; i++) { ended |= drop.Keys(false, true, 1 / 60.0); drop.Update(1 / 60.0); }
                            for (int i = 0; i < 10; i++) { ended |= drop.Keys(false, true, 1 / 60.0); drop.Update(1 / 60.0); }
                            Expect(drop.Under == df && drop.Lift == top && !drop.Sliding && !ended, $"{at}: Shift 를 누른 채로 올라서면 안 내려간다");

                            // 발판 위 씨앗 무지개는 서서 다 먹고, 하트는 뛰어 먹고 같은 발판에 내려선다
                            var eatUp = Stand(2.2, out var ef);
                            Thing? last = null;
                            for (int i = 0; i < 6; i++) last = eatUp.Add(ThingKind.Seed, "icon/seed", eatUp.Px(1), eatUp.PetX + (200 + i * 34) * u, eatUp.Ground - top - (.3 + .7 * Math.Sin(i / 5.0 * Math.PI)) * stand1);
                            var heart = eatUp.Add(ThingKind.Heart, "icon/heart", eatUp.Px(1), eatUp.PetX + 300 * u, eatUp.Ground - top - 2 * stand1);
                            Pass(eatUp, last!);
                            Expect(eatUp.Seeds == 6 && !heart.Gone && eatUp.Under == ef, $"{at}: 발판 위 씨앗 무지개는 서서 다 먹고, 하트는 서서는 안 닿는다 ({eatUp.Seeds}/6)");
                            var reach = Stand(2.2, out var rf);
                            var heart2 = reach.Add(ThingKind.Heart, "icon/heart", reach.Px(1), reach.PetX + 300 * u, reach.Ground - top - 2 * stand1);
                            bool got = false;
                            reach.OnEvent = e => got |= e == "heart";
                            for (int i = 0; i < 150 && !(got && reach.Landed); i++) { Bot(reach); reach.Update(1 / 60.0); }
                            Expect(got && reach.Under == rf && reach.Lift == top, $"{at}: 발판 위 하트는 뛰어 먹고 같은 발판에 내려선다");
                        }
                    }

                // 잘하는 사람은 어느 몸·어느 배율에서든 버틴다 (가장 쉬운 레벨과 가장 어려운 레벨)
                foreach (int level in new[] { 1, 12 })
                {
                    var runs = Enumerable.Range(0, 4).Select(seed => Play(level, 20 + seed, true, body, scale, Ride.Seek, floorStat)).ToList();
                    double sec = runs.Average(x => x.Sec), hit = runs.Average(x => x.Hits);
                    (worstSec, worstHits) = (Math.Min(worstSec, sec), Math.Max(worstHits, hit));
                    Expect(sec > 35 && hit <= 1.5, $"{form}×{scale} Lv{level}: 잘하면 버틴다 ({sec:F0}초, 부딪힘 {hit:F1})");
                }
            }
        }
        Expect(floorStat.Floors > combos * 8 && floorStat.Ridden >= floorStat.Floors * .9, $"잘하는 사람은 2층을 거의 다 탄다: {floorStat.Ridden}/{floorStat.Floors}");

        // ── 2층: 얼마나 자주 나오는지, 2층을 타면 더 좋은지, 끝에 함정이 없는지 (여러 씨로) ──
        var freq = new List<string>();
        double lastRate = 0;
        foreach (int level in new[] { 1, 6, 12 })
        {
            var st = new FloorStat();
            for (int seed = 0; seed < 12; seed++) Play(level, 100 + seed, true, null, 3, Ride.Seek, st);
            double rate = st.Bunches / (st.Sec / 60);
            freq.Add($"Lv{level} {rate:F1}번");
            Expect(rate is >= 1 and <= 4 && rate > lastRate, $"2층은 1분에 한 번에서 네 번 사이, 레벨이 오르면 조금 잦아진다: {string.Join(", ", freq)}");
            lastRate = rate;
        }
        // 같은 길(같은 씨)을 2층을 찾아 타는 사람과 일부러 찾지 않는 사람이 달린다
        var seek = new List<(double Sec, int Score, int Hits, int Level)>();
        var plain = new List<(double Sec, int Score, int Hits, int Level)>();
        FloorStat seekStat = new(), plainStat = new();
        foreach (int level in new[] { 1, 4, 8, 12 })
            for (int seed = 0; seed < 10; seed++)
            {
                seek.Add(Play(level, 200 + seed, true, null, 3, Ride.Seek, seekStat));
                plain.Add(Play(level, 200 + seed, true, null, 3, Ride.Plain, plainStat));
            }
        string seekRow = $"찾아 타면 {seek.Average(x => x.Sec):F1}초 {seek.Average(x => x.Score):F0}점 부딪힘 {seek.Average(x => x.Hits):F2} (발판 {seekStat.Ridden}/{seekStat.Floors}), 안 찾으면 {plain.Average(x => x.Sec):F1}초 {plain.Average(x => x.Score):F0}점 부딪힘 {plain.Average(x => x.Hits):F2} ({plainStat.Ridden}/{plainStat.Floors})";
        Expect(seek.Average(x => x.Sec) >= plain.Average(x => x.Sec) && seek.Average(x => x.Score) > plain.Average(x => x.Score) && seek.Average(x => x.Hits) <= plain.Average(x => x.Hits) + .05 && seekStat.Ridden > plainStat.Ridden,
            $"2층을 타면 더 오래 버티고 더 번다: {seekRow}");
        // 끝의 함정: 끝까지 걷다 떨어져 내려선 뒤 0.3초 손을 떼도, 끝에서 뛰어내려도 안 맞는다
        FloorStat walk = new(), leap = new();
        int ends = 0;
        foreach (var form in forms)
            foreach (int scale in Scales)
                foreach (int level in new[] { 1, 6, 12 })
                    for (int seed = 0; seed < 3; seed++, ends++)
                    {
                        Play(level, 300 + seed, true, BodyOf(form), scale, Ride.Walk, walk);
                        Play(level, 300 + seed, true, BodyOf(form), scale, Ride.Leap, leap);
                        // 2층을 안 찾는 사람: 2층 앞 버섯을 그냥 밟는다 (그 발판에 내려서는지는 Play 가 본다). 버섯이 일찍 나오는 낮은 레벨에서
                        Play(level == 1 ? 2 : level == 6 ? 3 : level, 300 + seed, true, BodyOf(form), scale, Ride.Plain);
                    }
        Expect(walk.Ends > ends && walk.EndHits == 0 && walk.MinGap >= .45, $"발판 끝에서 떨어져도 함정이 없다: {ends}판 {walk.Ends}번 떨어져 {walk.EndHits}번 맞음, 내려선 뒤 다음 장애물까지 가장 짧게 {walk.MinGap:F2}초");
        Expect(leap.Ends > ends && leap.EndHits == 0, $"발판 끝에서 뛰어내려도 함정이 없다: {leap.Ends}번 뛰어 {leap.EndHits}번 맞음");

        // 다른 크기의 모니터에서도 (좁은 노트북 ~ 4K, 배율은 그 모니터에서 쓸 만한 것들): 잘하는 사람이 버티고, 기믹이 공정하게 놓인다
        //   넓은 화면에서는 말벌·돌이 더 멀리서 출발해 뒤 묶음이 더 기다리고, 좁은 화면에서는 말벌·돌이 덜 빠르다
        int wideRuns = 0, wideHits = 0;
        double wideSec = 0;
        foreach (var (mon, scales) in Monitors)
            foreach (int scale in scales)
                foreach (var form in new[] { "baby", forms[^1] })
                    foreach (int level in new[] { 4, 8, 12 })
                        for (int seed = 0; seed < 2; seed++, wideRuns++)
                        {
                            var run = Play(level, 400 + seed, true, BodyOf(form), scale, Ride.Seek, null, mon);
                            (wideHits, wideSec) = (wideHits + run.Hits, wideSec + run.Sec);
                        }
        Expect(wideSec / wideRuns > 35 && wideHits <= wideRuns * .5, $"다른 크기의 모니터에서도 잘하면 버틴다: {wideRuns}판 평균 {wideSec / wideRuns:F0}초, 모두 {wideHits}번 부딪힘");

        return $"달리기 잘하는 사람 ({string.Join(" / ", rows)}), 가만히 {lazy.Sec:F0}초\n"
            + $"달리기 몸 크기별: {forms.Count}형태 × 배율 {string.Join("·", Scales)} = {combos}조합 — 서면 구름에 맞고 엎드리면 지나감, 장애물 {jumps / 4}개를 두 속도에서 맞춰 뛰면 넘음, 잘하는 사람 가장 짧게 {worstSec:F0}초·가장 많이 부딪힘 {worstHits:F1}\n"
            + "  배율 3 (화면 px):\n" + string.Join("\n", table) + "\n"
            + $"달리기 2층: {combos}조합 모두 윗면 {floorLow:F0}~{floorHigh:F0}칸px(뛰는 높이 62.7, 여유 {floorRoom:F1}칸px 이상)에 한 번 뛰어 올라서고({floorChecks}번, 느린 프레임 포함) 선 채로 밑을 지나감(캐릭터 {sp.SpeciesList.Count}종 모두 귀도 안 닿음, 가장 긴 귀는 {earWho}), 밑에서 뛰면 뚫고 올라섬, Shift 로 내려감. 1분에 {string.Join(" · ", freq)}\n"
            + $"  {seekRow}\n"
            + $"  다른 모니터({string.Join(" · ", Monitors.Select(m => $"{m.Area.Width}×{m.Area.Height} 배율 {string.Join("·", m.Scales)}"))}): {wideRuns}판 평균 {wideSec / wideRuns:F0}초, 모두 {wideHits}번 부딪힘\n"
            + $"  끝의 함정 없음: {ends}판에서 {walk.Ends}번 걸어서 떨어져 {walk.EndHits}번 맞음 (내려선 뒤 0.3초 손 뗌, 다음 장애물까지 가장 짧게 {walk.MinGap:F2}초), {leap.Ends}번 뛰어내려 {leap.EndHits}번 맞음. 잘하는 사람은 발판 {floorStat.Ridden}/{floorStat.Floors} 탐\n"
            + "  배율 3 (화면 px):\n" + string.Join("\n", floorTable);
    }

    // 여러 판에 걸친 기믹 통계 (한 판 해 보기에서 모은다). Hits 는 무엇에 맞았는지: [0] 은 기믹이 아닌 것(버섯은 안 다친다), 그 뒤로 말벌·돌·구덩이·동굴
    sealed class GimStat
    {
        public readonly int[] Met = new int[5], Fresh = new int[5], Hits = new int[5];
        public int AllHits, Bounces, PadFloor, Checks, Runs;
        public double Sec, MinGap = 9, MinFresh = 9, MinLand = 9;   // MinLand: 버섯으로 튀어 내려선 뒤·동굴 지붕에서 떨어져 내려선 뒤 다음 것이 닿을 때까지
    }
    static GimStat played = new();

    // 부딪히는 것(과 버섯) 하나가 볼록이 몸에 닿기까지(Front)·다 지나가기까지(Back) 길이 더 흘러야 하는 거리. 길보다 빨리 오는 말벌·돌도 같은 자로 잰다
    readonly record struct Span(double Front, double Back, ThingKind Kind, Thing T);

    static void Spans(Runner r, List<Span> to)
    {
        to.Clear();
        var me = r.PetBox;
        foreach (var t in r.Things)
        {
            if (t.Gone) continue;
            if (t.Kind is ThingKind.Block or ThingKind.Fly or ThingKind.Wasp or ThingKind.Rock)
            {
                var h = Runner.Hard(t);
                to.Add(new((h.Left - me.Right) / t.Fast, (h.Right - me.Left) / t.Fast, t.Kind, t));
            }
            else if (t.Kind == ThingKind.Pad) to.Add(new(t.X + t.W / 6 - me.Right, t.X + t.W - t.W / 6 - me.Left, t.Kind, t));
        }
        foreach (var p in r.Pits) to.Add(new(p.X + r.PitEdge - r.PetX, p.X + p.W - r.PitEdge - r.PetX, p.Kind, p));
        foreach (var c in r.Caves) to.Add(new(c.X + r.CaveEdge - me.Right, c.X + c.W - r.CaveEdge - me.Left, c.Kind, c));
    }

    // 기믹이 공정하게 놓였는지 (지금 길 위에 있는 것 모두):
    //   묶음과 묶음 사이에 틈이 있다 (닿는 때로 0.5초 이상. 버섯 뒤는 튀어 내려설 때까지 + 0.65초, 동굴 뒤는 지붕에서 떨어지는 시간 + 0.65초, 2층이 끝난 뒤는 떨어지는 시간 + 0.5초)
    //   → 동굴 안·구덩이 위·버섯으로 튀는 길에 2층도 장애물도 없다. 이 판에서 처음 보는 기믹은 혼자, 앞뒤 1.2초 이상.
    //   길보다 빠른 말벌·돌은 화면에서 저보다 늦게 닿을 것을 앞지르지 않는다 (겹쳐 지나가지 않는다)
    static void Fair(Runner r, List<Span> spans, string who)
    {
        Spans(r, spans);
        var me = r.PetBox;
        double sp = r.Speed;
        played.Checks++;
        for (int i = 0; i < spans.Count; i++)
        {
            var a = spans[i];
            if (a.T.Bunch == 0) continue;
            bool mover = a.Kind is ThingKind.Wasp or ThingKind.Rock, seen = mover && a.T.X < r.Track.Right;
            double air = a.Kind == ThingKind.Pad ? Runner.BounceSec : a.Kind == ThingKind.Cave ? r.FallSec(r.TopOf(a.T)) : 0;  // 튀어 떠 있는·지붕에서 떨어지는 시간
            double after = air > 0 ? air + .65 : .5;
            string what = $"{who} {r.Clock:F1}초: {a.Kind}{(a.T.Fresh ? "(처음)" : "")}";
            for (int j = 0; j < spans.Count; j++)
            {
                if (i == j) continue;
                var b = spans[j];
                bool later = b.Front >= a.Front;
                double gap = (later ? b.Front - a.Back : a.Front - b.Back) / sp;
                if (seen && later) Expect(b.T.X >= a.T.X + a.T.W - 1, $"{what} 이 뒤에 닿을 {b.Kind} 를 화면에서 앞지르지 않는다");
                if (b.T.Bunch == a.T.Bunch)
                {
                    Expect(!a.T.Fresh, $"{what} 처음 보는 기믹은 혼자 나온다");
                    Expect(gap >= .5, $"{what} 둘씩 올 때 사이가 {gap:F2}초 (0.5초 이상)");
                    continue;
                }
                bool fresh = a.T.Fresh || b.T.Fresh;
                double need = fresh ? 1.2 : later ? after : .5;
                Expect(gap >= need, $"{what} 와 {(later ? "뒤" : "앞")}의 {b.Kind} 사이가 {gap:F2}초 ({need:F2}초 이상)");
                if (fresh) played.MinFresh = Math.Min(played.MinFresh, gap); else played.MinGap = Math.Min(played.MinGap, gap);
                if (later && air > 0) played.MinLand = Math.Min(played.MinLand, gap - air);
            }
            foreach (var f in r.Floors)
            {
                double front = f.X - me.Right, back = f.X + f.W - me.Left;
                bool later = front >= a.Front;
                double gap = (later ? front - a.Back : a.Front - back) / sp;
                double need = a.T.Fresh ? 1.2 : later ? after - .1 : r.FallSec(r.TopOf(f)) + .5;
                Expect(gap >= need, $"{what} 와 {(later ? "뒤" : "앞")}의 2층 사이가 {gap:F2}초 ({need:F2}초 이상)");
                if (seen && later) Expect(f.X >= a.T.X + a.T.W - 1, $"{what} 이 뒤에 올 2층을 화면에서 앞지르지 않는다");
                if (a.T.Fresh) played.MinFresh = Math.Min(played.MinFresh, gap);
            }
        }
    }

    // ── 기믹 (YELL 2026-11-13): 그림 데이터의 모든 형태 × 배율 × 가장 느릴 때·가장 빠를 때. 맞게 하면 지나가고, 틀리게 하면 뜻한 대로 맞는다 ──
    static string Gimmicks(Sprites sp)
    {
        var area = TestArea;
        var measure = Measure(sp);
        var forms = Forms();
        string[] names = { "버섯", "말벌", "돌", "구덩이", "동굴" };
        int combos = 0, cases = 0, arcLeast = 99;
        double waspRoom = 99, rockRoom = 99, pitWindow = 99, roofRoom = 99, bounceUp = 0, overTime = 99;
        var table = new List<string>();
        foreach (var form in forms)
        {
            var body = BodyOf(form);
            foreach (int scale in Scales)
            {
                combos++;
                double u = scale / 3.0, grav = Runner.Gravity * u, v1 = Runner.JumpSpeed * u, air = 2 * Runner.JumpSpeed / Runner.Gravity;
                Runner At(double speed, int level)
                {
                    var r = new Runner(area, TestGround, 40 * scale, body, new Random(7), measure, level) { Mute = true };
                    r.Things.Clear();
                    r.Speed = speed;
                    return r;
                }
                // 놓아둔 것이 다 지나가고 tail 초 더 달린다. 맞은 수를 돌려준다
                int Go(Runner r, Action<Runner>? each = null, double dt = 1 / 60.0, double tail = .4)
                {
                    int hits = 0;
                    var old = r.OnEvent;
                    r.OnEvent = e => { if (e == "hit") hits++; old?.Invoke(e); };
                    var spans = new List<Span>();
                    for (int i = 0; i < 60 * 40 && tail > 0; i++)
                    {
                        each?.Invoke(r);
                        r.Update(dt);
                        Spans(r, spans);
                        bool ahead = false;
                        foreach (var x in spans) ahead |= x.Back > -10;
                        if (!ahead) tail -= dt;
                    }
                    r.OnEvent = old;
                    cases++;
                    return hits;
                }

                // 모양 (속도와 무관): 말벌·돌의 높이는 볼록이까지 남은 거리로만 정해진다. 닿을 때는 맨 아래이거나 맨 위다
                var probe = At(Runner.StartSpeedOf(1) * u, 1);
                foreach (var kind in new[] { ThingKind.Wasp, ThingKind.Rock })
                    foreach (int way in new[] { 0, 1 })
                    {
                        var t = probe.AddMover(kind, way, probe.PetX, 1.3);
                        double want = way == 1 ? probe.CloudUp : kind == ThingKind.Wasp ? Runner.WaspLow * scale : 0;
                        Expect(Math.Abs(probe.UpOf(t) - want) < .01 && probe.Ground - t.Y == probe.UpOf(t), $"{form}×{scale}: {kind} {(way == 1 ? "높게" : "낮게")} 는 볼록이 자리에서 밑면이 {want:F1}");
                        int half = (probe.BoxW + t.W - 2 * (t.W / 6)) / 2 + 2; // 몸에 걸치는 동안 (부딪히는 자리 기준, 2px 더)
                        for (int d = -half; d <= half; d++)
                        {
                            t.X = probe.PetX + d - t.W / 2.0;
                            double up = probe.UpOf(t);
                            if (way == 1)
                            {
                                Expect(up >= probe.DuckPx + .7 * scale && up <= probe.CloudUp + .01, $"{form}×{scale}: 높게 오는 {kind} 는 몸에 걸치는 동안 내내 엎드린 키 {probe.DuckPx} 보다 높다 ({d}px 에서 {up:F1})");
                                if (kind == ThingKind.Wasp) waspRoom = Math.Min(waspRoom, (up - probe.DuckPx) / scale); else rockRoom = Math.Min(rockRoom, (up - probe.DuckPx) / scale);
                            }
                            else Expect(up + t.H <= probe.LowTop(t) + .01 && up < probe.DuckPx - t.H / 6, $"{form}×{scale}: 낮게 오는 {kind} 는 몸에 걸치는 동안 넘을 높이 {probe.LowTop(t):F0} 를 안 넘고, 밑으로는 못 지나간다 ({d}px 에서 {up:F1})");
                        }
                        // 넘을 높이 위에 떠 있는 시간이 가장 느릴 때 몸에 걸치는 시간보다 0.25초 넘게 길다 (뛸 때를 맞출 틈)
                        double over = 2 * Math.Sqrt(2 * (probe.JumpUp - probe.LowTop(t)) / grav), cross = (2.0 * half) / (Runner.StartSpeedOf(1) * u * 1.1);
                        if (way == 0) { overTime = Math.Min(overTime, over - cross); Expect(over - cross >= .25, $"{form}×{scale}: 낮게 오는 {kind} 를 넘을 틈 {over - cross:F2}초"); }
                        probe.Things.Clear();
                    }
                Expect(probe.TopOf(probe.AddCave(0, 1)) <= Runner.FloorReach * probe.JumpUp && probe.Caves[0].Box.Bottom <= probe.Ground - probe.DuckPx - 2 * scale && probe.Caves[0].Box.Bottom > probe.Ground - probe.StandPx + scale,
                    $"{form}×{scale}: 동굴 천장 밑면은 선 키와 엎드린 키 사이, 윗면 {probe.TopOf(probe.Caves[0]):F0} 은 뛰어 올라설 수 있는 높이");
                roofRoom = Math.Min(roofRoom, (probe.JumpUp - probe.TopOf(probe.Caves[0])) / scale);
                bounceUp = probe.BounceUp / scale;
                if (scale == 3)
                {
                    var (w, k) = (probe.AddMover(ThingKind.Wasp, 0, 0, 1.2), probe.AddMover(ThingKind.Rock, 0, 0, 1.2));
                    table.Add($"  {form,-13} 버섯 높이 {probe.Arts[probe.AddPad(0).Art].H} → 튀는 높이 {probe.BounceUp:F0} (뛰는 높이 {probe.JumpUp:F0}), 말벌 {w.W}×{w.H} 밑면 {Runner.WaspLow * scale:F0}~{probe.CloudUp:F1}, 돌 {k.W}×{k.H} 밑면 0~{Runner.HopUp * scale:F0} / 0~{probe.CloudUp:F1}, 구덩이 폭 {probe.AddPit(0, probe.PitTiles(Runner.PitMin * scale)).W}px 부터 (가장자리 {probe.PitEdge}px 는 안 빠짐), 동굴 천장 {probe.Ground - probe.Caves[0].Box.Bottom}~{probe.TopOf(probe.Caves[0]):F1} (선 키 {probe.StandPx}, 엎드린 키 {probe.DuckPx})");
                }

                foreach (bool fast in new[] { false, true })
                {
                    double speed = (fast ? Runner.MaxSpeedOf(12) : Runner.StartSpeedOf(1)) * u;
                    int level = fast ? 12 : 1;
                    string at = $"{form}×{scale} 속도 {speed:F0}";
                    Runner Lay() => At(speed, level);

                    // ── 버섯 점프대: 달려와 밟아도, 위에서 내려서도 튄다. 안 다친다. 뛰어넘으면 안 튄다 ──
                    foreach (double dt in new[] { 1 / 60.0, 1 / 30.0 })
                    {
                        var r = Lay();
                        var pad = r.AddPad(r.PetX + 300 * u);
                        int bounces = 0;
                        double peak = 0, from = -1, flew = 0;
                        r.OnEvent = e => { if (e == "bounce") { bounces++; from = r.Clock; } else if (e == "land" && flew == 0) flew = r.Clock - from; };
                        int hits = Go(r, x => peak = Math.Max(peak, x.Lift), dt, 1.3);
                        Expect(bounces == 1 && hits == 0 && r.Landed && r.Lift == 0 && r.Jumps == 0, $"{at}: 버섯을 달려와 밟으면 한 번 튀고 안 다친다 (튄 수 {bounces}, 맞음 {hits})");
                        Expect(peak > r.BounceUp - Runner.Bounce * u * dt * .55 - 1 && peak <= r.BounceUp + 1 && peak > (dt < 1 / 40.0 ? 1.4 : 1.3) * r.JumpUp && Math.Abs(flew - Runner.BounceSec) <= 3 * dt, /* 느린 프레임은 계산보다 조금 낮게 뜬다 */ $"{at}: 버섯은 뛰는 높이의 1.4배 넘게 튄다 (가장 높이 {peak:F0}, 계산 {r.BounceUp:F0}, {flew:F2}초)");

                        var drop = Lay();
                        var pad2 = drop.AddPad(drop.PetX + 1000 * u);
                        (bounces, flew) = (0, 0);
                        bool jumped = false, twice = false;
                        drop.OnEvent = e => { if (e == "bounce") bounces++; };
                        hits = Go(drop, x =>
                        {
                            if (!jumped && pad2.X + pad2.W / 2.0 - x.PetX <= x.Speed * (air - 2 * dt)) { x.Jump(); jumped = true; }  // 버섯 위에 내려서게
                            else if (bounces == 1 && !twice && x.Vy > 0) { Expect(x.Jumps == 1, $"{at}: 버섯으로 튄 것은 한 번 뛴 셈이다"); x.Jump(); twice = x.Jumps == 2; }
                        }, dt, 1.5);
                        Expect(bounces == 1 && hits == 0 && twice && drop.Landed, $"{at}: 버섯 위에 내려서도 튀고, 튄 뒤 한 번 더 뛸 수 있다 (튄 수 {bounces})");
                    }
                    if (!fast) // 뛰어넘으면 밟지 않는다 (느릴 때: 빠를 때는 뛴 채로 지나가고도 남는다)
                    {
                        var over = Lay();
                        var pad = over.AddPad(over.PetX + 300 * u);
                        int bounces = 0;
                        bool jumped = false;
                        over.OnEvent = e => { if (e == "bounce") bounces++; };
                        Go(over, x => { if (!jumped && pad.X - x.PetBox.Right <= x.Speed * .1) { x.Jump(); jumped = true; } });
                        Expect(bounces == 0, $"{at}: 버섯을 뛰어넘으면 튀지 않는다");
                    }
                    // 2층 앞 버섯: 밟으면 그 발판에 내려선다 (가장 낮은 발판·가장 높은 발판)
                    foreach (int top in new[] { probe.FloorLow, probe.FloorHigh })
                        foreach (double dt in new[] { 1 / 60.0, 1 / 30.0 })
                        {
                            var r = Lay();
                            var pad = r.AddPad(r.PetX + 300 * u);
                            var f = r.AddFloor(pad.X + pad.W / 6.0 + speed * Runner.PadLead, (int)Math.Ceiling(speed * 1.2 / r.Arts[r.LedgeArt.M].W), top);   // 가장 짧은 발판 (연달아 둘일 때)
                            bool on = false;
                            double stay = 0;
                            int hits = Go(r, x => { on |= x.Under == f; if (x.Under == f) { stay += dt; Expect(x.Lift == top, $"{at}: 발판 윗면에 선다"); } }, dt, 1.2);
                            Expect(stay > .3, $"{at}: 버섯으로 올라선 발판(윗면 {top})이 0.3초 넘게 남아 있다 ({stay:F2}초)");
                            Expect(on && hits == 0, $"{at}: 2층(윗면 {top}) 앞 버섯을 밟으면 그 발판에 내려선다 (프레임 {dt:F3}초)");
                        }

                    // ── 말벌·돌: 낮게 오면 뛰어넘고(엎드리면 맞는다), 높게 오면 엎드려 지나간다. 가만히 서 있으면 어느 쪽이든 맞는다 ──
                    foreach (var kind in new[] { ThingKind.Wasp, ThingKind.Rock })
                        foreach (int way in new[] { 0, 1 })
                            foreach (double dt in new[] { 1 / 60.0, 1 / 30.0 })
                            {
                                Runner One() { var r = Lay(); r.AddMover(kind, way, r.PetX + 500 * u, r.FastOf(kind == ThingKind.Wasp ? Runner.WaspFast : Runner.RockFast)); return r; }
                                string name = $"{at}: {(way == 1 ? "높게" : "낮게")} 오는 {(kind == ThingKind.Wasp ? "말벌" : "돌")} (프레임 {dt:F3}초)";
                                int stand = Go(One(), null, dt), duck = Go(One(), x => x.Slide(true), dt), bot = Go(One(), x => Bot(x), dt);
                                Expect(stand == 1, $"{name} 은 서 있으면 맞는다 ({stand})");
                                Expect(duck == (way == 1 ? 0 : 1), $"{name} 은 엎드리면 {(way == 1 ? "지나간다" : "맞는다")} ({duck})");
                                Expect(bot == 0, $"{name} 은 맞게 하면 안 맞는다 ({bot})");
                                if (way == 0 && dt > 1 / 40.0) continue;
                                if (way == 0) // 조금 이르게·늦게 뛰어도 넘는다 (가운데서 가장 높이 뜨는 때에서 0.08초씩)
                                    foreach (double off in new[] { -.08, .08 })
                                    {
                                        var r = One();
                                        var t = r.Things[0];
                                        bool jumped = false;
                                        int hits = Go(r, x => { if (!jumped && (t.X + t.W / 2.0 - x.PetX) / (x.Speed * t.Fast) <= air / 2 + off) { x.Jump(); jumped = true; } }, dt);
                                        Expect(hits == 0, $"{name} 을 {(off < 0 ? "0.08초 늦게" : "0.08초 일찍")} 뛰어도 넘는다");
                                    }
                            }

                    // ── 구덩이: 뛰어넘는다. 걷거나 엎드려 지나가면 빠진다 (한 번 맞고 튕겨 나온다) ──
                    foreach (double wide in new[] { Runner.PitMin * scale, speed * .26 })
                    {
                        Runner One(out Thing pit) { var r = Lay(); pit = r.AddPit(r.PetX + 400 * u, r.PitTiles(wide)); return r; }
                        var walk = One(out var p);
                        string name = $"{at}: 구덩이(폭 {p.W})";
                        double peak = 0;
                        int hits = Go(walk, x => peak = Math.Max(peak, x.Lift));
                        Expect(hits == 1 && peak > 0 && walk.Landed && walk.Lift == 0, $"{name} 를 걸어가면 빠진다 = 한 번 맞고 튕겨 나온다 (맞음 {hits}, 튄 높이 {peak:F0})");
                        Expect(Go(One(out _), x => x.Slide(true)) == 1, $"{name} 를 엎드려 지나가도 빠진다");
                        foreach (double dt in new[] { 1 / 60.0, 1 / 30.0 }) Expect(Go(One(out _), x => Bot(x), dt) == 0, $"{name} 를 맞춰 뛰면 넘는다 (프레임 {dt:F3}초)");
                        // 가장 늦게(구멍 바로 앞에서), 가장 일찍(건너자마자 내려서게) 뛰어도 넘는다
                        var late = One(out var lp);
                        bool jumped = false;
                        Expect(Go(late, x => { if (!jumped && lp.X + x.PitEdge - x.PetX <= x.Speed / 60 * 1.5) { x.Jump(); jumped = true; } }) == 0 && jumped, $"{name} 바로 앞에서 뛰어도 넘는다");
                        var early = One(out var ep);
                        jumped = false;
                        Expect(Go(early, x => { if (!jumped && ep.X + ep.W - x.PitEdge - x.PetX <= x.Speed * (air - 2.5 / 60)) { x.Jump(); jumped = true; } }) == 0 && jumped, $"{name} 를 건너자마자 내려서게 일찍 뛰어도 넘는다");
                        pitWindow = Math.Min(pitWindow, air - (p.W - 2 * walk.PitEdge) / speed);
                    }

                    // ── 동굴: Shift 를 누른 채 기어간다. 서 있거나 안에서 뛰면 머리 콩. 미리 뛰면 지붕에 올라선다 ──
                    {
                        Runner One(out Thing cave) { var r = Lay(); cave = r.AddCave(r.PetX + 400 * u, r.CaveTiles(speed * 1.2), 3); return r; }
                        var crawl = One(out var c);
                        for (int i = 0; i < 4; i++) crawl.Add(ThingKind.Seed, "icon/seed", crawl.Px(1), c.X + c.W / 2.0 + i * 34 * u, crawl.Ground - 10 * u);
                        string name = $"{at}: 동굴(길이 {c.W})";
                        foreach (double dt in new[] { 1 / 60.0, 1 / 30.0 })
                        {
                            var r = dt > 1 / 40.0 ? One(out _) : crawl;
                            Expect(Go(r, x => x.Slide(true), dt) == 0 && (r != crawl || r.Seeds == 4), $"{name} 을 엎드려 지나가면 안 맞고 안의 씨앗을 먹는다 (프레임 {dt:F3}초)");
                            Expect(Go(One(out _), x => Bot(x), dt) == 0, $"{name} 을 맞게 하면 안 맞는다 (프레임 {dt:F3}초)");
                        }
                        int stand = Go(One(out _));
                        Expect(stand >= 1 && stand <= 3, $"{name} 에 서서 들어가면 머리를 찧는다 ({stand}번)");
                        // 안에서 뛰면 머리를 찧고 그 자리에 있다 (천장을 뚫지 못한다). 다시 엎드리면 더 안 맞는다
                        var hop = One(out var hc);
                        bool jumped = false;
                        double peak = 0;
                        int hits = Go(hop, x =>
                        {
                            peak = Math.Max(peak, x.Lift);
                            x.Slide(true);
                            if (!jumped && hc.X + hc.W / 2.0 < x.PetX) { x.Jump(); jumped = true; }
                        });
                        Expect(hits == 1 && peak == 0 && jumped && hop.Jumps == 0, $"{name} 안에서 뛰면 머리를 찧는다: 한 번 맞고 천장을 뚫지 못한다 (맞음 {hits}, 뜬 높이 {peak:F0})");
                        // 미리 뛰면 지붕에 올라서서 그 위로 달린다 (Shift 를 눌러도 뚫고 내려가지 않는다). 끝나면 땅으로 떨어진다
                        var roof = One(out var rc);
                        double top = roof.TopOf(rc), rise = Rise(v1, top + 2 * scale, grav);
                        jumped = false;
                        bool on = false, kept = true;
                        string trace = "";
                        roof.OnEvent = e => { if (e is "hit" or "jump" or "land") trace += $" {e}@{roof.Clock:F2}s 높이 {roof.Lift:F0} 동굴 {rc.X - roof.PetBox.Right:F0}"; };
                        hits = Go(roof, x =>
                        {
                            if (!jumped && rc.X - x.PetBox.Right <= x.Speed * (rise + 2.5 / 60)) { x.Jump(); jumped = true; }  // 입구가 닿기 두 프레임 전에는 윗면보다 높이 떠 있게
                            if (x.Under == rc) { on = true; x.Keys(false, true, 1 / 60.0); kept &= x.Under == rc && x.Lift == top && !x.Sliding; }
                        }, tail: .6);
                        Expect(on && kept && hits == 0 && roof.Landed && roof.Lift == 0, $"{name} 지붕(윗면 {top:F0})에 미리 뛰어 올라서면 그 위로 달리고, 끝나면 땅에 내려선다 (맞음 {hits}, 올라섬 {on}, 그대로 {kept}:{trace})");
                        // 너무 늦게 뛰면 입구에 부딪힌다: 맞고, 지붕에 올라가지도 천장을 뚫지도 못한다
                        var bump = One(out var bc);
                        jumped = on = false;
                        peak = 0;
                        hits = Go(bump, x =>
                        {
                            if (!jumped && bc.X - x.PetBox.Right <= x.Speed / 60 * 2) { x.Jump(); jumped = true; }
                            on |= x.Under == bc;
                            if (bc.X < x.PetBox.Left && bc.X + bc.W > x.PetBox.Right) peak = Math.Max(peak, x.Lift);
                        });
                        Expect(hits >= 1 && !on && peak < top, $"{name} 입구 바로 앞에서 뛰면 부딪힌다 (맞음 {hits}, 안에서 뜬 높이 {peak:F0})");
                    }

                    // ── 길에 나오는 그대로 (처음 보는 꼴 → 그 뒤의 꼴 여러 번): 가만히 있으면 뜻한 대로 맞고, 잘하는 사람은 안 맞는다 ──
                    foreach (var what in Enum.GetValues<Gimmick>())
                    {
                        var lazy = Lay();
                        int bounces = 0;
                        lazy.OnEvent = e => { if (e == "bounce") bounces++; };
                        lazy.AddBunch(what);
                        int hits = Go(lazy, null, 1 / 60.0, what == Gimmick.Pad ? 1.3 : .4);
                        if (what == Gimmick.Pad)
                        {
                            Expect(hits == 0 && bounces == 1 && lazy.Seeds >= 5 && lazy.Landed && lazy.Lift == 0, $"{at}: 길에 나온 버섯을 밟으면 튀어서 씨앗 줄을 먹고 안 다친다 (씨앗 {lazy.Seeds}/7)");
                            arcLeast = Math.Min(arcLeast, lazy.Seeds);
                        }
                        else Expect(what == Gimmick.Cave ? hits >= 1 : hits == 1, $"{at}: 길에 처음 나온 {names[(int)what]} 은 가만히 있으면 맞는다 ({hits})");
                        var good = Lay();
                        hits = 0;
                        for (int n = 0; n < 6; n++)
                        {
                            good.AddBunch(what);
                            hits += Go(good, x => Bot(x), n % 2 == 0 ? 1 / 60.0 : 1 / 30.0, what == Gimmick.Pad ? 1.3 : .4);
                        }
                        Expect(hits == 0, $"{at}: 길에 나온 {names[(int)what]} 을 잘하는 사람은 안 맞는다 (6번에 {hits}번 맞음)");
                    }
                }
            }
        }

        // ── 열리는 차례와 잦기: 체력이 안 닳게 하고 가만히 세워 둔다 (레벨이 그대로다). 레벨 1 은 기믹이 없고, 레벨마다 하나씩 열린다 ──
        var child = BodyOf("child");
        var rate = new List<string>();
        double lastRate = -1, slowest = 0;
        var spansAll = new List<Span>();
        foreach (int level in new[] { 1, 2, 3, 4, 5, 6, 8, 12 })
        {
            var met = new int[5];
            double sec = 0;
            for (int seed = 0; seed < 6; seed++)
            {
                var r = new Runner(area, TestGround, 120, child, new Random(500 + seed), measure, level);
                var first = new double[5];
                r.OnGimmick = (g, fresh) =>
                {
                    Expect(level >= Runner.UnlockOf(g), $"Lv{level}: {names[(int)g]} 은 Lv{Runner.UnlockOf(g)} 부터 나온다");
                    Expect(fresh == (first[(int)g] == 0), $"Lv{level}: 이 판에서 처음 나온 {names[(int)g]} 만 「처음」 꼴이다");
                    if (first[(int)g] == 0) first[(int)g] = r.Clock;
                    met[(int)g]++;
                };
                string who = $"Lv{level} 씨 {500 + seed} 세워 둠";
                for (int i = 0; i < 60 * 75; i++)
                {
                    (r.Energy, r.Seeds) = (100, 0);   // 안 지치고, 주운 씨앗으로 레벨이 오르지도 않게
                    r.Update(1 / 60.0);
                    if (i % 5 == 0) Fair(r, spansAll, who);
                }
                Expect(r.Level == level, $"Lv{level}: 세워 둔 판은 레벨이 그대로다");
                sec += r.Clock;
                if (level is >= 2 and <= 6)
                {
                    double debut = first[level - 2];
                    Expect(debut > 0 && debut < 12, $"Lv{level} 씨 {500 + seed}: 새로 열린 {names[level - 2]} 은 시작하고 곧 나온다 ({debut:F1}초)");
                    slowest = Math.Max(slowest, debut);
                }
            }
            int all = met.Sum();
            Expect(level > 1 || all == 0, "Lv1 에는 기믹이 없다");
            for (int g = 0; g < 5; g++) Expect(met[g] > 0 == (level >= 2 + g), $"Lv{level}: {names[g]} {(level >= 2 + g ? "이 나온다" : "은 아직 안 나온다")} ({met[g]}번)");
            double perMin = all / (sec / 60);
            if (level is not (1 or 2 or 4 or 6 or 12)) continue; // 두 레벨씩 건너 견준다 (한 레벨 차이는 씨에 따라 뒤집히기도 한다)
            Expect(perMin > lastRate, $"레벨이 오르면 기믹이 잦아진다: Lv{level} 1분에 {perMin:F1}번 (앞 {lastRate:F1}번)");
            lastRate = perMin;
            if (level > 1) rate.Add($"Lv{level} {perMin:F1}번({string.Join("·", met.Where((_, g) => level >= 2 + g))})");
        }

        // 다른 크기의 모니터에 세워 둔 판: 넓으면 말벌·돌이 멀리서 출발하고(뒤 묶음이 그만큼 기다린다), 좁으면 덜 빠르다
        foreach (var (mon, scales) in Monitors)
            foreach (int scale in scales)
                foreach (int level in new[] { 4, 12 })
                    for (int seed = 0; seed < 3; seed++)
                    {
                        var r = new Runner(mon, mon.Bottom - 50, 40 * scale, child, new Random(600 + seed), measure, level);
                        string who = $"화면 {mon.Width}×{mon.Height} 배율 {scale} Lv{level} 씨 {600 + seed} 세워 둠";
                        for (int i = 0; i < 60 * 60; i++)
                        {
                            (r.Energy, r.Seeds) = (100, 0);
                            r.Update(1 / 60.0);
                            if (i % 5 == 0) Fair(r, spansAll, who);
                            if (i % 30 == 0)
                                foreach (var t in r.Things)
                                    if (t.Kind is ThingKind.Wasp or ThingKind.Rock)
                                    {
                                        double see = (r.Track.Right - r.PetX) / (r.Speed * t.Fast), least = (r.Track.Right - r.PetX) / (r.Speed * 1.1);
                                        Expect(t.Fast >= 1.1 && t.Fast <= 1 + (t.Kind == ThingKind.Wasp ? Runner.WaspFast : Runner.RockFast) && see >= Math.Min(Runner.MinSee * .85, least * .85),
                                            $"{who}: {t.Kind} 는 길보다 {t.Fast:F2}배 빨리 오고, 화면에 들어와 닿기까지 {see:F2}초 보인다");
                                    }
                        }
                    }

        var all2 = played;
        int gimHits = all2.Hits.Skip(1).Sum(), gimMet = all2.Met.Sum();
        Expect(gimMet > 3000 && all2.Met.All(n => n > 200) && all2.Fresh.All(n => n > 50), $"한 판 해 보기에서 기믹을 넉넉히 만났다: {string.Join("·", all2.Met)} (처음 {string.Join("·", all2.Fresh)})");
        Expect(gimHits <= gimMet * .01, $"잘하는 사람은 기믹에 거의 안 맞는다: {gimMet}번 만나 {gimHits}번 맞음 (말벌 {all2.Hits[1]}·돌 {all2.Hits[2]}·구덩이 {all2.Hits[3]}·동굴 {all2.Hits[4]})");
        Expect(all2.PadFloor > 50, $"2층 앞 버섯을 넉넉히 밟아 봤다: {all2.PadFloor}번");
        return $"달리기 기믹: {combos}조합 × 가장 느릴 때·가장 빠를 때 ({cases}번 달려 봄) — 버섯 점프대는 밟아도 위에 내려서도 튀고 안 다침(튀는 높이 {bounceUp:F1}칸px = 뛰는 높이의 {bounceUp / 62.67:F2}배, 길에 나온 씨앗 줄 7개 중 {arcLeast}개 이상 먹음, 2층 앞에서는 그 발판에 내려섬), "
            + $"말벌·돌은 낮게 오면 뛰어넘고 높게 오면 엎드려 지나감(엎드린 머리 위 틈 말벌 {waspRoom:F1}·돌 {rockRoom:F1}칸px 이상, 넘을 때 맞출 틈 {overTime:F2}초 이상, 서 있으면 맞음), "
            + $"구덩이는 뛰어넘고 걷거나 엎드리면 빠짐(뛸 때를 고를 틈 {pitWindow:F2}초 이상), 동굴은 엎드려 지나가고 서거나 안에서 뛰면 머리 콩(천장 못 뚫음), 미리 뛰면 지붕에 올라섬(여유 {roofRoom:F1}칸px 이상)\n"
            + $"  열리는 차례: Lv1 없음 → Lv2 버섯 → Lv3 말벌 → Lv4 돌 → Lv5 구덩이 → Lv6 동굴 (새로 열린 것은 가장 늦어도 {slowest:F1}초에 처음 나옴). 1분에 기믹 묶음 {string.Join(" · ", rate)}\n"
            + $"  한 판 해 보기 {all2.Runs}판({all2.Sec / 60:F0}분)에서 기믹 묶음 {gimMet}번(버섯 {all2.Met[0]}·말벌 {all2.Met[1]}·돌 {all2.Met[2]}·구덩이 {all2.Met[3]}·동굴 {all2.Met[4]}), 맞은 수 말벌 {all2.Hits[1]}·돌 {all2.Hits[2]}·구덩이 {all2.Hits[3]}·동굴 {all2.Hits[4]} (기믹 아닌 것 {all2.Hits[0]}), 버섯 {all2.Bounces}번 밟음(2층 앞 {all2.PadFloor}번 모두 그 발판에 내려섬)\n"
            + $"  공정 검사 {all2.Checks}번: 묶음 사이 가장 짧게 {all2.MinGap:F2}초(버섯으로 튀거나 동굴 지붕에서 떨어져 내려선 뒤로는 {all2.MinLand:F2}초), 처음 보는 기믹은 혼자·앞뒤 가장 짧게 {all2.MinFresh:F2}초, 말벌·돌이 뒤의 것을 앞지르지 않음\n"
            + "  배율 3 (화면 px):\n" + string.Join("\n", table);
    }

    enum Ride { Seek, Plain, Walk, Leap }

    // 검사에 쓰는 다른 모니터들 (작업 영역)과 거기서 쓸 만한 볼록이 배율
    static readonly (Rectangle Area, int[] Scales)[] Monitors =
    {
        (new Rectangle(0, 0, 1366, 728), new[] { 2, 3 }), (new Rectangle(0, 0, 1280, 680), new[] { 4 }),
        (new Rectangle(0, 0, 2560, 1400), new[] { 3, 4 }), (new Rectangle(0, 0, 3840, 2100), new[] { 3, 6 }),
    };

    // 여러 판에 걸친 2층 통계
    sealed class FloorStat
    {
        public int Floors, Bunches, Ridden, Ends, EndHits;
        public double Sec, MinGap = 9;
    }

    // ── 모듈: 조작, 시작·끝, 끝난 점수, 기록, 다시 칠할 자리 ──
    static string Module(SceneHost host)
    {
        var (area, sp, pet) = (TestArea, host.Sprites, host.Pet);
        var down = new HashSet<Keys> { Keys.Space, Keys.W, Keys.Up, Keys.Tab }; // 읽지 않는 키들은 눌려 있어도 아무 일 없다
        var game = new RunGame(host);
        Runner Seat(int seed = 4, int level = 1, bool empty = true)
        {
            var r = New(sp, area, TestGround, 3, "child", new Random(seed), level);
            if (empty) r.Things.Clear();
            game.Begin(r, Session.Fake(area, down.Contains));
            return r;
        }
        void Frames(int n) { for (int i = 0; i < n; i++) game.Frame(1 / 60.0); }

        // 조작: 어느 창에서든 Ctrl 톡 점프·Shift 앉기만. 본체는 Puppet 으로 제자리에서 달린다
        var play = Seat();
        Frames(20);
        Expect(play.Jumps == 0 && !play.Sliding && play.Lift == 0, "Space·글자 키·방향키로는 뛰지도 앉지도 않는다");
        Expect(game.Running && pet.Mode == Mode.Puppet && pet.X == play.PetX && pet.Y == play.Ground && pet.Dir == 1 && pet.Pose == "walk" && pet.PuppetMs == 60, "볼록이는 제자리에서 빠른 발로 달린다");
        down.Add(Keys.ControlKey);
        Frames(8);
        Expect(play.Jumps == 1 && play.Lift > 0 && pet.Pose == "happy" && pet.Y == play.Ground - play.Lift && pet.PuppetMs == 0, "Ctrl 을 누르는 순간 한 번 뛴다 (누르고 있어도 한 번)");
        down.Remove(Keys.ControlKey);
        Frames(2);
        down.Add(Keys.ControlKey);
        Frames(2);
        Expect(play.Jumps == 2 && pet.Pose == "roll", "공중에서 한 번 더 누르면 더블 점프");
        down.Remove(Keys.ControlKey);
        Frames(120);
        down.Add(Keys.ShiftKey);
        Frames(5);
        Expect(play.Lift == 0 && play.Sliding && pet.Pose == "land", "Shift 를 누르는 동안 앉는다");
        down.Remove(Keys.ShiftKey);
        Frames(2);
        Expect(!play.Sliding && pet.Pose == "walk", "Shift 를 떼면 일어난다");
        play.InvUntil = play.Clock + 1;
        int blinks = 0;
        for (int i = 0; i < 30; i++) { Frames(1); if (pet.Blink) blinks++; }
        Expect(blinks is > 5 and < 25, $"다친 뒤 안 다치는 동안 본체가 깜빡인다: 30프레임 중 {blinks}");
        play.HurtUntil = play.Clock + .2;
        Frames(1);
        Expect(pet.Pose == "hurt", "부딪히면 아파한다");

        // 손으로 잡아 옮기는 동안은 가는 대로 두고, 내려놓으면 다시 달린다
        pet.Mode = Mode.Held;
        (pet.X, pet.Y) = (500, 300);
        Frames(5);
        Expect(pet.X == 500 && pet.Y == 300 && game.Running, "잡혀 있는 동안은 자리를 건드리지 않는다");
        pet.Mode = Mode.Wander;
        Frames(2);
        Expect(pet.Mode == Mode.Puppet && pet.X == play.PetX && pet.PuppetBlink != null, "내려앉으면 다시 달린다");

        // 하는 중에 또 시작해도 판은 그대로. 보스 키(Stop(true))는 점수·표정 없이 바로 끝나고 기록은 조용히 남는다
        game.Start();
        Expect(ReferenceEquals(game.run, play), "하는 중에 또 시작해도 판은 그대로다");
        play.Seeds = 12;
        var open = game.ses!;
        game.Stop(true);
        Expect(!game.Running && open.Closed && game.ses == null && pet.Mode == Mode.Wander && !pet.Emoting && pet.PuppetBlink == null, "보스 키로 끝내면 점수·표정 없이 바로 끝난다");
        Expect(game.last == play.Score && game.best == play.Score && game.scores.Count == 1 && game.scores[0].Seeds == 12, "보스 키로 끝낸 판도 기록에 남는다");
        var none = new List<Rectangle>();
        using var ov = host.Overlay(game);
        game.Dirty(ov, none);
        Expect(none.Count == 0, "끝난 뒤에는 그릴 것이 없다");

        // 체력이 다 닳아 끝나면 머리 위에 점수를 2.5초 (그동안 녹화 숨김도 그대로), 지나면 저절로 끝난다
        var tired = Seat(5, 3);
        tired.Seeds = 200; // 2000점 넘게: 새 기록
        tired.Energy = .01;
        open = game.ses!;
        Frames(1);
        Expect(tired.Over && game.run == null && game.Running && !open.Closed, "체력이 닳아 끝나면 점수가 떠 있는 동안은 아직 끝나지 않았다 (녹화 숨김 유지)");
        Expect(pet.Mode == Mode.Anim && pet.Pose == "dizzy" && !pet.Emoting, "끝나면 어질어질, 별 말풍선 자리에는 점수가 뜬다");
        Expect(game.scoreShown == tired.Score && game.scoreBest && game.best == tired.Score && game.level == tired.Level, "새 기록이면 왕관과 함께, 다음 판은 오른 레벨에서");
        game.Dirty(ov, none);
        Expect(none.Count == 2 && none[1] == area, "끝난 프레임에 길 위의 것을 통째로 지운다");
        Frames((int)(ScoreSec * 60) - 10);
        Expect(game.Running, "점수는 2.5초 떠 있다");
        Frames(12);
        Expect(!game.Running && open.Closed, "점수가 사라지면 녹화 숨김도 풀린다");

        // 점수가 떠 있을 때 Esc 를 누르면 그것도 바로 지운다. 점수가 떠 있을 때 또 시작하면 새 판
        var again = Seat(6);
        again.Energy = .01;
        open = game.ses!;
        Frames(3);
        Expect(game.Running && game.run == null && !game.scoreBest, "기록을 못 넘으면 왕관 없이 점수만");
        game.Stop(true);
        Expect(!game.Running && open.Closed, "점수가 떠 있을 때 Esc 면 점수도 바로 지운다");
        again = Seat(6);
        again.Energy = .01;
        Frames(3);
        game.Start(); // 실제 조작(Session.Open)으로 열린다: 프레임은 돌리지 않는다
        Expect(game.run != null && !ReferenceEquals(game.run, again) && game.scoreLeft == 0 && game.run.Level == game.level, "점수가 떠 있을 때 또 시작하면 저장된 레벨에서 새 판");
        game.Command(new[] { "run", "stop" });
        Expect(!game.Running && game.scores.Count == 4 && pet.Mode == Mode.Wander, "디버그 stop 은 바로 끝나고 기록에 안 남는다");

        // 디버그 「run lv 6」: 그 레벨에서 해 본다. 끝까지 달려도 기록에 안 남고 저장된 레벨도 그대로다
        int kept = game.scores.Count, keptLevel = game.level, keptBest = game.best;
        game.Command(new[] { "run", "lv", "6" });
        Expect(game.run is { Level: 6 } && game.trial && !game.auto && game.level == keptLevel, "디버그 run lv 6 은 6레벨에서 시작한다 (저장된 레벨은 그대로)");
        game.run!.Seeds = 500; // 실제 조작(Session.Open)으로 열렸다: 프레임은 돌리지 않는다
        game.Stop(false);
        Expect(game.run == null && game.scores.Count == kept && game.level == keptLevel && game.best == keptBest && !game.trial, "해 본 판은 끝내도 기록에 안 남는다");
        pet.Hush();
        game.Command(new[] { "run", "lv", "9", "auto" });
        Expect(game.run is { Level: 9 } && game.trial && game.auto, "run lv 9 auto 는 9레벨을 알아서 달린다");
        game.Command(new[] { "run", "stop" });
        Expect(!game.Running && game.scores.Count == kept && pet.Mode == Mode.Wander, "해 보던 판을 멈춰도 기록은 그대로다");
        pet.Hush();

        // Ctrl+Shift 를 0.5초 누르는 옛 끝내기: 점수는 안 띄우고 기록은 남는다
        var hold = Seat(7);
        down.Add(Keys.ControlKey); down.Add(Keys.ShiftKey);
        Frames(20);
        Expect(game.Running, "Ctrl+Shift 는 0.5초 전엔 안 끝난다");
        Frames(15);
        Expect(!game.Running && pet.Mode == Mode.Anim && pet.Pose == "happy" && pet.Emoting && game.scores.Count == 5, "Ctrl+Shift 를 0.5초 누르면 점수 없이 끝나고 기록은 남는다");
        down.Clear();
        pet.Hush();

        // 앱처럼 돌린다 (볼록이 Update 다음에 게임 Frame): 달리는 동안 자리·자세는 판이 정하고, 끝나면 어질어질하다 하던 대로 돌아간다
        var live = Seat(11);
        void Live(int n) { for (int i = 0; i < n; i++) { pet.Update(1 / 60.0, false); game.Frame(1 / 60.0); } }
        Live(30);
        Expect(pet.Mode == Mode.Puppet && pet.X == live.PetX && pet.Y == live.Ground && pet.Pose == "walk" && !pet.P.Asleep, "달리는 동안 볼록이는 판이 정한 자리에 있다");
        live.Energy = .01;
        Live(31);
        Expect(game.Running && game.run == null && pet.Mode == Mode.Anim && pet.Pose == "dizzy" && !pet.Emoting, "끝나면 어질어질 (점수가 떠 있다)");
        Live(150);
        Expect(!game.Running && pet.Mode != Mode.Puppet && pet.Pose != "dizzy" && pet.PuppetMs == 0 && !pet.Blink, "점수가 사라지면 하던 대로 돌아간다");

        // 기록: 옛 저장 칸에서 넘어오고, 그대로 다시 저장된다. 최근 50판만 남긴다
        Expect(new RunGame(host).SaveState() == null && !new RunGame(host).Stats().Single().Played, "한 번도 안 했으면 기록이 없다");
        var old = new RunGame(host);
        old.LoadState(GameModes.WithLegacy(new SaveData { RunBest = 1240, RunLast = 300, RunLevel = 3, RunScores = new() { new RunRecord(new DateTime(2026, 11, 1), 2, 300, 12) } })["run"]);
        Expect(old.best == 1240 && old.last == 300 && old.level == 3 && old.scores.Count == 1 && old.scores[0] == new Played(new DateTime(2026, 11, 1), 2, 300, 12), "옛 기록 칸이 그대로 넘어온다");
        Expect(old.Stats().Single() is { Played: true } stat && stat.Value.Contains(1240.ToString("N0", L.Culture)) && old.HudLine.Contains("1240"), "설정 → 상태에 레벨과 최고 점수");
        var copy = new RunGame(host);
        copy.LoadState(old.SaveState());
        Expect(copy.SaveState() == old.SaveState() && copy.level == 3, "기록은 저장했다 읽어도 같다");
        copy.LoadState("{깨진");
        Expect(copy.best == 1240, "깨진 기록은 버린다");
        for (int i = 0; i < Keep + 5; i++) { copy.Begin(New(sp, area, TestGround, 3, "child", new Random(i), 1), Session.Fake(area)); copy.Stop(true); }
        Expect(copy.scores.Count == Keep && copy.best == 1240 && copy.last == 0, $"최근 {Keep}판만 남긴다");

        // 안내 말풍선: 한 번 그려 둔 그림이 그 자리에 바로 그린 것과 같다
        var note = Seat(8);
        Frames(1);
        var rect = ov.Client(game.GuideRect(ov, note));
        using (var direct = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb))
        {
            using (var dg = Graphics.FromImage(direct)) ov.DrawNoteAt(dg, note.PetX, note.Ground, note.Track, game.guide);
            var cached = game.GuidePic(ov, note);
            bool same = cached.Size == rect.Size && rect.Width > 40 * 3 && game.guide.Length == 4;
            for (int y = 0; same && y < rect.Height; y++) for (int x = 0; same && x < rect.Width; x++) same = cached.GetPixel(x, y) == direct.GetPixel(rect.X + x, rect.Y + y);
            Expect(same, "안내 말풍선: 그려 둔 그림이 바로 그린 것과 같다");
        }
        game.Stop(true);

        // 다시 칠할 자리: 가만히 서서 한 판(안내 → 부딪힘 → 레벨 별 → 끝난 점수)을 끝까지. 바뀐 자리만 다시 그려도 지나간 그림이 남지 않는다
        var paint = Seat(9, 1, empty: false);
        bool leveled = false;
        var strip = new Rectangle(area.Left, TestGround - 420, area.Width, 460); // 2층 발판 위의 하트까지
        var (bad, first) = SceneKit.Repaint(ov, game, strip, () =>
        {
            if (!game.Running) return false;
            if (!leveled && paint.Clock > 5) { paint.Seeds += Runner.GoalOf(1) / 10; leveled = true; }
            game.Frame(1 / 60.0);
            pet.EmoteUntil = 0; // 부딪힐 때 뜨는 말풍선 그림은 앱(Overlay)이 제 자리를 따로 칠한다: 여기서는 게임 것만 본다
            return true;
        }, 60 * 120, every: 6);
        Expect(!game.Running && leveled && paint.Level == 2 && bad == 0, $"바뀐 자리만 다시 칠해도 화면이 맞다: {bad}번 다름 (처음 {first}프레임), 끝남 {!game.Running}");
        pet.Hush();

        // 2층: 발판 위에서도 제자리에서 달리고(본체가 그 높이로), Shift 를 새로 누르면 뚫고 내려가 엎드린다. 발판을 타고 뛰어도 바뀐 자리만 칠하면 된다
        var upper = new RunGame(host);
        var floor = New(sp, area, TestGround, 3, "child", new Random(12), 1);
        floor.Things.Clear();
        upper.Begin(floor, Session.Fake(area, down.Contains));
        var ledge = floor.AddFloor(floor.PetX - 100, 11, floor.FloorHigh);
        for (int i = 0; i < 6; i++) floor.Add(ThingKind.Seed, "icon/seed", 1, floor.PetX + 300 + i * 34, floor.Ground - floor.FloorHigh - 30);
        using var ov2 = host.Overlay(upper);
        int step = 0;
        var (bad2, first2) = SceneKit.Repaint(ov2, upper, strip, () =>
        {
            if (step == 2 || step == 80) down.Add(Keys.ControlKey); else down.Remove(Keys.ControlKey); // 밑에서 뛰어 올라서고, 위에서 한 번 더 뛴다
            upper.Frame(1 / 60.0);
            if (step == 70) Expect(floor.Under == ledge && pet.Y == floor.Ground - floor.FloorHigh && pet.Pose == "walk" && pet.PuppetMs == 60, "발판 위에서도 빠른 발로 달린다 (본체가 발판 높이에)");
            if (step == 90) Expect(floor.Under == null && floor.Lift > floor.FloorHigh && pet.Pose == "happy", "발판 위에서도 뛴다");
            return ++step < 150;
        }, 600, every: 3);
        Expect(bad2 == 0 && floor.Under == ledge && floor.Seeds == 6, $"발판을 타는 동안에도 바뀐 자리만 다시 칠하면 화면이 맞다: {bad2}번 다름 (처음 {first2}프레임), 씨앗 {floor.Seeds}");
        down.Add(Keys.ShiftKey);
        for (int i = 0; i < 2; i++) upper.Frame(1 / 60.0);
        Expect(floor.Under == null && !floor.Landed && pet.Pose == "happy", "발판 위에서 Shift 를 누르면 뚫고 내려간다");
        for (int i = 0; i < 40; i++) upper.Frame(1 / 60.0);
        Expect(floor.Lift == 0 && floor.Sliding && pet.Pose == "land" && pet.Y == floor.Ground, "내려와서도 누르고 있으면 엎드린다");
        down.Clear();
        upper.Command(new[] { "run", "stop" });
        pet.Hush();

        // 기믹 그림: 여러 장짜리(버섯·말벌·돌)는 모든 장이 첫 장의 상자 안에 든다 (다시 칠할 자리가 첫 장 상자다). 동굴 가운데 조각 세 가지는 같은 상자다
        var stamps = Stamps.Of(sp);
        foreach (int k in Scales)
        {
            foreach (var (name, frames) in new[] { ("run/shroom", 2), ("run/wasp", 2), ("run/rock", 4) })
            {
                var row = stamps.Row(name, k);
                Expect(row.Length == frames && row.All(f => row[0].Box(0, 0).Contains(f.Box(0, 0))), $"{name}×{k}: {frames}장 모두 첫 장의 상자 안에 그려진다");
            }
            var mid = stamps.Row("run/cave_m", k);
            Expect(mid.Length == 3 && mid.All(f => f.Box(0, 0) == mid[0].Box(0, 0)) && mid[0].Bmp.Width == sp.Cell * k, $"run/cave_m×{k}: 가운데 조각 세 가지는 크기가 같고 칸을 꽉 채운다");
        }
        Expect(Enumerable.Range(0, 40).Select(n => CaveLook(new Thing { Way = 3 }, n)).Distinct().Count() == 3, "동굴 천장에 고드름돌·박쥐·큰 고드름돌이 섞여 나온다");

        // 기믹: 놓아둔 버섯·말벌·돌·구덩이·동굴을 알아서 달리는 동안(튀고, 뛰어넘고, 엎드리고) 바뀐 자리만 다시 칠해도 화면이 맞다. 튈 때 본체도 같이 올라간다
        var trick = new RunGame(host);
        var gim = New(sp, area, TestGround, 3, "child", new Random(21), 12);
        gim.Things.Clear();
        gim.Mute = true;
        trick.Begin(gim, Session.Fake(area));
        trick.auto = true;
        double far = gim.PetX + 500;
        gim.AddPad(far);
        gim.AddMover(ThingKind.Wasp, 0, far += 1300, gim.FastOf(Runner.WaspFast));
        gim.AddMover(ThingKind.Wasp, 1, far += 700, gim.FastOf(Runner.WaspFast));
        gim.AddMover(ThingKind.Rock, 0, far += 700, gim.FastOf(Runner.RockFast));
        gim.AddMover(ThingKind.Rock, 1, far += 700, gim.FastOf(Runner.RockFast));
        gim.AddPit(far += 600, gim.PitTiles(160));
        var den = gim.AddCave(far += 800, gim.CaveTiles(900), 3);
        for (int i = 0; i < 4; i++) gim.Add(ThingKind.Seed, "icon/seed", 1, den.X + 300 + i * 34, gim.Ground - 10);
        using var ov3 = host.Overlay(trick);
        int bounced = 0, struck = 0;
        double peak = 0, petPeak = 0;
        bool crawled = false, squashed = false;
        gim.OnEvent += e => { if (e == "bounce") bounced++; else if (e == "hit") struck++; };
        var (bad3, first3) = SceneKit.Repaint(ov3, trick, strip, () =>
        {
            trick.Frame(1 / 60.0);
            pet.EmoteUntil = 0;
            (peak, petPeak) = (Math.Max(peak, gim.Lift), Math.Max(petPeak, gim.Ground - pet.Y));
            crawled |= gim.Sliding && pet.Pose == "land" && den.X < gim.PetX && den.X + den.W > gim.PetX;
            squashed |= gim.Things.Any(t => t.Kind == ThingKind.Pad && Cel(gim, t) == 1);
            return trick.run != null && (gim.Caves.Count > 0 || gim.Clock < 3);
        }, 60 * 30, every: 2);
        Expect(bad3 == 0, $"기믹이 지나가는 동안에도 바뀐 자리만 다시 칠하면 화면이 맞다: {bad3}번 다름 (처음 {first3}프레임)");
        Expect(bounced == 1 && struck == 0 && squashed && peak > gim.JumpUp * 1.4 && petPeak == peak && crawled && gim.Seeds == 4 && gim.Pits.Count == 0 && gim.Things.Count == 0,
            $"알아서 달리면 버섯에 튀고(본체도 그 높이로) 말벌·돌·구덩이를 넘고 동굴을 기어간다: 튐 {bounced}, 맞음 {struck}, 가장 높이 {peak:F0}, 씨앗 {gim.Seeds}");
        trick.Command(new[] { "run", "stop" });
        pet.Hush();
        // 길에 저절로 나오는 대로 (12레벨, 알아서 달린다) 30초
        var mix = new RunGame(host);
        var wild = New(sp, area, TestGround, 3, "child", new Random(22), 12);
        mix.Begin(wild, Session.Fake(area));
        mix.auto = true;
        int kinds = 0;
        wild.OnGimmick = (g, _) => kinds |= 1 << (int)g;
        using var ov4 = host.Overlay(mix);
        var (bad4, first4) = SceneKit.Repaint(ov4, mix, strip, () => { mix.Frame(1 / 60.0); pet.EmoteUntil = 0; return mix.run != null && wild.Clock < 30; }, 60 * 40, every: 3);
        Expect(bad4 == 0 && System.Numerics.BitOperations.PopCount((uint)kinds) >= 3, $"12레벨을 알아서 달리는 30초 동안 바뀐 자리만 다시 칠해도 화면이 맞다: {bad4}번 다름 (처음 {first4}프레임), 나온 기믹 {System.Numerics.BitOperations.PopCount((uint)kinds)}가지");
        mix.Command(new[] { "run", "stop" });
        pet.Hush();
        return "달리기 모듈: Ctrl·Shift 만 읽음, 보스 키는 점수 없이 바로, 끝난 점수 2.5초 동안 녹화 숨김 유지, 옛 기록 넘어옴, 바뀐 자리만 다시 칠해도 화면이 맞음 (2층 발판을 타고 뛸 때도, 기믹 다섯 가지가 지나갈 때도)";
    }

    // 달리기 한 프레임 값: 규칙(Update)·다시 칠할 자리(Sync)·그리기(Render)를 따로 재고, 프레임마다 새로 잡는 메모리도 센다.
    // 화면 없이 재는 것이라 창에 실제로 칠하는 값은 빠져 있다. 2026-11-13 에 고치기 전 → 뒤 (같은 재기, Debug 빌드, 길 위 12개):
    //   달릴 때  규칙 3.2µs 72B → 1.0µs 6B, 다시 칠할 자리 47µs 3201B → 7µs 0B, 그리기 177µs 1562B → 63µs 1B
    //   안내 4초  규칙 3.0µs 73B → 0.7µs 7B, 다시 칠할 자리 15µs 3239B → 7µs 4B, 그리기 500µs 1678B → 57µs 2B
    // 2층 발판을 넣은 뒤 (2026-11-13): 프레임마다 잡는 메모리는 그대로 (달릴 때 6B·0B·0B), 발판이 지나는 동안만 긴 띠 하나를 더 칠한다 —
    //   --mode-bench run (8레벨 한 판 2971프레임): 규칙 2.8 → 3.2µs, 그리기 98 → 100µs, 다시 칠한 넓이 64.8 → 77.5천px²/프레임, 메모리 6B/프레임 그대로.
    //   말풍선 그림(하트·부딪힘)은 앱 것이라 빼고 잰다: 화면 없이 잴 때는 볼록이 시계가 안 가서 한 번 뜨면 끝까지 남아 값을 흐린다
    // 기믹(버섯·말벌·돌·구덩이·동굴)을 넣은 뒤 (2026-11-13): --mode-bench run (8레벨 한 판 2971프레임) 규칙 3.2 → 4.6µs, 그리기 105 → 106µs,
    //   다시 칠한 넓이 77.5 → 68.6천px²/프레임, 메모리 6 → 7B/프레임 (Thing 이 다섯 칸 커졌다), 가장 느린 프레임 2.2 → 4.7ms.
    //   규칙 값과 가장 느린 프레임은 기믹 코드가 처음 불릴 때의 JIT 한 번(2.5ms쯤)이 들어간 것이다: 규칙을 미리 한 판 돌려 두고 재면 규칙 1.9µs, 가장 느린 프레임 2.1ms.
    //   말벌·돌은 프레임마다 높이 식(말벌은 Math.Cos 한 번)을 한 번씩, 구덩이·동굴은 지나가는 동안 띠 하나씩 더 칠한다
    static string Perf(SceneHost host)
    {
        var (area, sp) = (TestArea, host.Sprites);
        var game = new RunGame(host);
        using var ov = host.Overlay(game);
        using var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        double[] ms = new double[3];
        long[] bytes = new long[3];
        int frames = 0, things = 0;
        void Section(int i, Action a)
        {
            long b0 = GC.GetAllocatedBytesForCurrentThread(), t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            a();
            ms[i] += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            bytes[i] += GC.GetAllocatedBytesForCurrentThread() - b0;
        }
        Action update = () => { game.run!.Update(1 / 60.0); game.Track(); }, sync = ov.Sync, render = () => ov.Render(g);
        string Bench(bool guide, int warm, int count)
        {
            int seed = 0;
            for (int i = 0; i < warm + count; i++)
            {
                // 달릴 때: 안내 말풍선이 지난 뒤부터. 안내: 처음 4초만 되풀이
                if (game.run is not { Over: false } r || (guide && r.Clock > 3.9))
                {
                    r = New(sp, area, TestGround, 3, "child", new Random(seed++), 8);
                    if (!guide) r.Clock = 10;
                    game.Begin(r, Session.Fake(area));
                }
                Bot(r);
                host.Pet.EmoteUntil = 0; // 하트·부딪힘 말풍선 그림은 앱(Overlay)이 그린다: 여기서는 게임 것만 잰다 (볼록이 시계가 안 가서 한 번 뜨면 끝까지 남는다)
                if (i == warm) { Array.Clear(ms); Array.Clear(bytes); frames = things = 0; } // 처음은 데우기 (그림 불러오기)
                Section(0, update);
                Section(1, sync);
                Section(2, render);
                frames++;
                things += r.Things.Count;
            }
            game.Stop(true);
            ov.Sync(); // 지난 판의 것을 치운다
            double all = (bytes[0] + bytes[1] + bytes[2]) / (double)frames;
            Expect(all < 400, $"달리기 한 프레임에 새로 잡는 메모리가 적다 ({(guide ? "안내" : "달릴 때")}): {all:F0}B");
            return $"규칙 {ms[0] / frames * 1000:F1}µs {bytes[0] / (double)frames:F0}B, 다시 칠할 자리 {ms[1] / frames * 1000:F1}µs {bytes[1] / (double)frames:F0}B, 그리기 {ms[2] / frames * 1000:F1}µs {bytes[2] / (double)frames:F0}B (길 위 평균 {things / (double)frames:F1}개)";
        }
        string running = Bench(false, 600, 6000), guiding = Bench(true, 600, 6000);
        host.Pet.Hush();
        return $"달리기 한 프레임 (6000프레임): 달릴 때 {running} / 안내 4초 {guiding}";
    }

    // 속도 재기: 같은 씨로 8레벨 한 판을 자동 조종으로 끝까지 (--mode-bench run). 처음 4초는 안내 말풍선이 떠 있다
    public static string BenchRun()
    {
        var host = new SceneHost();
        var game = new RunGame(host);
        var r = New(host.Sprites, TestArea, TestGround, 3, "child", new Random(5), 8);
        game.Begin(r, Session.Fake(TestArea));
        using var ov = host.Overlay(game);
        return Bench.Run("run", ov, game, r.Track, () => { if (r.Over) return false; Bot(r); host.Pet.EmoteUntil = 0; return true; }, () => { r.Update(1 / 60.0); game.Track(); }); // 말풍선 그림(앱 것)은 빼고 잰다
    }

    // 장면 그려 보기 (--mode-scene run[@배율] <그림.png>): 형태마다 한 줄. 왼쪽부터 서서 구름에 맞음 · 레벨 별 · 엎드려 구름 밑 ·
    // 보통 장애물과 키 큰 장애물(가장 큰 것) · 씨앗 무지개와 하트 · 끝난 점수 ·
    // 2층 (가장 낮은 발판, 가장 높은 발판 위의 볼록이와 씨앗 무지개·하트, 그 밑으로 선 볼록이와 보통 장애물. 노란 점선은 한 번 뛰어 오르는 높이).
    // 빨간 테두리는 볼록이 판정, 파란 테두리는 부딪히는 자리.
    // 형태 하나만 자세히 (GameModes.SceneArg, 옛 --run-scene <그림.png> <배율> <형태>): 두 배로 키워 왼쪽·오른쪽 두 장으로 (<이름>-1.png, <이름>-2.png)
    // 기믹은 따로 한 장 더 (<이름>-gimmick.png, 형태 하나만이면 <이름>-gimmick-1~4.png): GimmickScene
    // 실제로 달리는 모습 (--mode-scene run <그림.png> live[레벨]): 알아서 달리는 한 판에서 기믹 묶음이 다가올 때마다 한 줄씩 찍는다: LiveScene
    public static void Scene(string path, int scale)
    {
        string? only = GameModes.SceneArg;
        if (only != null && only.StartsWith("live", StringComparison.Ordinal)) { LiveScene(path, scale, int.TryParse(only[4..], out int lv) ? lv : 12); return; }
        var forms = Forms().Where(f => only == null || f == only).ToList();
        double u = scale / 3.0;
        int width = (int)(2460 * u), rowH = (int)(440 * u);
        using var bmp = new Bitmap(width, rowH * forms.Count);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(150, 160, 170));
        using var red = new Pen(Color.FromArgb(230, 40, 60));
        using var blue = new Pen(Color.FromArgb(40, 90, 230));
        using var line = new Pen(Color.FromArgb(110, 118, 128));
        using var limit = new Pen(Color.FromArgb(250, 185, 40)) { DashStyle = DashStyle.Dash };
        for (int row = 0; row < forms.Count; row++)
        {
            // 줄마다 제 좌표(0, 0)에서 그려 그림의 그 줄 자리로 옮긴다: 큰 배율에서 그림이 화면보다 커도 아래 줄까지 다 나온다
            // (창은 화면보다 크게 못 잡아서, 그림 창 밖으로 나간 줄에는 길 위의 것이 안 그려졌다)
            string form = forms[row];
            var track = new Rectangle(0, 0, width, rowH);
            int ground = track.Bottom - (int)(30 * u), cell = 40 * scale;
            var whole = g.Save();
            g.TranslateTransform(0, row * rowH);
            var host = new SceneHost(form, scale);
            var (sp, brain) = (host.Sprites, host.Pet);
            var game = new RunGame(host);
            var modes = new IGameMode[] { game };
            var run = game.run = New(sp, track, ground, scale, form, new Random(1), 3);
            run.Clock = 10;
            run.LevelAt = run.Clock - .5;
            run.Energy = 60;
            using var ov = new Overlay(brain, () => sp, track) { Modes = () => modes };
            double X(double at) => at * u;
            void PetAt(double x, bool down, int lift = 0)
            {
                g.DrawImageUnscaled(sp.Get($"{form}/0/{(down ? "land" : "walk")}", 0, scale), (int)(x - cell / 2.0), ground - lift - cell);
                int h = down ? run.DuckPx : run.StandPx;
                g.DrawRectangle(red, (int)(x - run.BoxW / 2.0), ground - lift - h, run.BoxW - 1, h - 1);
            }
            void Outline(Thing t) { var r = Runner.Hard(t); g.DrawRectangle(blue, r.X, r.Y, r.Width - 1, r.Height - 1); }
            int Tallest(IReadOnlyList<int> pool) => pool.MaxBy(a => run.Arts[a].H);

            g.DrawLine(line, 0, ground, width, ground);
            var hit = run.Add(ThingKind.Fly, "prop/cloud", run.Px(2), 0, ground - run.CloudUp);
            var pass = run.Add(ThingKind.Fly, "prop/cloud", run.Px(2), 0, ground - run.CloudUp);
            (hit.X, pass.X) = (X(150) - hit.W / 2.0, X(700) - pass.W / 2.0);
            var (n, t2) = (run.Arts[Tallest(run.NormalBlocks)], run.Arts[Tallest(run.TallBlocks)]);
            var low = run.Add(ThingKind.Block, n.Sprite, n.Scale, X(1010), ground);
            var high = run.Add(ThingKind.Block, t2.Sprite, t2.Scale, X(1130), ground);
            for (int i = 0; i < 6; i++) run.Add(ThingKind.Seed, "icon/seed", run.Px(1), X(1340 + i * 34), ground - (.3 + .7 * Math.Sin(i / 5.0 * Math.PI)) * run.StandPx);
            run.Add(ThingKind.Heart, "icon/heart", run.Px(1), X(1440), ground - 2 * run.StandPx);
            g.DrawLine(limit, (int)X(900), ground - (int)run.NormalMax, (int)X(1110), ground - (int)run.NormalMax);
            g.DrawLine(limit, (int)X(1110), ground - (int)run.TallMax, (int)X(1260), ground - (int)run.TallMax);

            // 2층: 가장 낮은 발판(짧게), 가장 높은 발판 위에 볼록이·씨앗 무지개·하트, 밑으로는 선 볼록이와 가장 큰 보통 장애물
            run.AddFloor(X(1900), 1, run.FloorLow);
            run.AddFloor(X(2060), 3, run.FloorHigh);
            for (int i = 0; i < 6; i++) run.Add(ThingKind.Seed, "icon/seed", run.Px(1), X(2200 + i * 34), ground - run.FloorHigh - (.3 + .7 * Math.Sin(i / 5.0 * Math.PI)) * run.StandPx);
            run.Add(ThingKind.Heart, "icon/heart", run.Px(1), X(2300), ground - run.FloorHigh - 2 * run.StandPx);
            var below = run.Add(ThingKind.Block, n.Sprite, n.Scale, X(2290), ground);
            g.DrawLine(limit, (int)X(1880), ground - (int)run.JumpUp, (int)X(2440), ground - (int)run.JumpUp);

            PetAt(X(150), false);
            PetAt(run.PetX, false);
            PetAt(X(700), true);
            PetAt(X(930), false);
            PetAt(X(1300), false);
            PetAt(X(2130), false);
            ov.Render(g);
            PetAt(X(2130), false, run.FloorHigh); // 발판 위 (본체는 발판보다 앞에 그려진다)
            foreach (var t in new[] { hit, pass, low, high, below }) Outline(t);

            // 끝난 점수: 달리기가 끝난 뒤 머리 위 (새 기록이면 왕관)
            game.run = null;
            (brain.X, brain.Y) = (X(1700), ground);
            (game.scoreShown, game.scoreBest, game.scoreLeft) = (1234, true, ScoreSec);
            g.DrawImageUnscaled(sp.Get($"{form}/0/dizzy", 0, scale), (int)(brain.X - cell / 2.0), ground - cell);
            ov.Render(g);
            (brain.X, brain.Y) = (X(1830), ground);
            (game.scoreShown, game.scoreBest) = (870, false);
            g.DrawImageUnscaled(sp.Get($"{form}/0/happy", 0, scale), (int)(brain.X - cell / 2.0), ground - cell);
            ov.Render(g);
            g.Restore(whole);
        }
        GimmickScene(path, scale, forms, only != null);
        if (only == null) { bmp.Save(path); return; }
        for (int half = 0; half < 2; half++)
        {
            using var zoom = new Bitmap(width, rowH * 2);
            using var zg = Graphics.FromImage(zoom);
            (zg.InterpolationMode, zg.PixelOffsetMode) = (InterpolationMode.NearestNeighbor, PixelOffsetMode.Half);
            zg.DrawImage(bmp, new Rectangle(0, 0, width, rowH * 2), new Rectangle(half * width / 2, 0, width / 2, rowH), GraphicsUnit.Pixel);
            zoom.Save(Path.Combine(Path.GetDirectoryName(path) ?? "", $"{Path.GetFileNameWithoutExtension(path)}-{half + 1}.png"));
        }
    }

    // 기믹 장면 (<이름>-gimmick.png): 형태마다 두 줄.
    //   윗줄: 버섯 점프대(밟는 자리의 볼록이, 튄 길을 따라 놓인 씨앗, 꼭대기의 볼록이. 왼쪽은 밟혀 눌린 버섯) · 낮게 오는 말벌(뛰어넘는다) · 높게 오는 말벌(엎드린다)
    //   아랫줄: 낮게 튀는 돌(뛰어넘는다) · 높게 튀는 돌(엎드린다) · 구덩이(가장 좁은 것 위를 뛰어넘는 볼록이, 오른쪽은 넓은 것) · 동굴(안에 엎드린 볼록이와 씨앗, 지붕 위의 볼록이)
    //   말벌·돌은 볼록이에게 닿은 것과, 같은 것이 오는 길 위의 여러 자리를 함께 그린다 (높이는 남은 거리로만 정해진다).
    //   빨간 테두리는 볼록이 판정, 파란 테두리는 부딪히는(빠지는) 자리, 노란 점선은 한 번 뛰는 높이(버섯 칸에서는 튀는 높이도)
    static void GimmickScene(string path, int scale, List<string> forms, bool zoom)
    {
        double u = scale / 3.0;
        int width = (int)(2460 * u), highH = (int)(440 * u), lowH = (int)(270 * u), rowH = highH + lowH;
        using var bmp = new Bitmap(width, rowH * forms.Count);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(150, 160, 170));
        using var red = new Pen(Color.FromArgb(230, 40, 60));
        using var blue = new Pen(Color.FromArgb(40, 90, 230));
        using var line = new Pen(Color.FromArgb(110, 118, 128));
        using var limit = new Pen(Color.FromArgb(250, 185, 40)) { DashStyle = DashStyle.Dash };
        for (int row = 0; row < forms.Count; row++)
            for (int half = 0; half < 2; half++)
            {
                string form = forms[row];
                int h = half == 0 ? highH : lowH, ground = h - (int)(half == 0 ? 30 * u : 40 * u), cell = 40 * scale;
                var whole = g.Save();
                g.TranslateTransform(0, row * rowH + half * highH);
                var host = new SceneHost(form, scale);
                var (sp, brain) = (host.Sprites, host.Pet);
                var game = new RunGame(host);
                var modes = new IGameMode[] { game };
                using var ov = new Overlay(brain, () => sp, new Rectangle(0, 0, width, h)) { Modes = () => modes };
                g.DrawLine(line, 0, ground, width, ground);
                g.DrawLine(line, 0, h - 1, width, h - 1);
                // 칸 하나: 볼록이는 칸 왼쪽에서 22% 자리에 있다
                Runner Panel(int left, int w, int level = 12)
                {
                    var r = New(sp, new Rectangle((int)(left * u), 0, (int)(w * u), h), ground, scale, form, new Random(1), level);
                    (r.Mute, r.Clock, r.Energy) = (true, 10, 60);
                    r.Things.Clear();
                    g.DrawLine(line, (int)(left * u), 0, (int)(left * u), h);
                    return r;
                }
                void Show(Runner r) { game.run = r; ov.Render(g); }
                void PetAt(Runner r, double x, string pose, double lift = 0)
                {
                    g.DrawImageUnscaled(sp.Get($"{form}/0/{pose}", 0, scale), (int)(x - cell / 2.0), (int)(ground - lift) - cell);
                    int tall = pose == "land" ? r.DuckPx : r.StandPx;
                    g.DrawRectangle(red, (int)(x - r.BoxW / 2.0), (int)(ground - lift) - tall, r.BoxW - 1, tall - 1);
                }
                void Box(Rectangle b) => g.DrawRectangle(blue, b.X, b.Y, b.Width - 1, b.Height - 1);
                void Dash(Runner r, double up) => g.DrawLine(limit, r.Track.Left + 4, ground - (int)up, r.Track.Right - 4, ground - (int)up);
                // 말벌·돌을 볼록이에게서 d(칸px) 떨어진 자리에: 닿은 것(0)부터 오는 길을 따라
                void Trail(Runner r, ThingKind kind, int way, params int[] ds)
                {
                    foreach (int d in ds)
                    {
                        var t = r.AddMover(kind, way, 0, 1.3);
                        t.X = r.PetX + d * scale - t.W / 2.0;
                        t.Y = r.Ground - r.UpOf(t);
                    }
                    Dash(r, r.JumpUp);
                    double sf = u;
                    if (way == 0) for (int i = 0; i < 3; i++) r.Add(ThingKind.Seed, "icon/seed", r.Px(1), r.PetX + (i - 1) * 34 * sf - 6 * sf, ground - r.LowTop(r.Things[0]) - (i == 1 ? 70 : 50) * sf);
                    else for (int i = 0; i < 4; i++) r.Add(ThingKind.Seed, "icon/seed", r.Px(1), r.PetX + (i - 1.5) * 34 * sf - 6 * sf, ground - 10 * sf);
                    Show(r);
                    PetAt(r, r.PetX, way == 0 ? "happy" : "land", way == 0 ? r.LowTop(r.Things[0]) + 14 * u : 0);
                    Box(Runner.Hard(r.Things[0]));
                }

                if (half == 0)
                {
                    // 버섯 점프대: 길에 나오는 그대로 (가장 느릴 때)를 밟는 자리로 옮겨 놓는다
                    var pad = Panel(0, 620, 1);
                    pad.AddBunch(Gimmick.Pad);
                    var shroom = pad.Things.First(t => t.Kind == ThingKind.Pad);
                    double shift = shroom.X + shroom.W / 6.0 - (pad.PetX + pad.BoxW / 2.0);
                    foreach (var t in pad.Things) t.X -= shift;
                    pad.AddPad(pad.PetX - 110 * u).Hot = pad.Clock; // 밟혀 눌린 그림
                    Dash(pad, pad.JumpUp);
                    Dash(pad, pad.BounceUp);
                    Show(pad);
                    PetAt(pad, pad.PetX, "walk");
                    PetAt(pad, pad.PetX + pad.Speed * Runner.BounceSec / 2, "happy", pad.BounceUp);
                    Trail(Panel(620, 920), ThingKind.Wasp, 0, 0, 55, 110, 165, 220);
                    Trail(Panel(1540, 920), ThingKind.Wasp, 1, 0, 55, 110, 165, 220);
                }
                else
                {
                    Trail(Panel(0, 520), ThingKind.Rock, 0, 0, 54, 108);
                    Trail(Panel(520, 920), ThingKind.Rock, 1, 0, 40, 80, 120, 160, 200);
                    // 구덩이: 가장 좁은 것(뛰어넘는 중)과 가장 빠를 때의 넓은 것
                    var pit = Panel(1440, 440);
                    var narrow = pit.AddPit(0, pit.PitTiles(Runner.PitMin * scale));
                    narrow.X = pit.PetX - narrow.W / 2.0;
                    var wide = pit.AddPit(pit.PetX + 90 * u, pit.PitTiles(Runner.MaxSpeedOf(12) * u * .26));
                    for (int i = 0; i < 3; i++) pit.Add(ThingKind.Seed, "icon/seed", pit.Px(1), pit.PetX + (i - 1) * 34 * u - 6 * u, ground - (i == 1 ? 70 : 50) * u);
                    Dash(pit, pit.JumpUp);
                    Show(pit);
                    PetAt(pit, pit.PetX, "happy", 64 * u);
                    foreach (var p in new[] { narrow, wide }) g.DrawLine(blue, (int)p.X + pit.PitEdge, ground, (int)(p.X + p.W) - pit.PitEdge, ground);
                    // 동굴: 안에 엎드린 볼록이와 씨앗, 지붕 위의 볼록이
                    var cave = Panel(1880, 580);
                    var c = cave.AddCave(cave.PetX - 70 * u, 3, 1);
                    for (int i = 0; i < 4; i++) cave.Add(ThingKind.Seed, "icon/seed", cave.Px(1), cave.PetX + (70 + i * 34) * u, ground - 10 * u);
                    Dash(cave, cave.JumpUp);
                    Show(cave);
                    PetAt(cave, cave.PetX, "land");
                    PetAt(cave, cave.PetX + 270 * u, "walk", cave.TopOf(c));
                    Box(new Rectangle((int)c.X + cave.CaveEdge, c.Box.Y, c.W - 2 * cave.CaveEdge, c.H));
                }
                game.run = null;
                g.Restore(whole);
            }
        string name = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", System.IO.Path.GetFileNameWithoutExtension(path));
        if (!zoom) { bmp.Save(name + "-gimmick.png"); return; }
        for (int n = 0; n < 4; n++) // 형태 하나만: 두 배로 키워 윗줄 왼쪽·오른쪽, 아랫줄 왼쪽·오른쪽
        {
            int top = n < 2 ? 0 : highH, tall = n < 2 ? highH : lowH;
            using var big = new Bitmap(width, tall * 2);
            using var zg = Graphics.FromImage(big);
            (zg.InterpolationMode, zg.PixelOffsetMode) = (InterpolationMode.NearestNeighbor, PixelOffsetMode.Half);
            zg.DrawImage(bmp, new Rectangle(0, 0, width, tall * 2), new Rectangle(n % 2 * width / 2, top, width / 2, tall), GraphicsUnit.Pixel);
            big.Save($"{name}-gimmick-{n + 1}.png");
        }
    }

    // 실제로 달리는 모습: 그 레벨에서 알아서 달리는 한 판 (어린이, 같은 씨). 기믹 묶음이 볼록이 앞 화면 한가운데쯤 왔을 때마다 달리는 띠를 한 줄씩 찍는다 (열두 줄까지).
    // 길에 저절로 나온 그대로라 묶음 사이 틈·씨앗 놓임·말벌과 돌이 오는 길을 눈으로 본다
    static void LiveScene(string path, int scale, int level)
    {
        var host = new SceneHost("child", scale);
        var (sp, pet) = (host.Sprites, host.Pet);
        var game = new RunGame(host);
        var r = New(sp, TestArea, TestGround, scale, "child", new Random(31), level);
        game.Begin(r, Session.Fake(TestArea));
        game.auto = true;
        using var ov = host.Overlay(game);
        double u = scale / 3.0;
        var view = new Rectangle(0, TestGround - (int)(400 * u), TestArea.Width, (int)(450 * u));
        const int Rows = 12;
        using var bmp = new Bitmap(view.Width, (view.Height + 6) * Rows);
        using var g = Graphics.FromImage(bmp);
        using var frame = new Bitmap(ov.Width, ov.Height);
        using var fg = Graphics.FromImage(frame);
        using var line = new Pen(Color.FromArgb(110, 118, 128));
        g.Clear(Color.White);
        var spans = new List<Span>();
        int shot = 0, lastBunch = 0, cell = 40 * scale;
        for (int i = 0; i < 60 * 120 && shot < Rows && game.run != null; i++)
        {
            game.Frame(1 / 60.0);
            pet.EmoteUntil = 0;
            Spans(r, spans);
            Thing? near = null;
            foreach (var x in spans) if (x.T.Bunch > lastBunch && x.Front > 0 && x.T.X - r.PetX < TestArea.Width * .3) near = x.T;
            if (near == null) continue;
            lastBunch = near.Bunch;
            fg.Clear(Color.FromArgb(150, 160, 170));
            fg.DrawLine(line, 0, TestGround, ov.Width, TestGround);
            ov.Render(fg);
            fg.DrawImageUnscaled(sp.Get($"child/0/{pet.Pose}", 0, scale), (int)(pet.X - cell / 2.0), (int)pet.Y - cell); // 본체는 그림 창 위에 따로 뜬다
            g.DrawImage(frame, new Rectangle(0, shot++ * (view.Height + 6), view.Width, view.Height), view, GraphicsUnit.Pixel);
        }
        bmp.Save(path);
    }
}
