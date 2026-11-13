using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace Bolok;

// 볼록이 배구 모듈: IGameMode 약속 구현 (앱 붙이기·조작·그리기·기록·디버그 명령)
// 물리·규칙은 VolleySim.cs, 자가 검사·장면·속도 재기는 VolleyCheck.cs
sealed partial class VolleyGame : IGameMode
{
    readonly IGameHost host;
    VolleySim? sim;
    Session? ses;
    int wins, plays;
    internal bool showMe; // 장면 그림에서만: 볼록이 본체도 여기서 그린다

    IntPtr courtWinH = IntPtr.Zero;
    int freshFrames;
    readonly List<Rectangle> dirty = new(64);

    // 이전 프레임 위치 추적 (Dirty 사각형)
    Rectangle lastCourtRect;
    Rectangle lastBallRect;
    Rectangle lastPlayerMarkRect;
    Rectangle lastRivalMarkRect;
    Rectangle lastPipsRect;
    Rectangle lastSpikeRect;
    Rectangle lastDustRect;
    Rectangle lastGhostRect;

    // 공 비트맵 캐시
    Bitmap? ballBmp;
    int ballK = -1;

    // 라이벌 및 본체 스탬프 캐시
    Sprites? artOf;
    int artK;
    readonly Dictionary<string, Stamp[]>[] rivalStamps = { new(), new() };
    public string RivalSpecies { get; internal set; } = "";
    Sprites? rivalSprites;

    Stamp star = null!, crown = null!;
    Stamp meIdleStamp = null!, meHopStamp = null!, meAngryStamp = null!, meHappyStamp = null!, meSadStamp = null!, meDizzyStamp = null!;

    // GDI+ 브러시 및 펜 캐시 (프레임당 메모리 할당 0B)
    static readonly SolidBrush poleBrush = new(Color.FromArgb(120, 80, 50));
    static readonly Pen polePen = new(Color.FromArgb(160, 116, 80), 1);
    static readonly SolidBrush capBrush = new(Color.FromArgb(240, 90, 80));

    static readonly SolidBrush pipMeBrush = new(Color.FromArgb(255, 120, 120));
    static readonly SolidBrush pipRivalBrush = new(Color.FromArgb(120, 190, 255));
    static readonly SolidBrush pipOffBrush = new(Color.FromArgb(40, 52, 96));
    static readonly SolidBrush pipBackingBrush = new(Color.FromArgb(160, 24, 28, 44));
    static readonly SolidBrush whiteBrush = new(Color.White);
    static readonly SolidBrush sparkleBrush = new(Color.FromArgb(255, 240, 130));

    static readonly Pen ghostPen = new(Color.FromArgb(230, 236, 246), 1);
    static readonly Pen speedLinePen = new(Color.FromArgb(255, 236, 120), 2);
    static readonly Pen bodySpeedLinePen = new(Color.FromArgb(150, 170, 220), 2);
    static readonly Pen starRayPen = new(Color.FromArgb(255, 210, 60), 2);
    static readonly SolidBrush starBrush = new(Color.FromArgb(255, 210, 60));
    static readonly SolidBrush dustBrush = new(Color.FromArgb(236, 228, 208));

    static readonly SolidBrush mockGlass = new(Color.FromArgb(246, 248, 252));
    static readonly SolidBrush mockBar = new(Color.FromArgb(60, 104, 214));
    static readonly Pen mockBorderPen = new(Color.FromArgb(40, 44, 60), 1);
    static readonly SolidBrush mockBtnMin = new(Color.FromArgb(250, 200, 70));
    static readonly SolidBrush mockBtnMax = new(Color.FromArgb(110, 200, 110));
    static readonly SolidBrush mockBtnClose = new(Color.FromArgb(240, 90, 80));
    static readonly SolidBrush mockIcon = new(Color.FromArgb(250, 214, 90));

    static readonly Pen dottedFloorPen = new(Color.FromArgb(40, 40, 50), 2)
    {
        DashPattern = new float[] { 3, 2 }
    };

    public VolleyGame(IGameHost host) => this.host = host;

    public string Id => "volley";
    public string? MenuKey => "menu.volley"; // 완성되어 메뉴에 등록
    public bool Running => sim != null;

    public void Start() => Begin(null);

