namespace Bolok;

// 게임 모듈: 게임 하나 = 폴더 하나 (app/Games/<게임>/). 규칙·앱에 붙이기·그리기·기록·자가 검사·디버그 명령을 그 안에서 다 한다.
// 앱(PetForm·Overlay·Sim)은 게임을 하나하나 알지 않고 이 약속으로만 부른다.
// 게임을 빼려면 폴더를 지우고 GameModes 에서 한 줄 빼면 된다 (YELL 2026-11-12: 기능마다 서로 매이지 않게)
interface IGameMode
{
    string Id { get; }          // 디버그 명령·기록·로그 이름 ("raid")
    string? MenuKey { get; }    // 메뉴 이름 (lang.json 키). null 이면 메뉴에 안 단다 (디버그 도구처럼 메뉴를 따로 다는 것)
    bool Running { get; }
    void Start();               // 메뉴에서 (알이거나 「다른」 게임 중이면 앱이 부르지 않는다. 이 게임을 하는 중에는 또 부른다: 무시하거나 한 번 더 하거나는 게임이 정한다)
    void Frame(double dt);      // 하는 동안 매 프레임
    void Stop(bool quit);
    void Dirty(Overlay o, List<Rectangle> r); // 이번 프레임에 다시 칠할 자리 (o.Client 로 창 좌표로)
    void Draw(Overlay o, Graphics g);
    bool Command(string[] a);   // 디버그: <id> ... (내 명령이면 처리하고 true)
    IEnumerable<GameStat> Stats();   // 설정 → 상태 게임 기록
    string? SaveState();             // 기록 (JSON 글), 없으면 null
    void LoadState(string? json);
    IReadOnlyList<GameChoice>? Choices => null; // 하위 메뉴 (대포 고르기처럼 시작할 때 하나를 고르는 게임). 있으면 메뉴 이름 아래에 달고, Start 대신 고른 줄의 Start 를 부른다
}

// 하위 메뉴 한 줄: 보일 글(언어가 바뀌면 다시 묻는다)과 고르면 할 일
sealed record GameChoice(Func<string> Text, Action Start);

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
    bool HitPet(int x, int y);         // 화면 좌표가 본체 창에 그려진 볼록이 몸(그림 픽셀, 둘레 조금)에 닿는가
    void HideFromCapture(bool on);     // 본체·그림 창·판을 화면 공유·녹화에 안 찍히게 (Session 이 판을 여닫을 때 부른다)
    IReadOnlyList<string> Friends { get; } // 자연으로 떠난 친구들 (종#색, 겹치지 않게)
    void Repaint();                    // 그림 창을 통째로 다시 칠한다 (판 없이 그리던 것을 한꺼번에 지울 때)
}

// 게임 한 줄: 이름, 만들기, 자가 검사, 장면 그려 보기(그림 경로, 볼록이 배율), 속도 재기 (없으면 null)
sealed record GameEntry(string Id, Func<IGameHost, IGameMode> Create, Func<string>? Check = null, Action<string, int>? Scene = null, Func<string>? Bench = null);

static class GameModes
{
    // 게임마다 한 줄. 메뉴에 이 순서로 나온다. 게임을 빼려면 폴더를 지우고 여기서 그 줄만 뺀다
    static readonly GameEntry[] All =
    {
        new("raid", h => new RaidGame(h), RaidGame.Check, RaidGame.Scene, RaidGame.BenchRun),
        new("swarm", h => new SwarmGame(h), SwarmGame.Check, SwarmGame.Scene, SwarmGame.BenchRun),
        new("breakout", h => new BreakoutGame(h), BreakoutGame.Check, BreakoutGame.Scene, BreakoutGame.BenchRun),
    };

    public static List<IGameMode> Create(IGameHost host) => All.Select(e => e.Create(host)).ToList();

    // 자가 검사: 공통 도구(Kit) 다음에 게임마다 제 규칙을 검사하고 한 줄 결과를 돌려준다 (실패면 예외)
    public static IEnumerable<Func<string>> Checks => All.Where(e => e.Check != null).Select(e => e.Check!).Prepend(KitCheck.Check);

    // 화면을 띄우지 않고 그려 보기: Bolok.exe --mode-scene <id> <그림.png> (kit: 공통 도구 그림).
    // 다른 배율로 보려면 id 뒤에 @배율: --mode-scene raid@5 (기본은 3. 모니터가 크거나 글자 크기를 키운 화면은 4~6)
    public static Dictionary<string, Action<string>> Scenes
    {
        get
        {
            var scenes = new Dictionary<string, Action<string>> { ["kit"] = KitCheck.Scene };
            foreach (var e in All.Where(e => e.Scene != null))
            {
                scenes[e.Id] = path => e.Scene!(path, 3);
                for (int k = 1; k <= 6; k++) { int scale = k; scenes[$"{e.Id}@{k}"] = path => e.Scene!(path, scale); }
            }
            return scenes;
        }
    }

    // 속도 재기: Bolok.exe --mode-bench <id>
    public static Dictionary<string, Func<string>> Benches => All.Where(e => e.Bench != null).ToDictionary(e => e.Id, e => e.Bench!);

    // 옛 저장 칸(게임이 모듈이 되기 전 SaveData 에 따로 있던 기록)을 모듈 기록(id → JSON 글)으로 바꾼다.
    // 이미 모듈 기록이 있으면 그것이 먼저다. 게임 타입을 참조하지 않는 글 변환이라 게임 폴더를 지워도 깨지지 않는다.
    // 모듈로 옮기는 게임은 이 id 와 칸 이름으로 LoadState 를 받는다:
    //   run {Best, Last, Level, Scores[]} · breakout {Best} · fortress {Wins} · shoot {Best}
    public static Dictionary<string, string> WithLegacy(SaveData d)
    {
        var data = new Dictionary<string, string>(d.GameData ?? new());
        void Old(string id, bool has, object record) { if (has && !data.ContainsKey(id)) data[id] = System.Text.Json.JsonSerializer.Serialize(record); }
        Old("run", d.RunBest > 0 || d.RunLast > 0 || d.RunLevel > 1, new { Best = d.RunBest, Last = d.RunLast, Level = Math.Max(1, d.RunLevel), Scores = d.RunScores ?? new() });
        Old("breakout", d.BreakBest > 0, new { Best = d.BreakBest });
        Old("fortress", d.WormsWins > 0, new { Wins = d.WormsWins });
        Old("shoot", d.GameBest > 0, new { Best = d.GameBest });
        return data;
    }
}
