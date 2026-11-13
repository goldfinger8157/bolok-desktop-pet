using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Bolok;

// 로그. 디버그 모드에서만 run/log.jsonl 에 남는다
public static class Dbg
{
    public static bool On;
    static StreamWriter? w;
    static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static void Start(string dir)
    {
        On = true;
        // 이어 쓴다: tail 로 지켜보는 중에는 파일 이름을 바꿀 수 없어서 앱이 뜨다 죽는다
        var path = Path.Combine(dir, "log.jsonl");
        w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) { AutoFlush = true };
    }

    static string? lastError;
    static DateTime lastErrorAt;

    // 같은 예외가 매 프레임 나도 1분에 한 번만 남긴다
    public static void Error(Exception ex)
    {
        var text = ex.ToString();
        if (text == lastError && DateTime.UtcNow - lastErrorAt < TimeSpan.FromMinutes(1)) return;
        (lastError, lastErrorAt) = (text, DateTime.UtcNow);
        Log("error", ("msg", text));
    }

    public static void Log(string ev, params (string Key, object? Value)[] kv)
    {
        if (w == null) return;
        var d = new Dictionary<string, object?> { ["t"] = DateTime.Now.ToString("HH:mm:ss.f"), ["ev"] = ev };
        foreach (var (k, v) in kv) d[k] = v;
        w.WriteLine(JsonSerializer.Serialize(d, Json));
    }
}

// 띄워 둔 채 고치는 통로: 명령은 run/cmd.txt, 수치는 run/tuning.json, 그림은 assets/ 를 지켜본다
public sealed class DevLink
{
    readonly string run, assets;
    readonly Brain b;
    readonly PetForm form;
    readonly string cmdPath, tunePath, shotPath;
    DateTime tuneTime, spriteTime;
    double nextPoll, nextBeat;
    readonly Process self = Process.GetCurrentProcess();
    TimeSpan lastCpu;
    DateTime lastWall = DateTime.UtcNow;

    public bool? ForceAfk;
    public double Cpu;                  // 최근 10초 CPU: 컴퓨터 전체(모든 코어)를 100 으로 친 값

    public DevLink(string run, string assets, Brain b, PetForm form)
    {
        this.run = run; this.assets = assets; this.b = b; this.form = form;
        cmdPath = Path.Combine(run, "cmd.txt");
        tunePath = Path.Combine(run, "tuning.json");
        shotPath = Path.Combine(run, "shot.png");
        File.WriteAllText(cmdPath, "");
        spriteTime = File.GetLastWriteTimeUtc(Path.Combine(assets, "sprites.png"));
        lastCpu = self.TotalProcessorTime;
    }

    (double Cores, double MemMb) SampleUsage()
    {
        self.Refresh();
        var (cpu, now) = (self.TotalProcessorTime, DateTime.UtcNow);
        double wall = (now - lastWall).TotalMilliseconds;
        double cores = wall > 0 ? (cpu - lastCpu).TotalMilliseconds / wall : 0;
        (lastCpu, lastWall) = (cpu, now);
        Cpu = cores / Environment.ProcessorCount * 100;
        return (cores, self.WorkingSet64 / 1048576.0);
    }

    public static Tuning LoadTuning(string run)
    {
        var path = Path.Combine(run, "tuning.json");
        if (!File.Exists(path)) return new Tuning();
        return JsonSerializer.Deserialize<Tuning>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Tuning();
    }

    public void Poll(double fps, double frameMs)
    {
        if (b.Now < nextPoll) return;
        nextPoll = b.Now + .3;

        try { Commands(); WatchTuning(); WatchSprites(); }
        catch (IOException) { } // 저쪽이 쓰는 중이면 다음에 다시 읽는다
        catch (Exception ex) { Dbg.Log("devlink-error", ("msg", ex.Message)); }

        if (b.Now >= nextBeat)
        {
            nextBeat = b.Now + 10;
            var p = b.P;
            var (cores, mem) = SampleUsage();
            Dbg.Log("beat", ("stage", p.Stage), ("age", Math.Round(p.AgeMin, 1)), ("full", Math.Round(p.Full)), ("mood", Math.Round(p.Mood)),
                ("energy", Math.Round(p.Energy)), ("asleep", p.Asleep), ("bites", p.Bites), ("holes", b.Holes.Count), ("miss", p.Miss),
                ("mode", b.Mode.ToString()), ("pose", b.Pose), ("x", (int)b.X), ("y", (int)b.Y), ("speed", b.Speed), ("fps", Math.Round(fps)), ("active", b.Active?.R.ToString()),
                ("cpu", Math.Round(Cpu, 2)), ("coreUse", Math.Round(cores * 100, 1)), ("mem", Math.Round(mem)), ("gdi", Native.GdiObjects()), ("frameMs", Math.Round(frameMs, 2)));
        }
    }