    internal void Begin(Rectangle? targetCourt, bool forceDesktop = false)
    {
        if (Running) return;
        var b = host.Pet;
        var screens = new List<Rectangle>();
        foreach (var s in Screen.AllScreens) screens.Add(s.WorkingArea);
        var field = screens.Count > 0 ? screens[0] : new Rectangle(0, 0, 1920, 1080);

        Rectangle court;
        bool isDesktop;
        courtWinH = IntPtr.Zero;

        if (targetCourt.HasValue)
        {
            court = targetCourt.Value;
            isDesktop = forceDesktop;
            field = screens.FirstOrDefault(s => s.IntersectsWith(court), field);
        }
        else
        {
            int minW = Math.Max(320, b.CellPx * 4);
            int minH = Math.Max(180, (int)(b.CellPx * 2.2));
            var (f, win) = WindowPick.Arena(Native.Foreground(), Native.ListWindows(), b.Area, screens, b.CellPx, minW, minH);
            field = f;

            if (win != null && !forceDesktop)
            {
                court = win.Value.R;
                courtWinH = win.Value.H;
                isDesktop = false;
            }
            else
            {
                court = MakeDesktopCourt(field, b.X, b.Y, b.Scale);
                isDesktop = true;
            }
        }

        var vSim = new VolleySim(court, isDesktop, b.Scale, new Random());
        InitRival(vSim.Rnd);
        Open(vSim, Session.Open(host, field, () => Stop(true)));
        b.StartPuppet();

        Dbg.Log("volley-start", ("window", isDesktop ? "desktop" : courtWinH.ToString()), ("court", court.ToString()), ("scale", b.Scale));
    }

    internal static Rectangle MakeDesktopCourt(Rectangle field, double petX, double petY, int scale)
    {
        int cell = 40 * scale;
        int courtW = Math.Clamp(cell * 5, 420, 680);
        int courtH = Math.Clamp((int)(cell * 2.8), 240, 360);

        int floorY = Math.Clamp((int)petY, field.Top + courtH + 20, field.Bottom - 10);
        int centerX = Math.Clamp((int)petX + courtW / 4, field.Left + courtW / 2 + 10, field.Right - courtW / 2 - 10);

        return new Rectangle(centerX - courtW / 2, floorY - courtH, courtW, courtH);
    }

    internal void Open(VolleySim w, Session s, bool events = true)
    {
        (sim, ses) = (w, s);
        if (string.IsNullOrEmpty(RivalSpecies)) InitRival(w.Rnd);

        var b = host.Pet;
        if (events)
        {
            w.OnEvent = (ev, arg) =>
            {
                if (ev == "serve")
                {
                    Dbg.Log("volley-serve", ("server", arg?.ToString() ?? ""), ("score", $"{w.ScorePlayer}-{w.ScoreRival}"));
                }
                else if (ev == "hit")
                {
                    Snd.Play(Sound.Tap);
                }
                else if (ev == "spike")
                {
                    Snd.Play(Sound.Boom);
                    Dbg.Log("volley-spike", ("who", arg?.ToString() ?? ""));
                }
                else if (ev == "score")
                {
                    Snd.Play(Sound.Tap);
                    Dbg.Log("volley-score", ("who", arg?.ToString() ?? ""), ("score", $"{w.ScorePlayer}-{w.ScoreRival}"));
                }
                else if (ev == "end")
                {
                    bool won = (bool)(arg ?? false);
                    if (won)
                    {
                        b.Emote("star", 2.0);
                        Snd.Play(Sound.Pick);
                    }
                    else
                    {
                        b.Emote("sweat", 2.0);
                        Snd.Play(Sound.Hurt);
                    }
                }
            };
        }

        freshFrames = 0;
        Art(w.K);
        Sync();
        Track();
    }

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

