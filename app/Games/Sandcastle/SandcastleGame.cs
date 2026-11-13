using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Bolok;

// 모래성 모듈: 앱에 붙이기(판·조작·볼록이), 그리기, 기록, 디버그 명령
// 규칙은 Sandcastle.cs / SandGrid.cs, 자가 검사·장면·속도 재기는 SandcastleCheck.cs
sealed partial class SandcastleGame : IGameMode
{
    readonly IGameHost host;
    Sandcastle? game;
    Session? ses;
    int wins, plays;
    internal bool showMe; // 장면 그림에서만: 볼록이 본체도 여기서 그린다

    // 모래 비트맵 버퍼 (성 화면 좌표 크기, 고속 LockBits)
    Bitmap? sandBmp;
    int[]? pixelBuf;
    int bmpW, bmpH;

    // 자주 찍는 스탬프 캐시
    Sprites? artOf;
    int artK;
    Stamp star = null!, crown = null!;
    Stamp meIdleStamp = null!, meHappyStamp = null!, meDizzyStamp = null!;
    readonly Dictionary<string, Stamp[]>[] rivalStamps = { new(), new() };
    public string RivalSpecies { get; internal set; } = "";
    Sprites? rivalSprites;

    // GDI+ 브러시 및 펜 캐시 (프레임당 메모리 할당 0B)
    static readonly SolidBrush poleBrush1 = new(Color.FromArgb(130, 90, 56));
    static readonly SolidBrush poleBrush2 = new(Color.FromArgb(100, 66, 40));
    static readonly SolidBrush goldBrush = new(Color.FromArgb(255, 220, 80));
    static readonly SolidBrush redBrush = new(Color.FromArgb(235, 70, 70));
    static readonly SolidBrush whiteBrush = new(Color.White);
    static readonly SolidBrush darkOutBrush = new(Color.FromArgb(30, 28, 36));
    static readonly SolidBrush glowBrush = new(Color.FromArgb(60, 255, 240, 120));
    static readonly SolidBrush handle1Brush = new(Color.FromArgb(130, 90, 56));
    static readonly SolidBrush handle2Brush = new(Color.FromArgb(100, 66, 40));
    static readonly SolidBrush bladeBrush = new(Color.FromArgb(80, 160, 220));
    static readonly SolidBrush edgeBrush = new(Color.FromArgb(40, 90, 150));
    static readonly SolidBrush sandLtBrush = new(Color.FromArgb(252, 232, 176));
    static readonly SolidBrush sandBaseBrush = new(Color.FromArgb(238, 208, 140));
    static readonly SolidBrush arrowRedFill = new(Color.FromArgb(255, 90, 80));
    static readonly SolidBrush arrowRedDk = new(Color.FromArgb(120, 30, 30));
    static readonly SolidBrush arrowGreyFill = new(Color.FromArgb(150, 154, 168));
    static readonly SolidBrush arrowGreyDk = new(Color.FromArgb(70, 74, 90));

    static readonly Pen angerPen = new(Color.FromArgb(240, 90, 80), 2);
    static readonly Pen chargePen = new(Color.FromArgb(220, 255, 230, 110), 2) { DashStyle = DashStyle.Dash };

    static readonly string[] arrowRows = { ".111.", ".111.", "11111", ".111.", "..1.." };
    static readonly string[] handRows =
    {
        "....11....",
        "....11....",
        "....11111.",
        ".11.111111",
        ".111111111",
        "..11111111",
        "...1111111",
        "....111111",
    };
    readonly Point[] flagPts = new Point[3];

    internal void InitRival(Random? rnd = null)
    {
        rnd ??= new Random();
        string mySpecies = PetArt.Base(host.Sprites.Species);
        var friends = host.Friends
            .Select(PetArt.Base)
            .Where(s => !string.IsNullOrEmpty(s) && s != mySpecies)
            .Distinct()
            .ToList();

        if (friends.Count > 0)
        {
            RivalSpecies = friends[rnd.Next(friends.Count)];
        }
        else
        {
            var others = PetArt.Embedded.Species
                .Where(s => !string.IsNullOrEmpty(s) && s != mySpecies && s != "egg")
                .ToList();
            if (others.Count > 0)
                RivalSpecies = others[rnd.Next(others.Count)];
            else
                RivalSpecies = mySpecies == "corgi" ? "cat" : "corgi";
        }

        rivalSprites = host.SpritesOf(RivalSpecies);
        rivalStamps[0].Clear();
        rivalStamps[1].Clear();
    }

