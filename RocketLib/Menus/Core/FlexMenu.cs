using System.Collections.Generic;
using HarmonyLib;
using RocketLib.Menus.Elements;
using RocketLib.Menus.Layout;
using RocketLib.Menus.Utilities;
using RocketLib.Menus.Vanilla;
using UnityEngine;

namespace RocketLib.Menus.Core
{
    public enum FocusRestoreMode
    {
        Default,            // Always return to default/first element
        LastSubmenuTrigger, // Return to element that opened a submenu
        LastFocused         // Return to whatever was last focused
    }

    public class FlexMenu : MonoBehaviour
    {
        protected LayoutContainer rootContainer;
        protected NavigationManager navigationManager;
        protected GridMenuHighlight highlight;

        protected FlexMenu parentFlexMenu;
        protected Menu parentGameMenu;

        private static readonly Dictionary<string, FlexMenu> instances = new Dictionary<string, FlexMenu>();
        public static FlexMenu activeMenu;

        // Menu return override system (replicates GameState.immediatelyGoToCustomCampaign pattern)
        private static bool returnToFlexMenuOverride = false;
        private static System.Type returnTargetMenuType = null;
        private static string returnTargetMenuInstanceId = null;

        public string InstanceId { get; protected set; } = "default";

        // Transition settings (defaults match Lobby Canvas)
        public bool EnableTransition { get; set; } = false;
        public bool EnableDebugOutput { get; set; } = false;
        public AnimationCurve TransitionCurve { get; set; }
        public float TransitionSpeed { get; set; } = 3f;
        public float TransitionDelay { get; set; } = 0.5f;

        // Shake settings (defaults match Lobby Canvas)
        public float ShakeDamping { get; set; } = 15f;
        public float ShakeFreqDamping { get; set; } = 15f;
        public float ShakeAmplitude { get; set; } = 8f;
        public float ShakeFreqX { get; set; } = 200f;
        public float ShakeFreqY { get; set; } = 135f;

        private Coroutine transitionCoroutine;
        private Shake shakeComponent;
        private bool skipNextTransition = false;

        private System.Collections.IEnumerator TransitionInRoutine()
        {
            if (parentGameMenu != null && parentGameMenu.name == "MainMenu" && !hasStoredMainMenuState)
            {
                var mainMenu = GetMainMenu() as MainMenu;
                if (mainMenu != null)
                {
                    if (mainMenu.logo != null)
                    {
                        wasMainMenuLogoVisible = mainMenu.logo.gameObject.activeSelf;
                    }

                    var versionDisplay = GameObject.FindObjectOfType<DisplayVersionNumber>();
                    if (versionDisplay != null)
                    {
                        versionDisplayObject = versionDisplay.gameObject;
                        wasVersionTextVisible = versionDisplayObject.activeSelf;
                    }

                    hasStoredMainMenuState = true;
                }
            }

            // Set initial scale to zero (matches ZoomIn behavior)
            transform.localScale = Vector3.zero;

            // Disable ZoomInCamera animation if present - it interferes with FlexMenu transitions
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                var zoomInCamera = mainCam.GetComponent("ZoomInCamera");
                if (zoomInCamera != null)
                {
                    var enabledProp = zoomInCamera.GetType().GetProperty("enabled");
                    if (enabledProp != null)
                    {
                        enabledProp.SetValue(zoomInCamera, false, null);
                        mainCam.orthographicSize = 160f;
                    }
                }
            }

            // Get or add Shake component
            if (shakeComponent == null)
            {
                shakeComponent = GetComponent<Shake>();
                if (shakeComponent == null)
                {
                    shakeComponent = gameObject.AddComponent<Shake>();
                    shakeComponent.setToZeroOnAwake = true;
                    shakeComponent.applyDampimg = true;
                }
            }

            // Apply configurable shake settings
            shakeComponent.damping = ShakeDamping;
            shakeComponent.freqDamping = ShakeFreqDamping;

            RefreshLayout();
            RestoreFocus();

            // Set highlight position immediately so it's on the correct item from the start
            if (highlight != null && navigationManager?.FocusedElement != null)
            {
                var bounds = navigationManager.FocusedElement.GetBounds();
                highlight.SetBoundsImmediate(bounds);
            }

            if (MainMenu.instance != null && MainMenu.instance.starFieldAnimation != null)
            {
                MainMenu.instance.starFieldAnimation.Play("starfieldLobbyTransition");
            }

