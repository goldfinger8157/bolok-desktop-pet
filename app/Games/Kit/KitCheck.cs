using System.Text.Json;

namespace Bolok;

// 공통 도구(Kit) 자가 검사와 그려 보기 (--selfcheck, --mode-scene kit <그림.png>)
static class KitCheck
{
    static void Expect(bool ok, string what) { if (!ok) throw new Exception("자가 검사 실패: " + what); }

    // 옮기기 전 ICBM 구멍 모양 (옛 Overlay.CraterShape 그대로). Ragged 가 같은 모양을 내는지 견준다. 그리는 쪽이 그 모양을 쓰는지는 ICBM 모듈이 검사한다
    const int IcbmCell = 12;
    static (List<(int, int, bool)> Cells, List<(int, int)> Cracks) OldCrater(double cx, double cy, double R, double R0, int c)
    {
        var cells = new List<(int, int, bool)>();
        var cracks = new List<(int, int)>();
        var rng = new Random((int)(cx * 7 + cy * 13));
        double p3 = rng.NextDouble() * 6.3, p5 = rng.NextDouble() * 6.3, p7 = rng.NextDouble() * 6.3, p11 = rng.NextDouble() * 6.3;
        double F(double a) => 1 + .13 * Math.Sin(3 * a + p3) + .09 * Math.Sin(5 * a + p5) + .07 * Math.Sin(7 * a + p7) + .06 * Math.Sin(11 * a + p11);
        int n = (int)Math.Ceiling(R * 1.4 / c) + 1;
        for (int j = -n; j <= n; j++)
            for (int i = -n; i <= n; i++)
            {
                double x = (i + .5) * c, y = (j + .5) * c, d = Math.Sqrt(x * x + y * y), r = R * F(Math.Atan2(y, x));
                if (d < r - c) cells.Add((i, j, true));
                else if (d < r) cells.Add((i, j, false));
            }
        int h = c / 2, count = 3 + rng.Next(3);
        for (int m = 0; m < count; m++)
        {
            double a = rng.NextDouble() * Math.PI * 2, r = R * F(a) - h, side = 0;
            int len = (int)((5 + rng.Next(6)) * R / Math.Max(R0, 1));
            for (int t = 0; t < len; t++, r += h)
            {
                side = Math.Clamp(side + (rng.NextDouble() - .5) * 1.2, -1.5, 1.5);
                double x = Math.Cos(a) * r - Math.Sin(a) * side * h, y = Math.Sin(a) * r + Math.Cos(a) * side * h;
                cracks.Add(((int)Math.Floor(x / h) * h, (int)Math.Floor(y / h) * h));
            }
        }
        return (cells, cracks);
    }

