using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Px = Bolok.ClimbArt.Px;

namespace Bolok;

// 클라이밍 그리기 (docs/climb.md 2-3·5장, 시안 run/mockups/climb): 판 하나를 칸 그림판(Px)에 그려 ×f 로 키운 그림 한 장으로 얹는다.
// 판 아래(바탕)는 칠하지 않는다 — 덩굴·잎·줍는 것·볼록이와 가벼운 표시만 (모양 A2 콩나무 투명, 모양 A4 창 테두리 덩굴). 그림자는 점무늬 (색 열쇠 창이라 반투명이 안 된다).
// 판을 열 때 그림을 다 만들어 두고 프레임에서는 찍기만 한다: 칸 그림판·큰 그림 버퍼를 다시 쓰고 (고정해 둔 배열이 곧 비트맵), 바뀐 게 없으면 다시 그리지 않는다.

// 모양: A2 = 콩나무 투명 (넓은 창·바탕화면), A4 = 창 테두리 덩굴 (좁은 판, 줄기가 창 왼쪽·오른쪽 테두리에 걸친다)
enum ClimbLook { A2, A4 }

// 볼록이 한 자세 (칸 그림 40×40, 왼쪽·오른쪽 보기)
sealed class ClimbPose
{
    public readonly Px[] R, L;
    public int Frames => R.Length;
    public ClimbPose(Sprites sp, string row)
    {
        int n = sp.Frames(row);
        R = new Px[n]; L = new Px[n];
        for (int i = 0; i < n; i++)
        {
            R[i] = Wrap(sp.Get(row, i, 1, false));
            L[i] = Wrap(sp.Get(row, i, 1, true));
        }
    }
    static Px Wrap(Bitmap b) { var p = new Px(b.Width, b.Height); Array.Copy(ClimbArt.FromBitmap(b), p.P, p.P.Length); return p; }
    public static ClimbPose? Try(Sprites sp, string row) => sp.Has(row) ? new ClimbPose(sp, row) : null;
    public Px Get(int frame, bool left) => (left ? L : R)[Math.Abs(frame) % R.Length];

    // 모든 프레임을 합친 찬 칸의 상자 (오른쪽 보기): 친구·볼록이가 안 겹치는지 볼 때
    public (int X0, int X1, int Y0, int Y1) Box()
    {
        int x0 = 99, x1 = -1, y0 = 99, y1 = -1;
        foreach (var p in R)
            for (int y = 0; y < p.H; y++) for (int x = 0; x < p.W; x++)
                if (p.On(x, y)) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
        return (x0, x1, y0, y1);
    }

    // 그림에서 가장 아래 찬 줄 (발이 닿는 줄)
    public static int FootRow(Sprites sp, string idleRow)
    {
        var p = new ClimbPose(sp, idleRow).R[0];
        int low = 0;
        for (int y = 0; y < p.H; y++) for (int x = 0; x < p.W; x++) if (p.On(x, y)) low = y;
        return low;
    }
}

// 볼록이 자세 묶음 (판을 열 때 한 번): 지금 모습의 옆걸음·떨어짐·기뻐함 …
sealed class ClimbPet
{
    public readonly ClimbPose Walk, Fly, Dizzy, Shock, Happy, Sad, Slide, Freeze, Look, Held;
    public readonly int Foot;
    public readonly int HalfW, Tall, HeldTop;       // 걷는 몸의 반 너비·키 (가운데 x 20 기준, 발 줄에서 위로), 매달린 자세의 머리 줄
    public ClimbPet(Sprites sp, string stage)
    {
        string st = stage;
        ClimbPose Get(string pose) => ClimbPose.Try(sp, $"{st}/0/{pose}") ?? ClimbPose.Try(sp, $"{st}/0/idle")!;
        (Walk, Fly, Dizzy, Shock, Happy, Sad, Slide, Freeze, Look, Held) = (Get("walk"), Get("fly"), Get("dizzy"), Get("shock"), Get("happy"), Get("sad"), Get("slide"), Get("freeze"), Get("look"), Get("held"));
        Foot = ClimbPose.FootRow(sp, $"{st}/0/idle");
        var b = Walk.Box();
        HalfW = Math.Max(20 - b.X0, b.X1 - 19);
        Tall = Foot - b.Y0 + 1;
        HeldTop = Held.Box().Y0;
    }
}

// 100잎마다 손 흔드는 친구 한 마리 (없으면 새)
sealed class ClimbFriend
{
    public readonly string Species;
    public readonly ClimbPose? Hop;
    public readonly int Foot;
    public readonly int HalfW, Tall;                // 몸 상자 (가운데 x 20 기준 반 너비, 발 줄에서 위로). 새는 15×11
    public ClimbFriend(string species, Sprites sp)
    {
        Species = species;
        Hop = ClimbPose.Try(sp, "adult_sweet/0/hop");
        Foot = Hop != null ? ClimbPose.FootRow(sp, "adult_sweet/0/idle") : 0;
        if (Hop != null) { var b = Hop.Box(); HalfW = Math.Max(20 - b.X0, b.X1 - 19); Tall = Foot - b.Y0 + 1; }
        else (HalfW, Tall) = (7, 11);
    }
}

// 한 프레임 그릴 때 게임이 넘기는 값: 프레임마다 새로 만들지 않고 고쳐 쓴다
sealed class ClimbShow
{
    public double Clock;
    public bool Paused;                  // 판이 앞 창이 아니다 (두리번)
    public bool Result, NewBest, Guide;   // Guide: 시작 안내 말풍선이 떠 있다
    public int Best;                     // 결과 화면의 최고 기록
    public int BestLeaf = -1;            // 지난 최고 기록 잎 (작은 왕관). −1 이면 없다
    public int FriendAt = -1;            // 친구가 온 이정표 잎 (그 높이), −1 이면 없다
    public int FriendLeaf, FriendSide, FriendLen, FriendGap;   // 친구가 선 곁가지: 붙은 잎, 쪽(−1·+1), 길이(칸), 볼록이 몸과 떨어진 칸 (PlaceFriend 가 정한다)
    public double FriendAge;
    public ClimbFriend? Friend;
}

