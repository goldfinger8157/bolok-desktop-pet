namespace Bolok;

// 연타하는 동안만 볼록이 칸 전체에 까는 받침. 폴짝·벌렁으로 몸 모양이 바뀌면 누르던 자리가 투명해져서
// 다음 클릭이 뒤 창으로 빠지기 때문이다. 불투명도가 1% 라 안 보이지만 클릭은 받는다
public sealed class ClickPad : Form
{
    public ClickPad()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Opacity = .01;
        Cursor = Cursors.Hand;
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
        if (m.Msg == Native.WM_DPICHANGED) return;
        base.WndProc(ref m);
    }
}
