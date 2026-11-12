namespace Bolok;

// 마우스 손짓 (YELL 2026-11-13: 클릭만이 아니라 커서 움직임으로 쓰다듬기·툭툭·찌르기를 알아듣자).
// 앱이 매 프레임 커서 자리(볼록이 칸 좌표 0~40)·몸 위인지·어느 부분(머리·배·볼·발)인지를 넣으면, 손짓을 알아들었을 때 이름을 돌려준다.
//   쓰다듬기(stroke): 머리 위에서 좌우로 살살 — 1.5초 안에 좌우가 3번 넘게 바뀐다
//   툭툭(pat):        머리 위에서 위아래로 톡톡 — 좌우로는 거의 안 움직이고 위아래가 2번 넘게 바뀐다
//   찌르기(poke):     몸 밖에서 빠르게 들어와 곧바로 반대로 빠져나간다 (그냥 지나가는 커서는 아니다)
// 화면과 무관한 규칙이라 --selfcheck 로 검사한다
public sealed class Gestures
{
    readonly record struct Sample(double T, double X, double Y, bool Inside, string Zone);

    public const double Window = 1.5, Turn = 1.5, PokeSpeed = 120, PokeSec = .4, Cool = 1.0;

    readonly List<Sample> trail = new();
    double cool, enterAt = -1;
    (double X, double Y) enterV;

    public void Reset() { trail.Clear(); enterAt = -1; }

    public string? Feed(double t, double x, double y, bool inside, string zone)
    {
        var s = new Sample(t, x, y, inside, zone);
        string? found = null;
        if (trail.Count > 0)
        {
            var last = trail[^1];
            double dt = Math.Max(1e-3, t - last.T);
            (double X, double Y) v = ((x - last.X) / dt, (y - last.Y) / dt);
            if (!last.Inside && inside) (enterAt, enterV) = (t, v); // 몸으로 들어왔다
            else if (last.Inside && !inside) // 나갔다: 빠르게 들어와 금방 반대로 나갔으면 찌르기
            {
                double a = Len(enterV), b = Len(v);
                if (enterAt >= 0 && t - enterAt < PokeSec && a > PokeSpeed && b > 1 && (enterV.X * v.X + enterV.Y * v.Y) < -.3 * a * b) found = "poke";
                enterAt = -1;
            }
        }
        trail.Add(s);
        trail.RemoveAll(q => t - q.T > Window);
        if (found == null) found = Strokes(trail);
        if (found == null || t < cool) return null;
        cool = t + Cool;
        Reset();
        return found;
    }

    static double Len((double X, double Y) v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);

    // 머리 위에 머문 자리들로 좌우·위아래로 몇 번 오갔는지 센다 (짧게 떨린 건 안 센다)
    static string? Strokes(List<Sample> trail)
    {
        var head = trail.Where(q => q.Inside && q.Zone == "head").ToList();
        if (head.Count < 6 || head[^1].T - head[0].T < .5) return null;
        int Turns(Func<Sample, double> pos)
        {
            int turns = 0, dir = 0;
            double run = 0;
            for (int i = 1; i < head.Count; i++)
            {
                double d = pos(head[i]) - pos(head[i - 1]);
                if (Math.Abs(d) < .3) continue;
                int sd = Math.Sign(d);
                if (dir != 0 && sd != dir) { if (run >= Turn) turns++; run = 0; }
                dir = sd;
                run += Math.Abs(d);
            }
            return turns;
        }
        double spanX = head.Max(q => q.X) - head.Min(q => q.X), spanY = head.Max(q => q.Y) - head.Min(q => q.Y);
        if (spanX >= spanY && Turns(q => q.X) >= 3) return "stroke";
        if (spanX < 5 && spanY >= 2 && Turns(q => q.Y) >= 2) return "pat";
        return null;
    }

    // ── 자가 검사: 손짓을 흉내 내서 넣어 본다 (30fps) ──
    public static string Check()
    {
        static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: 마우스 손짓 — " + what); }
        static string Zone(double x, double y) => y < 20 ? "head" : "belly";
        static bool Body(double x, double y) => x is >= 9 and < 32 && y is >= 10 and < 37;
        string? Run(Func<double, (double X, double Y)> path, double sec)
        {
            var g = new Gestures();
            string? got = null;
            for (double t = 0; t <= sec; t += 1 / 30.0)
            {
                var (x, y) = path(t);
                got ??= g.Feed(t, x, y, Body(x, y), Zone(x, y));
            }
            return got;
        }
        Expect(Run(t => (20.5 + 6 * Math.Sin(t * 2 * Math.PI * 2.2), 14), 1.6) == "stroke", "머리 위 좌우로 살살 → 쓰다듬기");
        Expect(Run(t => (20.5 + .4 * Math.Sin(t * 9), 14 + 2.5 * Math.Sin(t * 2 * Math.PI * 2.5)), 1.2) == "pat", "머리 위 위아래 톡톡 → 툭툭");
        Expect(Run(t => (t < .12 ? -5 + t / .12 * 22 : 17 - (t - .12) / .12 * 22, 26), .3) == "poke", "빠르게 콕 찔렀다 빼기 → 찌르기");
        Expect(Run(t => (-5 + t * 250, 26), .25) == null, "빠르게 지나가는 커서는 아무것도 아니다");
        Expect(Run(t => (20 + t * .5, 14), 2) == null, "머리 위에 가만히 있으면 아무것도 아니다");
        Expect(Run(t => (20.5 + 6 * Math.Sin(t * 2 * Math.PI * 2.2), 30), 1.6) == null, "배 위에서 흔드는 건 쓰다듬기가 아니다");
        var cool = new Gestures();
        int n = 0;
        for (double t = 0; t < 2.5; t += 1 / 30.0) if (cool.Feed(t, 20.5 + 6 * Math.Sin(t * 2 * Math.PI * 2.2), 14, true, "head") != null) n++;
        Expect(n is >= 1 and <= 2, $"계속 쓰다듬어도 1초에 한 번까지 ({n}번)");
        return "마우스 손짓: 쓰다듬기·툭툭·찌르기 알아듣고, 지나가거나 가만히 있는 커서는 무시";
    }
}