sealed partial class ClimbView : IDisposable
{
    public const int PH = 176, Foot = 136, DX = 22, DY = 16, WideA2 = 150, WideA4 = 96, SideA4 = 13;
    public readonly ClimbLook Look;
    public readonly ClimbSkin? Skin;      // 덩굴 말고 넷의 그림 (덩굴이면 null)
    public ClimbTheme Theme => Skin?.Theme ?? ClimbTheme.Vine;
    public readonly int F, CW;
    readonly Sprites art;
    readonly ClimbPet pet;

    // 칸 그림판과 큰 그림 (배열이 곧 비트맵: 프레임마다 잠그거나 새로 만들지 않는다)
    readonly uint[] cells;
    readonly int[] big;
    readonly GCHandle pin;
    readonly Bitmap bmp;

    // 그림
    readonly ClimbArt.Sprite[,] leaf = new ClimbArt.Sprite[6, 2], leafBig = new ClimbArt.Sprite[6, 2], leafDroop = new ClimbArt.Sprite[6, 2];
    readonly ClimbArt.Sprite[] wilt = new ClimbArt.Sprite[2], wiltDroop = new ClimbArt.Sprite[2];
    readonly ClimbArt.Seg[,] seg = new ClimbArt.Seg[4, 2];      // [dx 22·−8·8·−22][굽는 쪽]
    readonly ClimbArt.Seg strip = null!;
    readonly Px berry = null!, puff = null!, puffHead = null!, curl = null!, curlOpen = null!, petiole = null!, star = null!, smallCloud = null!;
    readonly Px flower, flowerBud;
    readonly Px[] curlFx = new Px[5];                // 덩굴손: 말림 · 풀리는 중 · 쭉 뻗음 · 되튐 · 다 폄
    readonly Px[] fog = new Px[2], bird = new Px[2], cloudPuff = new Px[3];
    readonly Px[] iconSmall;   // 표시 아이콘 (배율 1): 싱싱·노랑·갈색·빈 칸 (테마마다 다르다)
    readonly Bitmap[] icon = new Bitmap[4];
    static readonly int[] LensA2 = { 40, 54, 78 }, LensA4 = { 26 };   // 친구가 서는 곁가지 길이 (칸)
    readonly ClimbArt.Seg[,] branch = new ClimbArt.Seg[2, 3];     // 친구가 선 곁가지 [왼쪽·오른쪽][길이]
    readonly ClimbArt.Sprite[] branchLeaf = new ClimbArt.Sprite[2];

    // 지금 (시선·그림이 따라오는 것)
    double vN, viewX;                 // 보이는 높이 (잎), 시선 가운데 (A2: 칸)
    bool started;
    (int, int, int, int, int, int, int, int, int, int, int) sig, lastSig;
    public bool WorldChanged { get; private set; }
    bool needCompose = true;
    Climb? t;
    ClimbShow? sh;

    public double VN => vN;
    public double ViewX => viewX;

    public ClimbView(Sprites art, string stage, ClimbLook look, int f, ClimbTheme theme = ClimbTheme.Vine)
    {
        (this.art, Look, F) = (art, look, f);
        Skin = look == ClimbLook.A2 ? ClimbSkin.Make(theme) : null;   // 창 테두리(A4)는 덩굴뿐
        CW = look == ClimbLook.A2 ? WideA2 : WideA4;
        pet = new ClimbPet(art, stage);
        cells = new uint[CW * PH];
        big = new int[CW * f * PH * f];
        pin = GCHandle.Alloc(big, GCHandleType.Pinned);
        bmp = new Bitmap(CW * f, PH * f, CW * f * 4, PixelFormat.Format32bppPArgb, pin.AddrOfPinnedObject());
        if (Skin != null)
        {
            iconSmall = Skin.Pip;
            int sk = GameHud.Small(f);
            for (int i = 0; i < 4; i++) icon[i] = Scaled(iconSmall[i], sk);
            for (int i = 0; i < 2; i++) bird[i] = ClimbArt.Bird(i);
            (flower, flowerBud) = (ClimbArt.Flower(), ClimbArt.FlowerBud());   // 50잎 이정표: 발판 위에 피는 꽃
            for (int si = 0; si < 2; si++)                                    // 친구 받침 (왼쪽·오른쪽 × 길이 셋) 을 미리
                for (int li = 0; li < LensA2.Length; li++) { perch[si, li] = BakePerch(Skin, si == 0 ? -1 : 1, LensA2[li], out int bx0); perchBx[si * 3 + li] = bx0; }
            return;
        }
        // 잎 (구역 여섯 × 왼쪽·오른쪽)
        for (int z = 0; z < 6; z++)
            for (int d = 0; d < 2; d++)
            {
                int dir = d == 0 ? -1 : 1;
                leaf[z, d] = ClimbArt.Leaf(ClimbArt.ZoneLeaf[z], dir);
                leafBig[z, d] = ClimbArt.Leaf(ClimbArt.ZoneLeaf[z], dir, false, true);
                leafDroop[z, d] = ClimbArt.Leaf(ClimbArt.ZoneLeaf[z], dir, true);
            }
        for (int d = 0; d < 2; d++) { wilt[d] = ClimbArt.Leaf(ClimbArt.Wilt, d == 0 ? -1 : 1); wiltDroop[d] = ClimbArt.Leaf(ClimbArt.Wilt, d == 0 ? -1 : 1, true); }
        int[] dxs = { 22, -8, 8, -22 };
        for (int k = 0; k < 4; k++) for (int b = 0; b < 2; b++) seg[k, b] = ClimbArt.StemSeg(dxs[k], -DY, b == 0 ? 1 : -1);
        strip = ClimbArt.StemStrip();
        (berry, puff, puffHead, curl, curlOpen, petiole) = (ClimbArt.Berry(), ClimbArt.Puff(), ClimbArt.PuffHead(), ClimbArt.Curl(), ClimbArt.Curl(true), ClimbArt.Petiole());
        (flower, flowerBud) = (ClimbArt.Flower(), ClimbArt.FlowerBud());
        star = ClimbArt.Glyph(new[] { "..o..", ".oyo.", "oyyyo", ".oyo.", "..o.." }, ch => ch == 'y' ? ClimbArt.C("#FFF6C8") : ch == 'o' ? ClimbArt.C("#B9A85A") : 0);
        for (int i = 0; i < 2; i++) { bird[i] = ClimbArt.Bird(i); fog[i] = ClimbArt.Cloud(i == 0 ? 44 : 40, 12, 31 + i * 7, ClimbArt.White, ClimbArt.C("#E4ECF5"), ClimbArt.C("#A9BCD2")); }
        for (int i = 0; i < 3; i++) cloudPuff[i] = ClimbArt.Cloud(10 + i * 3, 5, i * 5 + 1, ClimbArt.White, ClimbArt.C("#E4ECF5"), ClimbArt.C("#A9BCD2"));
        smallCloud = cloudPuff[0];
        iconSmall = new[] { ClimbArt.Icon(0), ClimbArt.Icon(1), ClimbArt.Icon(2), ClimbArt.Icon(3) };
        int sm = GameHud.Small(f);
        for (int i = 0; i < 4; i++) icon[i] = Scaled(iconSmall[i], sm);
        curlFx[0] = curl; curlFx[1] = ClimbArt.CurlSpring(1); curlFx[2] = ClimbArt.CurlSpring(2); curlFx[3] = ClimbArt.CurlSpring(3); curlFx[4] = curlOpen;
        var lens = look == ClimbLook.A2 ? LensA2 : LensA4;
        foreach (int s in new[] { -1, 1 })
        {
            for (int li = 0; li < lens.Length; li++) branch[s < 0 ? 0 : 1, li] = BranchSeg(s, lens[li]);
            branchLeaf[s < 0 ? 0 : 1] = ClimbArt.Leaf(ClimbArt.Fresh, s);
        }
    }

