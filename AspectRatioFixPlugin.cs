using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MonsterSanctuaryAspectRatioFix
{
    [BepInPlugin("lemonacle.MonsterSanctuary.AspectRatioFix", "Aspect Ratio Fix", "2.1.11")]
    public class AspectRatioFixPlugin : BaseUnityPlugin
    {
        private const int OriginalWidth = 480;
        private const int OriginalHeight = 270;
        private const float AspectRatioFixAspect = 4f / 3f;
        private const float SixteenByTenAspect = 16f / 10f;
        private const float AspectTolerance = 0.02f;
        private const float UiAspect = 16f / 9f;
        private static float VisibleWorldWidth { get; set; } = 360f;
        private static float TargetAspect { get; set; } = AspectRatioFixAspect;
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
        private Coroutine pendingApply;
        private Coroutine sceneRegistrationCoroutine;
        private Coroutine familiarSelectionLayoutCoroutine;
        private Coroutine buffInfoUiShadeHideCoroutine;
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
        private RenderTexture uiRenderTexture;
        private GameObject uiQuadObject;
        private MeshRenderer uiQuadRenderer;
        private Material uiQuadMaterial;
        private int tooltipLayer = -1;
        private GameObject tooltipCameraObject;
        private Camera tooltipRenderCamera;
        private tk2dCamera tooltipRenderTkCamera;
        private RenderTexture tooltipRenderTexture;
        private GameObject tooltipQuadObject;
        private MeshRenderer tooltipQuadRenderer;
        private Material tooltipQuadMaterial;
        private GameObject buffInfoUiShadeObject;
        private tk2dTiledSprite buffInfoUiShadeSprite;
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
        private bool suppressTooltipCompositeForCombatBuffInfo;
        /*
         * Skip prompts are anchored only when shown. Their renderer
         * bounds are not recalculated during normal gameplay.
         */
        private readonly Dictionary<Transform, Vector3> originalSkipPromptLocalPositions = new Dictionary<Transform, Vector3>();
        private Coroutine skipPromptAnchorCoroutine;
        /*
         * Familiar-choice layout is captured and centered once when
         * the intro initializes, before the selection menu opens.
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
            Logger.LogInfo("Aspect Ratio Fix 2.1.11 4:3 and 16:10 camera-policy plugin loaded.");
            harmony = new Harmony("lemonacle.MonsterSanctuary.AspectRatioFix");
            harmony.PatchAll();
            previousScreenWidth = Screen.width;
            previousScreenHeight = Screen.height;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ScheduleApply();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (sceneRegistrationCoroutine != null)
            {
                StopCoroutine(sceneRegistrationCoroutine);
                sceneRegistrationCoroutine = null;
            }
            if (buffInfoUiShadeHideCoroutine != null)
            {
                StopCoroutine(buffInfoUiShadeHideCoroutine);
                buffInfoUiShadeHideCoroutine = null;
            }
            DestroyBuffInfoUiShade();
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
            if (Input.GetKeyDown(KeyCode.F8))
            {
                ScheduleApply();
            }
            if (!CropActive)
            {
                return;
            }
            UpdateExplorationHudLayout();
            UpdateUiCameraTransform();
            UpdateTooltipCameraTransform();
            UpdateUiQuadLayout();
            UpdateTooltipQuadLayout();
        }

        private void ScheduleApply()
        {
            if (pendingApply != null)
            {
                StopCoroutine(pendingApply);
            }
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
            EnsureFullFrameEffectsUseWorldPresentation();
            ExcludeUiLayerFromOtherCameras();
            UpdateExplorationHudLayout();
            UpdateUiCameraTransform();
            UpdateTooltipCameraTransform();
            UpdateUiQuadLayout();
            UpdateTooltipQuadLayout();
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
            Logger.LogInfo($"Aspect Ratio Fix applied with separate UI composite: " +
                $"world={VisibleWorldWidth:0}x270 crop, UI=480x270, " +
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
            CropActive = false;
            UiCompositeActive = false;
            TooltipCompositeActive = false;
            UiInputActive = false;
            SetCombatBuffInfoCompositePriority(false);
            DestroyBuffInfoUiShade();
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
            if (uiRenderTexture == null)
            {
                uiRenderTexture = new RenderTexture(OriginalWidth, OriginalHeight, 24, RenderTextureFormat.ARGB32);
                uiRenderTexture.name = "AspectRatioFix_UI_480x270";
                uiRenderTexture.filterMode = FilterMode.Point;
                uiRenderTexture.wrapMode = TextureWrapMode.Clamp;
                uiRenderTexture.useMipMap = false;
                uiRenderTexture.autoGenerateMips = false;
                uiRenderTexture.antiAliasing = 1;
                uiRenderTexture.Create();
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
            if (tooltipQuadRenderer != null)
            {
                tooltipQuadRenderer.enabled = !suppressTooltipCompositeForCombatBuffInfo;
            }
            UiCompositeActive = uiRenderCamera != null && uiQuadRenderer != null;
            TooltipCompositeActive = tooltipRenderCamera != null && tooltipQuadRenderer != null;
        }

        private int FindUnusedLayer(int excludedLayer = -1)
        {
            bool[] usedLayers = new bool[32];
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject gameObject in objects)
            {
                if (gameObject != null && gameObject.layer >= 0 && gameObject.layer < 32)
                {
                    usedLayers[gameObject.layer] = true;
                }
            }
            for (int layer = 30; layer >= 8; layer--)
            {
                if (layer != excludedLayer && !usedLayers[layer] && string.IsNullOrEmpty(LayerMask.LayerToName(layer)))
                {
                    return layer;
                }
            }
            // Layer 30 is the least likely remaining layer to be used.
            if (excludedLayer != 30 && !usedLayers[30])
            {
                return 30;
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
            uiRenderCamera.depth = primaryCamera.depth + 10f;
            uiRenderCamera.allowHDR = false;
            uiRenderCamera.allowMSAA = false;
            uiRenderTkCamera = uiCameraObject.AddComponent<tk2dCamera>();
            uiRenderTkCamera.InheritConfig = primaryTkCamera;
            uiRenderTkCamera.nativeResolutionWidth = OriginalWidth;
            uiRenderTkCamera.nativeResolutionHeight = OriginalHeight;
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
            uiQuadMaterial.renderQueue = 4000;
            uiQuadRenderer.sharedMaterial = uiQuadMaterial;
            uiQuadRenderer.sortingOrder = 32766;
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
            tooltipRenderCamera.depth = primaryCamera.depth + 11f;
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
            tooltipQuadMaterial.renderQueue = 4001;
            tooltipQuadRenderer.sharedMaterial = tooltipQuadMaterial;
            tooltipQuadRenderer.sortingOrder = 32767;
            // The duplicated world quad contains cropped UVs; restore the full 480x270 tooltip frame.
            SetHorizontalUvRange(tooltipQuadRenderer, 0f, 1f);
        }

        private void DestroyUiPipeline()
        {
            DestroyBuffInfoUiShade();
            UiCompositeActive = false;
            TooltipCompositeActive = false;
            UiInputActive = false;
            UiLayerIndex = -1;
            TooltipLayerIndex = -1;
            if (tooltipQuadObject != null)
            {
                UnityEngine.Object.Destroy(tooltipQuadObject);
                tooltipQuadObject = null;
            }
            if (tooltipCameraObject != null)
            {
                UnityEngine.Object.Destroy(tooltipCameraObject);
                tooltipCameraObject = null;
            }
            if (tooltipQuadMaterial != null)
            {
                UnityEngine.Object.Destroy(tooltipQuadMaterial);
                tooltipQuadMaterial = null;
            }
            if (tooltipRenderTexture != null)
            {
                tooltipRenderTexture.Release();
                UnityEngine.Object.Destroy(tooltipRenderTexture);
                tooltipRenderTexture = null;
            }
            if (uiQuadObject != null)
            {
                UnityEngine.Object.Destroy(uiQuadObject);
                uiQuadObject = null;
            }
            if (uiCameraObject != null)
            {
                UnityEngine.Object.Destroy(uiCameraObject);
                uiCameraObject = null;
            }
            if (uiQuadMaterial != null)
            {
                UnityEngine.Object.Destroy(uiQuadMaterial);
                uiQuadMaterial = null;
            }
            if (uiRenderTexture != null)
            {
                uiRenderTexture.Release();
                UnityEngine.Object.Destroy(uiRenderTexture);
                uiRenderTexture = null;
            }
            tooltipRenderCamera = null;
            tooltipRenderTkCamera = null;
            tooltipQuadRenderer = null;
            uiRenderCamera = null;
            uiRenderTkCamera = null;
            uiInputCamera = null;
            uiQuadRenderer = null;
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
            uiRenderTkCamera?.UpdateCameraMatrix();
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
            uiQuadObject.transform.localScale = new Vector3(finalWorldQuad.transform.localScale.x * widthRatio,
                finalWorldQuad.transform.localScale.y * heightRatio, finalWorldQuad.transform.localScale.z);
            Vector3 center = finalWorldQuad.bounds.center;
            center -= finalTkCamera.transform.forward * 0.01f;
            uiQuadObject.transform.position = center;
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
            UiScreenRectPixels = new Rect((Screen.width - uiPixelWidth) / 2f, (Screen.height - uiPixelHeight) / 2f, uiPixelWidth,
                uiPixelHeight);
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
            center -= finalTkCamera.transform.forward * 0.02f;
            tooltipQuadObject.transform.position = center;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ScheduleSceneRegistration();
        }

        private void ScheduleSceneRegistration()
        {
            if (sceneRegistrationCoroutine != null)
            {
                StopCoroutine(sceneRegistrationCoroutine);
            }
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
            if (!CropActive)
            {
                yield break;
            }
            EnsureUiPipeline();
            AssignKnownUiObjects();
            RegisterExistingKeepersIntros();
            AssignRegisteredKeepersIntros();
            ExcludeUiLayerFromOtherCameras();
            RefreshUiInputState();
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
                 * Move menu and dialogue roots to the separate 480x270
                 * camera. Exploration HUD elements remain on the cropped
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
                AssignUiComponent(uiController.BuffInfoOverlay);
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
                EnsureFullFrameEffectsUseWorldPresentation();
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
            UpdateTooltipCameraTransform();
            UpdateTooltipQuadLayout();
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
            UpdateTooltipCameraTransform();
            UpdateTooltipQuadLayout();
        }

        private void EnsureBuffInfoUiShade()
        {
            if (!CropActive || !UiCompositeActive || uiLayer < 0 || buffInfoUiShadeObject != null)
            {
                return;
            }
            UIController uiController = UIController.Instance;
            ShadeLayer sourceShade = uiController != null ? uiController.ShadeLayer : null;
            if (sourceShade == null || sourceShade.layer == null)
            {
                return;
            }
            buffInfoUiShadeObject = UnityEngine.Object.Instantiate(sourceShade.gameObject);
            buffInfoUiShadeObject.name = "AspectRatioFix Buff Info UI Shade";
            buffInfoUiShadeObject.transform.SetParent(sourceShade.transform.parent, worldPositionStays: true);
            ShadeLayer clonedShade = buffInfoUiShadeObject.GetComponent<ShadeLayer>();
            if (clonedShade != null)
            {
                buffInfoUiShadeSprite = clonedShade.layer;
                clonedShade.enabled = false;
            }
            if (buffInfoUiShadeSprite == null)
            {
                buffInfoUiShadeSprite = buffInfoUiShadeObject.GetComponentInChildren<tk2dTiledSprite>(true);
            }
            foreach (ColorTween tween in buffInfoUiShadeObject.GetComponentsInChildren<ColorTween>(true))
            {
                if (tween != null)
                {
                    tween.enabled = false;
                }
            }
            SetLayerRecursively(buffInfoUiShadeObject, uiLayer);
            buffInfoUiShadeObject.SetActive(false);
            ExcludeUiLayerFromOtherCameras();
        }

        private void ShowBuffInfoUiShade(BuffInfoOverlay overlay)
        {
            if (!CropActive || overlay == null)
            {
                return;
            }
            EnsureUiPipeline();
            EnsureBuffInfoUiShade();
            UIController uiController = UIController.Instance;
            ShadeLayer sourceShade = uiController != null ? uiController.ShadeLayer : null;
            if (buffInfoUiShadeObject == null || buffInfoUiShadeSprite == null ||
                sourceShade == null || sourceShade.layer == null)
            {
                return;
            }
            if (buffInfoUiShadeHideCoroutine != null)
            {
                StopCoroutine(buffInfoUiShadeHideCoroutine);
                buffInfoUiShadeHideCoroutine = null;
            }
            buffInfoUiShadeObject.transform.localPosition = sourceShade.transform.localPosition;
            buffInfoUiShadeObject.transform.localRotation = sourceShade.transform.localRotation;
            buffInfoUiShadeObject.transform.localScale = sourceShade.transform.localScale;
            buffInfoUiShadeSprite.dimensions = sourceShade.layer.dimensions;
            buffInfoUiShadeObject.SetActive(true);
            ColorTween.EndTween(buffInfoUiShadeSprite.gameObject, recursive: false);
            Color transparent = new Color(0f, 0f, 0f, 0f);
            Color shaded = new Color(0f, 0f, 0f, sourceShade.transparency);
            buffInfoUiShadeSprite.color = transparent;
            ColorTween.StartTween(buffInfoUiShadeSprite.gameObject, transparent, shaded, 0.1f);
        }

        private void HideBuffInfoUiShade()
        {
            if (buffInfoUiShadeObject == null || buffInfoUiShadeSprite == null ||
                !buffInfoUiShadeObject.activeSelf)
            {
                return;
            }
            if (buffInfoUiShadeHideCoroutine != null)
            {
                StopCoroutine(buffInfoUiShadeHideCoroutine);
            }
            Color current = buffInfoUiShadeSprite.color;
            ColorTween.EndTween(buffInfoUiShadeSprite.gameObject, recursive: false);
            ColorTween.StartTween(buffInfoUiShadeSprite.gameObject, current, new Color(0f, 0f, 0f, 0f), 0.1f);
            buffInfoUiShadeHideCoroutine = StartCoroutine(DisableBuffInfoUiShadeAfterFade());
        }

        private IEnumerator DisableBuffInfoUiShadeAfterFade()
        {
            yield return new WaitForSecondsRealtime(0.12f);
            buffInfoUiShadeHideCoroutine = null;
            if (buffInfoUiShadeObject != null)
            {
                buffInfoUiShadeObject.SetActive(false);
            }
        }

        private void DestroyBuffInfoUiShade()
        {
            if (buffInfoUiShadeObject != null)
            {
                UnityEngine.Object.Destroy(buffInfoUiShadeObject);
                buffInfoUiShadeObject = null;
            }
            buffInfoUiShadeSprite = null;
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
            UpdateTooltipCameraTransform();
            UpdateTooltipQuadLayout();
        }

        private void SetCombatBuffInfoCompositePriority(bool buffInfoOnTop)
        {
            suppressTooltipCompositeForCombatBuffInfo = false;
            ApplyTooltipCompositeVisibility();
            if (uiQuadMaterial != null)
            {
                uiQuadMaterial.renderQueue = buffInfoOnTop ? 4001 : 4000;
            }
            if (tooltipQuadMaterial != null)
            {
                tooltipQuadMaterial.renderQueue = buffInfoOnTop ? 4000 : 4001;
            }
            if (uiQuadRenderer != null)
            {
                uiQuadRenderer.sortingOrder = buffInfoOnTop ? 32767 : 32766;
            }
            if (tooltipQuadRenderer != null)
            {
                tooltipQuadRenderer.sortingOrder = buffInfoOnTop ? 32766 : 32767;
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
             * now render together through the complete 480x270 UI layer.
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
            UpdateUiCameraTransform();
            UpdateUiQuadLayout();
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
            if (familiarSelectionLayoutCoroutine != null)
            {
                StopCoroutine(familiarSelectionLayoutCoroutine);
            }
            familiarSelectionLayoutCoroutine = StartCoroutine(CenterFamiliarSelectionAfterOpening(intro));
        }

        private IEnumerator CenterFamiliarSelectionAfterOpening(KeepersIntro intro)
        {
            /*
             * Wait for the initial familiar buttons and text meshes to finish
             * their first layout pass. Center the authored menu and information
             * panel before the menu becomes interactive so the row does not jump
             * when the selection controls appear.
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
            if (familiarSelectionLayoutCoroutine != null)
            {
                StopCoroutine(familiarSelectionLayoutCoroutine);
                familiarSelectionLayoutCoroutine = null;
            }
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
            if (skipPromptAnchorCoroutine != null)
            {
                StopCoroutine(skipPromptAnchorCoroutine);
            }
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
            if (skipPromptAnchorCoroutine != null)
            {
                StopCoroutine(skipPromptAnchorCoroutine);
                skipPromptAnchorCoroutine = null;
            }
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
             * - keep the separate UI composite at the full 480x270 frame.
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
            if (UiCompositeActive)
            {
                UpdateUiCameraTransform();
                UpdateUiQuadLayout();
            }
            if (TooltipCompositeActive)
            {
                UpdateTooltipCameraTransform();
                UpdateTooltipQuadLayout();
            }
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
                RestoreBuffInfoIconHierarchy(combatUi.BuffInfoMenu);
            }
        }

        private void OnCombatStarted(CombatUIController combatUi)
        {
            if (!CropActive || combatUi == null)
            {
                return;
            }
            activeCombatUi = combatUi;
            if (combatInitializationCoroutine != null)
            {
                StopCoroutine(combatInitializationCoroutine);
            }
            /*
             * Apply immediately so the first visible combat frame uses
             * the unified UI presentation. The short coroutine below
             * repeats the layer assignment only during combat setup to
             * catch children that the game activates, reparents, or
             * creates during the transition.
             */
            SetCombatUiHierarchy(combatUi, true);
            ExcludeUiLayerFromOtherCameras();
            UpdateUiCameraTransform();
            UpdateTooltipCameraTransform();
            UpdateUiQuadLayout();
            UpdateTooltipQuadLayout();
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
            const int InitializationFrames = 12;
            for (int frame = 0;
                 frame < InitializationFrames;
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
                UpdateUiCameraTransform();
                UpdateTooltipCameraTransform();
                UpdateUiQuadLayout();
                UpdateTooltipQuadLayout();
            }
            combatInitializationCoroutine = null;
        }

        private void OnCombatEnded(CombatUIController combatUi)
        {
            if (combatInitializationCoroutine != null)
            {
                StopCoroutine(combatInitializationCoroutine);
                combatInitializationCoroutine = null;
            }
            if (combatBuffInfoLayoutCoroutine != null)
            {
                StopCoroutine(combatBuffInfoLayoutCoroutine);
                combatBuffInfoLayoutCoroutine = null;
            }
            SetCombatBuffInfoCompositePriority(false);
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

        private void RestoreBuffInfoIconHierarchy(BuffInfoMenu buffInfoMenu)
        {
            if (buffInfoMenu?.MenuList == null)
            {
                return;
            }
            /*
             * BuffInfoMenu positions its icon list from the combat health
             * bars' world positions. Keep that MenuList, its selection
             * view, and its generated icon items on their original combat
             * layers while the descriptive BuffInfo panel remains in the
             * centered menu presentation.
             */
            SetMenuListGraph(buffInfoMenu.MenuList, new HashSet<int>(), false);
            /*
             * Some scene hierarchies place the descriptive BuffInfo panel
             * beneath the MenuList root. Re-apply the UI layer afterward so
             * only the health-bar-aligned icons return to world presentation.
             */
            SetUiComponentLayer(buffInfoMenu.BuffInfo, true);
        }

        private void OnCombatBuffInfoOpened(BuffInfoMenu buffInfoMenu)
        {
            if (!CropActive || buffInfoMenu == null)
            {
                return;
            }
            /*
             * Keep the normal combat skill/item tooltip visible. Temporarily
             * draw the regular UI composite above the tooltip composite so the
             * Buff Info panel wins only where the two presentations overlap.
             */
            SetCombatBuffInfoCompositePriority(true);
            RestoreBuffInfoIconHierarchy(buffInfoMenu);
            ScheduleCombatBuffInfoLayout(buffInfoMenu);
        }

        private void OnCombatBuffInfoHovered(BuffInfoMenu buffInfoMenu)
        {
            if (!CropActive || buffInfoMenu == null || !buffInfoMenu.IsOpen)
            {
                return;
            }
            RestoreBuffInfoIconHierarchy(buffInfoMenu);
            ScheduleCombatBuffInfoLayout(buffInfoMenu);
        }

        private void OnCombatBuffInfoClosed()
        {
            if (combatBuffInfoLayoutCoroutine != null)
            {
                StopCoroutine(combatBuffInfoLayoutCoroutine);
                combatBuffInfoLayoutCoroutine = null;
            }
            SetCombatBuffInfoCompositePriority(false);
        }

        private void ApplyTooltipCompositeVisibility()
        {
            if (tooltipQuadRenderer != null)
            {
                tooltipQuadRenderer.enabled = !suppressTooltipCompositeForCombatBuffInfo;
            }
        }

        private void ScheduleCombatBuffInfoLayout(BuffInfoMenu buffInfoMenu)
        {
            if (combatBuffInfoLayoutCoroutine != null)
            {
                StopCoroutine(combatBuffInfoLayoutCoroutine);
            }
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
            float targetViewportY = BottomMarginPixels / OriginalHeight;
            if (minimumViewportY >= targetViewportY)
            {
                return;
            }
            float verticalWorldSpan = uiRenderCamera.orthographicSize * 2f;
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
                RestoreBuffInfoIconHierarchy(activeCombatUi.BuffInfoMenu);
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

        private void EnsureFullFrameEffectsUseWorldPresentation()
        {
            UIController uiController = UIController.Instance;
            if (uiController == null)
            {
                return;
            }
            if (uiController.Overlay != null)
            {
                RestoreOriginalLayersRecursively(uiController.Overlay.gameObject);
            }
            if (uiController.ShadeLayer != null)
            {
                RestoreOriginalLayersRecursively(uiController.ShadeLayer.gameObject);
            }
            if (uiController.Dialogue != null && uiController.Dialogue.NarrationBackground != null)
            {
                /*
                 * The dialogue text and controls render through the
                 * full 480x270 UI composite. The narration dimmer is
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
            int compositeMask = uiMask | tooltipMask;
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
        private static class BuffInfoOverlayOpenShadePatch
        {

            private static void Postfix(BuffInfoOverlay __instance)
            {
                Instance?.ShowBuffInfoUiShade(__instance);
            }
        }

        [HarmonyPatch(typeof(BuffInfoOverlay), "Close")]
        private static class BuffInfoOverlayCloseShadePatch
        {

            private static void Postfix()
            {
                Instance?.HideBuffInfoUiShade();
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
        private static class TitleAnimationFitPatch
        {

            private static void Postfix(TitleAnimation __instance)
            {
                Instance?.AssignTitleAnimationPresentation(__instance);
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
                    screenPos = ConvertUiMousePosition(screenPos);
                }
            }
        }
    }
}