    // Dirty 사각형 추적
    readonly List<Rectangle> dirty = new(64);
    int freshFrames;
    Rectangle lastChangeRect;
    Rectangle lastPetMarkRect, lastRivalMarkRect;
    Rectangle lastCursorRect, lastScoopAnimRect, lastFlagRect;
    int lastTurnKey;

    public SandcastleGame(IGameHost host) => this.host = host;

    public string Id => "sandcastle";
    public string? MenuKey => "menu.sandcastle";
    public bool Running => game != null;

    public void Start() => Begin(null);

    void Begin(Rectangle? targetCastle)
    {
        if (Running) return;
        var b = host.Pet;
        var screens = new List<Rectangle>();
        foreach (var s in Screen.AllScreens) screens.Add(s.WorkingArea);

        Rectangle field;
        Rectangle castle;

        if (targetCastle.HasValue)
        {
            field = screens.FirstOrDefault(s => s.IntersectsWith(targetCastle.Value), screens[0]);
            castle = targetCastle.Value;
        }
        else
        {
            // minW, minH: 볼록이 몇 마리 크기 (너비 250px, 높이 160px 이상)
            int minW = Math.Max(250, b.CellPx * 3);
            int minH = Math.Max(160, b.CellPx * 2);
            var (f, win) = WindowPick.Arena(Native.Foreground(), Native.ListWindows(), b.Area, screens, b.CellPx, minW, minH);
            if (win == null)
            {
                // 쓸 만한 창이 없음: 말 대신 nowin 표정과 고개 젓기
                b.Emote("nowin", 3);
                b.ShakeUntil = b.Now + 0.5;
                Dbg.Log("sandcastle-nowin");
                return;
            }
            (field, castle) = (f, win.Value.R);
        }

        var w = new Sandcastle(field, castle, b.Scale, new Random());
        InitRival(w.Rnd);
        Open(w, Session.Open(host, field, () => Stop(true)));
        b.StartPuppet();
        Dbg.Log("mode-start", ("id", Id), ("field", field.ToString()), ("castle", castle.ToString()));
    }

    internal void Open(Sandcastle w, Session s, bool events = true)
    {
        (game, ses) = (w, s);
        if (string.IsNullOrEmpty(RivalSpecies)) InitRival(w.Rnd);
        var b = host.Pet;
        if (events)
        {
            w.OnEvent = ev =>
            {
                if (ev == "scoop") Snd.Play(Sound.Tap);
                else if (ev == "invalid_scoop")
                {
                    b.ShakeUntil = b.Now + 0.5;
                    Snd.Play(Sound.Tap);
                }
                else if (ev == "flag_fall") Snd.Play(Sound.Boom);
                else if (ev == "won") { b.Emote("star", 2.0); Snd.Play(Sound.Pick); }
                else if (ev == "lost") { Snd.Play(Sound.Hurt); }
            };
        }

        freshFrames = 0;
        InitSandBitmap(w);
        Sync();
        Track();
    }

    void InitSandBitmap(Sandcastle w)
    {
        sandBmp?.Dispose();
        var b = w.Grid.FieldBounds;
        bmpW = b.Width;
        bmpH = b.Height;
        sandBmp = new Bitmap(bmpW, bmpH, PixelFormat.Format32bppPArgb);
        pixelBuf = new int[bmpW * bmpH];

        UpdateSandBitmap(w, forceAll: true);
    }

