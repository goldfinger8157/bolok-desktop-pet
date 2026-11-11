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

        if (args.Length == 2 && args[0] == "--settings") // 설정 창 네 탭을 그림으로 (화면 밖 먼 곳에 잠깐 띄워 그린다)
        {
            ApplicationConfiguration.Initialize();
            var book = new PetBook("fox", false, new[] { "hamster", "fox", "penguin" }, new[] { new ReleasedPet("hamster", "adult_chubby", 2900, new DateTime(2026, 11, 11)) });
            using var f = new Settings(false, false, Sprites.Embedded("fox"), book, () => "볼록이 · 여우 어린이    3시간 20분\n포만 ♥♥♥♡\n기분 ♥♥♥♥\n기운 ♥♥♡♡\n실수 0    볼 속 0입")
                { StartPosition = FormStartPosition.Manual, Location = new Point(-30000, -30000) };
            f.Show();
            Application.DoEvents();
            var tabs = (TabControl)f.Controls[0];
            using var bmp = new Bitmap(f.Width * 4 + 60, f.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                for (int i = 0; i < 4; i++)
                {
                    tabs.SelectedIndex = i;
                    Application.DoEvents();
                    using var page = new Bitmap(f.Width, f.Height);
                    f.DrawToBitmap(page, new Rectangle(Point.Empty, f.Size));
                    g.DrawImage(page, i * (f.Width + 20), 0);
                }
            }
            f.Close();
            bmp.Save(args[1]);
            return 0;
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
        Application.Run(new PetForm(sprites, tuning, data, run, assets));
        return 0;
    }
}
