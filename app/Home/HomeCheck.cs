using System.Diagnostics;
using System.Text.Json;

namespace Bolok;

// 방석·집 자가 검사 (--selfcheck, docs/home.md 11장)와 속도 재기 (--home-bench)
static class HomeCheck
{
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: 방석·집 — " + what); }

    static HomeSpot Spot(double x, double y, bool house, bool top = false) => new()
    {
        On = true, X = x, Y = y, SleepX = x + 1, SleepDy = house ? 9 : 0, SitDy = house ? 0 : -6, HasTop = top, TopX = x + 40, TopY = y - 80,
    };

    public static string Check(Tuning k)
    {
        var art = HomeArt.Embedded;

        // ── 표·뽑기 ──
        Expect(HomeBook.Cushions.Length == 10 && HomeBook.Houses.Length == 10, "방석 10·집 10");
        var ids = HomeBook.Cushions.Concat(HomeBook.Houses).ToList();
        Expect(ids.Distinct().Count() == 20, "id 가 겹치지 않는다");
        Expect(art.Items.Count == 20 && ids.All(id => art.Find(id) != null), "20개 모두 home.json 에 있다");
        foreach (var id in ids)
            foreach (var (code, _) in L.Languages)
                Expect(L.Has("home." + id, code), $"home.{id} 의 {code} 글");
        foreach (var key in new[] { "menu.home", "menu.home_cushion", "menu.home_house", "menu.home_reset", "menu.home_hide" })
            foreach (var (code, _) in L.Languages) Expect(L.Has(key, code), $"{key} 의 {code} 글");

        var species = PetArt.Embedded.Species.ToList();
        Expect(species.All(s => HomeBook.FamilyOf(s) != null), "그림 데이터의 모든 종에 가족이 있다: " + string.Join(",", species.Where(s => HomeBook.FamilyOf(s) == null)));
        Expect(HomeBook.FamilySpecies.All(s => species.Contains(s)) && HomeBook.FamilySpecies.Count() == species.Count, "가족 표의 종이 그림 데이터와 같다");
        Expect(HomeBook.FamilyOf("cat#1") == "cat", "색 변형도 가족");
        foreach (var s in species)
            foreach (bool house in new[] { true, false })
                Expect(Enumerable.Range(0, 20).All(i => HomeBook.Known(HomeBook.Pick(house, s, new List<string>(), new Random(i)))), $"{s} 뽑기가 아는 물건만 낸다");

        string One(int seed) => string.Join(",", Enumerable.Range(0, 30).Select(_ => HomeBook.Pick(true, "cat", new List<string>(), new Random(seed))));
        Expect(One(7) == One(7), "씨가 같으면 같은 뽑기");
        var nine = HomeBook.Houses.Take(9).ToList();
        var rnd = new Random(3);
        Expect(Enumerable.Range(0, 1000).All(_ => HomeBook.Pick(true, "cat", nine, rnd) == HomeBook.Houses[9]), "안 받은 것이 남아 있으면 받은 것은 안 나온다 (9개 받은 뒤 1000번)");
        Expect(Enumerable.Range(0, 100).All(_ => HomeBook.Known(HomeBook.Pick(false, "hamster", HomeBook.Cushions, rnd))), "다 받은 뒤에도 뽑힌다");

        Dictionary<string, int> Count(bool house, string sp, int n)
        {
            var r = new Random(11);
            var d = new Dictionary<string, int>();
            for (int i = 0; i < n; i++) { var id = HomeBook.Pick(house, sp, new List<string>(), r); d[id] = d.GetValueOrDefault(id) + 1; }
            return d;
        }
        var cat = Count(true, "cat", 2000);
        double catShare = (cat.GetValueOrDefault("cattree") + cat.GetValueOrDefault("box")) / 2000.0;
        Expect(catShare is > .5 and < .7, $"고양이 집 2000번 cattree+box 가 50~70% ({catShare:P0})");
        var peng = Count(true, "penguin", 2000);
        Expect(peng.GetValueOrDefault("igloo") / 2000.0 is > .40 and < .55, "펭귄 → igloo 가 40~55%");
        var ham = Count(true, "hamster", 2000);
        Expect(ham.OrderByDescending(c => c.Value).First().Key == "rounddoor", "햄스터 → rounddoor 가 가장 많다");
        foreach (var four in new[] { "chick", "duck", "owl", "penguin" })
            Expect(Count(false, four, 1000).GetValueOrDefault("egg") == 0, $"{four} 는 방석 egg 가 안 나온다");
        Expect(Count(false, "dragon", 4000).GetValueOrDefault("egg") > 0, "다른 종은 egg 도 나온다");

        // ── 줄 것 ──
        Expect(HomeBook.Owed("egg", false, false) == null && HomeBook.Owed("baby", false, false) == null, "아기 → 없음");
        Expect(HomeBook.Owed("child", false, false) == "cushion", "어린이 → 방석");
        Expect(HomeBook.Owed("adult_sweet", false, false) == "cushion" && HomeBook.Owed("adult_sweet", true, false) == "house", "어른 → 방석 다음 집");
        Expect(HomeBook.Owed("final_grumpy", true, true) == null && HomeBook.Owed("child", true, false) == null, "다 받았으면 없음");
        var book = new HomeBook();
        book.Place(false, "round"); book.Place(true, "kennel");
        (book.CushionGiven, book.HouseGiven) = (true, true);
        book.NewPet();
        Expect(HomeBook.Owed("child", book.CushionGiven, book.HouseGiven) == "cushion" && book.Got.Count == 2 && book.Cushion == "round", "새 알 뒤엔 다시 (수집·놓인 것은 그대로)");

        // ── 저장 ──
        var save = new HomeBook();
        save.Place(false, "leaf"); save.Place(true, "teepee"); save.Receive("egg");
        (save.CushionAt, save.HouseAt, save.CushionGiven, save.HouseGiven, save.Hidden) = (new[] { 100.5, 900 }, new[] { 400.0, 900 }, true, false, true);
        var json = JsonSerializer.Serialize(save.Snapshot());
        var back = new HomeBook();
        back.Load(JsonSerializer.Deserialize<HomeSave>(json));
        Expect(JsonSerializer.Serialize(back.Snapshot()) == json && back.Got.SequenceEqual(new[] { "leaf", "teepee", "egg" }), "HomeSave 를 JSON 으로 썼다 읽으면 같다");
        Expect(JsonSerializer.Deserialize<SaveData>("{\"X\":3}")!.Home == null, "칸이 없는 옛 저장은 null");
        var old = new HomeBook();
        old.Load(null);
        Expect(old.Got.Count == 0 && !old.CushionGiven && !old.HouseGiven, "칸이 없으면 빈 수집");
        old.Load(new HomeSave(new List<string> { "round", "없어진물건", "kennel" }, "없어진물건", "round", null, null, true, true));
        Expect(old.Got.SequenceEqual(new[] { "round", "kennel" }) && old.Cushion == null && old.House == null, "모르는 id 는 읽을 때 빠지고, 방석 자리에 집 id 가 있으면 무시");

        // ── 그림 ──
        foreach (var it in art.Items)
        {
            Expect(it.W <= 72 && it.H <= 72, $"{it.Id}: 72칸 이하 (티피는 시안을 다시 재니 68칸 — 계획의 62쯤은 어림이었다)");
            Expect(!it.Pal.ContainsKey('o') || it.Pal['o'] == Color.FromArgb(0x5C, 0x3A, 0x28), $"{it.Id}: o 는 #5C3A28");
            Expect(!it.Pal.Values.Any(c => c.R == 255 && c.G == 0 && c.B == 255), $"{it.Id}: 자홍(투명 키)이 없다");
            Expect(it.House == HomeBook.IsHouse(it.Id) && (it.House ? it.Door.Length > 0 : it.Door.Length == 0), $"{it.Id}: 집만 door 가 있다");
            Expect(it.Seat.X >= 0 && it.Seat.X <= it.W && it.Seat.Y >= 0 && it.Seat.Y <= it.H + 6, $"{it.Id}: seat 가 그림 근처");
            Expect((it.Top != null) == (it.Id == "cattree"), $"{it.Id}: top 은 캣타워만");
            foreach (var (y, x0, x1) in it.Door.Concat(it.Front))
                Expect(y >= 0 && y < it.H && x0 >= 0 && x1 < it.W && x0 <= x1, $"{it.Id}: door·front 가 그림 안");
            foreach (var (y, x0, x1) in it.Door)
                for (int x = x0; x <= x1; x++) Expect(it.At(x, y) != null, $"{it.Id}: door 칸이 투명 ({x},{y})");
            foreach (int s in new[] { 2, 3, 4, 6 })
            {
                var bmp = art.Body(it, s);
                Expect(bmp.Width == it.W * s && bmp.Height == it.H * s, $"{it.Id}: 배율 {s} 그림 크기");
                using var region = art.Clip(it, s, it.House).Clone();
            }
            Expect(art.Thumb(it, 32).Width == 32, $"{it.Id}: 작은 그림");
        }
        var futon = art.Find("futon")!;
        Expect(futon.Cover != null && futon.Front.Length > 0, "이불 요 세트는 펫이 있을 때 덧그리는 앞 겹 그림이 있다");
        Expect(art.Body(futon, 3, true) != art.Body(futon, 3, false), "이불 앞 겹 그림이 다른 그림");
        // 놓인 자리: seat 줄이 바닥에 닿고, 펫 X 가 그림 안
        foreach (var it in art.Items)
        {
            var sp = HomeGeom.Spot(it, 3, HomeGeom.Origin(it, 3, 800, 1030), 1030, 20.5, 22, false);
            Expect(Math.Abs(sp.Y - 1030) < 1 && sp.X > 800 - it.W * 3 / 2.0 && sp.X < 800 + it.W * 3 / 2.0, $"{it.Id}: 자리가 바닥·그림 안");
        }

        BrainChecks(k);
        return $"방석·집: 20개 (방석 10·집 10), 47종 가족 표, 고양이 집 cattree+box {catShare:P0}, 펭귄 igloo {peng.GetValueOrDefault("igloo") / 2000.0:P0}, 줄 것·저장 칸, 그림 20개 × 배율 2·3·4·6, 낮잠·잠·집 들어가기·깨기·끌기·활성 창·들기, 프레임 메모리 늘지 않음";
    }

