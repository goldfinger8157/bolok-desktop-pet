using Px = Bolok.ClimbArt.Px;

namespace Bolok;

// 클라이밍 그리기의 테마 (ClimbView 의 나머지 반): docs/climb.md 「테마」
// · 테마 판(책상 위 문구·과자 탑·풍선·장난감 블록)은 덩굴 대신 발판 그림을 찍는다. 규칙·자리·시간은 같다
// · 서 있는 발판이 줄어들면(풍선·과자) 볼록이가 같이 가라앉는다 (SinkOf)
// · 콤보 효과·카페는 ClimbViewFx.cs
sealed partial class ClimbView
{
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

    void PutSk(SkinPx m, int x, int y) => PutPx(m.P, x - m.Ax, y - m.Ay);

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
            MarkAhead(c, j, sp.P, x - sp.Ax, y - sp.Ay);
            switch (kind)
            {
                case ClimbKind.Berry: if (!taken) PutSk(sk.Berry, x + ndir * 4, y); break;
                case ClimbKind.Puff: if (!taken) PutSk(sk.Save, x - ndir * 3, y); break;
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
