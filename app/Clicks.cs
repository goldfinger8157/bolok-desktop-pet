namespace Bolok;

// 클릭 반응 고르기. 누른 곳·연타 횟수·성격·기분으로 정한다. 순수 계산이라 --selfcheck 로 검사한다
public static class Clicks
{
    // 칸(40×40) 안에서 몸이 시작되는 높이와 몸 너비의 절반 (그림 데이터의 몸 크기를 따른다: 발은 37, 몸 위는 그 위로 지름만큼)
    static (double Top, double Half) Body(string stage)
    {
        var (rx, ry) = PetArt.Embedded.Size(stage);
        return (36 - 2 * ry, rx);
    }

    public static string Zone(string stage, double x, double y)
    {
        var (top, half) = Body(stage);
        double t = (y - top) / (37 - top);
        if (t < .36) return "head";
        if (t > .86) return "feet";
        return Math.Abs(x - 20.5) > half * .55 ? "cheek" : "belly";
    }

    public static string Pick(string zone, int combo, string stage, double mood, string? last, Random rng)
    {
        bool grumpy = stage == "adult_grumpy";
        var w = new Dictionary<string, double>();
        if (combo >= (grumpy ? 5 : 9))
        {
            w["pout"] = 50;
            w["nope"] = 35;
            if (grumpy) w["nip"] = 45;
        }
        else if (combo >= 5)
        {
            w["hop"] = 35;
            w["spin"] = 30;
            w["giggle"] = 25;
            if (mood >= 50) w["love"] = 15;
        }
        else
        {
            switch (zone)
            {
                case "head": w["nuzzle"] = 40; w["happy"] = 25; w["love"] = 20; w["wink"] = 15; break;
                case "cheek": w["squish"] = 70; w["giggle"] = 15; w["wink"] = 15; break;
                case "belly": w["giggle"] = 50; w["flop"] = 35; w["happy"] = 15; break;
                default: w["hop"] = 60; w["giggle"] = 25; w["spin"] = 15; break;
            }
            if (stage == "adult_sweet")
            {
                w["love"] = w.GetValueOrDefault("love") + 40;
                w["wink"] = w.GetValueOrDefault("wink") + 25;
            }
            if (stage == "adult_playful") // 장난꾸러기: 누르면 폴짝·빙글·킥킥
            {
                w["hop"] = w.GetValueOrDefault("hop") + 35;
                w["spin"] = w.GetValueOrDefault("spin") + 25;
                w["giggle"] = w.GetValueOrDefault("giggle") + 20;
            }
            if (grumpy)
            {
                w.Remove("love");
                w["pout"] = 40;
                w["nip"] = 25;
            }
            if (stage == "baby") { w.Remove("wink"); if (w.ContainsKey("giggle")) w["giggle"] *= 2; }
            if (mood < 30) { w.Remove("hop"); w.Remove("spin"); w["pout"] = w.GetValueOrDefault("pout") + 25; }
        }
        if (w.Count > 1 && last != null) w.Remove(last); // 같은 반응이 연달아 나오지 않게

        double at = rng.NextDouble() * w.Values.Sum();
        foreach (var (kind, weight) in w.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            at -= weight;
            if (at < 0) return kind;
        }
        return w.Keys.First();
    }
}