    public void Frame(double dt)
    {
        var (w, s) = (sim!, ses!);
        s.Tick();

        // 윈도우 추적: 움직이면 따라가고, 닫히거나 최소화·축소 시 바탕화면 판으로 전환
        if (courtWinH != IntPtr.Zero)
        {
            var st = Native.Probe(courtWinH);
            int minW = Math.Max(320, w.K * 40 * 4);
            int minH = Math.Max(180, (int)(w.K * 40 * 2.2));

            if (!st.Alive || st.Minimized || st.R.Width < minW || st.R.Height < minH)
            {
                courtWinH = IntPtr.Zero;
                var dc = MakeDesktopCourt(s.Field, w.PlayerX, w.PlayerY, w.K);
                w.SetCourt(dc, true);
                Dbg.Log("volley-window-lost", ("reason", !st.Alive ? "closed" : st.Minimized ? "minimized" : "too_small"));
            }
            else if (st.R != w.Court)
            {
                w.SetCourt(st.R, false);
            }
        }

        // 입력 전달
        int axisX = s.AxisX;
        bool jump = s.Up;
        bool spike = s.Ctrl;

        w.Update(dt, axisX, jump, spike);
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
        var (w, b) = (sim!, host.Pet);
        (b.X, b.Y, b.Dir) = (w.PlayerX, w.PlayerY, 1);
        b.Pose = w.PlayerPose;
    }

    public void Stop(bool quit)
    {
        if (sim is not { } w) return;
        sim = null;
        var b = host.Pet;
        bool won = w.Won;
        if (won) wins++;
        plays++;

        ses?.Close(b, quit, won, "volley", ("result", won ? "win" : "lost"), ("score", $"{w.ScorePlayer}-{w.ScoreRival}"));
        ses = null;

        ballBmp?.Dispose();
        ballBmp = null;
        ballK = -1;

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
        var w = sim!;
        int k = w.K;
        Art(k);
        dirty.Clear();

        if (freshFrames < 2)
        {
            freshFrames++;
            dirty.Add(w.Court);
            lastCourtRect = w.Court;
        }

        if (w.Court != lastCourtRect)
        {
            dirty.Add(lastCourtRect);
            dirty.Add(w.Court);
            lastCourtRect = w.Court;
        }

        // 공 영역
        var ballRect = new Rectangle((int)w.BallX - w.BallR - 4, (int)w.BallY - w.BallR - 4, (w.BallR + 4) * 2, (w.BallR + 4) * 2);
        if (ballRect != lastBallRect)
        {
            dirty.Add(lastBallRect);
            dirty.Add(ballRect);
            lastBallRect = ballRect;
        }

        // 라이벌 펫 영역
        var rivalRect = new Rectangle((int)w.RivalX - 24 * k, (int)w.RivalY - 44 * k, 48 * k, 48 * k);
        if (rivalRect != lastRivalMarkRect)
        {
            dirty.Add(lastRivalMarkRect);
            dirty.Add(rivalRect);
            lastRivalMarkRect = rivalRect;
        }

        // 본체 펫 영역 (장면 모드 등에서 직접 그릴 때)
        if (showMe)
        {
            var playerRect = new Rectangle((int)w.PlayerX - 24 * k, (int)w.PlayerY - 44 * k, 48 * k, 48 * k);
            if (playerRect != lastPlayerMarkRect)
            {
                dirty.Add(lastPlayerMarkRect);
                dirty.Add(playerRect);
                lastPlayerMarkRect = playerRect;
            }
        }

        // 점수 핍 영역
        int pipsY = w.IsDesktop ? w.Court.Top - 12 * k : w.Court.Top + w.TitleH / 2;
        var pipsRect = new Rectangle(w.NetX - 35 * k, pipsY - 10 * k, 70 * k, 20 * k);
        if (pipsRect != lastPipsRect || w.SparkleTimer > 0)
        {
            dirty.Add(lastPipsRect);
            dirty.Add(pipsRect);
            lastPipsRect = pipsRect;
        }

        // 스파이크 연출 영역
        if (w.SpikeEffectActive)
        {
            var spRect = new Rectangle((int)w.SpikeX - 40 * k, (int)w.SpikeY - 40 * k, 80 * k, 80 * k);
            dirty.Add(lastSpikeRect);
            dirty.Add(spRect);
            lastSpikeRect = spRect;
        }
        else if (!lastSpikeRect.IsEmpty)
        {
            dirty.Add(lastSpikeRect);
            lastSpikeRect = Rectangle.Empty;
        }

        // 화면 흔들림 고스트 영역
        if (w.ShakeActive)
        {
            var ghRect = Rectangle.Inflate(w.Court, 4 * k, 4 * k);
            dirty.Add(lastGhostRect);
            dirty.Add(ghRect);
            lastGhostRect = ghRect;
        }
        else if (!lastGhostRect.IsEmpty)
        {
            dirty.Add(lastGhostRect);
            lastGhostRect = Rectangle.Empty;
        }

        // 먼지 연출 영역
        if (w.DustActive)
        {
            var duRect = new Rectangle((int)w.DustX - 30 * k, (int)w.DustY - 15 * k, 60 * k, 20 * k);
            dirty.Add(lastDustRect);
            dirty.Add(duRect);
            lastDustRect = duRect;
        }
        else if (!lastDustRect.IsEmpty)
        {
            dirty.Add(lastDustRect);
            lastDustRect = Rectangle.Empty;
        }

        // 이긴 쪽 머리 위 왕관 및 별 영역
        if (w.Done)
        {
            int winnerX = (int)(w.Won ? w.PlayerX : w.RivalX);
            int winnerY = (int)(w.Won ? w.PlayerY : w.RivalY) - 38 * k;
            dirty.Add(new Rectangle(winnerX - 16 * k, winnerY - 24 * k, 32 * k, 44 * k));
        }
    }

