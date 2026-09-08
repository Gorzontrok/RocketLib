using System;
using System.Collections.Generic;
using HarmonyLib;
using RocketLib.Menus.Utilities;
using UnityEngine;

namespace RocketLib.Menus.Vanilla
{
    /// <summary>
    /// Abstract base class for all custom menus in RocketLib.
    /// Provides core functionality for menu creation, item management, and scene persistence.
    /// </summary>
    public abstract class BaseCustomMenu : Menu
    {
        /// <summary>
        /// Display title for this menu
        /// </summary>
        public abstract string MenuTitle { get; }

        /// <summary>
        /// Instance identifier (set by MenuRegistry for caching)
        /// </summary>
        internal string InstanceId { get; set; }

        /// <summary>
        /// Vertical spacing between menu items
        /// </summary>
        protected virtual float ItemSpacing => 26f;

        /// <summary>
        /// Compressed vertical spacing (used when moveHighlight is false)
        /// </summary>
        protected virtual float ItemSpacingCompressed => 13.5f;

        /// <summary>
        /// Initial vertical offset for menu items
        /// </summary>
        protected virtual float InitialVerticalOffset => 107f;

        /// <summary>
        /// Initialize the menu with references from parent menu
        /// </summary>
        /// <param name="parent">The parent menu to copy references from</param>
        public virtual void Initialize(Menu parent)
        {
            PrevMenu = parent;

            DontDestroyOnLoad(this);
        }

        /// <summary>
        /// Set up the menu items for this menu.
        /// Override this to define your menu's items.
        /// </summary>
        protected abstract void SetupMenuItems();

        /// <summary>
        /// Unity Awake - set up the menu when GameObject becomes active
        /// </summary>
        protected override void Awake()
        {
            if (PrevMenu == null)
            {
                PrevMenu = FindParentMenuInScene();
            }

            if (PrevMenu != null)
            {
                CopyReferencesFromParent();
            }

            SetupMenuItems();

            desktopItems = masterItems;

            base.Awake();
        }

        private void CopyReferencesFromParent()
        {
            if (PrevMenu.textPrefab != null)
            {
                textPrefab = PrevMenu.textPrefab;
            }

            if (PrevMenu.drumSounds != null)
            {
                drumSounds = PrevMenu.drumSounds;
            }

            // Create our own highlight instead of sharing with parent
            try
            {
                CreateCustomHighlight();
            }
            catch (Exception ex)
            {
                RocketMain.Logger.Log("error: " + ex.ToString());
            }


            // Inherit spacing values from parent menu instead of using hardcoded values
            verticalSpacing = PrevMenu.verticalSpacing;
            verticalSpacingCompressed = PrevMenu.verticalSpacingCompressed;
            initialVerticalOffset = PrevMenu.initialVerticalOffset;

            fadeItems = true;
            fadeDistance = 0;
            hideDistance = -1;
            moveHighlight = false;

            // Inherit font sizing from parent menu
            Traverse parentTraverse = Traverse.Create(PrevMenu);
            overrideIndivdualItemCharacterSizes = parentTraverse.Field<bool>("overrideIndivdualItemCharacterSizes").Value;
            characterSizes = parentTraverse.Field<float>("characterSizes").Value;
            lineSpacing = parentTraverse.Field<float>("lineSpacing").Value;
            deselectedTextScale = parentTraverse.Field<float>("deselectedTextScale").Value;
        }

        private void CreateCustomHighlight()
        {
            if (menuHighlight == null)
            {
                menuHighlight = HighlightFactory.CreateHighlight(transform, 17);
            }
        }

        /// <summary>
        /// Called when the menu is opened
        /// </summary>
        public virtual void OnMenuOpened()
        {
            // Ensure highlight is visible when menu opens
            if (menuHighlight != null && menuHighlight.gameObject != null)
            {
                menuHighlight.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// Called when the menu is closed
        /// </summary>
        public virtual void OnMenuClosed()
        {
            if (PrevMenu != null)
            {
                MenuActive = false;
                PrevMenu.MenuActive = true;
                PrevMenu.TransitionIn();
            }
            else
            {
                MenuActive = false;
            }
        }

        /// <summary>
        /// Find the parent menu in the current scene
        /// </summary>
        /// <returns>The parent menu if found, null otherwise</returns>
        protected virtual Menu FindParentMenuInScene()
        {
            var mainMenu = FindObjectOfType<MainMenu>();
            if (mainMenu != null)
            {
                return mainMenu;
            }

            var pauseMenu = FindObjectOfType<PauseMenu>();
            if (pauseMenu != null)
            {
                return pauseMenu;
            }

            var optionsMenu = FindObjectOfType<OptionsMenu>();
            if (optionsMenu != null)
            {
                return optionsMenu;
            }

            return null;
        }

        /// <summary>
        /// Add a menu item to this menu
        /// </summary>
        /// <param name="displayText">Display text for the item</param>
        /// <param name="methodName">Method to invoke when selected</param>
        /// <param name="isToggle">Whether this item accepts left/right input</param>
        protected void AddMenuItem(string displayText, string methodName, bool isToggle = false)
        {
            // Use parent menu's font size if available, otherwise use appropriate default
            float itemSize = 3f; // Default MenuBarItem size
            if (PrevMenu != null)
            {
                // Try to get size from parent's first item
                Traverse parentTraverse = Traverse.Create(PrevMenu);
                var parentItems = parentTraverse.Field<MenuBarItem[]>("masterItems").Value;
                if (parentItems != null && parentItems.Length > 0)
                {
                    itemSize = parentItems[0].size;
                }
            }
            else if (PrevMenu is MainMenu)
            {
                itemSize = 6f; // MainMenu typically uses larger size
            }

            var item = new MenuBarItem
            {
                name = displayText,
                invokeMethod = methodName,
                size = itemSize,
                color = Color.white
            };

            if (isToggle)
            {
                item.invokeMethod = $"Toggle{methodName}";
            }

            if (masterItems == null)
            {
                masterItems = new MenuBarItem[] { item };
            }
            else
            {
                var list = new List<MenuBarItem>(masterItems);
                list.Add(item);
                masterItems = list.ToArray();
            }
        }

        /// <summary>
        /// Update the visual representation of menu items
        /// </summary>
        protected virtual void UpdateMenuItemVisuals()
        {
        }

        /// <summary>
        /// Override Update to handle Escape key like OptionsMenu does
        /// </summary>
        protected override void Update()
        {
            base.Update();

            if (menuActive && !activatedThisFrame &&
                (Input.GetKeyDown(KeyCode.Escape) || InputReader.GetControllerPressingCancel() > -1))
            {
                OnMenuClosed();
            }
        }

        /// <summary>
        /// Clean up when menu is destroyed
        /// </summary>
        protected override void OnDestroy()
        {
            PrevMenu = null;

            if (!string.IsNullOrEmpty(InstanceId))
            {
                Core.MenuInstanceCache.Remove(InstanceId);
            }
        }
    }
}
