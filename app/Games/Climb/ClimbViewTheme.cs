using Px = Bolok.ClimbArt.Px;

namespace Bolok;

// 클라이밍 그리기의 테마·건너뛰기·친구 자리 (ClimbView 의 나머지 반): docs/climb.md 「테마」·「건너뛰기와 친구」
// · 테마 판(책상 위 문구·과자 탑·풍선·장난감 블록)은 덩굴 대신 발판 그림을 찍는다. 규칙·자리·시간은 같다
// · 서 있는 발판이 줄어들면(풍선·과자) 볼록이가 같이 가라앉는다 (SinkOf)
// · 건너뛰기(덩굴손): 5잎을 줄기를 따라 한 칸씩. 테마마다 나르는 방식이 다르다 (Lifting·CarrierPaint)
// · 친구는 볼록이 몸에서 떨어진 곁가지에 선다 (PlaceFriend)
sealed partial class ClimbView
{
    readonly Px[,] perch = new Px[2, 3];       // 테마 판의 친구 받침 [왼쪽·오른쪽][길이] (판을 열 때 구워 둔다)
    const int PerchBx = 10, PerchBy = 40;      // 받침 그림 안 줄기 쪽 끝 자리 (오른쪽 보기. 왼쪽 보기는 길이에 따라 perchBx)

    // ── 서 있는 발판의 모습 ──
    static int StageBar(Climb c) => c.Bar <= .15 ? 3 : c.Bar <= .35 ? 2 : 1;                                   // 막대에 따른 1 싱싱 · 2 조짐 · 3 곧
    static int StandStage(Climb c) => c.Done && c.Why == Climb.Cause.Time ? 4 : StageBar(c);                  // 시간이 다 돼 떨어지면 4 (무너짐)
    static int WeakStage(Climb c) => c.WiltLeft > 0 && c.WiltLeft < c.T.WiltSec * .6 || c.Done && c.Why == Climb.Cause.Wilt ? 1 : 0;   // 약한 발판은 버티는 중 처진다

    // 테마 판의 발판 종류 (옆 발판과 달라 보이게: 이웃과 같은 종류는 피한다. 잎 번호로만 정해진다)
    int VarOf(int j)
    {
        int n = Skin!.Variants;
        int v = (int)(Hash(j, 11) % (ulong)n), p = (int)(Hash(j - 1, 11) % (ulong)n);
        return v == p ? (v + 1) % n : v;
    }

    // 서 있는 발판의 그림 (막대에 따른 모습. 무너진 뒤 모습은 안 쓴다 — 가라앉은 줄을 잴 때)
    SkinPx StandStep(Climb c, int stage)
    {
        int j = c.N;
        var (_, _, kind, _, _) = Info(c, j);
        int ndir = Info(c, j + 1).Dir;
        return kind == ClimbKind.Wilt ? Skin!.Weakp(ndir, WeakStage(c)) : Skin!.Step(VarOf(j), ndir, stage);
    }

    // 볼록이가 서 있는 발판과 같이 가라앉은 줄 수 (테마 판의 풍선·과자만. 덩굴·문구·블록은 0)
    public int SinkOf(Climb c) => Skin == null ? 0 : Math.Min(ClimbSkin.MaxSink, StandStep(c, StageBar(c)).Sink);
    internal int PetSink(Climb c) => SinkOf(c);

    // ── 건너뛰기 (덩굴손) ──
    // 덩굴손 잎에 닿은 뒤 건너뛰는 5잎을 지나는 동안
    bool Lifting(Climb c) => c.LiftLeft > 0 && c.Lifts > 0 && vN >= c.N - c.T.CurlUp - 1e-6 && vN < c.N - 1e-6;
    internal bool IsLifting(Climb c) => Lifting(c);

    // 덩굴손 그림 단계: 0 말림 · 1 풀리는 중 · 2 쭉 뻗음 · 3 되튐 · 4 다 폄 (밟으면 풀리며 튀어 오른다)
    int CurlFrame(Climb c, int j)
    {
        if (j > vN + .01) return 0;
        if (c.LiftLeft > 0 && c.Lifts > 0 && j == c.N - c.T.CurlUp)
        {
            double tt = c.T.LiftSec - c.LiftLeft - c.T.HopSec;     // 밟은 뒤 흐른 시간
            return tt < .08 ? 1 : tt < .2 ? 2 : tt < .32 ? 3 : 4;
        }
        return 4;
    }

