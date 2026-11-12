namespace Bolok;

// 캐릭터 확인 (디버그 앱 전용, YELL 2026-11-12): 키우는 아이를 숨기고, 고른 종의 아기·어린이·어른 3종을 화면 아래에 나란히 띄운다.
// 다섯이 같은 자세를 같이 한다 — 자동이면 모든 자세를 차례로, 고르면 그 자세만. 걷기는 제 자리에서 좌우로 오가며 돌아서는 것까지 본다.
// 그림은 실제 앱이 쓰는 그대로(데이터로 그리거나 받은 그림). 개발 중 art.json 을 고치면 바로 다시 그린다.
// 게임 모듈 틀(IGameMode)로 그리기·프레임만 빌려 쓴다 — 메뉴는 디버그일 때 PetForm 이 Menu() 로 단다
sealed class Showcase : IGameMode
{
    public static readonly string[] Forms = { "baby", "child", "adult_chubby", "adult_sweet", "adult_grumpy" };
    const double PoseSec = 3.2, WalkSec = 6;

    sealed class Actor { public string Form = ""; public double X, Home; public int Dir = 1; }

    readonly IGameHost host;
    readonly IReadOnlyDictionary<string, int> poseMs;
    readonly string[] poses;
    readonly List<Actor> actors = new();
    Sprites? sp, seen;
    string species = "";
    string? fixedPose;
    int auto, cheek;
    double clock, poseAt, ground, lane;

    public Showcase(IGameHost host, IReadOnlyDictionary<string, int> poseMs) =>
        (this.host, this.poseMs, poses) = (host, poseMs, PetArt.Embedded.Poses.ToArray());

    public string Id => "showcase";
    public string? MenuKey => null;
    public bool Running { get; private set; }
    string Pose => fixedPose ?? poses[auto % poses.Length];

    public void Start() => Show(species.Length > 0 ? species : Sprites.DefaultSpecies);

    // 이 종으로 (이미 하는 중이면 종만 바꾼다)
    public void Show(string id)
    {
        if (host.SpritesOf(id) is not { } s) { Dbg.Log("showcase-missing", ("id", id)); return; }
        (sp, seen, species) = (s, host.Sprites, id);
        if (!Running)
        {
            Running = true;
            (clock, poseAt, auto) = (0, 0, 0);
            host.Pet.StartPuppet();
            host.ShowPet(false);
            Place();
        }
        Dbg.Log("showcase", ("id", id), ("pose", fixedPose ?? "auto"), ("cheek", cheek));
    }

    // 키우는 아이가 있던 모니터 아래, 아이 근처에 다섯 칸
    void Place()
    {
        var b = host.Pet;
        var area = b.Area;
        lane = Math.Min(b.CellPx * 2.4, area.Width / 5.0);
        double center = Math.Clamp(b.X, area.Left + lane * 2.5, area.Right - lane * 2.5);
        ground = area.Bottom - b.K.FloorGap;
        actors.Clear();
        for (int i = 0; i < Forms.Length; i++)
        {
            double home = center + (i - 2) * lane;
            actors.Add(new Actor { Form = Forms[i], X = home, Home = home });
        }
        Dbg.Log("showcase-place", ("x", (int)center), ("y", (int)ground), ("lane", (int)lane));
    }

    public void Frame(double dt)
    {
        clock += dt;
        if (!ReferenceEquals(host.Sprites, seen)) // 개발 중 그림 데이터를 고쳤다 (본체 그림이 바뀌었다): 다시 그린다
        {
            seen = host.Sprites;
            sp = host.SpritesOf(species) ?? sp;
        }
        if (fixedPose == null && clock - poseAt >= (Pose == "walk" ? WalkSec : PoseSec)) { auto++; poseAt = clock; }
        if (Pose != "walk") return;
        double speed = host.Pet.K.WalkSpeed * host.Pet.Scale / 3.0, reach = lane * .3;
        foreach (var a in actors) // 제 칸 안에서 좌우로 오간다 (끝에서 돌아선다)
        {
            a.X += a.Dir * speed * dt;
            if (a.X > a.Home + reach) { a.X = a.Home + reach; a.Dir = -1; }
            else if (a.X < a.Home - reach) { a.X = a.Home - reach; a.Dir = 1; }
        }
    }

    public void Stop(bool quit)
    {
        if (!Running) return;
        Running = false;
        host.ShowPet(true);
        host.Pet.LeavePuppet();
        Dbg.Log("showcase-end");
    }

    string[] Note => new[] { $"{L.Species(species)} · {Pose}{(fixedPose == null ? $" {auto % poses.Length + 1}/{poses.Length}" : "")} · {L.T("menu.showcase_cheek")} {cheek}" };

    int CellPx => PetArt.Cell * host.Pet.Scale;

    public void Dirty(Overlay o, List<Rectangle> r)
    {
        int cell = CellPx;
        foreach (var a in actors) r.Add(o.Client(new Rectangle((int)(a.X - cell / 2.0) - 4, (int)(ground - cell) - 4, cell + 8, cell + 8)));
        r.Add(o.Client(NoteBox(o)));
    }

