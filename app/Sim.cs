namespace Bolok;

// 일주일 시뮬레이션과 자가 검사: 9시 출근, 12시 점심 1시간 자리 비움, 18시 퇴근.
// preview/model.js 의 SCENARIOS 와 같은 표가 나와야 한다
static class Sim
{
    sealed record Result(Pet P, double MinFull, double MinMood, double? FirstBite);

    static int H(int a, int m = 0) => a * 60 + m;
    static readonly int[] Three = { H(9, 30), H(13, 30), H(16) };

    static Result Week(Tuning k, int[] careAt, int feed, int pet, int play)
    {
        var p = new Pet();
        double minFull = 100, minMood = 100, active = 0;
        double? first = null;
        for (int day = 0; day < 5; day++)
        {
            for (int t = H(9); t < H(18); t++)
            {
                if (careAt.Contains(t))
                {
                    for (int i = 0; i < feed; i++) p.Act(k, "feed");
                    for (int i = 0; i < pet; i++) p.Act(k, "pet");
                    for (int i = 0; i < play; i++) p.Act(k, "play");
                }
                bool lunch = t >= H(12) && t < H(13);
                var ev = p.Tick(k, 1, lunch);
                if (!lunch) active++;
                if (ev.Contains("bite") && first == null) first = active;
                if (p.Stage != "egg") { minFull = Math.Min(minFull, p.Full); minMood = Math.Min(minMood, p.Mood); }
            }
            p.Tick(k, 15 * 60, true); // 퇴근 후 꺼져 있던 시간은 켤 때 "잔 것"으로 친다
        }
        return new Result(p, minFull, minMood, first);
    }

    static string Row(string name, Result r)
    {
        string first = r.FirstBite is { } f ? (f / 60).ToString("F1") + "시간" : "-";
        return $"{name.PadRight(8)} {r.P.Stage.PadRight(13)} 실수 {r.P.Miss} 배고팠던 비율 {r.P.HungryShare:P0} 누적 {r.P.BitesTotal}입 남은 {r.P.Bites}입 " +
               $"최저 포만 {r.MinFull:F0} 기분 {r.MinMood:F0} 첫 입 {first}";
    }

