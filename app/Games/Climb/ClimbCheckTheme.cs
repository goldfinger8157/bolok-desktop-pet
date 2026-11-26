using System.Drawing.Imaging;

namespace Bolok;

// 클라이밍 테마 자가 검사 (docs/climb.md 「테마」): 테마 고르기 · 모든 테마가 모든 모습을 그림 · 가라앉는 줄 · 길 읽기 · 잎 고리
static partial class ClimbCheck
{
    static readonly ClimbTheme[] AllThemes = { ClimbTheme.Vine, ClimbTheme.Desk, ClimbTheme.Sweet, ClimbTheme.Balloon, ClimbTheme.Block };

    static string CheckTheme()
    {
        // 고르기: 같은 씨 = 같은 테마, 5000씨에서 다섯이 각각 20% ± 2%, A4 는 늘 덩굴
        var count = new int[5];
        for (int seed = 1; seed <= 5000; seed++)
        {
            var a = ClimbPlace.Theme(seed, ClimbLook.A2);
            Expect(a == ClimbPlace.Theme(seed, ClimbLook.A2) && a == ClimbPlace.Pick(seed), "같은 씨는 같은 테마");
            count[(int)a]++;
            Expect(ClimbPlace.Theme(seed, ClimbLook.A4) == ClimbTheme.Vine, "창 테두리(A4)는 늘 덩굴");
            Expect(ClimbPlace.Theme(seed, ClimbLook.A4, ClimbTheme.Desk) == ClimbTheme.Vine, "A4 는 테마를 정해도 덩굴");
            Expect(ClimbPlace.Theme(seed, ClimbLook.A2, ClimbTheme.Block) == ClimbTheme.Block, "정한 테마는 A2 에서 그대로");
        }
        for (int i = 0; i < 5; i++) Expect(count[i] is >= 900 and <= 1100, $"{AllThemes[i]} 가 5000씨 중 {count[i]} (20% ±2% = 900~1100)");
        // 이어지는 씨 말고 A4 에 안 뽑힌 판(씨로 가끔)만 보아도 고르다: 테마와 A4 동전이 서로 치우치지 않는다
        var noEdge = new int[5]; int ne = 0;
        for (int seed = 1; seed <= 5000; seed++) if (!ClimbPlace.Coin(seed)) { noEdge[(int)ClimbPlace.Pick(seed)]++; ne++; }
        for (int i = 0; i < 5; i++) Expect(Math.Abs(noEdge[i] / (double)ne - .2) <= .025, $"A4 가 아닌 판에서도 {AllThemes[i]} 가 고르다 ({noEdge[i]}/{ne})");

        // 게임: 정한 테마·씨로 고르기·A4 는 덩굴
        var host = new SceneHost("adult_sweet", 3);
        var scr = new[] { new Rectangle(0, 0, 1920, 1040) };
        ClimbGame Open(Action<ClimbGame> set) { var g = new ClimbGame(host) { desktopOnly = true, FakeOpen = f => Session.Fake(f, null, () => true), FakeScreens = scr }; set(g); return g; }
        var g1 = Open(g => { g.Command(new[] { "climb", "seed", "77" }); g.Command(new[] { "climb", "theme", "balloon" }); });
        g1.Command(new[] { "climb" });
        Expect(g1.Running && g1.Theme == ClimbTheme.Balloon && g1.View!.Theme == ClimbTheme.Balloon && g1.Placed.Look == ClimbLook.A2, "climb theme balloon 으로 정한 테마");
        g1.Stop(true);
        var g2 = Open(g => { g.Command(new[] { "climb", "seed", "77" }); g.Command(new[] { "climb", "theme", "auto" }); });
        g2.Command(new[] { "climb" });
        Expect(g2.Running && g2.Theme == ClimbPlace.Pick(77), "auto 는 씨로 고른 테마");
        g2.Stop(true);
        foreach (var (word, th) in new[] { ("vine", ClimbTheme.Vine), ("desk", ClimbTheme.Desk), ("sweet", ClimbTheme.Sweet), ("balloon", ClimbTheme.Balloon), ("block", ClimbTheme.Block) })
        {
            var g = Open(g => g.Command(new[] { "climb", "theme", word }));
            g.Command(new[] { "climb" });
            Expect(g.Running && g.Theme == th, $"climb theme {word}");
            g.Stop(true);
        }
        // A4 가 뽑힌 판: 창 테두리 판은 정한 테마가 있어도 덩굴 (A4 는 A2 로만 물러난다)
        var win = new Win(new IntPtr(1), new Rectangle(200, 100, 1200, 820), false);
        host.Pet.X = 1390; host.Pet.Y = 880;
        var ga = new ClimbGame(host) { lookMode = 1, FakeOpen = pn => Session.Fake(pn, null, () => true), FakeWindows = () => new List<Win> { win }, FakeProbe = h => new WinState(true, false, win.R), FakeScreens = scr, themeForced = ClimbTheme.Sweet };
        ga.Start();
        Expect(ga.Running && ga.Placed.Look == ClimbLook.A4 && ga.Theme == ClimbTheme.Vine && ga.View!.Skin == null, "A4 는 늘 덩굴");
        ga.Stop(true);
        var gb = new ClimbGame(host) { FakeOpen = pn => Session.Fake(pn, null, () => true), FakeWindows = () => new List<Win> { win }, FakeProbe = h => new WinState(true, false, win.R), FakeScreens = scr, themeForced = ClimbTheme.Sweet };
        gb.Command(new[] { "climb", "seed", "1" });
        gb.Start();
        Expect(gb.Running && gb.Placed.Look == ClimbLook.A2 && gb.Theme == ClimbTheme.Sweet, "덩굴 말고 정한 테마는 A4 대신 A2");
        gb.Stop(true);

        string draws = CheckThemeDraw();
        string sinks = CheckSink();
        string path = CheckPath();
        string ring = CheckRing();
        return $"고르기 {string.Join("/", count)} · 그리기({draws}) · 가라앉음({sinks}) · 길 읽기({path}) · 잎 고리({ring})";
    }

