using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Bolok;

// ICBM 구멍 하나의 그림. 모양은 Kit 의 Ragged 가 정한다 (터진 자리가 씨라서 같은 자리는 늘 같은 모양이고, 고치며 줄어도 모양은 그대로다).
//  · 칸(12px): 뻥 뚫린 안쪽과 그을린 가장자리, 바깥으로 뻗는 금(6px). 칸을 가로줄로 이어 붙여 두 번에 칠한다
//  · 친구 구멍(Friend): 안쪽에 그 친구가 사는 자연(하늘·구름·숲·냇물·풀 언덕)이 터질 때부터 보이고, 친구가 풀 언덕 뒤에서 빼꼼한다.
//    자연은 구멍마다 점 그림 한 장에 미리 그려 두고(still), 움직이는 것(구름·냇물 반짝이)이 바뀐 프레임에만 그 자리 점만 고친다(live).
//    빼꼼하는 친구도 구멍 모양대로 잘라 둔 작은 그림 한 장이다. 화면에는 그 그림들만 찍는다 → 프레임마다 새로 잡는 메모리도, 수백 번 칠하기도 없다
// 자연 그림은 엽서(Memories.Card)가 쓰는 Overlay.PaintNature 와 같은 그림이다 (자가 검사가 점 하나까지 견준다): 그쪽을 고치면 PaintStill·Animate 도 같이 고친다
// 좌표는 화면 좌표. 구멍 가운데는 (int)X, (int)Y (그림 창이 그리던 자리와 같다)
sealed class CraterPic : IDisposable
{
    const int C = Icbm.Cell;
    const byte Sky = 0, Land = 1, Front = 2, Outside = 255;   // 점마다: 구름이 보이는 하늘 / 구름을 가리는 숲·풀밭·냇물 / 반짝이도 가리는 앞 언덕·나무 / 구멍 밖

    public readonly Icbm.Crater Hole;
    readonly int k, ps;                 // k: 볼록이 배율 (자연 도트 한 칸), ps: 빼꼼하는 친구 그림 배율
    readonly Point at;
    double builtR = double.NaN;

    // ── 모양 (구멍 가운데 기준) ──
    readonly List<Rectangle> holeRuns = new(), rimRuns = new();   // 안쪽 / 가장자리 + 금
    public Rectangle Bounds { get; private set; }                 // 그려지는 자리 (화면 좌표)
    public int Inner { get; private set; }                        // 안쪽 칸 수

    // ── 친구 구멍의 자연 ──
    readonly Rectangle buf;             // 점 그림 자리: 처음 크기일 때 안쪽 칸을 감싸는 상자 (줄어든 구멍도 이 안이다)
    readonly int[]? live, still;
    readonly byte[]? cover;
    readonly Bitmap? bmp;
    readonly int[] mids = Array.Empty<int>();   // 냇물 가운데 높이 (도트 줄마다)
    Rectangle box;                      // 지금 안쪽 칸을 감싸는 상자
    bool nature;                        // 자연이 보인다 (안쪽 칸이 하나라도 있다)
    int floor, stream, bank, seed;      // floor: 친구가 올라오는 풀 언덕 위 줄
    readonly int[] cloudW = new int[2], cloudSpan = new int[2], cloudPhase = new int[2], cloudY = new int[2];

    // 지금 live 에 그려 둔 것 (바뀌면 그 자리만 still 로 되돌리고 다시 그린다)
    readonly int[] cloudX = new int[2];
    int stepSeen;
    bool drawn;
    Rectangle sparkBox;

    // 빼꼼하는 친구: 도감 썸네일(눈 뜬 것·감은 것)과 몸이 그려진 부분, 지금 보이는 만큼만 담은 그림(peekLive)
    int[]? face, faceBlink, peekLive;
    Bitmap? peekBmp;
    int faceW, peekTop;
    Rectangle body, peekBox;    // peekBox: 보이는 자리 (화면 좌표)
    Point peekAt;               // 친구 그림 왼쪽 위
    bool faceTried, peekOn, peekBlink;

    public bool HeartOn { get; private set; }   // 다 올라온 친구 옆 작은 하트
    public Point HeartAt { get; private set; }  // 하트 칸 왼쪽 위
    Rectangle heartRect;