    public void Draw(Overlay o, Graphics g)
    {
        if (sim is not { } w) return;
        int k = w.K;

        // 0. 장면 모드(showMe)에서만: 시합장 창 배경 및 제목줄 (실제 게임에서는 실제 윈도우 창이 그 자리에 있다)
        if (showMe && !w.IsDesktop)
        {
            var cr = o.Client(w.Court);
            g.FillRectangle(mockGlass, cr);
            g.FillRectangle(mockBar, cr.X, cr.Y, cr.Width, w.TitleH);
            g.DrawRectangle(mockBorderPen, cr.X, cr.Y, cr.Width - 1, cr.Height - 1);

            int btnSize = Math.Max(3, 3 * k / 2);
            g.FillRectangle(mockIcon, cr.X + 3 * k, cr.Y + w.TitleH / 2 - btnSize / 2, btnSize, btnSize);

            int bx = cr.Right - 4 * k - 3 * (btnSize + 2 * k);
            int by = cr.Y + w.TitleH / 2 - btnSize / 2;
            g.FillRectangle(mockBtnMin, bx, by, btnSize, btnSize);
            g.FillRectangle(mockBtnMax, bx + btnSize + 2 * k, by, btnSize, btnSize);
            g.FillRectangle(mockBtnClose, bx + (btnSize + 2 * k) * 2, by, btnSize, btnSize);
        }

        // 1. 바탕화면 판이면 점선 바닥 선 그리기
        if (w.IsDesktop)
        {
            var p0 = o.ToClient(w.WallLeft, w.FloorY);
            var p1 = o.ToClient(w.WallRight, w.FloorY);
            g.DrawLine(dottedFloorPen, p0.X, p0.Y, p1.X, p1.Y);
        }

        // 2. 강스파이크 흔들림: 어긋난 고스트 테두리 (va5 참조)
        if (w.ShakeActive)
        {
            var cr = o.Client(w.Court);
            g.DrawRectangle(ghostPen, cr.X + 2, cr.Y - 2, cr.Width, cr.Height);
        }

        // 3. 네트 (가는 기둥 + 상단 빨간 캡, va1 참조)
        var netBase = o.ToClient(w.NetX, w.FloorY);
        var netTop = o.ToClient(w.NetX, w.NetTopY);
        int netW = w.NetW;
        g.FillRectangle(poleBrush, netBase.X - netW / 2, netTop.Y, netW, netBase.Y - netTop.Y);
        g.DrawLine(polePen, netBase.X, netTop.Y, netBase.X, netBase.Y);

        int capW = Math.Max(4, w.CapR * 2);
        int capH = Math.Max(2, 2 * k);
        g.FillRectangle(capBrush, netBase.X - capW / 2, netTop.Y - capH / 2, capW, capH);

        // 4. 점수 점 (Pips): 제목줄 가운데 (va2/va6 참조)
        DrawScorePips(o, g, w);

        // 5. 득점 순간 바닥 먼지 연출 (va6 참조)
        if (w.DustActive)
        {
            DrawDust(o, g, w);
        }

        // 6. 강스파이크 연출 (속도선·별, va5 참조)
        if (w.SpikeEffectActive)
        {
            DrawSpikeEffect(o, g, w);
        }

        // 7. 배구공
        DrawBall(o, g, w);

        // 8. 라이벌 펫
        DrawRival(o, g, w);

        // 9. 본체 펫 (Scene 모드 등에서만 직접 그린다)
        if (showMe)
        {
            DrawMe(o, g, w);
        }

        // 10. 경기 종료 시 이긴 쪽 머리 위에 왕관(game/crown)과 별(game/star), 진 쪽은 dizzy (모래성 스타일)
        if (w.Done)
        {
            int winnerX = (int)(w.Won ? w.PlayerX : w.RivalX);
            int winnerY = (int)(w.Won ? w.PlayerY : w.RivalY) - 38 * k;
            crown.Put(o, g, winnerX, winnerY - 14 * k);
            star.Put(o, g, winnerX, winnerY);
        }
    }

