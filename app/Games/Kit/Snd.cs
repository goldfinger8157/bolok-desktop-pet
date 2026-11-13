namespace Bolok;

// 게임 소리: 사건의 뜻 → 소리 한 가지. 게임마다 Sfx 이름을 따로 고르지 않고 이 표를 쓴다 (같은 일은 어느 게임에서나 같은 소리)
enum Sound
{
    Tap,        // 톡: 점프·튕김·버튼
    Shot,       // 쏘기 (권총)
    ShotMg,     // 쏘기 (기관총·미니건)
    ShotGun,    // 쏘기 (산탄총)
    Hit,        // 맞힘
    Pick,       // 줍기·레벨 업·다음 판
    Hurt,       // 내가 다침
    Boom,       // 폭발 (진짜 터질 때만. 다친 소리로 쓰지 않는다)
    Reload,     // 장전 (철컥)
}

static class Snd
{
    static readonly string[] names = { "empty", "pistol", "mg", "shotgun", "hit", "pick", "hurt", "boom", "reload" };

    public static string Name(Sound s) => names[(int)s];
    public static void Play(Sound s) => Sfx.Play(names[(int)s]);

    // 게임의 사건 이름표로: Snd.Play(table, "kill"). 표에 없는 사건은 소리가 없다
    public static void Play(IReadOnlyDictionary<string, Sound> table, string ev)
    {
        if (table.TryGetValue(ev, out var s)) Play(s);
    }
}