    static int Argb(int r, int g, int b) => unchecked((int)0xFF000000) | r << 16 | g << 8 | b;
    static readonly int White = Argb(255, 255, 255), CloudShade = Argb(222, 236, 250), Far = Argb(48, 112, 74), FarLight = Argb(70, 140, 90),
        Grass = Argb(120, 192, 96), Bank = Argb(86, 156, 92), Water = Argb(70, 146, 222), Spark = Argb(196, 230, 252),
        Hill = Argb(92, 172, 78), Blade = Argb(150, 214, 118), Trunk = Argb(112, 78, 50), Leaf = Argb(40, 120, 66), LeafLight = Argb(76, 156, 88);

    public static int PeekScale(int k) => Math.Max(1, (int)Math.Round((k + 1) * .6)); // 처음 크기(k+1)의 60% 쯤 (YELL: 너무 크다). 도트가 안 깨지게 정수 배율
    public static int HeartScale(int k) => Math.Max(1, PeekScale(k) - 1);

    // 구멍 + 금이 닿을 수 있는 가장 먼 거리 (가운데에서)
    public static int Reach(Icbm.Crater cr) => (int)(cr.R0 * 1.4) + 6 * C;

    public CraterPic(Icbm.Crater hole, int k)
    {
        (Hole, this.k, ps) = (hole, k, PeekScale(k));
        at = new Point((int)hole.X, (int)hole.Y);
        if (hole.Friend == null) return;
        int i0 = 0, j0 = 0, i1 = -1, j1 = -1; // 처음 크기일 때 안쪽 칸 상자
        foreach (var (i, j, core) in Ragged.At(hole.X, hole.Y).Cells(Math.Max(hole.R, hole.R0), C))
        {
            if (!core) continue;
            if (i1 < i0) (i0, j0, i1, j1) = (i, j, i, j);
            else (i0, j0, i1, j1) = (Math.Min(i0, i), Math.Min(j0, j), Math.Max(i1, i), Math.Max(j1, j));
        }
        if (i1 < i0) return;
        buf = Rectangle.FromLTRB(at.X + i0 * C, at.Y + j0 * C, at.X + (i1 + 1) * C, at.Y + (j1 + 1) * C);
        live = GC.AllocateArray<int>(buf.Width * buf.Height, pinned: true); // 그림이 이 메모리를 바로 읽는다: 점을 고치면 그림이 바뀐다
        still = new int[live.Length];
        cover = new byte[live.Length];
        mids = new int[buf.Width / k + 2];
        bmp = new Bitmap(buf.Width, buf.Height, buf.Width * 4, PixelFormat.Format32bppPArgb, Marshal.UnsafeAddrOfPinnedArrayElement(live, 0));
    }

    public void Dispose()
    {
        bmp?.Dispose();
        peekBmp?.Dispose();
    }

    // 구멍 크기가 바뀌었으면 모양을 다시 뽑는다. 바뀐 자리(옛 모양 + 새 모양)를 dirty 에 담는다
    public void Shape(List<Rectangle> dirty)
    {
        if (Hole.R == builtR) return;
        builtR = Hole.R;
        var old = Bounds;
        holeRuns.Clear();
        rimRuns.Clear();
        (Inner, box, nature) = (0, Rectangle.Empty, false);
        int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
        if (Hole.R >= C)
        {
            var shape = Ragged.At(Hole.X, Hole.Y);
            var cells = shape.Cells(Hole.R, C);
            for (int n = 0; n < cells.Count;) // 같은 줄에 이어진 같은 종류 칸은 네모 하나로
            {
                var (i, j, core) = cells[n];
                int len = 1;
                while (n + len < cells.Count && cells[n + len] == (i + len, j, core)) len++;
                (core ? holeRuns : rimRuns).Add(new Rectangle(i * C, j * C, len * C, C));
                (l, t, r, b) = (Math.Min(l, i * C), Math.Min(t, j * C), Math.Max(r, (i + len) * C), Math.Max(b, (j + 1) * C));
                if (core) Inner += len;
                n += len;
            }
            foreach (var run in holeRuns) box = box.IsEmpty ? run : Rectangle.Union(box, run);
            box.Offset(at);
            foreach (var (x, y) in shape.Cracks(Hole.R, Hole.R0, C / 2))
            {
                rimRuns.Add(new Rectangle(x, y, C / 2, C / 2));
                (l, t, r, b) = (Math.Min(l, x), Math.Min(t, y), Math.Max(r, x + C / 2), Math.Max(b, y + C / 2));
            }
        }
        Bounds = r > l ? Rectangle.FromLTRB(at.X + l, at.Y + t, at.X + r, at.Y + b) : Rectangle.Empty;
        if (live != null)
        {
            Array.Clear(live);
            if (Inner > 0) { nature = true; PaintStill(); }
            (drawn, sparkBox) = (false, Rectangle.Empty); // 구름·반짝이는 Animate 가 처음부터 다시 얹는다
            peekTop = int.MinValue;                       // 친구도 새 모양대로 다시 자른다
        }
        if (!old.IsEmpty) dirty.Add(old);
        if (!Bounds.IsEmpty) dirty.Add(Bounds);
    }