    void PutSk(SkinPx m, int x, int y) => PutPx(m.P, x - m.Ax, y - m.Ay);

    // 나르는 것 (볼록이 밑·뒤에): 비행기 · 풍선 묶음 · 사탕 줄과 갈고리 · 용수철
    void CarrierPaint(Climb c, ClimbShow s, double px, double footY)
    {
        if (!Lifting(c)) return;
        var sk = Skin!;
        int i0 = (int)Math.Floor(vN);
        double fr = vN - i0;
        bool left = c.At(i0 + 1).Dir < 0;
        int xi = (int)Math.Round(px), fy = (int)Math.Round(footY);
        switch (sk.Lift)
        {
            case ClimbLift.Glide: { var cs = left ? sk.CarrierL : sk.Carrier; PutPx(cs.P, xi - cs.Ax, fy - cs.Ay + 1); break; }                       // 날개 위에 선다
            case ClimbLift.Hang: { var cs = sk.Carrier; PutPx(cs.P, xi - cs.Ax, fy - pet.Foot + pet.HeldTop - cs.Ay + 2); break; }                  // 끈 묶음을 쥔 손 위에
            case ClimbLift.Swing:
                {
                    var (ax, ay) = LeafAt(c, i0 + 1, vN);                  // 줄은 다음 잎 위 높이에 걸려 있다 (볼록이가 그 아래로 흔들려 간다)
                    ay -= 46;
                    int hx = xi, hy = fy - pet.Foot + pet.HeldTop + 1;
                    int n = Math.Max(Math.Abs(hx - ax), Math.Abs(hy - ay)) + 1;
                    for (int pass = 0; pass < 2; pass++)
                        for (int i = 0; i <= n; i++)
                        {
                            int x = ax + (hx - ax) * i / n, y = ay + (hy - ay) * i / n;
                            if (pass == 0) { Cell(x - 1, y, ClimbArt.Line); Cell(x + 2, y, ClimbArt.Line); Cell(x, y - 1, ClimbArt.Line); Cell(x + 1, y + 1, ClimbArt.Line); }
                            else { uint col = (i / 3 & 1) == 0 ? ClimbArt.Red : ClimbArt.White; Cell(x, y, col); Cell(x + 1, y, col); }
                        }
                    PutSk(sk.Carrier, ax, ay);
                    break;
                }
            case ClimbLift.Bounce:
                if (fr < .4) { var (lx, ly) = LeafAt(c, i0, vN); PutSk(sk.Carrier, lx, ly); }                                                       // 떠나는 발판에서 눌렸다 튀는 용수철
                break;
        }
    }

    void Cell(int x, int y, uint col) { if (x >= 0 && y >= 0 && x < CW && y < PH) cells[y * CW + x] = col; }

    // 볼록이 머리 위 (지닌 홀씨·별 풍선·솜사탕 …). save: 홀씨로 돌아오는 중이면 조금 더 위
    void HeadThing(double px, double footY, int bob, bool save)
    {
        int top = (int)Math.Round(footY) - pet.Foot, x = (int)Math.Round(px);
        if (Skin != null) { var h = Skin.SaveHead; PutPx(h.P, x - h.P.W / 2, top - h.P.H + 8 + bob - (save ? 4 : 0)); }
        else PutPx(puffHead, x - 8, top - (save ? 12 : 8) + (save ? 0 : bob));
    }

