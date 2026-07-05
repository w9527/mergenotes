using System.Globalization;

namespace MergerNotes.App.Localization;

public enum AppLanguage
{
    System = 0,
    English = 1,
    SimplifiedChinese = 2
}

public static class AppLanguageExtensions
{
    public static CultureInfo ResolveCulture(this AppLanguage language)
        => language switch
        {
            AppLanguage.English => CultureInfo.GetCultureInfo("en"),
            AppLanguage.SimplifiedChinese => CultureInfo.GetCultureInfo("zh-Hans"),
            _ => GetSystemCulture()
        };

    public static string DisplayName(this AppLanguage language)
        => language switch
        {
            AppLanguage.English => "English",
            AppLanguage.SimplifiedChinese => "简体中文",
            _ => "System / 跟随系统"
        };

    public static AppLanguage FromCulture(CultureInfo culture)
    {
        var language = culture.Name.ToLowerInvariant();
        if (language.StartsWith("zh-cn") || language.StartsWith("zh-hans") || language.StartsWith("zh-sg") || language.StartsWith("zh"))
        {
            return AppLanguage.SimplifiedChinese;
        }

        return AppLanguage.English;
    }

    private static CultureInfo GetSystemCulture()
    {
        var culture = CultureInfo.CurrentUICulture;
        return culture;
    }
}