    bool Core(int i, int j)
    {
        foreach (var run in holeRuns) if (run.Y == j * C && i * C >= run.Left && i * C < run.Right) return true;
        return false;
    }

    // 친구가 올라오는 바닥: 몸 너비보다 양옆 한 칸씩 더 넓게 다 뚫린 가장 아래 줄 (들쭉날쭉한 바닥 끝에서 올라오면 양옆이 잘려 귀만 보인다)
    int PeekFloor()
    {
        int half = 30 * ps / C / 2 + 1, floorJ = 0;
        for (int j = (box.Bottom - at.Y) / C - 1; j >= (box.Top - at.Y) / C; j--)
        {
            bool open = true;
            for (int i = -half; i < half && open; i++) open = Core(i, j) && Core(i, j - 1);
            if (open) { floorJ = j; break; }
        }
        return at.Y + (floorJ + 1) * C;
    }

    // ── 구멍 너머 자연: 파란 하늘과 구름, 나무숲, 흐르는 냇물, 풀밭. k 칸 도트로, 자리마다 조금씩 다르게 ──
    // 움직이지 않는 것만 still 에 그린다. 구름과 반짝이는 Animate 가 live 에 얹는다
    void Fill(int argb, byte layer, int x, int y, int w, int h)
    {
        int x0 = Math.Max(x - buf.X, 0), x1 = Math.Min(x - buf.X + w, buf.Width), y0 = Math.Max(y - buf.Y, 0), y1 = Math.Min(y - buf.Y + h, buf.Height);
        if (x1 <= x0 || y1 <= y0) return;
        for (int yy = y0; yy < y1; yy++)
        {
            still.AsSpan(yy * buf.Width + x0, x1 - x0).Fill(argb);
            cover.AsSpan(yy * buf.Width + x0, x1 - x0).Fill(layer);
        }
    }

