namespace Bolok;

// 한 판의 조작과 판: 게임은 키·마우스를 직접 읽지 않고 여기 값만 본다 (게임마다 조작 규칙이 달라지지 않게).
//
// 조작 규칙 (YELL 2026-11-13): 게임이 쓰는 건 마우스 움직임·왼쪽/오른쪽 버튼, 방향키, Ctrl, Shift 뿐이다. 글자 키·Space·Tab 은 읽지 않는다
//  · 방향키는 판(GameLayer)이 앞 창일 때만 읽는다 → 다른 창에 글을 쓰는 동안에는 볼록이가 움직이지 않는다. 판이 없는 게임은 방향키를 못 쓴다
//  · Ctrl·Shift 는 어느 창에서든 읽는다 (눌러도 일하던 문서에 아무것도 찍히지 않는 키라서)
//  · 마우스 버튼은 판 위를 눌렀을 때만 온다 (판 밖 클릭은 일하던 창으로 간다)
//  · Esc 는 여기서 읽지 않는다. 보스 키(PetForm.BossKey)가 어느 창에서든 읽어 모든 게임의 Stop(true) 를 부른다.
//    판에 초점이 있을 때 누른 Esc 도 앱이 quit(= 게임의 Stop(true))로 돌려준다. Stop(true) 는 결과 화면·표정 없이 바로 끝나야 한다 (Close(quit: true))
//  · 판이 열려 있는 동안 본체·그림 창·판은 화면 공유·녹화에 찍히지 않는다
//
// 쓰는 법: Start 에서 Session.Open(...) → Frame 맨 처음에 Tick() → 값 읽기 → Stop 에서 Close(...)
sealed class Session
{
    public static readonly Keys[] Allowed = { Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.ControlKey, Keys.ShiftKey }; // 읽는 키는 이것뿐

    readonly IGameHost? host;
    readonly Func<Keys, bool> key;
    readonly Func<bool> active;
    readonly Func<Point> mouse;
    bool leftNow, rightNow, leftHit, rightHit, leftUp, rightUp, started;

    public readonly Rectangle Field;
    public GameLayer? Layer { get; private set; }   // 커서 모양을 바꿀 때만 쓴다 (키·마우스 이벤트를 따로 달지 않는다)

    public bool Active { get; private set; }        // 판이 지금 앞 창인가
    public int AxisX { get; private set; }          // 방향키 ← → : -1·0·1 (판이 앞 창일 때만)
    public int AxisY { get; private set; }          // 방향키 ↑ ↓ : -1(위)·0·1(아래)
    public bool Up => AxisY < 0;                    // ↑ (점프처럼 한 키로 쓸 때)
    public bool Ctrl { get; private set; }
    public bool Shift { get; private set; }
    public Point Mouse { get; private set; }        // 화면 좌표
    public bool MouseMoved { get; private set; }    // 지난 프레임 뒤로 움직였나
    public bool Left { get; private set; }          // 왼쪽 버튼을 누르고 있나
    public bool Right { get; private set; }
    public bool LeftHit { get; private set; }       // 이번 프레임에 눌렀나
    public bool RightHit { get; private set; }
    public bool LeftUp { get; private set; }        // 이번 프레임에 뗐나
    public bool RightUp { get; private set; }

    Session(IGameHost? host, Rectangle field, Func<Keys, bool> key, Func<bool> active, Func<Point> mouse) =>
        (this.host, Field, this.key, this.active, this.mouse) = (host, field, key, active, mouse);

    // 판을 연다. layer: false 면 판 없이 (일하던 창을 그대로 쓰는 게임: Ctrl·Shift 만 읽는다). quit 은 게임의 Stop(true)
    public static Session Open(IGameHost host, Rectangle field, Action quit, bool layer = true)
    {
        Session s = null!;
        s = new Session(host, field, Native.KeyDown, () => s.Layer is { IsDisposed: false } l && Native.GetForegroundWindow() == l.Handle, () => Control.MousePosition);
        if (layer) s.Layer = host.OpenLayer(field, (_, e) => s.Press(e.Button, true), (_, e) => s.Press(e.Button, false), quit);
        host.HideFromCapture(true); // 판을 연 뒤에: 판도 같이 숨는다
        return s;
    }

