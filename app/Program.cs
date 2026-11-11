namespace Bolok;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--selfcheck"))
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            return Sim.Check(Console.Out);
        }

        if (args.Length == 2 && args[0] == "--settings") // 설정 창 페이지들을 그림으로 (화면 밖 먼 곳에 잠깐 띄워 그린다). 마지막은 풀어주기 확인
        {
            ApplicationConfiguration.Initialize();
            var sp = Sprites.Embedded();
            var book = new PetBook("hamster", false, new[] { "hamster", "fox", "penguin", "slime" },
                new[] { new ReleasedPet("hamster", "adult_chubby", 2900, new DateTime(2026, 11, 11)), new ReleasedPet("penguin", "adult_sweet", 2500, new DateTime(2026, 11, 12)) });
            var status = new PetStatus("hamster", "child", 200, 72, 88, 18, false, "sleepy", 1, 2, 5, 2080, .06, "어른", new[]
            {
                new GameStat("달리기", "Lv.3 · 최고 1,240점", true), new GameStat("벽돌깨기", "최고 86조각", true),
                new GameStat("포트리스", "아직 못 이겼어요", false), new GameStat("총쏘기", "최고 15번 맞혔어요", true),
            });
            using var f = new Settings(false, false, () => sp, () => status, book) { StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
            f.Show();
            Application.DoEvents();
            var shots = new List<Bitmap>();
            foreach (var name in Settings.Pages.Append("풀어주기").Append("상태 아래"))
            {
                if (name == "풀어주기") { f.SelectTab("도감"); f.AskRelease(); }
                else if (name == "상태 아래") { f.SelectTab("상태"); f.ScrollToEnd(); }
                else f.SelectTab(name);
                Application.DoEvents();
                var page = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(page, new Rectangle(Point.Empty, f.Size));
                shots.Add(page);
            }
            using var bmp = new Bitmap((f.Width + 20) * 3 - 20, (f.Height + 20) * 2 - 20);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int i = 0; i < shots.Count; i++) g.DrawImage(shots[i], i % 3 * (f.Width + 20), i / 3 * (f.Height + 20));
            }
            foreach (var s in shots) s.Dispose();
            f.Close();
            bmp.Save(args[1]);
            return 0;
        }

        if (args.Length == 2 && args[0] == "--pets-check") // 이 exe 가 받을 캐릭터 그림이 저장소(자기 태그)에 다 있고 목록과 맞는지 (릴리스 워크플로가 돌린다)
        {
            var ps = new PetSheets(args[1], null, PetSheets.RefFor(Updater.Current, Updater.IsDevBuild));
            int ok = 0;
            foreach (var id in ps.Manifest.Keys)
                if (ps.Fetch(id).GetAwaiter().GetResult() && ps.Read(id) != null) ok++;
                else Console.WriteLine($"못 받음: {id} ({ps.Url(id + ".png")})");
            Console.WriteLine($"캐릭터 그림 {ok}/{ps.Manifest.Count} ({ps.GitRef})");
            return ok == ps.Manifest.Count ? 0 : 1;
        }

        if (args.Length == 2 && args[0] == "--gun") // 총과 불꽃을 여러 각도로 그려 본다 (화면을 띄우지 않는다)
        {
            var sp = Sprites.Embedded();
            using var bmp = new Bitmap(1500, 790);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(150, 160, 170));
                double[] angles = { -.25, -.8, -1.57, -2.3, -2.9 };
                for (int i = 0; i < angles.Length; i++)
                {
                    GunArt.Draw(g, sp, "pistol", new PointF(150 + i * 300, 230), angles[i], 0, 6, 0);
                    GunArt.Draw(g, sp, "mg", new PointF(150 + i * 300, 500), angles[i], 0, 6, 1);
                    GunArt.Draw(g, sp, "shotgun", new PointF(150 + i * 300, 770), angles[i], 0, 6, 0);
                }
            }
            bmp.Save(args[1]);
            return 0;
        }

        if (args.Length == 2 && args[0] == "--icbm-scene") // ICBM 이 떨어지는 장면과 구멍(크기별)·빼꼼 장면 (화면을 띄우지 않는다)
        {
            var sp = Sprites.Embedded();
            const int k = 3;
            var ic = new Icbm(new List<Rectangle> { new(0, 0, 1920, 1080) }, 1500, 1000, k, new Random(2)) { Friends = new[] { "hamster" } };
            ic.Launch();
            using var bmp = new Bitmap(1920 * 2, 1080);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(150, 160, 170));
            using var rim = new SolidBrush(Color.FromArgb(70, 60, 52));
            using var hole = new SolidBrush(Color.FromArgb(14, 16, 22));
            foreach (double t in new[] { .2, .55, .95 }) // 떨어지는 세 순간을 한 장에
            {
                while (!(ic.Stage == Icbm.Phase.Fall && ic.FallT >= t)) ic.Update(1 / 60.0, null);
                Overlay.PaintMissile(g, sp, ic, new Point((int)ic.MX, (int)ic.MY), k);
            }
            while (ic.Craters.Count == 0) ic.Update(1 / 60.0, null);
            var cr = ic.Craters[0];
            g.TranslateTransform(1920, 0);
            foreach (var (dx, rise) in new[] { (-600, .3), (-200, 1.2), (200, 2.5) }) // 올라오는 중, 다 올라옴, 내려가는 중
            {
                var c = new Icbm.Crater { X = 960 + dx, Y = 380, R0 = cr.R0, R = cr.R0 };
                Overlay.PaintCrater(g, c, new Point((int)c.X, (int)c.Y), rim, hole);
                Overlay.PaintPeek(g, sp, new Icbm.Peek { Hole = c, Species = "hamster", At = 0 }, rise, new Point((int)c.X, (int)c.Y), k);
            }
            foreach (var (dx, frac) in new[] { (-600, 1.0), (-200, .66), (200, .33), (560, 1.0) }) // 고치며 줄어드는 구멍, 다른 자리의 구멍
            {
                var c = new Icbm.Crater { X = 960 + dx + (frac == 1 && dx > 0 ? 37 : 0), Y = 820, R0 = cr.R0, R = Math.Ceiling(cr.R0 * frac / Icbm.Cell) * Icbm.Cell };
                Overlay.PaintCrater(g, c, new Point((int)c.X, (int)c.Y), rim, hole);
            }
            g.ResetTransform();
            bmp.Save(args[1]);
            return 0;
        }

        if (args.Length == 3 && args[0] == "--scene") // 창 위 소품만 그려 본다 (화면을 찍지 않는다)
        {
            var sp = Sprites.Embedded();
            var area = new Rectangle(0, 0, 1920, 1080);
            var rng = new Random(4);
            var solid = Props.All.ToDictionary(n => n, n => Props.Solid(sp.Get(n, 0, 1)));
            var fakeWins = new[] { new Win(new IntPtr(1), new Rectangle(200, 300, 800, 500), false), new Win(new IntPtr(2), new Rectangle(1150, 180, 600, 420), false) };
            var props = Props.OnWindows(fakeWins, new[] { area }, Props.Sets[args[1]], rng, n => solid[n], Array.Empty<Rectangle>());
            using var bmp = new Bitmap(1920, 1080);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(150, 160, 170));
                using var frame = new SolidBrush(Color.FromArgb(225, 228, 232));
                foreach (var w in fakeWins) g.FillRectangle(frame, w.R);
                foreach (var p in props) g.DrawImageUnscaled(sp.Get(p.Name, 0, p.Scale), p.Origin);
            }
            bmp.Save(args[2]);
            return 0;
        }

        // 모르는 옵션이면 볼록이를 띄우지 않고 끝낸다 (그림 확인용 옵션을 옛 빌드에 줬다가 볼록이가 하나 더 뜬 적이 있다)
        string[] known = { "--debug", "--after" };
        if (args.FirstOrDefault(x => x.StartsWith("--") && !known.Contains(x)) is { } unknown)
        {
            Console.Error.WriteLine($"모르는 옵션: {unknown}");
            return 2;
        }
        bool debug = args.Contains("--debug");
        int after = Array.IndexOf(args, "--after"); // 업데이트 뒤 다시 시작: 앞 프로세스가 끝나길 기다린다
        if (after >= 0 && after + 1 < args.Length && int.TryParse(args[after + 1], out int pid))
            try { System.Diagnostics.Process.GetProcessById(pid).WaitForExit(8000); } catch { /* 이미 끝났다 */ }
        // 볼록이는 한 번에 하나만: 디버그로 켜든 받은 exe 로 켜든 같은 잠금을 쓴다 (따로 쓰다 둘이 동시에 뜬 적이 있다, 2026-11-11)
        using var one = new Mutex(true, @"Local\Bolok", out bool first);
        if (!first) return 0;

        ApplicationConfiguration.Initialize();

        // 디버그 때 exe 는 run/bin-a 나 run/bin-b 에 있다 (tools/dev.sh)
        string? run = debug ? Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('\\')) : null;
        string? assets = run == null ? null : Path.Combine(Path.GetDirectoryName(run)!, "assets");
        string data = run ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Bolok");
        Directory.CreateDirectory(data);

        if (debug) Dbg.Start(run!);
        // 일반 실행에서도 프레임 하나가 실패했다고 앱이 죽으면 안 된다
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Dbg.Error(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Dbg.Log("fatal", ("msg", e.ExceptionObject.ToString()));

        var sprites = assets != null ? Sprites.FromDir(assets) : Sprites.Embedded();
        var tuning = run != null ? DevLink.LoadTuning(run) : new Tuning();
        // 받은 캐릭터 그림: 릴리스는 %LOCALAPPDATA%\Bolok\pets, 개발은 assets 를 바로 읽는다
        var pets = new PetSheets(Path.Combine(run ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bolok"), run != null ? "pets-cache" : "pets"),
            assets, PetSheets.RefFor(Updater.Current, Updater.IsDevBuild));
        pets.Cleanup();
        Application.Run(new PetForm(sprites, pets, tuning, data, run, assets));
        return 0;
    }
}