    void DrawScorePips(Overlay o, Graphics g, VolleySim w)
    {
        int k = w.K;
        int pipsY = w.IsDesktop ? w.Court.Top - 12 * k : w.Court.Top + w.TitleH / 2;
        var centerPt = o.ToClient(w.NetX, pipsY);

        int pipSize = Math.Max(3, 3 * k / 2);
        int gap = pipSize + Math.Max(2, 2 * k / 2);

        // 바탕화면 판이면 점수 점 배경 캡슐
        if (w.IsDesktop)
        {
            int totalW = gap * (VolleySim.TargetScore * 2 + 1);
            g.FillRectangle(pipBackingBrush, centerPt.X - totalW / 2 - 2 * k, centerPt.Y - pipSize / 2 - 2 * k, totalW + 4 * k, pipSize + 4 * k);
        }

        // 중앙 콜론/분리점
        g.FillRectangle(whiteBrush, centerPt.X - 1, centerPt.Y - 1, 2, 2);

        // 왼쪽: 플레이어 점수 (5개)
        int leftBaseX = centerPt.X - gap * VolleySim.TargetScore - pipSize;
        for (int i = 0; i < VolleySim.TargetScore; i++)
        {
            int px = leftBaseX + i * gap;
            int py = centerPt.Y - pipSize / 2;
            bool on = i < w.ScorePlayer;
            g.FillRectangle(on ? pipMeBrush : pipOffBrush, px, py, pipSize, pipSize);
            if (on) g.FillRectangle(whiteBrush, px + 1, py + 1, 1, 1);

            // 새로 켜진 점 빛 반짝임 연출 (va6)
            if (w.NewlyLitTeam == 0 && w.NewlyLitIndex == i && w.SparkleTimer > 0)
            {
                DrawPipSparkle(g, px, py, pipSize);
            }
        }

        // 오른쪽: 라이벌 점수 (5개)
        int rightBaseX = centerPt.X + gap;
        for (int i = 0; i < VolleySim.TargetScore; i++)
        {
            int px = rightBaseX + i * gap;
            int py = centerPt.Y - pipSize / 2;
            bool on = i < w.ScoreRival;
            g.FillRectangle(on ? pipRivalBrush : pipOffBrush, px, py, pipSize, pipSize);
            if (on) g.FillRectangle(whiteBrush, px + 1, py + 1, 1, 1);

            if (w.NewlyLitTeam == 1 && w.NewlyLitIndex == i && w.SparkleTimer > 0)
            {
                DrawPipSparkle(g, px, py, pipSize);
            }
        }
    }

    static void DrawPipSparkle(Graphics g, int px, int py, int s)
    {
        g.FillRectangle(sparkleBrush, px - 2, py - 2, 2, 2);
        g.FillRectangle(sparkleBrush, px + s, py - 2, 2, 2);
        g.FillRectangle(sparkleBrush, px - 2, py + s, 2, 2);
        g.FillRectangle(sparkleBrush, px + s, py + s, 2, 2);
    }

    void DrawDust(Overlay o, Graphics g, VolleySim w)
    {
        var cp = o.ToClient(w.DustX, w.DustY);
        int k = w.K;

        g.FillEllipse(dustBrush, cp.X - 8 * k, cp.Y - 2 * k, 16 * k, 4 * k);
        g.FillEllipse(dustBrush, cp.X - 16 * k, cp.Y - 1 * k, 8 * k, 3 * k);
        g.FillEllipse(dustBrush, cp.X + 9 * k, cp.Y - 1 * k, 8 * k, 3 * k);
    }