    // 화면 없이 (자가 검사·장면 그리기): 키·앞 창·마우스를 넣어 준다
    public static Session Fake(Rectangle field, Func<Keys, bool>? key = null, Func<bool>? active = null, Func<Point>? mouse = null) =>
        new(null, field, key ?? (_ => false), active ?? (() => false), mouse ?? (() => Point.Empty));

    // 판 위에서 버튼을 누르거나 뗐다 (판이 알려 준다. 자가 검사는 직접 부른다)
    public void Press(MouseButtons button, bool down)
    {
        if (button == MouseButtons.Left) { leftNow = down; if (down) leftHit = true; else leftUp = true; }
        else if (button == MouseButtons.Right) { rightNow = down; if (down) rightHit = true; else rightUp = true; }
    }

    bool Key(Keys k) => Array.IndexOf(Allowed, k) >= 0 && key(k);

    // 프레임마다 맨 처음에 한 번
    public void Tick()
    {
        Active = active();
        AxisX = Active ? (Key(Keys.Right) ? 1 : 0) - (Key(Keys.Left) ? 1 : 0) : 0;
        AxisY = Active ? (Key(Keys.Down) ? 1 : 0) - (Key(Keys.Up) ? 1 : 0) : 0;
        (Ctrl, Shift) = (Key(Keys.ControlKey), Key(Keys.ShiftKey));
        var at = mouse();
        MouseMoved = started && at != Mouse; // 처음 자리는 움직인 걸로 치지 않는다 (메뉴를 고르던 커서 자리로 튀지 않게)
        (Mouse, started) = (at, true);
        (Left, Right, LeftHit, RightHit, LeftUp, RightUp) = (leftNow, rightNow, leftHit, rightHit, leftUp, rightUp);
        leftHit = rightHit = leftUp = rightUp = false;
    }

    // ── 조작 안내 말풍선: 판이 열리고 GuideSec 초 동안. 줄 순서는 마우스 → 키 → Esc (lang.json 키로) ──
    public const double GuideSec = 4;
    string[]? guide;
    public double GuideUntil { get; set; }

    public void Guide(params string[] langKeys)
    {
        guide = new string[langKeys.Length];
        for (int i = 0; i < guide.Length; i++) guide[i] = L.T(langKeys[i]);
        GuideUntil = GuideSec;
    }

    public bool GuideOn(double clock) => guide != null && clock < GuideUntil;
    public void DrawGuide(Overlay o, Graphics g, double clock, double petX, double feet) { if (GuideOn(clock)) o.DrawNoteAt(g, petX, feet, Field, guide!); }
    public void DirtyGuide(Overlay o, List<Rectangle> rs, double clock, double petX, double feet) { if (GuideOn(clock)) rs.Add(o.Client(o.NoteRect(petX, feet, Field, guide!))); }

    // ── 끝내기 ──
    public bool Closed { get; private set; }

    // 판을 닫고 녹화 숨김을 푼다. 하던 창으로 초점이 돌아간다
    public void Close()
    {
        if (Closed) return;
        Closed = true;
        if (host == null) return;
        host.HideFromCapture(false);
        if (Layer != null) host.CloseLayer();
        Layer = null;
        host.Repaint();
    }

    // 판을 닫고 볼록이를 돌려놓는다. quit(보스 키·디버그 stop)이면 표정·별 없이 바로 하던 대로, 아니면 이겼는지에 따라 기뻐하거나 시무룩
    public void Close(Brain pet, bool quit, bool won, string what, params (string, object?)[] log)
    {
        Close();
        if (quit) { pet.LeavePuppet(); Dbg.Log(what + "-quit", log); }
        else pet.EndPuppet(won, what, log);
    }
}
