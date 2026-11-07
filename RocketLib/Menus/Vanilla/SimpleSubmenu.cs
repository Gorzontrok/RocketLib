using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RocketLib.Menus.Vanilla
{
    /// <summary>
    /// A simple submenu that can be quickly created with just a list of items.
    /// Provides an easy way to create submenus without needing to create a custom class.
    /// </summary>
    public class SimpleSubmenu : BaseCustomMenu
    {
        private string menuName;
        private Dictionary<string, Action> itemActionMap;
        private MenuBarItem[] menuItems;
        private static string pendingMenuName;
        private static Dictionary<string, Action> pendingItemActionMap;

        /// <summary>
        /// Gets the unique identifier for this menu
        /// </summary>
        /// <summary>
        /// Gets the display title for this menu
        /// </summary>
        public override string MenuTitle => menuName;

        /// <summary>
        /// Static factory method to create a SimpleSubmenu with the specified items.
        /// </summary>
        /// <param name="name">Menu display name</param>
        /// <param name="items">Dictionary of item names to action callbacks. Use null for automatic back handling.</param>
        /// <returns>A configured SimpleSubmenu instance</returns>
        public static SimpleSubmenu Create(string name, Dictionary<string, Action> items)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentNullException("name");
            }
            if (items == null)
            {
                throw new ArgumentNullException("items");
            }

            GameObject menuObject = new GameObject($"SimpleSubmenu_{name}");

            pendingMenuName = name;
            pendingItemActionMap = new Dictionary<string, Action>(items);

            SimpleSubmenu menu = menuObject.AddComponent<SimpleSubmenu>();

            pendingMenuName = null;
            pendingItemActionMap = null;

            return menu;
        }

        /// <summary>
        /// Called when the component is added - initialize from pending data
        /// </summary>
        protected override void Awake()
        {
            if (pendingMenuName != null && pendingItemActionMap != null)
            {
                menuName = pendingMenuName;
                itemActionMap = pendingItemActionMap;
            }

            base.Awake();
        }

        /// <summary>
        /// Set up the menu items for this menu.
        /// Converts the dictionary items to MenuBarItems.
        /// </summary>
        protected override void SetupMenuItems()
        {
            if (itemActionMap == null)
            {
                return;
            }

            var itemList = new List<MenuBarItem>();
            int index = 0;

            foreach (var kvp in itemActionMap)
            {
                var item = new MenuBarItem
                {
                    name = kvp.Key,
                    size = GetParentFontSize(),
                    color = Color.white
                };

                item.invokeMethod = $"Action_{index}";
                itemList.Add(item);
                index++;
            }

            bool hasBackItem = false;
            foreach (var kvp in itemActionMap)
            {
                if (kvp.Key.Equals("Back", StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Equals("Return", StringComparison.OrdinalIgnoreCase))
                {
                    hasBackItem = true;
                    break;
                }
            }

            if (!hasBackItem)
            {
                var backItem = new MenuBarItem
                {
                    name = "BACK",
                    invokeMethod = "GoBack",
                    size = GetParentFontSize(),
                    color = Color.white
                };
                itemList.Add(backItem);
            }

            menuItems = itemList.ToArray();
            masterItems = menuItems;
        }

        /// <summary>
        /// Get the appropriate font size based on parent menu
        /// </summary>
        private float GetParentFontSize()
        {
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
            return itemSize;
        }

        /// <summary>
        /// Override RunInput to handle actions directly without SendMessage
        /// </summary>
        protected override void RunInput()
        {
            if (up || down || left || right)
            {
                if (Time.time - lastInputTime < upDownDelay)
                {
                    acceptPrev = true;
                    return;
                }
                lastInputTime = Time.time;
            }
            if (accept || decline)
            {
                if (Time.time - lastInputTime < 0.05f)
                {
                    acceptPrev = true;
                    return;
                }
                lastInputTime = Time.time;
            }
            if (accept && acceptPrev)
            {
                return;
            }
            if (up)
            {
                do
                {
                    highlightIndex = (highlightIndex - 1 + items.Length) % items.Length;
                } while (!itemEnabled[highlightIndex]);
                Sound instance = Sound.GetInstance();
                if (instance != null)
                {
                    instance.PlaySoundEffect(drumSounds.attackSounds[0], 0.25f);
                }
                if (items != null && items[highlightIndex] != null)
                {
                    Animation anim = items[highlightIndex].GetComponent<Animation>();
                    if (anim != null)
                    {
                        anim.Play();
                    }
                }
            }
            if (down)
            {
                do
                {
                    highlightIndex = (highlightIndex + 1) % items.Length;
                } while (!itemEnabled[highlightIndex]);
                Sound instance2 = Sound.GetInstance();
                if (instance2 != null)
                {
                    instance2.PlaySoundEffect(drumSounds.attackSounds[0], 0.25f);
                }
                if (items != null && items[highlightIndex] != null)
                {
                    Animation anim = items[highlightIndex].GetComponent<Animation>();
                    if (anim != null)
                    {
                        anim.Play();
                    }
                }
            }
            if (!activatedThisFrame && accept && !acceptPrev && masterItems[highlightIndex].invokeMethod != string.Empty)
            {
                string methodName = masterItems[highlightIndex].invokeMethod;

                if (methodName == "GoBack" || methodName == "OnMenuClosed")
                {
                    OnMenuClosed();
                    PlayDrumSound(1);
                }
                else if (methodName.StartsWith("Action_"))
                {
                    string indexStr = methodName.Substring(7);
                    int actionIndex;
                    if (int.TryParse(indexStr, out actionIndex))
                    {
                        int currentIndex = 0;
                        foreach (var kvp in itemActionMap)
                        {
                            if (currentIndex == actionIndex)
                            {
                                if (kvp.Value != null)
                                {
                                    try
                                    {
                                        kvp.Value.Invoke();
                                        PlayDrumSound(1);
                                    }
                                    catch (Exception ex)
                                    {
                                        RocketMain.Logger.Error($"[SimpleSubmenu] Error invoking action for '{kvp.Key}': {ex.Message}");
                                    }
                                }
                                else if (kvp.Key.Equals("Back", StringComparison.OrdinalIgnoreCase) ||
                                         kvp.Key.Equals("Return", StringComparison.OrdinalIgnoreCase))
                                {
                                    OnMenuClosed();
                                    PlayDrumSound(1);
                                }
                                break;
                            }
                            currentIndex++;
                        }
                    }
                }
            }
            if (!activatedThisFrame && (left || right) && masterItems[highlightIndex].invokeMethod != string.Empty && masterItems[highlightIndex].invokeMethod.Contains("Toggle"))
            {
                var traverse = Traverse.Create(this);
                float toggleTimer = traverse.Field<float>("toggleTimer").Value;

                if (Time.time - toggleTimer > toggleDelay)
                {
                    string methodName = masterItems[highlightIndex].invokeMethod;
                    PlayDrumSound(1);
                    traverse.SetFieldValue("toggleTimer", Time.time);
                }
            }
            if (MenuActive && !activatedThisFrame)
            {
                int controllerPressingCancel = InputReader.GetControllerPressingCancel();
                bool flag = controllerPressingCancel > -1 && (controlledByControllerID == -1 || controllerPressingCancel == controlledByControllerID);
                if (Input.GetKeyDown(KeyCode.Escape) || flag)
                {
                    PlayDrumSound(1);
                    OnMenuClosed();
                }
            }
        }
    }
}