    // ── 테마 판의 세계: 발판 · 줍는 것 · 구름 띠 (덩굴 대신) ──
    void ComposeSkin(Climb c, ClimbShow s, int lo, int hi, int grow)
    {
        var sk = Skin!;
        int n0 = (int)Math.Floor(vN);
        bool Grown(int j) => grow == 99 || j <= grow - 3;
        for (int j = lo; j <= hi; j++)
        {
            if (!Grown(j) || j >= 0 && c.Hidden(j)) continue;
            var (_, _, kind, _, taken) = Info(c, j);
            int ndir = Info(c, j + 1).Dir;                          // 발판은 다음 잎 쪽으로 향한다
            var (x, y) = LeafAt(c, j, vN);
            bool stand = j == c.N;
            var sp = kind == ClimbKind.Wilt ? sk.Weakp(ndir, stand ? WeakStage(c) : 0) : sk.Step(VarOf(j), ndir, stand ? StandStage(c) : 0);
            if (y - sp.Ay + sp.P.H < 6) continue;                   // 판 위로 거의 나간 발판은 그리지 않는다
            PutPx(sp.P, x - sp.Ax, y - sp.Ay);
            switch (kind)
            {
                case ClimbKind.Berry: if (!taken) PutSk(sk.Berry, x + ndir * 4, y); break;
                case ClimbKind.Puff: if (!taken) PutSk(sk.Save, x - ndir * 3, y); break;
                case ClimbKind.Curl: PutSk(j <= vN + .01 ? sk.SkipOpen : sk.Skip, x + ndir * 2, y); break;
                case ClimbKind.Flower: PutPx(j <= vN + .01 ? flower : flowerBud, x + ndir * 9 - 7, y - 8); break;
            }
        }
        for (int j = Math.Max(n0 + c.T.CloudHide, 0); j <= hi; j++)
        {
            if (!c.Hidden(j) || !Grown(j)) continue;
            var (x, y) = LeafAt(c, j, vN);
            var b = sk.Band[j & 1];
            PutPx(b, x - b.W / 2, y - 8);
        }
    }

    // ── 친구 ──
    int LenIdx(int len) { var lens = Look == ClimbLook.A2 ? LensA2 : LensA4; int i = Array.IndexOf(lens, len); return i < 0 ? 0 : i; }

    // 테마 판의 받침 그림을 미리 구워 둔다 (프레임마다 새로 만들지 않게): 줄기 쪽 끝이 (bx0, PerchBy), 끝이 side·len 칸 옆
    static Px BakePerch(ClimbSkin sk, int side, int len, out int bx0)
    {
        bx0 = side > 0 ? PerchBx : len + 60;
        var g = new Px(len + 72, 70);
        sk.Perch(g, bx0, PerchBy, side, bx0 + side * len, PerchBy - 14);
        return g;
    }
    readonly int[] perchBx = new int[6];

    // 친구가 서는 자리를 정한다 (이정표를 밟고 볼록이가 내려선 바로 뒤 한 번 — 폴짝하는 동안 겹치지 않게): 볼록이 몸에서 20칸쯤 떨어진 곁가지 끝. 안 겹치게, 판 안에
    // 1) 한 잎 아래 높이의 곁가지 (짧은 것부터, 덩굴이 갈 쪽에서 먼 쪽부터)  2) 그래도 없으면 몇 잎 위
    public const int FriendGapMin = 20;

    public void PlaceFriend(Climb c, ClimbShow s)
    {
        int n = c.N;
        int hw = pet.HalfW, tall = pet.Tall;
        int fw = s.Friend?.HalfW ?? 7, fh = s.Friend?.Tall ?? 11;
        double vx = Look == ClimbLook.A2 ? TargetX(c, n) : 0;
        double px = Look == ClimbLook.A2 ? CW / 2.0 + c.At(n).Col * DX - vx : CW / 2.0 + Info(c, n).Dir * SideA4;

        var lens = Look == ClimbLook.A2 ? LensA2 : LensA4;
        (int Pass, int Li, int Clear, int Side, int Leaf, int Gap) best = (9, 9, -999, 0, n - 1, -999);
        for (int pass = 0; pass < 2; pass++)
        {
            int j = Look == ClimbLook.A4 ? n + 4 : pass == 0 ? n - 1 : n + 4;
            if (Look == ClimbLook.A4 && pass == 0) continue;
            double ly = Foot + DY * (n - j), lx = Look == ClimbLook.A2 ? CW / 2.0 + Info(c, j).Col * DX - vx : CW / 2.0;
            for (int li = 0; li < lens.Length; li++)
                for (int side = -1; side <= 1; side += 2)
                {
                    double tip = Look == ClimbLook.A2 ? lx - side * 6 + side * lens[li] : CW / 2.0 + side * lens[li];
                    double ty = ly + 6 - 14, fTop = ty + 1 - fh + 1, fBot = ty + 7, pTop = Foot - tall + 1 - 7, pBot = Foot;   // 폴짝 높이 6칸까지 (볼록이가 내려선 뒤 놓으니 가로는 한 자리)
                    if (tip - fw < 2 || tip + fw > CW - 2) continue;
                    double gapX = Math.Abs(tip - px) - fw - hw, gapY = Math.Max(fTop - pBot, pTop - fBot);
                    int gap = (int)Math.Floor(Math.Max(gapX, gapY));
                    if (gap < FriendGapMin) continue;
                    int clear = 999;
                    for (int k = 1; k <= 3; k++) clear = Math.Min(clear, (int)Math.Abs(tip - (Look == ClimbLook.A2 ? CW / 2.0 + c.At(n + k).Col * DX - vx : CW / 2.0)));   // 앞길과의 거리: 클수록 길을 안 가린다
                    var cand = (pass, li, clear, side, j, gap);
                    if (cand.pass < best.Pass || cand.pass == best.Pass && (cand.li < best.Li || cand.li == best.Li && cand.clear > best.Clear)) best = (pass, li, clear, side, j, gap);
                }
        }
        if (best.Pass == 9)   // 맞는 자리가 없다 (일어나지 않아야 한다 — 검사): 가장 먼 자리
        {
            best = (9, 0, 0, -Math.Sign(px - CW / 2.0 + .01), n - 1, 0);
        }
        s.FriendLeaf = best.Leaf; s.FriendSide = best.Side; s.FriendGap = best.Gap;
        s.FriendLen = best.Pass == 9 ? lens[0] : lens[best.Li];
    }