    static void BrainChecks(Tuning k)
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        double floor = area.Bottom - k.FloorGap;
        Brain Make(string stage, double energy, double x = 400)
        {
            var b = new Brain(new Pet { Stage = stage, Full = 90, Mood = 90, Energy = energy }, k, area) { X = x, Y = floor };
            b.NightNow = () => false;
            return b;
        }
        void Run(Brain b, double sec, bool afk = false) { for (int i = 0; i < sec * 60; i++) b.Update(1 / 60.0, afk); }

        // 방석만 있고 기운 40 → 30초 안에 가서 방석 위에서 잔다
        {
            var b = Make("child", 40);
            b.Cushion = Spot(1500, floor, false);
            Run(b, 30);
            Expect(b.Mode == Mode.Home && b.AtHome == HomeUse.Nap && b.P.Asleep, "방석만 있고 기운 40 → 30초 안에 방석 낮잠");
            Expect(Math.Abs(b.X - b.Cushion.SleepX) < 2 && Math.Abs(b.Y - b.Cushion.Y) < 2 && b.HomeClipNow == HomeClipKind.Front, "자리가 앉는 자리, 앞 겹만 가린다");
            // 물건 자리를 옮기면 따라간다, 없어지면 Wander
            b.Cushion.SleepX += 200; b.Cushion.X += 200;
            Run(b, .1);
            Expect(Math.Abs(b.X - b.Cushion.SleepX) < 2 && b.Mode == Mode.Home, "물건 자리를 옮기면 펫이 따라간다");
            // 활성 창이 덮쳐도 안 달아난다
            b.Foreground = () => new Win(new IntPtr(9), new Rectangle((int)b.X - 150, (int)b.Y - 200, 300, 300), false);
            Run(b, 2);
            Expect(b.Mode == Mode.Home && b.P.Asleep, "방석 위에서 활성 창이 덮쳐도 안 달아나고 안 날아간다");
            b.Foreground = () => null;
            // 다 자면 기지개 뒤 나온다
            b.P.Energy = 100; b.P.Asleep = false;
            for (int i = 0; i < 60 * 5 && b.Mode == Mode.Home; i++) b.Update(1 / 60.0, false); // 기지개 1.2초 뒤 나온다 (나온 뒤엔 혼자 놀 수 있어 Wander 인지는 안 본다)
            Expect(b.Mode != Mode.Home && b.HomeClipNow == HomeClipKind.None && b.AtHome == HomeUse.None, "깨면 나온다, 오림이 풀린다");
            // 낮잠 중 없어지면 그 자리에서 계속 잔다
            var c = Make("child", 40);
            c.Cushion = Spot(900, floor, false);
            Run(c, 20);
            Expect(c.Mode == Mode.Home, "다시 방석으로");
            c.Cushion.On = false;
            Run(c, .1);
            Expect(c.Mode == Mode.Wander && c.P.Asleep, "물건이 없어지면 Wander, 자던 중이면 그 자리에서 계속 잔다");
        }