    static (Brain, double Floor) Flight(Tuning k, double vx, double vy, bool hard = false)
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        var b = new Brain(new Pet { Stage = "child", Mood = 50 }, k, area) { X = 900, Y = 500 };
        b.Throw(vx, vy, hard);
        for (int i = 0; i < 3000 && b.Mode == Mode.Thrown; i++) b.Update(1 / 60.0, false);
        return (b, area.Bottom - k.FloorGap);
    }

    static void HideChecks(Tuning k)
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        var rng = new Random(1);
        const int cell = 120;
        var a = new IntPtr(1);
        var mid = new Rectangle(400, 300, 900, 600);

        var one = new[] { area };
        var edges = HideSpots.Find(new[] { new Win(a, mid, false) }, one, cell, .64, rng).Select(p => p.E).ToList();
        Expect(edges.Count == 3, "창이 화면 가운데면 왼쪽·오른쪽·위 다 숨을 수 있다");
        Expect(HideSpots.Find(new[] { new Win(a, area, false) }, one, cell, .64, rng).Count == 0, "최대화한 창은 가장자리가 없다");
        Expect(HideSpots.Find(new[] { new Win(a, mid, true) }, one, cell, .64, rng).Count == 0, "항상 위 창 뒤로는 못 숨는다");
        Expect(HideSpots.Find(new[] { new Win(new IntPtr(2), area, false), new Win(a, mid, false) }, one, cell, .64, rng).Count == 0, "더 위의 창에 덮인 자리는 안 쓴다");
        var corner = new Rectangle(0, 0, 900, 600);
        Expect(HideSpots.Find(new[] { new Win(a, corner, false) }, one, cell, .64, rng).All(p => p.E == Edge.Right), "화면에 붙은 쪽 가장자리는 못 쓴다");

        foreach (var roll in Enumerable.Range(0, 100).Select(i => i / 100.0))
        {
            Expect(HideSpots.Reaction("moved", "adult_grumpy", 80, roll) != "shy", "심술이는 부끄러워하지 않는다");
            Expect(HideSpots.Reaction("closed", "child", 80, roll) != "chase", "닫힌 창은 따라갈 수 없다");
        }

        // 숨었다가 창을 치우면 앞으로 나온다
        var rect = mid;
        var b = new Brain(new Pet { Stage = "child", Mood = 90, Full = 90, Energy = 90 }, k, area)
        {
            Windows = () => new List<Win> { new(a, rect, false) },
            Probe = _ => new WinState(true, false, rect),
        };
        Expect(b.TryHide(), "숨을 창을 찾는다");
        for (int i = 0; i < 1200 && b.Mode != Mode.Hiding; i++) b.Update(1 / 60.0, false);
        Expect(b.Mode == Mode.Hiding && b.Behind && b.HideWnd == a, "창 뒤로 들어간다");
        var near = Math.Min(Math.Abs(b.X - rect.Left), Math.Abs(b.X - rect.Right));
        Expect(near < 8 || Math.Abs(b.Y - (rect.Top + cell * .36)) < 8, "가장자리에서 내다본다");
        rect.Offset(300, 200);
        for (int i = 0; i < 60 && b.Behind; i++) b.Update(1 / 60.0, false);
        Expect(!b.Behind && b.Mode != Mode.Hiding, "창을 치우면 앞으로 나온다");
        Expect(b.P.Mood > 90, "찾아주면 기분이 좋아진다");
    }

    static void EmoteChecks(Tuning k)
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        var b = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 90 }, k, area) { X = 900, Y = 800 };
        b.Throw(1200, -800);
        Expect(b.Emoting && b.EmoteName == "bang", "던지면 머리 위에 그림이 뜬다");
        for (int i = 0; i < 120; i++) b.Update(1 / 60.0, false);
        Expect(!b.Emoting, "그림은 금방 사라진다");

        var quiet = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 90 }, k, area) { X = 900, Y = 800 };
        for (int i = 0; i < 60 * 60; i++) { quiet.Update(1 / 60.0, false); Expect(!quiet.Emoting, "아무 일 없으면 조용하다"); }

        var hungry = new Brain(new Pet { Stage = "child", Full = 10, Mood = 90, Energy = 90 }, k, area) { X = 900, Y = 800 };
        int calls = 0;
        bool was = false;
        for (int i = 0; i < 60 * 120; i++)
        {
            hungry.Update(1 / 60.0, false);
            if (hungry.Emoting && !was) { calls++; Expect(hungry.EmoteName == "seed", "배고프면 씨앗 그림을 띄운다"); }
            was = hungry.Emoting;
        }
        Expect(calls is >= 3 and <= 5, $"배고프면 30초쯤마다 알린다: 2분에 {calls}번");

        // 코드에서 쓰는 그림이 스프라이트에 다 있어야 한다
        var sp = Sprites.Embedded();
        foreach (var name in new[] { "note", "bang", "no", "star", "tear", "sweat", "zzz", "spark", "dots", "vein", "heart", "seed", "ask" })
            Expect(sp.Get("emo/" + name, 0, 3).GetPixel(15 * 3 + 1, 14 * 3 + 1).ToArgb() == Color.White.ToArgb(), "그림이 흰 말풍선 안에 있다: " + name);
    }

    static void ClickChecks(Tuning k)
    {
        var rng = new Random(3);
        Expect(Clicks.Zone("child", 20, 18) == "head", "위를 누르면 머리");
        Expect(Clicks.Zone("child", 20, 28) == "belly", "가운데는 배");
        Expect(Clicks.Zone("child", 12, 26) == "cheek" && Clicks.Zone("child", 29, 26) == "cheek", "옆은 볼");
        Expect(Clicks.Zone("child", 20, 36) == "feet", "아래는 발");

        var seen = new HashSet<string>();
        string? last = null;
        foreach (var zone in new[] { "head", "cheek", "belly", "feet" })
            for (int i = 0; i < 100; i++)
            {
                string kind = Clicks.Pick(zone, 1, "child", 90, last, rng);
                Expect(kind != last, "같은 반응이 연달아 나오지 않는다");
                seen.Add(kind);
                last = kind;
            }
        Expect(seen.Count >= 7, "클릭 반응이 여러 가지다: " + string.Join(",", seen));
        Expect(Enumerable.Range(0, 200).All(_ => Clicks.Pick("cheek", 1, "child", 90, null, rng) != "flop"), "볼을 눌렀는데 드러눕지 않는다");
        Expect(Enumerable.Range(0, 200).All(_ => Clicks.Pick("head", 1, "adult_grumpy", 90, null, rng) != "love"), "심술이는 하트 눈을 안 한다");
        Expect(Enumerable.Range(0, 200).All(_ => Clicks.Pick("head", 9, "child", 90, null, rng) is "pout" or "nope"), "너무 연타하면 삐진다");
        Expect(Enumerable.Range(0, 200).All(_ => Clicks.Pick("head", 5, "adult_grumpy", 90, null, rng) is "pout" or "nope" or "nip"), "심술이는 더 빨리 삐진다");
        Expect(Enumerable.Range(0, 200).All(_ => Clicks.Pick("feet", 1, "child", 10, null, rng) is not ("hop" or "spin")), "기분이 나쁘면 폴짝거리지 않는다");

        var area = new Rectangle(0, 0, 1920, 1080);
        var b = new Brain(new Pet { Stage = "child", Mood = 50, Full = 90, Energy = 90 }, k, area) { X = 900, Y = 800 };
        b.Click(20, 18);
        Expect(b.Mode == Mode.Anim && b.P.Pets == 1, "클릭하면 반응하고 쓰다듬기로 친다");
        var mood = b.P.Mood;
        for (int i = 0; i < 12; i++) { b.Update(.05, false); b.Click(20, 26); }
        Expect(b.P.Pets <= 2 && b.P.Mood <= mood + k.PetMood + .1, "연타해도 수치는 1.5초에 한 번만 오른다");

        // 눌러도 제자리에 있다. 움직이면 다음 클릭이 뒤 창으로 간다
        foreach (var stage in new[] { "child", "adult_grumpy", "adult_sweet" })
            for (int n = 0; n < 15; n++)
            {
                var still = new Brain(new Pet { Stage = stage, Mood = 90, Full = 90, Energy = 90 }, k, area) { X = 900, Y = 800 };
                for (int i = 0; i < 30; i++)
                {
                    still.Click(rng.Next(10, 30), rng.Next(16, 37));
                    for (int f = 0; f < 6; f++) still.Update(1 / 60.0, false);
                    Expect(still.X == 900 && still.Y == 800, $"연타해도 안 움직인다: {stage} {still.Mode}");
                }
                for (int f = 0; f < 150; f++) { still.Update(1 / 60.0, false); Expect(still.X == 900 && still.Y == 800, "누른 뒤 잠시 제자리에 있는다"); }
            }

        var over = new Brain(new Pet { Stage = "child", Mood = 90, Full = 90, Energy = 90 }, k, area) { X = 900, Y = 800 };
        over.Foreground = () => new Win(new IntPtr(5), new Rectangle(600, 500, 800, 400), false);
        over.Update(1 / 60.0, false);
        over.Click(20, 26);
        var (ox, oy) = (over.X, over.Y);
        for (int i = 0; i < 150; i++) over.Update(1 / 60.0, false);
        Expect(over.X == ox && over.Y == oy, "활성 창 위여도 누른 직후에는 안 비킨다");
        for (int i = 0; i < 240; i++) over.Update(1 / 60.0, false);
        Expect(over.X != ox || over.Y != oy, "시간이 지나면 활성 창에서 비킨다");

        var hid = new Rectangle(400, 300, 900, 600);
        var peek = new Brain(new Pet { Stage = "child", Mood = 90, Full = 90, Energy = 90 }, k, area)
        {
            Windows = () => new List<Win> { new(new IntPtr(1), hid, false) },
            Probe = _ => new WinState(true, false, hid),
        };
        for (int n = 0; n < 12; n++)
        {
            peek.Reset();
            peek.TryHide();
            for (int i = 0; i < 1500 && peek.Mode != Mode.Hiding; i++) peek.Update(1 / 60.0, false);
            peek.Click(20, 26);
            var (hx, hy) = (peek.X, peek.Y);
            for (int i = 0; i < 150; i++) peek.Update(1 / 60.0, false);
            Expect(peek.X == hx && peek.Y == hy, "숨어 있다 눌려도 달아나지 않는다");
        }

        var egg = new Brain(new Pet(), k, area);
        egg.Click(20, 26);
        Expect(egg.P.Pets == 0, "알은 쓰다듬기가 안 된다");
    }

    static string RunnerChecks(Tuning k)
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        var sp = Sprites.Embedded();
        (int, int, int, int) Measure(string name, int scale)
        {
            var (x0, y0, x1, y1) = Props.Trim(Props.Solid(sp.Get(name, 0, 1)));
            return ((x1 - x0 + 1) * scale, (y1 - y0 + 1) * scale, x0 * scale, (y1 + 1) * scale);
        }
        Runner Make() { var r = new Runner(area, 1030, 120, new Random(1), Measure); r.Things.Clear(); return r; }
        void Run(Runner r, double sec, Action<Runner, double>? each = null) { for (double t = 0; t < sec; t += 1 / 60.0) { each?.Invoke(r, t); r.Update(1 / 60.0); } }

        // 가만히 있으면 땅 장애물에 부딪히고, 맞춰 점프하면 넘는다
        var stand = Make();
        stand.Add("block", "prop/mushroom", 2, stand.PetX + 300, stand.Ground);
        Run(stand, 1.5);
        Expect(stand.Energy < 100 - Runner.HitCostOf(1) + 1, $"가만히 있으면 부딪힌다: 체력 {stand.Energy:F0}");
        var hop = Make();
        var block = hop.Add("block", "prop/mushroom", 2, hop.PetX + 300, hop.Ground);
        bool jumped = false;
        Run(hop, 1.5, (r, t) => { if (!jumped && block.X - (r.PetX + r.PetBox.Width / 2) < 50) { r.Jump(); jumped = true; } }); // 바로 앞에서 뛴다
        var early = Make();
        var far = early.Add("block", "prop/mushroom", 2, early.PetX + 300, early.Ground);
        bool jumpedEarly = false;
        Run(early, 1.5, (r, t) => { if (!jumpedEarly && far.X - (r.PetX + r.PetBox.Width / 2) < 120) { r.Jump(); jumpedEarly = true; } });
        Expect(early.Energy > 95, $"조금 일찍 뛰어도 넘는다 (타이밍 여유): 체력 {early.Energy:F0}");
        Expect(jumped && hop.Energy > 95, $"맞춰 점프하면 넘는다: 체력 {hop.Energy:F0}");

        // 공중 장애물은 슬라이드로 밑을 지난다
        var duck = Make();
        duck.Add("fly", "prop/cloud", 2, duck.PetX + 300, duck.Ground - 58);
        Run(duck, 1.5, (r, t) => r.Slide(true));
        Expect(duck.Energy > 95, $"슬라이드하면 공중 장애물 밑을 지난다: 체력 {duck.Energy:F0}");
        var tall = Make();
        tall.Add("fly", "prop/cloud", 2, tall.PetX + 300, tall.Ground - 58);
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
        for (int i = 0; i < 4; i++) eat.Add("seed", "icon/seed", 1, eat.PetX + 100 + i * 34, eat.Ground - 18);
        Run(eat, 1.5);
        Expect(eat.Seeds == 4 && eat.Score >= 40, $"씨앗을 먹으면 점수: {eat.Seeds}개 {eat.Score}점");
        var safe = Make();
        safe.Add("block", "prop/mushroom", 2, safe.PetX + 200, safe.Ground);
        safe.Add("block", "prop/acorn", 2, safe.PetX + 320, safe.Ground);
        Run(safe, 1.5);
        Expect(safe.Energy > 100 - Runner.HitCostOf(1) * 2, "부딪힌 뒤 잠깐은 안 다친다");
        var end = new Runner(area, 1030, 120, new Random(3), Measure);
        Run(end, 70, (r, t) => { if (r.Things.Any(x => x.Kind == "block" && x.X - (r.PetX + 30) is > 0 and < 60) && r.Lift == 0) r.Jump(); r.Slide(r.Things.Any(x => x.Kind == "fly" && x.X - r.PetX is > -80 and < 200)); });
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
        Expect(new Runner(area, 1030, 120, new Random(1), Measure, 5).Speed == Runner.StartSpeedOf(5), "저장된 레벨에서 시작한다");

        // 잘하는 사람: 장애물 한가운데서 가장 높이 뜨게 뛰고, 구름이 오면 엎드리고, 구름이 머리 위에 있으면 기다린다
        (double Sec, int Score, int Hits, int Level) Play(int level, int seed, bool good)
        {
            var r = new Runner(area, 1030, 120, new Random(seed), Measure, level);
            int hits = 0;
            r.OnEvent = e => { if (e == "hit") hits++; };
            for (int i = 0; i < 60 * 150 && !r.Over; i++)
            {
                if (good)
                {
                    var me = r.PetBox;
                    bool cloud = r.Things.Any(x => x.Kind == "fly" && x.X < me.Right + r.Speed * .1 && x.X + x.W > me.Left);
                    r.Slide(cloud);
                    // 사이에 내려설 틈이 없는 장애물들은 한 번에 넘는다
                    var blocks = r.Things.Where(x => x.Kind == "block" && x.X + x.W > me.Left).OrderBy(x => x.X).ToList();
                    if (!cloud && r.Lift == 0 && blocks.Count > 0)
                    {
                        double from = blocks[0].X, to = from + blocks[0].W;
                        foreach (var nb in blocks.Skip(1)) { if (nb.X - to < r.Speed * .45) to = nb.X + nb.W; else break; }
                        if (from - me.Right <= r.Speed * .395 - (to - from + me.Width) / 2.0) r.Jump();
                    }
                }
                r.Update(1 / 60.0);
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
        return $"달리기 잘하는 사람 ({string.Join(" / ", rows)}), 가만히 {lazy.Sec:F0}초";
    }

    // 다국어: 모든 문장에 영어·한국어가 있고, 번역된 문장은 {0} 같은 자리표시자가 영어와 같다. 언어 바꾸기·조사
    static void LangChecks()
    {
        foreach (var key in L.Keys)
        {
            Expect(L.Has(key, "en") && L.Has(key, "ko"), $"영어·한국어가 다 있다: {key}");
            var want = System.Text.RegularExpressions.Regex.Matches(L.Raw(key, "en"), @"\{\d\}").Select(m => m.Value).OrderBy(x => x).ToList();
            foreach (var (code, _) in L.Languages.Where(l => L.Has(key, l.Code)))
            {
                var got = System.Text.RegularExpressions.Regex.Matches(L.Raw(key, code), @"\{\d\}").Select(m => m.Value).OrderBy(x => x).ToList();
                Expect(got.SequenceEqual(want), $"자리표시자가 영어와 같다: {key} ({code})");
            }
        }
        foreach (var sp in Sprites.Embedded().SpeciesList) Expect(L.Has("sp." + sp, "en"), $"종 이름: {sp}");
        var was = L.Code;
        L.Use("ko");
        Expect(L.T("menu.feed") == "밥 주기" && L.With("여우", "를", "을") == "여우를" && L.With("곰", "를", "을") == "곰을", "한국어와 조사");
        L.Use("en");
        Expect(L.T("menu.feed") == "Feed" && L.With("Fox", "를", "을") == "Fox" && L.T("no.such.key") == "no.such.key", "영어, 조사 없음, 없는 키는 키 그대로");
        Expect(L.Span(3) == "3 min" && L.Span(125) == "2 h 5 min" && L.Span(60 * 27) == "1 d 3 h", "시간 쓰기");
        L.Use("fr");
        Expect(L.T("menu.feed").Length > 0, "번역이 없으면 영어로");
        L.Use(was);
    }

    // 추억: 사진·엽서가 PNG 로 남고, 다시 읽힌다
    static void MemoryChecks()
    {
        var sp = Sprites.Embedded();
        var dir = Path.Combine(Path.GetTempPath(), "bolok-check-mem-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Memories.Photo(sp, "child/0/happy", "child", Path.Combine(dir, "a.png"));
            Memories.Card(sp, sp, "hamster", "adult_chubby", 3, Path.Combine(dir, "c.png"));
            using var a = Memories.Load(Path.Combine(dir, "a.png"));
            using var c = Memories.Load(Path.Combine(dir, "c.png"));
            Expect(a is { Width: Memories.PhotoSize } && c is { Width: Memories.CardW, Height: Memories.CardH }, "사진·엽서 파일");
            Expect(Memories.Load(Path.Combine(dir, "none.png")) == null, "없는 파일은 null");
            foreach (var k in Memories.Kinds) Expect(sp.Has("item/" + k), $"선물 그림: {k}");
            Expect(sp.Has("emo/sun") && sp.Has("emo/moon") && sp.Has("emo/mail") && sp.Has("game/nightcap"), "아침·밤·엽서·수면 모자 그림");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    // 떠난 친구가 놀러 온다: 걸어 들어와 인사, 따라다니기, 같이 자기, 바쁠 땐 기다리기, 5분 뒤 돌아가기, 누가 올지 고르기
    static void VisitorChecks()
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        double ground = 1030, cell = 120;
        var v = new Visitor("fox", "adult_sweet", -cell, ground, 3, new Random(1));
        var ev = new List<string>();
        v.OnEvent = ev.Add;
        void Run(double sec, double mx, bool free = true, bool asleep = false) { for (int i = 0; i < sec * 60; i++) v.Update(1 / 60.0, mx, ground, free, asleep, area); }
        Run(15, 900);
        Expect(v.Stage == Visitor.Phase.Stay && ev.Contains("hello") && Math.Abs(Math.Abs(v.X - 900) - Visitor.Side * cell) < 2, $"걸어 들어와 볼록이 옆에서 인사: {v.Stage} x {v.X:F0}");
        Run(20, 1400);
        Expect(Math.Abs(v.X - 1400) < Visitor.Side * cell + cell * .6, $"볼록이를 따라다닌다 (가끔 반대쪽으로 자리를 바꾼다): x {v.X:F0}");
        // 볼록이가 조금씩 움직여도 걷기·서기, 방향이 매 프레임 바뀌지 않는다 (파르르 떨림)
        int flips = 0;
        string lastPose = v.Pose;
        int lastDir = v.Dir;
        for (int i = 0; i < 600; i++)
        {
            v.Update(1 / 60.0, 1400 + Math.Sin(i / 20.0) * 30, ground - Math.Sin(i / 15.0) * 10, true, false, area);
            if (v.Pose != lastPose || v.Dir != lastDir) flips++;
            (lastPose, lastDir) = (v.Pose, v.Dir);
        }
        Expect(flips < 20, $"조금씩 움직이는 볼록이 옆에서 떨지 않는다: 10초에 {flips}번 바뀜");
        double still = v.X;
        Run(5, 300, free: false);
        Expect(Math.Abs(v.X - still) < 1, "볼록이가 바쁘면(게임·잡힘) 제자리에서 기다린다");
        Run(15, 1400, asleep: true);
        Expect(v.Pose == "sleep" && ev.Contains("sleep") && Math.Abs(v.X - 1400) < cell * 1.1, $"볼록이가 자면 옆에서 같이 잔다: {v.Pose}");
        Run(2, 1400);
        Expect(v.Pose != "sleep", "볼록이가 깨면 같이 일어난다");
        Run(Visitor.StaySec, 1400);
        Run(30, 1400);
        Expect(v.Gone && ev.Contains("bye") && ev.Contains("gone") && (v.X < area.Left || v.X > area.Right), $"5분 뒤 인사하고 화면 밖으로 돌아간다: x {v.X:F0}");

        var rel = new List<ReleasedPet>
        {
            new("hamster", "adult_chubby", 100, new DateTime(2026, 11, 1)), new("fox", "child", 50, new DateTime(2026, 11, 2)),
            new("cat", "adult_sweet", 80, new DateTime(2026, 11, 3)), new("fox", "adult_grumpy", 90, new DateTime(2026, 11, 4)),
        };
        var seen = new Dictionary<string, DateTime> { ["hamster"] = new(2026, 11, 10), ["cat"] = new(2026, 11, 11) };
        var pick = Visitor.Pick(rel, seen, "beaver", new Random(1));
        Expect(pick?.Species == "fox" && pick.Stage == "adult_grumpy", $"가장 오래 안 온 친구, 같은 종은 마지막 모습으로: {pick?.Species} {pick?.Stage}");
        Expect(Visitor.Pick(rel, seen, "fox", new Random(1))?.Species == "hamster", "지금 키우는 종과 같은 친구는 뒤로");
        Expect(Visitor.Pick(new List<ReleasedPet>(), seen, "fox", new Random(1)) == null, "떠난 친구가 없으면 아무도 안 온다");
    }

    // 캐릭터 그림: 모든 종을 데이터로 그린다 (그림 파일을 받지 않는다)
    static void PetSheetChecks()
    {
        var art = PetArt.Embedded;
        Expect(art.Species.Count == 46 && art.Species.All(art.Can), $"46종 모두 데이터로 그린다 ({art.Species.Count})");
        foreach (var id in new[] { "fox", "octopus", "ghost" })
            Expect(Sprites.Drawn(art, id, null) is { } sp && sp.Species == id && sp.Has("adult_grumpy/3/fix") && sp.Has("thumb/" + id), $"그린 그림에 모든 줄이 있다: {id}");
        Expect(Sprites.Embedded("no-such-pet").Species == Sprites.DefaultSpecies, "모르는 종이면 햄스터");
        Expect(Sprites.Drawn(art, "fox#2", null) is { } fx2 && fx2.Species == "fox#2" && fx2.BaseSpecies == "fox", "색 변형 그림의 종은 그대로 여우");
        var old = System.Text.Json.JsonSerializer.Deserialize<ReleasedPet>("{\"Species\":\"cat\",\"Stage\":\"child\",\"AgeMin\":1,\"At\":\"2026-11-11T00:00:00\"}");
        Expect(old is { Species: "cat", Variant: 0 }, "예전 떠난 친구 기록(색 없음)은 기본 색");
    }

    static void UpdaterChecks()
    {
        string Json(string tag, string name = "Bolok.exe", string extra = "", string owner = "goldfinger8157/bolok-desktop-pet") =>
            $$"""{"tag_name":"{{tag}}","draft":false,"prerelease":false{{extra}},"assets":[{"name":"notes.txt","size":5,"browser_download_url":"https://github.com/{{owner}}/releases/download/{{tag}}/notes.txt"},{"name":"{{name}}","size":48000000,"digest":"sha256:ABCDEF","browser_download_url":"https://github.com/{{owner}}/releases/download/{{tag}}/{{name}}"}]}""";
        var now = new Version(1, 9, 3);
        var r = Updater.Newer(Json("v1.10.0"), now);
        Expect(r is { Tag: "v1.10.0", Size: 48000000, Sha256: "ABCDEF" } && r.Version == new Version(1, 10, 0) && r.Url.EndsWith("/v1.10.0/Bolok.exe"), "새 버전이면 exe 주소·크기·해시를 찾는다 (1.10 > 1.9)");
        Expect(Updater.Newer(Json("v1.9.3"), now) == null && Updater.Newer(Json("v1.2.0"), now) == null, "같거나 낮은 버전은 안 받는다");
        Expect(Updater.Newer(Json("v2.0.0", extra: ",\"prerelease\":true").Replace("\"prerelease\":false,", ""), now) == null, "시험판은 안 받는다");
        Expect(Updater.Newer(Json("v2.0.0", name: "Other.exe"), now) == null, "Bolok.exe 가 없는 릴리스는 넘긴다");
        Expect(Updater.Newer(Json("v2.0.0", owner: "someone/else"), now) == null, "다른 저장소 주소는 안 받는다");
        Expect(Updater.Newer(Json("nightly"), now) == null && Updater.ParseTag("v1.2") == new Version(1, 2, 0), "버전이 아닌 태그는 넘긴다");

        // 바꿔치기: 지금 것은 .old 로, 받은 것이 제자리에. 시작할 때 .old 를 지운다. 크기·해시가 다르면 안 쓴다
        string dir = Path.Combine(Path.GetTempPath(), "bolok-update-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string exe = Path.Combine(dir, "Bolok.exe"), fresh = exe + ".new";
            File.WriteAllText(exe, "old");
            var bytes = new byte[1_200_000];
            File.WriteAllBytes(fresh, bytes);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            var rel = new Updater.Release(new Version(2, 0, 0), "v2.0.0", "", bytes.Length, hash);
            Expect(Updater.Verify(fresh, rel) && !Updater.Verify(fresh, rel with { Size = 5 }) && !Updater.Verify(fresh, rel with { Sha256 = "00" }), "받은 파일의 크기·해시를 확인한다");
            Expect(Updater.Swap(exe, fresh) && new FileInfo(exe).Length == bytes.Length && File.ReadAllText(exe + ".old") == "old" && !File.Exists(fresh), "바꿔치기: 새 것이 제자리, 옛것은 .old");
            Updater.Cleanup(exe);
            Expect(!File.Exists(exe + ".old") && File.Exists(exe), "다음 시작 때 .old 를 지운다");
            Expect(!Updater.Swap(exe, Path.Combine(dir, "missing.new")) && new FileInfo(exe).Length == bytes.Length, "받은 파일이 없으면 원래 것을 그대로 둔다");
        }
        finally { Directory.Delete(dir, true); }
    }

    static string IcbmChecks()
    {
        var screens = new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(1920, -271, 1080, 1799) };
        var active = new Rectangle(300, 100, 1200, 800);
        // 떨어질 자리: 폭발이 활성 창에 닿지 않고, 볼록이 위도 아니다. 화면이 활성 창으로 꽉 차면 없다
        var rng = new Random(1);
        for (int i = 0; i < 500; i++)
        {
            var t = Icbm.Pick(screens, active, 900, 1000, Icbm.CraterR, rng)!.Value;
            var blast = Rectangle.Inflate(new Rectangle((int)t.X, (int)t.Y, 0, 0), (int)Icbm.CraterR - 1, (int)Icbm.CraterR - 1);
            Expect(!blast.IntersectsWith(active) && screens.Any(sc => sc.Contains(blast)), $"활성 창에는 안 떨어진다 ({t.X:F0},{t.Y:F0})");
        }
        // 버섯구름이 화면 위·옆에서 잘리지 않을 만큼 떨어져서 터진다 (실제 한 발씩 쏴서 본다)
        for (int seed = 0; seed < 40; seed++)
        {
            var shot = new Icbm(screens, 900, 1000, 3, new Random(seed));
            shot.Launch();
            for (int i = 0; i < 60 * 20 && shot.Craters.Count == 0; i++) shot.Update(1 / 60.0, active);
            var cr = shot.Craters[0];
            var cloud = Rectangle.FromLTRB((int)cr.X - Icbm.CloudHalf * 3, (int)cr.Y - Icbm.CloudUp * 3, (int)cr.X + Icbm.CloudHalf * 3, (int)cr.Y);
            Expect(screens.Any(sc => sc.Contains(cloud)), $"버섯구름이 화면에서 안 잘린다 (seed {seed}: {cr.X:F0},{cr.Y:F0})");
        }
        Expect(Icbm.Pick(new[] { screens[0] }, screens[0], 900, 1000, Icbm.CraterR, rng) == null, "화면이 활성 창으로 꽉 차면 떨어질 데가 없다");

        // 한 발: 버튼 → 솟음 → 화면 밖 → 떨어짐 → 터짐 → 달려가 고침 → 끝
        var ic = new Icbm(screens, 900, 1000, 3, new Random(2));
        var seen = new List<Icbm.Phase>();
        var events = new List<string>();
        ic.OnEvent = events.Add;
        Expect(ic.Launch(), "발사 명령을 받는다");
        PointF hit = default;
        for (int i = 0; i < 60 * 120 && !ic.Finished; i++)
        {
            ic.Update(1 / 60.0, active);
            if (seen.Count == 0 || seen[^1] != ic.Stage) seen.Add(ic.Stage);
            if (ic.Craters.Count == 1 && hit == default) hit = new PointF((float)ic.Craters[0].X, (float)ic.Craters[0].Y);
        }
        Expect(seen.SequenceEqual(new[] { Icbm.Phase.ToButton, Icbm.Phase.Press, Icbm.Phase.Rise, Icbm.Phase.Away, Icbm.Phase.Fall, Icbm.Phase.Boom, Icbm.Phase.ToCrater, Icbm.Phase.Repair, Icbm.Phase.Done }),
            "차례: " + string.Join(">", seen));
        events.Remove("blown");
        Expect(string.Join(",", events) == "press,launch,fall,boom,repaired" && ic.Finished && ic.Craters.Count == 0, "터지고 나면 고쳐서 구멍이 사라진다: " + string.Join(",", events));
        Expect(Math.Abs(ic.X - hit.X) < 3 && !active.Contains((int)hit.X, (int)hit.Y), "볼록이가 터진 자리까지 가서 고친다");

        // 떨어지는 건 콰콰콰콰~ 천천히: 1.6~2.6초
        var slow = new Icbm(screens, 900, 1000, 3, new Random(4));
        slow.Launch();
        double fallFrom = -1, fallTo = -1;
        for (int i = 0; i < 60 * 30 && fallTo < 0; i++)
        {
            slow.Update(1 / 60.0, active);
            if (slow.Stage == Icbm.Phase.Fall && fallFrom < 0) fallFrom = slow.Clock;
            if (fallFrom >= 0 && slow.Stage != Icbm.Phase.Fall) fallTo = slow.Clock;
        }
        Expect(fallTo - fallFrom is >= Icbm.FallMinSec - .05 and <= Icbm.FallMaxSec + .1, $"천천히 떨어진다: {fallTo - fallFrom:F2}초");

        // 떠난 친구 구멍: 터질 때 정해지고(자연이 바로 보인다), 그 구멍에서만 친구가 빼꼼. 고치는 구멍에서는 쏙, 친구가 없으면 다 검은 구멍
        int friendHoles = 0, allHoles = 0, peekStarts = 0;
        for (int seed = 0; seed < 8; seed++)
        {
            var pk = new Icbm(screens, 900, 1000, 3, new Random(seed)) { Friends = new[] { "hamster", "otter" } };
            var seenHoles = new HashSet<Icbm.Crater>();
            for (int i = 0; i < 5; i++) pk.Launch();
            for (int i = 0; i < 60 * 600 && !pk.Finished; i++)
            {
                int before = pk.Peeks.Count;
                pk.Update(1 / 60.0, active);
                foreach (var c in pk.Craters) if (seenHoles.Add(c)) { allHoles++; if (c.Friend != null) friendHoles++; Expect(c.Friend == null || pk.Friends.Contains(c.Friend), "떠난 친구 중 하나"); }
                peekStarts += Math.Max(0, pk.Peeks.Count - before);
                Expect(pk.Peeks.All(p => pk.Craters.Contains(p.Hole) && p.Hole.Friend == p.Species && pk.Clock - p.Hole.At >= Icbm.FirstPeek - 1e-6)
                    && pk.Peeks.GroupBy(p => p.Hole).All(g => g.Count() == 1), $"빼꼼은 친구 구멍에서 그 친구만, 구멍마다 하나씩 (seed {seed})");
                Expect(!(pk.Stage == Icbm.Phase.Repair && pk.Craters.Count == 1 && pk.Peeks.Count > 0), $"고치는 구멍에서는 안 나온다 (seed {seed})");
            }
        }
        Expect(friendHoles > 0 && friendHoles < allHoles && Math.Abs(friendHoles / (double)allHoles - Icbm.FriendChance) < .25,
            $"구멍 일부만 친구 구멍: {friendHoles}/{allHoles}");
        Expect(peekStarts >= friendHoles, $"친구 구멍에서는 빼꼼한다: {peekStarts}번 / 친구 구멍 {friendHoles}");
        var lonely = new Icbm(screens, 900, 1000, 3, new Random(1));
        for (int i = 0; i < 5; i++) lonely.Launch();
        bool anyFriend = false;
        for (int i = 0; i < 60 * 600 && !lonely.Finished; i++) { lonely.Update(1 / 60.0, active); anyFriend |= lonely.Craters.Any(c => c.Friend != null) || lonely.Peeks.Count > 0; }
        Expect(!anyFriend, "떠난 친구가 없으면 다 검은 구멍, 아무도 안 나온다");
        Expect(Icbm.Rise(0) == 0 && Icbm.Rise(1.4) == 1 && Icbm.Rise(Icbm.PeekSec) == 0, "빼꼼: 올라왔다가 내려간다");

        // 고치기 전에 또 쏘면 줄을 서고, 구멍은 다섯까지. 다 고치면 끝난다
        var many = new Icbm(screens, 900, 1000, 3, new Random(3));
        int ok = 0;
        for (int i = 0; i < 8; i++) if (many.Launch()) ok++;
        Expect(ok == Icbm.MaxCraters, $"한꺼번에 다섯 발까지: {ok}");
        int most = 0;
        for (int i = 0; i < 60 * 600 && !many.Finished; i++) { many.Update(1 / 60.0, active); most = Math.Max(most, many.Craters.Count); }
        Expect(many.Finished && most == Icbm.MaxCraters && many.Launched == Icbm.MaxCraters, $"다섯 발 다 떨어지고 다 고친다: 구멍 최대 {most}, {many.Clock:F0}초");

        // 가로 + 세로 모니터: 고치러 갈 때 모니터 사이 빈 곳(화면 밖)으로 안 지나간다. 걷는 속도는 평소(70)보다 조금 빠른 100
        var world = new World(screens);
        int crossed = 0;
        for (int seed = 0; seed < 12; seed++)
        {
            var walk = new Icbm(screens, 300, 1000, 3, new Random(seed));
            walk.Launch();
            double lastX = 300, lastY = 1000, fastest = 0;
            for (int i = 0; i < 60 * 200 && !walk.Finished; i++)
            {
                walk.Update(1 / 60.0, null);
                Expect(world.Inside(walk.X, walk.Y), $"화면 밖으로 걷지 않는다 (seed {seed}: {walk.X:F0},{walk.Y:F0})");
                if (walk.Stage == Icbm.Phase.ToCrater) fastest = Math.Max(fastest, Math.Sqrt((walk.X - lastX) * (walk.X - lastX) + (walk.Y - lastY) * (walk.Y - lastY)) * 60);
                (lastX, lastY) = (walk.X, walk.Y);
            }
            Expect(walk.Finished && fastest <= Icbm.GoSpeed + 1, $"뛰지 않고 걸어가서 고친다 (seed {seed}: {fastest:F0}px/s)");
            if (walk.X >= 1920) crossed++;
        }
        Expect(crossed >= 2, $"다른 모니터의 구멍도 건너가서 고친다: {crossed}/12");

        // 가끔(4번에 한 번쯤)은 볼록이 근처에 떨어지고, 그러면 볼록이가 폭발 반대쪽으로 날아간다
        int nearHits = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            var nb = new Icbm(screens, 900, 700, 3, new Random(seed));
            bool blown = false;
            nb.OnEvent = e => blown |= e == "blown";
            nb.Launch();
            for (int i = 0; i < 60 * 20 && nb.Craters.Count == 0; i++) nb.Update(1 / 60.0, null);
            var c0 = nb.Craters[0];
            double dx = 900 - c0.X, dy = 700 - 36 - c0.Y, d = Math.Sqrt(dx * dx + dy * dy);
            Expect(blown == d < Icbm.CraterR * 1.7, $"폭발 가까이 있을 때만 날아간다 (seed {seed}, {d:F0}px)");
            if (!blown) continue;
            nearHits++;
            Expect(nb.BlownVx * dx + nb.BlownVy * dy > 0 && Math.Sqrt(nb.BlownVx * nb.BlownVx + nb.BlownVy * nb.BlownVy) >= new Tuning().DizzySpeed,
                $"폭발 반대쪽으로 세게 던진 만큼 빠르게 날아간다 (seed {seed})");
            var (far, _) = Flight(new Tuning(), nb.BlownVx, nb.BlownVy, hard: true);
            Expect(far.Pose == "dizzy", $"날아갔다 내려앉으면 어지러워한다 (seed {seed})");
        }
        Expect(nearHits is >= 25 and <= 80, $"4번에 한 번쯤 볼록이 근처에 떨어진다: {nearHits}/200");

        // 잡아서 옮기기: 가는 중에 구멍 옆에 놓아 주면 거기서부터 걸어가 금방 고친다. 고치던 중에 잡으면 처음부터 다시
        var carry = new Icbm(screens, 300, 1000, 3, new Random(5));
        carry.Launch();
        for (int i = 0; i < 60 * 30 && carry.Stage != Icbm.Phase.ToCrater; i++) carry.Update(1 / 60.0, null);
        var hole = carry.Craters[0];
        for (int i = 0; i < 30; i++) { carry.Update(1 / 60.0, null); carry.Hold(hole.X - 60, hole.Y); }
        Expect(carry.Stage == Icbm.Phase.ToCrater && carry.X == hole.X - 60 && carry.Y == hole.Y, "잡고 있는 동안은 잡힌 자리에 있다");
        double from = carry.Clock;
        for (int i = 0; i < 60 * 10 && carry.Stage != Icbm.Phase.Repair; i++) carry.Update(1 / 60.0, null);
        Expect(carry.Stage == Icbm.Phase.Repair && carry.Clock - from < 2, $"구멍 옆에 놓으면 금방 도착한다: {carry.Clock - from:F1}초");
        for (int i = 0; i < 90; i++) carry.Update(1 / 60.0, null);
        Expect(hole.R < hole.R0, "고치는 중엔 구멍이 줄어든다");
        carry.Hold(hole.X + 200, hole.Y);
        Expect(carry.Stage == Icbm.Phase.ToCrater && hole.R == hole.R0, "고치다 잡히면 구멍은 그대로, 다시 가서 처음부터");
        for (int i = 0; i < 60 * 20 && !carry.Finished; i++) carry.Update(1 / 60.0, null);
        Expect(carry.Finished, "놓아 주면 끝까지 고친다");

        // 떨어질 데가 없으면 하늘에서 터지고 구멍은 안 난다
        var none = new Icbm(new[] { screens[0] }, 900, 1000, 3, new Random(4));
        bool sky = false;
        none.OnEvent = e => sky |= e == "nowhere";
        none.Launch();
        for (int i = 0; i < 60 * 20 && !none.Finished; i++) none.Update(1 / 60.0, screens[0]);
        Expect(sky && none.Finished && none.Craters.Count == 0, "떨어질 데가 없으면 구멍 없이 끝난다");
        return $"ICBM: 한 발 {ic.Clock:F0}초(발사부터 복구까지), 다섯 발 {many.Clock:F0}초";
    }

    static string MinigunChecks()
    {
        var field = new Rectangle(0, 0, 1920, 1040);
        var win = new Rectangle(700, 200, 500, 300);
        double ground = 990;
        Minigun Make(int seed = 1) => new(field, new[] { win }, ground, 3, new Random(seed), dualChance: 0);
        void Run(Minigun m, double sec, double mx, double my, bool fire) { for (double t = 0; t < sec; t += 1 / 60.0) m.Update(1 / 60.0, mx, my, fire); }

        // 마우스 X 쪽으로 걷고, 위쪽만 겨눈다
        var walk = Make();
        Run(walk, 3, 1500, 900, false);
        Expect(Math.Abs(walk.X - 1500) < 3 && walk.Aim < 0, $"볼록이는 마우스 X 쪽으로 걷는다: {walk.X:F0}");
        Run(walk, .2, 1500, 1040, false);
        Expect(walk.Aim <= -.11 && walk.Aim >= -Math.PI + .11, "아래로는 못 쏜다");

        // 총신이 돌아야 쏘고, 돌고 나면 초당 25발
        var spin = Make();
        Run(spin, Minigun.SpinUpSec * .5, 950, 100, true);
        Expect(spin.Shots == 0, "총신이 도는 동안은 안 나간다");
        Run(spin, 1, 950, 100, true);
        Expect(spin.Shots is >= 18 and <= 27, $"돌고 나면 초당 25발: {spin.Shots}");

        // 맞은 자리가 파이고, 계속 쏘면 아래부터 위로 뚫고 올라간다. 창 밖은 안 파인다
        var dig = Make();
        Run(dig, 3.5, 950, 0, false);
        Run(dig, .5, 950, 0, true); // 총신 0.35초 + 몇 발
        double Top(Minigun m) { for (int gy = 0; gy < m.Gh; gy++) for (int gx = 0; gx < m.Gw; gx++) if (m.Gone(gx, gy)) return gy * Minigun.Cell; return double.MaxValue; }
        int early = dig.GoneCount();
        double d1 = Top(dig);
        Run(dig, 4, 950, 0, true);
        double d2 = Top(dig);
        Expect(early > 0 && dig.GoneCount() > early && d2 < d1 && d1 >= win.Bottom - 120, $"아래부터 파여 올라간다: {d1:F0} → {d2:F0}");
        bool outside = false;
        for (int gx = 0; gx < dig.Gw; gx++)
            for (int gy = 0; gy < dig.Gh; gy++)
                if (dig.Gone(gx, gy) && !win.Contains(gx * Minigun.Cell + 2, gy * Minigun.Cell + 2)) outside = true;
        Expect(!outside, "창 밖은 안 파인다");
        int dug = dig.GoneCount();

        // 수류탄: 창에 닿으면 크게 터진다. 빗나가면 바닥에서 터지고 창은 그대로
        var nade = Make();
        Run(nade, 3.5, 950, 0, false);
        bool boom = false;
        nade.OnEvent = e => boom |= e == "boom";
        nade.Throw(950, 300);
        for (int i = 0; i < 60 * 3 && !boom; i++) nade.Update(1 / 60.0, 950, 300, false);
        int big = nade.GoneCount();
        Expect(boom && nade.Blasts.Count == 1 && Math.Abs(nade.Blasts[0].Y - 300) < 1 && big * Minigun.Cell * Minigun.Cell > 12000,
            $"수류탄은 가는 길의 창 아랫변에서 안 터지고 찍은 자리(창 안쪽)에서 크게 터진다: y {nade.Blasts[0].Y:F0}, {big * Minigun.Cell * Minigun.Cell}px²");
        foreach (var (tx, ty) in new[] { (500.0, 150.0), (1700.0, 300.0), (980.0, 600.0) })
        {
            var aim = new Minigun(field, Array.Empty<Rectangle>(), ground, 3, new Random(2));
            aim.Throw(tx, ty);
            double last = double.MaxValue; // 터지기 직전 프레임에 찍은 자리 가까이 와 있어야 한다 (포물선이 제대로 맞는지)
            for (int i = 0; i < 60 * 3 && aim.Nades.Count > 0; i++)
            {
                foreach (var n in aim.Nades) last = Math.Sqrt((n.X - tx) * (n.X - tx) + (n.Y - ty) * (n.Y - ty));
                aim.Update(1 / 60.0, aim.X, 0, false);
            }
            Expect(last < 40 && aim.Blasts.Count == 1 && aim.Blasts[0].X == tx && aim.Blasts[0].Y == ty, $"수류탄은 찍은 자리로 날아가 거기서 터진다 ({tx},{ty}): 직전 {last:F0}px");
        }
        var miss = Make();
        bool missBoom = false;
        miss.OnEvent = e => missBoom |= e == "boom";
        miss.Throw(100, 900);
        Run(miss, 3, 100, 900, false);
        Expect(missBoom && miss.GoneCount() == 0, "창 밖을 찍으면 거기서 터지고 창은 그대로");
        Expect(Math.Abs(nade.ThrowAt - nade.Clock) > 0 && nade.Nades.Count == 0, "터진 수류탄은 사라진다");

        // 쌍 미니건: 총이 둘이라 두 배로 쏟아진다. 하는 동안 한 번씩 저절로 된다
        var dual = Make();
        dual.StartDual();
        Run(dual, Minigun.SpinUpSec + 1, 950, 100, true);
        Expect(dual.Dual && dual.Guns().Count() == 2 && dual.Guns().Select(x => x.Pivot.X).Distinct().Count() == 2 && dual.Shots >= 36,
            $"쌍 미니건: 양손에 총 둘, 두 배로: {dual.Shots}발");
        Run(dual, Minigun.DualSec, 950, 100, false);
        Expect(!dual.Dual && dual.Guns().Count() == 1, "10초 지나면 다시 하나");
        var later = Make();
        bool came = false;
        later.OnEvent = e => came |= e == "dual";
        Run(later, 45, 950, 100, false);
        Expect(came, "하는 동안 20~40초마다 쌍 미니건이 된다");

        // 창이 사라지면 대상에서 빼고 파인 자리도 지운다
        dig.RemoveTarget(win);
        Expect(dig.Targets.Count == 0 && dig.GoneCount() == 0 && !dig.Solid(950, 400), "창이 사라지면 대상에서 뺀다");
        return $"미니건: 4.5초 쏘면 {dug * Minigun.Cell * Minigun.Cell}px² 파임(아래 {d1:F0} → {d2:F0}), 수류탄 한 방 {big * Minigun.Cell * Minigun.Cell}px²";
    }

    static string WormsChecks()
    {
        var field = new Rectangle(0, 0, 1920, 1040);
        var screens = new[] { field, new Rectangle(1920, 0, 1920, 1040) };
        var a = new Win(new IntPtr(1), new Rectangle(150, 500, 600, 400), false);
        var bwin = new Win(new IntPtr(2), new Rectangle(1150, 450, 650, 450), false);
        var covered = new Win(new IntPtr(3), new Rectangle(700, 650, 300, 200), false);
        var full = new Win(new IntPtr(4), new Rectangle(0, 0, 1920, 1040), false);
        var isl = Worms.Islands(a, new[] { a, bwin, covered, full }, field, screens, 120, 4);
        Expect(isl.Field == field && isl.Wins.Count == 2 && isl.Wins.Contains(a.R) && isl.Wins.Contains(bwin.R), "섬: 가린 창·최대화 창은 빼고 창모드 창들");
        Expect(Worms.Islands(full, new[] { full }, field, screens, 120, 4).Wins.Count == 0, "창모드 창이 없으면 섬도 없다");

        Worms Make(int seed = 1, string gun = "normal") => new(field, new[] { a.R, bwin.R }, 3, new Random(seed), gun, "normal") { Wind = 0 };
        void Tick(Worms w, double sec) { for (double t = 0; t < sec; t += 1 / 60.0) w.Update(1 / 60.0); }
        var w = Make();
        Tick(w, .5);
        Expect(!w.Me.Air && !w.Cpu.Air && Math.Abs(w.Me.Y - a.R.Top) < Worms.Cell && Math.Abs(w.Cpu.Y - bwin.R.Top) < Worms.Cell && w.Me.X < w.Cpu.X,
            $"볼록이는 왼쪽 섬, 라이벌은 오른쪽 섬 위에 선다: 나 ({w.Me.X:F0},{w.Me.Y:F0},{w.Me.Air},{w.Me.Hp}) 라이벌 ({w.Cpu.X:F0},{w.Cpu.Y:F0},{w.Cpu.Air},{w.Cpu.Hp}) {w.Stage} {w.MyTurn}");

        // 폭발: 땅이 깎이고, 가까우면 다치고 날아간다. 받침이 없어지면 떨어진다
        var boom = Make();
        boom.Explode(boom.Me.X + 30, boom.Me.Y);
        Expect(!boom.Solid(boom.Me.X + 30, boom.Me.Y + 10) && boom.Me.Hp < boom.Me.Gun.Hp && boom.Me.Air && boom.Me.Vx < 0 && boom.Cpu.Hp == boom.Cpu.Gun.Hp, "폭발: 땅이 깎이고 가까운 쪽만 다쳐 날아간다");
        var drop = Make();
        double y0 = drop.Me.Y;
        drop.Explode(drop.Me.X, drop.Me.Y + 45); // 발밑 껍질까지 깎이게 (60px 아래면 한 칸이 남아 그대로 선다)
        Tick(drop, 1.5);
        Expect(!drop.Me.Air && drop.Me.Y > y0 + 20, "받침이 깎이면 떨어져 아래에 선다");
        // 섬 끝에서 걸어 나가면 퐁당
        var walk = Make();
        for (int i = 0; i < 60 * 20 && !walk.Me.Dead; i++) { walk.Walk(walk.Me, -1, 1 / 60.0); walk.Update(1 / 60.0); }
        Expect(walk.Me.Out && walk.Me.Dead, "섬 밖으로 걸어 나가면 물에 빠진다");

        // 섬은 창 전체: 발밑을 끝까지 파면 뚫려서 떨어지고 퐁당
        Expect(w.Lands.Contains(a.R) && w.Lands.Contains(bwin.R), "섬은 창 높이 그대로");
        var dig = Make();
        for (int depth = 50; depth < a.R.Height + 80; depth += 80) dig.Explode(dig.Me.X, dig.Me.Y + depth);
        Tick(dig, 3);
        Expect(dig.Me.Out, $"바닥을 다 파면 떨어져 퐁당 ({dig.Me.Y:F0})");

        // 대포마다 체력이 다르고, 바람은 모든 포탄을 민다
        foreach (var gun in Worms.Guns)
        {
            var t = Make(gun: gun.Name);
            Expect(t.Me.Hp == gun.Hp, $"{gun.Label} 체력 {gun.Hp}");
        }
        Expect(Worms.Guns.Select(g => g.Hp).SequenceEqual(new[] { 200, 300, 400 }) && Worms.GunOf("multi").Shells == 9, "멀티캐논 9발/200, 왕대포/300, 일반대포/400");
        double Land(Worms x, double wind)
        {
            x.Wind = wind;
            double? at = null;
            x.OnEvent = e => { if (e == "boom") at ??= x.Blasts[^1].X; };
            x.Fire(x.Me, -Math.PI / 2, .5);
            for (int i = 0; i < 60 * 8 && at == null; i++) x.Update(1 / 60.0);
            return at ?? double.NaN;
        }
        foreach (var gun in new[] { "normal", "giant" })
        {
            var calm = Make(gun: gun); double still = Land(calm, 0);
            var windy = Make(gun: gun); double pushed = Land(windy, 1);
            Expect(Math.Abs(still - calm.Me.X) < 20 && pushed > still + 40, $"바람이 {gun} 포탄을 민다: {still:F0} → {pushed:F0}");
        }
        var big = Make(gun: "giant");
        Land(big, 0);
        var small = Make(gun: "normal");
        Land(small, 0);
        Expect(big.Blasts[0].R > small.Blasts[0].R * 1.5, "왕대포 구멍이 더 크다");
        var multi = Make(gun: "multi");
        multi.Fire(multi.Me, -75 * Math.PI / 180, .45); // 제 섬 위로 높이 (섬 사이로 빠지면 물에 떨어져 안 터진다)
        Expect(multi.Shells.Count == 9 && multi.Shells.Select(x => Math.Round(x.Vx)).Distinct().Count() == 9, "멀티캐논은 9발을 부채꼴로");
        for (int i = 0; i < 60 * 8 && multi.Stage == Worms.Phase.Flying; i++) multi.Update(1 / 60.0);
        Expect(multi.Blasts.Count >= 7 && multi.Blasts.All(x => x.R < small.Blasts[0].R), $"멀티캐논은 작은 폭발 여럿 ({multi.Blasts.Count})");

        // 차례: 내가 쏘고 가라앉으면 라이벌, 라이벌도 쏘면 다시 나. 시간이 다 되면 넘어간다
        var turn = Make();
        turn.Fire(turn.Me, -Math.PI / 2, .3);
        for (int i = 0; i < 60 * 8 && turn.MyTurn; i++) turn.Update(1 / 60.0);
        Expect(!turn.MyTurn && turn.Stage == Worms.Phase.Turn, "내가 쏘고 나면 라이벌 차례");
        for (int i = 0; i < 60 * 12 && !turn.MyTurn && !turn.Done; i++) turn.Update(1 / 60.0);
        Expect(turn.MyTurn || turn.Stage is Worms.Phase.Won or Worms.Phase.Lost, "라이벌이 쏘고 나면 다시 내 차례");
        var idle = Make();
        Tick(idle, Worms.TurnSec + 1.5);
        Expect(!idle.MyTurn, "시간이 다 되면 차례가 넘어간다");

        // 라이벌은 볼록이 근처를 노린다 (빗나감 없이 계산하면 가까이 떨어진다)
        int near = 0;
        for (int seed = 0; seed < 6; seed++)
        {
            var ai = Make(seed);
            ai.Wind = (seed - 3) / 3.0;
            var (ang, pow) = ai.Plan(miss: 0);
            ai.MyTurn = false;
            double? at = null;
            ai.OnEvent = e => { if (e == "boom") at = Math.Abs(ai.Blasts[^1].X - ai.Me.X); };
            ai.Fire(ai.Cpu, ang, pow);
            for (int i = 0; i < 60 * 8 && at == null; i++) ai.Update(1 / 60.0);
            if (at < 70) near++;
        }
        Expect(near >= 5, $"라이벌 계산은 볼록이 근처에 떨어진다: {near}/6");
        // 실제로는 흔들려 쏜다: 첫 발은 대부분 빗나간다
        int firstNear = 0;
        for (int seed = 0; seed < 12; seed++)
        {
            var ai = Make(seed);
            ai.Wind = (seed % 5 - 2) / 2.0;
            var (ang, pow) = ai.Plan();
            ai.MyTurn = false;
            double? at = null;
            ai.OnEvent = e => { if (e == "boom") at ??= Math.Abs(ai.Blasts[^1].X - ai.Me.X); };
            ai.Fire(ai.Cpu, ang, pow);
            for (int i = 0; i < 60 * 8 && at == null; i++) ai.Update(1 / 60.0);
            if (at < 70) firstNear++;
        }
        Expect(firstNear <= 5, $"라이벌 첫 발은 대부분 빗나간다: {firstNear}/12 이 70px 안");

        // 라이벌이 다 다치면 이긴다
        var win = Make();
        for (int i = 0; i < 20 && !win.Cpu.Dead; i++) win.Explode(win.Cpu.X, win.CenterY(win.Cpu)); // 쓰러질 때까지
        win.Fire(win.Me, -Math.PI / 2, .2);
        Tick(win, 6);
        Expect(win.Stage == Worms.Phase.Won, "라이벌이 쓰러지면 이긴다");

        // 둘 다 라이벌 계산대로 쏘는 판: 끝까지 간다
        var duel = Make(9);
        var seat = new Random(4);
        for (int i = 0; i < 60 * 900 && !duel.Done; i++)
        {
            if (duel.MyTurn && duel.Stage == Worms.Phase.Turn && duel.Clock - duel.TurnAt > .5)
            {
                // 볼록이 차례도 같은 계산으로 (좌우만 바꿔서): 라이벌을 겨냥
                double best = double.MaxValue, ba = -Math.PI / 4, bp = .5;
                for (double deg = -170; deg <= -10; deg += 6)
                    for (double p = .3; p <= 1; p += .1)
                    {
                        double ax = duel.Me.X + Math.Cos(deg * Math.PI / 180) * p * Worms.MaxShot * .5, d = Math.Abs(ax - duel.Cpu.X);
                        if (d < best) (best, ba, bp) = (d, deg * Math.PI / 180, p);
                    }
                duel.Fire(duel.Me, ba + (seat.NextDouble() - .5) * .3, bp);
            }
            duel.Update(1 / 60.0);
        }
        Expect(duel.Done, $"한 판이 끝난다: 나 {duel.Me.Hp}, 라이벌 {duel.Cpu.Hp}, {duel.Clock:F0}초");
        return $"포트리스 라이벌: 계산만 하면 6번 중 {near}번 70px 안, 실제 첫 발은 12번 중 {firstNear}번, 시험 대결 {duel.Clock:F0}초에 {(duel.Stage == Worms.Phase.Won ? "볼록이" : "라이벌")} 승";
    }

    static string BreakoutChecks()
    {
        var field = new Rectangle(0, 0, 1920, 1040);
        var screens = new[] { field, new Rectangle(1920, 0, 1920, 1040) };
        int cell = 120;
        var used = new Win(new IntPtr(5), new Rectangle(300, 120, 1100, 700), false);
        var full = new Win(new IntPtr(6), new Rectangle(0, 0, 1920, 1040), false);
        var top = new Win(new IntPtr(7), new Rectangle(200, 200, 900, 500), true);
        var other = new Win(new IntPtr(8), new Rectangle(2100, 100, 1200, 800), false);

        // 고르기: 쓰던 창모드 창 → 볼록이 모니터의 큰 창 → 다른 모니터. 최대화·맨 위 고정 창은 안 된다
        Expect(Breakout.Pick(used, new[] { full, used }, field, screens, cell)?.Targets[0].Win == used, "메뉴 전에 쓰던 창모드 창에서 한다");
        Expect(Breakout.Pick(full, new[] { full, top, used }, field, screens, cell)?.Targets[0].Win == used, "최대화·맨 위 고정 창은 건너뛴다");
        Expect(Breakout.Pick(null, new[] { other }, field, screens, cell) is { } o2 && o2.Field == screens[1], "다른 모니터의 창이면 그 모니터에서");
        Expect(Breakout.Pick(full, new[] { full, top }, field, screens, cell) == null, "창모드 창이 없으면 못 한다");
        var tall = new Win(new IntPtr(9), new Rectangle(300, 100, 1100, 920), false);
        Expect(Breakout.Pick(tall, new[] { tall }, field, screens, cell)?.Targets[0].Board.Bottom == field.Bottom - cell * 3, "쟁반 위 칸 셋 높이까지만 조각낸다");
        // 작은 창이 여럿이면 함께: 겹치거나 가린 창, 다른 모니터 창은 빼고 넷까지
        var s1 = new Win(new IntPtr(11), new Rectangle(100, 100, 500, 300), false);
        var s2 = new Win(new IntPtr(12), new Rectangle(700, 80, 400, 300), false);
        var s3 = new Win(new IntPtr(13), new Rectangle(800, 200, 400, 300), false);   // s2 밑에 깔려 가린다
        var s4 = new Win(new IntPtr(14), new Rectangle(1300, 150, 450, 350), false);
        var s5 = new Win(new IntPtr(15), new Rectangle(2000, 100, 500, 300), false);  // 다른 모니터
        var many = Breakout.Pick(s1, new[] { s1, s2, s3, s4, s5 }, field, screens, cell)!.Value;
        Expect(many.Targets.Select(t => t.Win).SequenceEqual(new[] { s1, s2, s4 }), "작은 창 여럿을 함께 쓴다 (가린 창·다른 모니터 창은 빼고): "
            + string.Join(",", many.Targets.Select(t => t.Win.H)));
        Expect(Breakout.Pick(used, new[] { used, s4 }, field, screens, cell)!.Value.Targets.Count == 1, "큰 창 하나면 그것만");

        // 조각: 창을 빈틈없이 덮고, 잘게 나뉜다
        var board = used.R;
        Breakout Make(int seed = 1) => new(field, new[] { board }, 3, new Random(seed));
        var bo = Make();
        Expect(bo.Tiles.Count >= 100 && bo.Tiles.Sum(t => t.R.Width * (long)t.R.Height) == board.Width * (long)board.Height
            && bo.Tiles.All(t => board.Contains(t.R)), $"조각이 창을 빈틈없이 덮는다: {bo.Tiles.Count}개");
        var two = new Breakout(field, many.Targets.Select(t => t.Board).ToList(), 3, new Random(1));
        Expect(many.Targets.All(t => two.Tiles.Any(x => t.Board.Contains(x.R))), "여러 창이면 창마다 조각낸다");

        double plateTop = field.Bottom - 100; // 시험 창(아래 820)과 쟁반 사이를 넉넉히
        void Tick(Breakout x, double sec, Func<Breakout, double>? plateX = null)
        {
            for (double t = 0; t < sec; t += 1 / 60.0) x.Update(1 / 60.0, plateX?.Invoke(x) ?? 960, plateTop);
        }
        Breakout.Ball Fly(Breakout x, double bx, double by, double vx, double vy)
        {
            x.Launch();
            var ball = x.Balls[0];
            (ball.X, ball.Y, ball.Vx, ball.Vy) = (bx, by, vx, vy);
            return ball;
        }

        // 쟁반 위에 얹혀 있다가 클릭하면 위로, 안 누르면 3초 뒤 저절로
        Tick(bo, .5, _ => 700);
        Expect(bo.Balls[0].Stuck && bo.Balls[0].X == 700 && bo.Balls[0].Y < plateTop, "던지기 전엔 쟁반 위에 얹혀 따라다닌다");
        bo.Launch();
        Expect(!bo.Balls[0].Stuck && bo.Balls[0].Vy < 0, "클릭하면 위로 던진다");
        var auto = Make();
        Tick(auto, Breakout.AutoLaunch + .2);
        Expect(!auto.Balls[0].Stuck, "안 누르면 3초 뒤 저절로 던진다");

        // 쟁반: 가운데는 똑바로 위, 오른쪽 끝은 오른쪽 위로 눕게
        var mid = Make();
        var m = Fly(mid, 960, plateTop - 60, 0, mid.Speed);
        Tick(mid, .12);
        Expect(m.Vy < 0 && Math.Abs(m.Vx) < mid.Speed * .05, "쟁반 가운데에 맞으면 똑바로 위로");
        var edge = Make();
        var e1 = Fly(edge, 960 + edge.PlateW / 2.0 - 4, plateTop - 60, 0, edge.Speed);
        Tick(edge, .12);
        Expect(e1.Vy < 0 && e1.Vx > edge.Speed * .7, "쟁반 끝에 맞으면 비스듬히");

        // 모니터 테두리에서 튕긴다
        var wall = Make();
        var w1 = Fly(wall, 60, 900, -wall.Speed * .8, -wall.Speed * .6);
        Tick(wall, .3);
        Expect(w1.Vx > 0 && w1.X >= field.Left + wall.BallR, "모니터 왼쪽 끝에서 튕긴다");

        // 조각: 아래에서 맞으면 깨지고 아래로 튕긴다. 아주 빨라도 건너뛰지 않는다
        var hit = Make();
        var target = hit.Tiles.Where(t => t.R.Left < 800 && t.R.Right > 800).MaxBy(t => t.R.Bottom)!;
        var h1 = Fly(hit, 800, target.R.Bottom + 80, 0, -hit.Speed);
        Tick(hit, .3);
        Expect(target.Broken && hit.Broken == 1 && h1.Vy > 0 && hit.Shards.Count > 0, "맞은 조각이 깨지고 아래로 튕긴다");
        var fast = Make();
        fast.Speed = 20000;
        Fly(fast, 800, target.R.Bottom + 80, 0, -20000);
        fast.Update(1 / 30.0, 960, plateTop);
        Expect(fast.Broken == 1, "아주 빨라도 조각을 건너뛰지 않는다");

        // 놓치면 하나를 잃고 다시 쟁반 위로, 셋 다 잃으면 끝. 다 깨면 이긴다
        var miss = Make();
        for (int i = 0; i < 3; i++)
        {
            Fly(miss, 200, field.Bottom - 20, 0, miss.Speed);
            Tick(miss, .3, _ => 1600);
        }
        Expect(miss.Lives == 0 && miss.Stage == Breakout.Phase.Lost, "셋 다 놓치면 끝");
        Tick(miss, 2.1);
        Expect(miss.Done, "끝나고 2초 뒤 정리한다");
        var win = Make();
        foreach (var t in win.Tiles.Skip(1)) t.Broken = true;
        var last = win.Tiles[0];
        Fly(win, last.R.Left + last.R.Width / 2.0, last.R.Bottom + 60, 0, -win.Speed);
        Tick(win, .3);
        Expect(win.Stage == Breakout.Phase.Won, "다 깨면 이긴다");

        // 아이템: 떨어지는 걸 쟁반으로 받으면 효과. 놓치면 사라진다
        var drop = Make();
        drop.Items.Add(new Breakout.Item { Kind = "wide", X = 960, Y = plateTop - 150 });
        drop.Items.Add(new Breakout.Item { Kind = "life", X = 300, Y = plateTop - 150 });
        Tick(drop, 1.5);
        Expect(drop.Wide && drop.PlateW > drop.Balls.Count * 0 + 36 * 3 && drop.Lives == 3 && drop.Items.Count == 0, "받은 아이템만 효과, 놓친 건 사라진다");
        Tick(drop, Breakout.WideSec);
        Expect(!drop.Wide && drop.PlateW == 36 * 3, "쟁반 늘이기는 시간이 지나면 돌아온다");
        // 공 나누기: 날아가는 공마다 둘로, 여덟까지
        var split = Make();
        Fly(split, 960, 500, 100, -split.Speed);
        split.Apply("split");
        Expect(split.Balls.Count == 2 && split.Balls.All(x => x.Vy < 0) && split.Balls[0].Vx != split.Balls[1].Vx, "공 나누기: 둘로 갈라진다");
        for (int i = 0; i < 4; i++) split.Apply("split");
        Expect(split.Balls.Count == Breakout.MaxBalls, "공은 여덟까지");
        // 공이 여럿이면 모두 빠져야 하나를 잃는다
        var multi = Make();
        Fly(multi, 960, 500, 0, -multi.Speed);
        multi.Apply("split");
        (multi.Balls[0].X, multi.Balls[0].Y, multi.Balls[0].Vx, multi.Balls[0].Vy) = (200, field.Bottom - 20, 0, multi.Speed);
        Tick(multi, .3, _ => 1600);
        Expect(multi.Lives == 3 && multi.Balls.Count == 1, "공이 남아 있으면 안 잃는다");
        // 느리게, 끈끈이, 불공, 하나 더
        var slow = Make();
        var sb = Fly(slow, 100, 900, 0, -slow.Speed); // 조각을 깨면 조금 빨라지니 창 밖에서
        slow.Apply("slow");
        Tick(slow, 1 / 60.0);
        Expect(Math.Abs(Math.Sqrt(sb.Vx * sb.Vx + sb.Vy * sb.Vy) - slow.Speed * .65) < 1, "느리게: 공이 느려진다");
        Expect(!Breakout.Kinds.Contains("sticky"), "공을 붙잡는 아이템은 없다");
        // 왕볼: 두 배로 커져서 튕기지 않고, 지나가는 자리에 닿는 조각을 몽땅 부순다
        var giant = Make();
        double gx = giant.Tiles.Select(t => t.R.Left).Where(x => x > 800).Min() - giant.BallR * 1.5; // 두 칸 경계 가까이: 보통 공은 한 칸, 왕볼은 두 칸에 닿는다
        int rowsHere = giant.Tiles.Count(t => t.R.Left <= gx && t.R.Right > gx);
        var gb = Fly(giant, gx, board.Bottom + 60, 0, -giant.Speed);
        giant.Apply("giant");
        Tick(giant, 1.15);
        Expect(giant.Radius == giant.BallR * 2 && gb.Vy < 0 && giant.Broken >= rowsHere * 2 - 2,
            $"왕볼: 튕기지 않고 두 줄을 쭉 부순다 ({giant.Broken}/{rowsHere * 2})");
        var fire = Make();
        var col = fire.Tiles.Where(t => t.R.Left < 800 && t.R.Right > 800).ToList();
        var fb = Fly(fire, 800, board.Bottom + 60, 0, -fire.Speed);
        fire.Apply("fire");
        Tick(fire, 1.2);
        Expect(fire.Broken >= col.Count && col.All(t => t.Broken), $"불공: 튕기지 않고 한 줄을 뚫는다 ({fire.Broken}/{col.Count})");
        Expect(fire.Shards.Count <= Breakout.MaxShards && fire.BrokenOrder.Count == fire.Broken, "몰아 깨도 파편은 40개까지, 깨진 차례를 남긴다");
        var life = Make();
        for (int i = 0; i < 4; i++) life.Apply("life");
        Expect(life.Lives == Breakout.MaxLives, "하나 더: 다섯까지");

        // 쟁반을 공 밑으로 가져가는 사람: 공이 모니터 밖으로 안 나가고, 조각이 계속 깨지고, 아이템도 받는다
        var play = Make(7);
        var aim = new Random(3);
        double offset = 0;
        int items = 0;
        play.OnEvent = e => { if (e == "plate") offset = (aim.NextDouble() - .5) * play.PlateW * .8; if (e.StartsWith("item:")) items++; };
        bool inside = true;
        for (double t = 0; t < 180 && !play.Done; t += 1 / 60.0)
        {
            // 가장 낮게 내려오는 공을 받고, 공이 높이 있으면 떨어지는 아이템을 받으러 간다
            var low = play.Balls.Where(x => !x.Stuck && x.Vy > 0).MaxBy(x => x.Y);
            var item = play.Items.MaxBy(x => x.Y);
            double want = low != null && (low.Y > plateTop - 400 || item == null) ? low.X - offset : item?.X ?? play.PlateX;
            play.Update(1 / 60.0, Math.Clamp(want, field.Left + play.PlateW / 2.0, field.Right - play.PlateW / 2.0), plateTop);
            inside &= play.Balls.All(x => x.X >= field.Left && x.X <= field.Right && x.Y >= field.Top);
        }
        Expect(inside, "공은 모니터 밖으로 안 나간다");
        Expect(play.Broken >= play.Tiles.Count / 2 && items > 0, $"쟁반으로 받아치면 조각이 깨진다: {play.Broken}/{play.Tiles.Count}, 아이템 {items}");
        return $"벽돌깨기 받아치는 사람: {play.Clock:F0}초에 {play.Broken}/{play.Tiles.Count}조각, 아이템 {items}개, 공 {play.Lives}개 남음";
    }

    static void GameChecks(Tuning k)
    {
        var arena = new Rectangle(0, 0, 1920, 1080);
        var rng = new Random(5);
        bool Miss(int x, int y) => false;
        bool HitAll(int x, int y) => true;
        var still = new Point(900, 600);
        void Tick(Game g, double sec, Point mouse, Func<int, int, bool> hits) { for (int i = 0; i < sec * 60; i++) g.Update(1 / 60.0, mouse, hits); }

        var guns = Enumerable.Range(0, 60).Select(_ => new Game(arena, 0, 3, rng)).ToList();
        Expect(Game.Guns.All(w => guns.Any(g => g.Gun == w)), "무기는 셋 중 랜덤");

        // 샷건: 한 번에 여러 알이 퍼지고, 한 알이라도 맞으면 명중 하나. 같은 거리에서 권총보다 잘 맞는다
        var sg = new Game(arena, 0, 3, rng, Game.Shotgun);
        Tick(sg, Game.CountSec + .05, still, Miss);
        sg.Press(900, 600, Miss);
        sg.Release();
        Expect(sg.Shots == 1 && sg.Ammo == 4 && sg.Holes.Count == 8, "샷건 한 발에 8알");
        Expect(sg.Holes.Max(h => Math.Sqrt(Math.Pow(h.X - 900, 2) + Math.Pow(h.Y - 600, 2))) > 20, "산탄이 퍼진다");
        sg.Press(900, 600, Miss);
        Expect(sg.Shots == 1, "펌프하는 동안은 못 쏜다");
        bool Near(int x, int y) => Math.Pow(x - 940, 2) + Math.Pow(y - 600, 2) < 28 * 28; // 겨눈 곳에서 40px 옆의 작은 표적
        int RateOf(Weapon wpn)
        {
            int hits = 0;
            for (int i = 0; i < 300; i++)
            {
                var gg = new Game(arena, 0, 3, new Random(i), wpn);
                gg.Update(Game.CountSec + .05, still, Miss);
                gg.Press(900, 600, Near);
                hits += gg.Hits;
            }
            return hits;
        }
        int sgRate = RateOf(Game.Shotgun), pRate = RateOf(Game.Pistol);
        Expect(sgRate > pRate * 3 && sgRate > 150, $"대충 겨눠도 샷건이 잘 맞는다: 샷건 {sgRate}/300, 권총 {pRate}/300");
        var once = new Game(arena, 0, 3, rng, Game.Shotgun);
        once.Update(Game.CountSec + .05, still, Miss);
        once.Press(900, 600, HitAll);
        Expect(once.Hits == 1, "여러 알이 맞아도 명중은 하나");
        Expect(guns.Select(g => g.Theme).Distinct().Count() == Props.Sets.Count, "소품 묶음도 랜덤");

        // 권총: 한 번 누르면 한 발, 6발 뒤 장전
        var p = new Game(arena, 3, 3, rng, Game.Pistol);
        Expect(p.Press(500, 500, HitAll) == "wait", "3·2·1 동안은 안 나간다");
        Tick(p, Game.CountSec + .05, still, Miss);
        Expect(p.Stage == Game.Phase.Play, "3·2·1 뒤에 시작한다");
        p.Press(900, 600, Miss);
        Tick(p, .5, still, Miss);
        Expect(p.Shots == 1, "권총은 누르고 있어도 한 발");
        p.Release();
        for (int i = 0; i < 5; i++) { p.Press(900, 600, Miss); p.Release(); Tick(p, .2, still, Miss); }
        Expect(p.Ammo == 0 && p.Shots == 6 && p.Holes.Count == 6 && p.ReloadLeft > 0, "6발 쏘면 알아서 장전을 시작한다");
        Expect(p.Press(900, 600, Miss) == "empty", "장전하는 동안은 안 나간다");
        p.Release();
        Tick(p, .75, still, Miss);
        Expect(p.Ammo == 6, "장전하면 6발");
        p.Press(900, 600, Miss); p.Release();
        p.Reload();
        Expect(p.ReloadLeft > 0, "오른쪽 클릭 장전도 된다");
        Tick(p, .75, still, Miss);

        // 기관총: 누르고 있으면 연사. 조준점은 마우스에서 안 벗어나고, 탄이 퍼진다
        var m = new Game(arena, 0, 3, rng, Game.MachineGun);
        Tick(m, Game.CountSec + .05, still, Miss);
        m.Press(still.X, still.Y, Miss);
        Tick(m, 1, still, Miss);
        Expect(m.Shots is >= 10 and <= 13, $"기관총은 누르고 있으면 연사: 1초에 {m.Shots}발");
        Expect(m.AimX == still.X && m.AimY == still.Y, "반동이 있어도 조준점은 마우스 그대로 (Doom 처럼)");
        Expect(m.Bloom > 20 && Crosshair.Level(m.Bloom) >= 3, $"연사하면 퍼짐이 커지고 조준경 십자가 벌어진다: {m.Bloom:F0}");
        double spreadFar = m.Holes.Skip(m.Holes.Count - 4).Max(h => Math.Sqrt(Math.Pow(h.X - still.X, 2) + Math.Pow(h.Y - still.Y, 2)));
        Expect(spreadFar > 8, "탄이 퍼진다");
        m.Release();
        Tick(m, .8, still, Miss);
        Expect(m.Bloom < 3 && Crosshair.Level(m.Bloom) == 0, $"손을 떼면 금방 모인다: {m.Bloom:F1}");
        m.Press(still.X, still.Y, Miss);
        Tick(m, 2, still, Miss); // 남은 탄을 다 쏘고(약 1.6초) 장전하는 중 (장전 0.9초)
        m.Release();
        Expect(m.Ammo == 0 && m.ReloadLeft > 0 || m.Ammo == Game.MachineGun.Mag, $"다 쏘면 알아서 장전한다 (탄 {m.Ammo}, 장전 {m.ReloadLeft:F2})");
        Tick(m, Game.MachineGun.ReloadSec + .05, still, Miss);
        Expect(m.Ammo == Game.MachineGun.Mag, "자동 장전이 끝나면 30발");

        // 20초 뒤 조준경이 터지고, 결과는 클릭해야 닫힌다. 결과 중 클릭은 뒤 창으로 가지 않는다
        var end = new Game(arena, 0, 3, rng, Game.Pistol);
        Tick(end, Game.CountSec + .05, still, Miss);
        end.Press(still.X, still.Y, HitAll);
        end.Release();
        Tick(end, Game.PlaySec, still, Miss);
        Expect(end.Stage == Game.Phase.Boom && !end.Aiming, "20초가 지나면 조준경이 터진다");
        Expect(end.Press(500, 500, HitAll) == "wait" && end.Hits == 1, "터지는 동안엔 안 쏴진다");
        Tick(end, Game.BoomSec + .05, still, Miss);
        Expect(end.Stage == Game.Phase.Result && end.NewBest && end.Best == 1, "그다음 결과와 최고 기록");
        Expect(end.Press(500, 500, HitAll) == "close" && end.Stage == Game.Phase.Result, "결과는 잠깐은 보여 준다");
        Tick(end, Game.ResultMinSec, still, Miss);
        Expect(end.Press(500, 500, HitAll) == "close" && end.Stage == Game.Phase.Done, "그 뒤 클릭하면 닫힌다");
        var idle = new Game(arena, 0, 3, rng);
        Tick(idle, Game.CountSec + Game.PlaySec + Game.BoomSec + Game.ResultSec + .2, still, Miss);
        Expect(idle.Stage == Game.Phase.Done, "안 눌러도 결과는 저절로 닫힌다");

        var quit = new Game(arena, 0, 3, rng);
        Tick(quit, Game.CountSec + .05, still, Miss);
        var exit = quit.ExitButton();
        Expect(quit.Press(exit.X + exit.Width / 2, exit.Y + exit.Height / 2, HitAll) == "exit" && quit.Stage == Game.Phase.Result, "나가기 버튼");

        var bmp = Sprites.Embedded().Get("child/0/idle", 0, 3);
        Expect(Game.HitBitmap(bmp, 60, 90, 6) && !Game.HitBitmap(bmp, 3, 3, 6), "그림의 실제 픽셀로 맞힌다");
        // 창 윗변 소품
        var sp = Sprites.Embedded();
        var solids = Props.All.ToDictionary(n => n, n => Props.Solid(sp.Get(n, 0, 1)));
        var front = new Win(new IntPtr(1), new Rectangle(300, 300, 900, 500), false);
        var names = Props.Sets["garden"];
        int sat = 0;
        for (int seed = 0; seed < 10; seed++)
        {
            var on = Props.OnWindows(new[] { front }, new[] { arena }, names, new Random(seed), n => solids[n], Array.Empty<Rectangle>());
            sat += on.Count;
            Expect(on.All(p => Math.Abs(p.Bounds.Bottom - front.R.Top) <= 3 && p.Bounds.Left >= front.R.Left && p.Bounds.Right <= front.R.Right), "창 윗변에 앉는다");
            Expect(on.All(p => p.Bounds.Height < 120), "창 위 소품은 작다");
        }
        Expect(sat >= 10, $"창 위에 소품이 놓인다: {sat}");
        Expect(Props.OnWindows(new[] { new Win(new IntPtr(1), arena, false) }, new[] { arena }, names, rng, n => solids[n], Array.Empty<Rectangle>()).Count == 0, "최대화 창 위에는 안 놓는다");
        Expect(Props.OnWindows(new[] { new Win(new IntPtr(2), new Rectangle(0, 200, 1920, 200), false), front }, new[] { arena }, names, rng, n => solids[n], Array.Empty<Rectangle>())
            .All(p => Math.Abs(p.Bounds.Bottom - front.R.Top) > 3), "덮인 창의 윗변에는 안 놓는다 (덮은 창 윗변은 괜찮다)");
        Expect(Props.OnWindows(new[] { front with { Topmost = true } }, new[] { arena }, names, rng, n => solids[n], Array.Empty<Rectangle>()).Count == 0, "항상 위 창은 빼고");

        // 게임할 창 고르기
        var screensOne = new[] { arena };
        var used = new Win(new IntPtr(5), new Rectangle(300, 200, 900, 600), false);
        Expect(Game.ChooseArena(used, new[] { used }, arena, screensOne, 120) == (used.R, used.H), "메뉴 전에 쓰던 창에서 한다");
        var tiny = new Win(new IntPtr(6), new Rectangle(10, 10, 200, 150), false);
        var bigWin = new Win(new IntPtr(7), new Rectangle(100, 100, 1400, 800), false);
        Expect(Game.ChooseArena(tiny, new[] { tiny, used, bigWin }, arena, screensOne, 120).Window == bigWin.H, "쓰던 창이 너무 작으면 가장 큰 창");
        Expect(Game.ChooseArena(null, Array.Empty<Win>(), arena, screensOne, 120) == (arena, IntPtr.Zero), "창이 없으면 모니터 전체");
        var off = new Win(new IntPtr(8), new Rectangle(1500, 700, 900, 600), false);
        Expect(arena.Contains(Game.ChooseArena(off, new[] { off }, arena, screensOne, 120).Arena), "화면 밖으로 나간 부분은 자른다");

        // 창 안에서만 논다
        var room = used.R;
        var inRoom = new Brain(new Pet { Stage = "child", Full = 90, Mood = 50, Energy = 90 }, k, arena) { X = 1700, Y = 1000 };
        inRoom.StartGame(room);
        for (int i = 0; i < 600; i++)
        {
            inRoom.Mouse = new Point(room.Left + (i * 37) % room.Width, room.Top + (i * 53) % room.Height);
            inRoom.Update(1 / 60.0, false);
            Expect(room.Contains((int)inRoom.X, (int)inRoom.Y - 1), $"게임 창 안에서만 움직인다 ({inRoom.X:F0},{inRoom.Y:F0})");
        }
        var floor = Props.OnFloor(room, Props.Sets["snack"], new Random(2), n => solids[n], new[] { new Rectangle(room.Left + room.Width / 2 - 200, room.Bottom - 200, 400, 200) });
        Expect(floor.Count == 2 && floor.All(p => room.Contains(p.Bounds) && p.Bounds.Bottom >= room.Bottom - 8 && p.Bounds.Height < 120), "창 아래쪽 안에 작은 소품 둘");
        Expect(floor.All(p => !p.Bounds.IntersectsWith(new Rectangle(room.Left + room.Width / 2 - 200, room.Bottom - 200, 400, 200))), "총 자리는 비운다");

        var aimGame = new Game(arena, 0, 3, rng) { GunPivot = new Point(960, 1040) };
        aimGame.Update(.01, new Point(1500, 400), Miss);
        Expect(aimGame.GunAngle() is > -Math.PI / 2 and < 0, "총구가 오른쪽 위 조준점을 향한다");
        aimGame.Update(.01, new Point(200, 1070), Miss);
        Expect(aimGame.GunAngle() < -Math.PI / 2, "총구는 아래로는 안 돌아간다");

        // 엄폐: 커서가 다가오면 실제 창 가장자리 뒤로 숨고, 창 안쪽 몸은 안 맞는다. 그 창 위의 창도 함께 가린다
        var cw = new Rectangle(600, 300, 600, 400);
        var top = new Rectangle(1100, 250, 400, 200);
        var covs = Props.Covers(new[] { new Win(new IntPtr(2), top, false), new Win(new IntPtr(1), cw, false), new Win(new IntPtr(3), arena, false) }, arena, 120);
        Expect(covs.Count == 2, "최대화 창은 엄폐물에서 뺀다");
        var hide = new Brain(new Pet { Stage = "child", Full = 90, Mood = 50, Energy = 90 }, k, arena) { X = 300, Y = 900 };
        hide.StartGame(arena, new[] { covs[1] });
        bool ducked = false;
        for (int i = 0; i < 600 && !ducked; i++)
        {
            hide.Mouse = new Point((int)hide.X - 200, (int)hide.Y - 60);
            hide.Update(1 / 60.0, false);
            ducked = hide.CoverProp == covs[1];
        }
        Expect(ducked, "커서가 다가오면 창 뒤로 숨는다");
        Expect(hide.Shielded(cw.X + 10, cw.Y + 10) && hide.Shielded(top.Right - 5, top.Top + 5) && !hide.Shielded(cw.X - 10, cw.Y - 10),
            "숨은 창과 그 위 창 안쪽만 가려 준다");
        for (int i = 0; i < 240; i++) { hide.Mouse = new Point(100, 100); hide.Update(1 / 60.0, false); }
        Expect(hide.CoverProp == null, "잠깐 숨었다가 다시 나온다");

        // 도망 다니기: 달리고 멈추기를 되풀이한다. 모니터 밖으로 안 나가고, 쫓아와도 거리를 벌린다
        var w = new World(new[] { new Rectangle(-1920, 0, 1920, 1080), arena });
        var prey = new Brain(new Pet { Stage = "child", Full = 90, Mood = 50, Energy = 90 }, k, w, arena) { X = 900, Y = 700 };
        prey.StartGame(arena);
        double far = 0;
        int still2 = 0, moving = 0;
        var mouse = new PointF(1500, 300);
        for (int i = 0; i < 600; i++)
        {
            double dx = prey.X - mouse.X, dy = prey.Y - 50 - mouse.Y, d = Math.Sqrt(dx * dx + dy * dy) + 1;
            mouse = new PointF((float)(mouse.X + dx / d * 4), (float)(mouse.Y + dy / d * 4)); // 커서가 초당 240px 로 쫓아온다
            prey.Mouse = Point.Round(mouse);
            var (px, py) = (prey.X, prey.Y);
            prey.Update(1 / 60.0, false);
            if (Math.Abs(prey.X - px) + Math.Abs(prey.Y - py) < .5) still2++; else moving++;
            Expect(arena.Contains((int)prey.X, (int)prey.Y - 1) && prey.Mode == Mode.Prey, $"게임 모니터 안에 있다 ({prey.X:F0},{prey.Y:F0})");
            far += d;
        }
        Expect(still2 > 60 && moving > 120, $"달리고 멈추기를 되풀이한다: 멈춤 {still2}, 달림 {moving}");
        Expect(far / 600 > 150, $"쫓아와도 거리를 벌린다: 평균 {far / 600:F0}px");

        // 맞으면 멈칫하고 번쩍이며 총 반대쪽으로 밀린다
        var shot = new Brain(new Pet { Stage = "child", Full = 90, Mood = 50, Energy = 90 }, k, arena) { X = 900, Y = 600 };
        shot.StartGame(arena);
        shot.GameHit(900, 550, 960, 1040);
        var (hx, hy) = (shot.X, shot.Y);
        Expect(hy < 600 && shot.Frozen && shot.Now < shot.FlashUntil, "총 반대쪽(위)으로 밀리고 멈칫, 번쩍");
        shot.Update(.03, false);
        Expect(shot.X == hx && shot.Y == hy, "멈칫하는 동안은 안 움직인다");
        shot.Update(.05, false);
        for (int i = 0; i < 20; i++) shot.Update(1 / 60.0, false);
        Expect(shot.X != hx || shot.Y != hy, "멈칫이 끝나면 달아난다");

        var wav = Sfx.Make("pistol");
        Expect(wav.Length > 1000 && wav[0] == 'R' && wav[8] == 'W', "효과음을 만든다");
        prey.P.Stage = "adult_chubby";
        prey.Advance(0, false);
        Expect(prey.Mode == Mode.Prey, "게임 중엔 다른 동작이 끼어들지 않는다");
        var mood = prey.P.Mood;
        prey.EndGame(3);
        Expect(prey.Mode == Mode.Anim && prey.P.Plays == 1 && prey.P.Mood > mood, "게임 한 판은 놀아주기 한 번");
    }

    // 화면을 못 읽어도 멈추지 않는다 — 2026-11-02 에 20초 동안 볼록이가 굳은 적이 있다
    static void FailureChecks(Tuning k)
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        int failures = 0;
        var b = new Brain(new Pet { Stage = "child", Full = 90, Mood = 0, Energy = 90 }, k, area) { X = 900, Y = 800 };
        b.Sample = (_, _, _) => { failures++; throw new System.ComponentModel.Win32Exception(6); };
        b.P.Bites = 1;
        for (int i = 0; i < 60 * 40 && failures == 0; i++)
        {
            try { b.Update(1 / 60.0, false); }
            catch (System.ComponentModel.Win32Exception) { }
        }
        Expect(failures == 1, "화면색 읽기가 실패했는지 시험한다");
        var hole = b.Holes.Count;
        for (int i = 0; i < 120; i++)
        {
            try { b.Update(1 / 60.0, false); }
            catch (System.ComponentModel.Win32Exception) { }
        }
        Expect(failures == 1 && hole == 1, $"실패해도 같은 일을 되풀이하지 않는다: {failures}번, 구멍 {hole}");
    }

    static void NapChecks(Tuning k)
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        var areas = new[] { area };
        const int cell = 120;
        var (a, c) = (new IntPtr(1), new IntPtr(2));
        var rng = new Random(2);
        var mid = new Rectangle(400, 300, 900, 600);

        Expect(HideSpots.FindPerch(new[] { new Win(a, mid, false) }, areas, cell, rng).Count == 1, "창 윗변에 누울 수 있다");
        Expect(HideSpots.FindPerch(new[] { new Win(a, area, false) }, areas, cell, rng).Count == 0, "최대화한 창은 위 공간이 없다");
        Expect(HideSpots.FindPerch(new[] { new Win(a, mid, true) }, areas, cell, rng).Count == 0, "항상 위 창에는 안 올라간다");
        Expect(HideSpots.FindPerch(new[] { new Win(c, area, false), new Win(a, mid, false) }, areas, cell, rng).Count == 0, "덮인 창에는 안 올라간다");
        Expect(HideSpots.FindPerch(new[] { new Win(a, new Rectangle(0, 0, 900, 500), false) }, areas, cell, rng).Count == 0, "화면 맨 위 창에는 못 올라간다");

        Brain Sleeper(Func<Rectangle> rect, Func<bool>? alive = null)
        {
            var b = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 60 }, k, area)
            {
                X = 200, Y = 950,
                Windows = () => new List<Win> { new(a, rect(), false) },
                Probe = _ => new WinState(alive?.Invoke() ?? true, false, rect()),
            };
            b.Care("sleep");
            for (int i = 0; i < 1500 && b.Mode != Mode.Perch; i++) b.Update(1 / 60.0, false);
            return b;
        }
        void Run(Brain b, double sec) { for (int i = 0; i < sec * 60; i++) b.Update(1 / 60.0, false); }

        // 올라가서 눕고, 창이 움직이면 실려 간다
        var rect = mid;
        var nap = Sleeper(() => rect);
        Expect(nap.Mode == Mode.Perch && nap.P.Asleep && nap.Pose == "sleep", "창 위로 올라가 누워 잔다");
        Expect(Math.Abs(nap.Y - rect.Top) < 12 && nap.X > rect.Left && nap.X < rect.Right, "윗변 위에 있다");
        var dx = nap.X - rect.Left;
        rect.Offset(300, -100);
        Run(nap, .2);
        Expect(nap.Mode == Mode.Perch && Math.Abs(nap.X - rect.Left - dx) < 4 && Math.Abs(nap.Y - rect.Top) < 12, "창을 옮기면 실려 간다"); // 도착 판정이 3픽셀 안이다

        // 다 자면 뛰어내린다
        nap.P.Energy = 99.9;
        Run(nap, 2.5); // 기운 0.1 이 차는 데 약 1.8초
        Expect(!nap.P.Asleep && nap.Mode != Mode.Perch, "다 자면 내려온다");
        Run(nap, 6);
        Expect(Math.Abs(nap.Y - (area.Bottom - k.FloorGap)) < 2 || nap.Mode != Mode.Thrown, "바닥까지 내려온다");

        // 창이 닫히면 깨서 떨어지고 화낸다
        bool open = true;
        var closed = Sleeper(() => mid, () => open);
        open = false;
        Run(closed, .2);
        Expect(!closed.P.Asleep && closed.Mode == Mode.Thrown, "창이 사라지면 떨어진다");
        for (int i = 0; i < 1000 && closed.Mode == Mode.Thrown; i++) closed.Update(1 / 60.0, false);
        Expect(closed.Pose == "angry", "떨어지면 화낸다");

        // 깨우면 뛰어내리고, 누워 있는 창을 활성화해도 안 놀란다
        var poke = Sleeper(() => mid);
        poke.Foreground = () => new Win(a, mid, false);
        Run(poke, .2);
        Expect(poke.Mode == Mode.Perch, "누워 있는 창을 눌러도 계속 잔다");
        poke.Care("wake");
        Run(poke, .1);
        Expect(!poke.P.Asleep && poke.Mode == Mode.Thrown, "깨우면 뛰어내린다");

        // 다른 창이 활성화되어 덮으면 일어나 비킨다
        var over = Sleeper(() => mid);
        over.Foreground = () => new Win(c, new Rectangle(300, 100, 1200, 800), false);
        Run(over, .2);
        Expect(!over.P.Asleep && over.Mode != Mode.Perch, "활성 창이 덮으면 깨서 비킨다");

        // 올라갈 창이 없으면 그 자리에서 잔다
        var here = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 60 }, k, area) { X = 500, Y = 800 };
        here.Care("sleep");
        Expect(here.P.Asleep && here.X == 500, "창이 없으면 그 자리에서 잔다");

        // 기운이 떨어지면 알아서 창으로 간다
        var tired = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 30 }, k, area)
        {
            X = 200, Y = 950,
            Windows = () => new List<Win> { new(a, mid, false) },
            Probe = _ => new WinState(true, false, mid),
        };
        for (int i = 0; i < 60 * 40 && tired.Mode != Mode.Perch; i++) tired.Update(1 / 60.0, false);
        Expect(tired.Mode == Mode.Perch, "졸리면 알아서 창 위로 간다");
    }

    static void FeedChecks(Tuning k)
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        Brain Pet(double full) => new(new Pet { Stage = "child", Full = full, Mood = 100, Energy = 100 }, k, area) { X = 900, Y = 800 };
        void Run(Brain b, double sec) { for (int i = 0; i < sec * 60; i++) b.Update(1 / 60.0, false); }

        // 던지면 날아와서 입에 들어가고, 닿으면 먹기 시작한다. 화면에 남지 않는다
        var one = Pet(30);
        one.Care("feed");
        Expect(one.P.Feeds == 1 && one.P.Full > 60 && one.Foods.Count == 1, "주면 바로 포만이 오르고 씨앗이 날아온다");
        var (sx, sy) = (one.Foods[0].Sx, one.Foods[0].Sy);
        Expect(Math.Abs(sx - one.X) is >= 140 and <= 230 && sy > one.Y - 200, "위가 아니라 근처에서 던져 준다");
        Expect(Math.Sign(sx - one.X) == one.Dir, "볼록이 앞쪽에서 날아온다");
        Run(one, .2);
        Expect(one.Foods.Count == 1 && Math.Abs(one.Foods[0].X - one.X) < Math.Abs(sx - one.X), "씨앗이 볼록이 쪽으로 날아온다");
        Run(one, .35);
        Expect(one.Foods.Count == 0 && one.Pose == "eat", "입에 닿으면 먹기 시작한다");
        Run(one, 2);
        Expect(one.Mode != Mode.Anim || one.Pose != "eat" || one.PlayingAlone, "다 먹으면 다시 논다"); // 혼자 놀기의 간식 먹기는 밥이 아니다

        // 연달아 주면 준 만큼 먹고, 오물오물만 조금 길어진다
        var many = Pet(10);
        for (int i = 0; i < 3; i++) { many.Care("feed"); Run(many, .1); }
        Run(many, .6);
        Expect(many.P.Feeds == 3 && Math.Abs(many.P.Full - Math.Min(100, 10 + 3 * k.FeedFull)) < 1, $"연달아 줘도 다 먹는다: {many.P.Feeds}, 포만 {many.P.Full:F0}");
        Expect(many.Pose == "eat", "먹는 중이다");
        Run(many, 4);
        Expect(many.Foods.Count == 0 && (many.Pose != "eat" || many.PlayingAlone), "오물오물은 끝이 난다");

        // 배불러도 던지는 건 보이고, 닿으면 고개를 젓는다
        var fed = Pet(100);
        fed.Care("feed");
        Expect(fed.P.Feeds == 0 && fed.Foods.Count == 1, "배불러도 씨앗은 날아온다");
        Run(fed, .5);
        Expect(fed.Emoting && fed.EmoteName == "no" && fed.P.Feeds == 0, "배부르면 닿았을 때 고개를 젓는다");
        Run(fed, .5);
        Expect(fed.Foods.Count == 0, "거절당한 씨앗도 사라진다");

        // 혼자 놀다가도 바로 먹는다
        var busy = Pet(30);
        for (int i = 0; i < 600 && busy.Mode != Mode.Anim; i++) busy.Update(.05, false);
        busy.Care("feed");
        Run(busy, .55);
        Expect(busy.Pose == "eat" && busy.P.Feeds == 1, "놀던 중에도 바로 먹는다");

        // 화면 끝에서는 반대쪽에서 던진다
        var edge = Pet(30);
        edge.X = 1860; edge.Dir = 1;
        edge.Care("feed");
        Expect(edge.Foods[0].Sx < area.Right && edge.Dir == -1, "화면 끝이면 뒤돌아 받는다");
    }

    static void ActiveChecks(Tuning k)
    {
        var left = new Rectangle(-1920, 0, 1920, 1080);
        var main = new Rectangle(0, 0, 1920, 1080);
        var side = new Rectangle(1920, -271, 1080, 1839);
        var w = new World(new[] { left, main, side });
        const int cell = 120;
        var (front, other) = (new IntPtr(7), new IntPtr(8));
        Brain Pet(double x, double y) => new(new Pet { Stage = "child", Mood = 90, Full = 90, Energy = 90 }, k, w, w.At(x, y)) { X = x, Y = y };

        // 친 것과 겹쳐 있던 것을 구분한다
        var box = new Rectangle(500, 500, 80, 90);
        Expect(HideSpots.HitBy(new(100, 400, 390, 300), new(130, 400, 390, 300), box) is { Dx: 1, Moved: 30 }, "오른쪽으로 끌어 치면 오른쪽으로 민다");
        Expect(HideSpots.HitBy(new(600, 400, 300, 300), new(570, 400, 300, 300), box) is { Dx: -1 }, "왼쪽으로 끌어 치면 왼쪽으로 민다");
        Expect(HideSpots.HitBy(new(400, 100, 300, 390), new(400, 130, 300, 390), box) is { Dy: 1 }, "위에서 치면 아래로 민다");
        Expect(HideSpots.HitBy(new(400, 400, 300, 300), new(420, 400, 300, 300), box) == null, "이미 뒤에 있던 건 친 것이 아니다");
        Expect(HideSpots.HitBy(new(0, 0, 100, 100), new(10, 0, 100, 100), box) == null, "안 닿으면 친 것이 아니다");

        // 활성 창을 피한다
        var wide = new Rectangle(0, 0, 1920, 1080);
        var near = w.NearestFree(900, 500, wide, cell, k.FloorGap);
        Expect(near is { } n && !main.Contains((int)n.X, (int)n.Y), "주 모니터가 활성 창으로 꽉 차면 다른 모니터로 나간다");
        Expect(new World(new[] { main }).NearestFree(900, 500, wide, cell, k.FloorGap) == null, "모니터가 하나뿐이고 꽉 차면 나갈 곳이 없다");
        var part = new Rectangle(400, 200, 800, 600);
        Expect(w.NearestFree(500, 500, part, cell, k.FloorGap) is { } f && !part.IntersectsWith(new Rectangle((int)f.X - 40, (int)f.Y - 90, 80, 90)), "일부만 가리면 가장 가까운 옆으로 나간다");

        var roam = Pet(100, 900);
        roam.Active = new Win(front, part, false);
        for (int i = 0; i < 500; i++)
        {
            var (x, y) = roam.NextRoam();
            Expect(!part.IntersectsWith(new Rectangle((int)(x - 42), (int)(y - 96), 84, 96)), "돌아다닐 곳은 활성 창 위가 아니다");
        }

        var onWindows = Pet(100, 900);
        var quiet = new Rectangle(1000, 100, 600, 500);
        onWindows.Windows = () => new List<Win> { new(other, quiet, false) };
        int on = Enumerable.Range(0, 400).Count(_ => { var (x, y) = onWindows.NextRoam(); return quiet.Contains((int)x, (int)y - 60); });
        Expect(on > 120, "돌아다닐 때 비활성 창 쪽을 고른다");

        // 어쩌다 활성 창 위에 있으면 곧장 빠져나가고, 창 뒤로 들어가지 않는다
        var ontop = new Rectangle(300, 200, 1000, 800);
        var dropped = Pet(100, 950); // 먼저 창 밖에서 활성 창을 알아 둔 뒤, 창 위로 옮겨 놓는다
        dropped.Foreground = () => new Win(front, ontop, false);
        dropped.Update(1 / 60.0, false);
        dropped.X = 800; dropped.Y = 600;
        for (int i = 0; i < 120; i++) { dropped.Update(1 / 60.0, false); Expect(!dropped.Behind, "활성 창 뒤로 들어가지 않는다"); }
        Expect(!ontop.IntersectsWith(new Rectangle((int)dropped.X - 42, (int)dropped.Y - 96, 84, 96)), "활성 창 위에서는 2초 안에 빠져나간다");

        // 혼자 놀던 중이어도 활성 창 위면 그만두고 나간다 — 멍때리기는 9초까지 가므로 운에 맡기지 않고 여러 번 본다
        for (int round = 0; round < 40; round++)
        {
            var busy = Pet(100, 950);
            busy.Foreground = () => new Win(front, ontop, false);
            for (int i = 0; i < 30 && busy.Mode != Mode.Anim; i++) busy.Update(.1, false);
            busy.X = 800; busy.Y = 600;
            for (int i = 0; i < 150; i++) busy.Update(1 / 60.0, false);
            Expect(!ontop.IntersectsWith(new Rectangle((int)busy.X - 42, (int)busy.Y - 96, 84, 96)), "혼자 놀다가도 활성 창에서 빠져나간다");
        }

        var stuck = new Brain(new Pet { Stage = "child", Mood = 90, Full = 90, Energy = 90 }, k, main) { X = 800, Y = 600 };
        stuck.Foreground = () => new Win(front, main, false);
        for (int i = 0; i < 600; i++) stuck.Update(1 / 60.0, false);
        Expect(stuck.X == 800 || stuck.Mode != Mode.Thrown, "나갈 곳이 없으면 그냥 있는다");

        // 비활성 창에 있다가 그 창이 활성화되면 놀라서 밖으로 뛰어나간다
        Win? fg = new Win(other, new Rectangle(1400, 700, 400, 300), false);
        var st = Pet(600, 500);
        st.Foreground = () => fg;
        st.Update(1 / 60.0, false);
        var target = new Rectangle(300, 200, 800, 700);
        fg = new Win(front, target, false);
        for (int i = 0; i < 12; i++) st.Update(1 / 60.0, false);
        Expect(st.Mode == Mode.Anim, "활성화되면 반응한다");
        for (int i = 0; i < 900 && target.IntersectsWith(new Rectangle((int)st.X - 42, (int)st.Y - 96, 84, 96)); i++) st.Update(1 / 60.0, false);
        Expect(!target.IntersectsWith(new Rectangle((int)st.X - 42, (int)st.Y - 96, 84, 96)), "반응한 뒤 창 밖으로 뛰어나간다");

        // 활성 창으로 치면 날아간다
        var dragged = new Rectangle(100, 500, 300, 400);
        Win? moving = new Win(front, dragged, false);
        var hitPet = Pet(700, 900);
        hitPet.Foreground = () => moving;
        hitPet.Update(1 / 60.0, false);
        for (int i = 0; i < 120 && hitPet.Mode != Mode.Thrown; i++)
        {
            dragged.Offset(30, 0);
            moving = new Win(front, dragged, false);
            hitPet.Update(1 / 60.0, false);
        }
        Expect(hitPet.Mode == Mode.Thrown && hitPet.Dir > 0, "창으로 치면 그 방향으로 날아간다");
        for (int i = 0; i < 2000 && hitPet.Mode == Mode.Thrown; i++) hitPet.Update(1 / 60.0, false);
        Expect(hitPet.Mode == Mode.Anim && hitPet.Pose == "angry", "맞고 내려앉으면 화낸다");

        // 혼자 논다
        var alone = Pet(1500, 900);
        alone.P.Mood = 100; alone.P.Full = 100; alone.P.Energy = 100;
        var seen = new HashSet<string>();
        for (int i = 0; i < 60 * 400; i++) { alone.Update(1 / 60.0, false); seen.Add(alone.Pose); } // 무작위라 240초면 가끔 셋만 나온다
        Expect(seen.Count(p => p is "groom" or "stretch" or "hop" or "roll" or "dig" or "think" or "eat") >= 4, "가만히 두면 혼자 논다: " + string.Join(",", seen));
    }

    // YELL 의 실제 배치: 왼쪽 가로, 가운데 가로, 오른쪽 세로(위아래로 더 길다)
    static void WorldChecks(Tuning k)
    {
        var left = new Rectangle(-1920, 0, 1920, 1080);
        var main = new Rectangle(0, 0, 1920, 1080);
        var side = new Rectangle(1920, -271, 1080, 1839);
        var w = new World(new[] { left, main, side });
        const int cell = 120;

        Expect(w.NextArea(left, side, cell) == main, "왼쪽에서 오른쪽은 가운데를 거친다");
        Expect(new World(new[] { left, side }).NextArea(left, side, cell) == null, "떨어진 모니터는 이어지지 않는다");
        Expect(w.At(2500, 1500) == side && w.At(-5000, 500) == left, "가장 가까운 모니터를 찾는다");
        Expect(!w.Inside(1800, 1300), "모니터 사이 빈 공간은 안이 아니다");

        var b = new Brain(new Pet { Stage = "child", Mood = 90, Full = 90, Energy = 90 }, k, w, main) { X = 1000, Y = 1000 };
        foreach (var (x, y) in new[] { (2500.0, 1500.0), (-1000.0, 500.0), (2400.0, -100.0), (900.0, 900.0) })
        {
            var from = (b.X, b.Y);
            int steps = 0;
            while (!b.Go(x, y, 900, 1 / 60.0) && steps++ < 20000)
                Expect(w.Inside(b.X, b.Y), $"걷는 동안 빈 공간에 안 들어간다 ({from} → {x},{y}: {b.X:F0},{b.Y:F0})");
            Expect(steps < 20000 && Math.Abs(b.X - x) < 4 && Math.Abs(b.Y - y) < 4, $"목적지에 닿는다 ({x},{y}: {b.X:F0},{b.Y:F0})");
        }

        Brain Fly(Rectangle from, double x, double y, double vx, double vy)
        {
            var f = new Brain(new Pet { Stage = "child", Mood = 90, Full = 90, Energy = 90 }, k, w, from) { X = x, Y = y };
            f.Throw(vx, vy);
            for (int i = 0; i < 4000 && f.Mode == Mode.Thrown; i++)
            {
                f.Update(1 / 60.0, false);
                Expect(w.Inside(f.X, f.Y - 1), $"날아가는 동안 빈 공간에 안 들어간다 ({f.X:F0},{f.Y:F0})");
            }
            Expect(f.Mode != Mode.Thrown, "결국 내려앉는다");
            return f;
        }
        var over = Fly(main, 1700, 500, 2400, -300);
        Expect(over.Area == side && Math.Abs(over.Y - (side.Bottom - k.FloorGap)) < 1, "맞닿은 곳은 날아서 넘어가 그 모니터 바닥에 내려앉는다");
        var wall = Fly(side, 2100, 1450, -2400, 0);
        Expect(wall.Area == side, "맞닿지 않은 쪽은 벽처럼 튕긴다");
        var back = Fly(main, 300, 500, -2400, -300);
        Expect(back.Area == left, "왼쪽 모니터로도 넘어간다");

        // 가운데가 최대화 창으로 덮여 있어도 다른 모니터의 창 뒤로 숨을 수 있다
        var wide = new IntPtr(1);
        var other = new IntPtr(2);
        var spots = HideSpots.Find(new[] { new Win(wide, main, false), new Win(other, new Rectangle(-1598, 156, 1258, 894), false) },
            w.Areas, cell, .64, new Random(1));
        Expect(spots.Count > 0 && spots.All(s => s.H == other && s.A == left), "다른 모니터의 창 뒤로 숨는다");
    }

    static void Expect(bool ok, string what)
    {
        if (!ok) throw new Exception("자가 검사 실패: " + what);
    }

    public static int Check(TextWriter o)
    {
        var k = new Tuning();
        var good = Week(k, Three, 2, 3, 1);
        var sweet = Week(k, Three, 2, 8, 1);
        var once = Week(k, new[] { H(9, 30) }, 2, 3, 1);
        var twice = Week(k, new[] { H(9, 30), H(14) }, 2, 3, 1);
        var none = Week(k, Array.Empty<int>(), 0, 0, 0);
        o.WriteLine(Row("성실", good));
        o.WriteLine(Row("애교", sweet));
        o.WriteLine(Row("하루 2번", twice));
        o.WriteLine(Row("바쁜 주", once));
        o.WriteLine(Row("방치", none));

        Expect(good.P.Miss == 0 && good.P.BitesTotal == 0 && good.P.Stage == "adult_chubby", "하루 3번이면 무탈");
        Expect(sweet.P.Stage == "adult_sweet", "쓰다듬기를 듬뿍 하면 애교쟁이");
        Expect(once.P.BitesTotal > 0, "하루 1번이면 화면을 먹는다");
        Expect(good.P.HungryShare < k.GrumpyHungry && sweet.P.HungryShare < k.GrumpyHungry && once.P.HungryShare >= k.GrumpyHungry,
            $"심술이는 횟수가 아니라 배고팠던 비율로: 하루 3번 {good.P.HungryShare:P0}·하루 1번 {once.P.HungryShare:P0} (기준 {k.GrumpyHungry:P0})");
        Expect(none.P.Stage == "adult_grumpy" && none.P.Bites == k.MaxBites, "방치하면 심술이");

        var back = none.P; // 밥을 주면 먹은 화면을 전부 뱉는다 (YELL 2026-11-13)
        (back.Food, back.Asleep) = (1, false);
        Expect(back.Act(k, "feed") && back.Bites == 0 && back.Food == 0, "밥을 주면 볼 속 화면을 다 뱉고, 바구니에서 하나 줄어든다");
        Expect(!back.Act(k, "feed"), "바구니가 비면 못 준다");
        var basket = new Pet { Food = 0 }; // 먹이 바구니는 일정 시간마다 하나씩, 최대까지
        basket.Tick(k, k.FoodEveryMin - 1);
        Expect(basket.Food == 0, "먹이는 시간이 차야 생긴다");
        basket.Tick(k, 2);
        Expect(basket.Food == 1, "시간이 차면 하나 생긴다");
        for (int i = 0; i < 20; i++) basket.Tick(k, k.FoodEveryMin);
        Expect(basket.Food == k.FoodMax, "바구니는 최대까지만");
        var full = new Pet(); // 배가 덜 차 있으면 간간이, 배고플수록 자주 먹는다
        full.Tick(k, k.EggMin);
        full.Full = 0; full.Mood = 80;
        for (int i = 0; i < 25; i++) full.Tick(k, 1);
        Expect(full.Bites >= 1, $"배가 비면 20분쯤에 한 입 ({full.Bites})");
        var fed = new Pet();
        fed.Tick(k, k.EggMin);
        fed.Full = 95; fed.Mood = 80;
        for (int i = 0; i < 60; i++) { fed.Tick(k, 1); fed.Full = 95; }
        Expect(fed.Bites == 0, "거의 배부르면 한 시간 동안은 안 먹는다");

        var away = new Pet(); // 자리를 비운 동안은 나빠지지 않는다
        away.Tick(k, k.EggMin);
        var before = (away.Full, away.Mood, away.AgeMin);
        for (int i = 0; i < 60 * 48; i++) away.Tick(k, 1, true);
        Expect(before == (away.Full, away.Mood, away.AgeMin), "자리 비움은 수치를 멈춘다");

        Expect(!new Pet().Act(k, "feed"), "알은 돌볼 수 없다");
        var s = new Pet();
        s.Tick(k, k.EggMin); s.Energy = 50; s.Act(k, "sleep");
        Expect(!s.Act(k, "pet") && s.Act(k, "wake"), "잠든 동안은 깨우기만 된다");

        var (fx, fy) = Brain.Fling(new[] { (0.0, new Point(0, 0)), (0.1, new Point(200, -100)) });
        Expect(Math.Abs(fx - 2000) < 1e-6 && Math.Abs(fy + 1000) < 1e-6, "놓는 순간의 속도");
        Expect(Brain.Fling(new[] { (0.0, new Point(5, 5)) }) == (0, 0), "움직임이 없으면 속도 0");

        var (soft, softY) = Flight(k, 1500, -900);
        Expect(soft.Mode == Mode.Anim && soft.Pose == "happy" && soft.Y == softY && soft.P.Mood > 50, "살살 던지면 튕기다 내려앉아 좋아한다");
        var (hard, hardY) = Flight(k, -6000, -3000);
        Expect(hard.Mode == Mode.Anim && hard.Pose == "dizzy" && hard.Y == hardY && hard.P.Mood < 50, "세게 던지면 어지러워한다");
        Expect(hard.X >= 60 && hard.X <= 1860, "벽 밖으로 나가지 않는다");
        var (blast, _) = Flight(k, 1500, -900, hard: true);
        Expect(blast.Pose == "dizzy" && blast.P.Mood < 50, "폭발에 날아가면 느려도 세게 던진 것처럼 어지러워한다");

        var zigzag = Enumerable.Range(0, 20).Select(i => (i * .03, new Point(500 + (i % 2) * 60, 400))).ToList();
        var straight = Enumerable.Range(0, 20).Select(i => (i * .03, new Point(500 + i * 40, 400))).ToList();
        Expect(Brain.Reversals(zigzag, 14) >= 3, "좌우로 흔들면 방향이 뒤집힌다");
        Expect(Brain.Reversals(straight, 14) == 0, "한쪽으로 끄는 건 흔드는 게 아니다");

        var shake = new Brain(new Pet { Stage = "child", Mood = 50 }, k, new Rectangle(0, 0, 1920, 1080)) { X = 900, Y = 500 };
        shake.Lift();
        for (int i = 0; i < 90; i++) { shake.Mouse = new Point(900 + (i % 2) * 80, 500); shake.Update(1 / 60.0, false); }
        Expect(shake.Pose == "fly", "마구 흔들면 빙글빙글 돈다");
        for (int i = 0; i < 12; i++) { shake.Mouse = new Point(900, 500); shake.Update(1 / 60.0, false); }
        shake.Drop();
        Expect(shake.Pose == "dizzy" && shake.P.Mood < 50, "오래 흔들고 놓으면 어지러워한다");

        var calm = new Brain(new Pet { Stage = "child", Mood = 50 }, k, new Rectangle(0, 0, 1920, 1080)) { X = 900, Y = 500 };
        calm.Lift();
        for (int i = 0; i < 60; i++) { calm.Mouse = new Point(500 + i * 6, 400); calm.Update(1 / 60.0, false); }
        Expect(calm.Pose == "held", "그냥 옮기면 안 돈다");

        HideChecks(k);
        WorldChecks(k);
        ActiveChecks(k);
        FeedChecks(k);
        NapChecks(k);
        FailureChecks(k);
        GameChecks(k);
        o.WriteLine(RunnerChecks(k));
        o.WriteLine(BreakoutChecks());
        o.WriteLine(WormsChecks());
        o.WriteLine(MinigunChecks());
        o.WriteLine(IcbmChecks());
        UpdaterChecks();
        PetSheetChecks();
        VisitorChecks();
        foreach (var check in GameModes.Checks) o.WriteLine(check()); // 게임 모듈은 제 규칙을 스스로 검사한다
        o.WriteLine(PetArt.Check()); // 데이터로 그린 캐릭터
        o.WriteLine(Clover.Check());
        o.WriteLine(Gestures.Check());
        LangChecks();
        MemoryChecks();
        ClickChecks(k);
        EmoteChecks(k);

        o.WriteLine("자가 검사 통과");
        return 0;
    }
}