    void PaintStill()
    {
        int px = k, W = box.Width;
        floor = PeekFloor();
        seed = (int)(Hole.X * 3 + Hole.Y * 5);
        var rng = new Random(seed);
        int horizon = box.Top + (int)((floor - box.Top) * .52) / px * px;
        Array.Clear(still!);
        Array.Clear(cover!);

        // 하늘: 위는 짙고 지평선 쪽은 옅게 (띠마다 한 색)
        for (int y = box.Top; y < horizon; y += px * 2)
        {
            double u = (y - box.Top) / (double)Math.Max(1, horizon - box.Top);
            Fill(Argb((int)(84 + (178 - 84) * u), (int)(150 + (218 - 150) * u), (int)(228 + (248 - 228) * u)), Sky, box.Left, y, W, px * 2);
        }
        // 구름: 천천히 흘러간다 (자리만 정해 둔다)
        for (int n = 0; n < 2; n++)
        {
            cloudW[n] = (10 + rng.Next(5)) * px;
            cloudSpan[n] = W + cloudW[n] * 2;
            cloudPhase[n] = rng.Next(cloudSpan[n]);
            cloudY[n] = box.Top + (int)((horizon - box.Top) * (.18 + .3 * n)) / px * px;
        }
        // 먼 숲: 지평선을 따라 둥근 나무 꼭대기가 이어진다
        for (int x = box.Left; x < box.Right; x += px)
        {
            double h = 4 + 2.5 * Math.Sin((x - box.Left) / (double)px * .55 + seed) + 1.5 * Math.Sin((x - box.Left) / (double)px * 1.7 + seed * .3);
            int top = horizon - (int)h * px;
            Fill(Far, Land, x, top, px, horizon - top + px);
            if ((x / px + seed) % 5 == 0) Fill(FarLight, Land, x, top + px, px, px); // 잎에 빛
        }
        // 풀밭
        stream = horizon + (int)((floor - horizon) * .35) / px * px;
        bank = (floor - horizon) / 4 / px * px + px * 2;
        Fill(Grass, Land, box.Left, horizon + px, W, box.Bottom - horizon); // 친구가 올라오는 줄 아래 구멍까지
        // 냇물: 구불구불 가로질러 흐른다 (반짝이는 Animate 가)
        for (int x = box.Left; x < box.Right; x += px)
        {
            int col = (x - box.Left) / px, mid = stream + (int)(Math.Sin(col * .22 + seed * .7) * 2) * px;
            mids[col] = mid;
            Fill(Bank, Land, x, mid - px, px, px); // 둑
            Fill(Water, Land, x, mid, px, bank);
        }
        // 앞 풀 언덕: 친구는 이 뒤에서 빼꼼한다 (언덕 위 줄이 floor)
        for (int x = box.Left; x < box.Right; x += px)
        {
            int col = (x - box.Left) / px, bump = (int)(Math.Sin(col * .5 + seed) * 1.2) * px;
            Fill(Hill, Front, x, floor - px + bump, px, box.Bottom - floor + px);
            if ((col + seed) % 3 == 0) Fill(Blade, Front, x, floor - px * 2 + bump, px, px); // 풀잎
        }
        // 가까운 나무 두 그루 (양옆)
        for (int side = -1; side <= 1; side += 2)
        {
            int tx = side < 0 ? box.Left + W / 6 : box.Right - W / 6, ty = horizon + px * 2, r = (5 + rng.Next(3)) * px;
            Fill(Trunk, Front, tx - px, ty - r / 2, px * 2, r);
            for (int dy = -r; dy <= r; dy += px)
            {
                int hw = (int)Math.Sqrt(Math.Max(0, r * r - dy * dy)) / px * px;
                Fill(Leaf, Front, tx - hw, ty - r * 2 + dy, hw * 2, px);
            }
            Fill(LeafLight, Front, tx - r / 2, ty - r * 2 - r / 2, px * 2, px);
        }

        // 안쪽 칸이 아닌 자리는 비운다 (찢긴 구멍 모양대로만 보인다)
        int run = 0;
        for (int y = 0; y < buf.Height; y += C)
        {
            int from = 0, cellY = buf.Y + y - at.Y;
            for (; run < holeRuns.Count && holeRuns[run].Y == cellY; run++)
            {
                Blank(from, holeRuns[run].Left + at.X - buf.X, y);
                from = holeRuns[run].Right + at.X - buf.X;
            }
            Blank(from, buf.Width, y);
        }
        still.AsSpan().CopyTo(live);
    }

    void Blank(int x0, int x1, int y)
    {
        if (x1 <= x0) return;
        for (int yy = y; yy < y + C; yy++)
        {
            still.AsSpan(yy * buf.Width + x0, x1 - x0).Clear();
            cover.AsSpan(yy * buf.Width + x0, x1 - x0).Fill(Outside);
        }
    }

    // 움직이는 것 하나를 live 에: 그 층(하늘이면 Sky, 냇물 위면 Land) 까지만 보인다. 그린 자리를 돌려준다
    Rectangle Dot(int argb, byte upTo, int x, int y, int w, int h)
    {
        int x0 = Math.Max(x - buf.X, 0), x1 = Math.Min(x - buf.X + w, buf.Width), y0 = Math.Max(y - buf.Y, 0), y1 = Math.Min(y - buf.Y + h, buf.Height);
        if (x1 <= x0 || y1 <= y0) return Rectangle.Empty;
        for (int yy = y0; yy < y1; yy++)
            for (int n = yy * buf.Width + x0, end = yy * buf.Width + x1; n < end; n++)
                if (cover![n] <= upTo) live![n] = argb;
        return Rectangle.FromLTRB(x0 + buf.X, y0 + buf.Y, x1 + buf.X, y1 + buf.Y);
    }

