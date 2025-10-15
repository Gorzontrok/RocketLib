using RocketLib.Menus.Vanilla;
using UnityEngine;

namespace RocketLib.Menus.Tests
{
    public class VanillaSubmenuExample : BaseCustomMenu
    {
        public override string MenuTitle => "VANILLA SUBMENU";

        protected override void SetupMenuItems()
        {
            AddMenuItem("OPTION 1", "SelectOption1");
            AddMenuItem("OPTION 2", "SelectOption2");
            AddMenuItem("OPTION 3", "SelectOption3");
            AddMenuItem("BACK", "GoBackToParent");
        }

        private void SelectOption1()
        {
            RocketMain.Logger.Log("Option 1 selected!");
        }

        private void SelectOption2()
        {
            RocketMain.Logger.Log("Option 2 selected!");
        }

        private void SelectOption3()
        {
            RocketMain.Logger.Log("Option 3 selected!");
        }

        private void GoBackToParent()
        {
            OnMenuClosed();
        }
    }
}