    // ── 모든 테마가 모든 모습을 예외 없이 그리고, 판 밖에 안 그린다 ──
    static readonly Shot[] AllShots = { Shot.Mid, Shot.Stand1, Shot.Stand2, Shot.Stand3, Shot.FallTime, Shot.ResultTime, Shot.Save, Shot.Weak, Shot.Combo, Shot.CafeIn, Shot.Fall, Shot.Result, Shot.NewBest };

    // 판 둘레 80칸 안에 판 밖 그림이 없는지 (바탕을 보랏빛으로 칠해 두고 그린 뒤 판 밖에 그 색이 아닌 것이 있으면 틀림)
    static void OffPanel(SceneHost host, ClimbGame game, Rectangle panel, string what)
    {
        using var ov = host.Overlay(game);
        using var bmp = new Bitmap(1920, 1080, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(255, 255, 0, 255));
            ov.Render(g);
        }
        var area = Rectangle.Intersect(Rectangle.Inflate(panel, 80, 80), new Rectangle(0, 0, 1920, 1080));
        var d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int outside = 0, inside = 0;
        try
        {
            var row = new int[bmp.Width];
            for (int y = area.Top; y < area.Bottom; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(d.Scan0 + y * d.Stride, row, 0, bmp.Width);
                for (int x = area.Left; x < area.Right; x++)
                {
                    bool bg = (row[x] & 0xFFFFFF) == 0xFF00FF;
                    if (panel.Contains(x, y)) { if (!bg) inside++; }
                    else if (!bg) outside++;
                }
            }
        }
        finally { bmp.UnlockBits(d); }
        Expect(outside == 0, $"{what}: 판 밖에 그림 {outside}점");
        Expect(inside > 500, $"{what}: 판 안에 그려진 것이 없다 ({inside}점)");
    }

    static string CheckThemeDraw()
    {
        var panel = new Rectangle(660, 300, 450, 528);
        int shots = 0, pics = 0;
        foreach (var th in AllThemes)
        {
            foreach (var shot in AllShots)
            {
                var host = new SceneHost("adult_sweet", 3);
                var (game, _) = Make(host, shot, ClimbLook.A2, panel, th, true);
                Expect(game.Running && game.Theme == th, $"{th}/{shot}: 판이 연다");
                OffPanel(host, game, panel, $"{th}/{shot}");
                game.Stop(true);
                shots++;
            }
            var h2 = new SceneHost("adult_sweet", 3);
            using var v = new ClimbView(h2.Sprites, "adult_sweet", ClimbLook.A2, 3, th);
            Expect(v.Theme == th && (th == ClimbTheme.Vine) == (v.Skin == null), $"{th}: 그림 묶음");
            foreach (var (name, px) in v.Gallery()) { Expect(px.W > 0 && px.H > 0 && px.P.Any(c => c >> 24 != 0), $"{th} {name} 이 빈 그림"); pics++; }
            if (v.Skin is { } sk)
            {
                // 알파는 0·255 뿐 (색 열쇠 창), 글자 없음은 소스 검사가 본다
                foreach (var (name, px) in v.Gallery()) Expect(px.P.All(c => c >> 24 is 0 or 255), $"{th} {name}: 알파가 0·255 가 아니다");
                Expect(sk.Pip.Length == 4 && sk.Band.All(b => b != null), $"{th}: 시간 조각 넷·띠");
            }
        }
        // 판 하나에 테마 하나: 덩굴이 아닌 판은 덩굴 그림을 안 만든다, 창 테두리(A4)는 테마를 줘도 덩굴
        var h3 = new SceneHost("adult_sweet", 3);
        using (var a4 = new ClimbView(h3.Sprites, "adult_sweet", ClimbLook.A4, 3, ClimbTheme.Balloon)) Expect(a4.Skin == null && a4.Theme == ClimbTheme.Vine, "A4 는 덩굴 그림");
        return $"5테마 × {AllShots.Length}가지 모습 판 밖 0점 · 그림 {pics}장";
    }

    // ── 가라앉는 줄: 서 있는 발판이 줄면 볼록이도 같이 (풍선·과자만), 한도 안 ──
    static string CheckSink()
    {
        var panel = new Rectangle(660, 300, 450, 528);
        var report = new List<string>();
        foreach (var th in AllThemes)
        {
            var sinks = new int[3];
            var stages = new[] { Shot.Stand1, Shot.Stand2, Shot.Stand3 };
            for (int i = 0; i < 3; i++)
            {
                var host = new SceneHost("adult_sweet", 3);
                var (game, t) = Make(host, stages[i], ClimbLook.A2, panel, th, false);
                sinks[i] = game.View!.PetSink(t);
                game.Stop(true);
            }
            Expect(sinks.All(s => s is >= 0 and <= ClimbSkin.MaxSink), $"{th} 가라앉는 줄 한도 {string.Join("·", sinks)}");
            Expect(sinks[0] <= sinks[1] && sinks[1] <= sinks[2], $"{th} 가라앉는 줄은 덜 싱싱할수록 는다 {string.Join("·", sinks)}");
            if (th is ClimbTheme.Vine or ClimbTheme.Desk or ClimbTheme.Block) Expect(sinks.All(s => s == 0), $"{th} 는 가라앉지 않는다 {string.Join("·", sinks)}");
            else Expect(sinks[2] > 0 && sinks[0] == 0, $"{th} 는 서 있는 동안 가라앉는다 {string.Join("·", sinks)}");
            if (ClimbSkin.Make(th) is { } sk) Expect(sk.MaxSinkSeen() <= ClimbSkin.MaxSink, $"{th} 발판 그림의 가라앉는 줄 한도");
            report.Add($"{th} {string.Join("/", sinks)}");
        }
        return string.Join(" · ", report);
    }

    // ── 길 읽기 (YELL 2026-11-26 「풍선쪽에 버그있는것 같은데? 중간에 길이 끊겼었어」): 테마 판(문구·과자·풍선·블록)에서 볼록이 앞 잎이 늘 보이고, 길이 아닌 것이 발판처럼 안 보인다 ──
    // 앞 세 잎은 판 안에 다 들고(발판 그림 전부) 이웃 발판·장식에 덮이지 않고 보이며(볼록이는 뺀다), 넷째 잎도 대부분 판 안이다.
    // 씨 549356045(그 판) 포함
    static string CheckPath()
    {
        var panel = new Rectangle(660, 300, 450, 528);
        int shots = 0;
        double minSeen = 9, minIn4 = 9;
        foreach (var th in AllThemes.Skip(1))
        {
            foreach (int seed in new[] { 549356045, 1, 2, 3, 4, 5 })
            {
                var host = new SceneHost("adult_sweet", 3);
                var game = NewGame(host);
                var t = new Climb(seed);
                game.Begin(t, ClimbLook.A2, panel, Session.Fake(panel, null, () => true), host.Pet.P.Stage, th);
                var v = game.View!;
                v.ProbeOn();
                using var ov = host.Overlay(game);
                using var bmp = new Bitmap(1920, 1080); using var fg = Graphics.FromImage(bmp);
                var rr = new List<Rectangle>(8);
                for (int i = 0; i < 20; i++) game.Frame(1 / 60.0);
                while (t.N < 230 && !t.Done)
                {
                    t.Bar = 1; t.GraceLeft = 99; t.Steps = Math.Max(t.Steps, 5);
                    t.Press(t.At(t.N + 1).Dir);
                    for (int i = 0; i < 14 || t.SaveLeft > 0 || v.VN < t.N - 1e-6; i++) game.Frame(1 / 60.0);
                    rr.Clear(); game.Dirty(ov, rr); ov.Render(fg);
                    for (int k = 1; k <= 4; k++)
                    {
                        if (t.Hidden(t.N + k)) { Expect(k >= t.T.CloudHide, $"{th}: 앞 {k}잎이 구름에 가렸다"); continue; }
                        var (inside, seenFrac) = v.StepSeen(t, t.N + k);
                        if (k <= 3)
                        {
                            Expect(inside >= .999, $"{th} 씨 {seed} 잎 {t.N}: 앞 {k}잎 발판이 판 밖으로 나감 (판 안 {inside:0.00})");
                            Expect(seenFrac >= .8, $"{th} 씨 {seed} 잎 {t.N}: 앞 {k}잎 발판이 가려짐 (보이는 {seenFrac:0.00})");
                            minSeen = Math.Min(minSeen, seenFrac);
                        }
                        else { Expect(inside >= .7, $"{th} 씨 {seed} 잎 {t.N}: 앞 4잎 발판이 판 밖 (판 안 {inside:0.00})"); minIn4 = Math.Min(minIn4, inside); }
                    }
                    shots++;
                }
                Expect(t.RingTrips == 0, $"{th} 씨 {seed}: 먼 잎을 읽어 고리를 건드렸다 ({t.RingTrips}번)");
                game.Stop(true);
            }
        }
        return $"4테마 × 6씨 {shots}장면: 앞 세 잎 판 안·가려짐 없음(가장 낮은 보임 {minSeen:0.00}) · 넷째 잎 판 안 {minIn4:0.00} 이상";
    }

    // ── 잎 고리: 멀리 앞·뒤 잎을 읽어도(최고 기록 잎·먼 자리) 길이 안 바뀐다. 바뀌면 잎 108 이 잎 300 처럼 보인다 (이정표 108, 열매 사라짐) ──
    static string CheckRing()
    {
        int checks = 0, trips = 0;
        foreach (int seed in new[] { 549356045, 11, 12, 13, 14 })
        {
            var re = new Climb(seed);
            var want = new (sbyte, ClimbKind, bool, int)[800];
            for (int i = 1; i < 800; i++) { ref var l = ref re.At(i); want[i] = (l.Dir, l.Kind, l.Fog, l.Col); }
            var t = new Climb(seed) { Best = 349, Guard = true };
            var rn = new Random(seed ^ 9);
            for (int step = 0; step < 60000 && !t.Done && t.N < 600; step++)
            {
                int r = rn.Next(12);
                if (r < 3) t.Update(rn.NextDouble() * .05);
                else if (r < 11) t.Press(t.At(t.N + 1).Dir);
                else { t.Puff = true; t.Press(-t.At(t.N + 1).Dir); }
                t.Bar = 1; t.WiltLeft = -1;
                t.At(349); t.At(t.N + 300); t.At(t.N - 300); t.At(t.N + 57);   // 최고 기록 잎·먼 앞·먼 뒤: 막혀야 한다
                for (int i = Math.Max(1, t.N - 3); i <= t.N + 19; i++)
                {
                    ref var l = ref t.At(i);
                    Expect((l.Dir, l.Kind, l.Fog, l.Col) == want[i], $"씨 {seed}: 먼 잎을 읽고 잎 {i} 이 바뀜 (N={t.N})");
                    checks++;
                }
            }
            Expect(t.RingTrips > 0, "먼 읽기가 막힌 적이 없다 (Guard 가 안 켜졌다)");
            trips += t.RingTrips;
            Expect(t.N >= 300, $"씨 {seed}: 300잎까지 오르지 못함 ({t.N})");
        }
        // Guard 가 꺼져 있으면 고리가 실제로 덮인다 (이 검사가 그 버그를 잡는지)
        var bad = new Climb(549356045);
        for (int i = 1; i <= 100; i++) { bad.Press(bad.At(bad.N + 1).Dir); bad.Bar = 1; }
        var before = bad.At(108).Kind;
        bad.At(349);
        Expect(before != ClimbKind.Flower && bad.At(108).Kind == ClimbKind.Flower, "Guard 없이 먼 잎을 읽으면 잎 108 이 잎 300(이정표)이 된다 (고리 덮임 재현)");
        return $"5씨 × 먼 읽기 {trips}번 막힘 · 잎 {checks} 개가 늘 같음 · Guard 없이는 잎 108 이 잎 300 처럼 바뀜";
    }
}