    public static string Check()
    {
        // ── WindowPick.Arena: 시합장 창 하나 (가린 창·최대화·맨 위 고정·작은 창은 빼고 가장 넓은 것, 손잡이까지) ──
        {
            var field = new Rectangle(0, 0, 1920, 1040);
            var screens = new[] { field };
            Win W(int h, int x, int y, int w, int hh, bool top = false) => new(new IntPtr(h), new Rectangle(x, y, w, hh), top);
            var cover = W(1, 900, 500, 300, 200);           // 맨 앞 (z 순서 첫째)
            var under = W(2, 800, 400, 700, 500);           // cover 에 가린다
            var small = W(3, 100, 300, 200, 150);           // 너무 작다
            var pin = W(4, 100, 600, 700, 380, true);       // 맨 위 고정
            var good = W(5, 1500, 200, 400, 300);
            var max = W(6, 0, 0, 1920, 1040);
            var (f, arena) = WindowPick.Arena(null, new[] { cover, under, small, pin, good, max }, field, screens, 120, 360, 240);
            Expect(f == field && arena is { } g && g.H == good.H, $"시합장 창: 가린·작은·고정·최대화 창은 빼고 쓸 만한 것 ({arena?.H})");
            Expect(WindowPick.Arena(null, new[] { max, small }, field, screens, 120, 360, 240).Arena == null, "쓸 만한 창이 없으면 null");
            var used = W(7, 100, 150, 500, 300);       // 메뉴 열기 전 쓰던 창 (good 보다 좁아도 이 창)
            var top = W(8, 1000, 20, 800, 400);        // 화면 위쪽에 붙은 창도 된다 (창 위 여유 기본 0)
            Expect(WindowPick.Arena(used, new[] { used, good }, field, screens, 120, 360, 240).Arena?.H == used.H, "쓰던 창이 되면 그 창");
            Expect(WindowPick.Arena(null, new[] { top }, field, screens, 120, 360, 240).Arena?.H == top.H, "위쪽에 붙은 창도 시합장이 된다");
        }
        // ── Ragged: 반지름 배수 0.65~1.35, 넓이는 동그라미 ±5%, 같은 씨 같은 모양, ICBM 구멍은 전과 같은 모양 ──
        double lo = 9, hi = 0, worstArea = 0;
        for (int seed = 0; seed < 40; seed++)
        {
            var s = Ragged.Seeded(seed * 37 + 1);
            double sum = 0;
            const int steps = 720;
            for (int i = 0; i < steps; i++)
            {
                double k = s.K(i * Math.PI * 2 / steps);
                (lo, hi, sum) = (Math.Min(lo, k), Math.Max(hi, k), sum + k * k);
            }
            worstArea = Math.Max(worstArea, Math.Abs(sum / steps - 1));
            Expect(s.Inside(0, 0, 10) && !s.Inside(14, 0, 10) && !s.Inside(0, -14, 10), "가운데는 안, 1.35배 밖은 밖");
        }
        Expect(lo >= .65 && hi <= 1.35, $"반지름 배수는 0.65~1.35: {lo:0.00}~{hi:0.00}");
        Expect(worstArea < .05, $"넓이는 동그라미와 5% 안: {worstArea:P1}");
        var a = Ragged.At(1210, 404);
        Expect(a.Cells(60, 12).SequenceEqual(Ragged.At(1210, 404).Cells(60, 12)) && a.Cracks(60, 108, 6).SequenceEqual(Ragged.At(1210, 404).Cracks(60, 108, 6)), "같은 자리는 같은 모양");
        Expect(!a.Cells(60, 12).SequenceEqual(Ragged.At(700, 300).Cells(60, 12)), "다른 자리는 다른 모양");
        int same = 0;
        foreach (var (x, y, r, r0) in new[] { (360.0, 380.0, 108.0, 108.0), (1521.5, 820.25, 72.0, 108.0), (997.0, 820.0, 36.0, 108.0), (-300.5, 77.0, 108.0, 108.0), (2500.0, 1300.0, 24.0, 90.0) })
        {
            var (cells, cracks) = OldCrater(x, y, r, r0, IcbmCell);
            var now = Ragged.At(x, y);
            Expect(cells.SequenceEqual(now.Cells(r, IcbmCell)) && cracks.SequenceEqual(now.Cracks(r, r0, IcbmCell / 2)), $"ICBM 구멍 모양이 전과 같다: ({x}, {y}) r {r}");
            int inside = cells.Count(c => now.Inside((c.Item1 + .5) * IcbmCell, (c.Item2 + .5) * IcbmCell, r));
            Expect(inside == cells.Count, "그려지는 칸은 모두 규칙의 폭발 안이다");
            same++;
        }

        // ── BlastArt: 불덩이는 크기·모양·장마다 한 번만 그리고, 다시 칠할 자리가 그림을 다 덮는다 ──
        var b0 = BlastArt.Burst(2, 1, 20, 2);
        Expect(ReferenceEquals(b0, BlastArt.Burst(2, 1, 20, 2)) && ReferenceEquals(b0, BlastArt.Burst(10, 1, 20, 2)), "같은 불덩이는 다시 그리지 않는다");
        Expect(!ReferenceEquals(b0, BlastArt.Burst(3, 1, 20, 2)) && !ReferenceEquals(b0, BlastArt.Burst(2, 2, 20, 2)), "모양·장이 다르면 다른 그림");
        var rect = BlastArt.PopRect(500, 400, 20, 2);
        Expect(rect.Width == b0.Width && rect.Height == b0.Height && rect.X + rect.Width / 2 == 500, $"다시 칠할 자리 = 그림 크기: {rect.Width}×{rect.Height} · {b0.Width}×{b0.Height}");
        Expect(BlastArt.Frame(0) == 0 && BlastArt.Frame(BlastArt.Sec * .99) == BlastArt.Frames - 1 && BlastArt.Frame(9) == BlastArt.Frames - 1, "다섯 장이 0.45초에 지나간다");
        Expect(BlastArt.Variant(120.5, 88) == BlastArt.Variant(120.5, 88) && Enumerable.Range(0, 200).Select(i => BlastArt.Variant(i * 13.7, i * 5.1)).Distinct().Count() == BlastArt.Variants, "모양은 자리로 정해지고 여덟 가지가 다 나온다");

        // ── Session: 읽는 키는 방향키·Ctrl·Shift 뿐. 방향키는 판이 앞 창일 때만. 마우스 버튼은 누른 프레임을 한 번만 알린다 ──
        var down = new HashSet<Keys> { Keys.A, Keys.D, Keys.W, Keys.S, Keys.Space, Keys.Tab, Keys.Q, Keys.R };
        var asked = new HashSet<Keys>();
        bool front = true;
        var mouse = new Point(100, 100);
        var ses = Session.Fake(new Rectangle(0, 0, 400, 300), k => { asked.Add(k); return down.Contains(k); }, () => front, () => mouse);
        ses.Tick();
        Expect(ses.AxisX == 0 && ses.AxisY == 0 && !ses.Ctrl && !ses.Shift, "글자 키·Space·Tab 은 조작이 아니다");
        Expect(asked.All(k => Session.Allowed.Contains(k)) && !Session.Allowed.Any(k => k is Keys.Escape or Keys.Space or Keys.Tab || k is >= Keys.A and <= Keys.Z), $"읽는 키는 방향키·Ctrl·Shift 뿐: {string.Join(" ", asked)}");
        Expect(!ses.MouseMoved, "처음 커서 자리는 움직인 걸로 치지 않는다");
        down.UnionWith(new[] { Keys.Left, Keys.Up, Keys.ShiftKey });
        ses.Tick();
        Expect(ses.AxisX == -1 && ses.AxisY == -1 && ses.Up && ses.Shift && !ses.Ctrl, "판이 앞 창이면 방향키를 읽는다");
        front = false;
        mouse = new Point(130, 100);
        ses.Tick();
        Expect(ses.AxisX == 0 && ses.AxisY == 0 && !ses.Up && !ses.Active, "다른 창을 쓰는 동안 방향키는 안 읽는다");
        Expect(ses.Shift && ses.MouseMoved && ses.Mouse == mouse, "Shift·마우스는 어느 창에서든 읽는다");
        down.Add(Keys.Right);
        front = true;
        ses.Tick();
        Expect(ses.AxisX == 0 && !ses.MouseMoved, "← → 를 같이 누르면 서 있는다");
        ses.Press(MouseButtons.Left, true);
        ses.Tick();
        Expect(ses.LeftHit && ses.Left && !ses.LeftUp && !ses.RightHit, "누른 프레임");
        ses.Tick();
        Expect(!ses.LeftHit && ses.Left, "누르고 있는 동안은 Left 만");
        ses.Press(MouseButtons.Left, false);
        ses.Press(MouseButtons.Right, true);
        ses.Press(MouseButtons.Right, false);
        ses.Tick();
        Expect(ses.LeftUp && !ses.Left && ses.RightHit && ses.RightUp && !ses.Right, "뗀 프레임, 한 프레임 안에 눌렀다 뗀 것도 놓치지 않는다");
        ses.Tick();
        Expect(!ses.LeftUp && !ses.RightHit && !ses.RightUp, "다음 프레임엔 지워진다");
        var was = L.Code;
        L.Use("ko");
        ses.Guide("note.raid_move", "note.esc");
        L.Use(was);
        Expect(ses.GuideOn(0) && ses.GuideOn(Session.GuideSec - .1) && !ses.GuideOn(Session.GuideSec + .1), "조작 안내는 4초");

        // 보스 키로 끝내면(quit) 표정·별 없이 바로 하던 대로, 제대로 끝나면 기뻐한다
        var brain = new Brain(new Pet { Stage = "child" }, new Tuning(), SceneHost.Area);
        brain.StartPuppet();
        (brain.PuppetMs, brain.PuppetBlink) = (60, () => true);
        Expect(brain.Blink, "게임이 정한 깜빡임");
        ses.Close(brain, quit: true, won: true, "kit");
        Expect(ses.Closed && brain.Mode == Mode.Wander && !brain.Emoting && brain.PuppetMs == 0 && !brain.Blink, "보스 키로 끝내면 티가 나지 않는다");
        brain.StartPuppet();
        Session.Fake(default).Close(brain, quit: false, won: true, "kit");
        Expect(brain.Emoting && brain.Mode == Mode.Anim, "끝까지 하면 기뻐한다");

        // ── Watch: 값이 바뀐 프레임만 (처음 두 프레임은 늘) ──
        var w = new Watch();
        w.Set(5); bool f1 = w.Changed && w.Fresh;
        w.Set(5); bool f2 = w.Changed && w.Fresh;
        w.Set(5); bool f3 = w.Changed || w.Fresh;
        w.Set(6); bool f4 = w.Changed && !w.Fresh;
        Expect(f1 && f2 && !f3 && f4, "표시는 값이 바뀐 프레임에만 다시 칠한다");
        Expect(GameHud.Digits(0) == 1 && GameHud.Digits(9) == 1 && GameHud.Digits(10) == 2 && GameHud.Digits(16850) == 5, "숫자 자릿수");

        // ── 소리 표: 모든 사건 소리가 만들어진다 (새 소리 pick·hurt 포함) ──
        foreach (Sound s in Enum.GetValues<Sound>())
        {
            var wav = Sfx.Make(Snd.Name(s));
            Expect(wav.Length > 44 + 400 && wav.Skip(44).Any(v => v != 0), $"소리가 난다: {s}");
        }

        // ── 옛 기록 칸 → 모듈 기록: 있던 것만 옮기고, 이미 모듈 기록이 있으면 그것이 먼저 ──
        var save = new SaveData { RunBest = 1240, RunLast = 300, RunLevel = 3, BreakBest = 86, GameBest = 15, RunScores = new() { new RunRecord(new DateTime(2026, 11, 1), 2, 300, 12) } };
        save.GameData["shoot"] = "{\"Best\":99}";
        save.GameData["raid"] = "{\"Best\":5,\"Stage\":2}";
        var data = GameModes.WithLegacy(save);
        using (var run = JsonDocument.Parse(data["run"]))
            Expect(run.RootElement.GetProperty("Best").GetInt32() == 1240 && run.RootElement.GetProperty("Level").GetInt32() == 3 && run.RootElement.GetProperty("Scores").GetArrayLength() == 1, "달리기 기록이 옮겨진다");
        Expect(data["breakout"] == "{\"Best\":86}" && !data.ContainsKey("fortress") && data["shoot"] == "{\"Best\":99}" && data["raid"] == save.GameData["raid"], "없던 기록은 안 만들고, 모듈 기록이 먼저");
        Expect(GameModes.WithLegacy(new SaveData()).Count == 0, "새로 시작하면 옮길 것이 없다");

        return $"게임 공통 도구: 폭발 모양 배수 {lo:0.00}~{hi:0.00}·넓이 차 {worstArea:P1}, ICBM 구멍 {same}개 전과 같음, 조작 키 {Session.Allowed.Length}개만 읽음";
    }

