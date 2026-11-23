using System.Diagnostics;
using System.Text.Json;

namespace Bolok;

// 방석·집 자가 검사 (--selfcheck, docs/home.md 11장)와 속도 재기 (--home-bench)
static class HomeCheck
{
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: 방석·집 — " + what); }

    // ground: 그 모니터의 바닥 (안 주면 y — 바닥에 있는 것). 창 위에 놓인 것은 y 가 ground 보다 작다. 그림 자리(Bounds)는 가로 140·세로 70 쯤 (방석 크기)
    static HomeSpot Spot(double x, double y, bool house, bool top = false, double? ground = null) => new()
    {
        On = true, X = x, Y = y, SleepX = x + 1, SleepDy = house ? 9 : 0, SitDy = house ? 0 : -6, HasTop = top, TopX = x + 40, TopY = y - 80,
        Ground = ground ?? y, Bounds = new Rectangle((int)x - 70, (int)y - 60, 140, 70),
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
        RoostChecks(k, art);
        return $"방석·집: 20개 (방석 10·집 10), 47종 가족 표, 고양이 집 cattree+box {catShare:P0}, 펭귄 igloo {peng.GetValueOrDefault("igloo") / 2000.0:P0}, 줄 것·저장 칸, 그림 20개 × 배율 2·3·4·6, 낮잠·잠·집 들어가기·깨기·끌기·활성 창·들기, 프레임 메모리 늘지 않음, 창 위 놓기(붙기·따라가기·떨어지기·불러오면 바닥·펫이 올라가기), 들어 놓은 펫 8~12초 머물기";
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

    // ── 창 위 놓기·창 위 물건으로 가기·들어 놓은 펫 머물기 (가짜 창 목록으로, Sim.cs 의 창 위 낮잠 검사처럼) ──
    static void RoostChecks(Tuning k, HomeArt art)
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        double floor = area.Bottom - k.FloorGap;
        var wh = new IntPtr(5);
        var round = art.Find("round")!;
        var kennel = art.Find("kennel")!;
        int perch = HomeRoost.Perch(3);

        // 가짜 창 하나(wh)와 그 앞에 끼울 수 있는 다른 창들. 창을 옮기거나 닫는 것은 win·alive·minimized 를 바꿔서
        var win = new Rectangle(400, 400, 800, 500);
        bool alive = true, minimized = false;
        var infront = new List<Win>();
        Brain Make()
        {
            var b = new Brain(new Pet { Stage = "adult_sweet", Full = 90, Mood = 90, Energy = 90 }, k, area);
            b.Windows = () => { var l = new List<Win>(infront) { new(wh, win, false) }; return l; };
            b.Probe = _ => new WinState(alive, minimized, win);
            return b;
        }
        double now = 0;
        RoostChange Step(HomeRoost r, HomeItem it, double sec, int kk = 3)
        {
            var all = RoostChange.None;
            for (int i = 0; i < Math.Round(sec * 60); i++) { now += 1 / 60.0; all |= r.Tick(now, 1 / 60.0, it, kk); }
            return all;
        }
        double bottom = win.Top + perch + 12; // 윗변 근처 (오차 12px — 18px 안)

        // 붙기: 윗변 가까이 놓으면 그 창 위, 멀면 바닥, 위에 공간이 없거나 최대화·항상 위·좁은 창·덮인 창은 안 붙는다
        {
            var b = Make();
            var r = new HomeRoost(b);
            Expect(r.Snap(round, 3, 800, bottom) && r.OnWindow && r.H == wh, "윗변 가까이 놓으면 그 창 위에 붙는다");
            Expect(Math.Abs(r.CenterX - 800) < .5 && Math.Abs(r.FloorY - (win.Top + perch)) < .5 && Math.Abs(r.Dx - 400) < .5, "붙은 자리: 가운데 x 그대로, 발 자리가 윗변 + 3칸");
            Expect(!new HomeRoost(b).Snap(round, 3, 800, win.Top + 200), "윗변에서 멀면 바닥 (붙지 않는다)");
            Expect(!new HomeRoost(b).Snap(round, 3, 1300, bottom), "창 가로 밖이면 안 붙는다");
            Expect(new HomeRoost(b).Snap(round, 3, 800, win.Top + perch - 15) && new HomeRoost(b).Snap(round, 3, 800, win.Top + perch + 15), "윗변 위아래 6칸(18px) 안이면 붙는다");
            Expect(!new HomeRoost(b).Snap(round, 3, 800, win.Top + perch + 24), "6칸을 넘으면 안 붙는다");
            var savedWin = win;
            win = new Rectangle(0, 0, 1920, 1030);
            Expect(!new HomeRoost(b).Snap(round, 3, 800, win.Top + perch), "최대화한 창 위에는 안 놓인다");
            win = new Rectangle(400, 40, 800, 500);
            Expect(!new HomeRoost(b).Snap(round, 3, 800, win.Top + perch), "위에 그림이 들 공간이 없으면 안 붙는다");
            win = new Rectangle(400, 400, 100, 500);
            Expect(!new HomeRoost(b).Snap(round, 3, 450, win.Top + perch), "그림보다 좁은 창에는 안 붙는다");
            win = savedWin;
            b.Windows = () => new List<Win> { new(wh, win, true) };
            Expect(!new HomeRoost(b).Snap(round, 3, 800, bottom), "항상 위 창에는 안 붙는다");
            b.Windows = () => new List<Win> { new(new IntPtr(9), new Rectangle(700, 250, 300, 200), false), new(wh, win, false) };
            Expect(!new HomeRoost(b).Snap(round, 3, 800, bottom), "윗변이 다른 창에 덮여 있으면 안 붙는다");
            b.Windows = () => new List<Win> { new(new IntPtr(9), new Rectangle(1300, 250, 300, 200), false), new(wh, win, false) };
            Expect(new HomeRoost(b).Snap(round, 3, 800, bottom), "덮는 창이 다른 자리면 붙는다");
            // 창이 둘일 때(앞 창 윗변이 420, 뒤 창 윗변이 400) 윗변이 더 가까운 쪽
            var front = new IntPtr(6);
            b.Windows = () => new List<Win> { new(front, new Rectangle(400, 420, 800, 400), false), new(wh, win, false) };
            var two = new HomeRoost(b);
            Expect(two.Snap(round, 3, 800, 420 + perch + 4) && two.H == front, "여러 창이면 윗변이 더 가까운 창에 (앞 창)");
            Expect(two.Snap(round, 3, 800, 400 + perch + 3) && two.H == wh, "여러 창이면 윗변이 더 가까운 창에 (뒤 창, 윗변이 안 덮였을 때)");
        }

        // 따라가기: 창을 옮기면 같은 자리에 실려 가고, 화면 밖으로 나가려 하면 창·화면 안쪽에 머문다
        {
            var b = Make();
            var r = new HomeRoost(b);
            Expect(r.Snap(round, 3, 800, bottom), "따라가기 준비");
            win.Offset(300, -100);
            Step(r, round, .2);
            Expect(r.OnWindow && Math.Abs(r.CenterX - (win.Left + 400)) < .5 && Math.Abs(r.FloorY - (win.Top + perch)) < .5, $"창을 옮기면 같은 자리에 실려 간다 ({r.CenterX:0}, {r.FloorY:0})");
            win = new Rectangle(1500, 300, 800, 500); // 오른쪽 화면 밖으로 반쯤
            Step(r, round, .2);
            Expect(r.OnWindow && r.CenterX + round.W * 3 / 2.0 <= area.Right + .5 && r.CenterX - round.W * 3 / 2.0 >= win.Left - .5, $"창이 화면 밖으로 나가도 그림은 화면 안·창 위에 ({r.CenterX:0})");
            win = new Rectangle(1900, 300, 800, 500); // 거의 다 나갔다: 그림이 들 자리가 없다
            Step(r, round, .2);
            Expect(!r.OnWindow && r.Falling, "창이 거의 다 화면 밖이면 떨어진다");
            Step(r, round, 3);
            Expect(!r.Active && r.FloorY == floor && r.CenterX + round.W * 3 / 2.0 <= area.Right + .5, "떨어진 뒤 화면 안 바닥에 있다");
        }

        // 떨어지기: 창이 닫히면·접히면·덮이면·자리가 없어지면 같은 x 의 바닥으로 (짧게 떨어졌다 통 튄다)
        {
            double start = 0;
            void Case(string what, Action<Brain> make, double wait)
            {
                win = new Rectangle(400, 400, 800, 500);
                (alive, minimized) = (true, false);
                infront.Clear();
                var b = Make();
                var r = new HomeRoost(b);
                Expect(r.Snap(round, 3, 700, win.Top + perch), $"{what}: 준비");
                start = r.CenterX;
                Step(r, round, .8);
                Expect(r.OnWindow, $"{what}: 아무 일 없으면 창 위에 그대로");
                make(b);
                var got = Step(r, round, wait);
                Expect((got & RoostChange.Fell) != 0 && !r.OnWindow, $"{what}: 떨어진다");
                double y0 = r.FloorY;
                Expect(y0 < floor || r.Falling, $"{what}: 떨어지기 시작은 창 높이");
                got |= Step(r, round, 3);
                Expect((got & RoostChange.Landed) != 0 && !r.Active && r.FloorY == floor && Math.Abs(r.CenterX - start) < .5, $"{what}: 같은 x 의 바닥에 내려앉는다 ({r.CenterX:0} vs {start:0}, y {r.FloorY:0})");
            }
            Case("창이 닫힘", _ => alive = false, .2);
            Case("창이 접힘", _ => minimized = true, .2);
            Case("다른 창이 덮음", _ => infront.Add(new Win(new IntPtr(9), new Rectangle(600, 250, 300, 200), false)), HomeRoost.CoverSec + .3);
            Case("창이 아래로 내려가 자리가 없음", _ => win = new Rectangle(400, 1040, 800, 500), .2);
            Case("창이 위로 올라가 자리가 없음", _ => win = new Rectangle(400, 60, 800, 500), .2);
            Case("창이 항상 위가 아닌 채 최대화", _ => win = new Rectangle(0, 0, 1920, 1030), .2);
            win = new Rectangle(400, 400, 800, 500); (alive, minimized) = (true, false); infront.Clear();
        }

        // 떨어지는 동안 높이는 단조롭게 내려오다 바닥에서 한 번 튄다 (프레임 사이 점프가 없다)
        {
            var b = Make();
            var r = new HomeRoost(b);
            r.Snap(round, 3, 700, win.Top + perch);
            alive = false;
            Step(r, round, .1);
            double prev = r.FloorY, maxJump = 0;
            int frames = 0;
            while (r.Falling && frames++ < 600) { Step(r, round, 1 / 60.0); maxJump = Math.Max(maxJump, Math.Abs(r.FloorY - prev)); prev = r.FloorY; }
            Expect(frames < 300 && maxJump < 60, $"떨어지는 데 5초 안 걸리고 프레임당 60px 넘게 뛰지 않는다 ({frames}프레임, 최대 {maxJump:0}px)");
            alive = true;
        }

        // 프레임마다 새로 잡는 메모리 없음 (창 위에 가만히 있는 동안)
        {
            var b = Make();
            b.Windows = () => new List<Win> { new(wh, win, false) };
            var r = new HomeRoost(b);
            r.Snap(round, 3, 700, win.Top + perch);
            Step(r, round, 1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Step(r, round, .5); // 덮임 확인(0.6초)이 끼지 않는 짧은 구간
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Expect(bytes < 2048 && r.OnWindow, $"창 위에 가만히 있을 때 프레임 메모리 ({bytes}B / 30프레임, 창 목록 확인은 0.6초에 한 번)");
        }

        // 불러오면 바닥: 창 위였어도 저장 칸에는 그 x 의 바닥 자리만, 불러오면 창 위가 아니다. 옛 저장도 그대로 읽힌다
        {
            var b = Make();
            var home = new HomeLife(b, () => art, () => null!, () => "cat", _ => 3, () => false, () => false, () => { });
            home.Book.Place(false, "round");
            var s = home.Cushion;
            s.Item = round;
            Expect(s.Roost.Snap(round, 3, 800, bottom), "저장 검사: 창 위에 놓았다");
            (s.CenterX, s.FloorY, s.Area) = (s.Roost.CenterX, s.Roost.FloorY, s.Roost.Area);
            win.Offset(100, 0);
            Step(s.Roost, round, .2);
            (s.CenterX, s.FloorY) = (s.Roost.CenterX, s.Roost.FloorY);
            var snap = home.Snapshot();
            Expect(snap.CushionAt is { Length: 2 } at && Math.Abs(at[0] - (win.Left + 400)) < .5 && at[1] == floor, $"창 위의 물건은 지금 x 의 바닥 자리로 저장된다 ({snap.CushionAt?[0]:0}, {snap.CushionAt?[1]:0})");
            var json = JsonSerializer.Serialize(snap);
            var again = new HomeLife(Make(), () => art, () => null!, () => "cat", _ => 3, () => false, () => false, () => { });
            again.Cushion.Roost.Snap(round, 3, 800, bottom); // 불러오기 전에 창 위였어도
            again.Load(JsonSerializer.Deserialize<HomeSave>(json));
            Expect(!again.Cushion.Roost.Active && again.Book.CushionAt is { Length: 2 } a2 && a2[1] == floor && Math.Abs(a2[0] - snap.CushionAt![0]) < .5, "불러오면 바닥에 (저장한 x 그대로)");
            again.Load(JsonSerializer.Deserialize<HomeSave>("{\"Got\":[\"round\"],\"Cushion\":\"round\",\"CushionAt\":[300,1030],\"CushionGiven\":true,\"HouseGiven\":false}"));
            Expect(again.Book.CushionAt![0] == 300 && !again.Cushion.Roost.Active, "창 칸이 없던 옛 저장도 그대로 읽힌다");
            home.Dispose(); again.Dispose();
            win = new Rectangle(400, 400, 800, 500);
        }

        // 펫이 창 위 물건으로 간다: 바닥에서 물건 밑까지 걸어가 곧게 올라가고, 쓰고, 나올 때 뛰어 내려온다
        {
            double top = 520, x = 1000;
            foreach (var (house, use, stage) in new[] { (false, HomeUse.Sit, "child"), (true, HomeUse.Peek, "adult_sweet"), (false, HomeUse.Nap, "child"), (true, HomeUse.Sleep, "adult_chubby") })
            {
                var b = new Brain(new Pet { Stage = stage, Full = 90, Mood = 90, Energy = use is HomeUse.Nap or HomeUse.Sleep ? 40 : 90 }, k, area) { X = 300, Y = floor };
                b.NightNow = () => false;
                var sp = Spot(x, top, house, false, floor);
                if (house) b.House = sp; else b.Cushion = sp;
                Expect(sp.Elevated, "창 위 자리는 Elevated");
                Expect(b.GoHome(house, use, stay: 2), $"{use}: 창 위 물건으로 갈 수 있다");
                double gx = use is HomeUse.Sleep or HomeUse.Nap ? sp.SleepX : sp.X;
                bool onFloorUntilUnder = true, climbed = false;
                for (int i = 0; i < 60 * 30 && b.Mode == Mode.GoHome; i++)
                {
                    b.Update(1 / 60.0, false);
                    if (Math.Abs(b.X - gx) > 8 && b.Y < floor - 4) onFloorUntilUnder = false;
                    if (b.Y < floor - 20) climbed = true;
                }
                Expect(onFloorUntilUnder && climbed, $"{use}: 바닥에서 물건 밑까지 걸어간 뒤 올라간다 (바닥 유지 {onFloorUntilUnder}, 올라감 {climbed})");
                Expect(b.Mode == Mode.Home && Math.Abs(b.Y - (use is HomeUse.Sleep or HomeUse.Nap ? top + sp.SleepDy : top)) < 12 && Math.Abs(b.X - gx) < 4, $"{use}: 창 위 물건에 도착해 쓴다 (모드 {b.Mode}, y {b.Y:0})");
                if (house) Expect(b.HomeClipNow == HomeClipKind.Inside || b.HomeClipNow == HomeClipKind.Front, $"{use}: 집 오림을 켠다");
                if (use is HomeUse.Nap or HomeUse.Sleep) { b.P.Energy = 100; b.P.Asleep = false; }
                bool jumped = false;
                for (int i = 0; i < 60 * 12 && !(b.Mode == Mode.Wander && Math.Abs(b.Y - floor) < 2); i++) { b.Update(1 / 60.0, false); jumped |= b.Mode == Mode.Thrown; if (b.Mode == Mode.Anim && Math.Abs(b.Y - floor) < 2) break; }
                Expect(jumped && Math.Abs(b.Y - floor) < 2 && b.Mode != Mode.Home, $"{use}: 나올 때 뛰어 내려와 바닥에 선다 (모드 {b.Mode}, y {b.Y:0})");
            }
            // 창 위에서 물건이 없어지면 공중에 안 뜨고 떨어진다
            var c = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 90 }, k, area) { X = 300, Y = floor };
            c.Cushion = Spot(x, top, false, false, floor);
            c.GoHome(false, HomeUse.Sit, stay: 60);
            for (int i = 0; i < 60 * 30 && c.Mode != Mode.Home; i++) c.Update(1 / 60.0, false);
            c.Cushion.On = false;
            bool landed = false;   // 떨어져 바닥에 한 번 닿는다 (그 뒤엔 바탕화면을 마음대로 돌아다녀도 된다 — 예전 검사는 6초 뒤에도 바닥 줄에 있기를 바라서 가끔 떨어졌다)
            for (int i = 0; i < 60 * 6 && !landed; i++) { c.Update(1 / 60.0, false); landed = c.Mode is not (Mode.Home or Mode.Thrown) && Math.Abs(c.Y - floor) < 2; }
            Expect(landed && c.Mode != Mode.Home, $"창 위 물건이 없어지면 펫은 떨어져 바닥에 선다 (모드 {c.Mode}, 자세 {c.Pose}, y {c.Y:0} / 바닥 {floor})");
            // 창이 움직이면 물건을 쓰는 펫도 같이
            var d = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 90 }, k, area) { X = 300, Y = floor };
            d.Cushion = Spot(x, top, false, false, floor);
            d.GoHome(false, HomeUse.Sit, stay: 60);
            for (int i = 0; i < 60 * 30 && d.Mode != Mode.Home; i++) d.Update(1 / 60.0, false);
            d.Cushion = Spot(x + 200, top + 100, false, false, floor);
            d.Update(1 / 60.0, false);
            Expect(d.Mode == Mode.Home && Math.Abs(d.X - d.Cushion.X) < 2 && Math.Abs(d.Y - (d.Cushion.Y + d.Cushion.SitDy)) < 2, "창이 움직이면 물건 위의 펫도 같이 간다");
        }

        // 그림 자리: 창 위 물건의 문 오림·앞 겹이 그림 자리를 그대로 따라간다
        {
            var onFloor = HomeGeom.Origin(kennel, 3, 1000, floor);
            var onWin = HomeGeom.Origin(kennel, 3, 1000, 520);
            var sp = HomeGeom.Spot(kennel, 3, onWin, 520, 20.5, 22, false, floor);
            Expect(sp.Elevated && Math.Abs(sp.Y - 520) < 1 && sp.Bounds.Y == onWin.Y && sp.Bounds.Width == kennel.W * 3, "창 위 집: 자리가 높고 Bounds 가 그림 자리");
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            using var rf = HomeGeom.Clip(art, kennel, 3, onFloor, HomeClipKind.Inside, false, 1000, floor, 3);
            using var rw = HomeGeom.Clip(art, kennel, 3, onWin, HomeClipKind.Inside, false, 1000, 520, 3);
            var (bf, bw) = (rf.GetBounds(g), rw.GetBounds(g));
            Expect(Math.Abs((bw.Y - bf.Y) - (onWin.Y - onFloor.Y)) < 1.01 && Math.Abs(bw.X - bf.X) < 1.01, "문 오림이 그림 자리를 따라 높이만 옮겨 간다");
            var d = kennel.Door[kennel.Door.Length / 2];
            Expect(!rw.IsVisible(onWin.X + (d.X0 + d.X1) / 2 * 3 + 1, onWin.Y + d.Y * 3 + 1), "문 안은 오려지지 않는다 (펫이 보인다)");
        }

        // 들어 놓은 펫: 방석·집 위에 놓으면 8~12초 머문 뒤 나온다. 누르거나 끌면 바로 나온다. 억지로 재우지 않는다
        {
            double Visit(string stage, double energy, bool house, HomeUse expect, out bool asleep, Action<Brain>? during = null)
            {
                var b = new Brain(new Pet { Stage = stage, Full = 90, Mood = 90, Energy = energy }, k, area) { X = 400, Y = floor };
                b.NightNow = () => false;
                var sp = Spot(1000, floor, house, false);
                if (house) b.House = sp; else b.Cushion = sp;
                b.Lift();
                b.Mouse = new Point(1000, (int)floor - 30);
                b.Update(1 / 60.0, false);
                Expect(b.Mode == Mode.Held, "들었다");
                b.Drop();
                Expect(b.Mode == Mode.Home && b.AtHome == expect, $"{expect}: 놓으면 바로 그 안에 (모드 {b.Mode}, {b.AtHome})");
                Expect(Math.Abs(b.X - (expect is HomeUse.Nap ? sp.SleepX : sp.X)) < 2, $"{expect}: 물건 자리에서 시작");
                double e0 = b.P.Energy;
                asleep = b.P.Asleep;
                if (house) Expect(b.HomeClipNow == HomeClipKind.Inside, "집이면 문 안 오림");
                double t = 0;
                for (; t < 20 && b.Mode == Mode.Home; t += 1 / 60.0)
                {
                    b.Update(1 / 60.0, false);
                    if (t < 7.9) Expect(b.Mode == Mode.Home, $"{expect}: 8초 안에 나오지 않는다 ({t:0.0}초)");
                }
                Expect(b.Mode != Mode.Home && !b.P.Asleep && b.HomeClipNow == HomeClipKind.None, $"{expect}: 머문 뒤 나온다, 깨어 있다 ({b.Mode}, 잠 {b.P.Asleep})");
                Expect(b.P.Energy <= e0 + 1.5, $"{expect}: 수치가 크게 늘지 않는다 (기운 {e0:0.0} → {b.P.Energy:0.0})");
                during?.Invoke(b);
                return t;
            }
            double t1 = Visit("child", 90, false, HomeUse.Sit, out bool s1);
            Expect(!s1 && t1 is >= 7.9 and <= 12.3, $"방석에 놓으면 앉아서 8~12초 ({t1:0.0}초, 잠 {s1})");
            double t2 = Visit("adult_sweet", 90, true, HomeUse.Peek, out bool s2);
            Expect(!s2 && t2 is >= 7.9 and <= 12.3, $"집에 놓으면 빼꼼 8~12초 ({t2:0.0}초)");
            double t3 = Visit("child", 40, false, HomeUse.Nap, out bool s3);
            Expect(s3 && t3 is >= 7.9 and <= 14, $"기운이 낮으면 방석에서 낮잠, 그래도 8~12초 뒤 깨서 나온다 ({t3:0.0}초)");
            // 8~12초 사이에서 정해진다 (여러 번)
            double lo = 99, hi = 0;
            for (int i = 0; i < 12; i++) { double t = Visit("child", 90, false, HomeUse.Sit, out _); lo = Math.Min(lo, t); hi = Math.Max(hi, t); }
            Expect(lo >= 7.9 && hi <= 12.3 && hi - lo > .5, $"머무는 시간이 8~12초 사이에서 달라진다 ({lo:0.0}~{hi:0.0}초)");

            // 눌러서 바로 나온다 / 다시 끌면 바로 나온다
            Brain Inside(bool house)
            {
                var b = new Brain(new Pet { Stage = "adult_sweet", Full = 90, Mood = 90, Energy = 90 }, k, area) { X = 400, Y = floor };
                var sp = Spot(1000, floor, house, false);
                if (house) b.House = sp; else b.Cushion = sp;
                b.Lift(); b.Mouse = new Point(1000, (int)floor - 30); b.Update(1 / 60.0, false); b.Drop();
                for (int i = 0; i < 60 * 2; i++) b.Update(1 / 60.0, false);
                Expect(b.Mode == Mode.Home, "머무는 중");
                return b;
            }
            foreach (bool house in new[] { false, true })
            {
                var b = Inside(house);
                b.Click(20, 20);
                for (int i = 0; i < 60 * 3 && b.Mode == Mode.Home; i++) b.Update(1 / 60.0, false);
                Expect(b.Mode != Mode.Home, $"{(house ? "집" : "방석")}: 눌러서 바로 나온다 (2초 머문 뒤 눌렀다)");
                var c = Inside(house);
                c.Lift();
                Expect(c.Mode == Mode.Held && c.AtHome == HomeUse.None && c.HomeClipNow == HomeClipKind.None, $"{(house ? "집" : "방석")}: 다시 끌면 바로 나온다");
            }
            // 자고 있을 때 누르면 깨서 나온다
            var nap = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 40 }, k, area) { X = 400, Y = floor };
            nap.Cushion = Spot(1000, floor, false);
            nap.Lift(); nap.Mouse = new Point(1000, (int)floor - 30); nap.Update(1 / 60.0, false); nap.Drop();
            for (int i = 0; i < 60 * 2; i++) nap.Update(1 / 60.0, false);
            nap.Care("wake");
            for (int i = 0; i < 60 * 3 && nap.Mode == Mode.Home; i++) nap.Update(1 / 60.0, false);
            Expect(nap.Mode != Mode.Home && !nap.P.Asleep, "낮잠 중에 눌러 깨우면 나온다");

            // 물건 밖에 놓으면 그냥 놓인다. 멀리 던지면 들어가지 않는다. 알·아기·자는 펫·끄는 중인 물건은 안 들어간다
            var out1 = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 90 }, k, area) { X = 400, Y = floor };
            out1.Cushion = Spot(1000, floor, false);
            out1.Lift(); out1.Mouse = new Point(500, (int)floor - 30); out1.Update(1 / 60.0, false); out1.Drop();
            Expect(out1.Mode == Mode.Wander, "물건 밖에 놓으면 그냥 선다");
            var fling = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 90 }, k, area) { X = 400, Y = floor };
            fling.Cushion = Spot(1000, floor, false);
            fling.Lift();
            for (int i = 0; i < 6; i++) { fling.Mouse = new Point(700 + i * 60, (int)floor - 30); fling.Update(1 / 60.0, false); }
            fling.Drop();
            Expect(fling.Mode == Mode.Thrown, "세게 던지면 물건 위여도 날아간다");
            var moving = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 90 }, k, area) { X = 400, Y = floor };
            var msp = Spot(1000, floor, false); msp.Moving = true; moving.Cushion = msp;
            moving.Lift(); moving.Mouse = new Point(1000, (int)floor - 30); moving.Update(1 / 60.0, false); moving.Drop();
            Expect(moving.Mode == Mode.Wander, "끄는 중인 물건에는 안 들어간다");
            var baby = new Brain(new Pet { Stage = "baby", Full = 90, Mood = 90, Energy = 90 }, k, area) { X = 400, Y = floor };
            baby.Cushion = Spot(1000, floor, false);
            baby.Lift(); baby.Mouse = new Point(1000, (int)floor - 30); baby.Update(1 / 60.0, false); baby.Drop();
            Expect(baby.Mode == Mode.Wander, "아기는 안 들어간다");
            // 창 위 물건에 놓아도 들어가고, 나올 땐 뛰어 내린다
            var up = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 90 }, k, area) { X = 400, Y = floor };
            up.Cushion = Spot(1000, 520, false, false, floor);
            up.Lift(); up.Mouse = new Point(1000, 500); up.Update(1 / 60.0, false); up.Drop();
            up.Update(1 / 60.0, false);
            Expect(up.Mode == Mode.Home && Math.Abs(up.Y - (520 + up.Cushion.SitDy)) < 2, "창 위 방석에 놓아도 들어간다");
            bool jumped = false;
            for (int i = 0; i < 60 * 20; i++) { up.Update(1 / 60.0, false); jumped |= up.Mode == Mode.Thrown; }
            Expect(jumped, "창 위 방석에서 나올 땐 뛰어 내린다");
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
