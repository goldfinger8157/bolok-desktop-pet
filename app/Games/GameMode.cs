namespace Bolok;

// 게임 모듈: 게임 하나 = 폴더 하나 (app/Games/<게임>/). 규칙·앱에 붙이기·그리기·기록·자가 검사·디버그 명령을 그 안에서 다 한다.
// 앱(PetForm·Overlay·Sim)은 게임을 하나하나 알지 않고 이 약속으로만 부른다.
// 게임을 빼려면 폴더를 지우고 GameModes 에서 한 줄 빼면 된다 (YELL 2026-11-12: 기능마다 서로 매이지 않게)
interface IGameMode
{
    string Id { get; }          // 디버그 명령·기록·로그 이름 ("raid")
    string? MenuKey { get; }    // 메뉴 이름 (lang.json 키). null 이면 메뉴에 안 단다 (디버그 도구처럼 메뉴를 따로 다는 것)
    bool Running { get; }
    void Start();               // 메뉴에서 (다른 게임 중이거나 알이면 앱이 부르지 않는다)
    void Frame(double dt);      // 하는 동안 매 프레임
    void Stop(bool quit);
    void Dirty(Overlay o, List<Rectangle> r); // 이번 프레임에 다시 칠할 자리 (o.Client 로 창 좌표로)
    void Draw(Overlay o, Graphics g);
    bool Command(string[] a);   // 디버그: <id> ... (내 명령이면 처리하고 true)
    IEnumerable<GameStat> Stats();   // 설정 → 상태 게임 기록
    string? SaveState();             // 기록 (JSON 글), 없으면 null
    void LoadState(string? json);
}

// 게임이 앱에서 빌려 쓰는 것
interface IGameHost
{
    Brain Pet { get; }
    Sprites Sprites { get; }
    bool Busy { get; }                 // 다른 게임 중이거나 알
    GameLayer OpenLayer(Rectangle field, MouseEventHandler down, MouseEventHandler up, Action quit); // 판(클릭·키 받기). 하던 창은 끝나면 돌려준다
    void CloseLayer();
    void ShowPet(bool show);           // 게임이 볼록이를 판 위에 작게 직접 그릴 때 본체 창을 숨긴다
    Sprites? SpritesOf(string species); // 다른 종 그림 (데이터로 그리거나 받아 둔 것, 없으면 null)
    void Save();
}

static class GameModes
{
    // 메뉴에 이 순서로 나온다
    public static List<IGameMode> Create(IGameHost host) => new() { new RaidGame(host), new SwarmGame(host) };

    // 자가 검사: 게임마다 제 규칙을 검사하고 한 줄 결과를 돌려준다 (실패면 예외)
    public static IEnumerable<Func<string>> Checks => new Func<string>[] { RaidGame.Check, SwarmGame.Check };

    // 화면을 띄우지 않고 그려 보기: Bolok.exe --mode-scene <id> <그림.png>
    public static Dictionary<string, Action<string>> Scenes => new() { ["raid"] = RaidGame.Scene, ["swarm"] = SwarmGame.Scene };
}