        // 집이 있고 자리 비움으로 잠들면 졸린 걸음으로 걸어가 안에서 잔다 (걷는 동안에도 Asleep)
        {
            var b = Make("adult_chubby", 80, 300);
            b.House = Spot(1200, floor, true);
            b.Update(1 / 60.0, true);
            Expect(b.Mode == Mode.GoHome && b.P.Asleep, "자리 비움으로 잠들면 집으로 걷는다 (Asleep 은 true)");
            for (int i = 0; i < 60 * 20 && b.Mode != Mode.Home; i++) b.Update(1 / 60.0, true);
            Expect(b.Mode == Mode.Home && b.AtHome == HomeUse.Sleep && b.P.Asleep && b.HomeClipNow == HomeClipKind.Inside, "집 안에서 잔다 (오림: 문 안만)");
            Run(b, 1, true);
            Expect(Math.Abs(b.Y - (b.House.Y + b.House.SleepDy)) < 1 && b.Pose == "sleep", "잘 때 문 안에서 더 내려간다");
            // 깨우면 내다본 뒤 걸어 나온다
            b.P.Asleep = false;
            Run(b, .2);
            Expect(b.Mode == Mode.Home && b.Pose == "idle", "깨면 먼저 내다본다");
            Run(b, 2);
            Expect(b.Mode == Mode.Wander && b.HomeClipNow == HomeClipKind.None, "그 뒤 걸어 나와 Wander");
            // 들기로 빠져나오면 오림이 풀린다
            var c = Make("adult_chubby", 80, 300);
            c.House = Spot(900, floor, true);
            c.Update(1 / 60.0, true);
            for (int i = 0; i < 60 * 20 && c.Mode != Mode.Home; i++) c.Update(1 / 60.0, true);
            c.Lift();
            Expect(c.AtHome == HomeUse.None && c.HomeClipNow == HomeClipKind.None, "들면 AtHome 은 None");
            var d = Make("child", 40);
            d.Cushion = Spot(700, floor, false);
            Run(d, 20);
            d.StartPuppet();
            Expect(d.AtHome == HomeUse.None, "게임을 시작하면 AtHome 은 None");
        }