    // 곁가지 (친구가 서는 잎까지): 가운데에서 s 쪽으로 len 칸, 살짝 위로. 원점은 줄기 쪽 끝
    static ClimbArt.Seg BranchSeg(int s, double len)
    {
        var pts = new List<(double X, double Y)>();
        for (double u = 0; u <= 1; u += .03) pts.Add((s * len * u, -14 * u + Math.Sin(u * Math.PI) * 3));
        int minX = (int)pts.Min(p => p.X) - 4, minY = (int)pts.Min(p => p.Y) - 4, maxX = (int)pts.Max(p => p.X) + 4, maxY = (int)pts.Max(p => p.Y) + 4;
        var o = new Px(maxX - minX + 1, maxY - minY + 1);
        var fl = new Px(o.W, o.H);
        foreach (var (x, y) in pts) o.Ell(x - minX, y - minY, 2.1, 2.1, ClimbArt.Line);
        foreach (var (x, y) in pts) fl.Ell(x - minX, y - minY, 1.1, 1.1, ClimbArt.StemFill);
        return new ClimbArt.Seg(o, fl, minX, minY);
    }

    static Bitmap Scaled(Px g, int k)
    {
        var px = new int[g.W * k * g.H * k];
        for (int y = 0; y < g.H * k; y++) for (int x = 0; x < g.W * k; x++) px[y * g.W * k + x] = unchecked((int)g.P[y / k * g.W + x / k]);
        var b = new Bitmap(g.W * k, g.H * k, PixelFormat.Format32bppPArgb);
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        for (int y = 0; y < b.Height; y++) Marshal.Copy(px, y * b.Width, d.Scan0 + y * d.Stride, b.Width);
        b.UnlockBits(d);
        return b;
    }

    public void Dispose()
    {
        bmp.Dispose();
        if (pin.IsAllocated) pin.Free();
        foreach (var b in icon) b.Dispose();
    }

    // ── 잎 자리 ──
    // 잎 j (음수는 시작 잎 아래로 같은 줄이 이어진다) 의 정보
    (int Dir, int Col, ClimbKind Kind, bool Fog, bool Taken) Info(Climb c, int j)
    {
        if (j >= 0) { ref var l = ref c.At(j); return (l.Dir, l.Col, l.Kind, l.Fog, l.Taken); }
        ref var l0 = ref c.At(0);
        return (l0.Dir, l0.Col + j * l0.Dir, ClimbKind.Leaf, false, false);
    }

    // 잎 윗면 가운데 (칸 그림판 안 자리). colX: A2 에서 시선 가운데를 뺀 가로
    (int X, int Y) LeafAt(Climb c, int j, double vn)
    {
        var (dir, col, _, _, _) = Info(c, j);
        int y = (int)Math.Round(Foot - DY * (j - vn));
        int x = Look == ClimbLook.A2 ? (int)Math.Round(CW / 2.0 + col * DX - viewX) : CW / 2 + dir * SideA4;
        return (x, y);
    }

    // 시선 목표 (A2): 지금 잎에서 다섯 잎 앞까지의 가운데. 볼록이가 판 가운데 ±50칸 안에서 떠다닌다
    public static double TargetX(Climb c, double vn)
    {
        int n0 = (int)Math.Floor(vn);
        double sum = 0;
        for (int k = 0; k < 5; k++) sum += c.At(n0 + k).Col;
        double mean = sum / 5 * DX, own = c.At(n0).Col * DX;
        return Math.Clamp(mean, own - 50, own + 50);
    }

    // 볼록이 가로 (칸 그림판 안): 높이 vn 에서
    double PetX(Climb c, double vn)
    {
        int i0 = (int)Math.Floor(vn);
        double fr = vn - i0;
        var (d0, c0, _, _, _) = Info(c, i0);
        var (d1, c1, _, _, _) = Info(c, i0 + 1);
        return Look == ClimbLook.A2 ? CW / 2.0 + (c0 + (c1 - c0) * fr) * DX - viewX : CW / 2.0 + (d0 + (d1 - d0) * fr) * SideA4;
    }

    // 볼록이 발 자리 (화면). 판이 열릴 때와 끝난 뒤 본체를 놓을 자리
    public Point PetFoot(Rectangle panel, Climb c) => new(panel.X + (int)Math.Round(PetX(c, c.N) * F), panel.Y + Foot * F);

