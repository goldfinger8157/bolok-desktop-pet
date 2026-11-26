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
    Rectangle lastBirdRect;
    Rectangle lastPlayerMarkRect;
    Rectangle lastRivalMarkRect;
    int lastRivalStep = -1;
    string lastRivalPose = "", lastPlayerPose = "";
    Rectangle lastPipsRect;
    Rectangle lastSpikeRect;
    Rectangle lastDustRect;
    Rectangle lastGhostRect;
    Rectangle lastStoneRect;
    Rectangle lastFlashRect;
    readonly Rectangle[] lastTripRect = new Rectangle[2];
    readonly Rectangle[] lastSlideRect = new Rectangle[2];
    Rectangle lastDigRect;

    // 공 비트맵 캐시
    Bitmap[]? ballFrames;                 // 돌아가는 공 그림 (BallTurns 장, 0 번 = 꼭지가 위)
    (VolleySim.BallKind Kind, int K) ballFor = (VolleySim.BallKind.Volleyball, -1);
    const int BallTurns = 32;
    double rivalBangUntil = -1;           // 라이벌 머리 위 느낌표 (꼭지 실수)

    // 라이벌 및 본체 스탬프 캐시
    Sprites? artOf;
    int artK;
    readonly Dictionary<string, Stamp[]>[] rivalStamps = { new(), new() };
    public string RivalSpecies { get; internal set; } = "";
    Sprites? rivalSprites;

    Stamp star = null!, crown = null!, bang = null!;
    Stamp meIdleStamp = null!, meHopStamp = null!, meAngryStamp = null!, meHappyStamp = null!, meSadStamp = null!, meDizzyStamp = null!;
    Stamp meBlockStamp = null!, meSlideStamp = null!, meShockStamp = null!, meWalkStamp = null!; // 장면(showMe)에서만 쓴다
    Stamp[] meDiveRight = null!, meDiveLeft = null!;                                              // dive 두 장 (던지는 중 · 누운 모습), 오른쪽·왼쪽 보기

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
    // 슬라이드 먼지 꼬리: 흩어지는 정도 네 단계의 붓·테두리 (프레임마다 새로 만들지 않는다). 테두리를 짙게 해서 흰 창 위에서도 읽힌다
    static readonly SolidBrush[] trailFill = { new(Color.FromArgb(70, 238, 230, 210)), new(Color.FromArgb(130, 238, 230, 210)), new(Color.FromArgb(200, 238, 230, 210)), new(Color.FromArgb(255, 238, 230, 210)) };
    static readonly Pen[] trailRim = { new(Color.FromArgb(70, 170, 156, 128), 1), new(Color.FromArgb(130, 170, 156, 128), 1), new(Color.FromArgb(200, 170, 156, 128), 1), new(Color.FromArgb(255, 170, 156, 128), 1) };
    static readonly SolidBrush[] trailSpeck = { new(Color.FromArgb(70, 196, 182, 152)), new(Color.FromArgb(130, 196, 182, 152)), new(Color.FromArgb(200, 196, 182, 152)), new(Color.FromArgb(255, 196, 182, 152)) };
    static readonly SolidBrush digHalo = new(Color.FromArgb(95, 255, 226, 120)), digCore = new(Color.FromArgb(255, 255, 248, 200));

    static readonly SolidBrush mockGlass = new(Color.FromArgb(246, 248, 252));
    static readonly SolidBrush mockBar = new(Color.FromArgb(60, 104, 214));
    static readonly Pen mockBorderPen = new(Color.FromArgb(40, 44, 60), 1);
    static readonly SolidBrush mockBtnMin = new(Color.FromArgb(250, 200, 70));
    static readonly SolidBrush mockBtnMax = new(Color.FromArgb(110, 200, 110));
    static readonly SolidBrush mockBtnClose = new(Color.FromArgb(240, 90, 80));
    static readonly SolidBrush mockIcon = new(Color.FromArgb(250, 214, 90));

    // 바탕화면 판 (쓸 창이 없을 때): 얇은 테두리 상자(양쪽 벽·천장) + 잔디 띠 바닥 (YELL 2026-11-16: 「얇은 테두리 상자에 바닥은 나 안으로」)
    static readonly Pen boxPen = new(Color.FromArgb(96, 104, 118), 2);
    // 잔디는 회색이 섞인 차분한 세이지 초록·얇게 (YELL 2026-11-16: 일할 때 하는 게임이라 눈에 덜 띄게 — 시안 3번)
    static readonly SolidBrush grassTop = new(Color.FromArgb(146, 162, 136)), grassMid = new(Color.FromArgb(122, 140, 112)), grassDeep = new(Color.FromArgb(104, 120, 96));
    internal const int GrassCells = 2; // 바닥 잔디 띠 두께 (칸, 바닥선 아래로)

    // 그림이 닿는 자리: 바탕화면 판은 테두리·잔디 띠까지 (칠할 자리에 넣어야 남거나 안 그려지지 않는다)
    // 점수판 자리: 경기장 바닥 아래, 네트 밑 (YELL 2026-11-16: 「점수판을 경기장 바닥 아래로」).
    // 창이면 창 아래 가장자리 밑, 바탕화면 판이면 잔디 띠 밑. 화면 아래에 자리가 없으면 바닥 안쪽 네트 아래로
    internal static int PipsY(VolleySim w, Rectangle field)
    {
        int below = w.Court.Bottom + (w.IsDesktop ? GrassCells * w.K : 0) + 7 * w.K;
        return below + 6 * w.K <= field.Bottom || field.IsEmpty ? below : w.FloorY - 6 * w.K;
    }
    int PipsY(VolleySim w) => PipsY(w, ses?.Field ?? Rectangle.Empty);

    internal static Rectangle PaintCourt(VolleySim w) =>
        w.IsDesktop ? Rectangle.FromLTRB(w.Court.Left - 3, w.Court.Top - 3, w.Court.Right + 3, w.Court.Bottom + GrassCells * w.K + 3) : w.Court;

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

        var vSim = new VolleySim(court, isDesktop, b.Scale, new Random()) { Ball = VolleySim.PickBall(Random.Shared), Birds = true }; // 과일 공 (가끔 배구공), 가끔 새
        InitRival(vSim.Rnd);
        Open(vSim, Session.Open(host, field, () => Stop(true)));
        b.StartPuppet();

        Dbg.Log("volley-start", ("window", isDesktop ? "desktop" : courtWinH.ToString()), ("court", court.ToString()), ("scale", b.Scale), ("ball", vSim.Ball.ToString()));
    }

    internal static Rectangle MakeDesktopCourt(Rectangle field, double petX, double petY, int scale)
    {
        int cell = 40 * scale;
        int courtW = Math.Clamp(cell * 5, 420, 680);
        int courtH = Math.Clamp((int)(cell * 2.8), 240, 360);

        int floorY = Math.Clamp((int)petY, field.Top + courtH + 20, field.Bottom - 10 - (GrassCells + 13) * scale); // 아래 잔디 띠와 그 밑 점수판 자리
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
                else if (ev == "fumble") // 꼭지에 헤딩: 느낌표 + 움찔 (자세는 규칙이 shock 으로)
                {
                    Snd.Play(Sound.Tap);
                    if (arg is VolleySim.Actor.Player) b.Emote("bang", 1.0);
                    else rivalBangUntil = w.Clock + 1.0;
                    Dbg.Log("volley-fumble", ("who", arg?.ToString() ?? ""), ("ball", w.Ball.ToString()));
                }
                else if (ev == "stone") // 돌부리가 솟음 (미리 보이는 예고)
                {
                    Snd.Play(Sound.Tap);
                    Dbg.Log("volley-stone", ("who", arg?.ToString() ?? ""), ("x", (int)w.StoneX));
                }
                else if (ev == "trip") // 돌에 걸려 넘어짐 (자세는 규칙이 slide 로)
                {
                    Snd.Play(arg is VolleySim.Actor.Player ? Sound.Hurt : Sound.Tap);
                    Dbg.Log("volley-trip", ("who", arg?.ToString() ?? ""));
                }
                else if (ev == "slide") // 몸을 던짐 (자세는 규칙이 dive 로): 넘어짐과 같은 소리
                {
                    Snd.Play(arg is VolleySim.Actor.Player ? Sound.Hurt : Sound.Tap);
                    Dbg.Log("volley-slide", ("who", arg?.ToString() ?? ""), ("x", (int)(arg is VolleySim.Actor.Player ? w.PlayerX : w.RivalX)));
                }
                else if (ev == "dig") // 누워서 받아 올림: hit 소리 + 빛
                {
                    Snd.Play(Sound.Tap);
                    Dbg.Log("volley-dig", ("who", arg?.ToString() ?? ""));
                }
                else if (ev == "block") // 맞은 블로킹: 번쩍
                {
                    Snd.Play(Sound.Tap);
                    Dbg.Log("volley-block", ("who", arg?.ToString() ?? ""));
                }
                else if (ev == "blockmiss") // 어긋난 블로킹
                {
                    Snd.Play(Sound.Tap);
                    Dbg.Log("volley-blockmiss", ("who", arg?.ToString() ?? ""));
                }
                else if (ev == "bird") // 공이 새에 맞음
                {
                    Snd.Play(Sound.Tap);
                    Dbg.Log("volley-bird", ("x", (int)w.BirdX), ("y", (int)w.BirdY));
                }
                else if (ev == "spike")
                {
                    Snd.Play(Sound.Boom);
                    Dbg.Log("volley-spike", ("who", arg?.ToString() ?? ""));
                }
                else if (ev == "score")
                {
                    Snd.Play(Sound.Tap);
                    Dbg.Log("volley-score", ("who", arg?.ToString() ?? ""), ("score", $"{w.ScorePlayer}-{w.ScoreRival}"), ("kind", w.LastPoint.ToString()));
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
        StoneBitmap(0, w.K); // 돌부리 그림 네 장을 미리 (판 도중에 만들지 않게)
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
        bool spike = !w.T.AutoSpike && s.Ctrl; // 자동 스파이크면 Ctrl 은 읽지 않는다

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
        bool dived = w.IsSliding(VolleySim.Actor.Player) || w.IsLying(VolleySim.Actor.Player);
        (b.X, b.Y, b.Dir) = (w.PlayerX, w.PlayerY, dived ? w.SlideDirOf(VolleySim.Actor.Player) : 1); // 던지는 동안만 던진 쪽을 본다
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

        DisposeBall();
        DisposeStone();

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
            dirty.Add(PaintCourt(w));
            lastCourtRect = PaintCourt(w);
        }

        if (PaintCourt(w) != lastCourtRect)
        {
            dirty.Add(lastCourtRect);
            dirty.Add(PaintCourt(w));
            lastCourtRect = PaintCourt(w);
        }

        // 공 영역
        int br = (int)(w.BallR * 1.45) + 4; // 돌아간 공 그림 (꼭지·잎이 모서리에 있어도)
        var ballRect = new Rectangle((int)w.BallX - br, (int)w.BallY - br, br * 2, br * 2);
        if (ballRect != lastBallRect)
        {
            dirty.Add(lastBallRect);
            dirty.Add(ballRect);
            lastBallRect = ballRect;
        }

        // 새 영역 (날갯짓·빙글 — 매 프레임)
        var birdRect = w.BirdOn ? new Rectangle((int)w.BirdX - 12 * k, (int)w.BirdY - 12 * k, 24 * k, 24 * k) : Rectangle.Empty;
        if (birdRect != lastBirdRect)
        {
            if (!lastBirdRect.IsEmpty) dirty.Add(lastBirdRect);
            if (!birdRect.IsEmpty) dirty.Add(birdRect);
            lastBirdRect = birdRect;
        }

        // 라이벌 펫 영역
        var rivalRect = new Rectangle((int)w.RivalX - 24 * k, (int)w.RivalY - 44 * k, 48 * k, 48 * k);
        int rivalStep = (int)(w.Clock * 4); // 라이벌 그림이 0.25초마다 바뀐다 (제자리에 있어도 다시 칠한다)
        if (rivalRect != lastRivalMarkRect || rivalStep != lastRivalStep || w.RivalPose != lastRivalPose)
        {
            dirty.Add(lastRivalMarkRect);
            dirty.Add(rivalRect);
            lastRivalMarkRect = rivalRect;
            (lastRivalStep, lastRivalPose) = (rivalStep, w.RivalPose);
        }

        // 본체 펫 영역 (장면 모드 등에서 직접 그릴 때)
        if (showMe)
        {
            var playerRect = new Rectangle((int)w.PlayerX - 24 * k, (int)w.PlayerY - 44 * k, 48 * k, 48 * k);
            if (playerRect != lastPlayerMarkRect || w.PlayerPose != lastPlayerPose)
            {
                dirty.Add(lastPlayerMarkRect);
                dirty.Add(playerRect);
                lastPlayerMarkRect = playerRect;
                lastPlayerPose = w.PlayerPose;
            }
        }

        // 점수 핍 영역
        int pipsY = PipsY(w);
        var pipsRect = new Rectangle(w.NetX - 35 * k, pipsY - 10 * k, 70 * k, 20 * k);
        if (pipsRect != lastPipsRect || w.SparkleTimer > 0 || freshFrames <= 2) // 처음 두 프레임은 점수판도 (칠할 자리 맨 처음)
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

        // 돌부리 (솟고 가라앉는 동안) · 넘어짐 먼지 · 블로킹 번쩍
        var stoneRect = w.StoneFrame >= 0 ? new Rectangle((int)w.StoneX - 9 * k, w.FloorY - 8 * k, 18 * k, 10 * k) : Rectangle.Empty;
        if (!stoneRect.IsEmpty) dirty.Add(stoneRect);                                  // 솟고 가라앉는 동안 장이 바뀐다
        if (stoneRect != lastStoneRect && !lastStoneRect.IsEmpty) dirty.Add(lastStoneRect);
        lastStoneRect = stoneRect;
        for (int i = 0; i < 2; i++)
        {
            var who = (VolleySim.Actor)i;
            var trRect = w.TripDustLeft(who) > 0 ? new Rectangle((int)w.TripDustX(who) - 24 * k, w.FloorY - 12 * k, 48 * k, 14 * k) : Rectangle.Empty;
            if (trRect == lastTripRect[i] && trRect.IsEmpty) continue;
            if (!lastTripRect[i].IsEmpty) dirty.Add(lastTripRect[i]);
            if (!trRect.IsEmpty) dirty.Add(trRect);
            lastTripRect[i] = trRect;
        }
        for (int i = 0; i < 2; i++) // 슬라이드 먼지 꼬리 (던진 자리에서 멈춘 자리까지, 흩어지는 동안 매 프레임)
        {
            var who = (VolleySim.Actor)i;
            Rectangle slRect = Rectangle.Empty;
            if (w.SlideTrailLeft(who) > 0)
            {
                var (x0, x1) = w.SlideTrail(who);
                slRect = Rectangle.FromLTRB((int)Math.Min(x0, x1) - 22 * k, w.FloorY - 16 * k, (int)Math.Max(x0, x1) + 22 * k, w.FloorY + 4 * k);
            }
            if (slRect.IsEmpty && lastSlideRect[i].IsEmpty) continue;
            if (!lastSlideRect[i].IsEmpty) dirty.Add(lastSlideRect[i]);
            if (!slRect.IsEmpty) dirty.Add(slRect);
            lastSlideRect[i] = slRect;
        }
        var digRect = w.DigFlashActive ? DigRect(w) : Rectangle.Empty;                 // 디그 빛 (0.2초, 매 프레임 자란다)
        if (!digRect.IsEmpty) dirty.Add(digRect);
        if (digRect != lastDigRect && !lastDigRect.IsEmpty) dirty.Add(lastDigRect);
        lastDigRect = digRect;
        var flashRect = w.BlockFlashActive ? FlashRect(w) : Rectangle.Empty;
        if (!flashRect.IsEmpty) dirty.Add(flashRect);                                  // 빛줄기가 매 프레임 자란다
        if (flashRect != lastFlashRect && !lastFlashRect.IsEmpty) dirty.Add(lastFlashRect);
        lastFlashRect = flashRect;

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

        // 1. 바탕화면 판이면 경기장: 얇은 테두리 상자(양쪽 벽·천장, 공이 튕기는 자리 그대로) + 바닥 잔디 띠
        if (w.IsDesktop) DrawDesktopCourt(o, g, w);

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

        // 5-1. 돌부리 (네트 뒤·공 앞) · 넘어짐 먼지 · 블로킹 번쩍
        if (w.StoneFrame >= 0) DrawStone(o, g, w);
        for (int i = 0; i < 2; i++)
            if (w.TripDustLeft((VolleySim.Actor)i) > 0) DrawTripDust(o, g, w, (VolleySim.Actor)i);
        for (int i = 0; i < 2; i++)
            if (w.SlideTrailLeft((VolleySim.Actor)i) > 0) DrawSlideTrail(o, g, w, (VolleySim.Actor)i);
        if (w.BlockFlashActive) DrawBlockFlash(o, g, w);
        if (w.DigFlashActive) DrawDigFlash(o, g, w);

        // 6. 강스파이크 연출 (속도선·별, va5 참조)
        if (w.SpikeEffectActive)
        {
            DrawSpikeEffect(o, g, w);
        }

        // 7. 배구공
        if (w.BirdOn) DrawBird(o, g, w);
        DrawBall(o, g, w);

        // 8. 라이벌 펫
        DrawRival(o, g, w);

        // 9. 본체 펫 (Scene 모드 등에서만 직접 그린다)
        if (showMe)
        {
            DrawMe(o, g, w);
        }

        if (w.Clock < rivalBangUntil) bang.Put(o, g, (int)w.RivalX + 6 * k, (int)w.RivalY - 36 * k); // 라이벌 꼭지 실수

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
        int pipsY = PipsY(w);
        var centerPt = o.ToClient(w.NetX, pipsY);

        int pipSize = Math.Max(3, 3 * k / 2);
        int gap = pipSize + Math.Max(2, 2 * k / 2);

        // 점수 점 배경 캡슐 (창 내용 위에서도 읽히게 늘)
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

    // 돌부리: 4장 중 지금 장 (솟을 때 아래부터, 가라앉을 때 거꾸로). 바닥선 위에 앉는다
    Bitmap[]? stoneBmps;
    int stoneK = -1;
    internal Bitmap StoneBitmap(int frame, int k)
    {
        if (stoneBmps == null || stoneK != k)
        {
            DisposeStone();
            stoneK = k;
            stoneBmps = new Bitmap[4];
            for (int i = 0; i < 4; i++) stoneBmps[i] = MakeStone(i, k);
        }
        return stoneBmps[frame];
    }

    void DisposeStone()
    {
        if (stoneBmps != null) foreach (var b in stoneBmps) b.Dispose();
        stoneBmps = null;
        stoneK = -1;
    }

    internal static Bitmap MakeStone(int frame, int k)
    {
        var rows = VolleyFruits.Stone;
        int m = VolleyFruits.StoneMargin, n = VolleyFruits.StoneRows(frame);
        int cols = rows[0].Length + m * 2;
        var bmp = new Bitmap(cols * k, rows.Length * k, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        for (int y = rows.Length - n; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                if (!VolleyFruits.StonePal.TryGetValue(rows[y][x], out var col)) continue;
                using var br = new SolidBrush(ColorTranslator.FromHtml(col));
                g.FillRectangle(br, (x + m) * k, y * k, k, k);
            }
        if (frame <= 1) // 솟는 처음 두 장: 양옆에 흙 알갱이
        {
            using var dirt = new SolidBrush(ColorTranslator.FromHtml(VolleyFruits.StonePal['t']));
            int bottom = rows.Length - 1;
            g.FillRectangle(dirt, 1 * k, bottom * k, k, k);
            g.FillRectangle(dirt, (cols - 2) * k, bottom * k, k, k);
            if (frame == 1)
            {
                g.FillRectangle(dirt, 0, (bottom - 1) * k, k, k);
                g.FillRectangle(dirt, (cols - 1) * k, (bottom - 1) * k, k, k);
            }
        }
        return bmp;
    }

    void DrawStone(Overlay o, Graphics g, VolleySim w)
    {
        int frame = w.StoneFrame;
        var bmp = StoneBitmap(frame, w.K);
        var cp = o.ToClient(w.StoneX, w.FloorY);
        g.DrawImageUnscaled(bmp, cp.X - bmp.Width / 2, cp.Y + 1 - bmp.Height);
    }

    // 넘어짐 먼지: 득점 먼지(DrawDust)와 같은 타원 셋을 작게, 0.4초 동안 옆으로 퍼지며 (겹칠 수 있어 따로)
    void DrawTripDust(Overlay o, Graphics g, VolleySim w, VolleySim.Actor who)
    {
        int k = w.K;
        double p = 1 - w.TripDustLeft(who) / VolleySim.TripDustSec; // 0 → 1
        int spread = (int)((5 + 12 * p) * k), small = Math.Max(2, (int)((4 - 2 * p) * k));
        var cp = o.ToClient(w.TripDustX(who), w.FloorY);
        int lift = (int)(3 * p * k);
        g.FillEllipse(dustBrush, cp.X - 4 * k, cp.Y - 2 * k - lift / 2, 8 * k, 3 * k);
        g.FillEllipse(dustBrush, cp.X - spread - small, cp.Y - 2 * k - lift, small * 2, small + k);
        g.FillEllipse(dustBrush, cp.X + spread - small, cp.Y - 2 * k - lift, small * 2, small + k);
    }

    // 슬라이드 먼지 꼬리: 던진 자리(x0)에서 지금·멈춘 자리(x1)까지 몸 뒤로 길게 깔리고, 0.35초 동안 흩어지며 옅어진다
    void DrawSlideTrail(Overlay o, Graphics g, VolleySim w, VolleySim.Actor who)
    {
        int k = w.K;
        var (x0, x1) = w.SlideTrail(who);
        double fade = Math.Clamp(w.SlideTrailLeft(who) / VolleySim.SlideTrailSec, 0, 1), spread = 1 - fade;
        int lv = Math.Clamp((int)(fade * 4), 0, 3);
        int dir = Math.Sign(x1 - x0);
        double len = Math.Abs(x1 - x0);
        if (dir == 0 || len < k) return;
        var floor = o.ToClient(Math.Min(x0, x1), w.FloorY);
        var fb = trailFill[lv];
        g.FillEllipse(fb, (float)(floor.X + len * .06), floor.Y - 1.4f * k, (float)(len * .9), 2.4f * k);       // 바닥에 깔린 긴 줄
        int n = Math.Max(3, (int)(len / (9 * k)));
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)(n - 1);                                    // 0 = 던진 자리(오래됨) → 1 = 몸 바로 뒤
            double px = dir > 0 ? floor.X + len * (.04 + .80 * t) : floor.X + len * (.96 - .80 * t);
            double w2 = (8.5 - 3 * t + 3 * spread) * k, h = (4 - 1.2 * t + 1.5 * spread) * k, lift = ((1 - t) * 3.5 + spread * 3) * k;
            var r = new RectangleF((float)(px - w2 / 2), (float)(floor.Y + k - h - lift), (float)w2, (float)h);
            g.FillEllipse(fb, r);
            g.DrawEllipse(trailRim[lv], r);
        }
        for (int i = 0; i < n; i++)                                            // 흩날리는 알갱이
        {
            double t = (i + .5) / n;
            double px = dir > 0 ? floor.X + len * (.08 + .8 * t) : floor.X + len * (.92 - .8 * t);
            int s2 = k * (i % 2 == 0 ? 2 : 1);
            g.FillRectangle(trailSpeck[lv], (int)px - s2 / 2, (int)(floor.Y - (8 + (i % 3) * 3 + spread * 4) * k), s2, s2);
        }
    }

    // 디그 빛: 공이 튕기는 자리에 노란 후광 + 흰 점 + 빛줄기 셋 (블로킹 번쩍보다 작게, 0.2초, 화면 흔들림 없음)
    static Rectangle DigRect(VolleySim w) => new((int)w.DigFlashX - 20 * w.K, (int)w.DigFlashY - 20 * w.K, 40 * w.K, 40 * w.K);

    void DrawDigFlash(Overlay o, Graphics g, VolleySim w)
    {
        int k = w.K;
        double p = w.DigFlashP;
        var c = o.ToClient(w.DigFlashX, w.DigFlashY);
        int hr = (int)((5 + 5 * p) * k);
        g.FillEllipse(digHalo, c.X - hr, c.Y - hr, hr * 2, hr * 2);
        int dot = Math.Max(3, (int)((5 - 3 * p) * k));
        g.FillEllipse(digCore, c.X - dot / 2, c.Y - dot / 2, dot, dot);
        int r0 = hr + k, r1 = hr + (int)((4 + 3 * p) * k);
        for (int sx = -1; sx <= 1; sx += 2)
            g.DrawLine(starRayPen, c.X + sx * (int)(r0 * .72), c.Y - (int)(r0 * .72), c.X + sx * (int)(r1 * .72), c.Y - (int)(r1 * .72));
        g.DrawLine(starRayPen, c.X, c.Y - r0, c.X, c.Y - r1);
    }

    // 블로킹 번쩍: 네트 위 흰 점 + 노란 빛줄기 넷 (스파이크 별보다 작게)
    static Rectangle FlashRect(VolleySim w) => new(w.NetX - 14 * w.K, w.NetTopY - 8 * w.K - 14 * w.K, 28 * w.K, 28 * w.K);

    void DrawBlockFlash(Overlay o, Graphics g, VolleySim w)
    {
        int k = w.K;
        double p = 1 - (w.BlockFlashUntil - w.Clock) / VolleySim.BlockFlashSec; // 0 → 1
        var c = o.ToClient(w.NetX, w.NetTopY - 8 * k);
        int dot = Math.Max(3, (int)((4 - 2 * p) * k));
        g.FillEllipse(whiteBrush, c.X - dot / 2, c.Y - dot / 2, dot, dot);
        int r0 = (int)((3 + 2 * p) * k), r1 = (int)((6 + 6 * p) * k);
        for (int i = 0; i < 4; i++)
        {
            int sx = i % 2 == 0 ? 1 : -1, sy = i < 2 ? 1 : -1; // 네 모서리 쪽
            g.DrawLine(starRayPen, c.X + sx * r0, c.Y + sy * r0, c.X + sx * r1, c.Y + sy * r1);
        }
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

    // 새: 날갯짓 두 장 (0.14초마다), 왼쪽으로 날면 뒤집고, 맞으면 빙글
    void DrawBird(Overlay o, Graphics g, VolleySim w)
    {
        var cp = o.ToClient(w.BirdX, w.BirdY);
        int frame = (int)(w.Clock / .14) % 2;
        var bmp = BirdBitmap(frame, w.BirdVx < 0, w.K);
        var st = g.Save();
        g.TranslateTransform(cp.X, cp.Y);
        if (w.BirdHit) g.RotateTransform((float)(w.BirdTurn * 180 / Math.PI));
        g.DrawImageUnscaled(bmp, -bmp.Width / 2, -bmp.Height / 2);
        g.Restore(st);
    }

    readonly Dictionary<(int, bool, int), Bitmap> birdBmps = new();
    Bitmap BirdBitmap(int frame, bool flip, int k)
    {
        if (birdBmps.TryGetValue((frame, flip, k), out var bmp)) return bmp;
        var rows = VolleyFruits.Bird[frame];
        bmp = new Bitmap(rows[0].Length * k, rows.Length * k, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                char ch = rows[y][flip ? rows[y].Length - 1 - x : x];
                if (!VolleyFruits.BirdPal.TryGetValue(ch, out var col)) continue;
                using var br = new SolidBrush(ColorTranslator.FromHtml(col));
                g.FillRectangle(br, x * k, y * k, k, k);
            }
        return birdBmps[(frame, flip, k)] = bmp;
    }

    static void DrawDesktopCourt(Overlay o, Graphics g, VolleySim w)
    {
        int k = w.K;
        var tl = o.ToClient(w.WallLeft, w.CeilingY);
        var br = o.ToClient(w.WallRight, w.FloorY);
        g.DrawLine(boxPen, tl.X, br.Y, tl.X, tl.Y);   // 왼쪽 벽
        g.DrawLine(boxPen, tl.X, tl.Y, br.X, tl.Y);   // 천장
        g.DrawLine(boxPen, br.X, tl.Y, br.X, br.Y);   // 오른쪽 벽
        int x0 = tl.X - 1, w0 = br.X - tl.X + 2, y = br.Y + 1;
        g.FillRectangle(grassTop, x0, y, w0, k);
        g.FillRectangle(grassDeep, x0, y + k, w0, (GrassCells - 1) * k);
        for (int x = x0 + 2 * k; x < x0 + w0 - k; x += 5 * k) g.FillRectangle(grassMid, x, y - k, k, k); // 작은 풀 끝 (바닥선 위로 한 칸)
    }

    void DrawBall(Overlay o, Graphics g, VolleySim w)
    {
        var cp = o.ToClient(w.BallX, w.BallY);
        var frames = BallFrames(w.Ball, w.K);
        double turn = w.BallSpin / (Math.PI * 2) * BallTurns;
        var f = frames[(((int)Math.Round(turn)) % BallTurns + BallTurns) % BallTurns];
        g.DrawImageUnscaled(f, cp.X - f.Width / 2, cp.Y - f.Height / 2);
    }

    // 공 그림을 BallTurns 방향으로 미리 돌려 둔다 (판마다 한 번, 픽셀은 가장 가까운 점으로)
    Bitmap[] BallFrames(VolleySim.BallKind kind, int k)
    {
        if (ballFrames != null && ballFor == (kind, k)) return ballFrames;
        DisposeBall();
        ballFor = (kind, k);
        using var basic = kind == VolleySim.BallKind.Volleyball ? Volleyball(k) : Fruit(kind, k);
        int side = (int)Math.Ceiling(basic.Width * 1.45) | 1;
        ballFrames = new Bitmap[BallTurns];
        for (int i = 0; i < BallTurns; i++)
        {
            var bmp = new Bitmap(side, side, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.TranslateTransform(side / 2f, side / 2f);
            g.RotateTransform(i * 360f / BallTurns);
            g.DrawImage(basic, -basic.Width / 2f, -basic.Height / 2f, basic.Width, basic.Height);
            ballFrames[i] = bmp;
        }
        return ballFrames;
    }

    void DisposeBall()
    {
        if (ballFrames != null) foreach (var f in ballFrames) f.Dispose();
        ballFrames = null;
        ballFor = (VolleySim.BallKind.Volleyball, -1);
    }

    // 과일 공: VolleyFruits 의 17×17 칸을 k 배로
    static Bitmap Fruit(VolleySim.BallKind kind, int k)
    {
        var (pal, rows) = VolleyFruits.Art[kind];
        var bmp = new Bitmap(rows[0].Length * k, rows.Length * k, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
            {
                int i = VolleyFruits.Keys.IndexOf(rows[y][x]);
                if (i < 0) continue;
                using var br = new SolidBrush(ColorTranslator.FromHtml(pal[i]));
                g.FillRectangle(br, x * k, y * k, k, k);
            }
        return bmp;
    }

    // 배구공 (처음 그림 그대로)
    static Bitmap Volleyball(int k)
    {
        int r = VolleySim.BallCells * k;
        int d = (r + 1) * 2;
        var bmp = new Bitmap(d, d, PixelFormat.Format32bppPArgb);

        int cx = r + 1;
        int cy = r + 1;
        double scaleRatio = k / 3.0 * VolleySim.BallCells / 4.0; // 줄 굵기도 공 크기에 맞춰

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

                    bmp.SetPixel(x, y, c);
                }
            }
        }
        return bmp;
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
        bool right = pose == VolleySim.DivePose && w.SlideDirOf(VolleySim.Actor.Rival) > 0;        // 보통은 왼쪽 보기 (라이벌은 오른쪽에서 왼쪽을 바라봄), 오른쪽으로 던지는 동안만 오른쪽
        var rows = rivalStamps[right ? 0 : 1];
        if (!rows.TryGetValue(pose, out var row))
        {
            var sp = rivalSprites ?? artOf!;
            string form = (rivalSprites != null) ? host.Pet.P.Stage : "adult_grumpy";
            var stamps = Stamps.Of(sp);
            rows[pose] = row = stamps.Row(stamps.Has($"{form}/1/{pose}") ? $"{form}/1/{pose}" : $"{form}/1/idle", w.K, !right); // 없는 자세는 가만히로 (그림 창이 멈추지 않게)
        }
        if (pose == VolleySim.DivePose && row.Length >= 2) return row[w.IsLying(VolleySim.Actor.Rival) ? 1 : 0]; // 던지는 중은 눈 뜬 첫 장, 누워 있으면 ^^ 둘째 장
        return row[(int)(w.Clock * 4) % row.Length];
    }

    void DrawMe(Overlay o, Graphics g, VolleySim w)
    {
        var stamp = w.PlayerPose switch
        {
            "happy" => meHappyStamp,
            "dizzy" => meDizzyStamp,
            "sad" => meSadStamp,
            "angry" => meAngryStamp,
            "hop" => meHopStamp,
            VolleySim.BlockPose => meBlockStamp,
            VolleySim.TripPose => meSlideStamp,
            VolleySim.DivePose => (w.SlideDirOf(VolleySim.Actor.Player) > 0 ? meDiveRight : meDiveLeft)[w.IsLying(VolleySim.Actor.Player) ? 1 : 0],
            "shock" => meShockStamp,
            "walk" => meWalkStamp,
            _ => meIdleStamp,
        };

        stamp?.Put(o, g, (int)w.PlayerX, (int)w.PlayerY - 20 * w.K);
    }

    Stamp MePose(Stamps st, string stage, string pose, int k) => st.Get(st.Has($"{stage}/1/{pose}") ? $"{stage}/1/{pose}" : $"{stage}/1/idle", 0, k, false); // 없는 자세는 가만히로

    void Art(int k)
    {
        var sp = host.Sprites;
        if (ReferenceEquals(artOf, sp) && artK == k) return;
        (artOf, artK) = (sp, k);
        var st = Stamps.Of(sp);
        int small = GameHud.Small(k);

        star = st.Get("game/star", 0, small);
        crown = st.Get("game/crown", 0, small);
        bang = st.Get("emo/bang", 0, small);

        var b = host.Pet;
        meIdleStamp = st.Get($"{b.P.Stage}/1/idle", 0, k, false);
        meHopStamp = st.Get($"{b.P.Stage}/1/hop", 0, k, false);
        meAngryStamp = st.Get($"{b.P.Stage}/1/angry", 0, k, false);
        meHappyStamp = st.Get($"{b.P.Stage}/1/happy", 0, k, false);
        meSadStamp = st.Get($"{b.P.Stage}/1/sad", 0, k, false);
        meDizzyStamp = st.Get($"{b.P.Stage}/1/dizzy", 0, k, false);
        meBlockStamp = MePose(st, b.P.Stage, VolleySim.BlockPose, k);
        meSlideStamp = MePose(st, b.P.Stage, VolleySim.TripPose, k);
        meShockStamp = MePose(st, b.P.Stage, "shock", k);
        meWalkStamp = MePose(st, b.P.Stage, "walk", k);
        string diveName = st.Has($"{b.P.Stage}/1/{VolleySim.DivePose}") ? $"{b.P.Stage}/1/{VolleySim.DivePose}" : $"{b.P.Stage}/1/idle";
        meDiveRight = new[] { st.Get(diveName, 0, k, false), st.Get(diveName, 1, k, false) };
        meDiveLeft = new[] { st.Get(diveName, 0, k, true), st.Get(diveName, 1, k, true) };

        if (!string.IsNullOrEmpty(RivalSpecies)) rivalSprites = host.SpritesOf(RivalSpecies);
        rivalStamps[0].Clear();
        rivalStamps[1].Clear();
    }

    // ── 디버그 명령 ──
    // volley / volley serve / volley spike / volley stone [me|rival] / volley slide [me|rival] [l|r] / volley block / volley end win|lose / volley bird / volley desktop / volley stop
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

        if (a.Length > 1 && a[1] == "stone" && sim is { } wStone) // 디버그: 바로 앞에 돌 (volley stone [me|rival])
        {
            wStone.DebugStone(a.Length > 2 && a[2] == "rival" ? VolleySim.Actor.Rival : VolleySim.Actor.Player);
            return true;
        }

        if (a.Length > 1 && a[1] == "block" && sim is { } wBlock) // 디버그: 네트 앞에서 뛰게 (블로킹 자세)
        {
            wBlock.DebugJump(VolleySim.Actor.Player, wBlock.NetX - wBlock.PetR - 4);
            return true;
        }

        if (a.Length > 1 && a[1] == "slide" && sim is { } wSlide) // 디버그: 바로 몸을 던지게 (volley slide [me|rival] [l|r], 방향 없으면 네트 쪽)
        {
            var who = a.Length > 2 && a[2] == "rival" ? VolleySim.Actor.Rival : VolleySim.Actor.Player;
            int dir = a.Length > 3 ? (a[3] == "l" ? -1 : 1) : who == VolleySim.Actor.Player ? 1 : -1;
            wSlide.DebugSlide(who, dir);
            return true;
        }

        if (a.Length > 1 && a[1] == "bird" && sim is { } wBird) // 디버그: 새 한 마리 바로
        {
            wBird.SpawnBird();
            return true;
        }

        if (a.Length > 1 && a[1] == "desktop" && !Running && !host.Busy) // 디버그: 창이 있어도 바탕화면 판으로
        {
            Begin(null, forceDesktop: true);
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
