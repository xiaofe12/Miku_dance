using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace MikuDanceProject.Core;

internal sealed class DancePluginConfig
{
    private readonly string _pluginDirectory;
    private readonly ConfigFile _configFile;
    private readonly ConfigEntry<string> _savedAirportPlacement;
    private readonly List<LocalizedConfigEntry> _visibleEntries = new();

    public DancePluginConfig(ConfigFile config, string pluginDirectory)
    {
        _configFile = config;
        _pluginDirectory = Path.GetFullPath(pluginDirectory);
        IsChineseLanguage = DanceConfigLocalization.DetectChineseLanguage();

        ModEnabled = BindEntry(
            DanceConfigKey.ModEnabled,
            true,
            null);

        EnableAudio = BindEntry(
            DanceConfigKey.EnableAudio,
            true,
            null);

        AudioVolume = BindEntry(
            DanceConfigKey.AudioVolume,
            0.1f,
            new AcceptableValueRange<float>(0f, 1f));

        AudioRangeMeters = BindEntry(
            DanceConfigKey.AudioRangeMeters,
            5f,
            new AcceptableValueRange<float>(1f, 30f));

        StabilizeModelLighting = BindEntry(
            DanceConfigKey.StabilizeModelLighting,
            true,
            null);

        LightingExposureCompensation = BindEntry(
            DanceConfigKey.LightingExposureCompensation,
            0.82f,
            new AcceptableValueRange<float>(0.4f, 1f));

        ModelScale = BindEntry(
            DanceConfigKey.ModelScale,
            1.2f,
            new AcceptableValueRange<float>(0.1f, 3f));

        SpawnModelKey = BindEntry(
            DanceConfigKey.SpawnModelKey,
            KeyCode.F8,
            null);

        HairColorEnabled = BindEntry(
            DanceConfigKey.HairColorEnabled,
            false,
            null);

        HairColorR = BindEntry(
            DanceConfigKey.HairColorR,
            0f,
            new AcceptableValueRange<float>(0f, 1f));

        HairColorG = BindEntry(
            DanceConfigKey.HairColorG,
            0f,
            new AcceptableValueRange<float>(0f, 1f));

        HairColorB = BindEntry(
            DanceConfigKey.HairColorB,
            0f,
            new AcceptableValueRange<float>(0f, 1f));

        ClothColorEnabled = BindEntry(
            DanceConfigKey.ClothColorEnabled,
            false,
            null);

        ClothColorR = BindEntry(
            DanceConfigKey.ClothColorR,
            0f,
            new AcceptableValueRange<float>(0f, 1f));

        ClothColorG = BindEntry(
            DanceConfigKey.ClothColorG,
            0f,
            new AcceptableValueRange<float>(0f, 1f));

        ClothColorB = BindEntry(
            DanceConfigKey.ClothColorB,
            0f,
            new AcceptableValueRange<float>(0f, 1f));

        RandomizeColors = BindEntry(
            DanceConfigKey.RandomizeColors,
            true,
            null);

        EnableVerboseLogs = BindEntry(
            DanceConfigKey.EnableVerboseLogs,
            false,
            null);

        _savedAirportPlacement = config.Bind(
            DanceConfigLocalization.GetSectionName(IsChineseLanguage),
            DanceConfigLocalization.GetSavedAirportPlacementKeyName(IsChineseLanguage),
            string.Empty,
            DanceConfigLocalization.GetSavedAirportPlacementDescription(IsChineseLanguage));

        DanceConfigLocalization.MigrateSavedAirportPlacement(config, _savedAirportPlacement);
        DanceConfigLocalization.MigrateLocalizedConfigEntries(config, _visibleEntries);
        RemoveObsoleteConfigEntries(config.ConfigFilePath);

        // 同步调试日志开关到 Runtime 层共享状态
        VerboseLogState.Update(EnableVerboseLogs.Value);
        EnableVerboseLogs.SettingChanged += (_, _) => VerboseLogState.Update(EnableVerboseLogs.Value);

        Save();
    }

    public ConfigEntry<bool> ModEnabled { get; }
    public ConfigEntry<bool> EnableAudio { get; }
    public ConfigEntry<float> AudioVolume { get; }
    public ConfigEntry<float> AudioRangeMeters { get; }
    public ConfigEntry<bool> StabilizeModelLighting { get; }
    public ConfigEntry<float> LightingExposureCompensation { get; }
    public ConfigEntry<float> ModelScale { get; }
    public ConfigEntry<KeyCode> SpawnModelKey { get; }
    public ConfigEntry<bool> HairColorEnabled { get; }
    public ConfigEntry<float> HairColorR { get; }
    public ConfigEntry<float> HairColorG { get; }
    public ConfigEntry<float> HairColorB { get; }
    public ConfigEntry<bool> ClothColorEnabled { get; }
    public ConfigEntry<float> ClothColorR { get; }
    public ConfigEntry<float> ClothColorG { get; }
    public ConfigEntry<float> ClothColorB { get; }
    public ConfigEntry<bool> RandomizeColors { get; }
    public ConfigEntry<bool> EnableVerboseLogs { get; }
    public bool IsChineseLanguage { get; private set; }
    public IReadOnlyList<LocalizedConfigEntry> VisibleEntries => _visibleEntries;