    void Commands()
    {
        if (new FileInfo(cmdPath).Length == 0) return;
        var lines = File.ReadAllLines(cmdPath);
        File.WriteAllText(cmdPath, "");
        foreach (var line in lines)
        {
            var a = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (a.Length == 0) continue;
            try { Run(a); Dbg.Log("cmd", ("line", line)); }
            catch (Exception ex) { Dbg.Log("cmd-error", ("line", line), ("msg", ex.Message)); }
        }
    }

    void Run(string[] a)
    {
        var p = b.P;
        switch (a[0])
        {
            case "speed": b.Speed = double.Parse(a[1]); break;
            case "skip": b.Advance(double.Parse(a[1]), false); break;
            case "act": b.Care(a[1]); break;
            case "afk": ForceAfk = a[1] switch { "on" => true, "off" => false, _ => null }; break;
            case "hole": b.P.Bites++; break;
            case "reset": b.Reset(); break;
            case "emote": b.Emote(a[1]); break;
            case "say": b.Say(3.5, string.Join(' ', a.Skip(1)).Split('|')); break; // 예: say 첫 줄|둘째 줄
            case "nap": if (!b.TryNap()) Dbg.Log("nap-none"); break;
            case "visit": form.VisitCommand(a); break;
            case "gesture": b.Gesture(a[1]); break; // 마우스 손짓 반응만: gesture stroke|pat|poke
            case "clover": form.CloverCommand(a); break; // clover (지금 돋기) / clover wear / clover drop / clover off
            case "icbm": form.IcbmCommand(a); break;
            case "species": form.SetSpecies(a[1], a.Length > 2 ? int.Parse(a[2]) : 0); break; // 예: species fox 2 (2번 색)
            case "release": form.Release(); break;
            case "break": form.BreakCommand(a.Length > 1 ? a[1] : "start", a.Length > 2 ? a[2] : null); break;
            case "esc": form.BossKey(); break; // 보스 키 (Esc 를 누른 것과 같다)
            case "game": form.StartGame(a.Length > 1 ? a[1] : null, a.Length > 2 ? a[2] : null); break;
            case "fire": form.GameMouse(false, int.Parse(a[1]), int.Parse(a[2])); form.GameRelease(); break;
            case "reload": form.GameMouse(true, 0, 0); break;
            case "probe": Dbg.Log("probe", ("x", a[1]), ("y", a[2]), ("who", form.WhoGetsClick(int.Parse(a[1]), int.Parse(a[2])))); break;
            case "petat": Dbg.Log("petat", ("x", (int)b.X), ("y", (int)(b.Y - b.CellPx * .45)), ("mode", b.Mode.ToString())); break;
            case "hide": if (!b.TryHide()) Dbg.Log("hide-none"); break;
            case "goto": b.GoTo(double.Parse(a[1]), double.Parse(a[2])); break;
            case "throw": b.Throw(double.Parse(a[1]), double.Parse(a[2])); break;
            case "giveback": b.GiveBack(); break;
            case "state": Dbg.Log("state", ("pet", p), ("tuning", b.K)); break;
            case "shot": form.Shot(shotPath); break;
            case "settings-shot": form.SettingsShot(Path.Combine(Path.GetDirectoryName(shotPath)!, "settings-live.png"), a.Length > 1 ? a[1] : "상태"); break; // 화면 밖에서 그려 저장 (화면을 가리지 않는다)
            case "quit": Application.Exit(); break;
            case "set":
                double v = a[1] == "stage" ? 0 : double.Parse(a[2]);
                switch (a[1])
                {
                    case "full": p.Full = v; break;
                    case "mood": p.Mood = v; break;
                    case "energy": p.Energy = v; break;
                    case "bites": p.Bites = (int)v; break;
                    case "age": p.AgeMin = v; break;
                    case "miss": p.Miss = (int)v; break;
                    case "food": p.Food = (int)v; break; // 먹이 바구니
                    case "stage": p.Stage = a[2]; break;
                    default: throw new ArgumentException("모르는 값: " + a[1]);
                }
                break;
            default: if (!form.ModeCommand(a)) throw new ArgumentException("모르는 명령"); break; // 게임 모듈 명령: <게임 id> ...
        }
    }

    void WatchTuning()
    {
        if (!File.Exists(tunePath)) return;
        var t = File.GetLastWriteTimeUtc(tunePath);
        if (t == tuneTime) return;
        tuneTime = t;
        b.K = LoadTuning(run);
        form.ApplyScale();
        Dbg.Log("tuning-reloaded");
    }

    void WatchSprites()
    {
        var t = new[] { "sprites.png", "pets/art.json" }.Max(f => File.GetLastWriteTimeUtc(Path.Combine(assets, f))); // 그림 데이터를 고쳐도 다시 그린다
        if (t == spriteTime) return;
        spriteTime = t;
        form.SwapSprites(Sprites.FromDir(assets, form.SpriteKey));
        Dbg.Log("sprites-reloaded");
    }
}