    // ── 한 프레임: 시선·그림이 따라오고, 바뀐 것을 알아낸다 ──
    public void Update(Climb c, ClimbShow s, double dt)
    {
        (t, sh) = (c, s);
        if (!started)
        {
            started = true;
            vN = c.N;
            viewX = Look == ClimbLook.A2 ? TargetX(c, vN) : 0;
        }
        // 그림이 규칙을 따라온다: 한 칸 0.08초. 빨리 두드리면 밀린 만큼 서둘러 따라잡는다
        // 건너뛰기(덩굴손): 덩굴손 잎까지 한 칸 폴짝, 그 뒤 건너뛰는 잎을 줄기를 따라 한 칸씩 (LiftSec 안에 끝난다)
        double gap = c.N - vN;
        if (c.LiftLeft > 0 && c.Lifts > 0 && c.N - c.PrevN > 1)
        {
            double e = c.T.LiftSec - c.LiftLeft, hop = c.T.HopSec;
            int leaf = c.N - c.T.CurlUp;
            double want = e < hop ? c.PrevN + e / hop : leaf + c.T.CurlUp * Math.Min(1, (e - hop) / (c.T.LiftSec - hop));
            vN = Math.Min(c.N, Math.Max(vN, want));
        }
        else if (gap > 0) vN = Math.Min(c.N, vN + Math.Max(1.0, gap) / c.T.HopSec * dt);
        else vN = c.N;
        if (Look == ClimbLook.A2)
        {
            double target = TargetX(c, vN);
            viewX += (target - viewX) * (1 - Math.Exp(-dt * 9));
            double own = c.At((int)Math.Floor(vN)).Col * DX;
            viewX = Math.Clamp(viewX, own - 50, own + 50);   // 볼록이가 판 가운데 ±50칸 안에
        }
        var pose = PetNow(c, s, out _, out _, out _, out int pf);
        if (s.FriendAt >= 0 && s.FriendSide == 0 && vN >= c.N - 1e-6) PlaceFriend(c, s);          // 이정표에 내려선 뒤 친구가 나타난다
        if (s.FriendAt >= 0 && s.FriendSide != 0 && FriendTooClose(c, s)) s.FriendAt = -1;       // 볼록이가 가까이 지나가면 친구는 손 흔들고 돌아간다 (겹치지 않게)
        int grow = Grow(c, s);
        int stg = Skin != null ? StandStage(c) * 4 + WeakStage(c) * 2 : 0;
        sig = (c.N, (int)Math.Round(vN * 24), (int)Math.Round(viewX * 4), (int)c.State * 8 + (c.Puff ? 4 : 0) + (s.Paused ? 2 : 0) + (s.Result ? 1 : 0),
            pf, grow, (c.WiltLeft > 0 ? 1 + (c.WiltLeft < c.T.WiltSec * .6 ? 1 : 0) : 0) + stg * 4,
            s.FriendAt >= 0 ? 1 + ((int)(s.FriendAge * 5) & 1) + (s.FriendAge < 3.2 ? 0 : 4) : 0,
            c.MileAt > 0 && c.Clock - c.MileAt < .6 ? 1 + (int)((c.Clock - c.MileAt) * 14) : 0,
            c.Done ? (c.SinceFall < .8 ? 1 + (int)(c.SinceFall * 40) : 99) : 0, c.Berries * 64 + c.Saves * 8 + (c.BerryAt > 0 && c.Clock - c.BerryAt < .25 ? 1 + (int)((c.Clock - c.BerryAt) * 20) : 0) + (s.Guide ? 100000 : 0));
        _ = pose;
        WorldChanged = sig != lastSig || needCompose;
        if (WorldChanged) { lastSig = sig; needCompose = true; }
    }

    // 판이 열릴 때 콩나무가 솟는 정도 (0.4초: 잎이 아래부터 차례로 돋는다). 완전히 자란 뒤는 99
    int Grow(Climb c, ClimbShow s) => s.Clock < .4 && c.N == 0 && c.Steps == 0 ? (int)(s.Clock / .4 * 12) : 99;

