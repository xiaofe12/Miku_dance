using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using TMPro;
using UnityEngine;

namespace MikuDanceProject.Core;

internal static class OptionalModConfigBridge
{
    private const string ModConfigPluginGuid = "com.github.PEAKModding.PEAKLib.ModConfig";
    private const string ModConfigPluginTypeName = "PEAKLib.ModConfig.ModConfigPlugin";
    private static readonly BindingFlags AllBindings = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static bool _registered;

    public static bool TryRegister(DancePluginConfig config, ManualLogSource logger)
    {
        if (_registered)
        {
            return true;
        }

        try
        {
            var pluginType = FindLoadedType(ModConfigPluginTypeName);
            if (pluginType == null)
            {
                return false;
            }

            var pluginInstance = pluginType.GetField("instance", AllBindings)?.GetValue(null);
            if (pluginInstance == null)
            {
                return false;
            }

            _registered = true;
            TryLocalizeVisibleUi(config);
            logger.LogDebug("ModConfig v1.6+ detected. Relying on automatic config entry discovery by ModConfig.");
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError($"Failed to detect ModConfig bridge: {exception}");
            return true;
        }
    }

    public static void TryLocalizeVisibleUi(DancePluginConfig config)
    {
        if (config == null || !TryGetModConfigMenuInstance(out var menuType, out var menuInstance) || menuType == null || menuInstance == null)
        {
            return;
        }

        if (menuInstance is not Behaviour behaviour || behaviour == null)
        {
            return;
        }

        try
        {
            if (!behaviour.isActiveAndEnabled || !behaviour.gameObject.activeInHierarchy)
            {
                return;
            }
        }
        catch
        {
            return;
        }

        var map = DanceConfigLocalization.BuildUiLocalizationMap(config.IsChineseLanguage);
        foreach (var root in EnumerateModConfigUiRoots(menuInstance, menuType))
        {
            ApplyTextLocalizationToRoot(root, map);
        }

        ApplyTextLocalizationToLoadedUi(map);
    }

    private static Type? FindLoadedType(string fullName)
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, throwOnError: false, ignoreCase: false))
            .FirstOrDefault(type => type != null);
    }

    private static bool TryGetModConfigMenuInstance(out Type? menuType, out object? menuInstance)
    {
        menuType = null;
        menuInstance = null;
        if (!Chainloader.PluginInfos.TryGetValue(ModConfigPluginGuid, out var pluginInfo) || pluginInfo == null || pluginInfo.Instance == null)
        {
            return false;
        }

        var assembly = pluginInfo.Instance.GetType().Assembly;
        menuType = assembly.GetType("PEAKLib.ModConfig.Components.ModdedSettingsMenu");
        menuInstance = menuType?.GetProperty("Instance", AllBindings)?.GetValue(null);
        return menuType != null && menuInstance != null;
    }

    private static IEnumerable<Transform> EnumerateModConfigUiRoots(object menuInstance, Type menuType)
    {
        var visited = new HashSet<int>();
        foreach (var root in EnumerateCandidateTransforms(menuInstance, menuType))
        {
            if (root != null && visited.Add(root.GetInstanceID()))
            {
                yield return root;
            }
        }
    }

    private static IEnumerable<Transform> EnumerateCandidateTransforms(object menuInstance, Type menuType)
    {
        if (menuInstance is Component component)
        {
            yield return component.transform;
        }

        var contentObject = menuType.GetProperty("Content", AllBindings)?.GetValue(menuInstance);
        if (contentObject is Transform content)
        {
            yield return content;
        }
    }

    private static void ApplyTextLocalizationToRoot(Transform root, Dictionary<string, string> map)
    {
        if (root == null || map == null || map.Count == 0)
        {
            return;
        }

        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            ApplyTextLocalization(text, map);
        }
    }

    private static void ApplyTextLocalizationToLoadedUi(Dictionary<string, string> map)
    {
        if (map == null || map.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (text == null || text.gameObject == null || !text.gameObject.scene.IsValid())
                {
                    continue;
                }

                ApplyTextLocalization(text, map);
            }
        }
        catch (Exception)
        {
            // Resources.FindObjectsOfTypeAll 在场景未初始化完成时会抛出异常，
            // 本地化会在后续场景加载时重新应用，此处忽略是安全的。
        }
    }

    private static void ApplyTextLocalization(TMP_Text text, Dictionary<string, string> map)
    {
        if (text == null || map == null || map.Count == 0)
        {
            return;
        }

        var trimmed = text.text == null ? string.Empty : text.text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        if (map.TryGetValue(trimmed, out var localized) && !string.Equals(trimmed, localized, StringComparison.Ordinal))
        {
            text.text = localized;
        }
    }
}