            if (parentGameMenu != null && parentGameMenu.name == "MainMenu" && parentGameMenu.gameObject.activeSelf)
            {
                var mainMenu = parentGameMenu as MainMenu;
                if (mainMenu != null)
                {
                    var enumerator = Traverse.Create(mainMenu).Method("HideRoutine", true).GetValue<System.Collections.IEnumerator>();
                    if (enumerator != null)
                    {
                        Traverse.Create(mainMenu).Method("ShowHideMenu", enumerator).GetValue();
                    }
                }
            }

            yield return new WaitForSeconds(TransitionDelay);

            // Scale from 0 to 1 with animation curve
            float lerp = 0f;
            while (lerp < 1f)
            {
                lerp += Time.deltaTime * TransitionSpeed;
                lerp = Mathf.Clamp01(lerp);

                // Use the animation curve if provided, otherwise linear
                float curveValue = TransitionCurve != null ? TransitionCurve.Evaluate(lerp) : lerp;
                transform.localScale = Vector3.one * curveValue;

                yield return null;
            }

            // Play impact sound and add shake
            MainMenu.PlayImpactSound();
            shakeComponent.AddShake(ShakeAmplitude, ShakeFreqX, ShakeFreqY);
        }

        protected bool wasMainMenuLogoVisible;
        protected bool wasVersionTextVisible;
        protected bool hasStoredMainMenuState;
        protected GameObject versionDisplayObject;

        protected float lastInputTime;
        protected bool wasAcceptPressed = false;  // Track if accept was pressed last frame
        protected bool wasDeclinePressed = false; // Track if decline was pressed last frame

        // Focus restoration
        public FocusRestoreMode FocusRestoreMode { get; set; } = FocusRestoreMode.LastSubmenuTrigger;
        protected LayoutElement savedFocusElement;
        protected LayoutElement submenuTriggerElement;

        public LayoutContainer RootContainer
        {
            get { return rootContainer; }
        }

        public NavigationManager Navigation
        {
            get { return navigationManager; }
        }

        public GridMenuHighlight Highlight
        {
            get { return highlight; }
        }


        public virtual string MenuId { get; set; }
        public virtual string MenuTitle { get; set; }

        public bool IsActive { get; protected set; }

        protected virtual void Awake()
        {
            MenuId = GetType().Name;
            MenuTitle = "Flex Menu";

            InitializeContainer();
            navigationManager = new NavigationManager(rootContainer);
        }

        protected virtual void InitializeContainer()
        {
            // Default to vertical layout container
            rootContainer = new VerticalLayoutContainer
            {
                Name = "RootContainer",
                WidthMode = Layout.SizeMode.Fill,
                HeightMode = Layout.SizeMode.Fill,
            };
        }

        protected virtual void CreateHighlight()
        {
            var highlightObj = HighlightFactory.CreateGridHighlight(transform, gameObject.layer);
            if (highlightObj != null)
            {
                highlight = highlightObj.GetComponent<GridMenuHighlight>();
                if (highlight != null)
                {
                    highlight.transform.SetParent(transform, false);
                    highlight.BorderThickness = 3f;
                    highlight.BorderPadding = 2f;

                    // Set initial position to first focused element if available
                    if (navigationManager != null && navigationManager.FocusedElement != null)
                    {
                        var bounds = navigationManager.FocusedElement.GetBounds();
                        highlight.SetBoundsImmediate(bounds);
                    }
                }
            }
        }

        protected virtual void Start()
        {
            if (!(EnableTransition && parentGameMenu != null && parentGameMenu.name == "MainMenu"))
            {
                HideMainMenuElements();
            }

            RefreshLayout();
            navigationManager?.FocusFirst();

            // Create highlight after layout and first focus
            CreateHighlight();
        }

