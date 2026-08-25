using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace MikuDanceProject.Core;

internal enum DanceConfigKey
{
    ModEnabled,
    EnableAudio,
    AudioVolume,
    AudioRangeMeters,
    StabilizeModelLighting,
    LightingExposureCompensation,
    ModelScale,
    SpawnModelKey,
    HairColorEnabled,
    HairColorR,
    HairColorG,
    HairColorB,
    ClothColorEnabled,
    ClothColorR,
    ClothColorG,
    ClothColorB,
    RandomizeColors,
    EnableVerboseLogs,
}

internal sealed class LocalizedConfigEntry
{
    public LocalizedConfigEntry(DanceConfigKey key, ConfigEntryBase entry)
    {
        Key = key;
        Entry = entry;
    }

    public DanceConfigKey Key { get; }
    public ConfigEntryBase Entry { get; }
}

internal static class DanceConfigLocalization
{
    private const int SimplifiedChineseLanguageIndex = 9;
    private const string SettingsSection = "Settings";
    private const string LocalizedSettingsSection = "设置";
    private const string LegacyInternalSection = "Internal";
    private const string LegacySavedAirportPlacementKey = "SavedAirportPlacement";
    private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static bool DetectChineseLanguage()
    {
        var hasConfiguredLanguage = TryGetConfiguredGameLanguage(out var configuredChineseLanguage);
        var hasRuntimeLanguage = TryGetLocalizedTextLanguageName(out var languageName);
        if (hasConfiguredLanguage)
        {
            return configuredChineseLanguage;
        }

        if (hasRuntimeLanguage)
        {
            return IsChineseLanguageName(languageName);
        }

        return CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            || CultureInfo.CurrentCulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            || TimeZoneInfo.Local.Id.IndexOf("China", StringComparison.OrdinalIgnoreCase) >= 0
            || TimeZoneInfo.Local.Id.IndexOf("Shanghai", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string BuildLanguageDetectionSummary(bool isChineseLanguage)
    {
        var configuredText = TryGetConfiguredGameLanguage(out var configuredChineseLanguage, out var configuredValue)
            ? configuredValue + "/" + (configuredChineseLanguage ? "Chinese" : "English")
            : "unknown";
        var runtimeText = TryGetLocalizedTextLanguageName(out var runtimeLanguage)
            ? runtimeLanguage
            : "unknown";
        return (isChineseLanguage ? "Chinese" : "English") + " (prefs=" + configuredText + ", runtime=" + runtimeText + ")";
    }

    public static string GetSectionName(bool isChineseLanguage)
    {
        return isChineseLanguage ? LocalizedSettingsSection : SettingsSection;
    }

    public static string GetSavedAirportPlacementKeyName(bool isChineseLanguage)
    {
        return isChineseLanguage ? "\u4fdd\u5b58\u7684\u673a\u573a\u5c55\u793a\u4f4d\u7f6e" : "Saved Airport Placement";
    }

    public static string GetSavedAirportPlacementDescription(bool isChineseLanguage)
    {
        return isChineseLanguage
            ? "\u5185\u90e8\u4f7f\u7528\u7684\u673a\u573a\u5c55\u793a\u4f4d\u7f6e\u3002\u63d2\u4ef6\u4f1a\u81ea\u52a8\u7ba1\u7406\u8fd9\u4e2a\u503c\u3002"
            : "Internal serialized Airport showcase placement. Managed automatically by the plugin.";
    }

    public static string GetKeyName(DanceConfigKey configKey, bool isChineseLanguage)
    {
        if (isChineseLanguage)
        {
            switch (configKey)
            {
                case DanceConfigKey.ModEnabled:
                    return "启用 Miku 展示";
                case DanceConfigKey.EnableAudio:
                    return "启用音乐";
                case DanceConfigKey.AudioVolume:
                    return "音乐音量";
                case DanceConfigKey.AudioRangeMeters:
                    return "音乐可听范围";
                case DanceConfigKey.StabilizeModelLighting:
                    return "稳定模型光照";
                case DanceConfigKey.LightingExposureCompensation:
                    return "肤色亮度";
                case DanceConfigKey.ModelScale:
                    return "模型大小";
                case DanceConfigKey.SpawnModelKey:
                    return "移动模型按键";
                case DanceConfigKey.HairColorEnabled:
                    return "启用头发颜色";
                case DanceConfigKey.HairColorR:
                    return "头发颜色 R";
                case DanceConfigKey.HairColorG:
                    return "头发颜色 G";
                case DanceConfigKey.HairColorB:
                    return "头发颜色 B";
                case DanceConfigKey.ClothColorEnabled:
                    return "启用服装颜色";
                case DanceConfigKey.ClothColorR:
                    return "服装颜色 R";
                case DanceConfigKey.ClothColorG:
                    return "服装颜色 G";
                case DanceConfigKey.ClothColorB:
                    return "服装颜色 B";
                case DanceConfigKey.RandomizeColors:
                    return "随机颜色";
                case DanceConfigKey.EnableVerboseLogs:
                    return "调试日志";
            }
        }

        switch (configKey)
        {
            case DanceConfigKey.ModEnabled:
                return "Enable Miku Showcase";
            case DanceConfigKey.EnableAudio:
                return "Enable Audio";
            case DanceConfigKey.AudioVolume:
                return "Audio Volume";
            case DanceConfigKey.AudioRangeMeters:
                return "Audio Range Meters";
            case DanceConfigKey.StabilizeModelLighting:
                return "Stabilize Model Lighting";
            case DanceConfigKey.LightingExposureCompensation:
                return "Skin Tone Brightness";
            case DanceConfigKey.ModelScale:
                return "Model Scale";
            case DanceConfigKey.SpawnModelKey:
                return "Spawn Model Key";
            case DanceConfigKey.HairColorEnabled:
                return "Hair Color Enabled";
            case DanceConfigKey.HairColorR:
                return "Hair Color R";
            case DanceConfigKey.HairColorG:
                return "Hair Color G";
            case DanceConfigKey.HairColorB:
                return "Hair Color B";
            case DanceConfigKey.ClothColorEnabled:
                return "Cloth Color Enabled";
            case DanceConfigKey.ClothColorR:
                return "Cloth Color R";
            case DanceConfigKey.ClothColorG:
                return "Cloth Color G";
            case DanceConfigKey.ClothColorB:
                return "Cloth Color B";
            case DanceConfigKey.RandomizeColors:
                return "Randomize Colors";
            case DanceConfigKey.EnableVerboseLogs:
                return "Verbose Logs";
            default:
                return string.Empty;
        }
    }

    public static string GetDescription(DanceConfigKey configKey, bool isChineseLanguage)
    {
        if (isChineseLanguage)
        {
            switch (configKey)
            {
                case DanceConfigKey.ModEnabled:
                    return "总开关。关闭后插件仍会加载，但不会生成或移动展示模型。";
                case DanceConfigKey.EnableAudio:
                    return "是否播放随展示模型一起打包的音乐。";
                case DanceConfigKey.AudioVolume:
                    return "打包音乐源使用的音量，范围 0 到 1。";
                case DanceConfigKey.AudioRangeMeters:
                    return "打包音乐源的最大可听距离，单位为米。";
                case DanceConfigKey.StabilizeModelLighting:
                    return "开启后会调整运行时材质，降低强光环境下模型过曝发白。";
                case DanceConfigKey.LightingExposureCompensation:
                    return "皮肤与脸部的亮度倍率。数值越低，肤色越暗；数值越高，肤色越亮。";
                case DanceConfigKey.ModelScale:
                    return "生成展示模型时使用的统一缩放倍率。";
                case DanceConfigKey.SpawnModelKey:
                    return "轻按此键会把模型移动到本地玩家附近的地面位置。在机场大厅按住 1 秒，会把模型移动到玩家位置并保存到配置文件供下次启动使用。";
                case DanceConfigKey.HairColorEnabled:
                    return "开启后用下方 RGB 滑块覆盖头发颜色（与原纹理相乘叠加）。关闭则使用模型原始颜色。";
                case DanceConfigKey.HairColorR:
                    return "头发颜色红色通道，范围 0 到 1。";
                case DanceConfigKey.HairColorG:
                    return "头发颜色绿色通道，范围 0 到 1。";
                case DanceConfigKey.HairColorB:
                    return "头发颜色蓝色通道，范围 0 到 1。";
                case DanceConfigKey.ClothColorEnabled:
                    return "开启后用下方 RGB 滑块覆盖服装颜色（与原纹理相乘叠加）。关闭则使用模型原始颜色。";
                case DanceConfigKey.ClothColorR:
                    return "服装颜色红色通道，范围 0 到 1。";
                case DanceConfigKey.ClothColorG:
                    return "服装颜色绿色通道，范围 0 到 1。";
                case DanceConfigKey.ClothColorB:
                    return "服装颜色蓝色通道，范围 0 到 1。";
                case DanceConfigKey.RandomizeColors:
                    return "开启后，每次生成模型时随机生成头发与服装颜色（覆盖下方 RGB 滑块的值）。每次按键生成颜色都不同。默认关闭。";
                case DanceConfigKey.EnableVerboseLogs:
                    return "开启后输出详细调试日志（资源加载、材质、动画同步等）。默认关闭，仅在排查问题时开启。";
            }
        }

        switch (configKey)
        {
            case DanceConfigKey.ModEnabled:
                return "Master switch. When disabled, the plugin stays loaded but will not spawn or move the display model.";
            case DanceConfigKey.EnableAudio:
                return "Whether the display model plays the bundled audio track.";
            case DanceConfigKey.AudioVolume:
                return "Audio volume applied to the bundled audio source. Range: 0 to 1.";
            case DanceConfigKey.AudioRangeMeters:
                return "Maximum audible range for the bundled audio source, in meters.";
            case DanceConfigKey.StabilizeModelLighting:
                return "When enabled, runtime materials are tuned to reduce over-bright scene lighting on the bundled model.";
            case DanceConfigKey.LightingExposureCompensation:
                return "Brightness multiplier for skin and face materials. Lower values darken the skin tone; higher values brighten it.";
            case DanceConfigKey.ModelScale:
                return "Uniform scale applied to the spawned display model.";
            case DanceConfigKey.SpawnModelKey:
                return "Tap this key to move the model to the local player's grounded position. Hold it for 1 second in the Airport lobby to move the model and save the lobby showcase position into this config file for future launches.";
            case DanceConfigKey.HairColorEnabled:
                return "When enabled, the RGB sliders below override the hair color (multiplied with the original texture). When disabled, the model's original color is used.";
            case DanceConfigKey.HairColorR:
                return "Hair color red channel, range 0 to 1.";
            case DanceConfigKey.HairColorG:
                return "Hair color green channel, range 0 to 1.";
            case DanceConfigKey.HairColorB:
                return "Hair color blue channel, range 0 to 1.";
            case DanceConfigKey.ClothColorEnabled:
                return "When enabled, the RGB sliders below override the cloth color (multiplied with the original texture). When disabled, the model's original color is used.";
            case DanceConfigKey.ClothColorR:
                return "Cloth color red channel, range 0 to 1.";
            case DanceConfigKey.ClothColorG:
                return "Cloth color green channel, range 0 to 1.";
            case DanceConfigKey.ClothColorB:
                return "Cloth color blue channel, 0 to 1.";
            case DanceConfigKey.RandomizeColors:
                return "When enabled, randomizes hair and cloth colors each time the model spawns (overrides the RGB sliders below). Each spawn produces different colors. Disabled by default.";
            case DanceConfigKey.EnableVerboseLogs:
                return "When enabled, emits verbose debug logs (asset loading, materials, animation sync, etc.). Off by default; turn on only when troubleshooting.";
            default:
                return string.Empty;
        }
    }

    public static bool TryGetConfigKey(string keyName, out DanceConfigKey configKey)
    {
        foreach (DanceConfigKey candidate in Enum.GetValues(typeof(DanceConfigKey)))
        {
            if (IsConfigKeyName(keyName, candidate))
            {
                configKey = candidate;
                return true;
            }
        }

        configKey = default;
        return false;
    }

    public static bool TryGetLocalizedSectionName(string sectionName, bool isChineseLanguage, out string localizedSectionName)
    {
        if (string.Equals(sectionName, SettingsSection, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sectionName, LocalizedSettingsSection, StringComparison.Ordinal)
            || string.Equals(sectionName, LegacyInternalSection, StringComparison.OrdinalIgnoreCase))
        {
            localizedSectionName = GetSectionName(isChineseLanguage);
            return true;
        }

        localizedSectionName = string.Empty;
        return false;
    }

    public static void ApplyLocalizedDescriptions(ConfigFile configFile, bool isChineseLanguage)
    {
        foreach (var entry in GetConfigEntriesSnapshot(configFile))
        {
            if (entry == null || entry.Definition == null || entry.Description == null || !TryGetConfigKey(entry.Definition.Key, out var configKey))
            {
                continue;
            }

            SetPrivateField(entry.Description, "<Description>k__BackingField", GetDescription(configKey, isChineseLanguage));
        }
    }

    public static void RewriteConfigFileLocalization(string configFilePath, bool isChineseLanguage)
    {
        if (string.IsNullOrWhiteSpace(configFilePath) || !File.Exists(configFilePath))
        {
            return;
        }

        var originalLines = File.ReadAllLines(configFilePath);
        var rewrittenLines = new string[originalLines.Length];
        var changed = false;

        for (var index = 0; index < originalLines.Length; index++)
        {
            var line = originalLines[index] ?? string.Empty;
            var rewrittenLine = RewriteConfigFileLine(line, isChineseLanguage);
            rewrittenLines[index] = rewrittenLine;
            changed |= !string.Equals(line, rewrittenLine, StringComparison.Ordinal);
        }

        if (changed)
        {
            File.WriteAllLines(configFilePath, rewrittenLines);
        }
    }

    public static void MigrateLocalizedConfigEntries(ConfigFile configFile, IEnumerable<LocalizedConfigEntry> visibleEntries)
    {
        var orphanedEntries = GetOrphanedEntries(configFile);
        if (orphanedEntries == null || orphanedEntries.Count == 0)
        {
            return;
        }

        var migratedAnyValue = false;
        foreach (var visibleEntry in visibleEntries)
        {
            migratedAnyValue |= TryMigrateLocalizedConfigValue(visibleEntry.Entry, visibleEntry.Key, orphanedEntries);
        }

        if (migratedAnyValue)
        {
            configFile.Save();
        }
    }

    public static void MigrateSavedAirportPlacement(ConfigFile configFile, ConfigEntryBase savedAirportPlacementEntry)
    {
        var orphanedEntries = GetOrphanedEntries(configFile);
        if (orphanedEntries == null || savedAirportPlacementEntry == null)
        {
            return;
        }

        foreach (var aliasDefinition in GetSavedAirportPlacementAliasDefinitions())
        {
            if (DefinitionsEqual(aliasDefinition, savedAirportPlacementEntry.Definition) || !orphanedEntries.Contains(aliasDefinition))
            {
                continue;
            }

            var orphanedValue = orphanedEntries[aliasDefinition];
            if (orphanedValue != null && string.IsNullOrWhiteSpace(savedAirportPlacementEntry.GetSerializedValue()))
            {
                savedAirportPlacementEntry.SetSerializedValue(orphanedValue.ToString());
            }

            orphanedEntries.Remove(aliasDefinition);
        }
    }

    public static Dictionary<string, string> BuildUiLocalizationMap(bool isChineseLanguage)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        AddUiLocalizationPair(map, PluginConstants.Name, GetLocalizedModDisplayName(isChineseLanguage));
        AddUiLocalizationPair(map, PluginConstants.Guid, GetLocalizedModDisplayName(isChineseLanguage));
        AddUiLocalizationPair(map, SettingsSection, GetSectionName(isChineseLanguage));
        AddUiLocalizationPair(map, LocalizedSettingsSection, GetSectionName(isChineseLanguage));
        AddUiLocalizationPair(map, LegacyInternalSection, GetSectionName(isChineseLanguage));
        AddUiLocalizationPair(map, LegacySavedAirportPlacementKey, GetSavedAirportPlacementKeyName(isChineseLanguage));
        AddUiLocalizationPair(map, GetSavedAirportPlacementKeyName(false), GetSavedAirportPlacementKeyName(isChineseLanguage));
        AddUiLocalizationPair(map, GetSavedAirportPlacementKeyName(true), GetSavedAirportPlacementKeyName(isChineseLanguage));

        foreach (DanceConfigKey configKey in Enum.GetValues(typeof(DanceConfigKey)))
        {
            var localizedSection = GetSectionName(isChineseLanguage);
            var localizedKey = GetKeyName(configKey, isChineseLanguage);
            AddUiLocalizationPair(map, SettingsSection, localizedSection);
            AddUiLocalizationPair(map, LocalizedSettingsSection, localizedSection);
            AddUiLocalizationPair(map, GetKeyName(configKey, false), localizedKey);
            AddUiLocalizationPair(map, ToReadableName(GetKeyName(configKey, false)), localizedKey);
            AddUiLocalizationPair(map, GetKeyName(configKey, true), localizedKey);
            AddUiLocalizationPair(map, GetLegacyKeyName(configKey), localizedKey);
            AddUiLocalizationPair(map, ToReadableName(GetLegacyKeyName(configKey)), localizedKey);
            AddUiLocalizationPair(map, GetDescription(configKey, false), GetDescription(configKey, isChineseLanguage));
            AddUiLocalizationPair(map, GetDescription(configKey, true), GetDescription(configKey, isChineseLanguage));
        }

        return map;
    }

    private static string RewriteConfigFileLine(string line, bool isChineseLanguage)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return line ?? string.Empty;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
        {
            var sectionName = trimmed.Substring(1, trimmed.Length - 2).Trim();
            if (!TryGetLocalizedSectionName(sectionName, isChineseLanguage, out var localizedSectionName))
            {
                return line;
            }

            var openIndex = line.IndexOf('[');
            var closeIndex = line.LastIndexOf(']');
            return openIndex < 0 || closeIndex < openIndex
                ? line
                : line.Substring(0, openIndex + 1) + localizedSectionName + line.Substring(closeIndex);
        }

        if (!TrySplitConfigSettingLine(line, out var leading, out var keyName, out var separatorAndValue)
            || (!TryGetConfigKey(keyName, out var configKey) && !IsSavedAirportPlacementKeyName(keyName)))
        {
            return line;
        }

        if (IsSavedAirportPlacementKeyName(keyName))
        {
            return leading + GetSavedAirportPlacementKeyName(isChineseLanguage) + separatorAndValue;
        }

        return leading + GetKeyName(configKey, isChineseLanguage) + separatorAndValue;
    }

