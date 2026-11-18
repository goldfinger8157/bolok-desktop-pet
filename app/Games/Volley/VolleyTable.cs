using System.Drawing;
using System.Text;

namespace Bolok;

// 배구 봇 표 (docs/volley-tune.md 8장): 코트 셋 × 묶음 셋. 같은 씨면 같은 표 (--mode-scene volley x table[:판 수[:steps]], --mode-bench volley)
static partial class VolleyCheck
{
    internal const int DefaultSeeds = 400;

    sealed class Tally
    {
        public int N, Unfinished;
        public double Wins;
        public readonly long[] Points = new long[2 * VolleySim.PointKinds];
        public long PointTotal, HitSum, ScoreSum;
        public double RallySec;
        public readonly long[] Stone = new long[2], Trip = new long[2], BlockHit = new long[2], BlockMiss = new long[2], Spike = new long[2], SpikeOver = new long[2];

        public void Add(VolleySim w)
        {
            N++;
            if (!w.Done) Unfinished++;
            Wins += w.Done ? (w.Won ? 1 : 0) : w.ScorePlayer > w.ScoreRival ? 1 : w.ScorePlayer == w.ScoreRival ? .5 : 0;
            for (int i = 0; i < Points.Length; i++) Points[i] += w.Points[i];
            PointTotal += w.PointTotal; HitSum += w.HitSum; RallySec += w.RallySecSum;
            ScoreSum += w.ScorePlayer + w.ScoreRival;
            for (int i = 0; i < 2; i++)
            {
                Stone[i] += w.StoneCount[i]; Trip[i] += w.TripCount[i]; BlockHit[i] += w.BlockHit[i]; BlockMiss[i] += w.BlockMissCount[i];
                Spike[i] += w.SpikeCount[i]; SpikeOver[i] += w.SpikeOver[i];
            }
        }

        public double Kind(VolleySim.PointKind k) => Points[(int)k] + Points[VolleySim.PointKinds + (int)k];
        public double Rate => Wins / Math.Max(1, N);
        public double SpikeShare => Kind(VolleySim.PointKind.Spike) / Math.Max(1, PointTotal);
        public double SpikeCross => (SpikeOver[0] + SpikeOver[1]) / (double)Math.Max(1, Spike[0] + Spike[1]);
        public double StonesPerMatch => (Stone[0] + Stone[1]) / (double)Math.Max(1, N);
        public double TripShare => Kind(VolleySim.PointKind.Trip) / Math.Max(1, PointTotal);
        public double FlukeShare => (Kind(VolleySim.PointKind.Bird) + Kind(VolleySim.PointKind.Fumble)) / Math.Max(1, PointTotal);
    }

    // 한 판: 오른쪽 = 라이벌 (sim 의 Rnd), 왼쪽 = 같은 공용 봇 (따로 만든 Random 이라 경기의 Rnd 순서를 건드리지 않는다)
    internal static VolleySim PlayMatch(Rectangle court, bool desktop, VolleyTune tune, in BotSkill left, int seed)
    {
        var w = new VolleySim(court, desktop, 3, new Random(seed), tune) { Ball = VolleySim.PickBall(new Random(seed + 13)), Birds = true, BirdRng = new Random(seed + 7) };
        w.ResetMatch(seed % 2 == 0 ? VolleySim.Actor.Player : VolleySim.Actor.Rival); // 첫 서브는 씨의 짝·홀
        var rng = new Random(seed + 101);
        var bot = new BotState();
        for (int step = 0; step < 60 * 180 && !w.Done; step++) // 판당 최대 3분
        {
            w.BotInput(VolleySim.Actor.Player, left, ref bot, rng, out int axis, out bool jump, out bool spike);
            w.Update(1 / 60.0, axis, jump, spike);
        }
        return w;
    }

    static string Pct(double v) => $"{v * 100,5:0.0}%";

    static void Row(StringBuilder sb, string label, Tally t)
    {
        double n = Math.Max(1, t.N);
        string LR(long[] a, string f = "0.00") => $"L{(a[0] / n).ToString(f)}/R{(a[1] / n).ToString(f)}";
        sb.AppendLine($"{label,-18} 승률 {Pct(t.Rate)}  점수/판 {t.ScoreSum / n,4:0.0}  타구/랠리 {t.HitSum / (double)Math.Max(1, t.PointTotal),4:0.0}  랠리 {t.RallySec / Math.Max(1, t.PointTotal),4:0.0}초  " +
                      $"스파이크 넘김 {Pct(t.SpikeCross)} ({LR(t.Spike, "0.0")})  | 돌/판 {LR(t.Stone)}  넘어짐/판 {LR(t.Trip)}  블로킹 맞음/판 {LR(t.BlockHit)} 어긋남/판 {LR(t.BlockMiss)}  미완 {t.Unfinished}");
        string K(string name, VolleySim.PointKind k) => $"{name} {t.Kind(k) / Math.Max(1, t.PointTotal) * 100:0.0}%({t.Points[(int)k] / n:0.00}/{t.Points[VolleySim.PointKinds + (int)k] / n:0.00})";
        sb.AppendLine($"{"",-18} 득점 종류 전체%(판당 왼/오): " + string.Join("  ", K("스파이크", VolleySim.PointKind.Spike), K("블로킹", VolleySim.PointKind.Block), K("일반", VolleySim.PointKind.Normal),
            K("꼭지", VolleySim.PointKind.Fumble), K("블로킹실수", VolleySim.PointKind.BlockMiss), K("넘어짐", VolleySim.PointKind.Trip), K("새", VolleySim.PointKind.Bird), K("자책", VolleySim.PointKind.Own)));
    }

