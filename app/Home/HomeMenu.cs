namespace Bolok;

// 방석·집 메뉴 (docs/home.md 8장): 물건을 오른쪽 클릭하면 그 종류의 받은 것만, 펫 메뉴에는 「집·방석」 묶음.
// 글은 menuTexts 에 넣지 않고 열 때 L.T 로 (받은 것이 늘어난다). 작은 그림은 HomeArt.Thumb
static class HomeMenu
{
    static int ThumbPx(Control c) => Math.Max(24, 32 * c.DeviceDpi / 96);

    static ToolStripMenuItem Entry(HomeLife h, HomeArt art, string id, bool placed, int px)
    {
        var it = art.Find(id)!;
        var m = new ToolStripMenuItem(L.Home(id), new Bitmap(art.Thumb(it, px))) { Checked = placed, Tag = id }; // 그림은 복사본: 메뉴가 닫힐 때 같이 놓는다 (캐시는 건드리지 않는다)
        m.Click += (_, _) => h.SetPlaced(it.House, id);
        return m;
    }

    // 물건을 오른쪽 클릭: 그 종류의 받은 것만 받은 순서로 ([작은 그림] 이름, 놓인 것은 체크) — 분리선 — 「제자리로」
    // 그 종류의 받은 것만 받은 순서로 + 분리선 + 「제자리로」 (그려 보기도 이걸 쓴다)
    public static ContextMenuStrip ItemMenu(HomeLife h, HomeArt art, bool house, int px)
    {
        var menu = new ContextMenuStrip { ImageScalingSize = new Size(px, px) };
        foreach (var id in h.Book.Received(house)) menu.Items.Add(Entry(h, art, id, id == h.Book.Placed(house), px));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.T("menu.home_reset"), null, (_, _) => h.ResetSpot(house));
        return menu;
    }

    // 메뉴 항목과 그 그림을 놓는다
    static void DisposeItems(IEnumerable<ToolStripItem> items)
    {
        foreach (var i in items.ToList()) { i.Image?.Dispose(); i.Dispose(); }
    }

    public static void ForItem(HomeLife h, HomeLife.Slot s, HomeForm f)
    {
        var menu = ItemMenu(h, f.Art!, s.House, ThumbPx(f));
        var back = Native.GetForegroundWindow(); // 메뉴를 닫으면 일하던 창으로 돌려준다 (펫 메뉴와 같게)
        Native.SetForegroundWindow(f.Handle);
        menu.Closed += (_, _) =>
        {
            if (back != IntPtr.Zero) Native.SetForegroundWindow(back);
            var t = new System.Windows.Forms.Timer { Interval = 100 }; // 닫히는 중에 놓지 않고 잠깐 뒤에
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); DisposeItems(menu.Items.Cast<ToolStripItem>()); menu.Dispose(); };
            t.Start();
        };
        menu.Show(Control.MousePosition);
    }

    // 펫 메뉴의 「집·방석」: 방석 ▸ (받은 방석들) · 집 ▸ (받은 집들) · 제자리로 · 숨기기. 받은 게 하나도 없으면 통째로 숨긴다
    public sealed class PetItem
    {
        public readonly ToolStripMenuItem Root = new() { Visible = false };
        readonly HomeLife h;
        readonly Func<HomeArt> art;
        readonly ToolStripMenuItem cushions = new(), houses = new(), reset = new(), hide = new() { CheckOnClick = false };
        readonly ToolStripSeparator line = new();

        public PetItem(HomeLife h, Func<HomeArt> art)
        {
            this.h = h;
            this.art = art;
            Root.DropDownItems.AddRange(new ToolStripItem[] { cushions, houses, line, reset, hide });
            reset.Click += (_, _) => h.ResetSpot(null);
            hide.Click += (_, _) => h.SetHidden(!h.Book.Hidden);
        }

        // 메뉴가 열릴 때마다 다시 짠다 (받은 것이 늘어난다). 집이 없으면 「집」 줄만 숨긴다
        public void Refresh(Control owner)
        {
            Root.Text = L.T("menu.home");
            Root.Visible = h.Book.Got.Count > 0;
            if (!Root.Visible) return;
            int px = ThumbPx(owner);
            Fill(cushions, false, px);
            Fill(houses, true, px);
            cushions.Text = L.T("menu.home_cushion");
            houses.Text = L.T("menu.home_house");
            reset.Text = L.T("menu.home_reset");
            hide.Text = L.T("menu.home_hide");
            hide.Checked = h.Book.Hidden;
        }

        void Fill(ToolStripMenuItem parent, bool house, int px)
        {
            var list = h.Book.Received(house).ToList();
            parent.Visible = list.Count > 0;
            DisposeItems(parent.DropDownItems.Cast<ToolStripItem>());
            ((ToolStripDropDownMenu)parent.DropDown).ImageScalingSize = new Size(px, px);
            foreach (var id in list) parent.DropDownItems.Add(Entry(h, art(), id, id == h.Book.Placed(house), px));
        }
    }
}
