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
        return Rules(sp) + "\n" + Perf(host) + "\n" + Module(host);
    }

    // ── 규칙 (화면 없이) ──
    static string Rules(Sprites sp)
    {
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
        (double Sec, int Score, int Hits, int Level) Play(int level, int seed, bool good, RunBody? body = null, int scale = 3)
        {
            var r = New(seed, level, body, scale);
            int hits = 0;
            r.OnEvent = e => { if (e == "hit") hits++; };
            for (int i = 0; i < 60 * 150 && !r.Over; i++)
            {
                if (good) Bot(r);
                r.Update(1 / 60.0);
                if (i % 30 != 0) continue;
                foreach (var t in r.Things)
                {
                    bool ok = t.Kind switch
                    {
                        ThingKind.Fly => t.Y == r.Ground - r.CloudUp && t.Scale == r.Px(2),
                        ThingKind.Block => t.Y == r.Ground && t.H <= r.TallMax && (r.TallBlocks.Contains(t.Art) || r.NormalBlocks.Contains(t.Art)),
                        ThingKind.Heart => t.Y == r.Ground - 2 * r.StandPx,
                        _ => t.Scale == r.Px(1) && t.Y <= r.Ground,
                    };
                    Expect(ok, $"길에 나온 것은 몸 크기 규칙을 따른다: {t.Kind} {t.Sprite}×{t.Scale} 높이 {t.H} (선 키 {r.StandPx}, 배율 {scale})");
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
        int combos = 0, jumps = 0;
        double worstSec = 999, worstHits = 0;
        foreach (var form in forms)
        {
            var body = BodyOf(form);
            // 그림과 맞는지: 서 있는 그림(귀 포함)은 선 키보다 낮지 않고, 엎드린 그림은 그만큼 낮다
            int Top(string pose) => sp.Cell - Props.Trim(Props.Solid(sp.Get($"{form}/0/{pose}", 0, 1))).Y0;
            int idleTop = Top("idle"), landTop = Top("land");
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

                // 잘하는 사람은 어느 몸·어느 배율에서든 버틴다 (가장 쉬운 레벨과 가장 어려운 레벨)
                foreach (int level in new[] { 1, 12 })
                {
                    var runs = Enumerable.Range(0, 4).Select(seed => Play(level, 20 + seed, true, body, scale)).ToList();
                    double sec = runs.Average(x => x.Sec), hit = runs.Average(x => x.Hits);
                    (worstSec, worstHits) = (Math.Min(worstSec, sec), Math.Max(worstHits, hit));
                    Expect(sec > 35 && hit <= 1.5, $"{form}×{scale} Lv{level}: 잘하면 버틴다 ({sec:F0}초, 부딪힘 {hit:F1})");
                }
            }
        }
        return $"달리기 잘하는 사람 ({string.Join(" / ", rows)}), 가만히 {lazy.Sec:F0}초\n"
            + $"달리기 몸 크기별: {forms.Count}형태 × 배율 {string.Join("·", Scales)} = {combos}조합 — 서면 구름에 맞고 엎드리면 지나감, 장애물 {jumps / 4}개를 두 속도에서 맞춰 뛰면 넘음, 잘하는 사람 가장 짧게 {worstSec:F0}초·가장 많이 부딪힘 {worstHits:F1}\n"
            + "  배율 3 (화면 px):\n" + string.Join("\n", table);
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
        var strip = new Rectangle(area.Left, TestGround - 330, area.Width, 370);
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
        return "달리기 모듈: Ctrl·Shift 만 읽음, 보스 키는 점수 없이 바로, 끝난 점수 2.5초 동안 녹화 숨김 유지, 옛 기록 넘어옴, 바뀐 자리만 다시 칠해도 화면이 맞음";
    }

    // 달리기 한 프레임 값: 규칙(Update)·다시 칠할 자리(Sync)·그리기(Render)를 따로 재고, 프레임마다 새로 잡는 메모리도 센다.
    // 화면 없이 재는 것이라 창에 실제로 칠하는 값은 빠져 있다. 2026-11-13 에 고치기 전 → 뒤 (같은 재기, Debug 빌드, 길 위 12개):
    //   달릴 때  규칙 3.2µs 72B → 1.0µs 6B, 다시 칠할 자리 47µs 3201B → 7µs 0B, 그리기 177µs 1562B → 63µs 1B
    //   안내 4초  규칙 3.0µs 73B → 0.7µs 7B, 다시 칠할 자리 15µs 3239B → 7µs 4B, 그리기 500µs 1678B → 57µs 2B
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
        return Bench.Run("run", ov, game, r.Track, () => { if (r.Over) return false; Bot(r); return true; }, () => { r.Update(1 / 60.0); game.Track(); });
    }

    // 장면 그려 보기 (--mode-scene run[@배율] <그림.png>): 형태마다 한 줄. 왼쪽부터 서서 구름에 맞음 · 레벨 별 · 엎드려 구름 밑 ·
    // 보통 장애물과 키 큰 장애물(가장 큰 것) · 씨앗 무지개와 하트 · 끝난 점수. 빨간 테두리는 볼록이 판정, 파란 테두리는 부딪히는 자리.
    // 형태 하나만 자세히 (GameModes.SceneArg, 옛 --run-scene <그림.png> <배율> <형태>): 두 배로 키워 왼쪽·오른쪽 두 장으로 (<이름>-1.png, <이름>-2.png)
    public static void Scene(string path, int scale)
    {
        string? only = GameModes.SceneArg;
        var forms = Forms().Where(f => only == null || f == only).ToList();
        double u = scale / 3.0;
        int width = (int)(1920 * u), rowH = (int)(330 * u);
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
            using var ov = new Overlay(brain, () => sp, () => null, track) { Modes = () => modes };
            double X(double at) => at * u;
            void PetAt(double x, bool down)
            {
                g.DrawImageUnscaled(sp.Get($"{form}/0/{(down ? "land" : "walk")}", 0, scale), (int)(x - cell / 2.0), ground - cell);
                int h = down ? run.DuckPx : run.StandPx;
                g.DrawRectangle(red, (int)(x - run.BoxW / 2.0), ground - h, run.BoxW - 1, h - 1);
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

            PetAt(X(150), false);
            PetAt(run.PetX, false);
            PetAt(X(700), true);
            PetAt(X(930), false);
            PetAt(X(1300), false);
            ov.Render(g);
            foreach (var t in new[] { hit, pass, low, high }) Outline(t);

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
}