    void DrawSpikeEffect(Overlay o, Graphics g, VolleySim w)
    {
        var sp = o.ToClient(w.SpikeX, w.SpikeY);
        int k = w.K;
        int dir = w.SpikeDir;

        // 1. 도약/스파이크 몸통 뒤 모션 선 3가닥 (va5 참조)
        int spikerX = (int)(dir > 0 ? w.PlayerX : w.RivalX);
        int spikerY = (int)(dir > 0 ? w.PlayerY : w.RivalY) - 16 * k;
        var bp = o.ToClient(spikerX, spikerY);
        for (int i = 0; i < 3; i++)
        {
            g.DrawLine(bodySpeedLinePen,
                bp.X - dir * (6 + i * 3) * k, bp.Y - 4 * k + i * 5 * k,
                bp.X - dir * (14 + i * 3) * k, bp.Y - 6 * k + i * 5 * k);
        }

        // 2. 속도선 (공 뒤로 3가닥, va5 참조)
        int len = 18 * k;
        g.DrawLine(speedLinePen, sp.X - dir * 4 * k, sp.Y - 4 * k, sp.X - dir * len, sp.Y - 10 * k);
        g.DrawLine(speedLinePen, sp.X - dir * 4 * k, sp.Y, sp.X - dir * len, sp.Y - 4 * k);
        g.DrawLine(speedLinePen, sp.X - dir * 4 * k, sp.Y + 4 * k, sp.X - dir * len, sp.Y + 2 * k);

        // 3. 스파이크 별 (8방 빛줄기, va5 참조)
        int starR = 8 * k;
        g.FillEllipse(starBrush, sp.X - starR / 2, sp.Y - starR / 2, starR, starR);
        for (int a = 0; a < 360; a += 45)
        {
            double rad = a * Math.PI / 180.0;
            int x0 = (int)(sp.X + (starR + 2) * Math.Cos(rad));
            int y0 = (int)(sp.Y + (starR + 2) * Math.Sin(rad));
            int x1 = (int)(sp.X + (starR + 7 * k) * Math.Cos(rad));
            int y1 = (int)(sp.Y + (starR + 7 * k) * Math.Sin(rad));
            g.DrawLine(starRayPen, x0, y0, x1, y1);
        }
    }

    void DrawBall(Overlay o, Graphics g, VolleySim w)
    {
        var cp = o.ToClient(w.BallX, w.BallY);
        int r = w.BallR;
        EnsureBallBitmap(w.K);
        if (ballBmp != null)
        {
            g.DrawImageUnscaled(ballBmp, cp.X - r - 1, cp.Y - r - 1);
        }
    }

    void EnsureBallBitmap(int k)
    {
        if (ballBmp != null && ballK == k) return;
        ballBmp?.Dispose();
        ballK = k;

        int r = 4 * k;
        int d = (r + 1) * 2;
        ballBmp = new Bitmap(d, d, PixelFormat.Format32bppPArgb);

        int cx = r + 1;
        int cy = r + 1;
        double scaleRatio = k / 3.0;

        for (int y = 0; y < d; y++)
        {
            for (int x = 0; x < d; x++)
            {
                double dx = x + 0.5 - cx;
                double dy = y + 0.5 - cy;
                double distSq = dx * dx + dy * dy;

                if (distSq < r * r)
                {
                    Color c;
                    if (distSq >= (r - 1.2 * scaleRatio) * (r - 1.2 * scaleRatio))
                    {
                        c = Color.FromArgb(90, 90, 110); // 테두리
                    }
                    else if (Math.Abs(dx - dy * 0.5) < 1.2 * scaleRatio)
                    {
                        c = Color.FromArgb(240, 190, 60); // 노란 이음매
                    }
                    else if (Math.Abs(dy + dx * 0.3 + r * 0.35) < 1.2 * scaleRatio)
                    {
                        c = Color.FromArgb(80, 130, 210); // 파란 줄무늬
                    }
                    else if (Math.Abs(dx - (-r * 0.4)) < 1.0 * scaleRatio && Math.Abs(dy - (-r * 0.4)) < 1.0 * scaleRatio)
                    {
                        c = Color.White; // 하이라이트 점
                    }
                    else
                    {
                        c = Color.White; // 기본 흰색
                    }

                    ballBmp.SetPixel(x, y, c);
                }
            }
        }
    }

