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

        if (args.Length is 2 or 3 && args[0] == "--settings") // 설정 창 페이지들을 그림으로 (화면 밖 먼 곳에 잠깐 띄워 그린다). 마지막은 풀어주기 확인
        {
            ApplicationConfiguration.Initialize();
            L.Use(args.Length == 3 ? args[2] : "ko"); // 세 번째 값: 언어 (en, ja, …)
            var sp = Sprites.Embedded("hamster#2"); // 지금 키우는 아이: 회색 햄스터 (작은 그림도 그 색인지 보려고)
            var mem = Path.Combine(Path.GetTempPath(), "bolok-render");
            Memories.Photo(sp, "egg/idle", "egg", Path.Combine(mem, "p1.png"));
            Memories.Photo(sp, "baby/0/happy", "baby", Path.Combine(mem, "p2.png"));
            Memories.Photo(sp, "adult_chubby/0/love", "adult_chubby", Path.Combine(mem, "p3.png"));
            Memories.Card(sp, null, "penguin", "adult_sweet", 77, Path.Combine(mem, "c1.png"));
            Memories.Card(sp, sp, "hamster", "adult_chubby", 12, Path.Combine(mem, "c2.png"));
            var day = new DateTime(2026, 11, 12);
            var book = new PetBook("hamster", false, new[] { "hamster", "fox", "penguin", "slime" },
                new[] { new ReleasedPet("hamster", "adult_chubby", 2900, new DateTime(2026, 11, 11), 1), new ReleasedPet("penguin", "adult_sweet", 2500, new DateTime(2026, 11, 12), 2) })
            {
                Variant = 2,
                Gifts = new[] { new GiftRecord("acorn", "hamster", day), new GiftRecord("flower", "penguin", day), new GiftRecord("acorn", "penguin", day), new GiftRecord("clover", "hamster", day) },
                Album = new[] { new PhotoRecord("hamster", "egg", day, Path.Combine(mem, "p1.png")), new PhotoRecord("hamster", "baby", day, Path.Combine(mem, "p2.png")), new PhotoRecord("hamster", "adult_chubby", day, Path.Combine(mem, "p3.png")) },
                Cards = new[] { new CardRecord("penguin", day, Path.Combine(mem, "c1.png")), new CardRecord("hamster", day, Path.Combine(mem, "c2.png")) },
                VisitCounts = new Dictionary<string, int> { ["hamster"] = 5, ["penguin"] = 2 },
            };
            var status = new PetStatus("hamster", "child", 200, 72, 88, 18, false, "sleepy", 1, 2, 5, 2080, .06, "adult", new[]
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
                if (name == "풀어주기") { f.SelectTab("book"); f.AskRelease(); }
                else if (name == "상태 아래") { f.SelectTab("status"); f.ScrollToEnd(); }
                else f.SelectTab(name);
                Application.DoEvents();
                var page = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(page, new Rectangle(Point.Empty, f.Size));
                shots.Add(page);
            }
            int rows = (shots.Count + 2) / 3;
            using var bmp = new Bitmap((f.Width + 20) * 3 - 20, (f.Height + 20) * rows - 20);
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

        if (args.Length == 2 && args[0] == "--icbm-scene") args = new[] { "--mode-scene", "icbm", args[1] }; // 옛 이름: ICBM 은 게임 모듈이 됐다 (--mode-scene icbm)

        if (args.Length >= 2 && args[0] == "--art-all") // 모든 종의 아기·어린이·어른을 한 장으로 (디버그: --art-all 그림.png [자세])
        {
            ApplicationConfiguration.Initialize();
            L.Use("ko");
            Showcase.Overview(Directory.Exists("assets") ? PetArt.FromDir("assets") : PetArt.Embedded, args[1], args.Length > 2 ? args[2] : "idle");
            return 0;
        }

        if (args.Length == 2 && args[0] == "--art-gif") // README 용 움직이는 그림 (34종 행진)
        {
            Gif.Parade(Directory.Exists("assets") ? PetArt.FromDir("assets") : PetArt.Embedded, args[1]);
            return 0;
        }

        if (args.Length >= 3 && args[0] == "--art-look") // 데이터로 그린 캐릭터 보기: --art-look 그림.png hamster '{"fur":"#FAB2CC",...}' (화면을 띄우지 않는다)
        {
            ApplicationConfiguration.Initialize();
            var art = Directory.Exists("assets") ? PetArt.FromDir("assets") : PetArt.Embedded;
            string[] forms = Showcase.Forms, poses = { "idle", "happy" };
            const int k = 3, cell = PetArt.Cell * k;
            using var bmp = new Bitmap(forms.Length * poses.Length * cell + 20, (args.Length - 2) * (cell + 10) + 10);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(236, 239, 244));
            for (int i = 2; i < args.Length; i++)
            {
                string id = args[i].StartsWith('{') ? "look" + i : args[i];
                var a = args[i].StartsWith('{') ? art.With(id, args[i]) : art;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var sp = Sprites.Drawn(a, id, null);
                Console.WriteLine($"{id}: 한 벌 그리기 {sw.ElapsedMilliseconds}ms");
                int x = 10;
                foreach (var f in forms)
                {
                    foreach (var p in poses) { g.DrawImage(sp.Get($"{f}/0/{p}", 0, k), x, 10 + (i - 2) * (cell + 10)); x += cell; }
                }
            }
            bmp.Save(args[1]);
            return 0;
        }

        if (args.Length is >= 2 and <= 4 && args[0] == "--run-scene") // 옛 이름: --mode-scene run@<배율> <그림> 과 같다. 뒤에 배율, 형태 하나(그 형태만 자세히)
        {
            GameModes.SceneArg = args.Length > 3 ? args[3] : null;
            args = new[] { "--mode-scene", "run@" + (args.Length > 2 ? args[2] : "3"), args[1] };
        }

        if (args.Length == 3 && args[0] == "--mode-scene") // 게임 모듈 장면 그려 보기 (화면을 띄우지 않는다)
        {
            ApplicationConfiguration.Initialize();
            GameModes.Scenes[args[1]](args[2]);
            return 0;
        }

        if (args.Length == 2 && args[0] == "--mode-bench") // 게임 모듈 속도 재기 (화면을 띄우지 않는다)
        {
            ApplicationConfiguration.Initialize();
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            L.Use("ko");
            Console.WriteLine(GameModes.Benches[args[1]]());
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
        L.Use(null); // 윈도우 언어로 시작 (저장된 언어가 있으면 PetForm 이 바꾼다)
        if (run == null) // 예전 판(0.1.6 까지)이 받아 둔 캐릭터 그림: 이제 데이터로 그리니 지운다
            try { Directory.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bolok", "pets"), true); }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
        Application.Run(new PetForm(sprites, tuning, data, run, assets));
        return 0;
    }
}
