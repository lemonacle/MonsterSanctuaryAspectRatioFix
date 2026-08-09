using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSanctuaryAspectRatioFix
{
    internal static class PluginMetadata
    {
        internal const string Id = "lemonacle.MonsterSanctuary.AspectRatioFix";
        internal const string Name = "Aspect Ratio Fix";
        internal const string Version = "2.1.29";
    }

    [BepInPlugin(PluginMetadata.Id, PluginMetadata.Name, PluginMetadata.Version)]
    public class AspectRatioFixPlugin : BaseUnityPlugin
    {
        [System.Flags]
        private enum PresentationRefresh
        {
            None = 0,
            ExplorationHud = 1 << 0,
            UiCamera = 1 << 1,
            TooltipCamera = 1 << 2,
            UiQuad = 1 << 3,
            TooltipQuad = 1 << 4,
            NativeShade = 1 << 5,
            CombatForeground = 1 << 6,
            UiComposite = UiCamera | UiQuad,
            TooltipComposite = TooltipCamera | TooltipQuad,
            CameraDependent = UiComposite | TooltipComposite | CombatForeground,
            All = ExplorationHud | CameraDependent | NativeShade
        }

        private const int OriginalWidth = 480;
        private const int OriginalHeight = 270;
        private const float AspectRatioFixAspect = 4f / 3f;
        private const float SixteenByTenAspect = 16f / 10f;
        private const float AspectTolerance = 0.02f;
        private const float UiAspect = 16f / 9f;
        private const float MapBackgroundOverscanPerEdge = 8f;
        private const int UnityLayerCount = 32;
        private const int FirstCandidateUiLayer = 8;
        private const int LastCandidateUiLayer = 30;
        private const float UiCameraDepthOffset = 10f;
        private const float TooltipCameraDepthOffset = 11f;
        private const float CombatForegroundCameraDepthOffset = 12f;
        private const int UiCompositeRenderQueue = 4000;
        private const int TooltipCompositeRenderQueue = 4001;
        private const int CombatForegroundRenderQueue = 4002;
        private const int BaseCompositeSortingOrder = 32766;
        private const int ForegroundCompositeSortingOrder = 32767;
        private const float UiQuadDepthOffset = 0.01f;
        private const float TooltipQuadDepthOffset = 0.02f;
        private const float CombatForegroundQuadDepthOffset = 0.03f;
        private const int CombatInitializationFrameCount = 12;
        private static float VisibleWorldWidth { get; set; } = 360f;
        private static float TargetAspect { get; set; } = AspectRatioFixAspect;
        private static int UiCanvasHeight => Mathf.RoundToInt(OriginalWidth / TargetAspect);
        private static float HorizontalHudInset => (OriginalWidth - VisibleWorldWidth) / 2f;
        internal static bool CropActive { get; private set; }
        internal static bool UiCompositeActive { get; private set; }
        internal static bool TooltipCompositeActive { get; private set; }
        internal static bool UiInputActive { get; private set; }
        internal static int UiLayerIndex { get; private set; } = -1;
        internal static int TooltipLayerIndex { get; private set; } = -1;
        internal static Rect UiScreenRectPixels { get; private set; }
        private static AspectRatioFixPlugin Instance { get; set; }
        private int previousScreenWidth;
        private int previousScreenHeight;
        private PresentationRefresh pendingPresentationRefresh;
        private bool applyingPresentationRefresh;
        private Coroutine pendingApply;
        private Coroutine sceneRegistrationCoroutine;
        private Coroutine familiarSelectionLayoutCoroutine;
        private Harmony harmony;
        private PixelCamera2D activePixelCamera;
        private Camera primaryCamera;
        private tk2dCamera primaryTkCamera;
        private MeshRenderer finalWorldQuad;
        private tk2dCamera finalTkCamera;
        private int uiLayer = -1;
        private GameObject uiCameraObject;
        private Camera uiRenderCamera;
        private tk2dCamera uiRenderTkCamera;
        private tk2dUICamera uiInputCamera;
        private tk2dCameraResolutionOverride[] expandedUiResolutionOverrides;
        private RenderTexture uiRenderTexture;
        private GameObject uiQuadObject;
        private MeshRenderer uiQuadRenderer;
        private Material uiQuadMaterial;
        private int validatedUiProjectionHeight = -1;
        private int tooltipLayer = -1;
        private GameObject tooltipCameraObject;
        private Camera tooltipRenderCamera;
        private tk2dCamera tooltipRenderTkCamera;
        private RenderTexture tooltipRenderTexture;
        private GameObject tooltipQuadObject;
        private MeshRenderer tooltipQuadRenderer;
        private Material tooltipQuadMaterial;
        private tk2dTiledSprite expandedShadeSprite;
        private Vector2 originalShadeDimensions;
        private bool originalShadeDimensionsCaptured;
        private tk2dTiledSprite expandedMapBackgroundSprite;
        private Vector2 originalMapBackgroundDimensions;
        private bool originalMapBackgroundDimensionsCaptured;
        private readonly Dictionary<Transform, Vector3> originalMapElementLocalPositions =
            new Dictionary<Transform, Vector3>();
        private readonly Dictionary<tk2dCameraAnchor, Camera> originalMapArrowAnchorCameras =
            new Dictionary<tk2dCameraAnchor, Camera>();
        private readonly Dictionary<Transform, GameObject> mapArrowOffsetWrappers =
            new Dictionary<Transform, GameObject>();
        private readonly Dictionary<Transform, int> originalMapArrowSiblingIndices =
            new Dictionary<Transform, int>();
        private tk2dTiledSprite expandedMonsterSelectorBackground;
        private Vector2 originalMonsterSelectorBackgroundDimensions;
        private bool originalMonsterSelectorBackgroundDimensionsCaptured;
        private tk2dBaseSprite expandedMonsterShiftBackground;
        private Vector2 originalMonsterShiftBackgroundDimensions;
        private bool originalMonsterShiftBackgroundDimensionsCaptured;
        private int combatForegroundLayer = -1;
        private GameObject combatForegroundCameraObject;
        private Camera combatForegroundRenderCamera;
        private tk2dCamera combatForegroundRenderTkCamera;
        private RenderTexture combatForegroundRenderTexture;
        private GameObject combatForegroundQuadObject;
        private MeshRenderer combatForegroundQuadRenderer;
        private Material combatForegroundQuadMaterial;
        private readonly Dictionary<GameObject, int> originalLayers = new Dictionary<GameObject, int>();
        private readonly Dictionary<Camera, int> originalCameraMasks = new Dictionary<Camera, int>();
        private readonly Dictionary<Transform, float> originalHudLocalX = new Dictionary<Transform, float>();
        private MinimapView anchoredMinimap;
        private Vector3 originalMinimapInitialPosition;
        private bool minimapInitialPositionCaptured;
        private bool minimapInsetApplied;
        private UIController anchoredUiController;
        private Vector3 originalTimerReferencePosition;
        private bool timerReferenceCaptured;
        private bool timerInsetApplied;
        /*
         * The game's PixelCamera2D methods remain responsible for deciding
         * when the display must be recalculated. Harmony postfixes apply
         * the 4:3 world policy immediately after those existing methods.
         *
         * Cache the world quad's full and cropped UV arrays so a normal
         * camera recalculation only changes the transform. The mesh is
         * uploaded again only when the crop state genuinely changes.
         */
        private Mesh cachedWorldQuadMesh;
        private Vector2[] cachedWorldFullUvs;
        private Vector2[] cachedWorldCroppedUvs;
        private bool cachedWorldUvIsCropped;
        private Coroutine combatInitializationCoroutine;
        private Coroutine combatBuffInfoLayoutCoroutine;
        private CombatUIController activeCombatUi;
        private readonly Dictionary<Transform, Vector3> originalVictoryBannerLocalPositions =
            new Dictionary<Transform, Vector3>();
        /*
         * Skip prompts are anchored only when shown. Their renderer
         * bounds are not recalculated during normal gameplay.
         */
        private readonly Dictionary<Transform, Vector3> originalSkipPromptLocalPositions = new Dictionary<Transform, Vector3>();
        private Coroutine skipPromptAnchorCoroutine;
        /*
         * Familiar-choice layout is captured and centered once after
         * the selection menu completes its initial layout.
         */
        private readonly List<KeepersIntro> registeredKeepersIntros = new List<KeepersIntro>();
        private readonly Dictionary<Transform, Vector3> originalFamiliarSelectionLocalPositions =
            new Dictionary<Transform, Vector3>();
        private readonly HashSet<KeepersIntro> centeredFamiliarSelections = new HashSet<KeepersIntro>();
        private readonly Dictionary<Transform, Vector3> originalKeeperIntroLocalPositions =
            new Dictionary<Transform, Vector3>();
        private readonly HashSet<KeepersIntro> adjustedKeeperIntros = new HashSet<KeepersIntro>();
        private static readonly FieldInfo BaseWidthField = typeof(PixelCamera2D).GetField("baseWidth",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo BaseHeightField = typeof(PixelCamera2D).GetField("baseHeight",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FinalCamRectField = typeof(PixelCamera2D).GetField("FinalCamRect",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo FinalCameraField = typeof(PixelCamera2D).GetField("FinalCamera",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo UpdateCameraMethod = typeof(PixelCamera2D).GetMethod("UpdateCamera",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TkScreenExtentsField = typeof(tk2dCamera).GetField("_screenExtents",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo UiRaycastTypeField = typeof(tk2dUICamera).GetField("raycastType",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MinimapInitialPositionField = typeof(MinimapView).GetField("initialPosition",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo RelativeOriginDirtyField = typeof(CameraController).GetField("bRelativeOriginDirty",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo($"{PluginMetadata.Name} {PluginMetadata.Version} loaded.");
            harmony = new Harmony(PluginMetadata.Id);
            harmony.PatchAll();
            previousScreenWidth = Screen.width;
            previousScreenHeight = Screen.height;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ScheduleApply();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            CancelAllPendingCoroutines();
            RestoreNativeShadeDimensions();
            RestoreMapBackgroundDimensions();
            RestoreMapScreenLayout();
            RestoreMonsterSelectorBackgroundDimensions();
            RestoreMonsterShiftBackgroundDimensions();
            harmony?.UnpatchSelf();
            RestoreExplorationHudLayout();
            RestoreFamiliarSelectionLayouts();
            RestoreKeeperIntroLayouts();
            RestoreVictoryBannerLayouts();
            RestoreAllOriginalLayers();
            RestoreAllCameraMasks();
            DestroyUiPipeline();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (Screen.width != previousScreenWidth || Screen.height != previousScreenHeight)
            {
                previousScreenWidth = Screen.width;
                previousScreenHeight = Screen.height;
                ScheduleApply();
            }
        }

        private void RefreshPresentation(PresentationRefresh refresh)
        {
            if (!CropActive || refresh == PresentationRefresh.None)
            {
                return;
            }

            pendingPresentationRefresh |= refresh;
            if (applyingPresentationRefresh)
            {
                return;
            }

            applyingPresentationRefresh = true;
            try
            {
                while (pendingPresentationRefresh != PresentationRefresh.None)
                {
                    PresentationRefresh currentRefresh = pendingPresentationRefresh;
                    pendingPresentationRefresh = PresentationRefresh.None;
                    if ((currentRefresh & PresentationRefresh.ExplorationHud) != 0)
                    {
                        UpdateExplorationHudLayout();
                    }
                    if ((currentRefresh & PresentationRefresh.UiCamera) != 0)
                    {
                        UpdateUiCameraTransform();
                    }
                    if ((currentRefresh & PresentationRefresh.TooltipCamera) != 0)
                    {
                        UpdateTooltipCameraTransform();
                    }
                    if ((currentRefresh & PresentationRefresh.UiQuad) != 0)
                    {
                        UpdateUiQuadLayout();
                    }
                    if ((currentRefresh & PresentationRefresh.TooltipQuad) != 0)
                    {
                        UpdateTooltipQuadLayout();
                    }
                    if ((currentRefresh & PresentationRefresh.NativeShade) != 0)
                    {
                        UpdateNativeShadeCoverage();
                    }
                    if ((currentRefresh & PresentationRefresh.CombatForeground) != 0)
                    {
                        UpdateCombatForegroundPresentation();
                    }
                }
            }
            finally
            {
                applyingPresentationRefresh = false;
            }
        }

        private void StopTrackedCoroutine(ref Coroutine coroutine)
        {
            if (coroutine == null)
            {
                return;
            }
            StopCoroutine(coroutine);
            coroutine = null;
        }

        private void CancelCombatPresentationCoroutines()
        {
            StopTrackedCoroutine(ref combatInitializationCoroutine);
            StopTrackedCoroutine(ref combatBuffInfoLayoutCoroutine);
        }

        private void CancelPresentationCoroutines()
        {
            StopTrackedCoroutine(ref familiarSelectionLayoutCoroutine);
            StopTrackedCoroutine(ref skipPromptAnchorCoroutine);
            CancelCombatPresentationCoroutines();
        }

        private void CancelAllPendingCoroutines()
        {
            StopTrackedCoroutine(ref pendingApply);
            StopTrackedCoroutine(ref sceneRegistrationCoroutine);
            CancelPresentationCoroutines();
        }

        private void ScheduleApply()
        {
            StopTrackedCoroutine(ref pendingApply);
            pendingApply = StartCoroutine(ApplyAfterResolutionSettles());
        }

        private IEnumerator ApplyAfterResolutionSettles()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            yield return null;
            pendingApply = null;
            ApplyAspectRatioPolicy();
        }

        private void ApplyAspectRatioPolicy()
        {
            activePixelCamera = FindActivePixelCamera();
            if (activePixelCamera == null)
            {
                Logger.LogWarning("Aspect Ratio Fix could not find PixelCamera2D.");
                return;
            }
            float screenAspect = Screen.width / (float)Screen.height;
            if (!TrySelectAspectProfile(screenAspect))
            {
                RestoreOriginalDisplay(activePixelCamera);
                return;
            }
            if (BaseWidthField == null || BaseHeightField == null || FinalCamRectField == null || FinalCameraField == null ||
                UpdateCameraMethod == null)
            {
                Logger.LogError("Aspect Ratio Fix could not access camera internals.");
                return;
            }
            primaryCamera = activePixelCamera.GetComponent<Camera>();
            primaryTkCamera = activePixelCamera.GetComponent<tk2dCamera>();
            validatedUiProjectionHeight = -1;
            finalWorldQuad = FinalCamRectField.GetValue(activePixelCamera) as MeshRenderer;
            finalTkCamera = FinalCameraField.GetValue(activePixelCamera) as tk2dCamera;
            if (primaryCamera == null || primaryCamera.targetTexture == null || primaryTkCamera == null || finalWorldQuad == null ||
                finalTkCamera == null)
            {
                Logger.LogError("Aspect Ratio Fix could not locate the complete " + "camera pipeline.");
                return;
            }
            // Preserve the game's intended 480x270 internal render.
            BaseWidthField.SetValue(activePixelCamera, OriginalWidth);
            BaseHeightField.SetValue(activePixelCamera, OriginalHeight);
            ResizeRenderTexture(primaryCamera.targetTexture, OriginalWidth, OriginalHeight);
            primaryTkCamera.nativeResolutionWidth = OriginalWidth;
            primaryTkCamera.nativeResolutionHeight = OriginalHeight;
            primaryTkCamera.UpdateCameraMatrix();
            /*
             * Enable the policy before invoking the game's own camera
             * recalculation methods. Their Harmony postfixes will apply
             * the selected centered world crop immediately after the game
             * performs its normal work.
             */
            CropActive = true;
            PreparePixelCameraPolicy(activePixelCamera);
            activePixelCamera.UpdateSecondCam();
            UpdateCameraMethod.Invoke(activePixelCamera, null);
            if (!ApplyWorldPresentationPolicy(activePixelCamera))
            {
                CropActive = false;
                Logger.LogError("Aspect Ratio Fix could not apply the world " + "camera presentation policy.");
                return;
            }
            EnsureUiPipeline();
            AssignKnownUiObjects();
            RegisterExistingKeepersIntros();
            AssignRegisteredKeepersIntros();
            EnsureShadePresentation();
            ExcludeUiLayerFromOtherCameras();
            RefreshPresentation(PresentationRefresh.All);
            RefreshUiInputState();
            if (RelativeOriginDirtyField != null)
            {
                foreach (CameraController controller in Resources.FindObjectsOfTypeAll<CameraController>())
                {
                    if (controller != null && controller.gameObject.activeInHierarchy)
                    {
                        RelativeOriginDirtyField.SetValue(controller, true);
                    }
                }
            }
            Logger.LogInfo($"Aspect Ratio Fix applied with expanded UI composite: " +
                $"world={VisibleWorldWidth:0}x270 crop, UI=480x{UiCanvasHeight}, " +
                $"screen={Screen.width}x{Screen.height}, " +
                $"uiLayer={uiLayer}, tooltipLayer={tooltipLayer}.");
        }

        private bool TrySelectAspectProfile(float screenAspect)
        {
            float selectedAspect;
            float selectedWorldWidth;

            if (Mathf.Abs(screenAspect - AspectRatioFixAspect) <= AspectTolerance)
            {
                selectedAspect = AspectRatioFixAspect;
                selectedWorldWidth = 360f;
            }
            else if (Mathf.Abs(screenAspect - SixteenByTenAspect) <= AspectTolerance)
            {
                selectedAspect = SixteenByTenAspect;
                selectedWorldWidth = 432f;
            }
            else
            {
                return false;
            }

            bool profileChanged =
                Mathf.Abs(TargetAspect - selectedAspect) > 0.001f ||
                Mathf.Abs(VisibleWorldWidth - selectedWorldWidth) > 0.001f;

            if (profileChanged)
            {
                /*
                 * Restore edge-anchored HUD objects using the old inset
                 * before changing to the newly selected aspect profile.
                 */
                if (CropActive)
                {
                    RestoreExplorationHudLayout();
                    RestoreFamiliarSelectionLayouts();
                    RestoreKeeperIntroLayouts();
                }
                cachedWorldQuadMesh = null;
                cachedWorldFullUvs = null;
                cachedWorldCroppedUvs = null;
            }

            TargetAspect = selectedAspect;
            VisibleWorldWidth = selectedWorldWidth;
            return true;
        }

        private void RestoreOriginalDisplay(PixelCamera2D pixelCamera)
        {
            CancelPresentationCoroutines();
            CropActive = false;
            UiCompositeActive = false;
            TooltipCompositeActive = false;
            UiInputActive = false;
            SetCombatBuffInfoCompositePriority(false);
            RestoreNativeShadeDimensions();
            RestoreMapBackgroundDimensions();
            RestoreMapScreenLayout();
            RestoreMonsterSelectorBackgroundDimensions();
            RestoreMonsterShiftBackgroundDimensions();
            RestoreExplorationHudLayout();
            RestoreFamiliarSelectionLayouts();
            RestoreKeeperIntroLayouts();
            MeshRenderer worldQuad = FinalCamRectField?.GetValue(pixelCamera) as MeshRenderer;
            if (worldQuad != null)
            {
                SetWorldUvCropState(worldQuad, cropped: false);
            }
            UpdateCameraMethod?.Invoke(pixelCamera, null);
            RestoreAllOriginalLayers();
            RestoreAllCameraMasks();
            if (uiRenderCamera != null)
            {
                uiRenderCamera.enabled = false;
            }
            if (uiQuadRenderer != null)
            {
                uiQuadRenderer.enabled = false;
            }
            if (tooltipRenderCamera != null)
            {
                tooltipRenderCamera.enabled = false;
            }
            if (tooltipQuadRenderer != null)
            {
                tooltipQuadRenderer.enabled = false;
            }
            if (combatForegroundRenderCamera != null)
            {
                combatForegroundRenderCamera.enabled = false;
            }
            if (combatForegroundQuadRenderer != null)
            {
                combatForegroundQuadRenderer.enabled = false;
            }
            Logger.LogInfo($"Aspect Ratio Fix skipped at " + $"{Screen.width}x{Screen.height}.");
        }

        private PixelCamera2D FindActivePixelCamera()
        {
            PixelCamera2D[] pixelCameras = Resources.FindObjectsOfTypeAll<PixelCamera2D>();
            foreach (PixelCamera2D pixelCamera in pixelCameras)
            {
                if (pixelCamera != null && pixelCamera.enabled && pixelCamera.gameObject.activeInHierarchy)
                {
                    return pixelCamera;
                }
            }
            return null;
        }

        private void EnsureUiPipeline()
        {
            if (!CropActive || primaryCamera == null || primaryTkCamera == null || finalWorldQuad == null || finalTkCamera == null)
            {
                return;
            }
            if (uiLayer < 0)
            {
                uiLayer = FindUnusedLayer();
                UiLayerIndex = uiLayer;
                if (uiLayer < 0)
                {
                    Logger.LogError("Aspect Ratio Fix could not find an unused Unity layer " + "for the separate UI camera.");
                    return;
                }
            }
            if (tooltipLayer < 0)
            {
                tooltipLayer = FindUnusedLayer(uiLayer);
                TooltipLayerIndex = tooltipLayer;
                if (tooltipLayer < 0)
                {
                    Logger.LogError("Aspect Ratio Fix could not find a second unused Unity layer " +
                        "for bottom-anchored tooltips.");
                }
            }
            if (combatForegroundLayer < 0)
            {
                combatForegroundLayer = FindUnusedLayer(uiLayer, tooltipLayer);
                if (combatForegroundLayer < 0)
                {
                    Logger.LogError("Aspect Ratio Fix could not find a third unused Unity layer " +
                        "for combat Buff Info foreground icons.");
                }
            }
            int uiCanvasHeight = UiCanvasHeight;
            if (uiRenderTexture == null)
            {
                uiRenderTexture = new RenderTexture(OriginalWidth, uiCanvasHeight, 24, RenderTextureFormat.ARGB32);
                uiRenderTexture.name = $"AspectRatioFix_UI_480x{uiCanvasHeight}";
                uiRenderTexture.filterMode = FilterMode.Point;
                uiRenderTexture.wrapMode = TextureWrapMode.Clamp;
                uiRenderTexture.useMipMap = false;
                uiRenderTexture.autoGenerateMips = false;
                uiRenderTexture.antiAliasing = 1;
                uiRenderTexture.Create();
            }
            else if (uiRenderTexture.width != OriginalWidth || uiRenderTexture.height != uiCanvasHeight)
            {
                ResizeRenderTexture(uiRenderTexture, OriginalWidth, uiCanvasHeight);
                uiRenderTexture.name = $"AspectRatioFix_UI_480x{uiCanvasHeight}";
            }
            if (tooltipLayer >= 0 && tooltipRenderTexture == null)
            {
                tooltipRenderTexture = new RenderTexture(OriginalWidth, OriginalHeight, 24, RenderTextureFormat.ARGB32);
                tooltipRenderTexture.name = "AspectRatioFix_Tooltips_480x270";
                tooltipRenderTexture.filterMode = FilterMode.Point;
                tooltipRenderTexture.wrapMode = TextureWrapMode.Clamp;
                tooltipRenderTexture.useMipMap = false;
                tooltipRenderTexture.autoGenerateMips = false;
                tooltipRenderTexture.antiAliasing = 1;
                tooltipRenderTexture.Create();
            }
            if (uiCameraObject == null)
            {
                CreateUiCamera();
            }
            if (uiQuadObject == null)
            {
                CreateUiQuad();
            }
            if (tooltipLayer >= 0 && tooltipCameraObject == null)
            {
                CreateTooltipCamera();
            }
            if (tooltipLayer >= 0 && tooltipQuadObject == null)
            {
                CreateTooltipQuad();
            }
            if (combatForegroundLayer >= 0 && combatForegroundCameraObject == null)
            {
                CreateCombatForegroundCamera();
            }
            if (combatForegroundLayer >= 0 && combatForegroundQuadObject == null)
            {
                CreateCombatForegroundQuad();
            }
            ConfigureExpandedUiCamera();
            if (uiRenderCamera != null)
            {
                uiRenderCamera.enabled = true;
            }
            if (uiQuadRenderer != null)
            {
                uiQuadRenderer.enabled = true;
            }
            if (tooltipRenderCamera != null)
            {
                tooltipRenderCamera.enabled = true;
            }
            if (combatForegroundRenderCamera != null)
            {
                combatForegroundRenderCamera.enabled = true;
            }
            if (tooltipQuadRenderer != null)
            {
                tooltipQuadRenderer.enabled = true;
            }
            UiCompositeActive = uiRenderCamera != null && uiQuadRenderer != null;
            TooltipCompositeActive = tooltipRenderCamera != null && tooltipQuadRenderer != null;
        }

        private int FindUnusedLayer(int excludedLayer = -1, int secondExcludedLayer = -1)
        {
            bool[] usedLayers = new bool[UnityLayerCount];
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject gameObject in objects)
            {
                if (gameObject != null && gameObject.layer >= 0 && gameObject.layer < UnityLayerCount)
                {
                    usedLayers[gameObject.layer] = true;
                }
            }
            for (int layer = LastCandidateUiLayer; layer >= FirstCandidateUiLayer; layer--)
            {
                if (layer != excludedLayer && layer != secondExcludedLayer && !usedLayers[layer] &&
                    string.IsNullOrEmpty(LayerMask.LayerToName(layer)))
                {
                    return layer;
                }
            }
            // The highest candidate layer is the least likely remaining layer to be used.
            if (excludedLayer != LastCandidateUiLayer && secondExcludedLayer != LastCandidateUiLayer &&
                !usedLayers[LastCandidateUiLayer])
            {
                return LastCandidateUiLayer;
            }
            return -1;
        }

        private void CreateUiCamera()
        {
            uiCameraObject = new GameObject("AspectRatioFix UI Camera");
            uiCameraObject.SetActive(false);
            uiCameraObject.transform.SetParent(primaryCamera.transform, worldPositionStays: false);
            uiCameraObject.transform.localPosition = Vector3.zero;
            uiCameraObject.transform.localRotation = Quaternion.identity;
            uiCameraObject.transform.localScale = Vector3.one;
            uiRenderCamera = uiCameraObject.AddComponent<Camera>();
            uiRenderCamera.CopyFrom(primaryCamera);
            uiRenderCamera.cullingMask = 1 << uiLayer;
            uiRenderCamera.clearFlags = CameraClearFlags.SolidColor;
            uiRenderCamera.backgroundColor = Color.clear;
            uiRenderCamera.targetTexture = uiRenderTexture;
            uiRenderCamera.rect = new Rect(0f, 0f, 1f, 1f);
            uiRenderCamera.depth = primaryCamera.depth + UiCameraDepthOffset;
            uiRenderCamera.allowHDR = false;
            uiRenderCamera.allowMSAA = false;
            uiRenderTkCamera = uiCameraObject.AddComponent<tk2dCamera>();
            ConfigureExpandedUiProjection();
            uiInputCamera = uiCameraObject.AddComponent<tk2dUICamera>();
            tk2dUICamera existingUiCamera = primaryCamera.GetComponent<tk2dUICamera>();
            if (existingUiCamera != null && UiRaycastTypeField != null)
            {
                object raycastType = UiRaycastTypeField.GetValue(existingUiCamera);
                UiRaycastTypeField.SetValue(uiInputCamera, raycastType);
            }
            uiInputCamera.AssignRaycastLayerMask(1 << uiLayer);
            uiCameraObject.SetActive(true);
            uiRenderTkCamera.UpdateCameraMatrix();
            CenterExpandedUiCamera();
        }

        private void ConfigureExpandedUiCamera()
        {
            if (uiRenderCamera == null || uiRenderTkCamera == null || uiRenderTexture == null)
            {
                return;
            }
            uiRenderCamera.targetTexture = uiRenderTexture;
            ConfigureExpandedUiProjection();
            uiRenderTkCamera.UpdateCameraMatrix();
            CenterExpandedUiCamera();
            ValidateExpandedUiProjection();
        }

        private void ConfigureExpandedUiProjection()
        {
            if (uiRenderTkCamera == null || primaryTkCamera == null)
            {
                return;
            }

            /*
             * Do not inherit the primary camera's resolution override here.
             * The game's override is authored for a 480x270 target and may use
             * StretchToFit. Applying it to a 480x300/360 target projects the
             * same 270-row UI view across every row of the taller texture.
             *
             * Copy the underlying camera convention instead, then give this
             * camera a wildcard FitVisible override whose native resolution
             * exactly matches the expanded target. This preserves one world
             * unit per original UI pixel on both axes and reveals additional
             * vertical world space without rescaling any UI geometry.
             */
            tk2dCamera sourceRoot = primaryTkCamera.SettingsRoot;
            tk2dCameraSettings sourceSettings = sourceRoot.CameraSettings;
            tk2dCameraSettings targetSettings = uiRenderTkCamera.CameraSettings;

            uiRenderTkCamera.InheritConfig = null;
            uiRenderTkCamera.nativeResolutionWidth = OriginalWidth;
            uiRenderTkCamera.nativeResolutionHeight = UiCanvasHeight;
            uiRenderTkCamera.ZoomFactor = primaryTkCamera.ZoomFactor;
            if (expandedUiResolutionOverrides == null)
            {
                expandedUiResolutionOverrides = new[]
                {
                    tk2dCameraResolutionOverride.DefaultOverride
                };
            }
            uiRenderTkCamera.resolutionOverride = expandedUiResolutionOverrides;

            targetSettings.projection = sourceSettings.projection;
            targetSettings.orthographicType = sourceSettings.orthographicType;
            targetSettings.orthographicOrigin = sourceSettings.orthographicOrigin;
            targetSettings.orthographicPixelsPerMeter = sourceSettings.orthographicPixelsPerMeter;
            targetSettings.orthographicSize = sourceSettings.orthographicSize;
            if (sourceSettings.orthographicType == tk2dCameraSettings.OrthographicType.OrthographicSize)
            {
                float sourceNativeHeight = Mathf.Max(1f, sourceRoot.nativeResolutionHeight);
                targetSettings.orthographicSize *= UiCanvasHeight / sourceNativeHeight;
            }
            targetSettings.transparencySortMode = sourceSettings.transparencySortMode;
            targetSettings.fieldOfView = sourceSettings.fieldOfView;
            targetSettings.rect = new Rect(0f, 0f, 1f, 1f);
        }

        private void CenterExpandedUiCamera()
        {
            if (uiCameraObject == null || uiRenderTkCamera == null || primaryTkCamera == null)
            {
                return;
            }
            Vector2 centerOffset = primaryTkCamera.ScreenExtents.center - uiRenderTkCamera.ScreenExtents.center;
            uiCameraObject.transform.localPosition = new Vector3(centerOffset.x, centerOffset.y, 0f);
        }

        private void ValidateExpandedUiProjection()
        {
            if (uiRenderTexture == null || uiRenderTkCamera == null || primaryTkCamera == null ||
                uiRenderTexture.width != OriginalWidth || uiRenderTexture.height != UiCanvasHeight ||
                validatedUiProjectionHeight == UiCanvasHeight)
            {
                return;
            }

            Rect primaryExtents = primaryTkCamera.ScreenExtents;
            Rect uiExtents = uiRenderTkCamera.ScreenExtents;
            if (primaryExtents.width <= 0f || primaryExtents.height <= 0f ||
                uiExtents.width <= 0f || uiExtents.height <= 0f)
            {
                return;
            }

            float primaryPixelsPerUnitX = OriginalWidth / primaryExtents.width;
            float primaryPixelsPerUnitY = OriginalHeight / primaryExtents.height;
            float uiPixelsPerUnitX = uiRenderTexture.width / uiExtents.width;
            float uiPixelsPerUnitY = uiRenderTexture.height / uiExtents.height;
            const float tolerance = 0.001f;
            bool uniform = Mathf.Abs(uiPixelsPerUnitX - uiPixelsPerUnitY) <= tolerance;
            bool matchesOriginal = Mathf.Abs(uiPixelsPerUnitX - primaryPixelsPerUnitX) <= tolerance &&
                Mathf.Abs(uiPixelsPerUnitY - primaryPixelsPerUnitY) <= tolerance;
            bool correctAspect = Mathf.Abs(uiExtents.width / uiExtents.height - TargetAspect) <= tolerance;

            validatedUiProjectionHeight = UiCanvasHeight;
            bool validationFailed = !(uniform && matchesOriginal && correctAspect);
            if (validationFailed)
            {
                Logger.LogError($"Expanded UI projection validation failed: " +
                    $"primaryScale={primaryPixelsPerUnitX:0.###}x{primaryPixelsPerUnitY:0.###}, " +
                    $"uiScale={uiPixelsPerUnitX:0.###}x{uiPixelsPerUnitY:0.###}, " +
                    $"uiExtents={uiExtents.width:0.###}x{uiExtents.height:0.###}.");
            }
            else
            {
                Logger.LogInfo($"Expanded UI projection validated at 480x{UiCanvasHeight}: " +
                    $"uniform scale={uiPixelsPerUnitX:0.###} pixels per world unit.");
            }
        }

        private void CreateUiQuad()
        {
            uiQuadObject = UnityEngine.Object.Instantiate(finalWorldQuad.gameObject);
            uiQuadObject.name = "AspectRatioFix UI Composite Quad";
            uiQuadObject.transform.SetParent(finalWorldQuad.transform.parent, worldPositionStays: false);
            uiQuadObject.transform.localPosition = finalWorldQuad.transform.localPosition;
            uiQuadObject.transform.localRotation = finalWorldQuad.transform.localRotation;
            uiQuadObject.transform.localScale = finalWorldQuad.transform.localScale;
            uiQuadObject.layer = finalWorldQuad.gameObject.layer;
            uiQuadRenderer = uiQuadObject.GetComponent<MeshRenderer>();
            if (uiQuadRenderer == null)
            {
                Logger.LogError("Aspect Ratio Fix could not create the UI composite quad.");
                UnityEngine.Object.Destroy(uiQuadObject);
                uiQuadObject = null;
                return;
            }
            Shader shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                Logger.LogWarning("A transparent built-in shader was not found. " +
                    "Falling back to a copy of the game's final material.");
                uiQuadMaterial = new Material(finalWorldQuad.sharedMaterial);
            }
            else
            {
                uiQuadMaterial = new Material(shader);
            }
            uiQuadMaterial.name = "AspectRatioFix UI Composite Material";
            uiQuadMaterial.mainTexture = uiRenderTexture;
            uiQuadMaterial.color = Color.white;
            uiQuadMaterial.renderQueue = UiCompositeRenderQueue;
            uiQuadRenderer.sharedMaterial = uiQuadMaterial;
            uiQuadRenderer.sortingOrder = BaseCompositeSortingOrder;
            // The duplicated world quad contains cropped UVs; restore all UI.
            SetHorizontalUvRange(uiQuadRenderer, 0f, 1f);
        }

        private void CreateTooltipCamera()
        {
            tooltipCameraObject = new GameObject("AspectRatioFix Tooltip Camera");
            tooltipCameraObject.SetActive(false);
            tooltipCameraObject.transform.SetParent(primaryCamera.transform, worldPositionStays: false);
            tooltipCameraObject.transform.localPosition = Vector3.zero;
            tooltipCameraObject.transform.localRotation = Quaternion.identity;
            tooltipCameraObject.transform.localScale = Vector3.one;
            tooltipRenderCamera = tooltipCameraObject.AddComponent<Camera>();
            tooltipRenderCamera.CopyFrom(primaryCamera);
            tooltipRenderCamera.cullingMask = 1 << tooltipLayer;
            tooltipRenderCamera.clearFlags = CameraClearFlags.SolidColor;
            tooltipRenderCamera.backgroundColor = Color.clear;
            tooltipRenderCamera.targetTexture = tooltipRenderTexture;
            tooltipRenderCamera.rect = new Rect(0f, 0f, 1f, 1f);
            tooltipRenderCamera.depth = primaryCamera.depth + TooltipCameraDepthOffset;
            tooltipRenderCamera.allowHDR = false;
            tooltipRenderCamera.allowMSAA = false;
            tooltipRenderTkCamera = tooltipCameraObject.AddComponent<tk2dCamera>();
            tooltipRenderTkCamera.InheritConfig = primaryTkCamera;
            tooltipRenderTkCamera.nativeResolutionWidth = OriginalWidth;
            tooltipRenderTkCamera.nativeResolutionHeight = OriginalHeight;
            tooltipCameraObject.SetActive(true);
            tooltipRenderTkCamera.UpdateCameraMatrix();
        }

        private void CreateTooltipQuad()
        {
            tooltipQuadObject = UnityEngine.Object.Instantiate(finalWorldQuad.gameObject);
            tooltipQuadObject.name = "AspectRatioFix Bottom Tooltip Composite Quad";
            tooltipQuadObject.transform.SetParent(finalWorldQuad.transform.parent, worldPositionStays: false);
            tooltipQuadObject.transform.localPosition = finalWorldQuad.transform.localPosition;
            tooltipQuadObject.transform.localRotation = finalWorldQuad.transform.localRotation;
            tooltipQuadObject.transform.localScale = finalWorldQuad.transform.localScale;
            tooltipQuadObject.layer = finalWorldQuad.gameObject.layer;
            tooltipQuadRenderer = tooltipQuadObject.GetComponent<MeshRenderer>();
            if (tooltipQuadRenderer == null)
            {
                Logger.LogError("Aspect Ratio Fix could not create the bottom-tooltip composite quad.");
                UnityEngine.Object.Destroy(tooltipQuadObject);
                tooltipQuadObject = null;
                return;
            }
            Shader shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                Logger.LogWarning("A transparent built-in shader was not found. " +
                    "Falling back to a copy of the game's final material for tooltips.");
                tooltipQuadMaterial = new Material(finalWorldQuad.sharedMaterial);
            }
            else
            {
                tooltipQuadMaterial = new Material(shader);
            }
            tooltipQuadMaterial.name = "AspectRatioFix Bottom Tooltip Composite Material";
            tooltipQuadMaterial.mainTexture = tooltipRenderTexture;
            tooltipQuadMaterial.color = Color.white;
            tooltipQuadMaterial.renderQueue = TooltipCompositeRenderQueue;
            tooltipQuadRenderer.sharedMaterial = tooltipQuadMaterial;
            tooltipQuadRenderer.sortingOrder = ForegroundCompositeSortingOrder;
            // The duplicated world quad contains cropped UVs; restore the full 480x270 tooltip frame.
            SetHorizontalUvRange(tooltipQuadRenderer, 0f, 1f);
        }

        private void CreateCombatForegroundCamera()
        {
            combatForegroundRenderTexture = new RenderTexture(OriginalWidth, OriginalHeight, 24,
                RenderTextureFormat.ARGB32);
            combatForegroundRenderTexture.name = "AspectRatioFix_CombatForeground_480x270";
            combatForegroundRenderTexture.filterMode = FilterMode.Point;
            combatForegroundRenderTexture.wrapMode = TextureWrapMode.Clamp;
            combatForegroundRenderTexture.useMipMap = false;
            combatForegroundRenderTexture.autoGenerateMips = false;
            combatForegroundRenderTexture.antiAliasing = 1;
            combatForegroundRenderTexture.Create();

            combatForegroundCameraObject = new GameObject("AspectRatioFix Combat Buff Info Camera");
            combatForegroundCameraObject.SetActive(false);
            combatForegroundCameraObject.transform.SetParent(primaryCamera.transform, false);
            combatForegroundRenderCamera = combatForegroundCameraObject.AddComponent<Camera>();
            combatForegroundRenderCamera.CopyFrom(primaryCamera);
            combatForegroundRenderCamera.cullingMask = 1 << combatForegroundLayer;
            combatForegroundRenderCamera.clearFlags = CameraClearFlags.SolidColor;
            combatForegroundRenderCamera.backgroundColor = Color.clear;
            combatForegroundRenderCamera.targetTexture = combatForegroundRenderTexture;
            combatForegroundRenderCamera.rect = new Rect(0f, 0f, 1f, 1f);
            combatForegroundRenderCamera.depth = primaryCamera.depth + CombatForegroundCameraDepthOffset;
            combatForegroundRenderCamera.allowHDR = false;
            combatForegroundRenderCamera.allowMSAA = false;
            combatForegroundRenderTkCamera = combatForegroundCameraObject.AddComponent<tk2dCamera>();
            combatForegroundRenderTkCamera.InheritConfig = primaryTkCamera;
            combatForegroundRenderTkCamera.nativeResolutionWidth = OriginalWidth;
            combatForegroundRenderTkCamera.nativeResolutionHeight = OriginalHeight;
            combatForegroundCameraObject.SetActive(true);
            combatForegroundRenderTkCamera.UpdateCameraMatrix();
        }

        private void CreateCombatForegroundQuad()
        {
            combatForegroundQuadObject = UnityEngine.Object.Instantiate(finalWorldQuad.gameObject);
            combatForegroundQuadObject.name = "AspectRatioFix Combat Buff Info Composite Quad";
            combatForegroundQuadObject.transform.SetParent(finalWorldQuad.transform.parent, false);
            combatForegroundQuadObject.layer = finalWorldQuad.gameObject.layer;
            combatForegroundQuadRenderer = combatForegroundQuadObject.GetComponent<MeshRenderer>();
            if (combatForegroundQuadRenderer == null)
            {
                UnityEngine.Object.Destroy(combatForegroundQuadObject);
                combatForegroundQuadObject = null;
                return;
            }
            Shader shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            combatForegroundQuadMaterial = shader != null
                ? new Material(shader)
                : new Material(finalWorldQuad.sharedMaterial);
            combatForegroundQuadMaterial.name = "AspectRatioFix Combat Buff Info Composite Material";
            combatForegroundQuadMaterial.mainTexture = combatForegroundRenderTexture;
            combatForegroundQuadMaterial.color = Color.white;
            combatForegroundQuadMaterial.renderQueue = CombatForegroundRenderQueue;
            combatForegroundQuadRenderer.sharedMaterial = combatForegroundQuadMaterial;
            combatForegroundQuadRenderer.sortingOrder = ForegroundCompositeSortingOrder;
            combatForegroundQuadRenderer.enabled = false;
            UpdateCombatForegroundPresentation();
        }

        private void UpdateCombatForegroundPresentation()
        {
            if (combatForegroundCameraObject != null && primaryCamera != null)
            {
                combatForegroundCameraObject.transform.localPosition = Vector3.zero;
                combatForegroundCameraObject.transform.localRotation = Quaternion.identity;
                combatForegroundRenderCamera.nearClipPlane = primaryCamera.nearClipPlane;
                combatForegroundRenderCamera.farClipPlane = primaryCamera.farClipPlane;
                combatForegroundRenderTkCamera?.UpdateCameraMatrix();
            }
            if (combatForegroundQuadObject != null && finalWorldQuad != null && finalTkCamera != null)
            {
                combatForegroundQuadObject.transform.localScale = finalWorldQuad.transform.localScale;
                Vector3 center = finalWorldQuad.bounds.center;
                center -= finalTkCamera.transform.forward * CombatForegroundQuadDepthOffset;
                combatForegroundQuadObject.transform.position = center;
            }
        }

        private void DestroyUiPipeline()
        {
            CancelPresentationCoroutines();
            RestoreNativeShadeDimensions();
            RestoreMonsterSelectorBackgroundDimensions();
            RestoreMonsterShiftBackgroundDimensions();
            UiCompositeActive = false;
            TooltipCompositeActive = false;
            UiInputActive = false;
            UiLayerIndex = -1;
            TooltipLayerIndex = -1;
            DestroyRuntimeObject(ref combatForegroundQuadObject);
            DestroyRuntimeObject(ref combatForegroundCameraObject);
            DestroyRuntimeObject(ref combatForegroundQuadMaterial);
            DestroyRenderTexture(ref combatForegroundRenderTexture);
            DestroyRuntimeObject(ref tooltipQuadObject);
            DestroyRuntimeObject(ref tooltipCameraObject);
            DestroyRuntimeObject(ref tooltipQuadMaterial);
            DestroyRenderTexture(ref tooltipRenderTexture);
            DestroyRuntimeObject(ref uiQuadObject);
            DestroyRuntimeObject(ref uiCameraObject);
            DestroyRuntimeObject(ref uiQuadMaterial);
            DestroyRenderTexture(ref uiRenderTexture);
            tooltipRenderCamera = null;
            tooltipRenderTkCamera = null;
            tooltipQuadRenderer = null;
            uiRenderCamera = null;
            uiRenderTkCamera = null;
            uiInputCamera = null;
            uiQuadRenderer = null;
            validatedUiProjectionHeight = -1;
            combatForegroundRenderCamera = null;
            combatForegroundRenderTkCamera = null;
            combatForegroundQuadRenderer = null;
            combatForegroundLayer = -1;
        }

        private static void DestroyRuntimeObject<TObject>(ref TObject runtimeObject)
            where TObject : UnityEngine.Object
        {
            if (runtimeObject != null)
            {
                UnityEngine.Object.Destroy(runtimeObject);
            }
            runtimeObject = null;
        }

        private static void DestroyRenderTexture(ref RenderTexture renderTexture)
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.Destroy(renderTexture);
            }
            renderTexture = null;
        }

        private void UpdateUiCameraTransform()
        {
            if (!UiCompositeActive || uiRenderCamera == null || primaryCamera == null)
            {
                return;
            }
            uiCameraObject.transform.localPosition = Vector3.zero;
            uiCameraObject.transform.localRotation = Quaternion.identity;
            uiRenderCamera.nearClipPlane = primaryCamera.nearClipPlane;
            uiRenderCamera.farClipPlane = primaryCamera.farClipPlane;
            ConfigureExpandedUiCamera();
        }

        private void UpdateTooltipCameraTransform()
        {
            if (!TooltipCompositeActive || tooltipRenderCamera == null || primaryCamera == null)
            {
                return;
            }
            tooltipCameraObject.transform.localPosition = Vector3.zero;
            tooltipCameraObject.transform.localRotation = Quaternion.identity;
            tooltipRenderCamera.nearClipPlane = primaryCamera.nearClipPlane;
            tooltipRenderCamera.farClipPlane = primaryCamera.farClipPlane;
            tooltipRenderTkCamera?.UpdateCameraMatrix();
        }

        private void UpdateUiQuadLayout()
        {
            if (!UiCompositeActive || uiQuadRenderer == null || finalWorldQuad == null || finalTkCamera == null ||
                Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }
            /*
             * The UI render texture now has the same aspect as the physical
             * output, so present it across the complete final quad. The
             * original 480x270 menu coordinate region remains centered inside
             * the taller 480x300 or 480x360 camera view.
             */
            uiQuadObject.transform.localScale = finalWorldQuad.transform.localScale;
            Vector3 center = finalWorldQuad.bounds.center;
            center -= finalTkCamera.transform.forward * UiQuadDepthOffset;
            uiQuadObject.transform.position = center;
            float screenAspect = Screen.width / (float)Screen.height;
            float uiPixelWidth;
            float uiPixelHeight;
            if (screenAspect < UiAspect)
            {
                uiPixelWidth = Screen.width;
                uiPixelHeight = Screen.width / UiAspect;
            }
            else
            {
                uiPixelHeight = Screen.height;
                uiPixelWidth = Screen.height * UiAspect;
            }
            UiScreenRectPixels = new Rect((Screen.width - uiPixelWidth) / 2f,
                (Screen.height - uiPixelHeight) / 2f, uiPixelWidth, uiPixelHeight);
        }

        private void UpdateTooltipQuadLayout()
        {
            if (!TooltipCompositeActive || tooltipQuadRenderer == null || finalWorldQuad == null || finalTkCamera == null ||
                Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }
            /*
             * Tooltips keep the same 16:9 scale and full 480-pixel width as
             * the regular UI composite. Only their presentation quad moves
             * downward so the authored bottom edge meets the physical output
             * bottom. This avoids enlarging/cropping long tooltip text.
             */
            float screenAspect = Screen.width / (float)Screen.height;
            float widthRatio = 1f;
            float heightRatio = 1f;
            if (screenAspect < UiAspect)
            {
                heightRatio = screenAspect / UiAspect;
            }
            else
            {
                widthRatio = UiAspect / screenAspect;
            }
            tooltipQuadObject.transform.localScale = new Vector3(finalWorldQuad.transform.localScale.x * widthRatio,
                finalWorldQuad.transform.localScale.y * heightRatio, finalWorldQuad.transform.localScale.z);
            Vector3 center = finalWorldQuad.bounds.center;
            if (heightRatio < 1f)
            {
                float bottomAnchorShift = finalWorldQuad.bounds.size.y * (1f - heightRatio) / 2f;
                center -= finalTkCamera.transform.up * bottomAnchorShift;
            }
            center -= finalTkCamera.transform.forward * TooltipQuadDepthOffset;
            tooltipQuadObject.transform.position = center;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ScheduleSceneRegistration();
        }

        private void ScheduleSceneRegistration()
        {
            StopTrackedCoroutine(ref sceneRegistrationCoroutine);
            sceneRegistrationCoroutine = StartCoroutine(RegisterSceneUiAfterInitialization());
        }

        private IEnumerator RegisterSceneUiAfterInitialization()
        {
            /*
             * sceneLoaded is raised before every Start method has necessarily
             * populated its menu lists. Wait through the first completed frame,
             * then register the scene once. Dynamic items created later inherit
             * their presentation layer through the menu event patches below.
             */
            yield return null;
            yield return new WaitForEndOfFrame();
            sceneRegistrationCoroutine = null;
            PruneDestroyedSceneReferences();
            if (!CropActive)
            {
                yield break;
            }
            EnsureUiPipeline();
            AssignKnownUiObjects();
            RegisterExistingKeepersIntros();
            AssignRegisteredKeepersIntros();
            ExcludeUiLayerFromOtherCameras();
            RefreshPresentation(PresentationRefresh.All);
            RefreshUiInputState();
        }

        private void PruneDestroyedSceneReferences()
        {
            /*
             * Destroyed Unity objects retain their managed wrappers and remain
             * valid dictionary keys. Release those keys once the previous
             * scene has finished unloading instead of retaining every scene's
             * layer, camera, and saved-layout state for the plugin lifetime.
             */
            PruneDestroyedKeys(originalMapElementLocalPositions);
            PruneDestroyedKeys(originalMapArrowAnchorCameras);
            PruneDestroyedKeys(mapArrowOffsetWrappers);
            PruneDestroyedKeys(originalMapArrowSiblingIndices);
            PruneDestroyedKeys(originalLayers);
            PruneDestroyedKeys(originalCameraMasks);
            PruneDestroyedKeys(originalHudLocalX);
            PruneDestroyedKeys(originalVictoryBannerLocalPositions);
            PruneDestroyedKeys(originalSkipPromptLocalPositions);
            PruneDestroyedKeys(originalFamiliarSelectionLocalPositions);
            PruneDestroyedKeys(originalKeeperIntroLocalPositions);
            PruneDestroyedObjects(registeredKeepersIntros);
            PruneDestroyedObjects(centeredFamiliarSelections);
            PruneDestroyedObjects(adjustedKeeperIntros);
        }

        private static void PruneDestroyedKeys<TKey, TValue>(Dictionary<TKey, TValue> entries)
            where TKey : UnityEngine.Object
        {
            List<TKey> destroyedKeys = null;
            foreach (TKey key in entries.Keys)
            {
                if (key != null)
                {
                    continue;
                }
                if (destroyedKeys == null)
                {
                    destroyedKeys = new List<TKey>();
                }
                destroyedKeys.Add(key);
            }
            if (destroyedKeys == null)
            {
                return;
            }
            foreach (TKey key in destroyedKeys)
            {
                entries.Remove(key);
            }
        }

        private static void PruneDestroyedObjects<TObject>(List<TObject> entries)
            where TObject : UnityEngine.Object
        {
            for (int index = entries.Count - 1; index >= 0; index--)
            {
                if (entries[index] == null)
                {
                    entries.RemoveAt(index);
                }
            }
        }

        private static void PruneDestroyedObjects<TObject>(HashSet<TObject> entries)
            where TObject : UnityEngine.Object
        {
            List<TObject> destroyedObjects = null;
            foreach (TObject entry in entries)
            {
                if (entry != null)
                {
                    continue;
                }
                if (destroyedObjects == null)
                {
                    destroyedObjects = new List<TObject>();
                }
                destroyedObjects.Add(entry);
            }
            if (destroyedObjects == null)
            {
                return;
            }
            foreach (TObject entry in destroyedObjects)
            {
                entries.Remove(entry);
            }
        }

        private bool TryGetPresentationLayer(GameObject root, out int targetLayer)
        {
            targetLayer = -1;
            if (!CropActive || root == null)
            {
                return false;
            }
            for (Transform current = root.transform; current != null; current = current.parent)
            {
                int currentLayer = current.gameObject.layer;
                if (currentLayer == uiLayer || currentLayer == tooltipLayer)
                {
                    targetLayer = currentLayer;
                    return true;
                }
            }
            return false;
        }

        private bool TryGetMenuPresentationLayer(MenuList menuList, out int targetLayer)
        {
            targetLayer = -1;
            if (menuList == null)
            {
                return false;
            }
            if (TryGetPresentationLayer(menuList.RootElement, out targetLayer))
            {
                return true;
            }
            return TryGetPresentationLayer(menuList.gameObject, out targetLayer);
        }

        private void RefreshUiInputState()
        {
            UiInputActive = false;
            if (!UiCompositeActive || uiLayer < 0)
            {
                return;
            }
            foreach (MenuList menu in MenuList.MenuStack)
            {
                int targetLayer;
                if (menu != null && menu.IsOpenOrLocked &&
                    TryGetMenuPresentationLayer(menu, out targetLayer) && targetLayer == uiLayer)
                {
                    UiInputActive = true;
                    return;
                }
            }
        }

        private void AssignKnownUiObjects()
        {
            if (!UiCompositeActive || uiLayer < 0)
            {
                return;
            }
            UIController uiController = UIController.Instance;
            if (uiController != null)
            {
                /*
                 * Move menu and dialogue roots to the separate expanded UI
                 * camera. Their authored 480x270 region stays centered.
                 * Exploration HUD elements remain on the cropped
                 * world presentation and are inset to the visible output
                 * edges. Full-frame effects remain on the world layer so
                 * they cover the entire cropped output.
                 */
                AssignUiComponent(uiController.IngameMenu);
                AssignUiComponent(uiController.PopupController);
                AssignUiComponent(uiController.Dialogue);
                AssignUiComponent(uiController.TradeMenu);
                AssignUiComponent(uiController.UpgradeMenu);
                AssignUiComponent(uiController.MultiChoicePopup);
                AssignTooltipComponent(uiController.BuffInfoOverlay);
                AssignUiComponent(uiController.NameMenu);
                AssignUiComponent(uiController.ChampionChallenge);
                AssignUiComponent(uiController.MonsterArmy);
                AssignUiComponent(uiController.ButtonPopup);
                AssignUiComponent(uiController.EvolveMenu);
                AssignUiComponent(uiController.CheatMenu);
                AssignUiComponent(uiController.DuelChallenge);
                AssignUiComponent(uiController.OptionsMenu);
                AssignUiComponent(uiController.ChooseCharacter);
                AssignUiComponent(uiController.OnlineArena);
                AssignUiComponent(uiController.Leaderboard);
                AssignUiComponent(uiController.ScrollingCredits);
                AssignUiComponent(uiController.CostumeMenu);
                AssignUiComponent(uiController.NewGameMenu);
                AssignNewGameDescriptionPresentation(uiController.NewGameMenu);
                UpdateMonsterSelectorBackgroundCoverage(
                    uiController.IngameMenu != null ? uiController.IngameMenu.MonsterSelector : null);
                EnsureShadePresentation();
            }
            SaveGameMenu[] saveMenus = Resources.FindObjectsOfTypeAll<SaveGameMenu>();
            foreach (SaveGameMenu saveMenu in saveMenus)
            {
                if (saveMenu != null)
                {
                    AssignUiLayerRecursively(saveMenu.gameObject);
                }
            }
            MainMenu[] mainMenus = Resources.FindObjectsOfTypeAll<MainMenu>();
            foreach (MainMenu mainMenu in mainMenus)
            {
                if (mainMenu == null)
                {
                    continue;
                }
                if (mainMenu.NewGamePlusExplanation != null)
                {
                    AssignUiLayerRecursively(mainMenu.NewGamePlusExplanation);
                }
                if (mainMenu.EAIntro != null)
                {
                    AssignUiLayerRecursively(mainMenu.EAIntro);
                }
                if (mainMenu.MenuTooltip != null)
                {
                    AssignUiLayerRecursively(mainMenu.MenuTooltip);
                }
            }
            AssignExistingTitleAnimations();
            AssignKnownTooltipObjects();
            UpdateExistingMapBackgroundCoverage();
        }

        private void AssignKnownTooltipObjects()
        {
            if (!CropActive || !TooltipCompositeActive || tooltipLayer < 0)
            {
                return;
            }
            foreach (ItemTooltip tooltip in Resources.FindObjectsOfTypeAll<ItemTooltip>())
            {
                AssignTooltipComponent(tooltip);
            }
            foreach (SkillTooltip tooltip in Resources.FindObjectsOfTypeAll<SkillTooltip>())
            {
                AssignTooltipComponent(tooltip);
            }
            foreach (FollowerTooltip tooltip in Resources.FindObjectsOfTypeAll<FollowerTooltip>())
            {
                AssignTooltipComponent(tooltip);
            }
            foreach (MonsterTeamTooltip tooltip in Resources.FindObjectsOfTypeAll<MonsterTeamTooltip>())
            {
                AssignTooltipComponent(tooltip);
            }
            foreach (LeaderboardTooltip tooltip in Resources.FindObjectsOfTypeAll<LeaderboardTooltip>())
            {
                AssignTooltipComponent(tooltip);
            }
        }

        private void OnTooltipOpened(Component tooltip)
        {
            if (!CropActive || tooltip == null)
            {
                return;
            }
            EnsureUiPipeline();
            AssignTooltipComponent(tooltip);
            ExcludeUiLayerFromOtherCameras();
            RefreshPresentation(PresentationRefresh.TooltipComposite);
        }

        private void AssignNewGameDescriptionPresentation(NewGameMenu newGameMenu)
        {
            if (!CropActive || newGameMenu == null)
            {
                return;
            }
            EnsureUiPipeline();
            if (!TooltipCompositeActive || tooltipLayer < 0)
            {
                return;
            }
            if (newGameMenu.DescriptionBG != null)
            {
                SetLayerRecursively(newGameMenu.DescriptionBG.gameObject, tooltipLayer);
            }
            if (newGameMenu.DescriptionText != null)
            {
                SetLayerRecursively(newGameMenu.DescriptionText.gameObject, tooltipLayer);
            }
            RefreshPresentation(PresentationRefresh.TooltipComposite);
        }

        private void UpdateNativeShadeCoverage()
        {
            if (!CropActive)
            {
                return;
            }
            UIController controller = UIController.Instance;
            ShadeLayer shade = controller != null ? controller.ShadeLayer : null;
            tk2dTiledSprite shadeSprite = shade != null ? shade.layer : null;
            if (shadeSprite == null)
            {
                return;
            }
            if (expandedShadeSprite != shadeSprite)
            {
                RestoreNativeShadeDimensions();
                expandedShadeSprite = shadeSprite;
                originalShadeDimensions = shadeSprite.dimensions;
                originalShadeDimensionsCaptured = true;
            }
            Vector2 targetDimensions = new Vector2(OriginalWidth, UiCanvasHeight);
            ResizeSpritePreservingCenter(shadeSprite, targetDimensions);
        }

        private void RestoreNativeShadeDimensions()
        {
            if (expandedShadeSprite != null && originalShadeDimensionsCaptured)
            {
                ResizeSpritePreservingCenter(expandedShadeSprite, originalShadeDimensions);
            }
            expandedShadeSprite = null;
            originalShadeDimensions = Vector2.zero;
            originalShadeDimensionsCaptured = false;
        }

        private void UpdateMonsterSelectorBackgroundCoverage(MonsterSelector selector)
        {
            if (!CropActive || selector == null || selector.Background == null)
            {
                return;
            }

            tk2dTiledSprite background = selector.Background as tk2dTiledSprite;
            if (background == null)
            {
                Logger.LogWarning("Aspect Ratio Fix expected the Monster Selector background to be a tiled sprite.");
                return;
            }

            if (expandedMonsterSelectorBackground != background)
            {
                RestoreMonsterSelectorBackgroundDimensions();
                expandedMonsterSelectorBackground = background;
                originalMonsterSelectorBackgroundDimensions = background.dimensions;
                originalMonsterSelectorBackgroundDimensionsCaptured = true;
            }

            /* Preserve the renderer's visual center while resizing. Some
             * tk2d tiled-sprite anchor configurations grow the mesh in one
             * direction even when the transform itself remains stationary.
             * The parent menu animation and alpha tween stay authoritative.
             */
            Vector2 targetDimensions = new Vector2(
                originalMonsterSelectorBackgroundDimensions.x,
                originalMonsterSelectorBackgroundDimensions.y + UiCanvasHeight - OriginalHeight);
            if (ResizeSpritePreservingCenter(background, targetDimensions))
            {
                Logger.LogInfo($"Expanded Monster Selector background from " +
                    $"{originalMonsterSelectorBackgroundDimensions.x:0}x" +
                    $"{originalMonsterSelectorBackgroundDimensions.y:0} to " +
                    $"{targetDimensions.x:0}x{targetDimensions.y:0}.");
            }
        }

        private void RestoreMonsterSelectorBackgroundDimensions()
        {
            if (expandedMonsterSelectorBackground != null &&
                originalMonsterSelectorBackgroundDimensionsCaptured)
            {
                ResizeSpritePreservingCenter(
                    expandedMonsterSelectorBackground,
                    originalMonsterSelectorBackgroundDimensions);
            }
            expandedMonsterSelectorBackground = null;
            originalMonsterSelectorBackgroundDimensions = Vector2.zero;
            originalMonsterSelectorBackgroundDimensionsCaptured = false;
        }

        private void UpdateMonsterShiftBackgroundCoverage(MonsterShiftMenu shiftMenu)
        {
            if (!CropActive || shiftMenu == null)
            {
                return;
            }

            tk2dBaseSprite bestCandidate = null;
            float bestArea = 0f;
            foreach (tk2dBaseSprite sprite in shiftMenu.GetComponentsInChildren<tk2dBaseSprite>(true))
            {
                if (sprite == null)
                {
                    continue;
                }
                Vector2 dimensions;
                if (!TryGetResizableSpriteDimensions(sprite, out dimensions))
                {
                    continue;
                }
                float width = Mathf.Abs(dimensions.x);
                float height = Mathf.Abs(dimensions.y);
                if (width < OriginalWidth * 0.75f || height < OriginalHeight * 0.75f)
                {
                    continue;
                }
                float area = width * height;
                if (area > bestArea)
                {
                    bestCandidate = sprite;
                    bestArea = area;
                }
            }

            if (bestCandidate == null)
            {
                Logger.LogWarning("Aspect Ratio Fix could not locate a resizable full-frame sprite under MonsterShiftMenu.");
                return;
            }

            if (expandedMonsterShiftBackground != bestCandidate)
            {
                RestoreMonsterShiftBackgroundDimensions();
                expandedMonsterShiftBackground = bestCandidate;
                TryGetResizableSpriteDimensions(bestCandidate, out originalMonsterShiftBackgroundDimensions);
                originalMonsterShiftBackgroundDimensionsCaptured = true;
            }

            Vector2 targetDimensions = new Vector2(
                originalMonsterShiftBackgroundDimensions.x,
                originalMonsterShiftBackgroundDimensions.y + UiCanvasHeight - OriginalHeight);
            if (!ResizeSpritePreservingCenter(bestCandidate, targetDimensions))
            {
                return;
            }
            Logger.LogInfo($"Expanded Monster Shift background " +
                $"'{GetTransformPath(bestCandidate.transform, shiftMenu.transform)}' " +
                $"({bestCandidate.GetType().Name}) from " +
                $"{originalMonsterShiftBackgroundDimensions.x:0}x" +
                $"{originalMonsterShiftBackgroundDimensions.y:0} to " +
                $"{targetDimensions.x:0}x{targetDimensions.y:0}.");
        }

        private static bool TryGetResizableSpriteDimensions(tk2dBaseSprite sprite, out Vector2 dimensions)
        {
            tk2dTiledSprite tiled = sprite as tk2dTiledSprite;
            if (tiled != null)
            {
                dimensions = tiled.dimensions;
                return true;
            }
            tk2dSlicedSprite sliced = sprite as tk2dSlicedSprite;
            if (sliced != null)
            {
                dimensions = sliced.dimensions;
                return true;
            }
            dimensions = Vector2.zero;
            return false;
        }

        private static void SetResizableSpriteDimensions(tk2dBaseSprite sprite, Vector2 dimensions)
        {
            tk2dTiledSprite tiled = sprite as tk2dTiledSprite;
            if (tiled != null)
            {
                tiled.dimensions = dimensions;
                return;
            }
            tk2dSlicedSprite sliced = sprite as tk2dSlicedSprite;
            if (sliced != null)
            {
                sliced.dimensions = dimensions;
            }
        }

        private static void PositionSpriteBoundsCenter(tk2dBaseSprite sprite, Vector3 targetCenter)
        {
            Renderer renderer = sprite != null ? sprite.GetComponent<Renderer>() : null;
            if (renderer != null)
            {
                sprite.transform.position += targetCenter - renderer.bounds.center;
            }
        }

        private static bool ResizeSpritePreservingCenter(tk2dBaseSprite sprite, Vector2 targetDimensions)
        {
            Vector2 currentDimensions;
            if (!TryGetResizableSpriteDimensions(sprite, out currentDimensions) ||
                (currentDimensions - targetDimensions).sqrMagnitude < 0.001f)
            {
                return false;
            }

            Renderer renderer = sprite.GetComponent<Renderer>();
            Vector3 originalCenter = renderer != null ? renderer.bounds.center : sprite.transform.position;
            SetResizableSpriteDimensions(sprite, targetDimensions);
            PositionSpriteBoundsCenter(sprite, originalCenter);
            return true;
        }

        private void RestoreMonsterShiftBackgroundDimensions()
        {
            if (expandedMonsterShiftBackground != null && originalMonsterShiftBackgroundDimensionsCaptured)
            {
                ResizeSpritePreservingCenter(
                    expandedMonsterShiftBackground,
                    originalMonsterShiftBackgroundDimensions);
            }
            expandedMonsterShiftBackground = null;
            originalMonsterShiftBackgroundDimensions = Vector2.zero;
            originalMonsterShiftBackgroundDimensionsCaptured = false;
        }

        private void UpdateExistingMapBackgroundCoverage()
        {
            if (!CropActive)
            {
                return;
            }
            foreach (MapMenu mapMenu in Resources.FindObjectsOfTypeAll<MapMenu>())
            {
                if (mapMenu == null || !mapMenu.gameObject.activeInHierarchy)
                {
                    continue;
                }
                UpdateMapBackgroundCoverage(mapMenu);
                ApplyMapScreenLayout(mapMenu);
                if (expandedMapBackgroundSprite != null)
                {
                    return;
                }
            }
        }

        private void UpdateMapBackgroundCoverage(MapMenu mapMenu)
        {
            if (!CropActive || mapMenu == null)
            {
                return;
            }
            tk2dTiledSprite background = FindMapBackgroundSprite(mapMenu);
            if (background == null)
            {
                Logger.LogWarning("Aspect Ratio Fix could not locate the Map screen background sprite.");
                return;
            }
            if (expandedMapBackgroundSprite != background)
            {
                RestoreMapBackgroundDimensions();
                expandedMapBackgroundSprite = background;
                originalMapBackgroundDimensions = background.dimensions;
                originalMapBackgroundDimensionsCaptured = true;
            }

            Vector2 targetDimensions = new Vector2(
                originalMapBackgroundDimensions.x,
                originalMapBackgroundDimensions.y + UiCanvasHeight - OriginalHeight +
                    MapBackgroundOverscanPerEdge * 2f);
            if (!ResizeSpritePreservingCenter(background, targetDimensions))
            {
                return;
            }
            Logger.LogInfo($"Expanded Map screen background from " +
                $"{originalMapBackgroundDimensions.x:0}x{originalMapBackgroundDimensions.y:0} to " +
                $"{targetDimensions.x:0}x{targetDimensions.y:0}.");
        }

        private static tk2dTiledSprite FindMapBackgroundSprite(MapMenu mapMenu)
        {
            Transform mapContentRoot = mapMenu.Root != null ? mapMenu.Root.transform : null;
            tk2dTiledSprite bestCandidate = null;
            float bestArea = 0f;
            foreach (tk2dTiledSprite sprite in mapMenu.GetComponentsInChildren<tk2dTiledSprite>(true))
            {
                if (sprite == null || sprite == mapMenu.PlayerDot)
                {
                    continue;
                }
                if (mapContentRoot != null &&
                    (sprite.transform == mapContentRoot || sprite.transform.IsChildOf(mapContentRoot)))
                {
                    // Map tiles scroll inside this root. The fixed black
                    // backing is outside it and must remain stationary.
                    continue;
                }
                float width = Mathf.Abs(sprite.dimensions.x);
                float height = Mathf.Abs(sprite.dimensions.y);
                if (width < OriginalWidth * 0.75f || height < OriginalHeight * 0.75f)
                {
                    continue;
                }
                float area = width * height;
                if (area > bestArea)
                {
                    bestCandidate = sprite;
                    bestArea = area;
                }
            }
            return bestCandidate;
        }

        private void RestoreMapBackgroundDimensions()
        {
            if (expandedMapBackgroundSprite != null && originalMapBackgroundDimensionsCaptured)
            {
                ResizeSpritePreservingCenter(expandedMapBackgroundSprite, originalMapBackgroundDimensions);
            }
            expandedMapBackgroundSprite = null;
            originalMapBackgroundDimensions = Vector2.zero;
            originalMapBackgroundDimensionsCaptured = false;
        }

        private void ApplyMapScreenLayout(MapMenu mapMenu)
        {
            if (!CropActive || mapMenu == null)
            {
                return;
            }

            float edgeOffset = (UiCanvasHeight - OriginalHeight) * 0.5f;
            Transform completion = mapMenu.AreaPercent != null ? mapMenu.AreaPercent.transform : null;

            SetMapElementVerticalOffset(completion, edgeOffset);
            ConfigureMapArrowForExpandedCanvas(mapMenu.TopArrow, edgeOffset, mapMenu.transform);
            ConfigureMapArrowForExpandedCanvas(mapMenu.BottomArrow, -edgeOffset, mapMenu.transform);
            SetMapElementVerticalOffset(
                mapMenu.MapMarkerButton != null ? mapMenu.MapMarkerButton.transform : null,
                -edgeOffset);
        }

        private void ConfigureMapArrowForExpandedCanvas(
            tk2dSprite arrow,
            float verticalOffset,
            Transform mapRoot)
        {
            if (arrow == null)
            {
                return;
            }

            /*
             * The Map arrows are camera-anchored controls. Point their native
             * anchors at the expanded UI camera so the existing LateUpdate
             * places them against the 480x300/360 canvas instead of the
             * original 480x270 camera. Native animation and visibility remain
             * owned by the game.
             */
            tk2dCameraAnchor anchor = arrow.GetComponent<tk2dCameraAnchor>();
            if (anchor != null && uiRenderCamera != null)
            {
                if (!originalMapArrowAnchorCameras.ContainsKey(anchor))
                {
                    originalMapArrowAnchorCameras.Add(anchor, anchor.AnchorCamera);
                    Logger.LogInfo($"Retargeted Map arrow anchor '{GetTransformPath(anchor.transform, mapRoot)}' " +
                        $"from the native UI camera to the expanded UI camera.");
                }
                anchor.AnchorCamera = uiRenderCamera;
                anchor.ForceUpdateTransform();
                return;
            }

            /*
             * Some prefab revisions may omit a dedicated anchor. In that case
             * place an offset wrapper above the arrow. Native code can keep
             * writing the arrow's own transform without erasing the wrapper's
             * expanded-canvas offset.
             */
            Transform arrowTransform = arrow.transform;
            GameObject wrapper;
            if (!mapArrowOffsetWrappers.TryGetValue(arrowTransform, out wrapper) || wrapper == null)
            {
                Transform originalParent = arrowTransform.parent;
                int originalSiblingIndex = arrowTransform.GetSiblingIndex();
                wrapper = new GameObject("AspectRatioFix " + arrow.gameObject.name + " Edge Offset");
                wrapper.layer = arrow.gameObject.layer;
                wrapper.transform.SetParent(originalParent, worldPositionStays: false);
                wrapper.transform.localPosition = Vector3.zero;
                wrapper.transform.localRotation = Quaternion.identity;
                wrapper.transform.localScale = Vector3.one;
                wrapper.transform.SetSiblingIndex(originalSiblingIndex);
                arrowTransform.SetParent(wrapper.transform, worldPositionStays: false);
                mapArrowOffsetWrappers.Add(arrowTransform, wrapper);
                originalMapArrowSiblingIndices.Add(arrowTransform, originalSiblingIndex);
                Logger.LogWarning($"Map arrow '{GetTransformPath(arrowTransform, mapRoot)}' has no dedicated " +
                    "tk2dCameraAnchor; using an event-driven parent offset instead.");
            }
            wrapper.transform.localPosition = new Vector3(0f, verticalOffset, 0f);
        }

        private static string GetTransformPath(Transform element, Transform root)
        {
            if (element == null)
            {
                return "<missing>";
            }
            string path = element.name;
            Transform current = element.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        private void SetMapElementVerticalOffset(Transform element, float verticalOffset)
        {
            if (element == null)
            {
                return;
            }
            Vector3 originalPosition;
            if (!originalMapElementLocalPositions.TryGetValue(element, out originalPosition))
            {
                originalPosition = element.localPosition;
                originalMapElementLocalPositions.Add(element, originalPosition);
            }
            element.localPosition = originalPosition + new Vector3(0f, verticalOffset, 0f);
        }

        private void RestoreMapScreenLayout()
        {
            foreach (KeyValuePair<tk2dCameraAnchor, Camera> entry in originalMapArrowAnchorCameras)
            {
                if (entry.Key != null)
                {
                    entry.Key.AnchorCamera = entry.Value;
                    entry.Key.ForceUpdateTransform();
                }
            }
            originalMapArrowAnchorCameras.Clear();

            foreach (KeyValuePair<Transform, GameObject> entry in mapArrowOffsetWrappers)
            {
                Transform arrow = entry.Key;
                GameObject wrapper = entry.Value;
                if (arrow == null || wrapper == null)
                {
                    continue;
                }
                Transform originalParent = wrapper.transform.parent;
                arrow.SetParent(originalParent, worldPositionStays: false);
                int siblingIndex;
                if (originalMapArrowSiblingIndices.TryGetValue(arrow, out siblingIndex))
                {
                    arrow.SetSiblingIndex(siblingIndex);
                }
                UnityEngine.Object.Destroy(wrapper);
            }
            mapArrowOffsetWrappers.Clear();
            originalMapArrowSiblingIndices.Clear();

            foreach (KeyValuePair<Transform, Vector3> entry in originalMapElementLocalPositions)
            {
                if (entry.Key != null)
                {
                    entry.Key.localPosition = entry.Value;
                }
            }
            originalMapElementLocalPositions.Clear();
        }

        private void RestoreMultiChoiceDescriptionPresentation(MultiChoicePopup popup)
        {
            if (!CropActive || popup == null || popup.DescriptionGO == null)
            {
                return;
            }
            EnsureUiPipeline();
            SetLayerRecursively(popup.DescriptionGO, uiLayer);
        }

        private void AssignOnlineArenaDescriptionPresentation()
        {
            if (!CropActive)
            {
                return;
            }
            MultiChoicePopup popup = MultiChoicePopup.Instance;
            if (popup == null || popup.DescriptionGO == null)
            {
                return;
            }
            EnsureUiPipeline();
            if (!TooltipCompositeActive || tooltipLayer < 0)
            {
                return;
            }
            SetLayerRecursively(popup.DescriptionGO, tooltipLayer);
            ExcludeUiLayerFromOtherCameras();
            RefreshPresentation(PresentationRefresh.TooltipComposite);
        }

        private void SetCombatBuffInfoCompositePriority(bool buffInfoOnTop)
        {
            if (uiQuadMaterial != null)
            {
                uiQuadMaterial.renderQueue = buffInfoOnTop
                    ? TooltipCompositeRenderQueue
                    : UiCompositeRenderQueue;
            }
            if (tooltipQuadMaterial != null)
            {
                tooltipQuadMaterial.renderQueue = buffInfoOnTop
                    ? UiCompositeRenderQueue
                    : TooltipCompositeRenderQueue;
            }
            if (uiQuadRenderer != null)
            {
                uiQuadRenderer.sortingOrder = buffInfoOnTop
                    ? ForegroundCompositeSortingOrder
                    : BaseCompositeSortingOrder;
            }
            if (tooltipQuadRenderer != null)
            {
                tooltipQuadRenderer.sortingOrder = buffInfoOnTop
                    ? BaseCompositeSortingOrder
                    : ForegroundCompositeSortingOrder;
            }
        }

        private void RefreshBuffInfoPresentation(BuffInfoOverlay overlay, BuffInfo buffInfo)
        {
            if (!CropActive || overlay == null || buffInfo == null)
            {
                return;
            }
            int targetLayer;
            if (TryGetPresentationLayer(overlay.BuffInfoRoot, out targetLayer))
            {
                SetLayerRecursively(buffInfo.gameObject, targetLayer);
            }
        }

        private void OnBuffInfoOverlayOpened(BuffInfoOverlay overlay)
        {
            if (!CropActive || overlay == null)
            {
                return;
            }
            EnsureUiPipeline();
            AssignTooltipComponent(overlay);
            ExcludeUiLayerFromOtherCameras();
            RefreshPresentation(PresentationRefresh.TooltipComposite);
        }

        private void ShowMissingModalShade(GameObject target)
        {
            UIController uiController = UIController.Instance;
            if (target != null && uiController != null && uiController.ShadeLayer != null)
            {
                uiController.ShadeLayer.Show(target);
                if (CropActive)
                {
                    EnsureShadePresentation();
                }
            }
        }

        private void HideMissingModalShade(GameObject target)
        {
            UIController uiController = UIController.Instance;
            if (target != null && uiController != null && uiController.ShadeLayer != null)
            {
                uiController.ShadeLayer.Hide(target);
            }
        }

        private void RefreshScrollingCreditsEntryPresentation(ScrollingCreditsEntry entry)
        {
            if (!CropActive || entry == null || entry.transform.parent == null)
            {
                return;
            }
            int targetLayer;
            if (TryGetPresentationLayer(entry.transform.parent.gameObject, out targetLayer))
            {
                SetLayerRecursively(entry.gameObject, targetLayer);
            }
        }

        private void RefreshMonsterVisualParticles(MonsterVisuals visuals)
        {
            if (!CropActive || visuals == null)
            {
                return;
            }
            int targetLayer;
            if (!TryGetPresentationLayer(visuals.gameObject, out targetLayer))
            {
                return;
            }
            /*
             * UpdateParticles can instantiate new particle children after the
             * preview hierarchy has already been assigned. Reapply the
             * existing presentation layer to the complete visuals hierarchy
             * without taking a compile-time dependency on ParticleSystemModule.
             */
            SetLayerRecursively(visuals.gameObject, targetLayer);
        }

        private void UpdateExplorationHudLayout()
        {
            if (!CropActive)
            {
                RestoreExplorationHudLayout();
                return;
            }
            UIController uiController = UIController.Instance;
            if (uiController == null)
            {
                return;
            }
            ApplyMinimapInset(uiController.Minimap);
            ApplyTimerInset(uiController);
            ApplyHudHorizontalOffset(uiController.SavingInfo != null ? uiController.SavingInfo.transform : null,
                HorizontalHudInset);
            ApplyHudHorizontalOffset(uiController.VersionString != null ? uiController.VersionString.transform : null,
                -HorizontalHudInset);
            ApplyEdgeAnchoredHudObject(uiController.AchievementWindow);
            /*
             * AreaTitle is already centered on the original camera.
             * Both supported crops keep the same horizontal center, so it
             * does not need an offset.
             */
        }

        private void RegisterKeepersIntro(KeepersIntro intro)
        {
            if (intro == null)
            {
                return;
            }
            if (!registeredKeepersIntros.Contains(intro))
            {
                registeredKeepersIntros.Add(intro);
            }
            if (CropActive)
            {
                AssignKeepersIntroUi(intro);
            }
        }

        private void RegisterExistingKeepersIntros()
        {
            KeepersIntro[] intros = Resources.FindObjectsOfTypeAll<KeepersIntro>();
            foreach (KeepersIntro intro in intros)
            {
                RegisterKeepersIntro(intro);
            }
        }

        private void AssignRegisteredKeepersIntros()
        {
            for (int index = registeredKeepersIntros.Count - 1;
                 index >= 0;
                 index--)
            {
                KeepersIntro intro = registeredKeepersIntros[index];
                if (intro == null)
                {
                    registeredKeepersIntros.RemoveAt(index);
                    continue;
                }
                AssignKeepersIntroUi(intro);
            }
        }

        private void AssignKeepersIntroUi(KeepersIntro intro)
        {
            if (!CropActive || intro == null)
            {
                return;
            }
            EnsureUiPipeline();
            /*
             * Keep the familiar-selection screen in one presentation
             * space. Earlier versions moved the four buttons and the
             * information panel independently, then tried to compensate
             * with manual offsets. That split the authored layout.
             *
             * The full MenuList root and its separate information panel
             * now render together through the expanded UI layer.
             * The surrounding intro scenes remain on the cropped gameplay
             * presentation and therefore transition naturally into play.
             */
            if (intro.SelectFamiliarMenu != null)
            {
                AssignUiLayerRecursively(intro.SelectFamiliarMenu.gameObject);
            }
            if (intro.FamiliarInfoRoot != null)
            {
                AssignUiLayerRecursively(intro.FamiliarInfoRoot);
            }
            ExcludeUiLayerFromOtherCameras();
            RefreshPresentation(PresentationRefresh.UiComposite);
            ScheduleFamiliarSelectionCenter(intro);
            if (IsKeeperIntroCompositionVisible(intro))
            {
                AdjustKeeperIntroComposition(intro);
            }
        }

        private void ScheduleFamiliarSelectionCenter(MenuList menuList)
        {
            if (menuList == null)
            {
                return;
            }
            foreach (KeepersIntro intro in registeredKeepersIntros)
            {
                if (intro != null && intro.SelectFamiliarMenu == menuList)
                {
                    ScheduleFamiliarSelectionCenter(intro);
                    return;
                }
            }
        }

        private void ScheduleFamiliarSelectionCenter(KeepersIntro intro)
        {
            if (!CropActive || intro == null || centeredFamiliarSelections.Contains(intro))
            {
                return;
            }
            StopTrackedCoroutine(ref familiarSelectionLayoutCoroutine);
            familiarSelectionLayoutCoroutine = StartCoroutine(CenterFamiliarSelectionAfterOpening(intro));
        }

        private IEnumerator CenterFamiliarSelectionAfterOpening(KeepersIntro intro)
        {
            /*
             * Wait for the initial familiar buttons and text meshes to finish
             * their first layout pass. Center the authored menu and information
             * panel using those final bounds. Depending on frame timing, the
             * authored position may be briefly visible before this correction.
             */
            yield return null;
            yield return new WaitForEndOfFrame();
            familiarSelectionLayoutCoroutine = null;
            CenterFamiliarSelectionOnce(intro);
        }

        private void CenterFamiliarSelectionOnce(KeepersIntro intro)
        {
            if (!CropActive || intro == null || intro.SelectFamiliarMenu == null ||
                intro.FamiliarInfoRoot == null || uiRenderCamera == null ||
                centeredFamiliarSelections.Contains(intro))
            {
                return;
            }
            Transform menuRoot = intro.SelectFamiliarMenu.RootElement != null
                ? intro.SelectFamiliarMenu.RootElement.transform
                : intro.SelectFamiliarMenu.transform;
            Transform infoRoot = intro.FamiliarInfoRoot.transform;
            List<Transform> roots = new List<Transform>();
            if (menuRoot.IsChildOf(infoRoot))
            {
                roots.Add(infoRoot);
            }
            else
            {
                roots.Add(menuRoot);
                if (!infoRoot.IsChildOf(menuRoot))
                {
                    roots.Add(infoRoot);
                }
            }
            bool boundsFound = false;
            Bounds bounds = new Bounds();
            HashSet<int> visitedRenderers = new HashSet<int>();
            foreach (Transform root in roots)
            {
                bool includeInactive = root == infoRoot;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null ||
                        (!includeInactive && (!renderer.enabled || !renderer.gameObject.activeInHierarchy)) ||
                        !visitedRenderers.Add(renderer.GetInstanceID()))
                    {
                        continue;
                    }
                    if (!boundsFound)
                    {
                        bounds = renderer.bounds;
                        boundsFound = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }
            }
            if (!boundsFound)
            {
                return;
            }
            Vector3 viewportCenter = uiRenderCamera.WorldToViewportPoint(bounds.center);
            Vector3 targetWorld = uiRenderCamera.ViewportToWorldPoint(
                new Vector3(0.5f, viewportCenter.y, viewportCenter.z));
            Vector3 shift = targetWorld - bounds.center;
            shift -= uiRenderCamera.transform.forward * Vector3.Dot(shift, uiRenderCamera.transform.forward);
            foreach (Transform root in roots)
            {
                if (!originalFamiliarSelectionLocalPositions.ContainsKey(root))
                {
                    originalFamiliarSelectionLocalPositions.Add(root, root.localPosition);
                }
                root.position += shift;
            }
            centeredFamiliarSelections.Add(intro);
        }

        private void RestoreFamiliarSelectionLayouts()
        {
            StopTrackedCoroutine(ref familiarSelectionLayoutCoroutine);
            foreach (KeyValuePair<Transform, Vector3> entry in originalFamiliarSelectionLocalPositions)
            {
                if (entry.Key != null)
                {
                    entry.Key.localPosition = entry.Value;
                }
            }
            originalFamiliarSelectionLocalPositions.Clear();
            centeredFamiliarSelections.Clear();
        }

        private bool IsKeeperIntroCompositionVisible(KeepersIntro intro)
        {
            if (intro == null)
            {
                return false;
            }
            foreach (GameObject keeper in intro.Keepers)
            {
                if (keeper != null && keeper.activeInHierarchy)
                {
                    return true;
                }
            }
            foreach (GameObject familiar in intro.Familiars)
            {
                if (familiar != null && familiar.activeInHierarchy)
                {
                    return true;
                }
            }
            return false;
        }

        private void AdjustKeeperIntroComposition(KeepersIntro intro)
        {
            if (!CropActive || intro == null || primaryCamera == null || adjustedKeeperIntros.Contains(intro))
            {
                return;
            }
            float horizontalScale = VisibleWorldWidth / OriginalWidth;
            int pairCount = Mathf.Min(intro.Keepers.Count, intro.Familiars.Count);
            for (int index = 0; index < pairCount; index++)
            {
                ShiftKeeperIntroPair(intro.Keepers[index], intro.Familiars[index], horizontalScale);
            }
            for (int index = pairCount; index < intro.Keepers.Count; index++)
            {
                ShiftKeeperIntroPair(intro.Keepers[index], null, horizontalScale);
            }
            for (int index = pairCount; index < intro.Familiars.Count; index++)
            {
                ShiftKeeperIntroPair(null, intro.Familiars[index], horizontalScale);
            }
            adjustedKeeperIntros.Add(intro);
        }

        private void ShiftKeeperIntroPair(GameObject keeper, GameObject familiar, float horizontalScale)
        {
            if (keeper == null && familiar == null)
            {
                return;
            }
            if (keeper != null && !originalKeeperIntroLocalPositions.ContainsKey(keeper.transform))
            {
                originalKeeperIntroLocalPositions.Add(keeper.transform, keeper.transform.localPosition);
            }
            if (familiar != null && !originalKeeperIntroLocalPositions.ContainsKey(familiar.transform))
            {
                originalKeeperIntroLocalPositions.Add(familiar.transform, familiar.transform.localPosition);
            }
            Vector3 pairCenter;
            if (keeper != null && familiar != null)
            {
                pairCenter = (keeper.transform.position + familiar.transform.position) / 2f;
            }
            else
            {
                pairCenter = keeper != null ? keeper.transform.position : familiar.transform.position;
            }
            Vector3 viewportCenter = primaryCamera.WorldToViewportPoint(pairCenter);
            float targetViewportX = 0.5f + (viewportCenter.x - 0.5f) * horizontalScale;
            Vector3 targetWorld = primaryCamera.ViewportToWorldPoint(
                new Vector3(targetViewportX, viewportCenter.y, viewportCenter.z));
            Vector3 shift = targetWorld - pairCenter;
            shift -= primaryCamera.transform.forward * Vector3.Dot(shift, primaryCamera.transform.forward);
            if (keeper != null)
            {
                keeper.transform.position += shift;
            }
            if (familiar != null)
            {
                familiar.transform.position += shift;
            }
        }

        private void PrepareFamiliarPortraitPosition(KeepersIntro intro)
        {
            if (!CropActive || intro == null || intro.FamiliarBig == null)
            {
                return;
            }
            Transform portrait = intro.FamiliarBig.transform;
            if (originalKeeperIntroLocalPositions.ContainsKey(portrait))
            {
                return;
            }
            originalKeeperIntroLocalPositions.Add(portrait, portrait.localPosition);
            Vector3 position = portrait.localPosition;
            /*
             * FamiliarBig is authored to slide in from the original 480-pixel
             * right edge. Move the complete tween inward by the crop inset so
             * the large selected-familiar portrait remains fully visible.
             */
            position.x -= HorizontalHudInset;
            portrait.localPosition = position;
        }

        private void RestoreKeeperIntroLayouts()
        {
            foreach (KeyValuePair<Transform, Vector3> entry in originalKeeperIntroLocalPositions)
            {
                if (entry.Key != null)
                {
                    entry.Key.localPosition = entry.Value;
                }
            }
            originalKeeperIntroLocalPositions.Clear();
            adjustedKeeperIntros.Clear();
        }

        private void ScheduleSkipPromptAnchor(UIController uiController)
        {
            if (!CropActive || uiController == null)
            {
                return;
            }
            StopTrackedCoroutine(ref skipPromptAnchorCoroutine);
            skipPromptAnchorCoroutine = StartCoroutine(AnchorSkipPromptAfterLayout(uiController));
        }

        private IEnumerator AnchorSkipPromptAfterLayout(UIController uiController)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            skipPromptAnchorCoroutine = null;
            AnchorSkipPromptOnce(uiController);
        }

        private void AnchorSkipPromptOnce(UIController uiController)
        {
            if (!CropActive || uiController == null || uiController.SkipIntro == null)
            {
                return;
            }
            GameObject skipText = uiController.SkipIntro.gameObject;
            List<GameObject> roots = new List<GameObject>();
            roots.Add(skipText);
            /*
             * SkipBar is normally positioned with the text. If it is a
             * sibling rather than a child, shift it by the same amount.
             */
            if (uiController.SkipBar != null && !uiController.SkipBar.transform.IsChildOf(skipText.transform))
            {
                roots.Add(uiController.SkipBar.gameObject);
            }
            foreach (GameObject root in roots)
            {
                if (root == null)
                {
                    continue;
                }
                Transform transform = root.transform;
                Vector3 originalPosition;
                if (!originalSkipPromptLocalPositions.TryGetValue(transform, out originalPosition))
                {
                    originalPosition = transform.localPosition;
                    originalSkipPromptLocalPositions.Add(transform, originalPosition);
                }
                /*
                 * The prompt is authored against the original left edge
                 * of the 480-pixel viewport. The cropped world begins at
                 * the selected profile's horizontal inset, so shift the
                 * prompt right by that amount to keep it fully visible.
                 */
                Vector3 anchoredPosition = originalPosition;
                anchoredPosition.x += HorizontalHudInset;
                transform.localPosition = anchoredPosition;
            }
        }

        private void RestoreSkipPromptLayout()
        {
            StopTrackedCoroutine(ref skipPromptAnchorCoroutine);
            foreach (KeyValuePair<Transform, Vector3> entry in originalSkipPromptLocalPositions)
            {
                if (entry.Key != null)
                {
                    entry.Key.localPosition = entry.Value;
                }
            }
            originalSkipPromptLocalPositions.Clear();
        }

        private bool IsSupportedAspectRatioOutput()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return false;
            }
            float screenAspect = Screen.width / (float)Screen.height;
            return Mathf.Abs(screenAspect - TargetAspect) <= AspectTolerance;
        }

        private void PreparePixelCameraPolicy(PixelCamera2D pixelCamera)
        {
            if (!CropActive || !IsSupportedAspectRatioOutput() || pixelCamera == null)
            {
                return;
            }
            BaseWidthField?.SetValue(pixelCamera, OriginalWidth);
            BaseHeightField?.SetValue(pixelCamera, OriginalHeight);
        }

        private bool CapturePixelCameraPipeline(PixelCamera2D pixelCamera)
        {
            if (pixelCamera == null || FinalCamRectField == null || FinalCameraField == null)
            {
                return false;
            }
            Camera candidatePrimaryCamera = pixelCamera.GetComponent<Camera>();
            tk2dCamera candidatePrimaryTkCamera = pixelCamera.GetComponent<tk2dCamera>();
            MeshRenderer candidateFinalWorldQuad = FinalCamRectField.GetValue(pixelCamera) as MeshRenderer;
            tk2dCamera candidateFinalTkCamera = FinalCameraField.GetValue(pixelCamera) as tk2dCamera;
            if (candidatePrimaryCamera == null || candidatePrimaryTkCamera == null || candidateFinalWorldQuad == null ||
                candidateFinalTkCamera == null)
            {
                return false;
            }
            activePixelCamera = pixelCamera;
            primaryCamera = candidatePrimaryCamera;
            primaryTkCamera = candidatePrimaryTkCamera;
            finalWorldQuad = candidateFinalWorldQuad;
            finalTkCamera = candidateFinalTkCamera;
            return true;
        }

        private bool ApplyWorldPresentationPolicy(PixelCamera2D pixelCamera)
        {
            if (!CropActive || !IsSupportedAspectRatioOutput() || pixelCamera == null)
            {
                return false;
            }
            PreparePixelCameraPolicy(pixelCamera);
            if (!CapturePixelCameraPipeline(pixelCamera))
            {
                return false;
            }
            /*
             * PixelCamera2D owns the timing of camera recalculation. After
             * it updates its normal 16:9 final quad, replace only the final
             * presentation policy:
             *
             * - sample the selected centered region of the 480x270 texture;
             * - fill the complete 4:3 or 16:10 output;
             * - keep the separate UI composite across the full output.
             */
            if (!SetWorldUvCropState(finalWorldQuad, cropped: true))
            {
                return false;
            }
            finalTkCamera.UpdateCameraMatrix();
            ScaleRendererToExtents(finalWorldQuad, finalTkCamera.ScreenExtents);
            /*
             * The UI composite derives its position and physical size from
             * the final world quad. Refresh it only when the game itself
             * recalculates the display.
             */
            RefreshPresentation(PresentationRefresh.CameraDependent);
            return true;
        }

        private void SetCombatUiHierarchy(CombatUIController combatUi, bool useUiLayer)
        {
            if (combatUi == null || (useUiLayer && !CropActive))
            {
                return;
            }
            if (useUiLayer)
            {
                EnsureUiPipeline();
            }
            SetLayerRecursively(combatUi.gameObject, useUiLayer);
            SetUiComponentLayer(combatUi.Menu, useUiLayer);
            SetUiComponentLayer(combatUi.ResultScreen, useUiLayer);
            SetUiComponentLayer(combatUi.OnlineResultScreen, useUiLayer);
            SetUiComponentLayer(combatUi.ExpScreen, useUiLayer);
            SetUiComponentLayer(combatUi.VictoryScreen, useUiLayer);
            if (useUiLayer)
            {
                AssignTooltipComponent(combatUi.Tooltip);
            }
            else
            {
                SetUiComponentLayer(combatUi.Tooltip, false);
            }
            SetUiComponentLayer(combatUi.ComboView, useUiLayer);
            SetUiComponentLayer(combatUi.MonsterInfo, useUiLayer);
            SetUiComponentLayer(combatUi.ActionTitle, useUiLayer);
            SetUiComponentLayer(combatUi.UltimateTitle, useUiLayer);
            SetUiComponentLayer(combatUi.TurnTitle, useUiLayer);
            SetUiComponentLayer(combatUi.ItemMenu, useUiLayer);
            SetUiComponentLayer(combatUi.LevelSelect, useUiLayer);
            if (useUiLayer)
            {
                AssignTooltipComponent(combatUi.ItemTooltip);
            }
            else
            {
                SetUiComponentLayer(combatUi.ItemTooltip, false);
            }
            SetUiComponentLayer(combatUi.ComboMessage, useUiLayer);
            SetUiComponentLayer(combatUi.StartMenu, useUiLayer);
            SetUiComponentLayer(combatUi.AllSelectionProxy, useUiLayer);
            SetUiComponentLayer(combatUi.PlayerBackupMonsters, useUiLayer);
            SetUiComponentLayer(combatUi.EnemyBackupMonsters, useUiLayer);
            SetUiComponentLayer(combatUi.TurnTimer, useUiLayer);
            SetUiComponentLayer(combatUi.ImitateInfo, useUiLayer);
            SetUiComponentLayer(combatUi.BuffInfoMenu, useUiLayer);
            SetCombatMenuListRoots(combatUi, useUiLayer);
            if (useUiLayer)
            {
                SetBuffInfoIconForeground(combatUi.BuffInfoMenu, combatUi.BuffInfoMenu.IsOpen);
            }
        }

        private void OnCombatStarted(CombatUIController combatUi)
        {
            if (!CropActive || combatUi == null)
            {
                return;
            }
            activeCombatUi = combatUi;
            StopTrackedCoroutine(ref combatInitializationCoroutine);
            /*
             * Apply immediately so the first visible combat frame uses
             * the unified UI presentation. The short coroutine below
             * repeats the layer assignment only during combat setup to
             * catch children that the game activates, reparents, or
             * creates during the transition.
             */
            SetCombatUiHierarchy(combatUi, true);
            ExcludeUiLayerFromOtherCameras();
            RefreshPresentation(PresentationRefresh.CameraDependent);
            combatInitializationCoroutine = StartCoroutine(InitializeCombatPresentation(combatUi));
        }

        private IEnumerator InitializeCombatPresentation(CombatUIController combatUi)
        {
            /*
             * This is deliberately bounded. It runs only during the
             * opening combat transition to catch combat UI objects that
             * are activated, reparented, or generated during setup.
             * World scaling is handled by the PixelCamera2D policy hooks.
             */
            for (int frame = 0;
                 frame < CombatInitializationFrameCount;
                 frame++)
            {
                if (!CropActive || combatUi == null)
                {
                    break;
                }
                SetCombatUiHierarchy(combatUi, true);
                yield return null;
            }
            /*
             * Refresh camera masks once more after the hierarchy has
             * settled. Newly assigned children already use the same UI
             * layer, so no continuous enforcement is required.
             */
            if (CropActive && combatUi != null)
            {
                ExcludeUiLayerFromOtherCameras();
                RefreshPresentation(PresentationRefresh.CameraDependent);
            }
            combatInitializationCoroutine = null;
        }

        private void OnCombatEnded(CombatUIController combatUi)
        {
            CancelCombatPresentationCoroutines();
            SetCombatBuffInfoCompositePriority(false);
            if (combatUi?.BuffInfoMenu != null)
            {
                SetBuffInfoIconForeground(combatUi.BuffInfoMenu, false);
            }
            else
            {
                if (combatForegroundQuadRenderer != null)
                {
                    combatForegroundQuadRenderer.enabled = false;
                }
            }
            RestoreVictoryBannerLayout(combatUi != null ? combatUi.VictoryScreen : null);
            SetCombatUiHierarchy(combatUi, false);
            activeCombatUi = null;
        }

        private void PositionVictoryBannerAtTop(VictoryScreen victoryScreen)
        {
            if (!CropActive || victoryScreen == null || primaryCamera == null)
            {
                return;
            }
            Transform root = victoryScreen.transform;
            Vector3 originalLocalPosition;
            if (!originalVictoryBannerLocalPositions.TryGetValue(root, out originalLocalPosition))
            {
                originalLocalPosition = root.localPosition;
                originalVictoryBannerLocalPositions.Add(root, originalLocalPosition);
            }

            /*
             * Preserve the game's normal Victory -> Combat Result sequence.
             * The banner stays on its original world presentation so its top
             * edge can reach the physical output edge instead of the centered
             * 16:9 UI composite. Only its local Y position is changed once,
             * immediately before the game's existing scale tween begins.
             */
            RestoreOriginalLayersRecursively(victoryScreen.gameObject);
            root.localPosition = originalLocalPosition;

            const float TopAnchorPixels = 26f;
            Vector3 viewportPosition = primaryCamera.WorldToViewportPoint(root.position);
            if (float.IsNaN(viewportPosition.z) || float.IsInfinity(viewportPosition.z))
            {
                return;
            }
            viewportPosition.y = 1f - TopAnchorPixels / OriginalHeight;
            Vector3 targetWorldPosition = primaryCamera.ViewportToWorldPoint(viewportPosition);
            Vector3 targetLocalPosition = root.parent != null
                ? root.parent.InverseTransformPoint(targetWorldPosition)
                : targetWorldPosition;

            Vector3 anchoredLocalPosition = originalLocalPosition;
            anchoredLocalPosition.y = targetLocalPosition.y;
            root.localPosition = anchoredLocalPosition;
        }

        private void AssignExistingTitleAnimations()
        {
            foreach (TitleAnimation titleAnimation in Resources.FindObjectsOfTypeAll<TitleAnimation>())
            {
                AssignTitleAnimationPresentation(titleAnimation);
            }
        }

        private void AssignTitleAnimationPresentation(TitleAnimation titleAnimation)
        {
            if (!CropActive || titleAnimation == null || titleAnimation.TitleInstances == null)
            {
                return;
            }
            EnsureUiPipeline();
            if (!UiCompositeActive || uiLayer < 0)
            {
                return;
            }

            /*
             * The layered logo sprites are animated independently and are
             * not guaranteed to share the TitleAnimation transform as their
             * effective presentation root. Render the authored 480x270 logo
             * through the existing UI composite instead of trying to resize
             * or reposition its controller transform. The child scale and
             * color tweens continue unchanged while the complete title fits
             * the supported output automatically.
             */
            foreach (tk2dSprite titleInstance in titleAnimation.TitleInstances)
            {
                if (titleInstance != null)
                {
                    AssignUiLayerRecursively(titleInstance.gameObject);
                }
            }
            ExcludeUiLayerFromOtherCameras();
        }

        private void FadeActiveTitlesWithSceneOverlay(Color overlayColor, float duration, float delay)
        {
            if (!CropActive || overlayColor.a <= 0f)
            {
                return;
            }
            foreach (TitleAnimation titleAnimation in Resources.FindObjectsOfTypeAll<TitleAnimation>())
            {
                if (titleAnimation == null || !titleAnimation.gameObject.activeInHierarchy ||
                    titleAnimation.TitleInstances == null)
                {
                    continue;
                }
                foreach (tk2dSprite title in titleAnimation.TitleInstances)
                {
                    if (title == null || !title.gameObject.activeInHierarchy)
                    {
                        continue;
                    }
                    Color current = title.color;
                    Color target = new Color(current.r, current.g, current.b, 0f);
                    ColorTween.StartTween(title.gameObject, current, target, duration,
                        ColorTween.Type.Linear, delay);
                }
            }
        }
        private void RestoreVictoryBannerLayout(VictoryScreen victoryScreen)
        {
            if (victoryScreen == null)
            {
                return;
            }
            Vector3 originalLocalPosition;
            if (originalVictoryBannerLocalPositions.TryGetValue(victoryScreen.transform, out originalLocalPosition))
            {
                victoryScreen.transform.localPosition = originalLocalPosition;
            }
        }

        private void RestoreVictoryBannerLayouts()
        {
            foreach (KeyValuePair<Transform, Vector3> entry in originalVictoryBannerLocalPositions)
            {
                if (entry.Key != null)
                {
                    entry.Key.localPosition = entry.Value;
                }
            }
            originalVictoryBannerLocalPositions.Clear();
        }

        private void SetCombatMenuListRoots(CombatUIController combatUi, bool useUiLayer)
        {
            if (combatUi == null)
            {
                return;
            }
            HashSet<int> visited = new HashSet<int>();
            SetMenuListGraph(combatUi.Menu?.MenuList, visited, useUiLayer);
            SetMenuListGraph(combatUi.ItemMenu?.MenuList, visited, useUiLayer);
            SetMenuListGraph(combatUi.LevelSelect?.MenuList, visited, useUiLayer);
            SetMenuListGraph(combatUi.StartMenu?.Menu, visited, useUiLayer);
            SetMenuListGraph(combatUi.StartMenu?.MonsterMenu, visited, useUiLayer);
        }

        private void SetBuffInfoIconForeground(BuffInfoMenu buffInfoMenu, bool foreground)
        {
            if (buffInfoMenu?.MenuList == null)
            {
                return;
            }
            /*
             * BuffInfoMenu continually positions its icon list from the combat
             * health bars' world positions. The foreground camera uses the same
             * 480x270 mapping and cropped output quad as those health bars, so
             * the list retains its exact position and scale while compositing
             * above the shade and menus.
             */
            SetMenuListGraph(buffInfoMenu.MenuList, new HashSet<int>(), false);
            if (foreground && combatForegroundLayer >= 0)
            {
                SetLayerRecursively(buffInfoMenu.MenuList.gameObject, combatForegroundLayer);
                SetLayerRecursively(buffInfoMenu.MenuList.RootElement, combatForegroundLayer);
                if (buffInfoMenu.MenuList.SelectionView != null)
                {
                    SetLayerRecursively(buffInfoMenu.MenuList.SelectionView.gameObject, combatForegroundLayer);
                }
            }
            /*
             * Some scene hierarchies place the descriptive BuffInfo panel
             * beneath the MenuList root. The description is independent of the
             * health-bar alignment and remains in the foreground UI composite.
             */
            SetUiComponentLayer(buffInfoMenu.BuffInfo, true);
            if (combatForegroundQuadRenderer != null)
            {
                combatForegroundQuadRenderer.enabled = foreground;
            }
        }

        private void OnCombatBuffInfoOpened(BuffInfoMenu buffInfoMenu)
        {
            if (!CropActive || buffInfoMenu == null)
            {
                return;
            }
            /*
             * Draw the expanded UI composite above the tooltip so the single
             * native shade and Buff Info panel win across the complete frame.
             */
            SetCombatBuffInfoCompositePriority(true);
            SetBuffInfoIconForeground(buffInfoMenu, true);
            RefreshPresentation(PresentationRefresh.CombatForeground);
            ExcludeUiLayerFromOtherCameras();
            ScheduleCombatBuffInfoLayout(buffInfoMenu);
        }

        private void OnCombatBuffInfoHovered(BuffInfoMenu buffInfoMenu)
        {
            if (!CropActive || buffInfoMenu == null || !buffInfoMenu.IsOpen)
            {
                return;
            }
            SetBuffInfoIconForeground(buffInfoMenu, true);
            ScheduleCombatBuffInfoLayout(buffInfoMenu);
        }

        private void OnCombatBuffInfoClosed()
        {
            StopTrackedCoroutine(ref combatBuffInfoLayoutCoroutine);
            SetCombatBuffInfoCompositePriority(false);
            if (activeCombatUi?.BuffInfoMenu != null)
            {
                SetBuffInfoIconForeground(activeCombatUi.BuffInfoMenu, false);
            }
            else
            {
                if (combatForegroundQuadRenderer != null)
                {
                    combatForegroundQuadRenderer.enabled = false;
                }
            }
        }

        private void ScheduleCombatBuffInfoLayout(BuffInfoMenu buffInfoMenu)
        {
            StopTrackedCoroutine(ref combatBuffInfoLayoutCoroutine);
            combatBuffInfoLayoutCoroutine = StartCoroutine(AdjustCombatBuffInfoAfterLayout(buffInfoMenu));
        }

        private IEnumerator AdjustCombatBuffInfoAfterLayout(BuffInfoMenu buffInfoMenu)
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            combatBuffInfoLayoutCoroutine = null;
            KeepCombatBuffInfoInsideUiFrame(buffInfoMenu);
        }

        private void KeepCombatBuffInfoInsideUiFrame(BuffInfoMenu buffInfoMenu)
        {
            if (!CropActive || buffInfoMenu == null || !buffInfoMenu.IsOpen ||
                buffInfoMenu.BuffInfo == null || uiRenderCamera == null)
            {
                return;
            }
            BuffInfo buffInfo = buffInfoMenu.BuffInfo;
            SetUiComponentLayer(buffInfo, true);
            Renderer[] renderers = buffInfo.GetComponentsInChildren<Renderer>(true);
            bool boundsFound = false;
            Bounds bounds = new Bounds();
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }
                if (!boundsFound)
                {
                    bounds = renderer.bounds;
                    boundsFound = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            if (!boundsFound)
            {
                return;
            }
            Vector3[] corners = new Vector3[8]
            {
                new Vector3(bounds.min.x, bounds.min.y, bounds.min.z),
                new Vector3(bounds.min.x, bounds.min.y, bounds.max.z),
                new Vector3(bounds.min.x, bounds.max.y, bounds.min.z),
                new Vector3(bounds.min.x, bounds.max.y, bounds.max.z),
                new Vector3(bounds.max.x, bounds.min.y, bounds.min.z),
                new Vector3(bounds.max.x, bounds.min.y, bounds.max.z),
                new Vector3(bounds.max.x, bounds.max.y, bounds.min.z),
                new Vector3(bounds.max.x, bounds.max.y, bounds.max.z)
            };
            float minimumViewportY = float.PositiveInfinity;
            foreach (Vector3 corner in corners)
            {
                minimumViewportY = Mathf.Min(minimumViewportY, uiRenderCamera.WorldToViewportPoint(corner).y);
            }
            const float BottomMarginPixels = 3f;
            float originalUiBottom = (UiCanvasHeight - OriginalHeight) * 0.5f;
            float targetViewportY = (originalUiBottom + BottomMarginPixels) / UiCanvasHeight;
            if (minimumViewportY >= targetViewportY)
            {
                return;
            }
            float verticalWorldSpan = uiRenderTkCamera != null
                ? uiRenderTkCamera.ScreenExtents.height
                : uiRenderCamera.orthographicSize * 2f;
            float shift = (targetViewportY - minimumViewportY) * verticalWorldSpan;
            buffInfo.transform.position += uiRenderCamera.transform.up * shift;
        }

        private void SetMenuListGraph(MenuList menuList, HashSet<int> visited, bool useUiLayer)
        {
            if (menuList == null || visited == null || !visited.Add(menuList.GetInstanceID()))
            {
                return;
            }
            SetLayerRecursively(menuList.gameObject, useUiLayer);
            SetLayerRecursively(menuList.RootElement, useUiLayer);
            SetUiComponentLayer(menuList.SelectionView, useUiLayer);
            if (menuList.OpeningAnim?.RootElement != null)
            {
                SetLayerRecursively(menuList.OpeningAnim.RootElement, useUiLayer);
            }
            if (menuList.Lists != null)
            {
                foreach (List<MenuListItem> list in menuList.Lists)
                {
                    if (list == null)
                    {
                        continue;
                    }
                    foreach (MenuListItem item in list)
                    {
                        SetUiComponentLayer(item, useUiLayer);
                    }
                }
            }
            SetMenuListGraph(menuList.ConnectedMenuTop, visited, useUiLayer);
            SetMenuListGraph(menuList.ConnectedMenuBottom, visited, useUiLayer);
            SetMenuListGraph(menuList.ConnectedMenuLeft, visited, useUiLayer);
            SetMenuListGraph(menuList.ConnectedMenuRight, visited, useUiLayer);
            if (menuList.ConnectedHoverLists != null)
            {
                foreach (MenuList connected in menuList.ConnectedHoverLists)
                {
                    SetMenuListGraph(connected, visited, useUiLayer);
                }
            }
        }

        private bool RefreshMenuPresentation(MenuList menuList)
        {
            int targetLayer;
            if (!TryGetMenuPresentationLayer(menuList, out targetLayer) &&
                !TryInheritModalMenuPresentation(menuList, out targetLayer))
            {
                return false;
            }
            ApplyMenuPresentationLayer(menuList, targetLayer);
            return true;
        }

        private bool TryInheritModalMenuPresentation(MenuList menuList, out int targetLayer)
        {
            targetLayer = -1;
            if (menuList == null)
            {
                return false;
            }
            int menuIndex = MenuList.MenuStack.LastIndexOf(menuList);
            if (menuIndex <= 0)
            {
                return false;
            }
            MenuList parentMenu = MenuList.MenuStack[menuIndex - 1];
            if (parentMenu == null || !parentMenu.IsLocked ||
                !TryGetMenuPresentationLayer(parentMenu, out targetLayer))
            {
                return false;
            }
            return true;
        }

        private void ApplyMenuPresentationLayer(MenuList menuList, int targetLayer)
        {
            if (menuList == null || targetLayer < 0)
            {
                return;
            }
            SetGameObjectLayer(menuList.gameObject, targetLayer);
            SetLayerRecursively(menuList.RootElement, targetLayer);
            if (menuList.SelectionView != null)
            {
                SetLayerRecursively(menuList.SelectionView.gameObject, targetLayer);
            }
            if (menuList.OpeningAnim != null)
            {
                GameObject openingRoot = menuList.OpeningAnim.RootElement != null
                    ? menuList.OpeningAnim.RootElement
                    : menuList.OpeningAnim.gameObject;
                SetLayerRecursively(openingRoot, targetLayer);
                if (menuList.OpeningAnim.SubElements != null)
                {
                    foreach (GameObject element in menuList.OpeningAnim.SubElements)
                    {
                        SetLayerRecursively(element, targetLayer);
                    }
                }
            }
            if (menuList.Lists == null)
            {
                return;
            }
            foreach (List<MenuListItem> list in menuList.Lists)
            {
                if (list == null)
                {
                    continue;
                }
                foreach (MenuListItem item in list)
                {
                    if (item != null)
                    {
                        SetLayerRecursively(item.gameObject, targetLayer);
                    }
                }
            }
        }

        private void RefreshMenuItemPresentation(MenuList menuList, MenuListItem item)
        {
            int targetLayer;
            if (item != null && TryGetMenuPresentationLayer(menuList, out targetLayer))
            {
                SetLayerRecursively(item.gameObject, targetLayer);
            }
        }

        private void RefreshMenuItemPresentation(MenuList menuList, List<MenuListItem> items)
        {
            if (items == null)
            {
                return;
            }
            foreach (MenuListItem item in items)
            {
                RefreshMenuItemPresentation(menuList, item);
            }
        }

        private void RefreshSelectionViewPresentation(SelectionView selectionView)
        {
            int targetLayer;
            if (selectionView != null && TryGetPresentationLayer(selectionView.gameObject, out targetLayer))
            {
                SetLayerRecursively(selectionView.gameObject, targetLayer);
            }
        }

        private void RefreshOpeningAnimationPresentation(UIOpeningAnim openingAnim)
        {
            if (openingAnim == null)
            {
                return;
            }
            GameObject root = openingAnim.RootElement != null ? openingAnim.RootElement : openingAnim.gameObject;
            int targetLayer;
            if (!TryGetPresentationLayer(root, out targetLayer))
            {
                return;
            }
            SetLayerRecursively(root, targetLayer);
            if (openingAnim.SubElements == null)
            {
                return;
            }
            foreach (GameObject element in openingAnim.SubElements)
            {
                SetLayerRecursively(element, targetLayer);
            }
        }

        private void RefreshOpeningAnimationElementPresentation(UIOpeningAnim openingAnim, GameObject element)
        {
            if (openingAnim == null || element == null)
            {
                return;
            }
            GameObject root = openingAnim.RootElement != null ? openingAnim.RootElement : openingAnim.gameObject;
            int targetLayer;
            if (TryGetPresentationLayer(root, out targetLayer))
            {
                SetLayerRecursively(element, targetLayer);
            }
        }

        private void OnMenuListOpened(MenuList menuList)
        {
            if (!CropActive || menuList == null)
            {
                return;
            }
            if (activeCombatUi?.BuffInfoMenu != null && menuList == activeCombatUi.BuffInfoMenu.MenuList)
            {
                SetBuffInfoIconForeground(activeCombatUi.BuffInfoMenu, activeCombatUi.BuffInfoMenu.IsOpen);
                RefreshUiInputState();
                return;
            }
            /*
             * Opening a menu is the universal inheritance boundary. Only
             * menus already belonging to the UI or tooltip presentation are
             * refreshed; world-space MenuLists remain untouched.
             */
            if (RefreshMenuPresentation(menuList))
            {
                ScheduleFamiliarSelectionCenter(menuList);
            }
            RefreshUiInputState();
        }

        private void ApplyHudHorizontalOffset(Transform transform, float horizontalOffset)
        {
            if (transform == null || originalHudLocalX.ContainsKey(transform))
            {
                return;
            }
            originalHudLocalX.Add(transform, transform.localPosition.x);
            Vector3 localPosition = transform.localPosition;
            localPosition.x += horizontalOffset;
            transform.localPosition = localPosition;
        }

        private void ApplyEdgeAnchoredHudObject(GameObject hudObject)
        {
            if (hudObject == null)
            {
                return;
            }
            float currentX = hudObject.transform.localPosition.x;
            float offset = 0f;
            if (currentX > 1f)
            {
                offset = -HorizontalHudInset;
            }
            else if (currentX < -1f)
            {
                offset = HorizontalHudInset;
            }
            if (!Mathf.Approximately(offset, 0f))
            {
                ApplyHudHorizontalOffset(hudObject.transform, offset);
            }
        }

        private void ApplyTimerInset(UIController uiController)
        {
            if (uiController == null || uiController.Timer == null)
            {
                return;
            }
            if (anchoredUiController != uiController)
            {
                anchoredUiController = uiController;
                originalTimerReferencePosition = uiController.OriginalTimerPos;
                timerReferenceCaptured = true;
                timerInsetApplied = false;
            }
            if (timerInsetApplied)
            {
                return;
            }
            if (!timerReferenceCaptured)
            {
                originalTimerReferencePosition = uiController.OriginalTimerPos;
                timerReferenceCaptured = true;
            }
            Vector3 timerReference = originalTimerReferencePosition;
            timerReference.x -= HorizontalHudInset;
            uiController.OriginalTimerPos = timerReference;
            Vector3 timerPosition = uiController.Timer.transform.localPosition;
            timerPosition.x -= HorizontalHudInset;
            uiController.Timer.transform.localPosition = timerPosition;
            timerInsetApplied = true;
        }

        private void ApplyMinimapInset(MinimapView minimap)
        {
            if (minimap == null || MinimapInitialPositionField == null)
            {
                return;
            }
            if (anchoredMinimap != minimap)
            {
                RestoreMinimapInset();
                CaptureMinimapInitialPosition(minimap);
            }
            if (!minimapInitialPositionCaptured || minimapInsetApplied)
            {
                return;
            }
            Vector3 shiftedInitialPosition = originalMinimapInitialPosition + Vector3.left * HorizontalHudInset;
            MinimapInitialPositionField.SetValue(minimap, shiftedInitialPosition);
            Vector3 currentPosition = minimap.transform.localPosition;
            currentPosition.x -= HorizontalHudInset;
            minimap.transform.localPosition = currentPosition;
            minimapInsetApplied = true;
            Logger.LogInfo($"Anchored minimap and timer with a {HorizontalHudInset:0}-pixel horizontal inset.");
        }

        private void CaptureMinimapInitialPosition(MinimapView minimap)
        {
            if (minimap == null || MinimapInitialPositionField == null)
            {
                return;
            }
            anchoredMinimap = minimap;
            originalMinimapInitialPosition = (Vector3)MinimapInitialPositionField.GetValue(minimap);
            minimapInitialPositionCaptured = true;
            minimapInsetApplied = false;
        }

        private void RestoreMinimapInset()
        {
            if (anchoredMinimap != null && minimapInitialPositionCaptured && MinimapInitialPositionField != null)
            {
                MinimapInitialPositionField.SetValue(anchoredMinimap, originalMinimapInitialPosition);
                if (minimapInsetApplied)
                {
                    Vector3 currentPosition = anchoredMinimap.transform.localPosition;
                    currentPosition.x += HorizontalHudInset;
                    anchoredMinimap.transform.localPosition = currentPosition;
                }
            }
            anchoredMinimap = null;
            minimapInitialPositionCaptured = false;
            minimapInsetApplied = false;
        }

        private void RestoreExplorationHudLayout()
        {
            RestoreMinimapInset();
            if (anchoredUiController != null && timerReferenceCaptured)
            {
                anchoredUiController.OriginalTimerPos = originalTimerReferencePosition;
                if (timerInsetApplied && anchoredUiController.Timer != null)
                {
                    Vector3 timerPosition = anchoredUiController.Timer.transform.localPosition;
                    timerPosition.x += HorizontalHudInset;
                    anchoredUiController.Timer.transform.localPosition = timerPosition;
                }
            }
            anchoredUiController = null;
            timerReferenceCaptured = false;
            timerInsetApplied = false;
            foreach (KeyValuePair<Transform, float> entry in originalHudLocalX)
            {
                if (entry.Key == null)
                {
                    continue;
                }
                Vector3 localPosition = entry.Key.localPosition;
                localPosition.x = entry.Value;
                entry.Key.localPosition = localPosition;
            }
            originalHudLocalX.Clear();
            RestoreSkipPromptLayout();
        }

        private void EnsureShadePresentation()
        {
            UIController uiController = UIController.Instance;
            if (uiController == null)
            {
                return;
            }
            if (uiController.Overlay != null)
            {
                /*
                 * Full-frame transitions belong to the original world camera.
                 * Moving the overlay into the centered UI texture creates a
                 * 16:9 black rectangle on 4:3 and 16:10 outputs.
                 */
                RestoreOriginalLayersRecursively(uiController.Overlay.gameObject);
            }
            if (uiController.ShadeLayer != null)
            {
                // Keep the native shade in the UI depth stack. The expanded UI
                // canvas lets this single sprite cover the complete output.
                AssignUiComponent(uiController.ShadeLayer);
                RefreshPresentation(PresentationRefresh.NativeShade);
            }
            if (uiController.Dialogue != null && uiController.Dialogue.NarrationBackground != null)
            {
                /*
                 * The dialogue text and controls render through the
                 * expanded UI composite. The narration dimmer is
                 * returned to the cropped world presentation so the
                 * dark effect covers the entire cropped frame rather than
                 * leaving uncovered bands above and below.
                 */
                RestoreOriginalLayersRecursively(uiController.Dialogue.NarrationBackground.gameObject);
            }
        }

        private void OnMinimapStarted(MinimapView minimap)
        {
            CaptureMinimapInitialPosition(minimap);
            if (CropActive)
            {
                ApplyMinimapInset(minimap);
            }
        }

        private void SetUiComponentLayer(Component component, bool useUiLayer)
        {
            if (component != null)
            {
                SetLayerRecursively(component.gameObject, useUiLayer);
            }
        }

        private void AssignUiComponent(Component component)
        {
            SetUiComponentLayer(component, true);
        }

        private void AssignTooltipComponent(Component component)
        {
            if (component == null)
            {
                return;
            }
            if (TooltipCompositeActive && tooltipLayer >= 0)
            {
                SetLayerRecursively(component.gameObject, tooltipLayer);
            }
            else
            {
                SetUiComponentLayer(component, true);
            }
        }

        private void SetLayerRecursively(GameObject root, bool useUiLayer)
        {
            if (useUiLayer)
            {
                SetLayerRecursively(root, uiLayer);
            }
            else
            {
                RestoreOriginalLayersRecursively(root);
            }
        }

        private void SetGameObjectLayer(GameObject gameObject, int targetLayer)
        {
            if (gameObject == null || targetLayer < 0)
            {
                return;
            }
            if (!originalLayers.ContainsKey(gameObject))
            {
                originalLayers.Add(gameObject, gameObject.layer);
            }
            gameObject.layer = targetLayer;
        }

        private void SetLayerRecursively(GameObject root, int targetLayer)
        {
            if (root == null || targetLayer < 0)
            {
                return;
            }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != null)
                {
                    SetGameObjectLayer(child.gameObject, targetLayer);
                }
            }
        }

        private void AssignUiLayerRecursively(GameObject root)
        {
            SetLayerRecursively(root, true);
        }

        private void RestoreOriginalLayersRecursively(GameObject root)
        {
            if (root == null)
            {
                return;
            }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == null)
                {
                    continue;
                }
                int originalLayer;
                if (originalLayers.TryGetValue(child.gameObject, out originalLayer))
                {
                    child.gameObject.layer = originalLayer;
                }
            }
        }

        private void RestoreAllOriginalLayers()
        {
            foreach (KeyValuePair<GameObject, int> entry in originalLayers)
            {
                if (entry.Key != null)
                {
                    entry.Key.layer = entry.Value;
                }
            }
            originalLayers.Clear();
        }

        private void ExcludeUiLayerFromOtherCameras()
        {
            if (!UiCompositeActive || uiLayer < 0)
            {
                return;
            }
            int uiMask = 1 << uiLayer;
            int tooltipMask = TooltipCompositeActive && tooltipLayer >= 0 ? 1 << tooltipLayer : 0;
            int foregroundMask = combatForegroundLayer >= 0 ? 1 << combatForegroundLayer : 0;
            int compositeMask = uiMask | tooltipMask | foregroundMask;
            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            foreach (Camera camera in cameras)
            {
                if (camera == null)
                {
                    continue;
                }
                if (camera == uiRenderCamera)
                {
                    camera.cullingMask = uiMask;
                    continue;
                }
                if (camera == tooltipRenderCamera)
                {
                    camera.cullingMask = tooltipMask;
                    continue;
                }
                if (camera == combatForegroundRenderCamera)
                {
                    camera.cullingMask = foregroundMask;
                    continue;
                }
                if (!originalCameraMasks.ContainsKey(camera))
                {
                    originalCameraMasks.Add(camera, camera.cullingMask);
                }
                camera.cullingMask &= ~compositeMask;
            }
        }

        private void RestoreAllCameraMasks()
        {
            foreach (KeyValuePair<Camera, int> entry in originalCameraMasks)
            {
                if (entry.Key != null)
                {
                    entry.Key.cullingMask = entry.Value;
                }
            }
            originalCameraMasks.Clear();
        }

        private void ScaleRendererToExtents(MeshRenderer renderer, Rect targetExtents)
        {
            if (renderer == null)
            {
                return;
            }
            Vector3 currentBounds = renderer.bounds.size;
            Vector3 localScale = renderer.transform.localScale;
            if (currentBounds.x > 0f && currentBounds.y > 0f)
            {
                localScale.x *= targetExtents.width / currentBounds.x;
                localScale.y *= targetExtents.height / currentBounds.y;
                renderer.transform.localScale = localScale;
            }
        }

        private static bool TryGetRendererMesh(MeshRenderer renderer, out Mesh mesh, out Vector2[] uvs)
        {
            mesh = null;
            uvs = null;
            if (renderer == null)
            {
                return false;
            }
            MeshFilter filter = renderer.GetComponent<MeshFilter>() ?? renderer.GetComponentInChildren<MeshFilter>();
            if (filter == null)
            {
                return false;
            }
            mesh = filter.mesh;
            uvs = mesh.uv;
            return uvs != null && uvs.Length > 0;
        }

        private static bool TryGetUvRange(Vector2[] uvs, out float minimum, out float maximum)
        {
            minimum = float.PositiveInfinity;
            maximum = float.NegativeInfinity;
            if (uvs == null || uvs.Length == 0)
            {
                return false;
            }
            foreach (Vector2 uv in uvs)
            {
                minimum = Mathf.Min(minimum, uv.x);
                maximum = Mathf.Max(maximum, uv.x);
            }
            return maximum > minimum;
        }

        private bool CaptureWorldUvCache(MeshRenderer renderer)
        {
            Mesh mesh;
            Vector2[] currentUvs;
            if (!TryGetRendererMesh(renderer, out mesh, out currentUvs))
            {
                return false;
            }
            if (cachedWorldQuadMesh == mesh && cachedWorldFullUvs != null && cachedWorldCroppedUvs != null &&
                cachedWorldFullUvs.Length == currentUvs.Length)
            {
                return true;
            }
            float minimumU;
            float maximumU;
            if (!TryGetUvRange(currentUvs, out minimumU, out maximumU))
            {
                return false;
            }
            float range = maximumU - minimumU;
            cachedWorldQuadMesh = mesh;
            cachedWorldFullUvs = new Vector2[currentUvs.Length];
            cachedWorldCroppedUvs = new Vector2[currentUvs.Length];
            float croppedMinimumU = HorizontalHudInset / OriginalWidth;
            float croppedMaximumU = 1f - croppedMinimumU;
            for (int index = 0; index < currentUvs.Length; index++)
            {
                float normalizedU = (currentUvs[index].x - minimumU) / range;
                Vector2 fullUv = currentUvs[index];
                fullUv.x = normalizedU;
                cachedWorldFullUvs[index] = fullUv;
                Vector2 croppedUv = fullUv;
                croppedUv.x = Mathf.Lerp(croppedMinimumU, croppedMaximumU, normalizedU);
                cachedWorldCroppedUvs[index] = croppedUv;
            }
            cachedWorldUvIsCropped =
                Mathf.Abs(minimumU - croppedMinimumU) < 0.001f &&
                Mathf.Abs(maximumU - croppedMaximumU) < 0.001f;
            return true;
        }

        private bool SetWorldUvCropState(MeshRenderer renderer, bool cropped)
        {
            if (!CaptureWorldUvCache(renderer))
            {
                return false;
            }
            if (cachedWorldUvIsCropped == cropped)
            {
                return true;
            }
            cachedWorldQuadMesh.uv = cropped ? cachedWorldCroppedUvs : cachedWorldFullUvs;
            cachedWorldQuadMesh.UploadMeshData(false);
            cachedWorldUvIsCropped = cropped;
            return true;
        }

        private bool SetHorizontalUvRange(MeshRenderer renderer, float minimumU, float maximumU)
        {
            Mesh mesh;
            Vector2[] uvs;
            if (!TryGetRendererMesh(renderer, out mesh, out uvs))
            {
                return false;
            }
            float existingMinimum;
            float existingMaximum;
            if (!TryGetUvRange(uvs, out existingMinimum, out existingMaximum))
            {
                return false;
            }
            float range = existingMaximum - existingMinimum;
            for (int index = 0; index < uvs.Length; index++)
            {
                float normalizedU = (uvs[index].x - existingMinimum) / range;
                uvs[index].x = Mathf.Lerp(minimumU, maximumU, normalizedU);
            }
            mesh.uv = uvs;
            mesh.UploadMeshData(false);
            return true;
        }

        private void ResizeRenderTexture(RenderTexture renderTexture, int width, int height)
        {
            renderTexture.Release();
            renderTexture.width = width;
            renderTexture.height = height;
            renderTexture.filterMode = FilterMode.Point;
            renderTexture.Create();
        }

        internal static Vector2 ConvertUiMousePosition(Vector2 physicalPosition)
        {
            // The game's manual menu-hover math still uses the authored
            // 480x270 coordinate region centered inside the expanded canvas.
            if (!UiCompositeActive || UiScreenRectPixels.width <= 0f || UiScreenRectPixels.height <= 0f)
            {
                return physicalPosition;
            }
            if (!UiScreenRectPixels.Contains(physicalPosition))
            {
                return new Vector2(-10000f, -10000f);
            }
            float logicalX = (physicalPosition.x - UiScreenRectPixels.xMin) * (OriginalWidth / UiScreenRectPixels.width);
            float logicalY = (physicalPosition.y - UiScreenRectPixels.yMin) * (OriginalHeight / UiScreenRectPixels.height);
            return new Vector2(logicalX, logicalY);
        }

        internal static Vector2 ConvertUiRaycastPosition(Vector2 physicalPosition)
        {
            // The tk2d UI camera raycasts against the complete expanded render
            // target, so it needs 480x300 or 480x360 texture coordinates.
            if (!UiCompositeActive || Screen.width <= 0 || Screen.height <= 0)
            {
                return physicalPosition;
            }
            return new Vector2(physicalPosition.x * OriginalWidth / Screen.width,
                physicalPosition.y * UiCanvasHeight / Screen.height);
        }
        private struct CameraBoundsPatchState
        {
            public tk2dCamera Camera;
            public Rect OriginalExtents;
            public bool Applied;
        }

        private static void ApplyVisibleCameraBounds(CameraController controller, ref CameraBoundsPatchState state)
        {
            if (!CropActive || controller == null || controller.TKCamera == null || TkScreenExtentsField == null)
            {
                return;
            }
            tk2dCamera camera = controller.TKCamera;
            Rect original = (Rect)TkScreenExtentsField.GetValue(camera);
            Rect cropped = original;
            float centerX = original.x + original.width / 2f;
            cropped.x = centerX - VisibleWorldWidth / 2f;
            cropped.width = VisibleWorldWidth;
            state.Camera = camera;
            state.OriginalExtents = original;
            state.Applied = true;
            TkScreenExtentsField.SetValue(camera, cropped);
        }

        private static void RestoreCameraBounds(ref CameraBoundsPatchState state)
        {
            if (!state.Applied || state.Camera == null || TkScreenExtentsField == null)
            {
                return;
            }
            TkScreenExtentsField.SetValue(state.Camera, state.OriginalExtents);
        }

        [HarmonyPatch(typeof(KeepersIntro), "Start")]
        private static class KeepersIntroStartPatch
        {

            private static void Postfix(KeepersIntro __instance)
            {
                Instance?.RegisterKeepersIntro(__instance);
            }
        }

        [HarmonyPatch(typeof(KeepersIntro), "ShowKeepers")]
        private static class KeepersIntroCompositionPatch
        {

            private static void Postfix(KeepersIntro __instance)
            {
                Instance?.AdjustKeeperIntroComposition(__instance);
            }
        }

        [HarmonyPatch(typeof(KeepersIntro), "HideOtherKeepers", new System.Type[] { typeof(bool) })]
        private static class KeepersIntroFamiliarPortraitPatch
        {

            private static void Prefix(KeepersIntro __instance)
            {
                Instance?.PrepareFamiliarPortraitPosition(__instance);
            }
        }


        [HarmonyPatch(typeof(UIController), nameof(UIController.ShowSkipButton))]
        private static class HoldSkipPromptAnchorPatch
        {

            private static void Postfix(UIController __instance)
            {
                Instance?.ScheduleSkipPromptAnchor(__instance);
            }
        }

        [HarmonyPatch(typeof(BaseCutscene), "ShowSkipPrompt")]
        private static class CutsceneSkipPromptAnchorPatch
        {

            private static void Postfix()
            {
                Instance?.ScheduleSkipPromptAnchor(UIController.Instance);
            }
        }


        [HarmonyPatch(typeof(ItemTooltip), nameof(ItemTooltip.Open), new System.Type[] { typeof(BaseItem), typeof(int) })]
        private static class ItemTooltipOpenLayerPatch
        {

            private static void Postfix(ItemTooltip __instance)
            {
                Instance?.OnTooltipOpened(__instance);
            }
        }

        [HarmonyPatch(typeof(ItemTooltip), nameof(ItemTooltip.OpenText))]
        private static class ItemTooltipOpenTextLayerPatch
        {

            private static void Postfix(ItemTooltip __instance)
            {
                Instance?.OnTooltipOpened(__instance);
            }
        }

        [HarmonyPatch(typeof(SkillTooltip), nameof(SkillTooltip.Open))]
        private static class SkillTooltipOpenLayerPatch
        {

            private static void Postfix(SkillTooltip __instance)
            {
                Instance?.OnTooltipOpened(__instance);
            }
        }

        [HarmonyPatch(typeof(FollowerTooltip), nameof(FollowerTooltip.Open))]
        private static class FollowerTooltipOpenLayerPatch
        {

            private static void Postfix(FollowerTooltip __instance)
            {
                Instance?.OnTooltipOpened(__instance);
            }
        }

        [HarmonyPatch(typeof(MonsterTeamTooltip), nameof(MonsterTeamTooltip.Open))]
        private static class MonsterTeamTooltipOpenLayerPatch
        {

            private static void Postfix(MonsterTeamTooltip __instance)
            {
                Instance?.OnTooltipOpened(__instance);
            }
        }

        [HarmonyPatch(typeof(LeaderboardTooltip), nameof(LeaderboardTooltip.Open))]
        private static class LeaderboardTooltipOpenLayerPatch
        {

            private static void Postfix(LeaderboardTooltip __instance)
            {
                Instance?.OnTooltipOpened(__instance);
            }
        }

        [HarmonyPatch(typeof(BuffInfoOverlay), "Open")]
        private static class BuffInfoOverlayOpenPresentationPatch
        {

            private static void Postfix(BuffInfoOverlay __instance)
            {
                Instance?.OnBuffInfoOverlayOpened(__instance);
            }
        }

        [HarmonyPatch(typeof(BuffInfoOverlay), "GetBuffInfo")]
        private static class BuffInfoOverlayGeneratedEntryLayerPatch
        {

            private static void Postfix(BuffInfoOverlay __instance, BuffInfo __result)
            {
                Instance?.RefreshBuffInfoPresentation(__instance, __result);
            }
        }

        [HarmonyPatch(typeof(MapMenu), nameof(MapMenu.Open))]
        private static class MapMenuBackgroundCoveragePatch
        {
            private static void Postfix(MapMenu __instance)
            {
                Instance?.UpdateMapBackgroundCoverage(__instance);
                Instance?.ApplyMapScreenLayout(__instance);
            }
        }

        [HarmonyPatch(typeof(MonsterSelector), nameof(MonsterSelector.Open))]
        private static class MonsterSelectorBackgroundCoveragePatch
        {
            private static void Postfix(MonsterSelector __instance)
            {
                Instance?.UpdateMonsterSelectorBackgroundCoverage(__instance);
            }
        }

        [HarmonyPatch(typeof(MonsterShiftMenu), nameof(MonsterShiftMenu.Open))]
        private static class MonsterShiftMenuBackgroundCoveragePatch
        {
            private static void Prefix(MonsterShiftMenu __instance)
            {
                /* Resize the backing before MenuList.Open collapses its animated
                 * root to zero height. This preserves the game's existing
                 * opening sequence while expanding the same native sprite.
                 */
                Instance?.UpdateMonsterShiftBackgroundCoverage(__instance);
            }
        }

        [HarmonyPatch(typeof(SkillMenu), "SwitchMonster", new System.Type[] { typeof(bool) })]
        private static class SkillMenuSwitchMonsterTooltipPatch
        {
            private static void Postfix(SkillMenu __instance)
            {
                if (__instance != null)
                {
                    Instance?.OnTooltipOpened(__instance.Tooltip);
                }
            }
        }

        [HarmonyPatch(typeof(InventoryMenu), "SwitchMonster", new System.Type[] { typeof(bool) })]
        private static class InventoryMenuSwitchMonsterTooltipPatch
        {
            private static void Postfix(InventoryMenu __instance)
            {
                if (__instance != null)
                {
                    Instance?.OnTooltipOpened(__instance.ItemTooltip);
                }
            }
        }

        [HarmonyPatch(typeof(SaveGameMenu), nameof(SaveGameMenu.Open))]
        private static class SaveGameMenuShadeOpenPatch
        {
            private static void Postfix(SaveGameMenu __instance)
            {
                Instance?.ShowMissingModalShade(__instance.gameObject);
            }
        }

        [HarmonyPatch(typeof(SaveGameMenu), "Close")]
        private static class SaveGameMenuShadeClosePatch
        {
            private static void Postfix(SaveGameMenu __instance)
            {
                Instance?.HideMissingModalShade(__instance.gameObject);
            }
        }

        [HarmonyPatch(typeof(MonsterTeamMenu), nameof(MonsterTeamMenu.Open))]
        private static class MonsterTeamMenuShadeOpenPatch
        {
            private static void Postfix(MonsterTeamMenu __instance)
            {
                Instance?.ShowMissingModalShade(__instance.gameObject);
            }
        }

        [HarmonyPatch(typeof(MonsterTeamMenu), "Close")]
        private static class MonsterTeamMenuShadeClosePatch
        {
            private static void Postfix(MonsterTeamMenu __instance)
            {
                Instance?.HideMissingModalShade(__instance.gameObject);
            }
        }

        [HarmonyPatch(typeof(MonsterMenu), nameof(MonsterMenu.Open))]
        private static class MonsterQuickMenuShadeOpenPatch
        {
            private static void Postfix(MonsterMenu __instance)
            {
                Instance?.ShowMissingModalShade(__instance.gameObject);
            }
        }

        [HarmonyPatch(typeof(MonsterMenu), nameof(MonsterMenu.Close))]
        private static class MonsterQuickMenuShadeClosePatch
        {
            private static void Postfix(MonsterMenu __instance)
            {
                Instance?.HideMissingModalShade(__instance.gameObject);
            }
        }

        [HarmonyPatch(typeof(ScrollingCreditsEntry), nameof(ScrollingCreditsEntry.SetText))]
        private static class ScrollingCreditsEntryLayerPatch
        {

            private static void Postfix(ScrollingCreditsEntry __instance)
            {
                Instance?.RefreshScrollingCreditsEntryPresentation(__instance);
            }
        }

        [HarmonyPatch(typeof(NewGameMenu), nameof(NewGameMenu.Open))]
        private static class NewGameDescriptionPresentationPatch
        {

            private static void Postfix(NewGameMenu __instance)
            {
                Instance?.AssignNewGameDescriptionPresentation(__instance);
            }
        }

        [HarmonyPatch(typeof(MonsterVisuals), nameof(MonsterVisuals.Init))]
        private static class MonsterVisualsInitParticleLayerPatch
        {

            private static void Postfix(MonsterVisuals __instance)
            {
                Instance?.RefreshMonsterVisualParticles(__instance);
            }
        }

        [HarmonyPatch(typeof(MonsterVisuals), "UpdateParticles")]
        private static class MonsterVisualsUpdateParticleLayerPatch
        {

            private static void Postfix(MonsterVisuals __instance)
            {
                Instance?.RefreshMonsterVisualParticles(__instance);
            }
        }

        [HarmonyPatch(typeof(MultiChoicePopup), nameof(MultiChoicePopup.Open))]
        private static class MultiChoicePopupDescriptionResetPatch
        {

            private static void Prefix(MultiChoicePopup __instance)
            {
                Instance?.RestoreMultiChoiceDescriptionPresentation(__instance);
            }
        }

        [HarmonyPatch(typeof(MultiChoicePopup), nameof(MultiChoicePopup.Close))]
        private static class MultiChoicePopupDescriptionClosePatch
        {

            private static void Postfix(MultiChoicePopup __instance)
            {
                Instance?.RestoreMultiChoiceDescriptionPresentation(__instance);
            }
        }

        [HarmonyPatch(typeof(OnlineArenaMenu), nameof(OnlineArenaMenu.OpenHostJoinMenu))]
        private static class OnlineArenaDescriptionPresentationPatch
        {

            private static void Postfix()
            {
                Instance?.AssignOnlineArenaDescriptionPresentation();
            }
        }

        [HarmonyPatch(typeof(BuffInfoMenu), nameof(BuffInfoMenu.Open))]
        private static class CombatBuffInfoOpenPresentationPatch
        {

            private static void Postfix(BuffInfoMenu __instance)
            {
                Instance?.OnCombatBuffInfoOpened(__instance);
            }
        }

        [HarmonyPatch(typeof(BuffInfoMenu), nameof(BuffInfoMenu.OnItemHovered))]
        private static class CombatBuffInfoHoverPresentationPatch
        {

            private static void Postfix(BuffInfoMenu __instance)
            {
                Instance?.OnCombatBuffInfoHovered(__instance);
            }
        }

        [HarmonyPatch(typeof(BuffInfoMenu), nameof(BuffInfoMenu.Close))]
        private static class CombatBuffInfoClosePresentationPatch
        {

            private static void Postfix()
            {
                Instance?.OnCombatBuffInfoClosed();
            }
        }

        [HarmonyPatch(typeof(MenuList), nameof(MenuList.Open))]
        private static class MenuListOpenLayerPatch
        {

            private static void Postfix(MenuList __instance)
            {
                Instance?.OnMenuListOpened(__instance);
            }
        }

        [HarmonyPatch(typeof(MenuList), nameof(MenuList.SetSelecting))]
        private static class MenuListSelectingStatePatch
        {

            private static void Postfix()
            {
                Instance?.RefreshUiInputState();
            }
        }

        [HarmonyPatch(typeof(MenuList), nameof(MenuList.SetLocked))]
        private static class MenuListLockedStatePatch
        {

            private static void Postfix()
            {
                Instance?.RefreshUiInputState();
            }
        }

        [HarmonyPatch(typeof(MenuList), nameof(MenuList.AddMenuItem))]
        private static class MenuListAddMenuItemLayerPatch
        {

            private static void Postfix(MenuList __instance, MenuListItem item)
            {
                Instance?.RefreshMenuItemPresentation(__instance, item);
            }
        }

        [HarmonyPatch(typeof(MenuList), nameof(MenuList.AddDisplayable))]
        private static class MenuListAddDisplayableLayerPatch
        {

            private static void Postfix(MenuList __instance, MenuListItem __result)
            {
                Instance?.RefreshMenuItemPresentation(__instance, __result);
            }
        }

        [HarmonyPatch(typeof(MenuList), nameof(MenuList.AddTextItem))]
        private static class MenuListAddTextItemLayerPatch
        {

            private static void Postfix(MenuList __instance, MenuListItem __result)
            {
                Instance?.RefreshMenuItemPresentation(__instance, __result);
            }
        }

        [HarmonyPatch(typeof(MenuList), nameof(MenuList.FillList))]
        private static class MenuListFillListLayerPatch
        {

            private static void Postfix(MenuList __instance, List<MenuListItem> list)
            {
                Instance?.RefreshMenuItemPresentation(__instance, list);
            }
        }

        [HarmonyPatch(typeof(SelectionView), nameof(SelectionView.Init))]
        private static class SelectionViewInitLayerPatch
        {

            private static void Postfix(SelectionView __instance)
            {
                Instance?.RefreshSelectionViewPresentation(__instance);
            }
        }

        [HarmonyPatch(typeof(UIOpeningAnim), nameof(UIOpeningAnim.Open))]
        private static class UiOpeningAnimationOpenLayerPatch
        {

            private static void Prefix(UIOpeningAnim __instance)
            {
                Instance?.RefreshOpeningAnimationPresentation(__instance);
            }
        }

        [HarmonyPatch(typeof(UIOpeningAnim), nameof(UIOpeningAnim.Add))]
        private static class UiOpeningAnimationAddLayerPatch
        {

            private static void Postfix(UIOpeningAnim __instance, GameObject element)
            {
                Instance?.RefreshOpeningAnimationElementPresentation(__instance, element);
            }
        }

        [HarmonyPatch(typeof(TitleAnimation), "Start")]
        private static class TitleAnimationPresentationPatch
        {

            private static void Postfix(TitleAnimation __instance)
            {
                Instance?.AssignTitleAnimationPresentation(__instance);
            }
        }

        [HarmonyPatch(typeof(ShadeLayer), nameof(ShadeLayer.Show))]
        private static class ShadeLayerShowResizePatch
        {
            private static void Postfix()
            {
                Instance?.RefreshPresentation(PresentationRefresh.NativeShade);
            }
        }

        [HarmonyPatch(typeof(ShadeLayer), nameof(ShadeLayer.Hide))]
        private static class ShadeLayerHideResizePatch
        {
            private static void Postfix()
            {
                // Hide can reveal the preceding entry in ShadeLayer's stack;
                // keep the shared shade expanded while that entry remains.
                Instance?.RefreshPresentation(PresentationRefresh.NativeShade);
            }
        }

        [HarmonyPatch(typeof(OverlayController), nameof(OverlayController.StartFadeOut))]
        private static class SceneOverlayTitleFadePatch
        {
            [HarmonyPostfix]
            private static void Postfix(Color color, float duration, float delay)
            {
                Instance?.FadeActiveTitlesWithSceneOverlay(color, duration, delay);
            }
        }

        [HarmonyPatch(typeof(VictoryScreen), nameof(VictoryScreen.StartVictoryScreen))]
        private static class VictoryScreenTopAnchorPatch
        {

            private static void Prefix(VictoryScreen __instance)
            {
                Instance?.PositionVictoryBannerAtTop(__instance);
            }
        }

        [HarmonyPatch(typeof(CombatUIController), nameof(CombatUIController.StartCombat))]
        private static class CombatStartPresentationPatch
        {

            private static void Postfix(CombatUIController __instance)
            {
                Instance?.OnCombatStarted(__instance);
            }
        }

        [HarmonyPatch(typeof(CombatController), nameof(CombatController.CleanupCombat))]
        private static class CombatSessionCleanupPresentationPatch
        {

            private static void Postfix()
            {
                UIController uiController = UIController.Instance;
                Instance?.OnCombatEnded(uiController != null ? uiController.CombatUI : null);
            }
        }

        [HarmonyPatch(typeof(PixelCamera2D), "UpdateCamera")]
        private static class PixelCameraUpdatePresentationPolicyPatch
        {

            private static void Prefix(PixelCamera2D __instance)
            {
                Instance?.PreparePixelCameraPolicy(__instance);
            }

            private static void Postfix(PixelCamera2D __instance)
            {
                Instance?.ApplyWorldPresentationPolicy(__instance);
            }
        }

        [HarmonyPatch(typeof(PixelCamera2D), nameof(PixelCamera2D.UpdateSecondCam))]
        private static class PixelCameraSecondCamPresentationPolicyPatch
        {

            private static void Prefix(PixelCamera2D __instance)
            {
                Instance?.PreparePixelCameraPolicy(__instance);
            }

            private static void Postfix(PixelCamera2D __instance)
            {
                Instance?.ApplyWorldPresentationPolicy(__instance);
            }
        }

        [HarmonyPatch(typeof(MinimapView), "Start")]
        private static class MinimapStartAnchorPatch
        {

            private static void Postfix(MinimapView __instance)
            {
                Instance?.OnMinimapStarted(__instance);
            }
        }

        [HarmonyPatch(typeof(CameraController), "Update")]
        private static class CameraFollowBoundsPatch
        {

            private static void Prefix(CameraController __instance, ref CameraBoundsPatchState __state)
            {
                ApplyVisibleCameraBounds(__instance, ref __state);
            }

            private static void Postfix(ref CameraBoundsPatchState __state)
            {
                RestoreCameraBounds(ref __state);
            }
        }

        [HarmonyPatch(typeof(CameraController), nameof(CameraController.FocusPlayer))]
        private static class CameraFocusPlayerBoundsPatch
        {

            private static void Prefix(CameraController __instance, ref CameraBoundsPatchState __state)
            {
                ApplyVisibleCameraBounds(__instance, ref __state);
            }

            private static void Postfix(ref CameraBoundsPatchState __state)
            {
                RestoreCameraBounds(ref __state);
            }
        }

        [HarmonyPatch(typeof(Utils), nameof(Utils.CalculateRelativeMousePos))]
        private static class RelativeMousePositionPatch
        {

            private static bool Prefix(ref Vector2 __result)
            {
                if (!UiInputActive)
                {
                    return true;
                }
                __result = ConvertUiMousePosition(Input.mousePosition);
                return false;
            }
        }

        [HarmonyPatch(typeof(tk2dUIManager), "RaycastForUIItem")]
        private static class Tk2dMouseRaycastPatch
        {

            private static void Prefix(ref Vector2 screenPos)
            {
                if (UiInputActive)
                {
                    screenPos = ConvertUiRaycastPosition(screenPos);
                }
            }
        }
    }
}