    void Restore(Rectangle r)
    {
        r = Rectangle.Intersect(r, buf);
        for (int y = r.Top; y < r.Bottom; y++)
        {
            int n = (y - buf.Y) * buf.Width + r.X - buf.X;
            Array.Copy(still!, n, live!, n, r.Width);
        }
    }

    static Rectangle Join(Rectangle a, Rectangle b) => a.IsEmpty ? b : b.IsEmpty ? a : Rectangle.Union(a, b);

    int CloudAt(int n, double clock) => box.Left - cloudW[n] + (int)((cloudPhase[n] + clock * (6 + n * 4) * k) % cloudSpan[n]) / k * k;
    Rectangle CloudBox(int n) => Rectangle.Intersect(new Rectangle(cloudX[n], cloudY[n], cloudW[n], k * 4), buf);

    // 프레임마다 한 번: 구름·반짝이·빼꼼하는 친구가 지난번과 달라졌으면 고쳐 그리고, 달라진 자리를 dirty 에 담는다
    public void Animate(double clock, Icbm.Peek? peek, Sprites sp, List<Rectangle> dirty)
    {
        bool on = false, blink = false, heart = false;
        int top = 0;
        if (nature)
        {
            int px = k, c0 = CloudAt(0, clock), c1 = CloudAt(1, clock), step = (int)(clock * 7);
            bool clouds = !drawn || c0 != cloudX[0] || c1 != cloudX[1], sparks = !drawn || step != stepSeen;
            if (clouds || sparks)
            {
                Rectangle oldClouds = drawn ? Join(CloudBox(0), CloudBox(1)) : Rectangle.Empty, oldSparks = sparkBox;
                Restore(oldClouds);
                Restore(oldSparks);
                (cloudX[0], cloudX[1], stepSeen, drawn) = (c0, c1, step, true);
                for (int n = 0; n < 2; n++)
                {
                    int cx = cloudX[n], cy = cloudY[n], cw = cloudW[n];
                    Dot(White, Sky, cx, cy + px, cw, px * 2);
                    Dot(White, Sky, cx + px * 2, cy, cw - px * 5, px);
                    Dot(CloudShade, Sky, cx + px, cy + px * 3, cw - px * 2, px);
                }
                // 반짝이가 흐름 따라 옮겨 간다. 두 칸 너비인데 오른쪽 칸은 다음 줄 냇물에 덮인다 (물 밖으로 삐져나온 데만 보인다)
                sparkBox = Rectangle.Empty;
                for (int x = box.Left, col = 0; x < box.Right; x += px, col++)
                {
                    if ((col + step) % 6 != 0) continue;
                    int y = mids[col] + px * ((col / 6 + seed) % Math.Max(1, bank / px));
                    sparkBox = Join(sparkBox, Dot(Spark, Land, x, y, px, px));
                    if (x + px >= box.Right) continue;
                    int next = mids[col + 1];
                    if (y < next - px || y >= next + Math.Max(0, bank)) sparkBox = Join(sparkBox, Dot(Spark, Land, x + px, y, px, px));
                }
                if (clouds) Add(dirty, Join(oldClouds, Join(CloudBox(0), CloudBox(1))));
                if (sparks) Add(dirty, Join(oldSparks, sparkBox));
            }

            double t = peek == null ? 0 : clock - peek.At, up = peek == null ? 0 : Icbm.Rise(t);
            if (peek != null && Inner >= 4 && up > 0 && Face(sp, peek.Species))
            {
                // 언덕 위로 귀·눈·코까지 (머리가 큰 아이들이라 몸 높이의 절반쯤이 「1/3 빼꼼」으로 보인다)
                (on, blink, top) = (true, up >= 1 && t % 1.1 is > .8 and < .95, floor - k - (int)(body.Height * .5 * up));
                heart = up >= 1 && t % .8 < .55;
            }
        }
        if (on != peekOn || on && (top != peekTop || blink != peekBlink))
        {
            var old = peekBox;
            (peekOn, peekTop, peekBlink) = (on, top, blink);
            peekBox = on ? PaintFace(blink ? faceBlink! : face!, top) : Rectangle.Empty;
            Add(dirty, Join(old, peekBox));
        }
        // 다 올라오면 작은 하트
        var heartAt = heart ? new Point(peekAt.X + body.Right - sp.Cell * HeartScale(k) / 3, top - sp.Cell * HeartScale(k) * 2 / 3) : Point.Empty;
        if (heart == HeartOn && heartAt == HeartAt) return;
        Add(dirty, heartRect);
        (HeartOn, HeartAt, heartRect) = (heart, heartAt, heart ? new Rectangle(heartAt, new Size(sp.Cell * HeartScale(k), sp.Cell * HeartScale(k))) : Rectangle.Empty);
        Add(dirty, heartRect);
    }

