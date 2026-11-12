using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Bolok;

// 다국어 (YELL 2026-11-12): 문장은 assets/lang.json (키 → 언어 → 문장) 한 곳에. 기준(영어·한국어)은 tools/lang_base.py.
// 처음엔 윈도우 언어를 따르고, 설정 → 일반에서 바꿀 수 있다. 빠진 번역은 영어로.
// 화면에 직접 그리는 것(게임·선물·앨범·엽서·꿈)은 글자 없이 그림·숫자·기호만 쓴다. 문장은 메뉴·설정 창·안내 말풍선에만
public static class L
{
    public static readonly (string Code, string Native)[] Languages =
    {
        ("en", "English"), ("ko", "한국어"), ("ja", "日本語"), ("zh-Hans", "简体中文"), ("zh-Hant", "繁體中文"),
        ("es", "Español"), ("de", "Deutsch"), ("fr", "Français"), ("pt", "Português"), ("ru", "Русский"),
    };

    static readonly Dictionary<string, Dictionary<string, string>> table = Load();
    public static string Code { get; private set; } = "en";
    public static event Action? Changed;

    static Dictionary<string, Dictionary<string, string>> Load()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("lang.json");
            return s == null ? new() : JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(s) ?? new();
        }
        catch (JsonException) { return new(); } // 깨진 파일이면 키가 그대로 보인다 (앱은 산다)
    }

    // 윈도우 표시 언어에서 고른다. 중국어는 대만·홍콩·마카오면 번체
    public static string FromWindows()
    {
        var c = CultureInfo.CurrentUICulture;
        if (c.Name.StartsWith("zh")) return c.Name is "zh-TW" or "zh-HK" or "zh-MO" || c.Name.Contains("Hant") ? "zh-Hant" : "zh-Hans";
        return Languages.Any(l => l.Code == c.TwoLetterISOLanguageName) ? c.TwoLetterISOLanguageName : "en";
    }

    // 고른 언어(없거나 모르는 값이면 윈도우 언어)로 바꾼다
    public static void Use(string? code)
    {
        string next = code != null && Languages.Any(l => l.Code == code) ? code : FromWindows();
        if (next == Code) return;
        Code = next;
        Changed?.Invoke();
    }

    public static string T(string key) =>
        table.TryGetValue(key, out var e) ? e.TryGetValue(Code, out var s) ? s : e.TryGetValue("en", out var en) ? en : key : key;

    public static string T(string key, params object?[] args) => string.Format(Culture, T(key), args);

    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Code switch { "zh-Hans" => "zh-CN", "zh-Hant" => "zh-TW", "pt" => "pt-BR", _ => Code });

    // 언어마다 그 글자가 예쁘게 나오는 윈도우 기본 글꼴
    public static string Font => Code switch
    {
        "ko" => "Malgun Gothic", "ja" => "Yu Gothic UI", "zh-Hans" => "Microsoft YaHei UI", "zh-Hant" => "Microsoft JhengHei UI", _ => "Segoe UI",
    };

    public static string Name => T("app.name");
    public static string Species(string id) => T("sp." + id);
    public static string Stage(string stage) => T("stage." + stage);

    // 한국어에만 조사를 붙인다 (받침이 있으면 b): With("여우", "를", "을") → "여우를"
    public static string With(string word, string a, string b) => Code == "ko" ? word + Names.Josa(word, a, b) : word;

    public static string Date(DateTime d) => d.ToString(Culture.DateTimeFormat.MonthDayPattern, Culture);

    // 3분, 2시간 5분, 1일 3시간
    public static string Span(double min)
    {
        int m = Math.Max(0, (int)Math.Ceiling(min));
        if (m < 60) return T("time.min", m);
        int h = m / 60, mm = m % 60;
        if (h < 24) return mm > 0 ? T("time.hm", h, mm) : T("time.h", h);
        int d = h / 24, hh = h % 24;
        return hh > 0 ? T("time.dh", d, hh) : T("time.d", d);
    }

    // 자가 검사용
    public static IEnumerable<string> Keys => table.Keys;
    public static bool Has(string key, string code) => table.TryGetValue(key, out var e) && e.ContainsKey(code);
    public static string Raw(string key, string code) => table[key][code];
}