        // 기운 0 으로 쓰러지면 그 자리 그대로
        {
            var b = Make("adult_chubby", 0, 300);
            b.House = Spot(1200, floor, true);
            Run(b, 2);
            Expect(b.P.Asleep && b.Mode == Mode.Wander, "기운 0 으로 쓰러진 잠은 제자리");
        }

        // 아기는 안 간다, 어린이는 집에서 안 잔다
        {
            var b = Make("baby", 20);
            b.Cushion = Spot(900, floor, false); b.House = Spot(1200, floor, true);
            Expect(!b.GoHome(false, HomeUse.Nap), "아기는 방석에 안 간다");
            var c = Make("child", 20);
            c.House = Spot(1200, floor, true);
            Expect(!c.GoHome(true, HomeUse.Sleep), "어린이는 집에서 안 잔다 (집은 다 자란 뒤)");
        }

        // 캣타워 어른은 위로 올라가 잔다
        {
            var b = Make("adult_sweet", 80, 300);
            b.House = Spot(1000, floor, true, true);
            b.Update(1 / 60.0, true);
            for (int i = 0; i < 60 * 20 && !(b.Mode == Mode.Home && b.Pose == "sleep"); i++) b.Update(1 / 60.0, true);
            Expect(b.Mode == Mode.Home && Math.Abs(b.Y - b.House.TopY) < 2 && b.HomeClipNow == HomeClipKind.Front, "캣타워 어른은 위 방석에서 잔다 (앞 겹만)");
        }

        // 낮잠 고르기 무게: 방석과 창 위가 다 있을 때 400번 → 방석 65~85% (3:1)
        {
            var mid = new Rectangle(400, 300, 900, 600);
            int cushion = 0, perch = 0, houseN = 0;
            for (int i = 0; i < 400; i++)
            {
                var b = Make("child", 40);
                b.Windows = () => new List<Win> { new(new IntPtr(1), mid, false) };
                b.Cushion = Spot(1500, floor, false);
                if (!b.TryRest()) continue;
                if (b.Mode == Mode.GoHome) cushion++; else if (b.Mode == Mode.GoNap) perch++;
            }
            double share = cushion / (double)Math.Max(1, cushion + perch);
            Expect(share is > .65 and < .85, $"방석과 창 위가 다 있을 때 방석 65~85% ({share:P0})");
            for (int i = 0; i < 400; i++)
            {
                var b = Make("adult_chubby", 40);
                b.NightNow = () => true;
                b.Cushion = Spot(1500, floor, false); b.House = Spot(1000, floor, true);
                if (!b.TryRest()) continue;
                if (b.Mode == Mode.GoHome && b.HomeIsHouse) houseN++;
            }
            Expect(houseN / 400.0 is > .55 and < .8, $"밤엔 집 6 : 방석 3 → 집 55~80% ({houseN / 400.0:P0})");
        }

