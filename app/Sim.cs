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
        var gr = r.P.Growth(new Tuning());
        double sum = Math.Max(1e-9, gr.Sum());
        return $"{name.PadRight(8)} {r.P.Stage.PadRight(13)} 통통 {gr[0] / sum:P0} 애교 {gr[1] / sum:P0} 장난 {gr[2] / sum:P0} 심술 {gr[3] / sum:P0} 실수 {r.P.Miss} 누적 {r.P.BitesTotal}입 남은 {r.P.Bites}입 " +
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
        Expect(once.P.Stage == "adult_grumpy", "하루 1번이면 배고픈 시간이 길어 심술이");
        Expect(twice.P.Stage != "adult_grumpy", "하루 2번이면 심술이는 아니다");
        string Grow(Action<Pet> care) // 어른 모습은 돌봄의 비율로 (YELL 2026-11-13)
        {
            var g = new Pet { Stage = "child", AgeMin = k.ChildMin - .5, Full = 100, Mood = 100, Energy = 100 }; // 어른이 되기 30초 전
            care(g);
            g.Tick(k, 1);
            return g.Stage;
        }
        Expect(Grow(g => g.Feeds = 20) == "adult_chubby", "밥을 많이 주면 통통이");
        Expect(Grow(g => (g.Feeds, g.Pets) = (20, 90)) == "adult_sweet", "밥보다 쓰다듬기가 많으면 애교쟁이");
        Expect(Grow(g => (g.Feeds, g.Plays, g.Tosses) = (20, 15, 20)) == "adult_playful", "놀아 주고 살살 던져 주면 장난꾸러기");
        Expect(Grow(g => (g.Feeds, g.Pets, g.Rough) = (20, 30, 25)) == "adult_grumpy", "세게 던지고 흔드는 게 가장 많으면 심술이");
        Expect(Grow(g => (g.Feeds, g.Pets, g.Rough) = (20, 30, 5)) == "adult_chubby", "가끔 세게 던져도 비율이 작으면 괜찮다");
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
