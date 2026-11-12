namespace Bolok;

// README 용 움직이는 그림 (Bolok.exe --art-gif assets/parade.gif): 34종이 걷다가 웃고 윙크한다 (종마다 박자를 어긋나게).
// 움직이는 GIF 는 GDI+ 로 만들면 색이 뭉개져서 직접 쓴다 (배경 투명, 무한 반복, 쓰는 색 255개까지)
static class Gif
{
    public static void Parade(PetArt art, string path, int cols = 9, int scale = 2)
    {
        var seq = Enumerable.Repeat(Enumerable.Range(0, 4).Select(i => ("walk", i)), 2).SelectMany(x => x)
            .Concat(new[] { ("happy", 0), ("happy", 1), ("wink", 0), ("idle", 3), ("love", 0), ("love", 1) }).ToArray();
        var species = art.Species;
        int cell = PetArt.Cell, rows = (species.Count + cols - 1) / cols, w = cols * cell * scale, h = rows * cell * scale;
        var strips = new Dictionary<(string, string), (uint[] Px, int Width, int Frames)>();
        var frames = new List<uint[]>();
        for (int t = 0; t < seq.Length; t++)
        {
            var fr = new uint[w * h];
            for (int n = 0; n < species.Count; n++)
            {
                var (pose, f) = seq[(t + n * 5) % seq.Length];
                var key = (species[n], pose);
                if (!strips.TryGetValue(key, out var st))
                {
                    var (bmp, count) = art.Row(species[n], $"child/1/{pose}");
                    using (bmp) strips[key] = st = (Pixels(bmp), bmp.Width, count);
                }
                int row = n / cols, inRow = Math.Min(cols, species.Count - row * cols);
                int oy = row * cell * scale, ox = n % cols * cell * scale + (cols - inRow) * cell * scale / 2; // 마지막 줄은 가운데로
                int fi = f % st.Frames;
                for (int y = 0; y < cell; y++)
                    for (int x = 0; x < cell; x++)
                    {
                        uint c = st.Px[y * st.Width + fi * cell + x];
                        if (c >>> 24 == 0) continue;
                        for (int a = 0; a < scale; a++)
                            for (int b = 0; b < scale; b++) fr[(oy + y * scale + a) * w + ox + x * scale + b] = c;
                    }
            }
            frames.Add(fr);
        }
        Write(frames, w, h, path, 160);
    }

    static uint[] Pixels(Bitmap bmp)
    {
        var px = new uint[bmp.Width * bmp.Height];
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++) px[y * bmp.Width + x] = (uint)bmp.GetPixel(x, y).ToArgb();
        return px;
    }

    // frames: ARGB (투명은 알파 0). 많이 쓴 색부터 255개, 0번은 투명
    public static void Write(List<uint[]> frames, int w, int h, string path, int delayMs)
    {
        var count = new Dictionary<uint, int>();
        foreach (var fr in frames)
            foreach (var c in fr)
                if (c >>> 24 != 0) count[c & 0xFFFFFF] = count.GetValueOrDefault(c & 0xFFFFFF) + 1;
        var colors = count.OrderByDescending(kv => kv.Value).Take(255).Select(kv => kv.Key).ToList();
        var index = colors.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => (byte)(t.i + 1));
        byte Near(uint c)
        {
            if (index.TryGetValue(c, out var i)) return i;
            int Ch(uint v, int s) => (int)(v >> s & 255);
            int best = 0, bestD = int.MaxValue;
            for (int k = 0; k < colors.Count; k++)
            {
                int d = 0;
                foreach (int s in new[] { 16, 8, 0 }) d += (Ch(colors[k], s) - Ch(c, s)) * (Ch(colors[k], s) - Ch(c, s));
                if (d < bestD) (best, bestD) = (k, d);
            }
            return index[c] = (byte)(best + 1);
        }
        using var o = new BinaryWriter(File.Create(path));
        o.Write("GIF89a"u8);
        o.Write((ushort)w); o.Write((ushort)h); o.Write((byte)0xF7); o.Write((byte)0); o.Write((byte)0);
        o.Write(new byte[] { 0, 0, 0 });
        foreach (var c in colors) o.Write(new[] { (byte)(c >> 16), (byte)(c >> 8), (byte)c });
        o.Write(new byte[3 * (255 - colors.Count)]);
        o.Write(new byte[] { 0x21, 0xFF, 0x0B }); o.Write("NETSCAPE2.0"u8); o.Write(new byte[] { 3, 1, 0, 0, 0 }); // 무한 반복
        foreach (var fr in frames)
        {
            o.Write(new byte[] { 0x21, 0xF9, 4, (2 << 2) | 1 }); o.Write((ushort)(delayMs / 10)); o.Write(new byte[] { 0, 0 }); // 다음 장 전에 지우기, 0번 투명
            o.Write((byte)0x2C); o.Write((ushort)0); o.Write((ushort)0); o.Write((ushort)w); o.Write((ushort)h); o.Write((byte)0);
            o.Write((byte)8);
            var packed = Lzw(fr.Select(c => c >>> 24 == 0 ? (byte)0 : Near(c & 0xFFFFFF)).ToArray(), 8);
            for (int i = 0; i < packed.Length; i += 255)
            {
                int n = Math.Min(255, packed.Length - i);
                o.Write((byte)n); o.Write(packed, i, n);
            }
            o.Write((byte)0);
        }
        o.Write((byte)0x3B);
    }

    // GIF 용 LZW (가변 길이 코드, 4096 에서 다시 시작)
    static byte[] Lzw(byte[] data, int minSize)
    {
        int clear = 1 << minSize, end = clear + 1, size = minSize + 1, next = end + 1, bits = 0, nbits = 0;
        var table = new Dictionary<(int, byte), int>();
        var o = new List<byte>();
        void Put(int code)
        {
            bits |= code << nbits;
            nbits += size;
            while (nbits >= 8) { o.Add((byte)(bits & 255)); bits >>= 8; nbits -= 8; }
        }
        Put(clear);
        int w = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            byte k = data[i];
            if (table.TryGetValue((w, k), out var hit)) { w = hit; continue; }
            Put(w);
            if (next < 4096)
            {
                table[(w, k)] = next++;
                if (next > 1 << size && size < 12) size++; // 해독기는 한 칸 늦게 표를 채우니 하나 넘었을 때 늘린다
            }
            else { Put(clear); table.Clear(); (size, next) = (minSize + 1, end + 1); }
            w = k;
        }
        Put(w);
        Put(end);
        if (nbits > 0) o.Add((byte)(bits & 255));
        return o.ToArray();
    }
}
