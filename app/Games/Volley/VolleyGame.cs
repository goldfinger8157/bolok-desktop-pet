namespace Bolok;

// 볼록이 배구 모듈 (틀). 설계와 규칙은 docs/volleyball.md — 이 폴더 안만 채운다
sealed class VolleyGame : IGameMode
{
    readonly IGameHost host;

    public VolleyGame(IGameHost host) => this.host = host;

    public string Id => "volley";
    public string? MenuKey => null; // 다 만들면 "menu.volley" (그때까지 메뉴에 안 단다)
    public bool Running => false;
    public void Start() { }
    public void Frame(double dt) { }
    public void Stop(bool quit) { }
    public void Dirty(Overlay o, List<Rectangle> r) { }
    public void Draw(Overlay o, Graphics g) { }
    public bool Command(string[] a) => false;
    public IEnumerable<GameStat> Stats() { yield return new GameStat(L.T("game.volley"), L.T("game.volley_none"), false); }
    public string? SaveState() => null;
    public void LoadState(string? json) { }

    public static string Check() => "배구: 아직 틀만";
    public static void Scene(string path, int scale) { using var bmp = new Bitmap(8, 8); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
    public static string BenchRun() => "배구: 아직 틀만";
}
