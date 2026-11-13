using System.Diagnostics;

namespace Bolok;

// 게임 모듈 속도 재기 (화면을 띄우지 않는다): Bolok.exe --mode-bench <id>
// 한 판을 자동 조종으로 끝까지 돌리며 프레임마다 규칙·다시 칠할 자리·그리기에 걸린 시간과 새로 잡은 메모리를 잰다.
// 자동 조종(step)이 쓴 시간·메모리는 빼고 잰다. 고치기 전후를 같은 씨로 견준다
static class Bench
{
    // step: 자동 조종 한 번 (끝났으면 false), update: 규칙 한 프레임. 그림은 모니터 크기 그림에 판 자리만 그린다
    public static string Run(string id, Overlay ov, IGameMode game, Rectangle field, Func<bool> step, Action update, int maxFrames = 60 * 240)
    {
        using var bmp = new Bitmap(ov.Width, ov.Height);
        using var g = Graphics.FromImage(bmp);
        g.SetClip(ov.Client(Rectangle.Inflate(field, 20, 20)));
        // 실제 창은 화면과 같은 종류의 버퍼(더블 버퍼)에 그린다. 화면에는 내보내지 않고 그 버퍼에 그리는 시간도 잰다
        using var desk = Graphics.FromHwnd(IntPtr.Zero);
        using var ctx = new BufferedGraphicsContext();
        using var buf = ctx.Allocate(desk, new Rectangle(0, 0, ov.Width, ov.Height));
        buf.Graphics.SetClip(ov.Client(Rectangle.Inflate(field, 20, 20)));
        long tBuf = 0;
        var rects = new List<Rectangle>(512);
        var cells = new HashSet<(int, int)>();
        long tUpdate = 0, tDirty = 0, tDraw = 0, bytes = 0, dirtyCells = 0, worst = 0;
        int frames = 0;
        for (int warm = 0; warm < 30 && step(); warm++) { update(); rects.Clear(); game.Dirty(ov, rects); ov.Render(g); } // 처음 그림 만들기·JIT 는 빼고
        int gc0 = GC.CollectionCount(0);
        for (; frames < maxFrames && step(); frames++)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread(), t0 = Stopwatch.GetTimestamp();
            update();
            long t1 = Stopwatch.GetTimestamp();
            rects.Clear();
            game.Dirty(ov, rects);
            long t2 = Stopwatch.GetTimestamp();
            ov.Render(g);
            long t3 = Stopwatch.GetTimestamp();
            bytes += GC.GetAllocatedBytesForCurrentThread() - a0;
            (tUpdate, tDirty, tDraw, worst) = (tUpdate + t1 - t0, tDirty + t2 - t1, tDraw + t3 - t2, Math.Max(worst, t3 - t0));
            ov.Render(buf.Graphics);
            tBuf += Stopwatch.GetTimestamp() - t3;
            cells.Clear(); // Overlay 가 하듯 64px 칸으로 모은 넓이
            foreach (var r in rects)
                for (int y = r.Top / 64; y <= (r.Bottom - 1) / 64; y++)
                    for (int x = r.Left / 64; x <= (r.Right - 1) / 64; x++) cells.Add((x, y));
            dirtyCells += cells.Count;
        }
        double Us(long ticks) => ticks * 1e6 / Stopwatch.Frequency / Math.Max(1, frames);
        return $"{id}: {frames}프레임, 규칙 {Us(tUpdate):0.0}µs, 칠할 자리 {Us(tDirty):0.0}µs, 그리기 {Us(tDraw):0.0}µs (화면 버퍼에는 {Us(tBuf):0.0}µs), 가장 느린 프레임 {worst * 1e3 / Stopwatch.Frequency:0.00}ms, " +
               $"메모리 {bytes / Math.Max(1, frames)}B/프레임 (GC {GC.CollectionCount(0) - gc0}번), 다시 칠한 넓이 {dirtyCells * 64.0 * 64 / Math.Max(1, frames) / 1000:0.0}천px²/프레임";
    }
}