        // 방석·집이 하나도 없으면 지금 낮잠 검사 그대로: 창 위로 간다 / 창도 없으면 TryRest 는 false
        {
            var b = Make("child", 40);
            b.Windows = () => new List<Win> { new(new IntPtr(1), new Rectangle(400, 300, 900, 600), false) };
            Expect(b.TryRest() && b.Mode == Mode.GoNap, "방석·집이 없으면 창 위로");
            var c = Make("child", 40);
            Expect(!c.TryRest(), "방석·집도 창도 없으면 TryRest 는 false (그 자리에서 잔다)");
        }

        // 혼자 놀 때 방석에 앉는다 (수치 그대로)
        {
            var b = Make("child", 60);
            b.Cushion = Spot(700, floor, false);
            int sat = 0;
            for (int i = 0; i < 300; i++)
            {
                b.Mode = Mode.Wander; b.P.Asleep = false;
                if (b.GoHome(false, HomeUse.Sit)) { sat++; b.Mode = Mode.Wander; }
            }
            Expect(sat == 300, "GoHome(Sit) 가 방석으로 보낸다");
            var c = Make("child", 85);   // 기운 80 넘게: 그 사이 무작위 낮잠(기운 80 이하에서만)이 끼지 않게 (예전 70 은 가끔 낮잠이 끼어 검사가 떨어졌다)
            c.Cushion = Spot(700, floor, false);
            Expect(c.GoHome(false, HomeUse.Sit), "앉으러 간다");
            double mood = c.P.Mood;
            Run(c, 12);
            Expect(c.Mode is not (Mode.GoNap or Mode.Perch) && !c.P.Asleep && c.P.Energy <= 85.1, $"앉기는 안 자고 기운이 늘지 않는다: {c.Mode}, 잠 {c.P.Asleep}, 기운 {c.P.Energy:0.0}");
        }

        // 프레임마다 새로 잡는 메모리가 늘지 않는다 (방석 위에서 자는 동안). Pet.Tick 이 틱마다 돌려주는 목록은 원래 있던 것이라, 방석 없이 그냥 자는 것과 견준다
        {
            long Bytes(Brain b)
            {
                for (int i = 0; i < 120; i++) b.Update(1 / 60.0, false); // 데우기
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 600; i++) b.Update(1 / 60.0, false);
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
            var home = Make("child", 40);
            home.Cushion = Spot(1000, floor, false);
            Run(home, 20);
            Expect(home.Mode == Mode.Home && home.P.Asleep, "메모리 재기 준비");
            var plain = Make("child", 40);
            plain.P.Asleep = true;
            long homeBytes = Bytes(home), plainBytes = Bytes(plain);
            Expect(homeBytes <= plainBytes, $"방석 위에서 자는 동안 프레임마다 새로 잡는 메모리가 그냥 자는 것보다 늘지 않는다 (600프레임 {homeBytes}B ≤ {plainBytes}B)");
        }
    }

    // 속도 재기: 그림·Region 만들기, 작은 그림, 집에서 자는 동안 프레임 메모리 (--home-bench)
    public static string Bench()
    {
        var art = HomeArt.Embedded;
        var sw = Stopwatch.StartNew();
        foreach (var it in art.Items) { art.Body(it, 3); art.Clip(it, 3, it.House); }
        double make = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        foreach (var it in art.Items) art.Thumb(it, 32);
        double thumbs = sw.Elapsed.TotalMilliseconds;
        long bytes = 0;
        foreach (var it in art.Items) bytes += (long)it.W * 3 * it.H * 3 * 4;
        var k = new Tuning();
        var area = new Rectangle(0, 0, 1920, 1080);
        double floor = area.Bottom - k.FloorGap;
        var b = new Brain(new Pet { Stage = "adult_chubby", Full = 90, Mood = 90, Energy = 80 }, k, area) { X = 300, Y = floor };
        b.House = Spot(1200, floor, true);
        b.Update(1 / 60.0, true);
        for (int i = 0; i < 60 * 20 && b.Mode != Mode.Home; i++) b.Update(1 / 60.0, true);
        for (int i = 0; i < 120; i++) b.Update(1 / 60.0, true);
        long before = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        for (int i = 0; i < 6000; i++) b.Update(1 / 60.0, true);
        double upd = sw.Elapsed.TotalMilliseconds / 6000;
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        return $"그림 20개 + 오림 20개 만들기 {make:0}ms (배율 3, 그림 합계 {bytes / 1024}KB), 작은 그림 20개 {thumbs:0}ms, 집에서 자는 Brain.Update {upd * 1000:0.0}µs/프레임, 6000프레임 메모리 {alloc}B (모드 {b.Mode})";
    }
}
