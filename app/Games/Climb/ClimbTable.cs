using System.Text;

namespace Bolok;

// 클라이밍 봇 표 (docs/climb.md 7장): 씨 N 개 × 봇 둘. 같은 씨면 같은 표 (--mode-scene climb x.png table[:씨 수[:손잡이]])
static partial class ClimbCheck
{
    internal const int DefaultSeeds = 500;

    // 한 판 (잘하는·느긋한 봇 모두 끝까지: 시간·틀림으로 끝난다). maxLeaves: 이 잎에서 멈춘다 (검사용 봇)
    internal static Climb Play(int seed, in ClimbSkill skill, ClimbTune? tune = null, int maxLeaves = int.MaxValue, int maxSec = 400)
    {
        var t = new Climb(seed, tune);
        var bot = new ClimbBotState(seed * 31 + 5);
        for (int i = 0; i < 60 * maxSec && !t.Done && t.N < maxLeaves; i++)
        {
            ClimbBot.Play(t, skill, bot);
            t.Update(Climb.Tick);
        }
        return t;
    }

    internal sealed class Tally
    {
        public readonly List<int> Leaves = new();
        public readonly List<double> Secs = new();
        public int Wrong, Time, Wilt, Berries, Saves, Lifts;
        public void Add(Climb t)
        {
            Leaves.Add(t.N); Secs.Add(t.RunClock);
            switch (t.Why) { case Climb.Cause.Wrong: Wrong++; break; case Climb.Cause.Time: Time++; break; case Climb.Cause.Wilt: Wilt++; break; }
            Berries += t.Berries; Saves += t.Saves; Lifts += t.Lifts;
        }
        public int N => Leaves.Count;
        public double Avg => Leaves.Average();
        public double MedianLeaves => Median(Leaves.Select(x => (double)x));
        public double MedianSec => Median(Secs);
        public double Past(int n) => Leaves.Count(x => x >= n) / (double)Math.Max(1, N);
        public double SecP95 => Secs.OrderBy(x => x).ElementAt((int)(Secs.Count * .95));
        static double Median(IEnumerable<double> v) { var a = v.OrderBy(x => x).ToArray(); return a.Length == 0 ? 0 : a[a.Length / 2]; }
    }

    internal static Tally Run(int seeds, in ClimbSkill skill, ClimbTune? tune = null)
    {
        var tally = new Tally();
        for (int s = 1; s <= seeds; s++) tally.Add(Play(s, skill, tune));
        return tally;
    }

    // 손잡이 맞출 때: table:<씨 수>:키=값,키=값 (ClimbTune 의 이름. e.same·e.flip·e.noise·e.err·e.errflip·e.fog 처럼 앞에 e./g. 를 붙이면 느긋한·잘하는 봇)
    static (ClimbTune, ClimbSkill, ClimbSkill) Overrides(string? spec)
    {
        object tune = new ClimbTune();
        var e0 = ClimbSkill.Easy; var g0 = ClimbSkill.Good;
        if (spec != null)
            foreach (var kv in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var (k, vs) = (kv.Split('=')[0], kv.Split('=')[1]);
                double v = vs is "true" or "false" ? (vs == "true" ? 1 : 0) : double.Parse(vs, System.Globalization.CultureInfo.InvariantCulture);
                ClimbSkill Set(ClimbSkill s, string name) => name switch { "same" => s with { Same = v }, "flip" => s with { Flip = v }, "noise" => s with { Noise = v }, "err" => s with { ErrSame = v }, "errflip" => s with { ErrFlip = v }, "fog" => s with { FogErr = v }, "warm" => s with { Warm = v }, _ => s };
                if (k.StartsWith("e.")) { e0 = Set(e0, k[2..]); continue; }
                if (k.StartsWith("g.")) { g0 = Set(g0, k[2..]); continue; }
                var prop = tune.GetType().GetProperty(k)!;
                prop.SetValue(tune, prop.PropertyType == typeof(bool) ? v != 0 : Convert.ChangeType(v, prop.PropertyType));
            }
        return ((ClimbTune)tune, e0, g0);
    }