    static void Add(List<Rectangle> dirty, Rectangle r) { if (!r.IsEmpty) dirty.Add(r); }

    // 친구 그림을 점으로 받아 둔다 (도감 썸네일: 눈 뜬 것·감은 것). 그릴 수 없는 종이면 false
    bool Face(Sprites sp, string species)
    {
        if (faceTried) return face != null;
        faceTried = true;
        string name = "thumb/" + species;
        if (!sp.Has(name)) return false;
        var open = sp.Get(name, 0, ps);
        (face, faceBlink, faceW) = (Pixels(open), Pixels(sp.Get(name, 1, ps)), open.Width);
        peekLive = GC.AllocateArray<int>(face.Length, pinned: true);
        peekBmp = new Bitmap(faceW, open.Height, faceW * 4, PixelFormat.Format32bppPArgb, Marshal.UnsafeAddrOfPinnedArrayElement(peekLive, 0));
        int x0 = faceW, y0 = open.Height, x1 = -1, y1 = -1; // 실제로 그려진 부분
        for (int n = 0; n < face.Length; n++)
        {
            if ((uint)face[n] >> 24 == 0) continue;
            int x = n % faceW, y = n / faceW;
            (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        }
        body = x1 < 0 ? new Rectangle(0, 0, faceW, open.Height) : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        return true;
    }

    static int[] Pixels(Bitmap b)
    {
        var pixels = new int[b.Width * b.Height];
        var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try { for (int y = 0; y < b.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * b.Width, b.Width); }
        finally { b.UnlockBits(data); }
        return pixels;
    }

    // 떠난 친구가 빼꼼 (YELL): 풀 언덕 뒤에서 몸의 1/3 만 올라와 눈만 깜빡깜빡. 구멍 안쪽 칸에만, 언덕 위 줄(floor - k) 위로만 보인다.
    // 보이는 점만 peekLive 에 담고 그 자리를 돌려준다
    Rectangle PaintFace(int[] img, int top)
    {
        int h = img.Length / faceW;
        peekAt = new Point(at.X - (body.Left + body.Width / 2), top - body.Top);
        Array.Clear(peekLive!);
        var r = Rectangle.Intersect(Rectangle.FromLTRB(peekAt.X, peekAt.Y, peekAt.X + faceW, Math.Min(peekAt.Y + h, floor - k)), buf);
        for (int y = r.Top; y < r.Bottom; y++)
            for (int x = r.Left; x < r.Right; x++)
            {
                if (cover![(y - buf.Y) * buf.Width + x - buf.X] == Outside) continue;
                int n = (y - peekAt.Y) * faceW + x - peekAt.X, s = img[n], a = (int)((uint)s >> 24);
                if (a is > 0 and < 255) // 반투명한 점은 그림 형식(미리 곱한 알파)에 맞춘다
                    s = a << 24 | ((s >> 16 & 255) * a + 127) / 255 << 16 | ((s >> 8 & 255) * a + 127) / 255 << 8 | ((s & 255) * a + 127) / 255;
                peekLive![n] = s;
            }
        return r;
    }

    // 구멍 모양(검은 구멍은 안쪽까지), 친구 구멍이면 그 위에 자연 그림 한 장
    public void Draw(Overlay o, Graphics g)
    {
        int x = at.X - o.Left, y = at.Y - o.Top;
        g.TranslateTransform(x, y);
        if (!nature && holeRuns.Count > 0) g.FillRectangles(Hole.Friend == null ? Pal.Hole : Pal.Soot, CollectionsMarshal.AsSpan(holeRuns));
        if (rimRuns.Count > 0) g.FillRectangles(Pal.Soot, CollectionsMarshal.AsSpan(rimRuns));
        g.TranslateTransform(-x, -y);
        if (nature) g.DrawImageUnscaled(bmp!, buf.X - o.Left, buf.Y - o.Top); // 안쪽 칸을 빈틈없이 덮는다 (그 아래는 칠하지 않아도 된다)
    }

    // 빼꼼하는 친구와 하트 (구멍들을 다 그린 뒤에: 겹친 구멍이 있어도 친구는 보인다)
    public void DrawPeek(Overlay o, Graphics g, Stamp heart)
    {
        if (peekOn) g.DrawImageUnscaled(peekBmp!, peekAt.X - o.Left, peekAt.Y - o.Top);
        if (HeartOn) heart.DrawCell(g, HeartAt.X - o.Left, HeartAt.Y - o.Top, heartRect.Width);
    }

    // 다 고친 구멍: 그려져 있던 자리를 dirty 에 담고 그림을 버린다
    public void Gone(List<Rectangle> dirty)
    {
        Add(dirty, Bounds);
        Add(dirty, heartRect);
        Dispose();
    }

    // 자가 검사: 그리는 칸·금이 규칙(Ragged)이 내는 것과 같은가
    public bool SameShape(List<(int I, int J, bool Core)> cells, List<(int X, int Y)> cracks)
    {
        var mine = new HashSet<(int, int, bool)>();
        var lines = new List<(int, int)>();
        foreach (var r in holeRuns) for (int x = r.Left; x < r.Right; x += C) mine.Add((x / C, r.Y / C, true));
        foreach (var r in rimRuns)
        {
            if (r.Height != C) { lines.Add((r.X, r.Y)); continue; }
            for (int x = r.Left; x < r.Right; x += C) mine.Add((x / C, r.Y / C, false));
        }
        return mine.Count == cells.Count && mine.SetEquals(cells) && lines.SequenceEqual(cracks);
    }

    // 자가 검사: 다른 그림과 지금 보이는 것이 같은가 (자연 점 하나하나, 하트)
    public bool SamePicture(CraterPic other) =>
        nature == other.nature && HeartOn == other.HeartOn && HeartAt == other.HeartAt && (!nature || live.AsSpan().SequenceEqual(other.live))
        && peekOn == other.peekOn && (!peekOn || peekAt == other.peekAt && peekBox == other.peekBox && peekLive.AsSpan().SequenceEqual(other.peekLive));

    // 자가 검사: 자연이 엽서의 자연(Overlay.PaintNature: 한 번에 다 그리는 쪽)과 점 하나까지 같은가. 구멍 안쪽 칸으로 잘라서 견준다
    public bool SameAsPostcard(double clock)
    {
        if (!nature) return true;
        using var want = new Bitmap(buf.Width, buf.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(want))
        using (var clip = new Region(Rectangle.Empty))
        {
            foreach (var run in holeRuns) clip.Union(new Rectangle(run.X + at.X - buf.X, run.Y + at.Y - buf.Y, run.Width, run.Height));
            g.Clip = clip;
            g.TranslateTransform(-buf.X, -buf.Y);
            Overlay.PaintNature(g, box, floor, seed, clock, k);
        }
        return Pixels(want).AsSpan().SequenceEqual(live);
    }

    // 자가 검사: 자연 그림이 안쪽 칸만 빈틈없이 덮는가 (안쪽 점은 모두 불투명, 밖은 모두 투명)
    public bool NatureFits()
    {
        if (!nature) return true;
        for (int n = 0; n < live!.Length; n++)
        {
            int x = buf.X + n % buf.Width - at.X, y = buf.Y + n / buf.Width - at.Y;
            bool inside = Core((int)Math.Floor(x / (double)C), (int)Math.Floor(y / (double)C));
            if (((uint)live[n] >> 24 == 255) != inside || ((uint)live[n] >> 24 == 0) == inside) return false;
        }
        return true;
    }
}
