using System.Diagnostics;
using System.Text.Json;

namespace Bolok;

// 방석·집 자가 검사 (--selfcheck, docs/home.md 11장)와 속도 재기 (--home-bench)
static class HomeCheck
{
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: 방석·집 — " + what); }

    // 그림 자리(Bounds)는 가로 140·세로 70 쯤 (방석 크기)
    static HomeSpot Spot(double x, double y, bool house, bool top = false) => new()
    {
        On = true, X = x, Y = y, SleepX = x + 1, SleepDy = house ? 9 : 0, SitDy = house ? 0 : -6, HasTop = top, TopX = x + 40, TopY = y - 80,
        Bounds = new Rectangle((int)x - 70, (int)y - 60, 140, 70),
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
        book.Place("round"); book.Place("kennel");
        Expect(book.Active == "kennel" && book.House == "kennel" && book.Cushion == null && book.Got.Count == 2, "하나만 놓인다: 집을 놓으면 방석이 빠지고, 받은 것은 둘 다 남는다");
        (book.CushionGiven, book.HouseGiven) = (true, true);
        book.NewPet();
        Expect(HomeBook.Owed("child", book.CushionGiven, book.HouseGiven) == "cushion" && book.Got.Count == 2 && book.Active == "kennel", "새 알 뒤엔 다시 (수집·놓인 것은 그대로)");

        // ── 저장 ──
        var save = new HomeBook();
        save.Place("leaf"); save.Place("teepee"); save.Receive("egg");
        (save.At, save.CushionGiven, save.HouseGiven, save.Hidden) = (new[] { 400.5, 900 }, true, false, true);
        var json = JsonSerializer.Serialize(save.Snapshot());
        var back = new HomeBook();
        back.Load(JsonSerializer.Deserialize<HomeSave>(json));
        Expect(JsonSerializer.Serialize(back.Snapshot()) == json && back.Got.SequenceEqual(new[] { "leaf", "teepee", "egg" }) && back.Active == "teepee" && back.At is { Length: 2 }, "HomeSave 를 JSON 으로 썼다 읽으면 같다 (놓인 id 하나·자리 [x, y] 하나)");
        Expect(!json.Contains("\"CushionAt\":[") && !json.Contains("\"HouseAt\":["), "새 저장에는 옛 방석·집 따로 자리가 안 쓰인다");
        Expect(JsonSerializer.Deserialize<SaveData>("{\"X\":3}")!.Home == null, "칸이 없는 옛 저장은 null");
        var old = new HomeBook();
        old.Load(null);
        Expect(old.Got.Count == 0 && !old.CushionGiven && !old.HouseGiven, "칸이 없으면 빈 수집");
        old.Load(new HomeSave(new List<string> { "round", "없어진물건", "kennel" }, true, true, Cushion: "없어진물건", House: "round"));
        Expect(old.Got.SequenceEqual(new[] { "round", "kennel" }) && old.Active == null, "모르는 id 는 읽을 때 빠지고, 집 자리에 방석 id 가 있으면 무시");
        old.Load(new HomeSave(new List<string> { "round" }, true, true, Active: "없어진물건", At: new[] { 1.0, 2.0 }));
        Expect(old.Active == null && old.Got.Count == 1, "놓인 것이 모르는 id 면 없음");

        // 옛 저장(방석·집이 따로 놓이던 것) → 하나로: 둘 다 있으면 집(어른)을 그 집 자리에, 하나뿐이면 그것을 제 자리에
        {
            var m = new HomeBook();
            m.Load(JsonSerializer.Deserialize<HomeSave>("{\"Got\":[\"round\",\"kennel\"],\"Cushion\":\"round\",\"House\":\"kennel\",\"CushionAt\":[300,900],\"HouseAt\":[1200.5,880],\"CushionGiven\":true,\"HouseGiven\":true,\"Hidden\":true}"));
            Expect(m.Active == "kennel" && m.At is { } a1 && a1[0] == 1200.5 && a1[1] == 880 && m.Got.Count == 2 && m.Hidden, "옛 저장: 방석과 집이 다 있었으면 집이 그 집 자리에 남고, 방석은 수집에만");
            m.Load(JsonSerializer.Deserialize<HomeSave>("{\"Got\":[\"round\"],\"Cushion\":\"round\",\"CushionAt\":[300,900],\"CushionGiven\":true,\"HouseGiven\":false}"));
            Expect(m.Active == "round" && m.At is { } a2 && a2[0] == 300 && a2[1] == 900, "옛 저장: 방석만 있었으면 그 방석 그 자리");
            m.Load(JsonSerializer.Deserialize<HomeSave>("{\"Got\":[\"kennel\"],\"House\":\"kennel\",\"HouseAt\":[700,880],\"CushionGiven\":true,\"HouseGiven\":true}"));
            Expect(m.Active == "kennel" && m.At is { } a3 && a3[0] == 700, "옛 저장: 집만 있었으면 그 집 그 자리");
            m.Load(JsonSerializer.Deserialize<HomeSave>("{\"Got\":[\"round\",\"kennel\"],\"Cushion\":\"round\",\"House\":\"kennel\",\"CushionAt\":[300,900],\"CushionGiven\":true,\"HouseGiven\":true}"));
            Expect(m.Active == "kennel" && m.At is { } a4 && a4[0] == 300, "옛 저장: 집 자리가 없으면 방석 자리로");
            m.Load(JsonSerializer.Deserialize<HomeSave>("{\"Got\":[\"round\"],\"CushionGiven\":true,\"HouseGiven\":false}"));
            Expect(m.Active == null && m.At == null && m.Got.Count == 1, "옛 저장: 놓인 것이 없었으면 없음");
            var again = JsonSerializer.Serialize(new HomeBook { Active = "kennel", At = new[] { 1.0, 2.0 } }.Snapshot());
            var m2 = new HomeBook();
            m2.Load(JsonSerializer.Deserialize<HomeSave>(JsonSerializer.Serialize(m.Snapshot())));
            Expect(m2.Active == m.Active && again.Contains("\"Active\":\"kennel\""), "옮긴 저장을 다시 쓰고 읽어도 같다");
        }

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
        FreeChecks(k, art);
        return $"방석·집: 20개 (방석 10·집 10), 47종 가족 표, 고양이 집 cattree+box {catShare:P0}, 펭귄 igloo {peng.GetValueOrDefault("igloo") / 2000.0:P0}, 줄 것·저장 칸, 그림 20개 × 배율 2·3·4·6, 낮잠·잠·집 들어가기·깨기·끌기·활성 창·들기, 프레임 메모리 늘지 않음, 자유롭게 놓기(어디든·모니터 안으로 눌림·여러 모니터·모니터 빠짐·저장 x y·옛 저장·펫이 아무 자리로 가기·끌 때 펫이 같이·창 순서), 들어 놓은 펫 8~12초 머물기, 화면엔 하나만(한 번에 하나만 보임·어른이 집을 받으면 방석 자리에 바뀜·메뉴로 방석↔집·낮잠·밤잠·자리 비움 잠·앉기·들어 놓기가 다 놓인 하나로·옛 저장 하나로 옮기기)";
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

        // 아기는 안 간다. 화면엔 하나뿐이라 어린이도 놓인 집을 쓴다 (메뉴에서 집을 고른 어린이 — 캣타워 위는 어른만)
        {
            var b = Make("baby", 20);
            b.Cushion = Spot(900, floor, false);
            Expect(!b.GoHome(false, HomeUse.Nap) && !b.GoSleepHome(), "아기는 방석에 안 간다");
            var c = Make("child", 20);
            c.House = Spot(1200, floor, true, true);
            Expect(c.GoSleepHome(), "어린이도 놓인 집으로 가서 잔다 (놓인 하나를 쓴다)");
            for (int i = 0; i < 60 * 40 && !(c.Mode == Mode.Home && c.Pose == "sleep"); i++) c.Update(1 / 60.0, false);
            Run(c, 1);
            Expect(c.Mode == Mode.Home && c.AtHome == HomeUse.Sleep && Math.Abs(c.Y - (c.House.Y + c.House.SleepDy)) < 2 && c.HomeClipNow == HomeClipKind.Inside, "어린이는 캣타워여도 위가 아니라 구멍 안에서 잔다");
        }

        // 캣타워 어른은 위로 올라가 잔다
        {
            var b = Make("adult_sweet", 80, 300);
            b.House = Spot(1000, floor, true, true);
            b.Update(1 / 60.0, true);
            for (int i = 0; i < 60 * 20 && !(b.Mode == Mode.Home && b.Pose == "sleep"); i++) b.Update(1 / 60.0, true);
            Expect(b.Mode == Mode.Home && Math.Abs(b.Y - b.House.TopY) < 2 && b.HomeClipNow == HomeClipKind.Front, "캣타워 어른은 위 방석에서 잔다 (앞 겹만)");
        }

        // 낮잠 고르기 무게: 놓인 하나(방석이든 집이든)와 창 위가 다 있을 때 400번 → 놓인 것 65~85% (3:1), 밤엔 6:1 → 80~95%
        {
            var mid = new Rectangle(400, 300, 900, 600);
            foreach (bool house in new[] { false, true })
            {
                int home = 0, perch = 0, night = 0, nightPerch = 0;
                for (int i = 0; i < 400; i++)
                    foreach (bool n in new[] { false, true })
                    {
                        var b = Make("adult_chubby", 40);
                        b.NightNow = () => n;
                        b.Windows = () => new List<Win> { new(new IntPtr(1), mid, false) };
                        if (house) b.House = Spot(1500, floor, true); else b.Cushion = Spot(1500, floor, false);
                        if (!b.TryRest()) continue;
                        if (b.Mode == Mode.GoHome) { Expect(b.HomeIsHouse == house, "놓인 쪽으로만 간다"); if (n) night++; else home++; }
                        else if (b.Mode == Mode.GoNap) { if (n) nightPerch++; else perch++; }
                    }
                string what = house ? "집" : "방석";
                double share = home / (double)Math.Max(1, home + perch), nshare = night / (double)Math.Max(1, night + nightPerch);
                Expect(share is > .65 and < .85, $"{what} 과 창 위가 다 있을 때 {what} 65~85% ({share:P0})");
                Expect(nshare is > .80 and < .95, $"밤엔 {what} 을 더 쓴다 (놓인 것 6 : 창 위 1) 80~95% ({nshare:P0})");
            }
        }

        // 놓인 하나로 밤잠·자리 비움 잠·낮잠이 다 간다 (방석도 밤에 잔다)
        {
            var c = Make("child", 70);
            c.NightNow = () => true;
            c.Cushion = Spot(1100, floor, false);
            Expect(c.GoSleepHome(), "밤 인사 뒤: 방석으로 간다");
            for (int i = 0; i < 60 * 40 && !(c.Mode == Mode.Home && c.P.Asleep); i++) c.Update(1 / 60.0, false);
            Expect(c.Mode == Mode.Home && c.AtHome == HomeUse.Nap && c.P.Asleep && !c.HomeIsHouse && c.HomeClipNow == HomeClipKind.Front, "방석 위에서 밤잠");
            var d = Make("child", 80, 300);
            d.Cushion = Spot(1100, floor, false);
            d.Update(1 / 60.0, true);
            Expect(d.Mode == Mode.GoHome && d.P.Asleep && !d.HomeIsHouse, "자리 비움으로 잠들면 놓인 방석으로 걷는다");
            for (int i = 0; i < 60 * 20 && d.Mode != Mode.Home; i++) d.Update(1 / 60.0, true);
            Expect(d.Mode == Mode.Home && d.AtHome == HomeUse.Nap && d.P.Asleep, "자리 비움 잠은 방석 위에서");
            var e = Make("adult_sweet", 80, 300);
            e.House = Spot(1100, floor, true);
            e.Update(1 / 60.0, true);
            Expect(e.Mode == Mode.GoHome && e.HomeIsHouse, "집이 놓였으면 자리 비움 잠은 집으로");
            // 놓인 하나가 방석이면 혼자 놀다 앉고, 집이면 문 앞에서 내다본다 (수치는 그대로)
            foreach (bool house in new[] { false, true })
            {
                var f = Make("adult_sweet", 60);   // 기운 65 이하: 앉을 확률 30%
                if (house) f.House = Spot(700, floor, true); else f.Cushion = Spot(700, floor, false);
                int went = 0;
                for (int i = 0; i < 400; i++)
                {
                    f.Mode = Mode.Wander; f.P.Asleep = false;
                    if (f.TrySit()) { Expect(f.Mode == Mode.GoHome && f.HomeIsHouse == house, "놓인 쪽으로만 간다"); went++; }
                }
                Expect(went is > 72 and < 168, $"혼자 놀다 놓인 {(house ? "집(문 앞 내다보기)" : "방석(앉기)")} 으로 간다: 30% ({went}/400)");
            }
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

    // ── 자유롭게 놓기 (추가 2): 바탕화면 아무 데나, 끌 때 펫이 같이, 창 순서. 창 없이 진짜 HomeLife(Headless)로 돌린다 ──
    sealed class Env : IDisposable
    {
        public readonly Brain B;
        public readonly HomeLife H;
        public readonly List<string> Log = new();      // 창 순서 기록: "item<번호>" = 물건 창을 맨 위로, "pet" = 그 위로 Overlay → 펫을 올림
        public int FollowCalls;
        public Func<bool>? OnFollow;                   // 끄는 동안 펫 창을 옮기라고 부를 때마다 (펫이 물건과 같이 와 있나)
        public bool FollowOk = true;

        public Env(Tuning k, Rectangle[] areas, string stage = "adult_sweet", double energy = 90, double petX = 100)
        {
            var sp = Sprites.Drawn(PetArt.Embedded, "cat", null);
            B = new Brain(new Pet { Stage = stage, Full = 90, Mood = 90, Energy = energy }, k, new World(areas), areas[0]) { Scale = 3, X = petX, Y = areas[0].Bottom - k.FloorGap };
            B.NightNow = () => false;
            B.Windows = () => new List<Win>();
            H = new HomeLife(B, () => HomeArt.Embedded, () => sp, () => "cat", _ => 3, () => false, () => false, () => { }) { Headless = true };
            H.Topmost = h => Log.Add("item" + h);
            H.RaiseAbove = () => Log.Add("pet");
            H.PetFollow = () => { FollowCalls++; if (OnFollow != null) FollowOk &= OnFollow(); };
            H.Cushion.TestHandle = new IntPtr(101);
            H.House.TestHandle = new IntPtr(102);
            (H.Book.CushionGiven, H.Book.HouseGiven) = (true, true);
        }

        // 놓인 하나를 id 로 바꾸고 그 자리에 (방석이든 집이든 하나만 놓인다)
        public void Place(string id, double x, double y)
        {
            H.Book.Place(id);
            H.Book.At = new[] { x, y };
            H.Relayout();
            H.Tick();
        }

        public void Step(double sec) { for (int i = 0, n = Math.Max(1, (int)Math.Round(sec * 60)); i < n; i++) { B.Update(1 / 60.0, false); H.Tick(); } }
        public HomeLife.Slot Slot(bool house) => house ? H.House : H.Cushion;
        public double[] At() => H.Book.At!;
        public int ShownCount => (H.Cushion.Shown ? 1 : 0) + (H.House.Shown ? 1 : 0);
        public void Dispose() => H.Dispose();
    }

    static bool Inside(Rectangle area, HomeLife.Slot s) => s.Item is { } it && new Rectangle(s.Origin.X, s.Origin.Y, it.W * s.K, it.H * s.K) is var r && area.Contains(r);

    static void FreeChecks(Tuning k, HomeArt art)
    {
        var area = new Rectangle(0, 0, 1920, 1080);
        double floor = area.Bottom - k.FloorGap;
        var round = art.Find("round")!;
        var kennel = art.Find("kennel")!;
        var A = new Rectangle(0, 0, 1920, 1040);          // 모니터 둘: 크기·높이가 다르다
        var B2 = new Rectangle(1920, 40, 1600, 900);

        // ── Fit: 어디 놓든 그림이 통째로 모니터 안, 펫이 설 자리도 안 (방석·집 20개 × 배율 2·3·4 × 모서리·가운데·바깥) ──
        {
            foreach (var it in art.Items)
                foreach (int kk in new[] { 2, 3, 4 })
                    foreach (var ar in new[] { A, B2 })
                        foreach (var (wx, wy) in new[] { (ar.Left - 500.0, ar.Top - 500.0), (ar.Left + 1.0, ar.Top + 1.0), (ar.Right + 500.0, ar.Bottom + 500.0), (ar.Left + ar.Width / 2.0, ar.Top + ar.Height / 2.0), (ar.Right - 3.0, ar.Top + 5.0), (ar.Left + 7.0, ar.Bottom - 2.0) })
                        {
                            var (x, y) = HomeGeom.Fit(it, kk, ar, k.FloorGap, wx, wy);
                            var o = HomeGeom.Origin(it, kk, x, y);
                            var rect = new Rectangle(o.X, o.Y, it.W * kk, it.H * kk);
                            Expect(ar.Contains(rect), $"{it.Id}×{kk} {ar}: ({wx:0},{wy:0}) 에 놓아도 그림이 통째로 모니터 안 ({rect})");
                            var sp = HomeGeom.Spot(it, kk, o, y, 20.5, 22, false);
                            double headTop = (it.Top != null ? sp.TopY : sp.Y + sp.SitDy) - 40 * kk;
                            Expect(sp.Y <= ar.Bottom - k.FloorGap + .5 && headTop >= ar.Top - .5, $"{it.Id}×{kk}: 펫이 서는 자리가 모니터 안 (발 {sp.Y:0}, 머리 {headTop:0})");
                            var (x2, y2) = HomeGeom.Fit(it, kk, ar, k.FloorGap, x, y);
                            Expect(x2 == x && y2 == y, $"{it.Id}×{kk}: 이미 맞춘 자리는 그대로 (두 번 맞춰도 같다)");
                        }
            var (mx, my) = HomeGeom.Fit(round, 3, area, k.FloorGap, 800, 600);
            Expect(mx == 800 && my == 600, "모니터 안이면 놓은 자리 그대로 (x·y 가 바닥이 아니어도)");
            var (fx, fy) = HomeGeom.Fit(round, 3, area, k.FloorGap, 300, 5000);
            Expect(fx == 300 && fy == floor, "너무 아래로 놓으면 바닥(예전 기본 자리)까지만");
        }

        // ── 끌기: 잡은 자리 그대로 따라오고, 놓은 자리에 그대로 선다. 저장 칸에 [x, y] ──
        {
            using var e = new Env(k, new[] { A, B2 });
            e.Place("donut", 400, 600);
            var s = e.Slot(false);
            var item = s.Item!;
            Expect(Inside(A, s) && s.Area == A && s.CenterX == 400 && s.FloorY == 600, "저장 자리 (400, 600) 가 바닥이 아니어도 그 자리에 놓인다");
            var o0 = s.Origin;
            // 그림 안 한 점을 잡는다 (가운데에서 오른쪽 20, 그림 위에서 아래 30)
            var press = new Point(o0.X + item.W * 3 / 2 + 20, o0.Y + 30);
            e.H.BeginDrag(s, 20, 30);
            Expect(s.Moving && e.B.Cushion.Moving, "끌기 시작: 끄는 중 (Brain 에 알려진다)");
            e.H.DragTo(s, press);
            Expect(s.Origin == o0, "끌기 시작 때 그림이 튀지 않는다 (잡은 자리 그대로)");
            var drop = new Point(1000, 300);
            e.H.DragTo(s, drop);
            Expect(s.Origin == new Point(drop.X - 20 - item.W * 3 / 2, drop.Y - 30), $"끄는 동안 잡은 자리가 마우스를 따른다 ({s.Origin})");
            e.H.EndDrag(s);
            Expect(!s.Moving && s.Origin == new Point(drop.X - 20 - item.W * 3 / 2, drop.Y - 30), "놓은 자리에 그대로 (바닥으로 안 떨어진다)");
            Expect(e.At()[0] == s.CenterX && e.At()[1] == s.FloorY && e.At()[1] != floor, "저장 칸에 놓은 x·y");
            // 불러와도 같은 자리: 저장 JSON → 새 HomeLife
            var json = JsonSerializer.Serialize(e.H.Snapshot());
            using var again = new Env(k, new[] { A, B2 });
            again.H.Load(JsonSerializer.Deserialize<HomeSave>(json));
            again.H.Tick();
            Expect(again.Slot(false).Origin == s.Origin && again.Slot(false).Area == A, $"저장했다 불러오면 같은 자리 ({again.Slot(false).Origin} vs {s.Origin})");
            // 모서리: 어디로 끌어도 통째로 모니터 안
            foreach (var m in new[] { new Point(-300, -300), new Point(1919, 0), new Point(1919, 1039), new Point(0, 1039), new Point(960, 5000) })
            {
                e.H.BeginDrag(s, 20, 30);
                e.H.DragTo(s, m);
                e.H.EndDrag(s);
                Expect(Inside(e.B.World.At(m.X, m.Y), s), $"({m.X},{m.Y}) 로 끌어 놓아도 그림이 통째로 안 ({s.Origin})");
            }
            // 둘째 모니터: 거기 놓으면 그 모니터 안에 머문다 (걸치지 않는다)
            foreach (var m in new[] { new Point(1925, 500), new Point(3519, 950), new Point(2500, 30), new Point(1921, 45) })
            {
                e.H.BeginDrag(s, 20, 30);
                e.H.DragTo(s, m);
                e.H.EndDrag(s);
                Expect(s.Area == B2 && Inside(B2, s), $"둘째 모니터({m.X},{m.Y}) 에 놓으면 그 모니터 안에 ({s.Origin}, {s.Area})");
            }
            // 모니터 하나가 빠졌다: 가장 가까운 모니터 안으로 옮겨 저장 칸에도 적는다
            e.H.BeginDrag(s, 20, 30); e.H.DragTo(s, new Point(3000, 400)); e.H.EndDrag(s);
            Expect(s.Area == B2, "(준비) 둘째 모니터에 놓였다");
            e.B.World = new World(new[] { A });
            e.H.Settle(); e.H.Tick();
            Expect(s.Area == A && Inside(A, s) && A.Contains((int)e.At()[0], (int)e.At()[1]), $"모니터가 빠지면 남은 모니터 안으로 ({s.Origin}, 저장 {e.At()[0]:0},{e.At()[1]:0})");
            // 해상도가 작아졌다
            e.H.BeginDrag(s, 20, 30); e.H.DragTo(s, new Point(1700, 900)); e.H.EndDrag(s);
            var small = new Rectangle(0, 0, 1280, 720);
            e.B.World = new World(new[] { small });
            e.H.Settle(); e.H.Tick();
            Expect(Inside(small, s), $"해상도가 작아져도 그림이 통째로 안 ({s.Origin})");
            // 집도 같다
            e.Place("kennel", 5000, 5000);
            Expect(Inside(small, e.Slot(true)), "집: 저장 자리가 모니터 밖이어도 안으로");
        }

        // ── 옛 저장: 방석·집이 따로 놓이던 저장은 하나로 (집이 그 집 자리에), 바닥 자리만 있던 [x, 바닥 y] 도 그대로 읽힌다 ──
        {
            using var e = new Env(k, new[] { area });
            e.H.Load(JsonSerializer.Deserialize<HomeSave>($"{{\"Got\":[\"round\",\"kennel\"],\"Cushion\":\"round\",\"House\":\"kennel\",\"CushionAt\":[300,{floor}],\"HouseAt\":[900.5,{floor}],\"CushionGiven\":true,\"HouseGiven\":true}}"));
            e.H.Tick();
            Expect(e.H.Book.Active == "kennel" && e.Slot(true).Origin == HomeGeom.Origin(kennel, 3, 900.5, floor), "방석·집이 다 있던 옛 저장: 집이 그 집 자리에 선다");
            Expect(e.Slot(true).Shown && !e.Slot(false).Shown && e.Slot(false).Item == null && e.ShownCount == 1 && e.B.House.On && !e.B.Cushion.On, "방석은 화면에서 빠지고 하나만 보인다");
            Expect(e.Slot(true).FloorY == floor && e.H.Book.Got.Count == 2, "발 자리 y 는 예전 바닥, 받은 것은 둘 다 수집에");
            var snap = e.H.Snapshot();
            Expect(snap.Active == "kennel" && snap.At is { Length: 2 } && snap.Cushion == null && snap.House == null && snap.CushionAt == null && snap.HouseAt == null, "다시 저장하면 새 꼴 (놓인 id 하나, 자리 [x, y] 하나)");
            e.H.Load(JsonSerializer.Deserialize<HomeSave>("{\"Got\":[\"round\"],\"Cushion\":\"round\",\"CushionGiven\":true,\"HouseGiven\":false}"));
            e.H.Tick();
            Expect(e.Slot(false).Item != null && e.At()[1] == floor && e.ShownCount == 1, "자리 칸이 없던 저장은 기본 자리 (바닥)");
            Expect(e.At()[0] > area.Right - 300, "기본 자리는 오른쪽 아래");
        }

        // ── 펫이 물건으로 간다: 바탕화면 어느 자리든 곧장 걸어가 쓰고 (앉기·낮잠·빼꼼·잠·캣타워 위) 나온다 ──
        {
            int n = 0;
            foreach (var (house, id, use, x, y) in new[]
            {
                (false, "round", HomeUse.Sit, 900.0, 400.0), (false, "donut", HomeUse.Nap, 200.0, 700.0), (false, "leaf", HomeUse.Sit, 1750.0, 300.0), (false, "futon", HomeUse.Nap, 960.0, 1000.0),
                (true, "kennel", HomeUse.Peek, 600.0, 500.0), (true, "kennel", HomeUse.Sleep, 1500.0, 900.0), (true, "box", HomeUse.Sleep, 300.0, 400.0), (true, "cattree", HomeUse.Sleep, 1400.0, 700.0), (true, "cattree", HomeUse.Nap, 700.0, 600.0),
                (true, "igloo", HomeUse.Peek, 1000.0, 300.0),
            })
            {
                using var e = new Env(k, new[] { area }, energy: use is HomeUse.Nap or HomeUse.Sleep ? 40 : 90);
                e.Place(id, x, y);
                var s = e.Slot(house);
                var sp = house ? e.B.House : e.B.Cushion;
                string what = $"{id} {use} ({x:0},{y:0})";
                Expect(sp.On && s.Shown && Inside(area, s), $"{what}: 놓였다");
                Expect(e.B.GoHome(house, use, stay: 3), $"{what}: 갈 수 있다");
                bool top = sp.HasTop && use is HomeUse.Sleep or HomeUse.Nap;
                double gx = top ? sp.TopX : use is HomeUse.Sleep or HomeUse.Nap ? sp.SleepX : sp.X;
                for (int i = 0; i < 60 * 40 && e.B.Mode != Mode.Home; i++) e.Step(1 / 60.0);
                Expect(e.B.Mode == Mode.Home && e.B.AtHome == use, $"{what}: 도착해 쓴다 (모드 {e.B.Mode}, 펫 {e.B.X:0},{e.B.Y:0})");
                Expect(Math.Abs(e.B.X - gx) < 3, $"{what}: 물건의 그 자리 x ({e.B.X:0} vs {gx:0})");
                for (int i = 0; i < 60 * 12 && use is HomeUse.Sleep or HomeUse.Nap && !(e.B.P.Asleep && e.B.Pose == "sleep"); i++) e.Step(1 / 60.0); // 집은 먼저 빼꼼한 뒤 잔다
                e.Step(1.5);
                double wantY = top ? sp.TopY : use is HomeUse.Sleep or HomeUse.Nap ? sp.Y + sp.SleepDy : sp.Y + (house ? 0 : sp.SitDy);
                Expect(Math.Abs(e.B.Y - wantY) < 3, $"{what}: y 가 물건 위·안 ({e.B.Y:0} vs {wantY:0})");
                Expect(e.B.HomeClip != null && e.B.HomeClipNow == (house && !top ? HomeClipKind.Inside : HomeClipKind.Front), $"{what}: 오림 ({e.B.HomeClipNow})");
                if (use is HomeUse.Sleep or HomeUse.Nap) Expect(e.B.P.Asleep, $"{what}: 잔다");
                // 깬다: 나온다 (캣타워 위는 뛰어내린다). 나온 뒤엔 바탕화면에서 평소처럼
                e.B.P.Energy = 100; e.B.P.Asleep = false; e.B.HomeOut();
                bool thrown = false;
                for (int i = 0; i < 60 * 30 && (e.B.Mode == Mode.Home || e.B.Mode == Mode.Thrown); i++) { e.Step(1 / 60.0); thrown |= e.B.Mode == Mode.Thrown; }
                Expect(e.B.Mode != Mode.Home && e.B.Mode != Mode.Thrown && e.B.HomeClipNow == HomeClipKind.None, $"{what}: 쓰고 나온다 (모드 {e.B.Mode})");
                Expect(!top || thrown, $"{what}: 캣타워 위에서는 뛰어내린다");
                Expect(e.B.Y <= area.Bottom - k.FloorGap + 1 && e.B.Y >= area.Top + 40 * 3 - 1, $"{what}: 나온 펫은 화면 안 ({e.B.Y:0})");
                n++;
            }
            Expect(n == 10, "열 곳 모두");
            // 물건이 없어지면 가던 펫은 그 자리에서 다시 돌아다닌다 (공중에 멈추지 않는다)
            using var g = new Env(k, new[] { area }, energy: 40);
            g.Place("round", 1400, 300);
            g.B.GoHome(false, HomeUse.Sit, stay: 60);
            for (int i = 0; i < 60 * 40 && g.B.Mode != Mode.Home; i++) g.Step(1 / 60.0);
            Expect(g.B.Mode == Mode.Home, "(준비) 높은 방석에 앉았다");
            g.H.SetHidden(true);
            g.Step(.5);
            Expect(g.B.Mode != Mode.Home && g.B.Mode != Mode.Thrown && g.B.Y > 0, $"물건을 숨기면 펫은 그 자리에서 평소대로 ({g.B.Mode})");
        }

        // ── 끌 때 펫이 같이: 방석 위·집 안·캣타워 위·잠든 채. 같은 프레임에 같이 옮기고 오림·순서도 따라온다 ──
        {
            using var gfx = Graphics.FromImage(new Bitmap(1, 1));
            foreach (var (house, id, use, x, y) in new[]
            {
                (false, "round", HomeUse.Sit, 600.0, 500.0), (false, "futon", HomeUse.Nap, 700.0, 600.0), (true, "kennel", HomeUse.Peek, 900.0, 700.0),
                (true, "kennel", HomeUse.Sleep, 800.0, 500.0), (true, "cattree", HomeUse.Sleep, 1200.0, 800.0),
            })
            {
                using var e = new Env(k, new[] { A, B2 }, energy: use is HomeUse.Nap or HomeUse.Sleep ? 40 : 90);
                e.Place(id, x, y);
                var s = e.Slot(house);
                var sp = house ? e.B.House : e.B.Cushion;
                string what = $"{id} {use}";
                e.B.GoHome(house, use, stay: 600);
                for (int i = 0; i < 60 * 40 && e.B.Mode != Mode.Home; i++) e.Step(1 / 60.0);
                for (int i = 0; i < 60 * 12 && use is HomeUse.Sleep or HomeUse.Nap && !(e.B.P.Asleep && e.B.Pose == "sleep"); i++) e.Step(1 / 60.0); // 집은 먼저 빼꼼(1.2~2초)한 뒤 잔다
                e.Step(2); // 잠든 채 문 안으로 다 내려갈 때까지 (0.25초)
                Expect(e.B.Mode == Mode.Home, $"{what}: (준비) 안·위에 있다");
                var kind = e.B.HomeClipNow;
                var (offX, offY) = (e.B.X - s.Origin.X, e.B.Y - s.Origin.Y);
                var clip0 = e.B.HomeClip!.GetBounds(gfx);
                var o0 = s.Origin;
                var bad = new List<string>();
                e.OnFollow = () => // 펫 창을 옮기는 순간: 펫이 이미 물건과 같은 자리에 있나, 오림이 이미 새 자리인가
                {
                    var clip = e.B.HomeClip?.GetBounds(gfx);
                    bool ok = Math.Abs(e.B.X - (s.Origin.X + offX)) < 1.5 && Math.Abs(e.B.Y - (s.Origin.Y + offY)) < 1.5;
                    bool clipOk = clip is { } c && Math.Abs(c.X - (clip0.X + s.Origin.X - o0.X)) < 1.5 && Math.Abs(c.Y - (clip0.Y + s.Origin.Y - o0.Y)) < 1.5;
                    if (!ok) bad.Add($"펫 ({e.B.X:0},{e.B.Y:0}) 물건 {s.Origin}");
                    if (!clipOk) bad.Add($"오림 {clip} 물건 {s.Origin}");
                    return ok && clipOk;
                };
                e.Log.Clear();
                int grabX = 15, grabY = 25;
                var m0 = new Point(o0.X + sp.Bounds.Width / 2 + grabX, o0.Y + grabY);
                e.H.BeginDrag(s, grabX, grabY);
                Expect(e.Log.Count > 0 && e.Log[^1] == "pet", $"{what}: 끌기 시작에 창 순서를 다시 건다 (물건 → 그 위로 펫): {string.Join(",", e.Log)}");
                // 대각선으로 4초 끌기 (다른 모니터까지 건넌다)
                int steps = 60 * 4;
                for (int i = 1; i <= steps; i++)
                {
                    e.Log.Clear();
                    var m = new Point(m0.X + (int)((2800 - m0.X) * i / (double)steps), m0.Y + (int)((200 - m0.Y) * i / (double)steps));
                    e.H.DragTo(s, m);
                    e.Step(1 / 60.0);
                    if (e.Log.Count > 0) Expect(e.Log[^1] == "pet", $"{what}: 창 순서는 늘 펫이 맨 위: {string.Join(",", e.Log)}");
                    if (i % 20 == 0)
                        Expect(e.B.Mode == Mode.Home && e.B.HomeClipNow == kind && e.B.House.Moving == (house && s.Moving) && Math.Abs(e.B.X - (s.Origin.X + offX)) < 1.5 && Math.Abs(e.B.Y - (s.Origin.Y + offY)) < 1.5,
                            $"{what}: 끄는 동안 펫이 물건과 같이 간다 ({i}/{steps}: 모드 {e.B.Mode}, 펫 {e.B.X:0},{e.B.Y:0}, 물건 {s.Origin})");
                    if (bad.Count > 0) break;
                }
                Expect(bad.Count == 0, $"{what}: 펫 창을 옮기는 같은 순간 펫·오림이 이미 물건을 따라와 있다: {string.Join(" / ", bad.Take(3))}");
                Expect(e.FollowCalls >= steps, $"{what}: 끌 때마다 펫 창을 옮긴다 ({e.FollowCalls}번)");
                e.Log.Clear();
                e.H.EndDrag(s);
                Expect(e.Log.Count > 0 && e.Log[^1] == "pet", $"{what}: 놓을 때도 창 순서를 다시 건다: {string.Join(",", e.Log)}");
                e.Step(2);
                Expect(!s.Moving && e.B.Mode == Mode.Home && Math.Abs(e.B.X - (s.Origin.X + offX)) < 1.5 && Math.Abs(e.B.Y - (s.Origin.Y + offY)) < 1.5, $"{what}: 놓은 뒤에도 펫은 물건 위·안 ({e.B.Mode}, {e.B.X:0},{e.B.Y:0})");
                Expect(s.Area == B2 && Inside(B2, s), $"{what}: 둘째 모니터에 놓였다 ({s.Area})");
                if (use is HomeUse.Sleep or HomeUse.Nap) Expect(e.B.P.Asleep, $"{what}: 자던 채 그대로");
            }
        }

        // ── 창 순서: 물건 창을 올릴 때마다 그 위로 펫까지. 새로 뜰 때·숨겼다 보일 때·5초 주기(Raise)도 ──
        {
            using var e = new Env(k, new[] { area });
            e.Place("round", 500, 500);
            e.Log.Clear();
            e.H.Raise();
            Expect(e.Log.SequenceEqual(new[] { "item101", "pet" }), $"5초 주기: 방석 → 펫 ({string.Join(",", e.Log)})");
            e.H.SetPlaced("kennel"); e.Step(.1);
            e.Log.Clear();
            e.H.Raise();
            Expect(e.Log.SequenceEqual(new[] { "item102", "pet" }), $"집으로 바꾸면 집 창 하나만 → 펫 ({string.Join(",", e.Log)})");
            e.H.SetPlaced("round"); e.Step(.1);
            e.H.SetHidden(true); e.Step(.1);
            Expect(!e.Slot(false).Shown && !e.Slot(true).Shown, "(준비) 숨겼다");
            e.Log.Clear();
            e.H.Raise();
            Expect(e.Log.SequenceEqual(new[] { "pet" }), $"숨긴 물건 창은 안 올린다 ({string.Join(",", e.Log)})");
            e.Log.Clear();
            e.H.SetHidden(false); e.Step(.1);
            Expect(e.Slot(false).Shown && e.Log.Count > 0 && e.Log[^1] == "pet" && e.Log.Contains("item101"), $"다시 보이면 새로 뜬 창이 펫 위로 못 가게 펫까지 다시 올린다 ({string.Join(",", e.Log)})");
            // 끄는 동안 간격마다 다시 건다 (0.1초)
            e.Log.Clear();
            var s = e.Slot(false);
            e.H.BeginDrag(s, 10, 10);
            for (int i = 0; i < 60; i++) { e.H.DragTo(s, new Point(500 + i, 500)); e.Step(1 / 60.0); }
            int raises = e.Log.Count(x => x == "pet");
            Expect(raises >= 8 && raises <= 14, $"끄는 1초 동안 창 순서를 0.1초마다 다시 건다 ({raises}번)");
            e.H.EndDrag(s);
        }

        OneChecks(k, art, area);

        // 그림 자리: 높은 곳에 놓인 물건의 문 오림·앞 겹이 그림 자리를 그대로 따라간다
        {
            var onFloor = HomeGeom.Origin(kennel, 3, 1000, floor);
            var onWin = HomeGeom.Origin(kennel, 3, 1000, 520);
            var sp = HomeGeom.Spot(kennel, 3, onWin, 520, 20.5, 22, false);
            Expect(Math.Abs(sp.Y - 520) < 1 && sp.Bounds.Y == onWin.Y && sp.Bounds.Width == kennel.W * 3, "높은 곳의 집: 자리가 그 y, Bounds 가 그림 자리");
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
            // 바탕화면 높은 곳의 물건에 놓아도 들어가고, 나올 땐 그 자리에서 평소처럼 (뛰어 내리지 않는다)
            var up = new Brain(new Pet { Stage = "child", Full = 90, Mood = 90, Energy = 90 }, k, area) { X = 400, Y = floor };
            up.Cushion = Spot(1000, 520, false);
            up.Lift(); up.Mouse = new Point(1000, 500); up.Update(1 / 60.0, false); up.Drop();
            up.Update(1 / 60.0, false);
            Expect(up.Mode == Mode.Home && Math.Abs(up.Y - (520 + up.Cushion.SitDy)) < 2, "높은 곳의 방석에 놓아도 들어간다");
            bool jumped = false;
            for (int i = 0; i < 60 * 20 && up.Mode == Mode.Home; i++) { up.Update(1 / 60.0, false); jumped |= up.Mode == Mode.Thrown; }
            Expect(!jumped && up.Mode != Mode.Home && up.Y >= up.Area.Top && up.Y <= floor, $"높은 곳의 방석에서 나올 땐 떨어지지 않고 그 자리에서 평소처럼 (모드 {up.Mode}, y {up.Y:0})");
        }
    }

    // ── 화면에는 하나만 (추가 3): 한 번에 하나만 보이고, 어른이 집을 받으면 방석이 그 자리에서 집으로 바뀌고, 메뉴로 종류를 바꾸고,
    //    낮잠·밤잠·자리 비움 잠·앉기·들어 놓기가 다 그 하나로 간다 ──
    static void OneChecks(Tuning k, HomeArt art, Rectangle area)
    {
        double floor = area.Bottom - k.FloorGap;
        bool Only(Env e, bool house) => e.ShownCount == 1 && (house ? e.H.House.Shown && !e.H.Cushion.Shown : e.H.Cushion.Shown && !e.H.House.Shown)
            && (house ? e.B.House.On && !e.B.Cushion.On : e.B.Cushion.On && !e.B.House.On);

        // 하나만 보인다: 놓기·바꾸기·불러오기·숨겼다 보이기 어느 때나 (받는 연출 중에도 한꺼번에 둘이 보이는 순간이 없다)
        {
            using var e = new Env(k, new[] { area });
            Expect(e.ShownCount == 0, "처음엔 아무것도 안 보인다");
            e.Place("donut", 700, 600);
            Expect(Only(e, false), "방석을 놓으면 방석 하나만");
            e.H.SetPlaced("cattree"); e.Step(.1);
            Expect(Only(e, true) && e.H.Book.Active == "cattree" && e.H.Book.Got.Contains("donut") && e.H.Book.Got.Contains("cattree"), "집으로 바꾸면 집 하나만, 받은 것은 둘 다 남는다");
            Expect(e.H.House.CenterX == 700 && e.H.House.FloorY == 600 && e.H.Book.At![0] == 700 && e.H.Book.At[1] == 600, "같은 자리에서 바뀐다");
            e.H.SetPlaced("round"); e.Step(.1);
            Expect(Only(e, false) && e.H.Cushion.CenterX == 700 && e.H.Cushion.FloorY == 600, "다시 방석으로: 하나만, 같은 자리");
            e.H.SetHidden(true); e.Step(.1);
            Expect(e.ShownCount == 0, "숨기면 하나도 없다");
            e.H.SetHidden(false); e.Step(.1);
            Expect(Only(e, false), "다시 보이면 하나만");
            int worst = 0;
            for (int i = 0; i < 20; i++) { e.H.SetPlaced(i % 2 == 0 ? "kennel" : "futon"); worst = Math.Max(worst, e.ShownCount); e.Step(.05); worst = Math.Max(worst, e.ShownCount); }
            Expect(worst == 1, $"자꾸 바꿔도 한 번에 둘이 보이지 않는다 (최대 {worst})");
            // 끌어도 하나, 끈 자리가 하나의 자리가 된다
            var s = e.H.ActiveSlot!;
            e.H.BeginDrag(s, 10, 10); e.H.DragTo(s, new Point(1100, 500)); e.H.EndDrag(s); e.Step(.1);
            Expect(e.ShownCount == 1 && e.H.Book.At![0] == s.CenterX && e.H.Book.At[1] == s.FloorY, "끌어 놓은 자리가 하나의 자리");
            e.H.SetPlaced("kennel"); e.Step(.1);
            Expect(e.H.House.CenterX == s.CenterX || e.H.House.Shown, "(바꾸면 끈 자리에서 바뀐다)");
            // 저장했다 불러와도 하나
            var json = JsonSerializer.Serialize(e.H.Snapshot());
            using var again = new Env(k, new[] { area });
            again.H.Load(JsonSerializer.Deserialize<HomeSave>(json));
            again.H.Tick();
            Expect(again.ShownCount == 1 && again.H.Book.Active == e.H.Book.Active && again.H.ActiveSlot!.Origin == e.H.ActiveSlot!.Origin, "저장했다 불러와도 하나, 같은 자리");
        }

        // 받는 순간: 어른이 집을 받으면 연출 뒤 집이 방석 자리에 바뀌어 놓인다 (방석은 집이 톡 떨어지는 0.5초까지 그 자리에)
        foreach (var (stage, from, to) in new[] { ("adult_sweet", "round", "kennel"), ("child", "cattree", "leaf") })
        {
            using var e = new Env(k, new[] { area }, stage, energy: 90, petX: 300);
            e.Place(from, 900, 620);
            var oldSlot = e.Slot(HomeBook.IsHouse(from));
            var newSlot = e.Slot(HomeBook.IsHouse(to));
            e.H.Give(HomeBook.IsHouse(to), to, true);
            Expect(e.H.Revealing && e.H.Book.Active == to && e.H.Book.At![0] == 900 && e.H.Book.At[1] == 620, $"{from}→{to}: 받았다 (저장은 0.0 에 이미 바뀌고, 자리는 그대로)");
            Expect(e.ShownCount == 1 && oldSlot.Shown && oldSlot.Id == from && !newSlot.Shown, $"{from}→{to}: 연출 0.0 엔 옛 물건이 그 자리에 그대로");
            int most = 0;
            bool swapped = false;
            for (int i = 0; i < 60 * 4; i++)
            {
                e.Step(1 / 60.0);
                most = Math.Max(most, e.ShownCount);
                if (!swapped && newSlot.Shown) { swapped = true; Expect(!oldSlot.Shown && e.ShownCount == 1 && e.B.Now > .45, $"{from}→{to}: 새 물건이 뜨는 순간 옛 물건이 빠진다 (한 번에 하나)"); }
            }
            Expect(most <= 1 && swapped, $"{from}→{to}: 연출 내내 한 번에 둘이 보이지 않고, 끝내 새 물건만 남는다");
            Expect(newSlot.Id == to && oldSlot.Item == null && newSlot.CenterX == 900 && newSlot.FloorY == 620, $"{from}→{to}: 같은 자리에 새 물건 ({newSlot.CenterX:0},{newSlot.FloorY:0})");
            Expect(e.H.Book.Got.Contains(from) && e.H.Book.Got.Contains(to), $"{from}→{to}: 둘 다 수집에 남는다");
            Expect(e.B.Mode is Mode.Home or Mode.GoHome && e.B.HomeIsHouse == HomeBook.IsHouse(to), $"{from}→{to}: 펫은 새 물건으로 달려간다 ({e.B.Mode})");
        }
        // 게임 중에 받아 연출이 끼어도 (연출 없이) 바뀌어 놓인다
        {
            using var e = new Env(k, new[] { area });
            e.Place("round", 800, 600);
            e.H.Give(true, "kennel", false);
            e.Step(.2);
            Expect(Only(e, true) && e.H.Book.At![0] == 800, "연출 없이 받아도 집 하나가 그 자리에");
        }
        // 줄 것 규칙: 방석 → 어른이 되면 집 (놓인 하나가 바뀐다)
        {
            using var e = new Env(k, new[] { area }, "child", energy: 90, petX: 300);
            (e.H.Book.CushionGiven, e.H.Book.HouseGiven) = (false, false);
            // 받는 때는 「한가할 때」(Wander) 라서 펫이 앉기·엿보기 같은 다른 일을 하면 늦어진다: 8초 고정으로 보면 가끔 못 받은 채 끝났다 (CI 에서 떨어짐)
            for (int i = 0; i < 60 * 40 && !e.H.Book.CushionGiven; i++) e.Step(1 / 60.0);
            for (int i = 0; i < 60 * 4; i++) e.Step(1 / 60.0);
            Expect(e.H.Book.CushionGiven && e.H.Book.Cushion != null && e.ShownCount <= 1, "어린이가 되면 방석 하나를 받는다");
            e.B.P.Stage = "adult_sweet";
            for (int i = 0; i < 60 * 40 && !e.H.Book.HouseGiven; i++) e.Step(1 / 60.0);
            for (int i = 0; i < 60 * 4; i++) e.Step(1 / 60.0);
            Expect(e.H.Book.HouseGiven && e.H.Book.House != null && Only(e, true) && e.H.Book.Got.Count == 2, "다 자라면 집을 받고 화면에는 집 하나만 (방석은 수집에)");
        }

        // 메뉴: 방석 ▸ · 집 ▸ 에서 고르면 그것이 하나로 바뀐다. 체크는 놓인 하나에만. 물건을 오른쪽 클릭한 메뉴에도 방석·집이 다 나온다
        {
            using var e = new Env(k, new[] { area });
            foreach (var id in new[] { "donut", "futon", "kennel", "cattree" }) e.H.Book.Receive(id);
            e.Place("donut", 700, 600);
            var pet = new HomeMenu.PetItem(e.H, () => art);
            using var owner = new ToolStrip();
            pet.Refresh(owner);
            var drops = pet.Root.DropDownItems.Cast<ToolStripItem>().OfType<ToolStripMenuItem>().Where(m => m.HasDropDownItems).ToList();
            Expect(drops.Count == 2 && drops[0].DropDownItems.Count == 2 && drops[1].DropDownItems.Count == 2, "펫 메뉴: 방석 ▸ 받은 방석 둘, 집 ▸ 받은 집 둘");
            var all = drops.SelectMany(d => d.DropDownItems.Cast<ToolStripMenuItem>()).ToList();
            Expect(all.Count(m => m.Checked) == 1 && all.Single(m => m.Checked).Tag as string == "donut", "체크는 놓인 하나에만");
            all.Single(m => (m.Tag as string) == "cattree").PerformClick();
            e.Step(.1);
            Expect(Only(e, true) && e.H.Book.Active == "cattree", "메뉴에서 집을 고르면 방석이 빠지고 집 하나만");
            pet.Refresh(owner);
            all = pet.Root.DropDownItems.Cast<ToolStripItem>().OfType<ToolStripMenuItem>().Where(m => m.HasDropDownItems).SelectMany(d => d.DropDownItems.Cast<ToolStripMenuItem>()).ToList();
            Expect(all.Count(m => m.Checked) == 1 && all.Single(m => m.Checked).Tag as string == "cattree", "체크가 집으로 옮겨졌다");
            all.Single(m => (m.Tag as string) == "futon").PerformClick();
            e.Step(.1);
            Expect(Only(e, false) && e.H.Book.Active == "futon", "메뉴에서 방석을 고르면 다시 방석 하나");
            // 물건 메뉴
            using var item = HomeMenu.ItemMenu(e.H, art, 32);
            var entries = item.Items.OfType<ToolStripMenuItem>().Where(m => m.Tag is string).ToList();
            Expect(entries.Select(m => (string)m.Tag!).SequenceEqual(new[] { "donut", "futon", "kennel", "cattree" }), "물건 메뉴: 받은 방석 → 받은 집이 다 나온다");
            Expect(entries.Count(m => m.Checked) == 1 && (string)entries.Single(m => m.Checked).Tag! == "futon", "물건 메뉴: 체크는 놓인 하나에만");
            Expect(item.Items.OfType<ToolStripSeparator>().Count() == 2 && item.Items[^1].Text == L.T("menu.home_reset"), "물건 메뉴: 분리선·제자리로");
            entries.Single(m => (string)m.Tag! == "kennel").PerformClick();
            e.Step(.1);
            Expect(Only(e, true) && e.H.Book.Active == "kennel", "물건 메뉴에서 집을 고르면 집 하나로 바뀐다");
            foreach (var m in item.Items.Cast<ToolStripItem>().ToList()) { m.Image?.Dispose(); m.Dispose(); }
            foreach (var m in drops.SelectMany(d => d.DropDownItems.Cast<ToolStripItem>()).ToList()) { m.Image?.Dispose(); }
            // 제자리로: 하나의 자리가 기본 자리로
            e.H.ResetSpot(); e.Step(.1);
            Expect(e.ShownCount == 1 && e.H.Book.At![0] > area.Right - 300 && e.H.Book.At[1] == floor, "제자리로: 하나의 자리가 기본 자리로");
        }

        // 놓인 하나로: 낮잠·밤잠·자리 비움 잠이 모두 방석이든 집이든 그 하나로 간다 (진짜 HomeLife 자리로)
        foreach (var (id, house) in new[] { ("donut", false), ("kennel", true), ("cattree", true), ("futon", false) })
        {
            // 낮잠 (메뉴 「재우기」 = TryRest, 창 위가 없으니 놓인 하나뿐)
            using (var e = new Env(k, new[] { area }, "adult_sweet", energy: 40, petX: 300))
            {
                e.Place(id, 1200, floor);
                Expect(e.B.TryRest() && e.B.Mode == Mode.GoHome && e.B.HomeIsHouse == house, $"{id}: 낮잠은 놓인 하나로 간다");
                for (int i = 0; i < 60 * 40 && !(e.B.Mode == Mode.Home && e.B.P.Asleep && e.B.Pose == "sleep"); i++) e.Step(1 / 60.0);
                Expect(e.B.Mode == Mode.Home && e.B.P.Asleep && e.B.AtHome == (house ? HomeUse.Sleep : HomeUse.Nap), $"{id}: 낮잠 (모드 {e.B.Mode})");
            }
            // 밤잠: 밤 규칙이 켜진 채 (밤 인사 뒤 = GoSleepHome)
            using (var e = new Env(k, new[] { area }, "adult_sweet", energy: 70, petX: 300))
            {
                e.B.NightNow = () => true;
                e.Place(id, 1200, floor);
                Expect(e.B.GoSleepHome() && e.B.HomeIsHouse == house, $"{id}: 밤 인사 뒤 놓인 하나로 간다");
                for (int i = 0; i < 60 * 40 && !(e.B.Mode == Mode.Home && e.B.P.Asleep && e.B.Pose == "sleep"); i++) e.Step(1 / 60.0);
                Expect(e.B.Mode == Mode.Home && e.B.P.Asleep && e.B.AtHome == (house ? HomeUse.Sleep : HomeUse.Nap), $"{id}: 밤잠은 {(house ? "집 안" : "방석 위")}에서 (모드 {e.B.Mode})");
                Expect(e.B.HomeClipNow == (house && e.B.House.HasTop ? HomeClipKind.Front : house ? HomeClipKind.Inside : HomeClipKind.Front), $"{id}: 오림");
            }
            // 자리 비움 잠: afk 로 잠들면 걸어가 그 하나에서
            using (var e = new Env(k, new[] { area }, "adult_sweet", energy: 80, petX: 300))
            {
                e.Place(id, 1200, floor);
                for (int i = 0; i < 4; i++) { e.B.Update(1 / 60.0, true); e.H.Tick(); }
                Expect(e.B.Mode == Mode.GoHome && e.B.P.Asleep && e.B.HomeIsHouse == house, $"{id}: 자리 비움 잠은 걸어서 놓인 하나로 ({e.B.Mode})");
                for (int i = 0; i < 60 * 40 && !(e.B.Mode == Mode.Home && e.B.Pose == "sleep"); i++) { e.B.Update(1 / 60.0, true); e.H.Tick(); }
                Expect(e.B.Mode == Mode.Home && e.B.P.Asleep && e.B.AtHome == (house ? HomeUse.Sleep : HomeUse.Nap), $"{id}: 자리 비움 잠이 놓인 하나 안에서 ({e.B.Mode})");
            }
            // 앉기 (혼자 놀기): 방석은 앉고 집은 문 앞에서 내다본다
            using (var e = new Env(k, new[] { area }, "adult_sweet", energy: 60, petX: 900))
            {
                e.Place(id, 1200, floor);
                int n = 0;
                for (int i = 0; i < 200; i++) { e.B.Mode = Mode.Wander; if (e.B.TrySit()) { n++; Expect(e.B.HomeIsHouse == house, $"{id}: 앉기도 놓인 하나로"); } }
                Expect(n > 20, $"{id}: 혼자 놀다 놓인 하나로 간다 ({n}/200)");
                e.B.Mode = Mode.Wander;
                Expect(e.B.GoHome(house, house ? HomeUse.Peek : HomeUse.Sit, stay: 2), $"{id}: 앉으러 간다");
                for (int i = 0; i < 60 * 40 && e.B.Mode != Mode.Home; i++) e.Step(1 / 60.0);
                Expect(e.B.Mode == Mode.Home && e.B.AtHome == (house ? HomeUse.Peek : HomeUse.Sit), $"{id}: 도착해 앉는다·내다본다");
            }
        }

        // 펫이 안·위에 있는 채 메뉴로 종류를 바꾸면 새 물건으로 옮겨 앉는다 (자던 채면 계속 자고, 깬 채면 앉기·내다보기로)
        foreach (var (from, to, asleep) in new[] { ("donut", "kennel", true), ("kennel", "round", true), ("round", "igloo", false), ("cattree", "futon", true) })
        {
            using var e = new Env(k, new[] { area }, "adult_sweet", energy: asleep ? 40 : 90, petX: 300);
            e.Place(from, 1100, floor);
            bool fromHouse = HomeBook.IsHouse(from), toHouse = HomeBook.IsHouse(to);
            e.B.GoHome(fromHouse, asleep ? (fromHouse ? HomeUse.Sleep : HomeUse.Nap) : (fromHouse ? HomeUse.Peek : HomeUse.Sit), stay: 600);
            for (int i = 0; i < 60 * 40 && !(e.B.Mode == Mode.Home && (!asleep || (e.B.P.Asleep && e.B.Pose == "sleep"))); i++) e.Step(1 / 60.0);
            Expect(e.B.Mode == Mode.Home && e.B.HomeIsHouse == fromHouse, $"{from}→{to}: (준비) 안·위에 있다");
            e.H.SetPlaced(to);
            e.Step(1.5);
            var sp = toHouse ? e.B.House : e.B.Cushion;
            Expect(Only(e, toHouse) && e.B.Mode == Mode.Home && e.B.HomeIsHouse == toHouse, $"{from}→{to}: 펫이 새 물건으로 옮겨 앉는다 (모드 {e.B.Mode})");
            Expect(e.B.P.Asleep == asleep && Math.Abs(e.B.X - (asleep ? sp.SleepX : sp.X)) < 3, $"{from}→{to}: 자던 채면 계속 자고, 새 물건 자리에 선다 (x {e.B.X:0} vs {(asleep ? sp.SleepX : sp.X):0})");
            Expect(e.B.HomeClipNow == (toHouse && !(asleep && sp.HasTop && Pet.Grown(e.B.P.Stage)) ? HomeClipKind.Inside : HomeClipKind.Front) && e.B.HomeClip != null, $"{from}→{to}: 오림도 새 물건 것 ({e.B.HomeClipNow})");
        }

        // 들어 놓은 펫: 놓인 하나 위에 놓으면 그 하나로 들어가고, 바꾼 뒤엔 바뀐 종류로 들어간다
        {
            using var e = new Env(k, new[] { area }, "adult_sweet", energy: 90, petX: 300);
            foreach (var id in new[] { "round", "kennel", "futon", "cattree" })
            {
                e.Place(id, 1100, floor);
                bool house = HomeBook.IsHouse(id);
                var sp = house ? e.B.House : e.B.Cushion;
                e.B.Mode = Mode.Wander; e.B.P.Asleep = false;
                e.B.Lift();
                e.B.Mouse = new Point((int)sp.X, (int)floor - 30);
                e.B.Update(1 / 60.0, false);
                Expect(e.B.Mode == Mode.Held, $"{id}: 들었다");
                e.B.Drop();
                Expect(e.B.Mode == Mode.Home && e.B.HomeIsHouse == house && e.B.AtHome == (house ? HomeUse.Peek : HomeUse.Sit), $"{id}: 놓인 하나 위에 놓으면 들어간다 ({e.B.Mode}, {e.B.AtHome})");
                double t = 0;
                for (; t < 20 && e.B.Mode == Mode.Home; t += 1 / 60.0) e.Step(1 / 60.0);
                Expect(e.B.Mode != Mode.Home && t is >= 7.9 and <= 12.5, $"{id}: 8~12초 머문 뒤 나온다 ({t:0.0}초)");
            }
            // 빠진 물건의 자리(같은 자리)에 놓아도 이제 새 종류로만 들어간다
            e.Place("round", 1100, floor);
            e.H.SetPlaced("kennel"); e.Step(.1);
            var h = e.B.House;
            e.B.Mode = Mode.Wander;
            e.B.Lift(); e.B.Mouse = new Point((int)h.X, (int)floor - 30); e.B.Update(1 / 60.0, false); e.B.Drop();
            Expect(e.B.Mode == Mode.Home && e.B.HomeIsHouse && !e.B.Cushion.On, "방석에서 집으로 바꾼 뒤 같은 자리에 놓으면 집으로 들어간다");
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
