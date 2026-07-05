using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace MergerNotes.App.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private const string SettingsDirectoryName = "MergerNotes";
    private const string SettingsFileName = "settings.json";

    private readonly string _settingsPath;
    private readonly CultureInfo _systemCulture;
    private AppLanguage _selectedLanguage;
    private LocalizedStrings _strings;

    public LocalizationService()
    {
        _systemCulture = CultureInfo.CurrentUICulture;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, SettingsDirectoryName);
        _settingsPath = Path.Combine(directory, SettingsFileName);
        _selectedLanguage = LoadLanguage();
        _strings = LocalizedStringsFactory.Create(EffectiveLanguage);
        ApplyCulture(EffectiveCulture);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (_selectedLanguage == value)
            {
                return;
            }

            _selectedLanguage = value;
            _strings = LocalizedStringsFactory.Create(EffectiveLanguage);
            ApplyCulture(EffectiveCulture);
            SaveLanguage(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLanguage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLanguageDisplayName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Strings)));
        }
    }

    public LocalizedStrings Strings => _strings;

    public string SelectedLanguageDisplayName => SelectedLanguage.DisplayName();

    public IReadOnlyList<LanguageChoice> LanguageChoices { get; } =
    [
        new LanguageChoice(AppLanguage.System, AppLanguage.System.DisplayName()),
        new LanguageChoice(AppLanguage.English, AppLanguage.English.DisplayName()),
        new LanguageChoice(AppLanguage.SimplifiedChinese, AppLanguage.SimplifiedChinese.DisplayName())
    ];

    public CultureInfo EffectiveCulture => SelectedLanguage == AppLanguage.System
        ? AppLanguageExtensions.FromCulture(_systemCulture).ResolveCulture()
        : SelectedLanguage.ResolveCulture();

    public AppLanguage EffectiveLanguage => SelectedLanguage == AppLanguage.System
        ? AppLanguageExtensions.FromCulture(_systemCulture)
        : SelectedLanguage;

    public void RefreshFromSystemCulture()
    {
        if (SelectedLanguage != AppLanguage.System)
        {
            return;
        }

        _strings = LocalizedStringsFactory.Create(EffectiveLanguage);
        ApplyCulture(EffectiveCulture);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Strings)));
    }

    private static void ApplyCulture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private AppLanguage LoadLanguage()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return AppLanguage.System;
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings?.Language ?? AppLanguage.System;
        }
        catch
        {
            return AppLanguage.System;
        }
    }

    private void SaveLanguage(AppLanguage language)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var settings = new AppSettings(language);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
        }
    }
}

public sealed record AppSettings(AppLanguage Language);

public sealed record LanguageChoice(AppLanguage Language, string DisplayName);