    // ── 그려 보기: 찢긴 구멍(크기·자리별), 불덩이 여덟 모양 × 다섯 장(크기 둘), 표시(점수·하트·별·막대·3·끝 상자 둘), 파편·연기·불꽃 ──
    sealed class Demo : IGameMode
    {
        public string Id => "kit";
        public string? MenuKey => null;
        public bool Running => true;
        public void Start() { }
        public void Frame(double dt) { }
        public void Stop(bool quit) { }
        public void Dirty(Overlay o, List<Rectangle> r) { }
        public bool Command(string[] a) => false;
        public IEnumerable<GameStat> Stats() => Array.Empty<GameStat>();
        public string? SaveState() => null;
        public void LoadState(string? json) { }

        public void Draw(Overlay o, Graphics g)
        {
            const int k = 3;
            // 구멍: 같은 자리 세 크기, 다른 자리 둘
            foreach (var (x, y, r) in new[] { (140, 140, 108.0), (400, 140, 72.0), (560, 140, 36.0), (760, 140, 90.0), (1000, 140, 60.0) })
                BlastArt.Hole(g, x < 600 ? Ragged.At(140, 140) : Ragged.At(x, y), new Point(x, y), r, 108, 12, Pal.Soot, Pal.Hole);
            // 불덩이: 줄마다 모양, 칸마다 장. 왼쪽은 작은 벌레가 터질 때 크기, 오른쪽은 큰 것
            for (int v = 0; v < BlastArt.Variants; v++)
                for (int f = 0; f < BlastArt.Frames; f++)
                {
                    var small = BlastArt.Burst(v, f, 18, 2);
                    g.DrawImageUnscaled(small, 60 + f * 70 - small.Width / 2, 330 + v * 84 - small.Height / 2);
                    if (v is not (0 or 5)) continue;
                    var big = BlastArt.Burst(v, f, 44, 3);
                    g.DrawImageUnscaled(big, 500 + f * 150 - big.Width / 2, (v == 0 ? 350 : 500) - big.Height / 2);
                }
            // 표시: 작은 판 둘 (게임 중, 끝·새 기록), 그 옆에 끝·기록 못 깸, 3·2·1
            var a = new Rectangle(1300, 40, 360, 480);
            var b = new Rectangle(1300, 560, 360, 480);
            var c = new Rectangle(900, 600, 360, 240);
            GameHud.Corners(o, g, a, k); GameHud.Score(o, g, a, k, 16850); GameHud.Hearts(o, g, a, k, 5); GameHud.Stage(o, g, a, k, 12);
            GameHud.Star(o, g, a.Left + 60, a.Top + 120, 2, 7); GameHud.Bar(o, g, new Rectangle(a.Left + 40, a.Top + 140, 78, 6), k, .6, Pal.Cyan);
            GameHud.Bar(o, g, new Rectangle(a.Left + 160, a.Top + 140, 120, 12), k, .35, Pal.Gold);
            GameHud.Count(o, g, new Rectangle(a.Left, a.Top + 240, 360, 240), k, 3);
            GameHud.Corners(o, g, b, k); GameHud.Score(o, g, b, k, 80); GameHud.Hearts(o, g, b, k, 2); GameHud.Result(o, g, b, k, 16850, 9000, true);
            GameHud.Result(o, g, new Rectangle(b.Left, b.Top + 200, 360, 280), k, 479, 512, false, "game/bug");
            GameHud.Corners(o, g, c, k); GameHud.Result(o, g, c, k, 1450, 16850, false);
            // 파편·연기·불꽃, 작은 볼록이 (보통·다쳤을 때)
            for (int i = 0; i < 6; i++) { Fx.Chip(g, 930 + i * 30, 900, 6 + i * 3); Fx.Puff(g, 930 + i * 30, 940, 18, .1 + i * .1); Fx.Ember(g, 930 + i * 30, 980, 6 + i * 2, i / 2); }
            Fx.Pet(o, g, o.Art, "child", "idle", 0, 2, 1160, 1000, false);
            Fx.Pet(o, g, o.Art, "child", "hurt", 0, 2, 1230, 1000, true);
        }
    }

    public static void Scene(string path)
    {
        var host = new SceneHost();
        using var ov = host.Overlay(new Demo());
        using var bmp = new Bitmap(1700, 1060);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(150, 160, 170));
        ov.Render(g);
        bmp.Save(path);
    }
}