    internal static string Table(int seeds, bool full = false, string? spec = null)
    {
        var (tune, easy, good) = Overrides(spec);
        var sb = new StringBuilder();
        sb.AppendLine($"클라이밍 봇 표 — 씨 {seeds}개");
        sb.AppendLine("봇 | 평균 잎 | 가운데 값 | 가운데 판 길이 | 20잎 넘김 | 100잎 넘김 | 끝난 까닭 (틀림·시간·시든 잎) | 열매/판 | 홀씨 살아남/판 | 덩굴손/판 | 95% 길이");
        foreach (var skill in new[] { easy, good }) sb.AppendLine(Row(skill.Name, Run(seeds, skill, tune)));
        sb.AppendLine("꺾임 비율 (구간 0~29 · 30~149 · 150~299 · 300~): " + string.Join(" · ", FlipRatios(tune).Select(x => $"{x * 100:0.0}%")));
        if (full)   // 기믹을 끄고 켠 표를 나란히: 기믹 하나가 가운데 값을 15% 넘게 깎으면 그 기믹을 누그러뜨린다
        {
            sb.AppendLine("[기믹 하나씩 끄기] 가운데 값 (켠 것 대비)");
            var all = new[] { easy, good }.Select(sk => Run(seeds, sk, tune).MedianLeaves).ToArray();
            foreach (var (name, off) in new (string, ClimbTune)[] { ("모두 끔", tune with { Gimmicks = false }), ("열매 끔", tune with { BerryFrom = 99999 }), ("홀씨 끔", tune with { PuffFrom = 99999 }), ("시든 잎 끔", tune with { WiltFrom = 99999 }), ("덩굴손 끔", tune with { CurlFrom = 99999 }), ("구름 띠 끔", tune with { CloudFrom = 99999 }) })
            {
                var m = new[] { easy, good }.Select(sk => Run(seeds, sk, off).MedianLeaves).ToArray();
                sb.AppendLine($"  {name}: {easy.Name} {m[0]:0} ({(m[0] / all[0] - 1) * 100:+0;-0;0}%) · {good.Name} {m[1]:0} ({(m[1] / all[1] - 1) * 100:+0;-0;0}%)");
            }
        }
        return sb.ToString().TrimEnd();
    }

    static string Row(string name, Tally t) =>
        $"{name} | {t.Avg,5:0.0} | {t.MedianLeaves,4:0} | {t.MedianSec,5:0.0}초 | {t.Past(20) * 100,5:0.0}% | {t.Past(100) * 100,5:0.0}% | {t.Wrong * 100.0 / t.N:0}% · {t.Time * 100.0 / t.N:0}% · {t.Wilt * 100.0 / t.N:0}% | {t.Berries / (double)t.N,4:0.0} | {t.Saves / (double)t.N,4:0.00} | {t.Lifts / (double)t.N,4:0.00} | {t.SecP95:0}초";

    // 구간별 꺾임 비율 (잎 하나가 앞 잎과 반대 쪽이 될 몫): 씨 300개 × 400잎
    internal static double[] FlipRatios(ClimbTune? tune = null)
    {
        int[] lo = { 1, 30, 150, 300 }, hi = { 29, 149, 299, 399 };
        var flips = new double[4]; var count = new double[4];
        for (int seed = 1; seed <= 300; seed++)
        {
            var t = new Climb(seed, tune);
            for (int i = 1; i < 400; i++)
                for (int z = 0; z < 4; z++)
                    if (i >= lo[z] && i <= hi[z]) { count[z]++; if (t.At(i + 1).Dir != t.At(i).Dir) flips[z]++; }
        }
        return Enumerable.Range(0, 4).Select(z => flips[z] / count[z]).ToArray();
    }
}
