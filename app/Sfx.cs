using System.Runtime.InteropServices;

namespace Bolok;

// 게임 효과음. 파일 없이 그 자리에서 짧은 소리를 만든다. 사무실이라 기본은 꺼 둔다 (메뉴 「게임 소리」)
public static class Sfx
{
    public static bool On;

    [DllImport("winmm.dll")]
    static extern bool PlaySound(IntPtr data, IntPtr module, uint flags);
    const uint SND_ASYNC = 1, SND_NODEFAULT = 2, SND_MEMORY = 4;

    static readonly Dictionary<string, IntPtr> loaded = new(); // 재생하는 동안 메모리가 움직이면 안 되니 고정해 둔다

    public static void Play(string name)
    {
        if (!On) return;
        if (!loaded.TryGetValue(name, out var p))
        {
            var wav = Make(name);
            p = Marshal.AllocHGlobal(wav.Length);
            Marshal.Copy(wav, 0, p, wav.Length);
            loaded[name] = p;
        }
        PlaySound(p, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
    }

    // 16비트 모노 22050Hz WAV
    public static byte[] Make(string name)
    {
        const int rate = 22050;
        var rng = new Random(name.GetHashCode());
        double len = name switch { "shotgun" => .3, "pistol" => .18, "mg" => .07, "hit" => .12, "empty" => .03, "reload" => .26, "boom" => .55, "rumble" => 2.2, _ => .05 };
        int n = (int)(rate * len);
        var s = new short[n];
        double low = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate, noise = rng.NextDouble() * 2 - 1, v;
            switch (name)
            {
                case "pistol": v = noise * Math.Exp(-t * 28) * .7 + Math.Sin(2 * Math.PI * 110 * t) * Math.Exp(-t * 18) * .6; break;
                case "shotgun": low += (noise - low) * .25; v = low * Math.Exp(-t * 14) * 1.8 + Math.Sin(2 * Math.PI * 70 * t) * Math.Exp(-t * 12) * .5; break;
                case "mg": v = noise * Math.Exp(-t * 55) * .7 + Math.Sin(2 * Math.PI * 85 * t) * Math.Exp(-t * 40) * .5; break;
                case "hit": v = Math.Sign(Math.Sin(2 * Math.PI * (900 + 5000 * t) * t)) * Math.Exp(-t * 22) * .35; break; // 삐약
                case "empty": v = noise * Math.Exp(-t * 300) * .5; break;
                case "reload": v = (noise * Math.Exp(-t * 250) + (t > .17 ? noise * Math.Exp(-(t - .17) * 250) : 0)) * .5; break;
                case "boom": low += (noise - low) * .08; v = low * Math.Exp(-t * 5) * 2.2; break;
                case "rumble": low += (noise - low) * .06; v = low * (.45 + .55 * Math.Max(0, Math.Sin(2 * Math.PI * 7 * t))) * (.5 + .5 * t / 2.2) * 2.6; break; // 콰콰콰콰: 낮게 울리며 점점 커진다
                default: v = 0; break;
            }
            s[i] = (short)(Math.Clamp(v * .35, -1, 1) * short.MaxValue); // 작게
        }
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        foreach (var v in s) w.Write(v);
        return ms.ToArray();
    }
}
