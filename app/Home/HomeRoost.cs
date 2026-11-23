namespace Bolok;

[Flags]
public enum RoostChange { None = 0, Moved = 1, Fell = 2, Landed = 4 }

// 창 위에 놓인 방석·집 (docs/home.md 「창 위 놓기」). 놓을 때 아래가 창 윗변 가까이면 그 창 윗변에 붙고(Snap), 창을 따라 움직이고(Tick),
// 창이 닫히거나 접히거나 덮이거나 자리가 없어지면 같은 x 로 바닥에 떨어진다. 화면(창)을 모른다 — Brain 의 창 목록·Probe·World 를 그대로 써서
// 자가 검사가 펫 윗변 낮잠(Sim.cs)처럼 가짜 창으로 돌린다. 창 한 개 확인은 0.05초, 덮임(창 목록)은 0.6초에 한 번 — 프레임마다 새로 잡는 것 없음.
// 저장은 안 한다: 창은 껐다 켜면 사라지니, 저장 칸에는 바닥 x 만 있고 불러오면 바닥에 있다
sealed class HomeRoost
{
    public const double ProbeSec = .05, CoverSec = .6;

    readonly Brain b;
    public IntPtr H;                       // 올라앉은 창 (0 이면 창 위가 아니다)
    public double Dx;                      // 창 왼쪽에서 그림 가운데까지 가로 거리 — 창이 움직여도 같은 자리에 있게
    public bool Falling;                   // 바닥으로 떨어지는 중
    public double CenterX, FloorY;         // 그림 아래 가운데 x, 발 자리(seat 줄) y — 창 위일 때·떨어지는 중일 때만 뜻이 있다
    public Rectangle Area;                 // 그 자리가 있는 모니터의 작업 영역
    double vy, nextProbe, nextCover;
    int bounced;

    public HomeRoost(Brain b) => this.b = b;

    public bool OnWindow => H != IntPtr.Zero;
    public bool Active => OnWindow || Falling;

    // 창 윗변에서 seat 줄까지 내려가는 픽셀: 펫이 윗변에 서는 것과 같다 (Brain.PerchY — 스프라이트 아랫단 여백)
    public static int Perch(int k) => 3 * k;

    public void Clear() { H = IntPtr.Zero; Falling = false; vy = 0; }

    // 창 위에서 바로 떨어뜨린다 (디버그·창을 못 쓰게 됐을 때)
    public void StartFall()
    {
        if (!OnWindow) return;
        (H, Falling, vy, bounced) = (IntPtr.Zero, true, 0, 0);
    }

    // 바닥 말고 창 위에 놓일 수 있나: 그 창이 최대화·항상 위가 아니고, 가로로 그림이 들고, 위에 그림과 펫이 설 공간(펫 윗변 낮잠과 같게 한 칸 이상)이 있을 때.
    // 바닥 높이보다 아래로 내려간 창도 안 된다 (그림이 화면 밖으로 나가지 않게). x 는 창·모니터 안쪽으로 맞춰 돌려준다
    static bool Fits(Rectangle r, Rectangle area, HomeItem it, int k, double wantX, Tuning t, out double x)
    {
        x = wantX;
        double hw = it.W * k / 2.0, top = r.Top + Perch(k);
        if (r.Left <= area.Left && r.Right >= area.Right && r.Top <= area.Top + 2) return false;   // 최대화
        if (top > area.Bottom - t.FloorGap) return false;                                          // 바닥보다 아래
        if (top - Math.Max(it.Seat.Y, 40) * k < area.Top) return false;                            // 위에 그림(과 그 위의 펫 한 칸)이 들 공간이 없다
        double lo = Math.Max(r.Left, area.Left) + hw, hi = Math.Min(r.Right, area.Right) - hw;
        if (lo > hi) return false;                                                                 // 창이 좁거나 화면 밖으로 나갔다
        x = Math.Clamp(wantX, lo, hi);
        return true;
    }

