namespace Bolok;

// 저장 칸 (SaveData.Home). Got 은 받은 것 id 를 받은 순서대로 (방석·집 섞어서) — 세대를 넘어 남는다.
// 화면에는 하나만 놓인다 (추가 3): Active = 그 하나의 id (방석이든 집이든), At = 그 자리 [x, y].
// CushionGiven·HouseGiven 은 「지금 키우는 아이」가 받았나 (새 알이 오면 둘 다 false). 옛 저장(칸 없음)은 null → 빈 수집, 둘 다 false 라
// 「줄 것이 있나」 규칙(HomeBook.Owed)이 알아서 준다 — 따로 옮기는 코드가 없다.
// 뒤의 Cushion·House·CushionAt·HouseAt 은 방석·집이 따로 놓이던 옛 저장을 읽는 칸이다 (새 저장은 늘 null) — HomeBook.Load 가 하나로 옮긴다
public sealed record HomeSave(List<string> Got, bool CushionGiven, bool HouseGiven, bool Hidden = false, string? Active = null, double[]? At = null,
    string? Cushion = null, string? House = null, double[]? CushionAt = null, double[]? HouseAt = null);

// 방석·집 수집 장부: 물건 목록, 종에 맞게 기울인 무작위 뽑기, 줄 것 규칙, 저장. 화면이 없다 (자가 검사가 그대로 돌린다)
public sealed class HomeBook
{
    // 물건 id (저장·lang 키·그림 데이터가 같이 쓴다). 번호가 아니라 이름 — 나중에 끼워 넣어도 안 밀린다. 표 순서가 뽑기 순서다
    public static readonly string[] Cushions = { "round", "tassel", "donut", "basket", "futon", "leaf", "cloud", "heart", "egg", "flower" };
    public static readonly string[] Houses = { "kennel", "box", "mushroom", "teepee", "igloo", "rounddoor", "wicker", "cattree", "strawberry", "teapot" };

    public static bool IsHouse(string id) => Array.IndexOf(Houses, id) >= 0;
    public static bool Known(string id) => Array.IndexOf(Cushions, id) >= 0 || Array.IndexOf(Houses, id) >= 0;

    // ── 종 가족 (47종, 한 종은 한 가족) ──
    static readonly (string Family, string[] Species)[] Families =
    {
        ("cat", new[] { "cat", "ragdoll", "scottishfold", "exotic" }),
        ("dog", new[] { "shiba", "jindo", "retriever", "corgi", "pug", "pomeranian" }),
        ("cold", new[] { "penguin", "malamute" }),
        ("burrow", new[] { "hamster", "squirrel", "hedgehog", "quokka" }),
        ("forest", new[] { "fox", "raccoon", "redpanda", "deer", "moonbear", "koala", "panda", "owl" }),
        ("water", new[] { "otter", "duck", "frog", "beaver", "capybara", "octopus" }),
        ("farm", new[] { "chick", "sheep", "goat", "alpaca", "pig" }),
        ("fairy", new[] { "unicorn", "slime", "mushroom", "mongma", "ghost" }),
        ("imp", new[] { "dragon", "drake", "golem", "imp", "shadowimp", "gumiho", "dokkaebi" }),
    };

    // ── 무게: 가족 → (집, 방석). 적지 않은 것은 1 ──
    static readonly Dictionary<string, (string[] House, string[] Cushion)> Weights = new()
    {
        ["cat"] = (new[] { "cattree 6", "box 6", "wicker 3" }, new[] { "donut 6", "basket 6", "futon 3" }),
        ["dog"] = (new[] { "kennel 6", "teepee 3", "box 3" }, new[] { "round 6", "basket 3", "futon 3" }),
        ["cold"] = (new[] { "igloo 8" }, new[] { "cloud 6", "donut 3" }),
        ["burrow"] = (new[] { "rounddoor 6", "wicker 3", "mushroom 3", "strawberry 3" }, new[] { "leaf 6", "basket 3", "flower 3" }),
        ["forest"] = (new[] { "mushroom 6", "teepee 6", "rounddoor 3" }, new[] { "leaf 6", "tassel 3", "flower 3" }),
        ["water"] = (new[] { "teapot 6", "igloo 3", "wicker 3" }, new[] { "leaf 6", "cloud 3" }),
        ["farm"] = (new[] { "wicker 6", "kennel 3", "strawberry 3" }, new[] { "flower 6", "basket 3", "futon 3" }),
        ["fairy"] = (new[] { "mushroom 6", "strawberry 6", "teapot 6" }, new[] { "cloud 6", "heart 6", "flower 3" }),
        ["imp"] = (new[] { "teepee 6", "box 3", "teapot 3" }, new[] { "tassel 6", "futon 3", "egg 3" }),
    };

    // 종별 덮어쓰기: 그 물건의 무게를 이 값으로 (가족 무게 대신). 새 넷(병아리·오리·부엉이·펭귄)은 방석 egg 0 — 달걀프라이 위에 앉는 건 좀 그렇다
    static readonly Dictionary<string, string[]> Overrides = new()
    {
        ["hamster"] = new[] { "rounddoor 10" },
        ["mushroom"] = new[] { "mushroom 10" },
        ["octopus"] = new[] { "teapot 8" },
        ["chick"] = new[] { "egg 0" }, ["duck"] = new[] { "egg 0" }, ["owl"] = new[] { "egg 0" }, ["penguin"] = new[] { "egg 0" },
    };

