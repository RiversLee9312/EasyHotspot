using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace EasyHotspot;

public static class Localization
{
    private static readonly string[] supportedLanguages =
    [
        "en-US", "zh-CN", "ja-JP", "ko-KR", "de-DE", "fr-FR", "es-ES", "pt-BR",
        "it-IT", "nl-NL", "pl-PL", "ru-RU", "uk-UA", "tr-TR", "sv-SE", "da-DK",
        "fi-FI", "cs-CZ", "ar-SA", "he-IL", "hi-IN", "id-ID", "vi-VN", "th-TH"
    ];

    private static ResourceLoader? loader;
    private static string? preferredLanguage;
    private static bool preferenceLoaded;
    private static readonly string languageFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EasyHotspot", "language.txt");

    public static IReadOnlyList<string> SupportedLanguages => supportedLanguages;
    public static CultureInfo FormattingCulture => CultureInfo.GetCultureInfo(CurrentLanguage);

    public static string CurrentLanguage => preferredLanguage ?? ResolveSystemLanguage();

    public static bool IsRightToLeft(string language) =>
        language.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
        || language.StartsWith("he", StringComparison.OrdinalIgnoreCase);

    public static void ApplyPreferredLanguage()
    {
        if(!preferenceLoaded){preferredLanguage=ReadPreferredLanguage();preferenceLoaded=true;}
        var language = preferredLanguage;
        if (language is not null)
            ApplicationLanguages.PrimaryLanguageOverride = language;
    }
    public static async Task InitializeAsync()
    {
        if(!preferenceLoaded){preferredLanguage=await Task.Run(ReadPreferredLanguage);preferenceLoaded=true;}
        ApplyPreferredLanguage();
    }

    public static void SavePreferredLanguage(string language)
    {
        if (!supportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentOutOfRangeException(nameof(language), "Unsupported language.");

        Directory.CreateDirectory(Path.GetDirectoryName(languageFile)!);
        File.WriteAllText(languageFile, language);
        preferredLanguage=language;preferenceLoaded=true;
    }

    public static string NativeName(string language)
    {
        try { return CultureInfo.GetCultureInfo(language).NativeName; }
        catch (CultureNotFoundException) { return language; }
    }

    public static string Get(string key)
    {
        try
        {
            loader ??= new ResourceLoader(ResourceLoader.GetDefaultResourceFilePath(), "Resources");
            var value = loader.GetString(key);
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch
        {
            return key;
        }
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(FormattingCulture, Get(key), args);

    public static string Error(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw ?? string.Empty;

        var parts = raw.Split('|');
        if (!parts[0].StartsWith("Error_", StringComparison.Ordinal)) return raw;

        var template = Get(parts[0]);
        if (template == parts[0]) return raw;
        try
        {
            return string.Format(FormattingCulture, template, parts.Skip(1).Cast<object>().ToArray());
        }
        catch (FormatException)
        {
            return raw;
        }
    }

    public static string Log(string raw)=>raw.Length>=10 && raw[2]==':' && raw[5]==':' && raw.Substring(8,2)=="  "
        ?raw[..10]+Error(raw[10..]):Error(raw);

    private static string? ReadPreferredLanguage()
    {
        try
        {
            var value = File.ReadAllText(languageFile).Trim();
            return supportedLanguages.FirstOrDefault(x =>
                string.Equals(x, value, StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static string ResolveSystemLanguage()
    {
        var current = CultureInfo.CurrentUICulture.Name;
        var exact = supportedLanguages.FirstOrDefault(x =>
            string.Equals(x, current, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var neutral = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return supportedLanguages.FirstOrDefault(x =>
            string.Equals(CultureInfo.GetCultureInfo(x).TwoLetterISOLanguageName, neutral,
                StringComparison.OrdinalIgnoreCase)) ?? "en-US";
    }
}