    // 표 만들기. steps: 고치기 전부터 하나씩 넣어 가며 (숫자 넷 → 스파이크 보조 → 블로킹 → 돌부리) 견준다
    internal static string Table(int seeds, bool steps)
    {
        var courts = new (string Name, Rectangle R, bool Desktop)[]
        {
            ("바탕화면 600×336", new Rectangle(0, 0, 600, 336), true),
            ("창 800×500", new Rectangle(0, 0, 800, 500), false),
            ("창 1400×900", new Rectangle(0, 0, 1400, 900), false),
        };
        var rows = steps
            ? new (string, VolleyTune, BotSkill)[]
            {
                ("전  (Old, 거울)", VolleyTune.Old, VolleyTune.Old.Rival),
                ("+숫자 넷 (거울)", VolleyTune.Numbers, VolleyTune.Numbers.Rival),
                ("+스파이크 보조", VolleyTune.WithAssist, VolleyTune.WithAssist.Rival),
                ("+블로킹", VolleyTune.WithBlocks, VolleyTune.WithBlocks.Rival),
                ("후  (Now, 거울)", VolleyTune.Now, VolleyTune.Now.Rival),
                ("후  (Now, 사람)", VolleyTune.Now, BotSkill.Human),
            }
            : new (string, VolleyTune, BotSkill)[]
            {
                ("전  (Old, 거울)", VolleyTune.Old, VolleyTune.Old.Rival),
                ("후  (Now, 거울)", VolleyTune.Now, VolleyTune.Now.Rival),
                ("후  (Now, 사람)", VolleyTune.Now, BotSkill.Human),
            };
        var sb = new StringBuilder();
        foreach (var (name, court, desktop) in courts)
        {
            sb.AppendLine($"[{name}, {seeds}판]");
            foreach (var (label, tune, left) in rows)
            {
                var t = new Tally();
                for (int seed = 1; seed <= seeds; seed++) t.Add(PlayMatch(court, desktop, tune, left, seed));
                Row(sb, label, t);
            }
        }
        return sb.ToString().TrimEnd();
    }

    // 표 인자: "table" · "table:200" · "table:200:steps"
    static bool TryTableArg(string? arg, out int seeds, out bool steps)
    {
        (seeds, steps) = (DefaultSeeds, false);
        if (arg == null || !arg.StartsWith("table")) return false;
        var parts = arg.Split(':');
        if (parts.Length > 1 && int.TryParse(parts[1], out int n) && n > 0) seeds = n;
        steps = parts.Length > 2 && parts[2] == "steps";
        return true;
    }

    // 돌·넘어짐·블로킹이 나오는 판에서 규칙 한 프레임이 새로 잡는 바이트 (봇 판단 포함, 그림은 뺀다)
    internal static long RuleBytesPerFrame(out int stones, out int trips, out int blocks)
    {
        var tune = VolleyTune.Now with { StoneChance = .6 };
        var court = new Rectangle(0, 0, 800, 500);
        var left = BotSkill.Human with { StoneHopProb = .3 };
        long bytes = 0, frames = 0;
        (stones, trips, blocks) = (0, 0, 0);
        for (int seed = 1; frames < 20000 && seed < 60; seed++)
        {
            var w = new VolleySim(court, false, 3, new Random(seed), tune) { Birds = true, BirdRng = new Random(seed + 7) };
            w.ResetMatch(VolleySim.Actor.Player);
            var rng = new Random(seed + 101);
            var bot = new BotState();
            for (int step = 0; step < 60 * 60 && !w.Done; step++)
            {
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                w.BotInput(VolleySim.Actor.Player, left, ref bot, rng, out int axis, out bool jump, out bool spike);
                w.Update(1 / 60.0, axis, jump, spike);
                long used = GC.GetAllocatedBytesForCurrentThread() - a0;
                if (seed > 1) { bytes += used; frames++; } // 첫 판은 JIT·처음 한 번 잡는 것을 빼고 잰다
            }
            stones += w.StoneCount[0] + w.StoneCount[1];
            trips += w.TripCount[0] + w.TripCount[1];
            blocks += w.BlockHit[0] + w.BlockHit[1] + w.BlockMissCount[0] + w.BlockMissCount[1];
        }
        return bytes / Math.Max(1, frames);
    }
}
