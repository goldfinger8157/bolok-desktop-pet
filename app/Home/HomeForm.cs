using System.Drawing.Imaging;

namespace Bolok;

// 방석·집 창 하나 (docs/home.md 5장). 펫 창·ClickPad 와 같은 꼴: 테두리 없음, 투명색 자리는 클릭이 뒤 창으로 지나가고 그림 픽셀만 클릭을 받는다.
// 창 크기 = 그림 칸 × 배율 딱 그만큼. OnPaint 는 DrawImageUnscaled 한 번 (받는 순간만 하얗게 번쩍). 바뀔 때만 다시 그린다
sealed class HomeForm : Form
{
    public readonly bool House;
    public HomeItem? Item { get; private set; }
    public int K { get; private set; } = 3;
    public bool Covered { get; private set; }     // 펫이 있다: 앞 겹 그림이 덮인 그림 (이불 요 세트)
    public double FlashUntil;                      // 이때까지 하얗게 번쩍 (받는 순간·바꿀 때, 펫 시계 b.Now 기준)
    public Func<double> Now = () => 0;

    public Action<HomeForm>? Clicked, RightClicked, DragStarted, DragEnded;
    public Action<HomeForm, Point>? DragMoved;     // 끄는 중: 마우스 화면 좌표
    public int GrabX;                              // 누른 자리와 그림 가운데의 가로 거리 (끌 때 그 자리를 잡은 채로)
    HomeArt? art;
    public HomeArt? Art => art;
    bool pressing, dragging, flashShown;
    Point pressAt;

    public HomeForm(bool house)
    {
        House = house;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = PetForm.Key;
        TransparencyKey = PetForm.Key;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var c = base.CreateParams;
            c.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return c;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_DPICHANGED) return; // 크기는 그림 칸 × 배율이 정한다
        base.WndProc(ref m);
    }

    // 그림·배율·자리를 정한다. 같은 것이면 아무것도 안 한다 (다시 그리지 않는다)
    public void Set(HomeArt art, HomeItem it, int k, Point at, bool covered)
    {
        bool redraw = !ReferenceEquals(Item, it) || K != k || Covered != covered || !ReferenceEquals(this.art, art);
        (this.art, Item, K, Covered) = (art, it, k, covered);
        var size = new Size(it.W * k, it.H * k);
        if (ClientSize != size) { ClientSize = size; redraw = true; }
        if (Location != at) Location = at;
        if (redraw) Invalidate();
    }

    public bool Flashing => Now() < FlashUntil;

    // 번쩍이 끝나는 순간 한 번 더 그리게 (프레임마다 부른다: 바뀔 때만 Invalidate)
    public void Tick()
    {
        bool f = Flashing;
        if (f != flashShown) { flashShown = f; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Item is not { } it || art == null) return;
        var bmp = art.Body(it, K, Covered);
        if (Flashing) // 하얀 그림자: 펫이 자랄 때 하얗게 빛나는 것과 같은 색 행렬
        {
            HomeArt.DrawFlash(e.Graphics, bmp, 0, 0);
        }
        else e.Graphics.DrawImageUnscaled(bmp, 0, 0);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            pressing = true; dragging = false; pressAt = MousePosition;
            GrabX = MousePosition.X - (Left + Width / 2);
        }
        else if (e.Button == MouseButtons.Right) RightClicked?.Invoke(this);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!pressing) return;
        var p = MousePosition;
        if (!dragging && Math.Abs(p.X - pressAt.X) + Math.Abs(p.Y - pressAt.Y) > 6) // 6px 넘게 움직이면 끌기
        {
            dragging = true;
            DragStarted?.Invoke(this);
        }
        if (dragging) DragMoved?.Invoke(this, p);
    }

    // 끄는 중에 창이 숨거나(게임 시작 등) 다른 창이 마우스를 가져가면 끌기를 끝낸 것으로 친다 (Moving 이 남아 펫이 물건에 못 가는 일이 없게)
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!pressing || Capture) return;
        pressing = false;
        if (dragging) { dragging = false; DragEnded?.Invoke(this); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !pressing) return;
        pressing = false;
        if (dragging) { dragging = false; DragEnded?.Invoke(this); }
        else Clicked?.Invoke(this);
    }
}
