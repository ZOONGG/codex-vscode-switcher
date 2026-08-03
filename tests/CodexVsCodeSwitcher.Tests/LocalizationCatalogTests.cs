using System.Globalization;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class LocalizationCatalogTests
{
    [Fact]
    public void RussianCatalogCoversEnglishKeys()
    {
        Assert.Empty(LocalizationCatalog.MissingRussianKeys());
    }

    [Fact]
    public void SystemDefaultUsesRussianForRussianWindowsCulture()
    {
        LanguagePreference resolved = LocalizationCatalog.Resolve(LanguagePreference.SystemDefault, new CultureInfo("ru-RU"));

        Assert.Equal(LanguagePreference.Russian, resolved);
    }

    [Fact]
    public void FormatsSwitchProgressWithoutTranslatingProfileName()
    {
        string text = LocalizationCatalog.Text(LanguagePreference.Russian, "SwitchingToProfile", "work-account");

        Assert.Contains("work-account", text);
        Assert.StartsWith("Переключение", text, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusLegendAndUnavailableStateAreLocalized()
    {
        foreach (LanguagePreference language in new[] { LanguagePreference.English, LanguagePreference.Russian })
        {
            Assert.NotEqual("IndicatorLegend", LocalizationCatalog.Text(language, "IndicatorLegend"));
            Assert.NotEqual("RecommendedProfile", LocalizationCatalog.Text(language, "RecommendedProfile"));
            Assert.NotEqual("AutomaticLimitsUnavailable", LocalizationCatalog.Text(language, "AutomaticLimitsUnavailable"));
            Assert.NotEqual("ClearManualStatus", LocalizationCatalog.Text(language, "ClearManualStatus"));
        }
    }

    [Fact]
    public void ManualOverlayRevealGuidanceIsLocalized()
    {
        foreach (LanguagePreference language in new[] { LanguagePreference.English, LanguagePreference.Russian })
        {
            Assert.NotEqual(
                "SelectProfileToShowOverlay",
                LocalizationCatalog.Text(language, "SelectProfileToShowOverlay"));
            Assert.NotEqual(
                "LaunchManagedVsCodeToShowOverlay",
                LocalizationCatalog.Text(language, "LaunchManagedVsCodeToShowOverlay"));
            Assert.NotEqual(
                "FocusManagedVsCodeToShowOverlay",
                LocalizationCatalog.Text(language, "FocusManagedVsCodeToShowOverlay"));
            Assert.NotEqual(
                "ChooseProfileTitle",
                LocalizationCatalog.Text(language, "ChooseProfileTitle"));
            Assert.NotEqual(
                "CodexExtensionRequiredForLaunch",
                LocalizationCatalog.Text(language, "CodexExtensionRequiredForLaunch", "profile"));
            Assert.NotEqual(
                "InstallAndContinue",
                LocalizationCatalog.Text(language, "InstallAndContinue"));
        }
    }

    [Fact]
    public void IndicatorLegendMatchesEnglishAndRussianProductCopy()
    {
        Assert.Equal("Recommended profile", LocalizationCatalog.Text(LanguagePreference.English, "RecommendedProfile"));
        Assert.Equal("It currently has the highest available capacity among checked profiles.", LocalizationCatalog.Text(LanguagePreference.English, "RecommendedProfileHelp"));
        Assert.Equal("High capacity", LocalizationCatalog.Text(LanguagePreference.English, "HighCapacity"));
        Assert.Equal("Medium capacity", LocalizationCatalog.Text(LanguagePreference.English, "MediumCapacity"));
        Assert.Equal("Low or exhausted", LocalizationCatalog.Text(LanguagePreference.English, "LowCapacity"));

        Assert.Equal("Рекомендуемый профиль", LocalizationCatalog.Text(LanguagePreference.Russian, "RecommendedProfile"));
        Assert.Equal("Сейчас у него больше всего доступного лимита среди проверенных аккаунтов.", LocalizationCatalog.Text(LanguagePreference.Russian, "RecommendedProfileHelp"));
        Assert.Equal("Лимитов много", LocalizationCatalog.Text(LanguagePreference.Russian, "HighCapacity"));
        Assert.Equal("Средний остаток", LocalizationCatalog.Text(LanguagePreference.Russian, "MediumCapacity"));
        Assert.Equal("Лимит почти закончился", LocalizationCatalog.Text(LanguagePreference.Russian, "LowCapacity"));
    }

    [Fact]
    public void WorkspaceAndCodexEntryPointCopyIsCompleteInEnglishAndRussian()
    {
        string[] keys =
        [
            "CurrentProject",
            "LastProject",
            "RecentProjects",
            "OpenCodexNow",
            "LaunchCodexSidebar",
            "CodexReady",
            "CodexStillLoading",
            "CodexSidebarOpenFailed",
            "FirstLaunchProjectTitle",
            "RemoveFromRecent",
        ];
        foreach (string key in keys)
        {
            Assert.NotEqual(key, LocalizationCatalog.Text(LanguagePreference.English, key));
            Assert.NotEqual(key, LocalizationCatalog.Text(LanguagePreference.Russian, key));
        }

        Assert.Equal("Open Codex automatically", LocalizationCatalog.Text(LanguagePreference.English, "LaunchCodexSidebar"));
        Assert.Equal("Автоматически открывать Codex", LocalizationCatalog.Text(LanguagePreference.Russian, "LaunchCodexSidebar"));
    }

    [Fact]
    public void ProjectChooserAndRecoveryWordingMatchesProductCopy()
    {
        Assert.Equal("Which project should Codex open?", LocalizationCatalog.Text(LanguagePreference.English, "FirstLaunchProjectTitle"));
        Assert.Equal("Choose project folder", LocalizationCatalog.Text(LanguagePreference.English, "ChooseFolder"));
        Assert.Equal("Choose .code-workspace file", LocalizationCatalog.Text(LanguagePreference.English, "ChooseWorkspaceFile"));
        Assert.Equal("Open without a project", LocalizationCatalog.Text(LanguagePreference.English, "OpenEmptyWindow"));

        Assert.Equal("Какой проект открыть?", LocalizationCatalog.Text(LanguagePreference.Russian, "FirstLaunchProjectTitle"));
        Assert.Equal("Выбрать папку проекта", LocalizationCatalog.Text(LanguagePreference.Russian, "ChooseFolder"));
        Assert.Equal("Выбрать файл проекта .code-workspace", LocalizationCatalog.Text(LanguagePreference.Russian, "ChooseWorkspaceFile"));
        Assert.Equal("Открыть без проекта", LocalizationCatalog.Text(LanguagePreference.Russian, "OpenEmptyWindow"));
        Assert.Equal("Повторить запуск", LocalizationCatalog.Text(LanguagePreference.Russian, "RetryLaunch"));
        Assert.Equal("Сбросить состояние запуска", LocalizationCatalog.Text(LanguagePreference.Russian, "ResetLaunchState"));
    }
}
