using System.Text;

namespace Bolok;

// 눈길 마중: 장면 그려 보기(--mode-scene trail[@배율] 그림.png [값]), 속도 재기(--mode-bench trail)
//   값 없음: 두 장 — 그림.png = 곧은 길 · 굽이+점프 · 넘어짐, 그림-2.png = 쉼터 도착(친구) · 완주+새 기록 · 시간 초과
//   값 check: 자가 검사만 · 값 table[:씨 수[:full[:손잡이]]]: 봇 표 · 값 art: 길 위 그림 모음(크기 단계 12) · 값 start: 3·2·1 + 시작 안내
static partial class TrailCheck
{
    static readonly string[] Friends = { "cat", "shiba", "redpanda", "fox", "corgi" };

    static TrailGame NewGame(SceneHost host, bool auto, TrailSkill skill)
    {
        var g = new TrailGame(host) { auto = auto, skill = skill };
        g.FriendList = () => Friends;
        g.FriendArt = sp => Sprites.Drawn(PetArt.Embedded, sp, null);
        return g;
    }

    public static void Scene(string path, int k)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string? arg = GameModes.SceneArg;
        if (arg == "check") { L.Use("ko"); Console.WriteLine(Check()); return; }
        if (arg is { } ta && ta.StartsWith("table"))
        {
            L.Use("ko");
            var p = ta.Split(':');
            Console.WriteLine(Table(p.Length > 1 && int.TryParse(p[1], out int n) ? n : DefaultSeeds, p.Length > 2 && p[2] == "full", p.Length > 3 ? p[3] : null));
            return;
        }
        L.Use("ko");
        if (arg is { } da && da.StartsWith("dodge:")) { Console.WriteLine(Dodges(da.Split(':'))); return; }   // 디버그: dodge:<씨>:<단계>:<dash|slow> 새까지 피하며 틈 따라가기에서 어디서 맞는지
        if (arg is { } fa && fa.StartsWith("follow:")) { Console.WriteLine(Follows(fa.Split(':'))); return; }   // 디버그: follow:<씨>:<단계>:<dash|slow> 틈 따라가기에서 어디서 닿는지
        var host = new SceneHost(scale: k);
        if (arg == "art") { ArtSheet(path, host, Math.Min(k, 4)); return; }
        int f = Math.Min(Math.Max(k, 2), 4);
        var win = new Rectangle(300, 90, 1320, 860);
        var panel = TrailGame.InWindow(win, 200 * f, 120 * f, f);
        var view = Rectangle.Inflate(panel, 24, 24);
        if (arg == "start")
        {
            var sg = NewGame(host, false, TrailSkill.Good);
            var st = new Trail(4, 1, Rx);
            sg.Begin(st, panel, Session.Fake(panel));
            using var sov = host.Overlay(sg);
            Shots(path, sov, view, win, 1, _ => { sg.Frame(1 / 60.0); return st.Clock > .5; }, "start");
            return;
        }
        if (arg == "bird")   // 새: 멀리서 그림자와 함께 · 가까이 · 맞은 순간 (깃털 + 점수 깎임). 길을 비워 새만 보이게
        {
            var bg = NewGame(host, false, TrailSkill.Good);
            var bt = new Trail(4, 1, Rx) { Things = Array.Empty<TrailThing>(), Score = 420 };
            bt.Birds = new[] { new TrailBird { M = 900, U = .25f } };
            bg.Begin(bt, panel, Session.Fake(panel));
            using var bov = host.Overlay(bg);
            double Left() => bt.Birds[0].M - bt.Pos;
            Shots(path, bov, view, win, 3, shot =>
            {
                bt.X = shot < 2 ? -.4 : (Left() < 70 ? .25 : -.4);
                bg.Frame(1 / 60.0);
                return shot switch { 0 => Left() < 360, 1 => Left() < 46, _ => bt.BirdHits == 1 && bt.Clock - bt.BirdAt > .28 };
            }, "bird");
            return;
        }
        Trail t = null!;
        var game = NewGame(host, true, TrailSkill.Good);
        int made = -1, tick = 0;
        bool Ready(int shot, int offset)
        {
            shot += offset;
            if (made != shot)
            {
                made = shot; tick = 0;
                (int seed, int stage, TrailSkill skill) = shot switch { 0 => (4, 1, TrailSkill.Good), 1 => (6, 3, TrailSkill.Good), 2 => (9, 2, TrailSkill.Easy), 3 => (5, 1, TrailSkill.Good), 4 => (11, 1, TrailSkill.Good), _ => (23, 3, TrailSkill.Easy) };
                (game.auto, game.skill) = (true, skill);
                game.forceNewBest = shot == 4;
                game.LoadState(shot == 5 ? "{\"Best\":2950,\"Stage\":3,\"MaxStage\":2,\"Clears\":2,\"Plays\":9,\"BestLegs\":5}" : "{\"Best\":0,\"Stage\":1,\"MaxStage\":0,\"Clears\":0,\"Plays\":0,\"BestLegs\":0}");
                t = new Trail(seed, stage, Rx);
                game.Begin(t, panel, Session.Fake(panel));
            }
            tick++;
            game.Frame(1 / 60.0);
            switch (shot)
            {
                case 0: return t.Clock > 9 && t.Things.Count(x => !x.Gone && x.Harm && t.Ahead(x) is > 70 and < 330) >= 3;
                case 1: return t.Airborne && Math.Abs(t.Curve) > .22 && t.Air > 18 || tick > 60 * 150;
                case 2: return t.Halted && t.Clock - t.HurtAt > .3 && t.LastHit != TrailKind.Pile || tick > 60 * 150;
                case 3: if (t.Gates[0] - t.Pos < 13 && t.Leg == 0) { t.X = .3; return true; } return false;
                case 4: return t.State == Trail.Phase.Win && t.EndedFor > .3;
                default:
                    if (t.Leg == 3 && t.Time > 2) t.Time = 2;
                    return t.State == Trail.Phase.Over && t.EndedFor > .3;
            }
        }
        string two = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path) + "-2" + Path.GetExtension(path));
        using var ov = host.Overlay(game);
        Shots(path, ov, view, win, 3, s => Ready(s, 0), "play");
        Shots(two, ov, view, win, 3, s => Ready(s, 3), "play2");
    }

    static string Dodges(string[] a)
    {
        var t = new Trail(int.Parse(a[1]), int.Parse(a[2]), Rx);
        bool dash = a.Length > 3 && a[3] == "dash";
        var sb = new StringBuilder();
        t.OnEvent = e => { if (e is "bird" or "hit") sb.AppendLine($"  {e} 자리 {t.Pos:0} X {t.X:0.00} 빠르기 {t.Speed:0}"); };
        bool trace = a.Length > 4 && double.TryParse(a[4], out _);
        double tm = trace ? double.Parse(a[4]) : -1;
        for (int i = 0; i < 60 * 300 && !t.Done; i++)
        {
            FollowDodge(t, dash); t.Update(Trail.Tick);
            if (trace && Math.Abs(t.Pos - tm) < 220 && i % 6 == 0) sb.AppendLine($"    자리 {t.Pos:0} X {t.X:0.00} Vx {t.Vx:0.00}");
        }
        sb.AppendLine($"끝 {t.State} 넘어짐 {t.Hits} 새 {t.BirdHits}");
        for (int i = 0; i < t.Birds.Length; i++)
        {
            var b = t.Birds[i];
            int bi = -1;
            for (int k = 0; k + 1 < t.Bundles.Count; k++) if (t.AfterOf(t.Bundles[k]) <= b.M && b.M <= t.BeforeOf(t.Bundles[k + 1])) bi = k;
            sb.AppendLine($"  새 {i} M {b.M:0} 줄 {b.U:0.00} {(b.Hit ? "맞음" : "")} 앞 묶음 {(bi >= 0 ? $"{t.AfterOf(t.Bundles[bi]):0} 틈 {t.Bundles[bi].Gap:0.00}" : "-")} 뒤 묶음 {(bi >= 0 ? $"{t.BeforeOf(t.Bundles[bi + 1]):0} 틈 {t.Bundles[bi + 1].Gap:0.00}" : "-")}");
        }
        return sb.ToString();
    }

    static string Follows(string[] a)
    {
        var t = new Trail(int.Parse(a[1]), int.Parse(a[2]), Rx);
        bool dash = a.Length > 3 && a[3] == "dash";
        var sb = new StringBuilder();
        t.OnEvent = e => { if (e == "hit") sb.AppendLine($"  닿음 {t.LastHit} 자리 {t.Pos:0} X {t.X:0.00} 빠르기 {t.Speed:0}"); };
        for (int i = 0; i < 60 * 300 && !t.Done; i++) { Follow(t, dash); t.Update(Trail.Tick); }
        sb.AppendLine($"끝 {t.State} 넘어짐 {t.Hits}");
        foreach (var b in t.Bundles.Where(b => b.Kind == TrailKind.Ball).Take(4)) sb.AppendLine($"  눈덩이 묶음 {b.Start:0}~{b.End:0} 틈 {b.Gap:0.00}");
        return sb.ToString();
    }

    // 작업 화면 흉내 (표 모양 창): 하늘이 비어 있는 판 뒤에 이것이 보인다
    static void FakeWork(Graphics g, Rectangle r)
    {
        g.FillRectangle(new SolidBrush(Color.FromArgb(86, 104, 124)), 0, 0, 1920, 1080);
        g.FillRectangle(new SolidBrush(Color.FromArgb(251, 251, 252)), r);
        g.FillRectangle(new SolidBrush(Color.FromArgb(58, 111, 216)), r.Left, r.Top, r.Width, 30);
        g.FillRectangle(new SolidBrush(Color.FromArgb(236, 239, 244)), r.Left, r.Top + 30, r.Width, 34);
        var grid = new Pen(Color.FromArgb(222, 226, 233));
        int top = r.Top + 64;
        for (int x = r.Left + 40; x < r.Right; x += 104) g.DrawLine(grid, x, top, x, r.Bottom);
        for (int y = top; y < r.Bottom; y += 26) g.DrawLine(grid, r.Left, y, r.Right, y);
        var bar = new SolidBrush(Color.FromArgb(140, 148, 160));
        int row = 0;
        for (int y = top + 26; y < r.Bottom - 8; y += 26, row++)
            for (int x = r.Left + 50, col = 0; x < r.Right - 20; x += 104, col++)
            {
                ulong h = (ulong)(row * 73856093 ^ col * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                if (h % 7 == 0) continue;
                g.FillRectangle(bar, x, y + 10, Math.Min(26 + (int)(h >> 8) % 60, 84), 6);
            }
    }

    // SceneKit.Shots 와 같되 뒤에 작업 화면을 깐다
    static void Shots(string path, Overlay ov, Rectangle view, Rectangle win, int count, Func<int, bool> ready, string _, int maxFrames = 60 * 900)
    {
        using var bmp = new Bitmap((view.Width + 20) * count, view.Height + 20);
        using var g = Graphics.FromImage(bmp);
        using var frame = new Bitmap(ov.Width, ov.Height);
        using var fg = Graphics.FromImage(frame);
        g.Clear(Color.White);
        for (int shot = 0, i = 0; shot < count && i < maxFrames; i++)
        {
            if (!ready(shot)) continue;
            FakeWork(fg, win);
            ov.Render(fg);
            g.DrawImage(frame, new Rectangle(shot * (view.Width + 20) + 10, 10, view.Width, view.Height), view, GraphicsUnit.Pixel);
            shot++;
        }
        bmp.Save(path);
    }

    // 길 위 그림 모음: 크기 단계 12 × 종류 (배율 f)
    static void ArtSheet(string path, SceneHost host, int f)
    {
        using var v = new TrailView(host.Sprites, f, new (string, Sprites)?[5]);
        var rows = v.Gallery().ToList();
        int cw = 46 * f, ch = 46 * f;
        using var bmp = new Bitmap(rows.Count > 0 ? TrailView.Steps * cw + 20 : 1, rows.Count * ch + 20);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(186, 198, 211));
        for (int r = 0; r < rows.Count; r++)
            for (int i = 0; i < TrailView.Steps; i++)
            {
                var p = rows[r].Item2[i];
                g.DrawImageUnscaled(p.Bmp, 10 + i * cw + (int)(cw / 2 - p.AnchorX * f), 10 + r * ch + ch - 4 * f - p.AnchorY * f);
            }
        bmp.Save(path);
    }

    // ── 속도 재기 (--mode-bench trail): 잘하는 봇으로 5단계 한 판 (길 위의 것이 가장 많다), 배율 3·4 ──
    public static string BenchRun()
    {
        var sb = new StringBuilder();
        foreach (int k in new[] { 3, 4 })
        {
            var host = new SceneHost(scale: k);
            var game = NewGame(host, true, TrailSkill.Good);
            var win = new Rectangle(300, 90, 1320, 860);
            var panel = TrailGame.InWindow(win, 200 * k, 120 * k, k);
            var t = new Trail(31, 5, Rx);
            game.Begin(t, panel, Session.Fake(panel));
            using var ov = host.Overlay(game);
            sb.AppendLine($"배율 {k} (판 {panel.Width}×{panel.Height}): " + Bench.Run($"trail@{k}", ov, game, panel, () => !t.Done, () => game.Frame(1 / 60.0), 60 * 240));
            game.Stop(true);
        }
        sb.AppendLine(Table(DefaultSeeds, false));
        return sb.ToString().TrimEnd();
    }
}