        protected virtual void OnEnable()
        {
            IsActive = true;
            lastInputTime = Time.time + 0.2f;  // Add 200ms input delay to prevent click-through
            wasAcceptPressed = false;  // Reset input state
            wasDeclinePressed = false;

            if (!(EnableTransition && parentGameMenu != null && parentGameMenu.name == "MainMenu"))
            {
                HideMainMenuElements();
            }

            // If transitions are enabled, start the zoom-in animation
            if (EnableTransition && !skipNextTransition)
            {
                if (transitionCoroutine != null)
                {
                    StopCoroutine(transitionCoroutine);
                }
                transitionCoroutine = StartCoroutine(TransitionInRoutine());
            }
            else
            {
                // Normal immediate activation (no transitions)
                // Deactivate parent game menu if present
                if (parentGameMenu != null)
                {
                    parentGameMenu.gameObject.SetActive(false);
                }

                RefreshLayout();
                RestoreFocus();

                // Set highlight position immediately to avoid animation
                if (highlight != null && navigationManager?.FocusedElement != null)
                {
                    var bounds = navigationManager.FocusedElement.GetBounds();
                    highlight.SetBoundsImmediate(bounds);
                }
            }

            skipNextTransition = false;
        }

        protected virtual void OnDisable()
        {
            IsActive = false;

            // Save the current focused element for potential restoration
            if (navigationManager != null)
            {
                savedFocusElement = navigationManager.FocusedElement;
            }

            // Only restore main menu elements when going back (activeMenu changed)
            // During forward transitions, activeMenu still equals this
            if (activeMenu != this)
            {
                RestoreMainMenuElements();
            }
        }

        protected virtual void OnDestroy()
        {
            RestoreMainMenuElements();
            navigationManager?.Clear();

            string key = $"{GetType().Name}:{InstanceId}";
            instances.Remove(key);
            if (activeMenu == this)
            {
                activeMenu = null;
            }
        }

        public virtual void RefreshLayout()
        {
            if (rootContainer != null)
            {
                // Pass our transform to the root container
                rootContainer.SetMenuTransform(transform);
                rootContainer.UpdateLayout();
                navigationManager?.RefreshFocusableElements();
            }
        }

        protected virtual void Update()
        {
            if (!IsActive) return;

            HandleInput();
            UpdateHighlight();
        }

        protected virtual void OnGUI()
        {
            // Check both IsActive and GameObject active state
            if (!IsActive || !gameObject.activeSelf)
            {
                return;
            }

            if (rootContainer != null)
            {
                rootContainer.Render();
            }
        }

        protected virtual void HandleInput()
        {
            // Use Broforce's InputReader for exact menu behavior
            bool up = false, down = false, left = false, right = false;
            bool accept = false, decline = false;

            // Try keyboard first, then combined input (matches Broforce's Menu.CheckInput)
            if (!InputReader.GetMenuInputStandardKeys(ref up, ref down, ref left, ref right, ref accept, ref decline))
            {
                InputReader.GetMenuInputCombined(ref up, ref down, ref left, ref right, ref accept, ref decline, 0);
            }

            // Prevent any input during initial cooldown period
            if (Time.time < lastInputTime)
            {
                return;  // Still in cooldown after menu activation
            }

            // Check timing like Broforce does (200ms for directional, 50ms for accept/decline)
            if (up || down || left || right)
            {
                if (Time.time - lastInputTime < 0.2f)  // upDownDelay
                {
                    return; // Too soon, ignore input
                }
                lastInputTime = Time.time;
            }

            if (accept || decline)
            {
                if (Time.time - lastInputTime < 0.05f)
                {
                    return; // Too soon, ignore input
                }
                lastInputTime = Time.time;
            }

            // Track button press/release state for toggles
            bool acceptJustPressed = accept && !wasAcceptPressed;
            bool declineJustPressed = decline && !wasDeclinePressed;

            // Process navigation - only process activate on first press
            if (up || down || left || right || acceptJustPressed)
            {
                navigationManager?.ProcessInput(
                    left ? -1f : (right ? 1f : 0f),
                    up ? 1f : (down ? -1f : 0f),
                    acceptJustPressed,  // Only activate on initial press, not hold
                    declineJustPressed
                );
            }

            if (declineJustPressed)
            {
                NavigationManager.PlayActionSound();
                GoBack();
            }

            // Update state for next frame
            wasAcceptPressed = accept;
            wasDeclinePressed = decline;
        }

        protected virtual void RestoreFocus()
        {
            if (navigationManager == null) return;

            LayoutElement elementToFocus = null;

            switch (FocusRestoreMode)
            {
                case FocusRestoreMode.LastSubmenuTrigger:
                    // Try to restore to the element that opened a submenu
                    if (submenuTriggerElement != null && navigationManager.IsElementFocusable(submenuTriggerElement))
                    {
                        elementToFocus = submenuTriggerElement;
                    }
                    break;

                case FocusRestoreMode.LastFocused:
                    // Try to restore to the last focused element
                    if (savedFocusElement != null && navigationManager.IsElementFocusable(savedFocusElement))
                    {
                        elementToFocus = savedFocusElement;
                    }
                    break;

                case FocusRestoreMode.Default:
                    // Always use default (first element)
                    break;
            }

            // Focus the chosen element or fall back to first
            if (elementToFocus != null)
            {
                navigationManager.FocusElement(elementToFocus);
            }
            else
            {
                navigationManager.FocusFirst();
            }
        }