    // 친구가 선 곁가지: (쪽, 줄기 쪽 끝 x·y, 끝 x·y = 친구 발이 닿는 곳)
    (int Side, int Bx, int By, int Tx, int Ty) FriendSpot(Climb c, ClimbShow s)
    {
        var (lx, ly) = LeafAt(c, s.FriendLeaf, vN);
        int side = s.FriendSide, len = s.FriendLen;
        int bx = Look == ClimbLook.A2 ? lx - side * 6 : CW / 2;
        int tx = Look == ClimbLook.A2 ? bx + side * len : CW / 2 + side * len;
        return (side, bx, ly + 6, tx, ly + 6 - 14);
    }

    // 친구: 곁가지 + 잎 + 폴짝 (덩굴 뒤)
    void FriendPaint(Climb c, ClimbShow s)
    {
        var (side, bx, by, tipX, tipY) = FriendSpot(c, s);
        int li = LenIdx(s.FriendLen), si = side < 0 ? 0 : 1;
        if (Skin != null)
        {
            var pc = perch[si, li];
            PutPx(pc, bx - perchBx[si * 3 + li], by - PerchBy);
        }
        else
        {
            var br = branch[si, li];
            PutPx(br.Out, bx + br.Ox, by + br.Oy); PutPx(br.In, bx + br.Ox, by + br.Oy);
            var lf = branchLeaf[si];
            PutPx(lf.P, tipX - lf.Ax, tipY - lf.Ay);
        }
        bool joy = ((int)(s.FriendAge * 5) & 1) == 0;
        if (s.Friend is { Hop: { } hop } fr) PutPx(hop.Get(joy ? 1 : 2, side >= 0), tipX - 20, tipY + 1 + (joy ? 0 : 6) - fr.Foot);
        else PutPx(bird[joy ? 0 : 1], tipX - 7, tipY - 12);
    }

    // 친구 몸 상자와 볼록이 몸 상자 (칸). 친구가 서 있을 때만
    internal bool FriendBoxes(Climb c, ClimbShow s, out Rectangle friend, out Rectangle petBox)
    {
        friend = petBox = default;
        if (s.FriendAt < 0 || s.FriendSide == 0) return false;
        var (_, _, _, tx, ty) = FriendSpot(c, s);
        int fw = s.Friend?.HalfW ?? 7, fh = s.Friend?.Tall ?? 11;
        friend = Rectangle.FromLTRB(tx - fw, ty + 1 - fh + 1, tx + fw + 1, ty + 8);
        PetNow(c, s, out double x, out double fy, out _, out _);
        int px = (int)Math.Round(x), foot = (int)Math.Round(fy);
        petBox = Rectangle.FromLTRB(px - pet.HalfW, foot - pet.Tall + 1, px + pet.HalfW + 1, foot + 1);
        return true;
    }

    // 볼록이가 친구 가까이(8칸 안)로 오면 친구는 돌아간다 (같은 프레임에 겹쳐 그리지 않게)
    bool FriendTooClose(Climb c, ClimbShow s)
    {
        if (!FriendBoxes(c, s, out var fr, out var pb)) return false;
        return Rectangle.Inflate(pb, 8, 8).IntersectsWith(fr);
    }