    public float ResolvedModelScale => Mathf.Clamp(ModelScale.Value, 0.1f, 3f);
    public float ResolvedAudioVolume => Mathf.Clamp01(AudioVolume.Value);
    public float ResolvedAudioRangeMeters => Mathf.Clamp(AudioRangeMeters.Value, 1f, 30f);
    public float ResolvedLightingExposureCompensation => Mathf.Clamp(LightingExposureCompensation.Value, 0.4f, 1f);

    // 随机颜色模式下，每次生成模型时缓存的随机颜色，覆盖 RGB 滑块的值。
    // 非随机模式下始终读取 RGB 滑块的值。
    private Color _randomHairColor = Color.white;
    private Color _randomClothColor = Color.white;

    // 适合初音未来风格的预设色板（已按 tint 叠加特性调整亮度，避免过暗或过饱和）。
    // 头发偏冷色系/明亮系（青、蓝、粉、紫、银等），服装偏协调色调（黑、白、红、蓝、粉等）。
    private static readonly Color[] HairColorPalette =
    {
        new Color(0.05f, 0.85f, 0.82f, 1f),   // 青绿（初音经典）
        new Color(0.10f, 0.55f, 0.90f, 1f),   // 天蓝
        new Color(0.25f, 0.40f, 0.95f, 1f),   // 钴蓝
        new Color(0.55f, 0.35f, 0.90f, 1f),   // 薰衣草紫
        new Color(0.85f, 0.40f, 0.75f, 1f),   // 樱花粉
        new Color(0.90f, 0.60f, 0.85f, 1f),   // 浅粉
        new Color(0.75f, 0.85f, 0.95f, 1f),   // 银白
        new Color(0.30f, 0.85f, 0.70f, 1f),   // 薄荷绿
        new Color(0.95f, 0.80f, 0.40f, 1f),    // 金色
        new Color(0.80f, 0.30f, 0.55f, 1f),   // 玫红
    };

    private static readonly Color[] ClothColorPalette =
    {
        new Color(0.05f, 0.05f, 0.08f, 1f),    // 深空黑
        new Color(0.95f, 0.95f, 0.95f, 1f),    // 纯白
        new Color(0.80f, 0.10f, 0.15f, 1f),    // 正红
        new Color(0.10f, 0.35f, 0.85f, 1f),    // 宝蓝
        new Color(0.85f, 0.45f, 0.65f, 1f),    // 粉红
        new Color(0.40f, 0.20f, 0.65f, 1f),    // 深紫
        new Color(0.15f, 0.55f, 0.45f, 1f),    // 墨绿
        new Color(0.90f, 0.75f, 0.20f, 1f),    // 金黄
        new Color(0.25f, 0.25f, 0.30f, 1f),    // 炭灰
        new Color(0.95f, 0.55f, 0.15f, 1f),    // 橙
    };

    private static readonly string[] HairColorNames =
    {
        "青绿", "天蓝", "钴蓝", "薰衣草紫", "樱花粉",
        "浅粉", "银白", "薄荷绿", "金色", "玫红",
    };

    private static readonly string[] ClothColorNames =
    {
        "深空黑", "纯白", "正红", "宝蓝", "粉红",
        "深紫", "墨绿", "金黄", "炭灰", "橙",
    };

    // 协调搭配表：每行对应 HairColorPalette 同索引头发色推荐的服装色索引（基于色彩搭配与角色风格预设）。
    // 避免绿配绿、粉配橙等不协调组合；银白为百搭色可选强对比的酷感配色。
    private static readonly int[][] CoordinatedClothIndices =
    {
        new[] { 0, 1, 3 },  // 青绿 → 深空黑（经典舞台风）/纯白/宝蓝（同系渐变）
        new[] { 1, 0, 3 },  // 天蓝 → 纯白/深空黑/宝蓝
        new[] { 1, 7, 0 },  // 钴蓝 → 纯白/金黄（活力撞色）/深空黑
        new[] { 5, 1, 4 },  // 薰衣草紫 → 深紫（同系）/纯白/粉红
        new[] { 1, 0, 4 },  // 樱花粉 → 纯白（甜美系）/深空黑/粉红
        new[] { 1, 4, 0 },  // 浅粉 → 纯白/粉红/深空黑
        new[] { 0, 2, 5 },  // 银白 → 深空黑（强对比酷感）/正红/深紫
        new[] { 1, 0, 9 },  // 薄荷绿 → 纯白/深空黑/橙（活力撞色）
        new[] { 0, 1, 5 },  // 金色 → 深空黑（金黑奢华）/纯白/深紫
        new[] { 0, 1, 5 },  // 玫红 → 深空黑/纯白/深紫
    };

