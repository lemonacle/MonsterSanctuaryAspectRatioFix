using System;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace MonsterSanctuaryMapTilePresentationTest
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    [BepInDependency("lemonacle.MonsterSanctuary.AspectRatioFix")]
    public sealed class MapTilePresentationTestPlugin : BaseUnityPlugin
    {
        private const string PluginId = "lemonacle.MonsterSanctuary.MapTilePresentationTest";
        private const string PluginName = "Map Tile Presentation Test";
        private const string PluginVersion = "0.1.0";
        private const string MainPluginTypeName = "MonsterSanctuaryAspectRatioFix.AspectRatioFixPlugin";

        private static MapTilePresentationTestPlugin Instance { get; set; }
        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            harmony = new Harmony(PluginId);
            harmony.PatchAll();
            Logger.LogInfo("Map Tile Presentation Test 0.1.0 loaded.");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void RefreshMapTilePresentation(object mapMenuInstance)
        {
            if (mapMenuInstance == null)
            {
                return;
            }

            Type mainPluginType = AccessTools.TypeByName(MainPluginTypeName);
            if (mainPluginType == null)
            {
                Logger.LogWarning("Could not find the Aspect Ratio Fix plugin type.");
                return;
            }

            PropertyInfo cropActiveProperty = mainPluginType.GetProperty(
                "CropActive", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo uiLayerIndexProperty = mainPluginType.GetProperty(
                "UiLayerIndex", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            PropertyInfo instanceProperty = mainPluginType.GetProperty(
                "Instance", BindingFlags.Static | BindingFlags.NonPublic);

            if (cropActiveProperty == null || uiLayerIndexProperty == null || instanceProperty == null)
            {
                Logger.LogWarning("Could not access Aspect Ratio Fix presentation state.");
                return;
            }

            bool cropActive = (bool)cropActiveProperty.GetValue(null, null);
            int uiLayerIndex = (int)uiLayerIndexProperty.GetValue(null, null);
            object mainPluginInstance = instanceProperty.GetValue(null, null);
            if (!cropActive || uiLayerIndex < 0 || mainPluginInstance == null)
            {
                return;
            }

            MethodInfo setLayerRecursively = mainPluginType.GetMethod(
                "SetLayerRecursively",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(GameObject), typeof(int) },
                null);
            MethodInfo excludeUiLayerFromOtherCameras = mainPluginType.GetMethod(
                "ExcludeUiLayerFromOtherCameras",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (setLayerRecursively == null)
            {
                Logger.LogWarning("Could not access Aspect Ratio Fix layer assignment method.");
                return;
            }

            Component mapMenuComponent = mapMenuInstance as Component;
            if (mapMenuComponent == null)
            {
                return;
            }

            int refreshedTiles = 0;
            foreach (Component component in mapMenuComponent.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().Name != "MinimapTileView")
                {
                    continue;
                }

                PropertyInfo isMapMenuProperty = component.GetType().GetProperty(
                    "IsMapMenu", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (isMapMenuProperty != null && isMapMenuProperty.PropertyType == typeof(bool) &&
                    !(bool)isMapMenuProperty.GetValue(component, null))
                {
                    continue;
                }

                setLayerRecursively.Invoke(mainPluginInstance, new object[] { component.gameObject, uiLayerIndex });
                refreshedTiles++;
            }

            if (refreshedTiles > 0)
            {
                excludeUiLayerFromOtherCameras?.Invoke(mainPluginInstance, null);
                Logger.LogInfo($"Reassigned {refreshedTiles} map tile(s) to UI presentation layer {uiLayerIndex} after InitMapTiles.");
            }
        }

        [HarmonyPatch]
        private static class MapMenuInitMapTilesPatch
        {
            private static MethodBase TargetMethod()
            {
                Type mapMenuType = AccessTools.TypeByName("MapMenu");
                return mapMenuType != null ? AccessTools.Method(mapMenuType, "InitMapTiles") : null;
            }

            private static void Postfix(object __instance)
            {
                Instance?.RefreshMapTilePresentation(__instance);
            }
        }
    }
}