    // wins[i] 보다 앞(위)의 창이 그림 자리를 덮나
    static bool Covered(IReadOnlyList<Win> wins, int i, HomeItem it, int k, double x, double top)
    {
        var box = new Rectangle((int)(x - it.W * k / 2.0), (int)(top - it.Seat.Y * k), it.W * k, it.Seat.Y * k + 6);
        for (int j = 0; j < i; j++) if (wins[j].R.IntersectsWith(box)) return true;
        return false;
    }

    // 놓았다: 그림 아래(bottomY)가 창 윗변에서 RoostSnapCells 칸 안이고 가운데 x 가 그 창 위면 붙는다. 여러 창이면 윗변이 가장 가까운 것
    public bool Snap(HomeItem it, int k, double centerX, double bottomY)
    {
        Clear();
        var wins = b.Windows();
        double tol = b.K.RoostSnapCells * k, bestD = double.MaxValue;
        int best = -1;
        double bestX = 0;
        Rectangle bestArea = default;
        for (int i = 0; i < wins.Count; i++)
        {
            var w = wins[i];
            if (w.Topmost) continue;
            var r = w.R;
            double top = r.Top + Perch(k), d = Math.Abs(top - bottomY);
            if (d > tol || d >= bestD || centerX < r.Left || centerX > r.Right) continue;
            var area = b.World.At(centerX, r.Top + 1);
            if (!Fits(r, area, it, k, centerX, b.K, out double x) || Covered(wins, i, it, k, x, top)) continue;
            (best, bestD, bestX, bestArea) = (i, d, x, area);
        }
        if (best < 0) return false;
        var rect = wins[best].R;
        (H, Dx, CenterX, FloorY, Area) = (wins[best].H, bestX - rect.Left, bestX, rect.Top + Perch(k), bestArea);
        nextProbe = 0;
        nextCover = 0;
        Dbg.Log("home-win", ("id", it.Id), ("x", (int)bestX), ("top", rect.Top));
        return true;
    }

    // 프레임마다: 창이 움직였으면 따라가고, 못 앉게 되면 떨어뜨린다. 떨어지는 중이면 중력으로 내려간다
    public RoostChange Tick(double now, double dt, HomeItem it, int k)
    {
        if (Falling) return Fall(dt, k);
        if (!OnWindow || now < nextProbe) return RoostChange.None;
        nextProbe = now + ProbeSec;
        var s = b.Probe(H);
        bool ok = s.Alive && !s.Minimized;
        double x = CenterX, top = FloorY;
        var area = Area;
        if (ok)
        {
            double want = s.R.Left + Dx;
            area = b.World.At(want, s.R.Top + 1);
            top = s.R.Top + Perch(k);
            ok = Fits(s.R, area, it, k, want, b.K, out x);
        }
        if (ok && now >= nextCover) // 덮임은 창 목록이 있어야 알 수 있어 드물게만
        {
            nextCover = now + CoverSec;
            var wins = b.Windows();
            int i = 0;
            while (i < wins.Count && wins[i].H != H) i++;
            ok = i < wins.Count && !Covered(wins, i, it, k, x, top);
        }
        if (!ok)
        {
            Dbg.Log("home-fall", ("id", it.Id), ("x", (int)CenterX));
            (H, Falling, vy, bounced) = (IntPtr.Zero, true, 0, 0);
            return RoostChange.Fell;
        }
        if (Math.Abs(x - CenterX) < .5 && Math.Abs(top - FloorY) < .5 && area == Area) return RoostChange.None;
        (CenterX, FloorY, Area) = (x, top, area);
        return RoostChange.Moved;
    }

    // 짧게 떨어진다: 펫이 던져졌을 때와 같은 중력, 바닥에서 한 번 작게 통 튄다
    RoostChange Fall(double dt, int k)
    {
        double s = k / 3.0, floor = Area.Bottom - b.K.FloorGap;
        vy += b.K.Gravity * s * dt;
        FloorY += vy * dt;
        if (FloorY < floor) return RoostChange.Moved;
        FloorY = floor;
        if (bounced == 0 && vy > 500 * s) { vy = -vy * b.K.Bounce * .3; bounced = 1; return RoostChange.Moved; }
        (Falling, vy) = (false, 0);
        return RoostChange.Moved | RoostChange.Landed;
    }
}