    // ── 건너뛰는 동안 반짝: 지난 잎마다 작은 반짝, 덩굴은 뒤따르는 반짝 셋 ──
    void LiftFx(Overlay o, Graphics g, Rectangle panel, Climb c, ClimbShow s, Stamps stamps, int small)
    {
        int k = F, inset = 7 * k;
        int Cx(double v) => Math.Clamp(panel.X + (int)v, panel.X + inset, panel.Right - inset);
        int Cy(double v) => Math.Clamp(panel.Y + (int)v, panel.Y + inset, panel.Bottom - inset);
        int leaf = c.N - c.T.CurlUp, top = (int)Math.Floor(vN + 1e-6);
        for (int j = top; j >= leaf && j > top - 2; j--)
        {
            double since = vN - j;
            if (since > .8) continue;
            var (x, y) = LeafAt(c, j, vN);
            stamps.Get("game/spark", (int)(since * 12) & 1, small).Put(o, g, Cx(x * k), Cy((y - 5) * k));
        }
        if (Skin != null) return;
        PetNow(c, s, out double px, out double fy, out _, out _);
        int trail = -c.At((int)Math.Floor(vN) + 1).Dir, step = (int)(s.Clock * 24);
        for (int i = 0; i < 3; i++)
            stamps.Get("game/spark", (i + step) & 1, small).Put(o, g, Cx((px + trail * (11 + 7 * i)) * k), Cy((fy - 12 + 8 * i) * k));
    }

    // ── 자가 검사 ──
    // 볼록이를 얹기 전 세계 (검사가 StepSeen 을 부르기 전에 켠다)
    uint[]? worldCells;
    internal void ProbeOn() { worldCells ??= new uint[cells.Length]; ForceCompose(); }

    // 발판 j 의 그림 중 판 안에 든 비율(Inside)과 그 가운데 다른 그림에 안 덮이고 보이는 비율(Seen). 구름에 가린 발판은 (0, 0).
    // 장식(열매·홀씨·덩굴손·꽃)과 이웃 발판이 덮는 만큼이 빠진다 (볼록이는 뺀다 — 늘 같은 자리에 선다). ProbeOn 뒤 한 프레임 그려진 다음에 부른다
    internal (double Inside, double Seen) StepSeen(Climb c, int j)
    {
        var sk = Skin!;
        if (j >= 0 && c.Hidden(j)) return (0, 0);
        var (_, _, kind, _, _) = Info(c, j);
        int ndir = Info(c, j + 1).Dir;
        var (x, y) = LeafAt(c, j, vN);
        bool stand = j == c.N;
        var sp = kind == ClimbKind.Wilt ? sk.Weakp(ndir, stand ? WeakStage(c) : 0) : sk.Step(VarOf(j), ndir, stand ? StandStage(c) : 0);
        int total = 0, inside = 0, seen = 0;
        for (int yy = 0; yy < sp.P.H; yy++)
            for (int xx = 0; xx < sp.P.W; xx++)
            {
                uint col = sp.P.P[yy * sp.P.W + xx];
                if (col >> 24 == 0) continue;
                total++;
                int cx = x - sp.Ax + xx, cy = y - sp.Ay + yy;
                if (cx < 0 || cy < 0 || cx >= CW || cy >= PH) continue;
                inside++;
                if (worldCells![cy * CW + cx] == col) seen++;
            }
        return total == 0 ? (0, 0) : (inside / (double)total, seen / (double)total);
    }

    internal (double X, double FootY) PetDrawn(Climb c, ClimbShow s)
    {
        PetNow(c, s, out double x, out double fy, out _, out _);
        return (x, fy);
    }

    // 점 (x, y) 에서 줄기(잎 위 이은 선)까지의 거리 (칸): 건너뛰는 볼록이가 길에서 안 벗어나는지
    internal double PathGap(Climb c, double x, double y)
    {
        double best = double.MaxValue;
        int n0 = (int)Math.Floor(vN);
        for (int j = n0 - 1; j <= n0 + 2; j++)
        {
            var (x0, y0) = LeafAt(c, j, vN);
            var (x1, y1) = LeafAt(c, j + 1, vN);
            double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy, t = len2 == 0 ? 0 : Math.Clamp(((x - x0) * dx + (y - y0) * dy) / len2, 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(x0 + dx * t - x, 2) + Math.Pow(y0 + dy * t - y, 2)));
        }
        return best;
    }
}
