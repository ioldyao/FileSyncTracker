using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using System;
using System.Diagnostics;
using System.Globalization;

namespace FileSyncTracker.UI.Services;

public class LocalizationService
{
    private static LocalizationService? _instance;
    public static LocalizationService Instance => _instance ??= new LocalizationService();

    private ResourceInclude? _currentLangResource;

    public event EventHandler? LanguageChanged;

    public string CurrentLanguage { get; private set; } = "en";

    private LocalizationService()
    {
        DetectSystemLanguage();
    }

    private void DetectSystemLanguage()
    {
        var culture = CultureInfo.CurrentUICulture;
        CurrentLanguage = culture.TwoLetterISOLanguageName switch
        {
            "zh" => "zh-CN",
            _ => "en"
        };
        Debug.WriteLine($"[Localization] Detected: {CurrentLanguage} (UI culture: {culture.Name})");
    }

    public void ApplyLanguage(Application app)
    {
        if (CurrentLanguage == "en")
        {
            Debug.WriteLine("[Localization] English - using default Strings from App.axaml");
            return;
        }

        try
        {
            var fileName = $"Strings.{CurrentLanguage}.axaml";
            var uri = new Uri($"avares://FileSyncTracker.UI/Strings/{fileName}");

            Debug.WriteLine($"[Localization] Loading: {uri}");

            _currentLangResource = new ResourceInclude(uri)
            {
                Source = uri
            };
            app.Resources.MergedDictionaries.Add(_currentLangResource);

            Debug.WriteLine($"[Localization] Applied {CurrentLanguage}, dictionaries: {app.Resources.MergedDictionaries.Count}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Localization] Failed to load {CurrentLanguage}: {ex.Message}");
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetLanguage(Application app, string language)
    {
        if (_currentLangResource != null)
            app.Resources.MergedDictionaries.Remove(_currentLangResource);

        CurrentLanguage = language;
        ApplyLanguage(app);
    }
}