        protected virtual void UpdateHighlight()
        {
            if (highlight == null || navigationManager == null) return;

            var focused = navigationManager.FocusedElement;
            if (focused != null)
            {
                var bounds = focused.GetBounds();
                highlight.SetTargetBounds(bounds);

                if (!highlight.gameObject.activeSelf)
                {
                    highlight.gameObject.SetActive(true);
                }
            }
            else if (highlight.gameObject.activeSelf)
            {
                highlight.gameObject.SetActive(false);
            }
        }

        public virtual void GoBack()
        {

            RestoreMainMenuElements();

            if (parentFlexMenu != null)
            {
                Show(parentFlexMenu.GetType(),
                     parentFlex: parentFlexMenu.parentFlexMenu,
                     parentGame: parentFlexMenu.parentGameMenu,
                     instanceId: parentFlexMenu.InstanceId,
                     skipTransition: true);
                gameObject.SetActive(false);
            }
            else if (parentGameMenu != null)
            {
                if (EnableTransition && parentGameMenu.name == "MainMenu")
                {
                    var mainMenu = parentGameMenu as MainMenu;
                    if (mainMenu != null && MainMenu.instance != null)
                    {
                        mainMenu.Show();
                    }
                }
                else
                {
                    parentGameMenu.gameObject.SetActive(true);
                }

                activeMenu = null;
                gameObject.SetActive(false);
            }
            else
            {
                var allMenus = FindObjectsOfType<Menu>();
                Menu foundMainMenu = null;
                foreach (var menu in allMenus)
                {
                    if (menu.name == "MainMenu")
                    {
                        foundMainMenu = menu;
                        break;
                    }
                }

                if (foundMainMenu != null)
                {
                    if (EnableTransition)
                    {
                        var mainMenu = foundMainMenu as MainMenu;
                        if (mainMenu != null && MainMenu.instance != null)
                        {
                            mainMenu.Show();
                        }
                    }
                    else
                    {
                        foundMainMenu.gameObject.SetActive(true);
                    }
                }

                activeMenu = null;
                gameObject.SetActive(false);
            }
        }

        protected virtual void HideMainMenuElements()
        {
            // Only store the state once, the first time we hide
            if (!hasStoredMainMenuState)
            {
                var mainMenu = GetMainMenu() as MainMenu;
                if (mainMenu == null)
                {
                    return;
                }

                // Store the original state of the logo
                if (mainMenu.logo != null)
                {
                    wasMainMenuLogoVisible = mainMenu.logo.gameObject.activeSelf;
                }

                // Store the original state of the version display
                var versionDisplay = GameObject.FindObjectOfType<DisplayVersionNumber>();
                if (versionDisplay != null)
                {
                    versionDisplayObject = versionDisplay.gameObject;  // Store reference
                    wasVersionTextVisible = versionDisplayObject.activeSelf;
                }

                hasStoredMainMenuState = true;
            }

            // Always hide the elements (even if we already stored the state)
            var menu = GetMainMenu() as MainMenu;
            if (menu != null && menu.logo != null)
            {
                menu.logo.gameObject.SetActive(false);
            }

            var version = GameObject.FindObjectOfType<DisplayVersionNumber>();
            if (version != null)
            {
                version.gameObject.SetActive(false);
            }
        }

        protected virtual void RestoreMainMenuElements()
        {
            var mainMenu = GetMainMenu() as MainMenu;
            if (mainMenu == null)
            {
                return;
            }

            // Restore the logo GameObject directly (like ComplexMenu does)
            if (mainMenu.logo != null)
            {
                mainMenu.logo.gameObject.SetActive(wasMainMenuLogoVisible);
            }

            // Restore the version number display using stored reference
            if (versionDisplayObject != null)
            {
                versionDisplayObject.SetActive(wasVersionTextVisible);
            }
            else
            {
                // Fallback to finding it if we don't have the reference
                var versionDisplay = GameObject.FindObjectOfType<DisplayVersionNumber>();
                if (versionDisplay != null)
                {
                    versionDisplay.gameObject.SetActive(wasVersionTextVisible);
                }
            }
        }

