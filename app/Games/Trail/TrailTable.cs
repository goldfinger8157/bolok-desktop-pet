using System.Text;

namespace Bolok;

// 눈길 마중 봇 표 (docs/ice-run.md 7장): 씨 N 개 × 단계 × 봇 둘. 같은 씨면 같은 표 (--mode-scene trail x table[:씨 수[:full]])
static partial class TrailCheck
{
    internal const int DefaultSeeds = 200;

    internal sealed class Tally
    {
        public int N, Wins, HitSum, LegAttempts;
        public double WinSec, DashShare, GatesSum;
        public int Timeouts;
        public void Add(Trail t)
        {
            N++;
            if (t.State == Trail.Phase.Win) { Wins++; WinSec += t.RunClock; }
            if (t.TimeOut) Timeouts++;
            GatesSum += t.Leg;
            HitSum += t.Hits;
            LegAttempts += t.State == Trail.Phase.Win ? t.T.Legs : t.Leg + 1;
            DashShare += t.RunSec > 0 ? t.DashSec / t.RunSec : 0;
        }
        public double WinRate => Wins / (double)Math.Max(1, N);
        public double AvgGates => GatesSum / Math.Max(1, N);
        public double HitsPerLeg => HitSum / (double)Math.Max(1, LegAttempts);
        public double AvgWinSec => WinSec / Math.Max(1, Wins);
        public double Dash => DashShare / Math.Max(1, N);
    }

    // 몸 크기: 어린이(rx 9.8). 형태를 바꿔 견줄 때 rx 를 넘긴다
    internal static Trail Play(int seed, int stage, in TrailSkill skill, double bodyHalf = 9.8, TrailTune? tune = null)
    {
        var t = new Trail(seed, stage, bodyHalf, tune);
        var bot = new TrailBotState(seed * 31 + 5);
        for (int i = 0; i < 60 * 400 && !t.Done; i++)
        {
            TrailBot.Play(t, skill, bot);
            t.Update(Trail.Tick);
        }
        return t;
    }

    internal static Tally Run(int seeds, int stage, in TrailSkill skill, double bodyHalf = 9.8, TrailTune? tune = null)
    {
        var tally = new Tally();
        for (int s = 1; s <= seeds; s++) tally.Add(Play(s, stage, skill, bodyHalf, tune));
        return tally;
    }

    // 손잡이 맞출 때: table:<씨 수>:<full|->:키=값,키=값 (TrailTune 의 이름, e.look 처럼 앞에 e./g. 를 붙이면 느긋한·잘하는 봇)
    static (TrailTune, TrailSkill, TrailSkill) Overrides(string? spec)
    {
        object tune = new TrailTune();
        if (spec != null)
            foreach (var kv in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var (k, v) = (kv.Split('=')[0], double.Parse(kv.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture));
                if (k.StartsWith("e.") || k.StartsWith("g.")) { /* 봇은 아래에서 */ continue; }
                tune.GetType().GetProperty(k)!.SetValue(tune, Convert.ChangeType(v, tune.GetType().GetProperty(k)!.PropertyType));
            }
        var e0 = TrailSkill.Easy; var g0 = TrailSkill.Good;
        if (spec != null)
            foreach (var kv in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var (k, v) = (kv.Split('=')[0], double.Parse(kv.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture));
                if (k.StartsWith("e.")) e0 = k[2..] switch { "look" => e0 with { Look = v }, "react" => e0 with { React = v }, "noise" => e0 with { Noise = v }, _ => e0 };
                if (k.StartsWith("g.")) g0 = k[2..] switch { "look" => g0 with { Look = v }, "react" => g0 with { React = v }, "noise" => g0 with { Noise = v }, _ => g0 };
            }
        return ((TrailTune)tune, e0, g0);
    }

    internal static string Table(int seeds, bool full, string? spec = null)
    {
        var (tune, easy, good) = Overrides(spec);
        var sb = new StringBuilder();
        int[] stages = full ? new[] { 1, 2, 3, 4, 5, 7, 10 } : new[] { 1, 3, 5 };
        sb.AppendLine($"아이스런 봇 표 — 씨 {seeds}개, 어린이 몸");
        foreach (var skill in new[] { easy, good })
        {
            sb.AppendLine($"[{skill.Name}] 단계 | 완주 | 평균 닿은 쉼터 | 구간당 넘어짐 | 완주 판 길이 | 질주 비율");
            foreach (int st in stages)
            {
                var t = Run(seeds, st, skill, 9.8, tune);
                sb.AppendLine($"  {st,2}단계 | {t.WinRate * 100,5:0.0}% | {t.AvgGates,4:0.00} | {t.HitsPerLeg,4:0.00} | {(t.Wins > 0 ? t.AvgWinSec.ToString("0") + "초" : "-"),5} | {t.Dash * 100,3:0}%");
            }
        }
        if (full)
        {
            sb.AppendLine("[몸 크기 견줌] 1단계 (아기 9.2 · 어린이 9.8 · 어른 12.5 · 통통 14)");
            foreach (var skill in new[] { easy, good })
                sb.AppendLine($"  {skill.Name}: " + string.Join("  ", new[] { 9.2, 9.8, 12.5, 14.0 }.Select(rx => $"{Run(seeds, 1, skill, rx, tune).WinRate * 100:0}%")));
        }
        return sb.ToString().TrimEnd();
    }
}