    private int _lastHairColorIndex = -1;
    private int _lastClothColorIndex = -1;

    // 在每次生成模型时调用一次，刷新随机颜色缓存。
    // 当且仅当 RandomizeColors=true 时：先随机头发色，再从协调搭配表中随机服装色，
    // 并避免与上次完全相同的组合；否则清空缓存回退到 RGB 滑块。
    public void RefreshRandomColors()
    {
        if (!RandomizeColors.Value)
        {
            _randomHairColor = Color.white;
            _randomClothColor = Color.white;
            _lastHairColorIndex = -1;
            _lastClothColorIndex = -1;
            LastRandomColorSummary = null;
            return;
        }

        var hairIndex = UnityEngine.Random.Range(0, HairColorPalette.Length);
        var clothCandidates = CoordinatedClothIndices[hairIndex];
        var clothIndex = clothCandidates[UnityEngine.Random.Range(0, clothCandidates.Length)];

        // 与上次组合完全相同时换一个头发色（偏移 1..N-1 保证变化），再重选服装色。
        if (hairIndex == _lastHairColorIndex && clothIndex == _lastClothColorIndex)
        {
            hairIndex = (hairIndex + UnityEngine.Random.Range(1, HairColorPalette.Length)) % HairColorPalette.Length;
            clothCandidates = CoordinatedClothIndices[hairIndex];
            clothIndex = clothCandidates[UnityEngine.Random.Range(0, clothCandidates.Length)];
        }

        _lastHairColorIndex = hairIndex;
        _lastClothColorIndex = clothIndex;
        _randomHairColor = HairColorPalette[hairIndex];
        _randomClothColor = ClothColorPalette[clothIndex];
        LastRandomColorSummary = $"{HairColorNames[hairIndex]} hair + {ClothColorNames[clothIndex]} cloth";
    }

    // 最近一次随机选色结果描述，供调用方写 verbose 日志。
    public string? LastRandomColorSummary { get; private set; }

    // 当 Enabled=true 或 RandomizeColors=true 时返回 Color（alpha=1），否则返回 false 使用模型原色。
    // 颜色会与原纹理相乘叠加（tint），而非纯色覆盖。
    public bool TryResolveHairColor(out Color color)
    {
        if (RandomizeColors.Value)
        {
            color = _randomHairColor;
            return true;
        }

        if (!HairColorEnabled.Value)
        {
            color = Color.white;
            return false;
        }

        color = new Color(HairColorR.Value, HairColorG.Value, HairColorB.Value, 1f);
        return true;
    }

    public bool TryResolveClothColor(out Color color)
    {
        if (RandomizeColors.Value)
        {
            color = _randomClothColor;
            return true;
        }

        if (!ClothColorEnabled.Value)
        {
            color = Color.white;
            return false;
        }

        color = new Color(ClothColorR.Value, ClothColorG.Value, ClothColorB.Value, 1f);
        return true;
    }