    public static string? FamilyOf(string species)
    {
        species = PetArt.Base(species);
        foreach (var (f, list) in Families) if (Array.IndexOf(list, species) >= 0) return f;
        return null;
    }

    public static IEnumerable<string> FamilySpecies => Families.SelectMany(f => f.Species);

    // 그 종이 그 물건을 받을 무게 (0 이면 안 받는다)
    public static int Weight(string id, string species)
    {
        species = PetArt.Base(species);
        if (Overrides.TryGetValue(species, out var ov))
            foreach (var w in ov) { var p = w.Split(' '); if (p[0] == id) return int.Parse(p[1]); }
        if (FamilyOf(species) is { } f && Weights.TryGetValue(f, out var fw))
            foreach (var w in IsHouse(id) ? fw.House : fw.Cushion) { var p = w.Split(' '); if (p[0] == id) return int.Parse(p[1]); }
        return 1;
    }

    // 무엇을 받나. 목록은 늘 표 순서로 돈다 (HashSet 순서에 기대지 않는다) — 씨가 같으면 같은 뽑기.
    // 후보 = 그 종류 10개 중 아직 안 받은 것 (다 받았으면 10개 전부 — 같은 것을 또 받아도 그게 놓인다), 무게대로 하나, 후보 무게가 모두 0 이면 모두 1 로
    public static string Pick(bool house, string species, IReadOnlyCollection<string> got, Random rng)
    {
        var all = house ? Houses : Cushions;
        var pool = all.Where(id => !got.Contains(id)).ToList();
        if (pool.Count == 0) pool = all.ToList();
        var w = pool.Select(id => Weight(id, species)).ToList();
        if (w.Sum() == 0) w = pool.Select(_ => 1).ToList();
        int roll = rng.Next(w.Sum());
        for (int i = 0; i < pool.Count; i++) { if (roll < w[i]) return pool[i]; roll -= w[i]; }
        return pool[^1];
    }

    // 줄 것이 있나: 어린이 이상이고 방석을 아직 안 받았으면 방석, 다 자랐고 집을 아직 안 받았으면 집 (방석 다음). 이 규칙 하나로
    // 자라는 순간·이미 어린이·어른인 옛 펫의 첫 실행·디버그 set stage 가 다 된다
    public static string? Owed(string stage, bool cushionGiven, bool houseGiven)
    {
        if (stage is "egg" or "baby") return null;
        if (!cushionGiven) return "cushion";
        if (Pet.Grown(stage) && !houseGiven) return "house";
        return null;
    }

    // ── 지금 상태 ──
    public readonly List<string> Got = new();
    public string? Active;                           // 지금 화면에 놓인 하나 (방석이든 집이든). null 이면 없음
    public double[]? At;                             // 그 자리 [x, y] (그림 아래 가운데·화면 좌표). null 이면 기본 자리
    public bool CushionGiven, HouseGiven, Hidden;

    public bool ActiveHouse => Active != null && IsHouse(Active);
    public string? Cushion => Active != null && !IsHouse(Active) ? Active : null;   // 놓인 것이 방석이면 그 id
    public string? House => ActiveHouse ? Active : null;                             // 놓인 것이 집이면 그 id

    public bool Has(string id) => Got.Contains(id);
    public string? Placed(bool house) => house ? House : Cushion;
    public IEnumerable<string> Received(bool house) => Got.Where(id => IsHouse(id) == house); // 받은 순서

    public void Receive(string id) { if (Known(id) && !Got.Contains(id)) Got.Add(id); }

    // 이 하나를 화면에 놓는다 (방석이면 집이 빠지고, 집이면 방석이 빠진다). 자리는 그대로
    public void Place(string id)
    {
        Receive(id);
        Active = id;
    }

    public void NewPet() => (CushionGiven, HouseGiven) = (false, false); // 새 알: 수집·놓인 것·자리는 그대로 (놓인 것은 그 아이가 받을 때 바뀐다)

    public void Clear()
    {
        Got.Clear();
        (Active, At, CushionGiven, HouseGiven) = (null, null, false, false);
    }

    public void Load(HomeSave? s)
    {
        Clear();
        if (s == null) return;
        foreach (var id in s.Got ?? new()) Receive(id); // 모르는 id(나중에 없앤 물건)는 빠진다
        Active = s.Active is { } a && Known(a) ? a : null;
        At = s.At is { Length: 2 } at ? at : null;
        if (Active == null) // 방석·집이 따로 놓이던 옛 저장: 둘 다 있으면 집(어른)을 그 집 자리에, 하나뿐이면 그것을 제 자리에
        {
            string? c = s.Cushion is { } c0 && Known(c0) && !IsHouse(c0) ? c0 : null;
            string? h = s.House is { } h0 && Known(h0) && IsHouse(h0) ? h0 : null;
            double[]? ca = s.CushionAt is { Length: 2 } ? s.CushionAt : null, ha = s.HouseAt is { Length: 2 } ? s.HouseAt : null;
            if (h != null) (Active, At) = (h, ha ?? ca);
            else if (c != null) (Active, At) = (c, ca);
        }
        if (Active != null) Receive(Active);
        (CushionGiven, HouseGiven, Hidden) = (s.CushionGiven, s.HouseGiven, s.Hidden);
    }

    public HomeSave Snapshot() => new(Got.ToList(), CushionGiven, HouseGiven, Hidden, Active, At);
}