    // 안내: 줄 가운데 위 (좁은 세로 모니터에서도 아이들을 가리지 않게)
    Rectangle NoteBox(Overlay o)
    {
        var size = o.NoteRect(0, 0, host.Pet.Area, Note);
        double cx = actors.Count > 0 ? actors[2].Home : host.Pet.X;
        return new Rectangle((int)(cx - size.Width / 2.0), (int)(ground - CellPx) - size.Height, size.Width, size.Height);
    }

    public void Draw(Overlay o, Graphics g)
    {
        if (sp == null) return;
        int k = host.Pet.Scale, cell = CellPx;
        string pose = Pose;
        int ms = poseMs.GetValueOrDefault(pose, 600);
        foreach (var a in actors)
        {
            string name = $"{a.Form}/{cheek}/{pose}";
            if (!sp.Has(name)) continue;
            var p = o.ToClient(a.X - cell / 2.0, ground - cell);
            g.DrawImageUnscaled(sp.Get(name, (int)((clock - poseAt) * 1000 / ms), k, a.Dir < 0), p.X, p.Y);
        }
        o.DrawNoteBox(g, NoteBox(o), Note);
    }

    // 디버그: showcase <종> / showcase pose <자세|auto> / showcase cheek <0~3> / showcase stop
    public bool Command(string[] a)
    {
        if (a.Length == 0 || a[0] != Id) return false;
        switch (a.Length > 1 ? a[1] : "")
        {
            case "stop": Stop(true); break;
            case "pose" when a.Length > 2: SetPose(a[2] == "auto" ? null : a[2]); break;
            case "cheek" when a.Length > 2: SetCheek(int.Parse(a[2])); break;
            case "": Start(); break;
            default: Show(a[1]); break;
        }
        return true;
    }

    void SetPose(string? pose)
    {
        if (pose != null && !poses.Contains(pose)) throw new ArgumentException("모르는 자세: " + pose);
        (fixedPose, poseAt) = (pose, clock);
        Dbg.Log("showcase-pose", ("pose", pose ?? "auto"));
    }

    void SetCheek(int c) { cheek = Math.Clamp(c, 0, PetArt.Embedded.Cheeks - 1); Dbg.Log("showcase-cheek", ("cheek", cheek)); }

    // 메뉴: 캐릭터 확인 ▸ 34종 / ─ / 자세 ▸ 자동·각 자세 / 볼 ▸ 0~3 / 끝내기
    public ToolStripMenuItem Menu(List<(ToolStripItem Item, Func<string> Text)> texts, Func<bool> busy, Action<Action> later)
    {
        var root = new ToolStripMenuItem();
        texts.Add((root, () => L.T("menu.showcase")));
        foreach (var id in PetArt.Embedded.Species)
        {
            var item = new ToolStripMenuItem("", null, (_, _) => later(() => { if (Running || !busy()) Show(id); }));
            texts.Add((item, () => L.Species(id)));
            root.DropDownItems.Add(item);
        }
        root.DropDownItems.Add(new ToolStripSeparator());
        var pose = new ToolStripMenuItem();
        texts.Add((pose, () => L.T("menu.showcase_pose")));
        var autoItem = new ToolStripMenuItem("", null, (_, _) => SetPose(null));
        texts.Add((autoItem, () => L.T("menu.showcase_auto")));
        pose.DropDownItems.Add(autoItem);
        foreach (var p in poses) pose.DropDownItems.Add(p, null, (_, _) => SetPose(p)); // 자세 이름은 그림 데이터의 이름 그대로 (디버그용)
        root.DropDownItems.Add(pose);
        var cheekItem = new ToolStripMenuItem();
        texts.Add((cheekItem, () => L.T("menu.showcase_cheek")));
        for (int c = 0; c < PetArt.Embedded.Cheeks; c++)
        {
            int n = c;
            cheekItem.DropDownItems.Add(n.ToString(), null, (_, _) => SetCheek(n));
        }
        root.DropDownItems.Add(cheekItem);
        var end = new ToolStripMenuItem("", null, (_, _) => Stop(true));
        texts.Add((end, () => L.T("menu.showcase_end")));
        root.DropDownItems.Add(end);
        root.DropDownOpening += (_, _) => // 지금 보는 종·자세·볼에 체크
        {
            foreach (var item in root.DropDownItems.OfType<ToolStripMenuItem>().Take(PetArt.Embedded.Species.Count).Select((it, i) => (it, i)))
                item.it.Checked = Running && PetArt.Embedded.Species[item.i] == species;
            end.Enabled = Running;
        };
        pose.DropDownOpening += (_, _) =>
        {
            autoItem.Checked = fixedPose == null;
            foreach (var item in pose.DropDownItems.OfType<ToolStripMenuItem>().Skip(1)) item.Checked = item.Text == fixedPose;
        };
        cheekItem.DropDownOpening += (_, _) =>
        {
            foreach (var item in cheekItem.DropDownItems.OfType<ToolStripMenuItem>()) item.Checked = item.Text == cheek.ToString();
        };
        return root;
    }

    public IEnumerable<GameStat> Stats() => Array.Empty<GameStat>();
    public string? SaveState() => null;
    public void LoadState(string? json) { }
}