    void DrawRival(Overlay o, Graphics g, VolleySim w)
    {
        int k = w.K;
        var st = RivalStamp(w);
        st.Put(o, g, (int)w.RivalX, (int)w.RivalY - 20 * k);
    }

    Stamp RivalStamp(VolleySim w)
    {
        string pose = w.RivalPose;
        var rows = rivalStamps[1]; // 왼쪽 보기 (라이벌은 오른쪽에서 왼쪽을 바라봄)
        if (!rows.TryGetValue(pose, out var row))
        {
            var sp = rivalSprites ?? artOf!;
            string form = (rivalSprites != null) ? host.Pet.P.Stage : "adult_grumpy";
            rows[pose] = row = Stamps.Of(sp).Row($"{form}/1/{pose}", w.K, true);
        }
        return row[(int)(w.Clock * 4) % row.Length];
    }

    void DrawMe(Overlay o, Graphics g, VolleySim w)
    {
        var stamp = (w.PlayerPose == "happy") ? meHappyStamp :
                    (w.PlayerPose == "dizzy") ? meDizzyStamp :
                    (w.PlayerPose == "sad") ? meSadStamp :
                    (w.PlayerPose == "angry") ? meAngryStamp :
                    (w.PlayerPose == "hop") ? meHopStamp : meIdleStamp;

        stamp?.Put(o, g, (int)w.PlayerX, (int)w.PlayerY - 20 * w.K);
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
        meHopStamp = st.Get($"{b.P.Stage}/1/hop", 0, k, false);
        meAngryStamp = st.Get($"{b.P.Stage}/1/angry", 0, k, false);
        meHappyStamp = st.Get($"{b.P.Stage}/1/happy", 0, k, false);
        meSadStamp = st.Get($"{b.P.Stage}/1/sad", 0, k, false);
        meDizzyStamp = st.Get($"{b.P.Stage}/1/dizzy", 0, k, false);

        if (!string.IsNullOrEmpty(RivalSpecies)) rivalSprites = host.SpritesOf(RivalSpecies);
        rivalStamps[0].Clear();
        rivalStamps[1].Clear();
    }

    // ── 디버그 명령 ──
    // volley / volley serve / volley spike / volley end win|lose / volley stop
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != Id) return false;

        if (a.Length > 1 && a[1] == "stop")
        {
            Stop(true);
            return true;
        }

        if (a.Length > 1 && a[1] == "serve" && sim is { } wServe)
        {
            if (wServe.Stage == VolleySim.Phase.Serving || wServe.Stage == VolleySim.Phase.PointScored)
            {
                wServe.ResetPositionsForServe(wServe.CurrentServer);
            }
            return true;
        }

        if (a.Length > 1 && a[1] == "spike" && sim is { } wSpike)
        {
            // 플레이어가 바로 공중 강스파이크 실행
            wSpike.PlayerOnGround = false;
            wSpike.PlayerY = wSpike.FloorY - 40 * wSpike.K;
            wSpike.PlayerVy = -100;
            wSpike.BallX = wSpike.PlayerX + 10 * wSpike.K;
            wSpike.BallY = wSpike.PlayerY - wSpike.PetR;
            wSpike.TryExecuteSpike(VolleySim.Actor.Player);
            return true;
        }

        if (a.Length > 2 && a[1] == "end" && sim is { } wEnd)
        {
            if (a[2] == "win") wEnd.ForceEnd(true);
            else if (a[2] == "lose") wEnd.ForceEnd(false);
            return true;
        }

        if (!Running)
        {
            Start();
            return true;
        }

        return true;
    }

    public IEnumerable<GameStat> Stats()
    {
        yield return new GameStat(
            L.T("game.volley"),
            wins > 0 ? string.Format(L.T("game.volley_val"), wins) : L.T("game.volley_none"),
            wins > 0);
    }

    public string? SaveState() => JsonSerializer.Serialize(new { Wins = wins, Plays = plays });

    public void LoadState(string? json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Wins", out var w)) wins = w.GetInt32();
            if (doc.RootElement.TryGetProperty("Plays", out var p)) plays = p.GetInt32();
        }
        catch { }
    }

    // 자가 검사·장면·속도 재기는 VolleyCheck.cs 로 위임
    public static string Check() => VolleyCheck.Check();
    public static void Scene(string path, int scale) => VolleyCheck.Scene(path, scale);
    public static string BenchRun() => VolleyCheck.BenchRun();
}