    // ── 볼록이 자세: 규칙 상태 → (그림, 프레임, 자리, 왼쪽 보기) ──
    // 서 있는 발판이 가라앉으면(풍선·과자) 볼록이도 같이 가라앉는다 (sink 줄). 건너뛰기(덩굴손)는 줄기를 따라 한 칸씩 (테마마다 나르는 방식이 다르다)
    Px PetNow(Climb c, ClimbShow s, out double x, out double footY, out bool left, out int poseKey)
    {
        int i0 = (int)Math.Floor(vN);
        double fr = vN - i0;
        x = PetX(c, vN);
        int sink = SinkOf(c);
        footY = Foot + sink;
        left = c.At(i0 + 1).Dir < 0;                 // 다음 잎 쪽을 본다 (보는 쪽이 곧 방향 힌트)
        ClimbPose p = pet.Walk;
        int frame = 0, id = 1;
        double age = c.SinceFall;
        if (c.State == Climb.Phase.Fall || c.State == Climb.Phase.Over)
        {
            // 떨어짐: 틀린 쪽이면 허공에서 shock → fly 로 빙글 아래 잎으로 → dizzy. 시간·시든 잎이면 slide → fly → dizzy
            bool wrong = c.Why == Climb.Cause.Wrong;
            double t0 = wrong ? .15 : .25, run = .45;
            var (lx, ly) = (PetX(c, c.N), (double)Foot + sink);        // 발판이 가라앉았으면 그 자리에서 떨어진다
            var (bx, by) = (PetX(c, c.N - 2), Foot + 2 * DY * 1.0);
            int dirW = wrong ? c.WrongDir : c.LastDir;
            if (age < t0) { p = wrong ? pet.Shock : pet.Slide; frame = (int)(age * 12); footY = ly - (wrong ? 4 : 0); id = wrong ? 20 : 21; left = dirW < 0; }
            else if (age < t0 + run)
            {
                double q = (age - t0) / run;
                x = lx + (bx - lx) * q + dirW * 10 * Math.Sin(q * Math.PI);
                footY = ly + (by - ly) * q * q - 10 * 4 * q * (1 - q) * .5;
                (p, frame, id, left) = (pet.Fly, (int)(age * 14), 22, dirW < 0);
            }
            else { x = bx; footY = by; (p, frame, id, left) = (pet.Dizzy, (int)(age * 6), 23, false); }
        }
        else if (s.Paused) { p = pet.Look; frame = (int)(s.Clock * 2.5); id = 2; }
        else if (c.SaveLeft > 0)
        {
            double q = 1 - c.SaveLeft / c.T.SaveSec;
            p = pet.Held; frame = (int)(s.Clock * 6); footY = Foot + sink - 8 * Math.Sin(q * Math.PI) - 4; id = 3;
        }
        else if (Lifting(c))
        {
            // 건너뛰는 중: 줄기를 따라 한 칸씩 (fr = 지금 칸 안에서 얼마나 갔나). 덩굴은 폴짝폴짝, 그 밖은 나르는 것에 실려
            int land = c.N - 1;
            double sk = i0 >= land ? sink * fr : 0;                    // 마지막 칸에서 가라앉은 발판에 내려앉는다
            double arc = 4 * fr * (1 - fr);
            switch (Skin?.Lift)
            {
                case ClimbLift.Glide: (p, frame, id, footY) = (pet.Walk, 0, 8, Foot - 2 - 2 * Math.Sin(s.Clock * 9) + sk); break;      // 비행기 위에 선 채 미끄러진다
                case ClimbLift.Hang:  (p, frame, id, footY) = (pet.Held, (int)(s.Clock * 6), 9, Foot + 7 - 2 * Math.Sin(s.Clock * 7) + sk); x += Math.Sin(s.Clock * 8) * 2.2; break;   // 풍선 아래 매달려 흔들
                case ClimbLift.Swing: (p, frame, id, footY) = (pet.Held, (int)(s.Clock * 6), 10, Foot + 5 - 7 * Math.Sin(fr * Math.PI) + sk); break;   // 줄에 매달려 그네처럼
                case ClimbLift.Bounce: (p, frame, id, footY) = (pet.Walk, 1, 11, Foot - 15 * arc + sk); break;                          // 용수철로 통통
                default: (p, frame, id, footY) = (pet.Walk, 1, 4, Foot - 7 * arc); break;                                              // 덩굴: 빠르게 폴짝폴짝
            }
            left = c.At(i0 + 1).Dir < 0;
        }
        else if (vN < c.N - 1e-6)
        {
            p = pet.Walk; frame = 1; id = 5;
            footY = Foot + sink * Math.Max(0, vN - (c.N - 1)) - 6 * 4 * fr * (1 - fr);             // 한 칸 폴짝 (6칸 높이 포물선)
            left = c.At(i0 + 1).Dir < 0;
        }
        else if (c.Clock - c.MileAt < .3 && c.MileAt > 0) { p = pet.Happy; frame = (int)(s.Clock * 6); id = 6; }
        else if (c.State == Climb.Phase.Run && c.Bar < .15) { p = pet.Freeze; frame = (int)(s.Clock * 4); id = 7; }
        poseKey = id * 8 + Math.Abs(frame) % p.Frames + (left ? 4096 : 0);
        return p.Get(frame, left);
    }

    // ── 칸 그림판에 그리기 ──
    void PutPx(Px s, int x, int y)
    {
        int x0 = Math.Max(0, -x), x1 = Math.Min(s.W, CW - x), y0 = Math.Max(0, -y), y1 = Math.Min(s.H, PH - y);
        if (x0 >= x1) return;
        for (int j = y0; j < y1; j++)
        {
            int si = j * s.W, di = (y + j) * CW + x;
            for (int i = x0; i < x1; i++) { uint c = s.P[si + i]; if (c >> 24 != 0) cells[di + i] = c; }
        }
    }

    void Thing(Climb c, int j, ClimbKind kind, bool done, int x, int y, int dir)
    {
        switch (kind)
        {
            case ClimbKind.Berry: if (!done) PutPx(berry, x + dir * 3 - 4, y - 8); break;
            case ClimbKind.Puff: if (!done) PutPx(puff, x - dir * 2 - 8, y - 18); break;
            case ClimbKind.Curl: { var cf = curlFx[CurlFrame(c, j)]; PutPx(cf, x + dir * 2 - 9, y + 1 - cf.H); break; }   // 밟으면 풀리며 튀어 오른다
            case ClimbKind.Flower: PutPx(done ? flower : flowerBud, x + dir * 9 - 7, y - 8); break;
        }
    }

    static ulong Hash(int a, int b) { ulong h = (ulong)(a * 73856093 ^ b * 19349663) ^ 0x9E3779B97F4A7C15UL; h ^= h >> 29; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 32; return h; }

    void Compose()
    {
        var c = t!; var s = sh!;
        Array.Clear(cells);
        int n0 = (int)Math.Floor(vN);
        int lo = n0 - 3, hi = n0 + 10;
        int grow = Grow(c, s);
        // 뒤: 친구 (덩굴이 그 위를 덮는다: 길이 가려지지 않게)
        if (s.FriendAt >= 0 && s.FriendSide != 0 && Math.Abs(s.FriendLeaf - vN) < 8) FriendPaint(c, s);
        if (Skin != null) ComposeSkin(c, s, lo, hi, grow);
        else ComposeVine(c, s, lo, hi, n0, grow);
        // 볼록이 (건너뛰는 중 나르는 것은 볼록이 밑·위에)
        var pp = PetNow(c, s, out double px, out double footY, out _, out _);
        if (Skin != null) CarrierPaint(c, s, px, footY);
        PutPx(pp, (int)Math.Round(px) - 20, (int)Math.Round(footY) - pet.Foot);
        if (c.Puff && c.SaveLeft <= 0 && c.State != Climb.Phase.Over && c.State != Climb.Phase.Fall && !Lifting(c))   // 지닌 홀씨: 머리 위에 둥실
            HeadThing(px, footY, (int)Math.Round(Math.Sin(s.Clock * 3) * 1.2), false);
        if (c.SaveLeft > 0) HeadThing(px, footY, 0, true);
        Shadow();
        Upscale();
        needCompose = false;
    }