    void UpdateSandBitmap(Sandcastle w, bool forceAll = false)
    {
        if (sandBmp == null || pixelBuf == null) return;
        var grid = w.Grid;

        int gx0 = forceAll ? 0 : Math.Max(0, grid.MinChangeX);
        int gy0 = forceAll ? 0 : Math.Max(0, grid.MinChangeY);
        int gx1 = forceAll ? grid.Gw - 1 : Math.Min(grid.Gw - 1, grid.MaxChangeX);
        int gy1 = forceAll ? grid.Gh - 1 : Math.Min(grid.Gh - 1, grid.MaxChangeY);

        if (gx0 > gx1 || gy0 > gy1) return;

        int c = grid.CellSize;
        int px0 = gx0 * c;
        int py0 = gy0 * c;
        int px1 = Math.Min(bmpW - 1, (gx1 + 1) * c - 1);
        int py1 = Math.Min(bmpH - 1, (gy1 + 1) * c - 1);
        int dirtyW = px1 - px0 + 1;
        int dirtyH = py1 - py0 + 1;

        if (dirtyW <= 0 || dirtyH <= 0) return;

        // 1. pixelBuf 에 변경 셀들 갱신
        for (int gy = gy0; gy <= gy1; gy++)
        {
            int basePy = gy * c;
            for (int gx = gx0; gx <= gx1; gx++)
            {
                int basePx = gx * c;
                int idx = grid.Index(gx, gy);
                int argb = (grid.Cells[idx] == SandGrid.CellType.Empty) ? 0 : grid.Colors[idx];

                for (int dy = 0; dy < c; dy++)
                {
                    int py = basePy + dy;
                    if (py >= bmpH) break;
                    int rowOffset = py * bmpW + basePx;
                    for (int dx = 0; dx < c; dx++)
                    {
                        if (basePx + dx >= bmpW) break;
                        pixelBuf[rowOffset + dx] = argb;
                    }
                }
            }
        }

        // 2. sandBmp 에 변경된 사각형만 LockBits 로 고속 복사
        var rect = new Rectangle(px0, py0, dirtyW, dirtyH);
        var data = sandBmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (int dy = 0; dy < dirtyH; dy++)
            {
                int srcOffset = (py0 + dy) * bmpW + px0;
                IntPtr dstPtr = data.Scan0 + dy * data.Stride;
                System.Runtime.InteropServices.Marshal.Copy(pixelBuf, srcOffset, dstPtr, dirtyW);
            }
        }
        finally
        {
            sandBmp.UnlockBits(data);
        }
    }

    public void Frame(double dt)
    {
        var (w, s) = (game!, ses!);
        s.Tick();

        if (w.Stage == Sandcastle.Phase.PlayerTurn)
        {
            var m = s.Mouse;
            if (s.LeftHit) w.StartCharging(m.X, m.Y);
            if (s.Left) w.UpdateCharge(m.X, m.Y, dt);
            if (s.RightHit) w.CancelCharge();
            if (s.LeftUp) w.ReleaseCharge(out _);
        }

        w.Update(dt);
        Sync();
        Track();

        // 결과 화면: 약 2.5초 대기 (0.2초 이후 클릭하면 바로 닫힘)
        if (w.Done)
        {
            double elapsed = w.Clock - w.EndAt;
            if (elapsed >= 2.5 || (elapsed > 0.2 && (s.LeftHit || s.RightHit)))
            {
                Stop(false);
            }
        }
    }

    void Sync()
    {
        var (w, b) = (game!, host.Pet);
        (b.X, b.Y, b.Dir) = (w.PetX, w.PetY, 1);
        b.Pose = (w.Stage == Sandcastle.Phase.Won) ? "happy" : (w.Stage == Sandcastle.Phase.Lost) ? "dizzy" : "idle";
    }

    public void Stop(bool quit)
    {
        if (game is not { } w) return;
        game = null;
        var b = host.Pet;
        bool won = (w.Stage == Sandcastle.Phase.Won);
        if (won) wins++;
        plays++;

        ses?.Close(b, quit, won, "sandcastle", ("result", won ? "win" : "lost"), ("turns", w.TurnsCount));
        ses = null;

        sandBmp?.Dispose();
        sandBmp = null;
        pixelBuf = null;

        Dbg.Log("mode-end", ("id", Id));
        host.Save();
    }

    public void Dirty(Overlay o, List<Rectangle> r)
    {
        foreach (var rect in dirty)
        {
            var cr = o.Client(rect);
            if (!cr.IsEmpty) r.Add(cr);
        }
    }

    void Track()
    {
        var w = game!;
        int k = w.K;
        Art(k);
        dirty.Clear();

        if (freshFrames < 2)
        {
            freshFrames++;
            dirty.Add(w.Field);
            dirty.Add(w.Grid.FieldBounds);
        }

        // 모래 변경 부분 비트맵 업데이트 및 dirty 추가
        if (w.Grid.HasChanges)
        {
            UpdateSandBitmap(w);
            var chg = w.Grid.GetChangeRectScreen();
            if (!chg.IsEmpty)
            {
                dirty.Add(Rectangle.Inflate(chg, w.Grid.CellSize, w.Grid.CellSize));
                lastChangeRect = chg;
            }
        }

        // 깃발 영역
        int flagH = 24 * k;
        var flagRect = new Rectangle((int)w.FlagBaseX - 20 * k, (int)w.FlagBaseY - flagH - 4 * k, 40 * k, flagH + 8 * k);
        if (flagRect != lastFlagRect || w.FlagAngle > 0)
        {
            dirty.Add(lastFlagRect);
            dirty.Add(flagRect);
            lastFlagRect = flagRect;
        }

        // 볼록이 & 라이벌 머리 위 화살표
        int arrowW = 16 * k, arrowH = 16 * k;
        var petMark = new Rectangle((int)w.PetX - arrowW / 2, (int)w.PetY - 32 * k - arrowH, arrowW, arrowH);
        var rivalMark = new Rectangle((int)w.RivalX - arrowW / 2, (int)w.RivalY - 32 * k - arrowH, arrowW, arrowH);
        int turnKey = (int)w.Stage * 10 + (int)w.ActiveTurn;
        if (turnKey != lastTurnKey || petMark != lastPetMarkRect || rivalMark != lastRivalMarkRect)
        {
            dirty.Add(lastPetMarkRect);
            dirty.Add(petMark);
            dirty.Add(lastRivalMarkRect);
            dirty.Add(rivalMark);
            (lastPetMarkRect, lastRivalMarkRect, lastTurnKey) = (petMark, rivalMark, turnKey);
        }

        // 커서 위치 (빛나는 삽 및 충전 원)
        if (w.Stage == Sandcastle.Phase.PlayerTurn && ses is { } s)
        {
            int r = (int)Math.Ceiling(Math.Max(w.ChargeRadius, 24 * k));
            var curRect = new Rectangle(s.Mouse.X - r - 2 * k, s.Mouse.Y - r - 2 * k, (r + 2 * k) * 2, (r + 2 * k) * 2);
            dirty.Add(lastCursorRect);
            dirty.Add(curRect);
            lastCursorRect = curRect;
        }
        else if (!lastCursorRect.IsEmpty)
        {
            dirty.Add(lastCursorRect);
            lastCursorRect = Rectangle.Empty;
        }

        // 삽 퍼내기 연출 (Scoop Animation)
        if (w.ScoopAnimActive)
        {
            var scRect = new Rectangle((int)w.ScoopAnimX - 20 * k, (int)w.ScoopAnimY - 20 * k, 40 * k, 40 * k);
            dirty.Add(lastScoopAnimRect);
            dirty.Add(scRect);
            lastScoopAnimRect = scRect;
        }
        else if (!lastScoopAnimRect.IsEmpty)
        {
            dirty.Add(lastScoopAnimRect);
            lastScoopAnimRect = Rectangle.Empty;
        }

        // 라이벌 본체 사각형
        var rivalBox = RivalBox(w);
        dirty.Add(rivalBox);

        // 장면 그림 등에서 showMe일 때 볼록이 영역도 dirty
        if (showMe)
        {
            dirty.Add(Fx.PetRect(w.PetX, w.PetY, 40 * k));
        }

        // 게임 종료 시 결과창 영역
        if (w.Done)
        {
            dirty.Add(GameHud.CenterRect(w.CastleRect, k));
        }
    }

    public void Draw(Overlay o, Graphics g)
    {
        if (game is not { } w) return;
        int k = w.K;

        // 1. 모래성 & 바닥 비트맵 그리기
        if (sandBmp != null)
        {
            var bounds = o.Client(w.Grid.FieldBounds);
            g.DrawImageUnscaled(sandBmp, bounds.X, bounds.Y);
        }

        // 2. 깃발 (창 꼭대기 중앙)
        DrawFlag(o, g, w);

        // 3. 라이벌 그리기 (오른쪽 땅 위)
        DrawRival(o, g, w);

        // 4. 볼록이 그리기 (showMe 일 때)
        if (showMe)
        {
            DrawMe(o, g, w);
        }

        // 5. 차례 화살표 표시 (시안 4번)
        DrawTurnMarks(o, g, w);

        // 6. 퍼내기 충전 원 및 커서 (내 차례일 때)
        if (w.Stage == Sandcastle.Phase.PlayerTurn && ses is { } s)
        {
            DrawCursor(o, g, w, s.Mouse.X, s.Mouse.Y);
        }

        // 7. 퍼내기 연출 (삽 위에 모래 한 덩이)
        if (w.ScoopAnimActive)
        {
            DrawScoopAnim(o, g, w);
        }

        // 8. 승리 왕관·별, 패배 열기 (시안 5번: 글자·숫자 상자 없이)
        if (w.Done)
        {
            bool won = (w.Stage == Sandcastle.Phase.Won);
            int winnerX = (int)(won ? w.PetX : w.RivalX);
            int winnerY = (int)(won ? w.PetY : w.RivalY) - 34 * k;
            crown.Put(o, g, winnerX, winnerY - 14 * k);
            star.Put(o, g, winnerX, winnerY);

            // 진 쪽 머리 위에 빨간 열기/당황 사선 셋 (시안 5번 c5)
            int loserX = (int)(won ? w.RivalX : w.PetX);
            int loserY = (int)(won ? w.RivalY : w.PetY) - 36 * k;
            var lp = o.ToClient(loserX, loserY);
            for (int i = 0; i < 3; i++)
            {
                g.DrawLine(angerPen, lp.X - 6 * k + i * 5 * k, lp.Y, lp.X - 4 * k + i * 5 * k, lp.Y - 5 * k);
            }
        }
    }

    // ── 깃발 그리기 (각도에 따라 쓰러짐 및 더미 위 안착 반영) ──
    void DrawFlag(Overlay o, Graphics g, Sandcastle w)
    {
        int k = w.K;
        var p = o.ToClient(w.FlagBaseX, w.FlagCurrentY);
        int poleH = 20 * k;

        var state = g.Save();
        g.TranslateTransform(p.X, p.Y);
        float deg = (float)(w.FlagAngle * 85.0); // 0도 ~ 85도 완전히 누움
        g.RotateTransform(deg);

        // 깃대 (갈색 2줄)
        g.FillRectangle(poleBrush1, -k, -poleH, k, poleH);
        g.FillRectangle(poleBrush2, 0, -poleH, k, poleH);

        // 꼭대기 금색 구
        g.FillRectangle(goldBrush, -k, -poleH - k, 2 * k, k);

        // 빨간 삼각형 깃발 (시안 make.py)
        flagPts[0] = new Point(0, -poleH);
        flagPts[1] = new Point(8 * k, -poleH + 3 * k);
        flagPts[2] = new Point(0, -poleH + 6 * k);
        g.FillPolygon(redBrush, flagPts);

        g.Restore(state);
    }

    // ── 차례 표시 (시안 4번 dn_arrow) ──
    void DrawTurnMarks(Overlay o, Graphics g, Sandcastle w)
    {
        int k = w.K;
        bool myTurn = (w.ActiveTurn == Sandcastle.TurnActor.Player);

        int petY = (int)w.PetY - 36 * k;
        int rivalY = (int)w.RivalY - 36 * k;

        // 화살표 상하 까딱거림
        int bob = (int)(Math.Sin(w.Clock * 6) * (1.5 * k));

        DrawArrow(o, g, (int)w.PetX, petY + (myTurn ? bob : 0), k, myTurn ? arrowRedFill : arrowGreyFill, myTurn ? arrowRedDk : arrowGreyDk);
        DrawArrow(o, g, (int)w.RivalX, rivalY + (!myTurn ? bob : 0), k, !myTurn ? arrowRedFill : arrowGreyFill, !myTurn ? arrowRedDk : arrowGreyDk);
    }

    static void DrawArrow(Overlay o, Graphics g, int cx, int cy, int k, SolidBrush bFill, SolidBrush bOut)
    {
        var p = o.ToClient(cx, cy);
        int s = Math.Max(2, k); // 화살표 도트 크기 (선명하게 보이도록)

        // 테두리 1칸 두껍게 먼저 그리고 속에 채움
        for (int y = -1; y <= 5; y++)
        {
            for (int x = -1; x <= 5; x++)
            {
                bool isOutline = false;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int ny = y + dy, nx = x + dx;
                        if (ny >= 0 && ny < 5 && nx >= 0 && nx < 5 && arrowRows[ny][nx] == '1')
                            isOutline = true;
                    }
                bool isCore = (y >= 0 && y < 5 && x >= 0 && x < 5 && arrowRows[y][x] == '1');
                int px = p.X + (x - 2) * s;
                int py = p.Y + (y - 2) * s;
                if (isCore) g.FillRectangle(bFill, px, py, s, s);
                else if (isOutline) g.FillRectangle(bOut, px, py, s, s);
            }
        }
    }

    // ── 내 차례 커서: 빛나는 삽 + 흰 손 + 충전 원 (시안 3, 4번) ──
    void DrawCursor(Overlay o, Graphics g, Sandcastle w, int mx, int my)
    {
        int k = w.K;
        var p = o.ToClient(mx, my);

        // 충전 중인 원
        if (w.Charging && w.ChargeRadius > 0)
        {
            int r = (int)w.ChargeRadius;
            g.DrawEllipse(chargePen, p.X - r, p.Y - r, 2 * r, 2 * r);
        }

        // 부드러운 노란 빛 (glow, 시안 make3.py)
        int glowR = 18 * k;
        g.FillEllipse(glowBrush, p.X - glowR, p.Y - glowR, 2 * glowR, 2 * glowR);

        // 삽과 손 그리기
        DrawShovel(g, p.X + 4 * k, p.Y - 6 * k, k);
        DrawHand(g, p.X, p.Y, k);
    }

    // 시안 make.py hand (흰 장갑 손)
    static void DrawHand(Graphics g, int x, int y, int k)
    {
        int s = Math.Max(1, k);

        for (int ry = -1; ry <= handRows.Length; ry++)
        {
            for (int rx = -1; rx <= 10; rx++)
            {
                bool isCore = (ry >= 0 && ry < handRows.Length && rx >= 0 && rx < handRows[ry].Length && handRows[ry][rx] == '1');
                bool isOut = false;
                if (!isCore)
                {
                    for (int dy = -1; dy <= 1 && !isOut; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int ny = ry + dy, nx = rx + dx;
                            if (ny >= 0 && ny < handRows.Length && nx >= 0 && nx < handRows[ny].Length && handRows[ny][nx] == '1')
                            {
                                isOut = true;
                                break;
                            }
                        }
                }
                int px = x + (rx - 5) * s;
                int py = y + (ry - 4) * s;
                if (isCore) g.FillRectangle(whiteBrush, px, py, s, s);
                else if (isOut) g.FillRectangle(darkOutBrush, px, py, s, s);
            }
        }
    }

    static void DrawShovel(Graphics g, int x, int y, int k)
    {
        int s = Math.Max(1, k);

        // 자루 (대각선으로 서 있는 막대)
        for (int i = 0; i < 7; i++)
        {
            g.FillRectangle(handle1Brush, x + i * s, y - i * s * 2, s, s * 2);
            g.FillRectangle(handle2Brush, x + i * s + s, y - i * s * 2, s, s * 2);
        }

        // 삽 날 (네모난 파란 날)
        g.FillRectangle(bladeBrush, x - 3 * s, y, 6 * s, 4 * s);
        g.FillRectangle(edgeBrush, x - 3 * s, y + 4 * s, 6 * s, s);
    }

    // ── 퍼내기 연출 (삽 위에 모래 한 덩이 + 손, 시안 3번) ──
    void DrawScoopAnim(Overlay o, Graphics g, Sandcastle w)
    {
        int k = w.K;
        var p = o.ToClient(w.ScoopAnimX, w.ScoopAnimY);

        // 삽과 손
        DrawShovel(g, p.X, p.Y, k);
        DrawHand(g, p.X - 4 * k, p.Y + 2 * k, k);

        // 삽 날 위에 모래알 덩어리 (시안 3번 scoop)
        int s = Math.Max(1, k);
        g.FillRectangle(sandBaseBrush, p.X - 3 * s, p.Y - 2 * s, 6 * s, 3 * s);
        g.FillRectangle(sandLtBrush, p.X - 2 * s, p.Y - 3 * s, 4 * s, 2 * s);
    }

    // ── 라이벌 그리기 (작은 삽을 든 모습) ──
    Rectangle RivalBox(Sandcastle w)
    {
        int k = w.K;
        var st = RivalStamp(w);
        var box = st.Box((int)w.RivalX, (int)w.RivalY - 20 * k);
        return Rectangle.Inflate(box, 10 * k, 10 * k);
    }

    void DrawRival(Overlay o, Graphics g, Sandcastle w)
    {
        int k = w.K;
        var st = RivalStamp(w);
        st.Put(o, g, (int)w.RivalX, (int)w.RivalY - 20 * k);

        // 라이벌 손에 쥔 작은 삽
        var p = o.ToClient(w.RivalX - 10 * k, w.RivalY - 8 * k);
        DrawShovel(g, p.X, p.Y, Math.Max(1, k / 2));
    }

    Stamp RivalStamp(Sandcastle w)
    {
        string pose = (w.Stage == Sandcastle.Phase.Won) ? "dizzy" : (w.Stage == Sandcastle.Phase.Lost) ? "happy" : "idle";
        var rows = rivalStamps[1]; // 왼쪽 보기
        if (!rows.TryGetValue(pose, out var row))
        {
            var sp = rivalSprites ?? artOf!;
            string form = (rivalSprites != null) ? host.Pet.P.Stage : "adult_grumpy";
            rows[pose] = row = Stamps.Of(sp).Row($"{form}/1/{pose}", w.K, true);
        }
        return row[(int)(w.Clock * 4) % row.Length];
    }

    void DrawMe(Overlay o, Graphics g, Sandcastle w)
    {
        var stamp = (w.Stage == Sandcastle.Phase.Won) ? meHappyStamp : (w.Stage == Sandcastle.Phase.Lost) ? meDizzyStamp : meIdleStamp;
        stamp?.Put(o, g, (int)w.PetX, (int)w.PetY - 20 * w.K);
    }

    void Art(int k)
    {
        var sp = host.Sprites;
        if (ReferenceEquals(artOf, sp) && artK == k) return;
        (artOf, artK) = (sp, k);
        var st = Stamps.Of(sp);
        int small = GameHud.Small(k);
        star = st.Get("game/star", 0, small);
        crown = st.Get("game/crown", 0, small);
        var b = host.Pet;
        meIdleStamp = st.Get($"{b.P.Stage}/1/idle", 0, k, false);
        meHappyStamp = st.Get($"{b.P.Stage}/1/happy", 0, k, false);
        meDizzyStamp = st.Get($"{b.P.Stage}/1/dizzy", 0, k, false);
        if (!string.IsNullOrEmpty(RivalSpecies)) rivalSprites = host.SpritesOf(RivalSpecies);
        rivalStamps[0].Clear();
        rivalStamps[1].Clear();
    }

    // ── 디버그 명령 ──
    // sandcastle / sandcastle scoop <x> <y> <r> / sandcastle cpu / sandcastle end win|lose
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != Id) return false;

        if (a.Length > 1 && a[1] == "stop") Stop(true);
        else if (a.Length > 1 && a[1] == "cpu" && game is { } wCpu)
        {
            var (cx, cy, cr) = wCpu.PlanCpuMove();
            wCpu.TryExecuteScoop(cx, cy, cr, wCpu.ActiveTurn, out _);
        }
        else if (a.Length > 4 && a[1] == "scoop" && game is { } wScoop)
        {
            double sx = double.Parse(a[2], CultureInfo.InvariantCulture);
            double sy = double.Parse(a[3], CultureInfo.InvariantCulture);
            double sr = double.Parse(a[4], CultureInfo.InvariantCulture);
            wScoop.TryExecuteScoop(sx, sy, sr, Sandcastle.TurnActor.Player, out _);
        }
        else if (a.Length > 2 && a[1] == "end" && game is { } wEnd)
        {
            wEnd.ForceEnd(win: a[2] == "win");
        }
        else if (!Running && !host.Busy)
        {
            Begin(null);
        }
        return true;
    }

    // ── 기록 및 통계 ──
    public IEnumerable<GameStat> Stats()
    {
        yield return new GameStat(L.T("game.sandcastle"), wins > 0 ? L.T("game.sandcastle_val", wins) : L.T("game.sandcastle_none"), wins > 0);
    }

    sealed record Record(int Wins, int Plays);
    public string? SaveState() => wins > 0 || plays > 0 ? JsonSerializer.Serialize(new Record(wins, plays)) : null;

    public void LoadState(string? json)
    {
        if (json == null) return;
        try
        {
            if (JsonSerializer.Deserialize<Record>(json) is { } r)
                (wins, plays) = (r.Wins, r.Plays);
        }
        catch (JsonException) { }
    }
}
