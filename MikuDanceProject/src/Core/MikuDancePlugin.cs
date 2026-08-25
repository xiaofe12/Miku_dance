using System.IO;
using System.Collections;
using BepInEx;
using UnityEngine;
using MikuDanceProject.Runtime;

namespace MikuDanceProject.Core;

[BepInDependency("com.github.PEAKModding.PEAKLib.ModConfig", BepInDependency.DependencyFlags.SoftDependency)]
[BepInPlugin(PluginConstants.Guid, PluginConstants.Name, PluginConstants.Version)]
public sealed class MikuDancePlugin : BaseUnityPlugin
{
    private const float LocalizationRefreshIntervalSeconds = 0.25f;
    private DancePluginConfig? _pluginConfig;
    private float _nextLocalizationRefreshTime;

    private void Awake()
    {
        var pluginDirectory = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
        var pluginConfig = new DancePluginConfig(Config, pluginDirectory);
        _pluginConfig = pluginConfig;
        var controllerObject = new GameObject("MikuDanceController");
        DontDestroyOnLoad(controllerObject);

        var controller = controllerObject.AddComponent<DanceController>();
        controller.Initialize(pluginConfig, Logger);

        // 订阅游戏官方语言切换事件（游戏源码字段名即此拼写），替代每 0.25s 轮询 PlayerPrefs。
        LocalizedText.OnLangugageChanged += HandleGameLanguageChanged;

        Logger.LogDebug(
            $"MikuShowcase plugin initialized. pluginDirectory='{pluginDirectory}', " +
            $"bundlePath='{pluginConfig.ResolveUnityBundlePath()}', configPath='{Config.ConfigFilePath}'.");
        Logger.LogInfo($"[MikuShowcase] Config language: {DanceConfigLocalization.BuildLanguageDetectionSummary(pluginConfig.IsChineseLanguage)}");
    }

    private void Start()
    {
        StartCoroutine(RegisterModConfigRoutine());
    }

    private void Update()
    {
        // 语言检测已改为事件驱动；轮询仅负责 ModConfig 界面文本本地化（设置菜单可随时打开）。
        if (_pluginConfig == null)
        {
            return;
        }

        if (Time.unscaledTime < _nextLocalizationRefreshTime)
        {
            return;
        }

        _nextLocalizationRefreshTime = Time.unscaledTime + LocalizationRefreshIntervalSeconds;
        OptionalModConfigBridge.TryLocalizeVisibleUi(_pluginConfig);
    }

    private void HandleGameLanguageChanged()
    {
        if (_pluginConfig == null)
        {
            return;
        }

        StartCoroutine(RefreshLanguageAfterChangeRoutine());
    }

    private IEnumerator RefreshLanguageAfterChangeRoutine()
    {
        // 等待一帧，确保语言设置写入 PlayerPrefs 后再检测，避免读到旧值。
        yield return null;

        if (_pluginConfig == null)
        {
            yield break;
        }

        var isChineseLanguage = DanceConfigLocalization.DetectChineseLanguage();
        if (_pluginConfig.IsChineseLanguage != isChineseLanguage && _pluginConfig.RefreshLocalization(isChineseLanguage, saveConfigFile: true))
        {
            Logger.LogInfo("[MikuShowcase] Config language changed: " + (isChineseLanguage ? "Chinese" : "English"));
        }

        OptionalModConfigBridge.TryLocalizeVisibleUi(_pluginConfig);
    }

    private IEnumerator RegisterModConfigRoutine()
    {
        if (_pluginConfig == null)
        {
            yield break;
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (OptionalModConfigBridge.TryRegister(_pluginConfig, Logger))
            {
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.5f);
        }
    }
}