    // 덩굴(콩나무) 판의 세계: 줄기 · 잎 · 줍는 것 · 구름 띠
    void ComposeVine(Climb c, ClimbShow s, int lo, int hi, int n0, int grow)
    {
        bool Grown(int j) => grow == 99 || j <= grow - 3;       // 솟는 중이면 아래 잎부터
        // 줄기
        if (Look == ClimbLook.A2)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int j = lo; j < hi; j++)
                {
                    if (!Grown(j + 1) || j + 1 >= 0 && c.Hidden(j + 1)) continue;   // 구름에 가린 잎으로 가는 줄기는 안 그린다 (길이 안 읽히게)
                    var (d0, _, _, _, _) = Info(c, j);
                    var (d1, _, _, _, _) = Info(c, j + 1);
                    var (x0, y0) = LeafAt(c, j, vN);
                    int k = (15 * d1 + 7 * d0) switch { 22 => 0, -8 => 1, 8 => 2, _ => 3 };
                    var sg = seg[k, ((j & 1) == 0) ? 0 : 1];
                    PutPx(pass == 0 ? sg.Out : sg.In, x0 - d0 * 7 + sg.Ox, y0 + 4 + sg.Oy);
                }
        }
        else
        {
            double leaf0 = Foot + DY * vN;                    // 잎 0 의 높이
            int start = (int)Math.Round(leaf0 % ClimbArt.StripH);
            for (int pass = 0; pass < 2; pass++)
                for (int y = start - ClimbArt.StripH; y < PH; y += ClimbArt.StripH)
                    PutPx(pass == 0 ? strip.Out : strip.In, CW / 2 + strip.Ox, y);
        }
        // 잎 (아래부터: 위 잎이 위에)
        for (int j = lo; j <= hi; j++)
        {
            if (!Grown(j) || j >= 0 && c.Hidden(j)) continue;
            var (dir, _, kind, fogged, taken) = Info(c, j);
            var (x, y) = LeafAt(c, j, vN);
            int d = dir < 0 ? 0 : 1, z = c.Zone(Math.Max(j, 0));
            if (Look == ClimbLook.A4) PutPx(petiole, CW / 2 + dir * 3 - 4, y + 5 - 3);
            bool droop = j == c.N && (c.Done && c.Why != Climb.Cause.Wrong || c.WiltLeft > 0 && c.WiltLeft < c.T.WiltSec * .6);
            var sp = kind switch
            {
                ClimbKind.Wilt => droop ? wiltDroop[d] : wilt[d],
                ClimbKind.Flower => leafBig[z, d],
                _ => droop ? leafDroop[z, d] : leaf[z, d],
            };
            if (y - sp.Ay + sp.P.H < 6) continue;                       // 판 위로 거의 나간 잎은 그리지 않는다 (조각만 남아 점처럼 보인다)
            PutPx(sp.P, x - sp.Ax, y - sp.Ay);
            if (kind != ClimbKind.Leaf && kind != ClimbKind.Wilt) Thing(c, j, kind, kind == ClimbKind.Flower ? j <= vN + .01 : taken, x, y, dir);
            Deco(c, j, kind, z, x, y, dir);
            _ = fogged;
        }
        // 구름 띠: 가린 잎 자리마다 구름 (넷째 잎부터)
        for (int j = Math.Max(n0 + c.T.CloudHide, 0); j <= hi; j++)
        {
            if (!c.Hidden(j) || !Grown(j)) continue;
            var (x, y) = LeafAt(c, j, vN);
            var cl = fog[(j & 1)];
            PutPx(cl, x - cl.W / 2, y - 8);
        }
    }

    // 높이 구역 장식 (잎 색 말고): 구름 구역은 잎 곁 작은 구름, 노을은 꽃, 별밤·달은 반짝 (잎마다 정해진 대로 — 규칙과 무관)
    void Deco(Climb c, int j, ClimbKind kind, int z, int x, int y, int dir)
    {
        if (j < 0 || z < 2) return;
        ulong h = Hash(j, z);
        switch (z)
        {
            case 2: if (h % 3 == 0) { var cl = cloudPuff[(int)((h >> 8) % 3)]; PutPx(cl, x + (dir > 0 ? -26 : 12), y - 2 - (int)((h >> 12) % 4)); } break;
            case 3: if (kind == ClimbKind.Leaf && h % 4 == 0) PutPx(flower, x + dir * 8 - 7, y - 9); break;
            default: if (h % 3 == 0) PutPx(star, x + (int)((h >> 9) % 28) - 16, y - 3 - (int)((h >> 15) % 7)); break;
        }
    }

    // 둘레 그림자: 한 점씩 어긋난 점무늬 (색 열쇠 창이라 반투명 없이). 뒤에서 앞으로 돌아 제자리에서 처리한다 (읽는 칸은 아직 안 바뀐 칸)
    void Shadow()
    {
        int ox = Look == ClimbLook.A4 ? 2 : 1, oy = Look == ClimbLook.A4 ? 3 : 2;
        for (int y = PH - 1; y >= oy; y--)
        {
            int yb = y & 3, rowI = y * CW, rowS = (y - oy) * CW;
            for (int x = CW - 1; x >= ox; x--)
            {
                if (cells[rowI + x] != 0 || cells[rowS + x - ox] == 0) continue;
                if (ClimbArt.Bayer[yb, x & 3] < 5) cells[rowI + x] = ClimbArt.ShadowDot;
            }
        }
    }

    // 칸 그림판 → 큰 그림 (칸 하나 = f×f 점). 빈 칸이 대부분이라 큰 그림을 지우고 찬 칸만 채운다
    void Upscale()
    {
        int f = F, w = CW * f;
        Array.Clear(big);
        var span = big.AsSpan();
        for (int y = 0; y < PH; y++)
        {
            int ci = y * CW, o = y * f * w;
            for (int x = 0; x < CW; x++)
            {
                uint c = cells[ci + x];
                if (c == 0) continue;
                int v = unchecked((int)c), p = o + x * f;
                for (int k = 0; k < f; k++) span.Slice(p + k * w, f).Fill(v);
            }
        }
    }

    // ── 표시 (화면 점): 점수 쪽지 · 시간 조각 · 최고 기록 왕관 · 반짝 · 결과 ──
    void Fill(Graphics g, SolidBrush b, int x, int y, int w, int h) => g.FillRectangle(b, x, y, w, h);

    public Rectangle ScoreRect(Rectangle panel, int digits)
    {
        int k = F, small = GameHud.Small(k), x0 = Look == ClimbLook.A4 ? 4 * k : 6 * k, y0 = 7 * k;
        int tw = 5 * small * digits + 4 * k;
        return new Rectangle(panel.X + x0 - 2 * k + 1 - 1, panel.Y + y0 - 4 * k - 1, tw + 2, 8 * k + 2);
    }

    public Rectangle PipsRect(Rectangle panel)
    {
        int k = F, small = GameHud.Small(k), x0 = Look == ClimbLook.A4 ? 4 * k : 6 * k, y0 = 7 * k;
        if (Look == ClimbLook.A4) return new Rectangle(panel.X + x0 - 2 * k, panel.Y + y0 + 4 * k, 3 * 10 * small + 9 * small, 2 * (17 * small / 2) + 9 * small + 4);
        return new Rectangle(panel.Right - 6 * k - 6 * 10 * small - 2, panel.Y + y0 - 2 * k - 2, 6 * 10 * small + 4, 9 * small + 6);
    }

    public Rectangle CrownRect(Rectangle panel, Climb c, int leafIdx)
    {
        var (x, y) = LeafAt(c, leafIdx, vN);
        int sm = GameHud.Small(F);
        return new Rectangle(panel.X + x * F + 10 * F / 3 - 8 * sm, panel.Y + (y - 7) * F - 8 * sm, 16 * sm, 16 * sm);
    }

    public void Draw(Overlay o, Graphics g, Rectangle panel, Climb c, ClimbShow s)
    {
        if (needCompose) Compose();
        int k = F, small = GameHud.Small(k), ox = panel.X - o.Left, oy = panel.Y - o.Top;
        g.DrawImageUnscaled(bmp, ox, oy);
        var stamps = Stamps.Of(o.Art);
        // 효과 그림(반짝·하트·느낌표)은 판 안에만 (다시 칠할 자리가 판 하나라 밖에 그리면 지워지지 않는다)
        int inset = 7 * k;
        int Cx(double v) => Math.Clamp(panel.X + (int)v, panel.X + inset, panel.Right - inset);
        int Cy(double v) => Math.Clamp(panel.Y + (int)v, panel.Y + inset, panel.Bottom - inset);
        // 지난 최고 기록 잎에 작은 왕관
        if (s.BestLeaf >= 0 && s.BestLeaf - c.N is > -4 and < 11 && !c.Hidden(s.BestLeaf))
        {
            var (bx, by) = LeafAt(c, s.BestLeaf, vN);
            stamps.Get("game/crown", 0, small).Put(o, g, Cx(bx * k + 10 * k / 3), Cy((by - 7) * k));
        }
        // 이정표·열매: 반짝
        double ma = c.Clock - c.MileAt;
        if (c.MileAt > 0 && ma < .6)
        {
            var (px, py) = PetCenter(c, s);
            int mq = (int)(ma * 14);          // 다시 칠하는 때(world 서명)와 같은 눈금으로 그린다
            double mm = mq / 14.0;
            foreach (var (dx, dy) in Spark)
                stamps.Get("game/spark", (mq ^ dx) & 1, small).Put(o, g, Cx((px + dx * (1 + mm)) * k), Cy((py + dy * (1 + mm)) * k));
        }
        else if (c.BerryAt > 0 && c.Clock - c.BerryAt < .25)
        {
            var (px, py) = PetCenter(c, s);
            stamps.Get("game/spark", (int)((c.Clock - c.BerryAt) * 20) & 1, small).Put(o, g, Cx((px + 12) * k), Cy((py - 16) * k));
        }
        if (s.FriendAt >= 0 && s.FriendAge < 3.2 && Stamps.Of(o.Art).Has("emo/heart"))
        {
            var (fx, fy) = FriendHead(c, s);
            stamps.Get("emo/heart", 0, small).Put(o, g, Cx(fx * k), Cy(fy * k));
        }
        if (c.State == Climb.Phase.Fall && c.Why == Climb.Cause.Wrong && c.SinceFall < .35 && stamps.Has("emo/bang"))
        {
            var (px, py) = PetCenter(c, s);
            stamps.Get("emo/bang", 0, small).Put(o, g, Cx(px * k - 14 * k * c.WrongDir), Cy((py - 12) * k));
        }
        if (Lifting(c)) LiftFx(o, g, panel, c, s, stamps, small);
        if (!s.Result) Hud(o, g, panel, c, s);
        else ResultBox(o, g, panel, c, s);
    }

    static readonly (int, int)[] Spark = { (-20, -30), (16, -36), (-6, -44), (24, -12), (-26, -8) };

    (double X, double Y) PetCenter(Climb c, ClimbShow s)
    {
        PetNow(c, s, out double x, out double fy, out _, out _);
        return (x, fy - 20);
    }

    (int X, int Y) FriendHead(Climb c, ClimbShow s)
    {
        var (_, _, _, tx, ty) = FriendSpot(c, s);
        return (tx + 2, ty - 46);
    }

    void Hud(Overlay o, Graphics g, Rectangle panel, Climb c, ClimbShow s)
    {
        int k = F, small = GameHud.Small(k);
        int x0 = Look == ClimbLook.A4 ? 4 * k : 6 * k, y0 = 7 * k;
        // 점수: 작은 크림 쪽지 (글줄 위에서도 숫자가 읽힌다)
        int digits = Math.Max(1, GameHud.Digits(c.N));
        int tw = 5 * small * digits + 4 * k, tx = panel.X + x0 - 2 * k + 1, ty = panel.Y + y0 - 4 * k, th = 8 * k;
        Box(g, o, tx, ty, tw, th, k);
        GameHud.Number(o, g, c.N, panel.X + x0 + 5 * small / 2, panel.Y + y0, small, -1);
        // 시간: 잎 조각 여섯 (싱싱 → 노랑 → 갈색, 빈 조각은 회색)
        int pips = 6, lit = (int)Math.Ceiling(c.Bar * pips - 1e-9);
        int st = c.Bar <= .15 ? 2 : c.Bar <= .35 ? 1 : 0;
        for (int i = 0; i < pips; i++)
        {
            var ic = icon[i >= lit ? 3 : st];
            int px, py;
            if (Look == ClimbLook.A4) { px = panel.X + x0 - 2 * k + i % 3 * 10 * small; py = panel.Y + y0 + 5 * k - 1 + i / 3 * (17 * small / 2); }
            else { px = panel.Right - 6 * k - (pips - i) * 10 * small; py = panel.Y + y0 - 2 * k; }
            g.DrawImageUnscaled(ic, px - o.Left, py - o.Top);
        }
    }

    // 쪽지: 크림 테두리, 갈색 속, 모서리를 한 칸 깎았다 (시안의 점수 쪽지: 갈색 가운데 · 크림 안쪽 — 숫자는 크림 바탕에 갈색)
    void Box(Graphics g, Overlay o, int x, int y, int w, int h, int k)
    {
        x -= o.Left; y -= o.Top;
        g.FillRectangle(Pal.Line, x + k, y, w - 2 * k, h); g.FillRectangle(Pal.Line, x, y + k, w, h - 2 * k);
        g.FillRectangle(Pal.Cream, x + k, y + k, w - 2 * k, h - 2 * k);
    }

    // 결과 (GameHud.Result 와 같은 배치): 잎 아이콘 × 점수, 새 기록이면 큰 왕관, 아니면 작은 왕관 + 최고
    void ResultBox(Overlay o, Graphics g, Rectangle panel, Climb c, ClimbShow s)
    {
        int k = F, big = k + 1, small = GameHud.Small(k);
        var stamps = Stamps.Of(o.Art);
        int cx = panel.X + panel.Width / 2, cy = (int)(panel.Y + panel.Height * .45);
        bool second = !s.NewBest && s.Best > 0;
        int digits = GameHud.Digits(c.N) + 1;
        int w1 = 14 * k + digits * 5 * big, w2 = second ? (13 + 5 * GameHud.Digits(s.Best)) * small : 0;
        int half = Math.Max(w1, w2) / 2 + 5 * k, row2 = cy + 5 * big + 2 * k + 4 * small;
        var box = o.Client(Rectangle.FromLTRB(cx - half, cy - 5 * big - 3 * k, cx + half, (second ? row2 + 4 * small : cy + 5 * big) + 3 * k));
        GameHud.Box(g, box, k);
        g.DrawImageUnscaled(icon[0], cx - w1 / 2 + 2 * k - 3 - o.Left, cy - 8 - o.Top);
        GameHud.Number(o, g, c.N, cx - w1 / 2 + 14 * k + 5 * big / 2, cy, big, -1, 'x');
        if (s.NewBest) stamps.Get("game/crown", 0, big).Put(o, g, cx, cy - 5 * big - 3 * k - 7 * big);
        else if (second)
        {
            int x = cx - w2 / 2;
            stamps.Get("game/crown", 0, small).Put(o, g, x + 6 * small, row2);
            GameHud.Number(o, g, s.Best, x + 13 * small + 5 * small / 2, row2, small, -1);
        }
    }

    // 다시 칠할 자리 (화면 좌표)
    public Rectangle ResultRect(Rectangle panel, Climb c, ClimbShow s)
    {
        int k = F, big = k + 1;
        int cx = panel.X + panel.Width / 2, cy = (int)(panel.Y + panel.Height * .45);
        return Rectangle.Inflate(Rectangle.FromLTRB(cx - 40 * k, cy - 5 * big - 3 * k - 16 * big, cx + 40 * k, cy + 5 * big + 3 * k + 10 * k), 2 * k, 2 * k);
    }

    public void ForceCompose() => needCompose = true;

    // 자가 검사: 만든 그림 모음
    internal IEnumerable<(string, Px)> Gallery()
    {
        if (Skin != null)
        {
            foreach (var x in Skin.Gallery()) yield return x;
            yield return ("꽃", flower); yield return ("꽃봉오리", flowerBud);
            for (int i = 0; i < 2; i++) yield return ($"새 {i}", bird[i]);
            for (int si = 0; si < 2; si++) for (int li = 0; li < LensA2.Length; li++) yield return ($"친구 받침 {si}/{li}", perch[si, li]);
            yield break;
        }
        for (int i = 1; i < 4; i++) yield return ($"풀리는 덩굴손 {i}", curlFx[i]);
        for (int z = 0; z < 6; z++) for (int d = 0; d < 2; d++) { yield return ($"잎 {z}/{d}", leaf[z, d].P); yield return ($"큰 잎 {z}/{d}", leafBig[z, d].P); yield return ($"처진 잎 {z}/{d}", leafDroop[z, d].P); }
        for (int d = 0; d < 2; d++) { yield return ($"시든 잎 {d}", wilt[d].P); yield return ($"처진 시든 잎 {d}", wiltDroop[d].P); }
        for (int k = 0; k < 4; k++) for (int b = 0; b < 2; b++) { yield return ($"덩굴 테두리 {k}/{b}", seg[k, b].Out); yield return ($"덩굴 속 {k}/{b}", seg[k, b].In); }
        yield return ("줄기 테두리", strip.Out); yield return ("줄기 속", strip.In);
        foreach (var (n, p) in new[] { ("열매", berry), ("홀씨", puff), ("머리 홀씨", puffHead), ("덩굴손", curl), ("펴진 덩굴손", curlOpen), ("꽃", flower), ("꽃봉오리", flowerBud), ("잎자루", petiole), ("반짝", star) }) yield return (n, p);
        for (int i = 0; i < 2; i++) { yield return ($"구름 {i}", fog[i]); yield return ($"새 {i}", bird[i]); }
        for (int i = 0; i < 3; i++) yield return ($"작은 구름 {i}", cloudPuff[i]);
        for (int i = 0; i < 4; i++) yield return ($"표시 잎 {i}", iconSmall[i]);
    }

    public byte[] CellsCopy() => cells.SelectMany(v => BitConverter.GetBytes(v)).ToArray();
    public Bitmap Board => bmp;
}
