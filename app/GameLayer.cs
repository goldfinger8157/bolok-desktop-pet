namespace Bolok;

// 게임 중 모니터마다 까는 판. 거의 투명해서 작업 화면은 평소 그대로 보이고, 클릭은 전부 여기로 온다.
// 커서는 게임이 정한다: 숨기거나(HidePointer), 보통 커서로 두거나(ShowPointer), 조준경 같은 제 커서를 단다
public sealed class GameLayer : Form
{
    static Cursor? blank;

    public GameLayer(Rectangle bounds, double opacity)
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        BackColor = Color.Black;
        Opacity = opacity;
        KeyPreview = true;
        blank ??= Native.MakeCursor(new Bitmap(16, 16), 0, 0);
        Cursor = blank;
    }

    public void ShowPointer() => Cursor = Cursors.Default; // 끝났다는 걸 커서로도 알린다
    public void HidePointer() => Cursor = blank!;          // 조준경이 터지는 동안

    protected override CreateParams CreateParams
    {
        get
        {
            var c = base.CreateParams;
            c.ExStyle |= Native.WS_EX_TOPMOST | Native.WS_EX_TOOLWINDOW;
            return c;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_DPICHANGED) return;
        base.WndProc(ref m);
    }
}