    public string ResolveUnityBundlePath()
    {
        var candidatePaths = new[]
        {
            Path.Combine(_pluginDirectory, PluginConstants.DefaultUnityBundleFileName),
            Path.Combine(_pluginDirectory, PluginConstants.LegacyUnityBundlePath),
            Path.Combine(_pluginDirectory, PluginConstants.LegacyAssetsUnityBundlePath),
        };

        return candidatePaths
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists)
            ?? Path.GetFullPath(candidatePaths[0]);
    }

    public void Save()
    {
        _configFile.Save();
        DanceConfigLocalization.RewriteConfigFileLocalization(_configFile.ConfigFilePath, IsChineseLanguage);
    }

    public bool RefreshLocalization(bool isChineseLanguage, bool saveConfigFile)
    {
        var changed = IsChineseLanguage != isChineseLanguage;
        IsChineseLanguage = isChineseLanguage;
        DanceConfigLocalization.ApplyLocalizedDescriptions(_configFile, IsChineseLanguage);
        if (saveConfigFile)
        {
            Save();
        }
        else
        {
            DanceConfigLocalization.RewriteConfigFileLocalization(_configFile.ConfigFilePath, IsChineseLanguage);
        }

        return changed;
    }

    public bool TryGetSavedAirportPlacement(out Vector3 position, out Vector3 forward)
    {
        position = default;
        forward = Vector3.forward;

        var serialized = _savedAirportPlacement.Value;
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return false;
        }

        var placementParts = serialized.Split('|');
        if (placementParts.Length != 2)
        {
            return false;
        }

        if (!TryParseVector3(placementParts[0], out position) || !TryParseVector3(placementParts[1], out forward))
        {
            position = default;
            forward = Vector3.forward;
            return false;
        }

        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (forward.sqrMagnitude <= 0.0001f)
        {
            forward = Vector3.forward;
        }
        else
        {
            forward.Normalize();
        }

        return true;
    }

    public void SaveAirportPlacement(Vector3 position, Vector3 forward)
    {
        var normalizedForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (normalizedForward.sqrMagnitude <= 0.0001f)
        {
            normalizedForward = Vector3.forward;
        }
        else
        {
            normalizedForward.Normalize();
        }

        _savedAirportPlacement.Value = $"{SerializeVector3(position)}|{SerializeVector3(normalizedForward)}";
        Save();
    }

    private ConfigEntry<T> BindEntry<T>(DanceConfigKey configKey, T defaultValue, AcceptableValueBase? acceptableValues)
    {
        var entry = _configFile.Bind(
            DanceConfigLocalization.GetSectionName(IsChineseLanguage),
            DanceConfigLocalization.GetKeyName(configKey, IsChineseLanguage),
            defaultValue,
            new ConfigDescription(
                DanceConfigLocalization.GetDescription(configKey, IsChineseLanguage),
                acceptableValues));
        _visibleEntries.Add(new LocalizedConfigEntry(configKey, entry));
        return entry;
    }

    private static void RemoveObsoleteConfigEntries(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return;
        }

        var obsoleteKeys = new HashSet<string>
        {
            "AssetRootPath",
            "ModelPath",
            "MotionPath",
            "VerticalOffset",
            "ModelPitchCorrection",
            "ModelRollCorrection",
            "LobbyPositionX",
            "LobbyPositionY",
            "LobbyPositionZ",
            "GroundProbeMaxDistance",
            "UseManualLobbyPlacement",
            "ManualLobbyPositionX",
            "ManualLobbyPositionY",
            "ManualLobbyPositionZ",
            "ManualLobbyForwardX",
            "ManualLobbyForwardY",
            "ManualLobbyForwardZ",
            "PitchOffset",
            "YawOffset",
            "RollOffset",
            "ElevatorForwardOffset",
            "ElevatorRightOffset",
            "FallbackPositionX",
            "FallbackPositionY",
            "FallbackPositionZ",
            "SpawnForwardOffset",
            "SpawnRightOffset",
            "HairColor",
            "ClothColor",
            "PreferredLobbyPositionX",
            "PreferredLobbyPositionY",
            "PreferredLobbyPositionZ",
            "ModelYawCorrection",
        };

        var lines = File.ReadAllLines(configPath);
        var filteredLines = new List<string>(lines.Length);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (!IsObsoleteConfigValueLine(line, obsoleteKeys))
            {
                filteredLines.Add(line);
                continue;
            }

            while (filteredLines.Count > 0 && string.IsNullOrWhiteSpace(filteredLines[filteredLines.Count - 1]))
            {
                filteredLines.RemoveAt(filteredLines.Count - 1);
            }

            while (filteredLines.Count > 0 && filteredLines[filteredLines.Count - 1].StartsWith("#", System.StringComparison.Ordinal))
            {
                filteredLines.RemoveAt(filteredLines.Count - 1);
            }

            while (index + 1 < lines.Length && string.IsNullOrWhiteSpace(lines[index + 1]))
            {
                index++;
            }
        }

        if (filteredLines.Count != lines.Length)
        {
            File.WriteAllLines(configPath, filteredLines);
        }
    }

    private static bool IsObsoleteConfigValueLine(string line, HashSet<string> obsoleteKeys)
    {
        var separatorIndex = line.IndexOf(" = ", System.StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return false;
        }

        var key = line.Substring(0, separatorIndex);
        return obsoleteKeys.Contains(key);
    }

    private static string SerializeVector3(Vector3 value)
    {
        return string.Join(
            ",",
            value.x.ToString("R", CultureInfo.InvariantCulture),
            value.y.ToString("R", CultureInfo.InvariantCulture),
            value.z.ToString("R", CultureInfo.InvariantCulture));
    }

    private static bool TryParseVector3(string serialized, out Vector3 value)
    {
        value = default;
        var parts = serialized.Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!float.TryParse(parts[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(parts[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var y)
            || !float.TryParse(parts[2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var z))
        {
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }

}