        protected Menu GetMainMenu()
        {
            Menu current = parentGameMenu;
            while (current != null)
            {
                if (current.name == "MainMenu")
                {
                    return current;
                }

                var customMenu = current as BaseCustomMenu;
                if (customMenu != null)
                {
                    var parentField = typeof(BaseCustomMenu).GetField("parentMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (parentField != null)
                    {
                        current = parentField.GetValue(customMenu) as Menu;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            var allMenus = FindObjectsOfType<Menu>();
            foreach (var menu in allMenus)
            {
                if (menu.name == "MainMenu")
                {
                    return menu;
                }
            }

            return null;
        }

        public static void SetReturnTarget(FlexMenu target)
        {
            if (target == null)
            {
                ClearReturnTarget();
                return;
            }

            returnToFlexMenuOverride = true;
            returnTargetMenuType = target.GetType();
            returnTargetMenuInstanceId = target.InstanceId;
        }

        public static void ClearReturnTarget()
        {
            returnToFlexMenuOverride = false;
            returnTargetMenuType = null;
            returnTargetMenuInstanceId = null;
        }

        public static bool HasReturnTargetOverride()
        {
            return returnToFlexMenuOverride && returnTargetMenuType != null;
        }

        public static FlexMenu GetReturnTarget()
        {
            if (!HasReturnTargetOverride())
            {
                return null;
            }

            string key = $"{returnTargetMenuType.Name}:{returnTargetMenuInstanceId}";

            if (instances.TryGetValue(key, out FlexMenu existing))
            {
                return existing;
            }

            return Show(returnTargetMenuType, null, MainMenu.instance, returnTargetMenuInstanceId);
        }

        public static FlexMenu Show(System.Type menuType, FlexMenu parentFlex = null, Menu parentGame = null, string instanceId = "default", bool skipTransition = false)
        {
            if (!typeof(FlexMenu).IsAssignableFrom(menuType))
            {
                RocketMain.Logger.Error($"[FlexMenu.Show] Type {menuType.Name} does not inherit from FlexMenu");
                return null;
            }

            string key = $"{menuType.Name}:{instanceId}";

            if (instances.TryGetValue(key, out FlexMenu existing))
            {
                existing.parentFlexMenu = parentFlex;
                existing.parentGameMenu = parentGame;

                if (parentFlex != null)
                {
                    if (parentFlex.navigationManager != null)
                    {
                        parentFlex.submenuTriggerElement = parentFlex.navigationManager.FocusedElement;
                    }
                    parentFlex.gameObject.SetActive(false);
                }
                else if (parentGame != null)
                {
                    if (!(existing.EnableTransition && parentGame.name == "MainMenu"))
                    {
                        parentGame.gameObject.SetActive(false);
                    }
                }

                if (activeMenu != null && activeMenu != existing)
                {
                    activeMenu.gameObject.SetActive(false);
                }

                existing.skipNextTransition = skipTransition;
                existing.gameObject.SetActive(true);
                activeMenu = existing;

                return existing;
            }

            string goName = instanceId == "default" ? menuType.Name : $"{menuType.Name}_{instanceId}";
            var go = new GameObject(goName);
            go.SetActive(false);
            var newMenu = go.AddComponent(menuType) as FlexMenu;

            newMenu.InstanceId = instanceId;
            instances[key] = newMenu;
            newMenu.parentFlexMenu = parentFlex;
            newMenu.parentGameMenu = parentGame;

            if (parentFlex != null)
            {
                if (parentFlex.navigationManager != null)
                {
                    parentFlex.submenuTriggerElement = parentFlex.navigationManager.FocusedElement;
                }
                parentFlex.gameObject.SetActive(false);
            }

            if (activeMenu != null)
            {
                activeMenu.gameObject.SetActive(false);
            }

            newMenu.skipNextTransition = skipTransition;
            newMenu.gameObject.SetActive(true);
            activeMenu = newMenu;

            return newMenu;
        }

        public static T Show<T>(FlexMenu parentFlex = null, Menu parentGame = null, string instanceId = "default", bool skipTransition = false) where T : FlexMenu
        {
            return Show(typeof(T), parentFlex, parentGame, instanceId, skipTransition) as T;
        }
    }
}