    private static bool TrySplitConfigSettingLine(string line, out string leading, out string keyName, out string separatorAndValue)
    {
        leading = string.Empty;
        keyName = string.Empty;
        separatorAndValue = string.Empty;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal))
        {
            return false;
        }

        var equalsIndex = line.IndexOf('=');
        if (equalsIndex <= 0)
        {
            return false;
        }

        var keyStart = 0;
        while (keyStart < equalsIndex && char.IsWhiteSpace(line[keyStart]))
        {
            keyStart++;
        }

        var keyEnd = equalsIndex - 1;
        while (keyEnd >= keyStart && char.IsWhiteSpace(line[keyEnd]))
        {
            keyEnd--;
        }

        if (keyEnd < keyStart)
        {
            return false;
        }

        leading = line.Substring(0, keyStart);
        keyName = line.Substring(keyStart, keyEnd - keyStart + 1);
        separatorAndValue = line.Substring(keyEnd + 1);
        return true;
    }

    private static bool TryMigrateLocalizedConfigValue(ConfigEntryBase entry, DanceConfigKey configKey, IDictionary orphanedEntries)
    {
        if (entry == null || entry.Definition == null || orphanedEntries == null)
        {
            return false;
        }

        var migrated = false;
        foreach (var aliasDefinition in GetAliasDefinitions(configKey))
        {
            if (DefinitionsEqual(aliasDefinition, entry.Definition) || !orphanedEntries.Contains(aliasDefinition))
            {
                continue;
            }

            if (!migrated)
            {
                var orphanedValue = orphanedEntries[aliasDefinition];
                if (orphanedValue != null)
                {
                    entry.SetSerializedValue(orphanedValue.ToString());
                }

                migrated = true;
            }

            orphanedEntries.Remove(aliasDefinition);
        }

        return migrated;
    }

    private static IEnumerable<ConfigDefinition> GetAliasDefinitions(DanceConfigKey configKey)
    {
        var sections = new[] { SettingsSection, LocalizedSettingsSection };
        var keys = new[] { GetKeyName(configKey, false), GetKeyName(configKey, true), GetLegacyKeyName(configKey) };

        foreach (var section in sections)
        {
            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    yield return new ConfigDefinition(section, key);
                }
            }
        }
    }

    private static IEnumerable<ConfigDefinition> GetSavedAirportPlacementAliasDefinitions()
    {
        var sections = new[] { SettingsSection, LocalizedSettingsSection, LegacyInternalSection };
        var keys = new[] { LegacySavedAirportPlacementKey, GetSavedAirportPlacementKeyName(false), GetSavedAirportPlacementKeyName(true) };

        foreach (var section in sections)
        {
            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    yield return new ConfigDefinition(section, key);
                }
            }
        }
    }

    private static bool IsConfigKeyName(string keyName, DanceConfigKey configKey)
    {
        return string.Equals(keyName, GetKeyName(configKey, false), StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyName, GetKeyName(configKey, true), StringComparison.Ordinal)
            || string.Equals(keyName, GetLegacyKeyName(configKey), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSavedAirportPlacementKeyName(string keyName)
    {
        return string.Equals(keyName, LegacySavedAirportPlacementKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyName, GetSavedAirportPlacementKeyName(false), StringComparison.OrdinalIgnoreCase)
            || string.Equals(keyName, GetSavedAirportPlacementKeyName(true), StringComparison.Ordinal);
    }

    private static string GetLegacyKeyName(DanceConfigKey configKey)
    {
        switch (configKey)
        {
            case DanceConfigKey.ModEnabled:
                return "ModEnabled";
            case DanceConfigKey.EnableAudio:
                return "EnableAudio";
            case DanceConfigKey.AudioVolume:
                return "AudioVolume";
            case DanceConfigKey.AudioRangeMeters:
                return "AudioRangeMeters";
            case DanceConfigKey.StabilizeModelLighting:
                return "StabilizeModelLighting";
            case DanceConfigKey.LightingExposureCompensation:
                return "LightingExposureCompensation";
            case DanceConfigKey.ModelScale:
                return "ModelScale";
            case DanceConfigKey.SpawnModelKey:
                return "SpawnModelKey";
            case DanceConfigKey.HairColorEnabled:
                return "HairColorEnabled";
            case DanceConfigKey.HairColorR:
                return "HairColorR";
            case DanceConfigKey.HairColorG:
                return "HairColorG";
            case DanceConfigKey.HairColorB:
                return "HairColorB";
            case DanceConfigKey.ClothColorEnabled:
                return "ClothColorEnabled";
            case DanceConfigKey.ClothColorR:
                return "ClothColorR";
            case DanceConfigKey.ClothColorG:
                return "ClothColorG";
            case DanceConfigKey.ClothColorB:
                return "ClothColorB";
            case DanceConfigKey.EnableVerboseLogs:
                return "EnableVerboseLogs";
            default:
                return string.Empty;
        }
    }

    private static bool TryGetConfiguredGameLanguage(out bool isChineseLanguage)
    {
        return TryGetConfiguredGameLanguage(out isChineseLanguage, out _);
    }

    private static bool TryGetConfiguredGameLanguage(out bool isChineseLanguage, out string languageValueText)
    {
        isChineseLanguage = false;
        languageValueText = string.Empty;
        try
        {
            if (!PlayerPrefs.HasKey("LanguageSetting"))
            {
                return false;
            }

            var languageValue = PlayerPrefs.GetInt("LanguageSetting", int.MinValue);
            if (languageValue != int.MinValue)
            {
                languageValueText = languageValue.ToString(CultureInfo.InvariantCulture);
                isChineseLanguage = IsChineseLanguageIndex(languageValue);
                return true;
            }

            var languageText = PlayerPrefs.GetString("LanguageSetting", string.Empty);
            languageValueText = languageText;
            if (string.IsNullOrWhiteSpace(languageText))
            {
                return false;
            }

            if (int.TryParse(languageText, NumberStyles.Integer, CultureInfo.InvariantCulture, out languageValue))
            {
                isChineseLanguage = IsChineseLanguageIndex(languageValue);
                return true;
            }

            isChineseLanguage = IsChineseLanguageName(languageText);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetLocalizedTextLanguageName(out string languageName)
    {
        languageName = string.Empty;
        try
        {
            languageName = LocalizedText.CURRENT_LANGUAGE.ToString();
            return !string.IsNullOrWhiteSpace(languageName);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsChineseLanguageName(string languageName)
    {
        if (string.IsNullOrWhiteSpace(languageName))
        {
            return false;
        }

        if (int.TryParse(languageName.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var languageValue))
        {
            return IsChineseLanguageIndex(languageValue);
        }

        return languageName.IndexOf("Chinese", StringComparison.OrdinalIgnoreCase) >= 0
            || languageName.IndexOf("中文", StringComparison.OrdinalIgnoreCase) >= 0
            || languageName.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChineseLanguageIndex(int languageValue)
    {
        return languageValue == SimplifiedChineseLanguageIndex || languageValue == 10;
    }

    private static ConfigEntryBase[] GetConfigEntriesSnapshot(ConfigFile configFile)
    {
        var entriesProperty = typeof(ConfigFile).GetProperty("Entries", InstanceFlags);
        if (configFile == null || entriesProperty?.GetValue(configFile) is not IDictionary dictionary || dictionary.Count == 0)
        {
            return Array.Empty<ConfigEntryBase>();
        }

        var entries = new List<ConfigEntryBase>();
        foreach (DictionaryEntry pair in dictionary)
        {
            if (pair.Value is ConfigEntryBase entry)
            {
                entries.Add(entry);
            }
        }

        return entries.ToArray();
    }

    private static IDictionary? GetOrphanedEntries(ConfigFile configFile)
    {
        return configFile?.GetType().GetProperty("OrphanedEntries", InstanceFlags)?.GetValue(configFile) as IDictionary;
    }

    private static bool DefinitionsEqual(ConfigDefinition left, ConfigDefinition right)
    {
        return string.Equals(left?.Section, right?.Section, StringComparison.Ordinal)
            && string.Equals(left?.Key, right?.Key, StringComparison.Ordinal);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null || string.IsNullOrWhiteSpace(fieldName))
        {
            return;
        }

        target.GetType().GetField(fieldName, InstanceFlags)?.SetValue(target, value);
    }

    private static void AddUiLocalizationPair(Dictionary<string, string> map, string source, string localized)
    {
        if (map == null || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(localized))
        {
            return;
        }

        var sourceTrimmed = source.Trim();
        var localizedTrimmed = localized.Trim();
        map[sourceTrimmed] = localizedTrimmed;
        map[localizedTrimmed] = localizedTrimmed;

        var sourceCompact = sourceTrimmed.Replace(" ", string.Empty);
        var localizedCompact = localizedTrimmed.Replace(" ", string.Empty);
        if (!map.ContainsKey(sourceCompact))
        {
            map[sourceCompact] = localizedTrimmed;
        }

        if (!map.ContainsKey(localizedCompact))
        {
            map[localizedCompact] = localizedTrimmed;
        }

        map[sourceTrimmed.ToUpperInvariant()] = localizedTrimmed;
        map[localizedTrimmed.ToUpperInvariant()] = localizedTrimmed;
    }

    private static string ToReadableName(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return string.Empty;
        }

        var chars = new List<char>(keyName.Length + 8);
        for (var index = 0; index < keyName.Length; index++)
        {
            var current = keyName[index];
            if (index > 0 && char.IsUpper(current) && !char.IsWhiteSpace(keyName[index - 1]) && !char.IsUpper(keyName[index - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(current);
        }

        return new string(chars.ToArray());
    }

    private static string GetLocalizedModDisplayName(bool isChineseLanguage)
    {
        return isChineseLanguage ? "Miku 展示" : PluginConstants.Name;
    }
}
