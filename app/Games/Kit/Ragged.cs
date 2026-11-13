namespace Bolok;

// 들쭉날쭉한 폭발 모양 (순수 수학: 규칙과 그림이 같이 쓴다. --selfcheck 로 검사)
// 동그라미 반지름에 각도마다 다른 값 K(0.65~1.35)를 곱한다. 자리(씨)마다 모양이 다르고, 같은 자리는 늘 같은 모양이다.
// ICBM 구멍(Overlay.CraterShape)에서 가져왔다: 씨와 난수 순서가 그대로라 같은 자리의 구멍 모양이 전과 같다
readonly struct Ragged
{
    readonly int seed;
    readonly double p3, p5, p7, p11;

    Ragged(int seed)
    {
        this.seed = seed;
        var rng = new Random(seed);
        (p3, p5, p7, p11) = (rng.NextDouble() * 6.3, rng.NextDouble() * 6.3, rng.NextDouble() * 6.3, rng.NextDouble() * 6.3);
    }

    public static Ragged At(double x, double y) => new((int)(x * 7 + y * 13)); // 터진 자리로 정한다
    public static Ragged Seeded(int seed) => new(seed);

    // 그 각도 쪽 반지름 배수
    public double K(double angle) => 1 + .13 * Math.Sin(3 * angle + p3) + .09 * Math.Sin(5 * angle + p5) + .07 * Math.Sin(7 * angle + p7) + .06 * Math.Sin(11 * angle + p11);

    // 가운데에서 (dx, dy) 떨어진 점이 반지름 r 짜리 폭발 안인가
    public bool Inside(double dx, double dy, double r)
    {
        double lim = r * K(Math.Atan2(dy, dx));
        return dx * dx + dy * dy < lim * lim;
    }

    // 칸(cell px) 격자로 본 모양: 가운데가 칸 (0,0) 의 왼쪽 위 모서리. Core = 뻥 뚫린 안쪽, 아니면 한 칸 두께 가장자리
    public List<(int I, int J, bool Core)> Cells(double r, int cell)
    {
        var cells = new List<(int, int, bool)>();
        int n = (int)Math.Ceiling(r * 1.4 / cell) + 1;
        for (int j = -n; j <= n; j++)
            for (int i = -n; i <= n; i++)
            {
                double x = (i + .5) * cell, y = (j + .5) * cell, d = Math.Sqrt(x * x + y * y), lim = r * K(Math.Atan2(y, x));
                if (d < lim - cell) cells.Add((i, j, true));
                else if (d < lim) cells.Add((i, j, false));
            }
        return cells;
    }

    // 가장자리에서 바깥으로 지그재그로 뻗는 금 3~5줄 (half px 칸의 왼쪽 위 자리). r0: 처음 반지름 (줄어든 구멍은 금도 짧다)
    public List<(int X, int Y)> Cracks(double r, double r0, int half)
    {
        var cracks = new List<(int, int)>();
        var rng = new Random(seed);
        for (int i = 0; i < 4; i++) rng.NextDouble(); // 모양에 쓴 난수 넷 다음부터 (전과 같은 순서)
        int count = 3 + rng.Next(3);
        for (int m = 0; m < count; m++)
        {
            double a = rng.NextDouble() * Math.PI * 2, d = r * K(a) - half, side = 0;
            int len = (int)((5 + rng.Next(6)) * r / Math.Max(r0, 1));
            for (int t = 0; t < len; t++, d += half)
            {
                side = Math.Clamp(side + (rng.NextDouble() - .5) * 1.2, -1.5, 1.5);
                double x = Math.Cos(a) * d - Math.Sin(a) * side * half, y = Math.Sin(a) * d + Math.Cos(a) * side * half;
                cracks.Add(((int)Math.Floor(x / half) * half, (int)Math.Floor(y / half) * half));
            }
        }
        return cracks;
    }
}
