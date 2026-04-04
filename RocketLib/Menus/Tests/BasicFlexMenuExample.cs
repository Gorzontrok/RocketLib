using RocketLib.Menus.Core;
using RocketLib.Menus.Elements;
using RocketLib.Menus.Layout;

namespace RocketLib.Menus.Tests
{
    public class BasicFlexMenuExample : FlexMenu
    {
        public override string MenuId => "RocketLib_BasicFlexMenuExample";
        public override string MenuTitle => "BASIC FLEX MENU";

        public BasicFlexMenuExample()
        {
            EnableDebugOutput = true;
        }

        protected override void InitializeContainer()
        {
            rootContainer = new VerticalLayoutContainer
            {
                Name = "MainContainer",
                WidthMode = SizeMode.Fill,
                HeightMode = SizeMode.Fill,
                Padding = 20f,
                Spacing = 10f
            };
        }

        protected override void Start()
        {
            base.Start();

            var title = new TextElement("Title")
            {
                Name = "MenuTitle",
                Text = "FLEX MENU EXAMPLE",
                HeightMode = SizeMode.Fixed,
                Height = 60f,
                WidthMode = SizeMode.Fill,
                FontSize = 10f
            };
            rootContainer.AddChild(title);

            var contentContainer = new VerticalLayoutContainer("ContentContainer")
            {
                WidthMode = SizeMode.Fill,
                HeightMode = SizeMode.Fill,
                Spacing = 8f,
                Padding = 10f,
                ChildHorizontalAlignment = HorizontalAlignment.Center
            };
            rootContainer.AddChild(contentContainer);

            for (int i = 1; i <= 3; i++)
            {
                int index = i;
                contentContainer.AddChild(new ActionButton($"Button{i}")
                {
                    Text = $"BUTTON {i}",
                    WidthMode = SizeMode.Fixed,
                    Width = 170f,
                    HeightMode = SizeMode.Fixed,
                    Height = 30f,
                    FontSize = 5f,
                    OnClick = () => RocketMain.Logger.Log($"Button {index} clicked!")
                });
            }

            var buttonContainer = new VerticalLayoutContainer("ButtonContainer")
            {
                WidthMode = SizeMode.Fill,
                HeightMode = SizeMode.Fixed,
                Height = 40f,
                ChildHorizontalAlignment = HorizontalAlignment.Right
            };
            buttonContainer.AddChild(new ActionButton("BackButton")
            {
                Text = "BACK",
                WidthMode = SizeMode.Fixed,
                Width = 85f,
                HeightMode = SizeMode.Fixed,
                Height = 27f,
                FontSize = 4f,
                OnClick = () => GoBack()
            });
            rootContainer.AddChild(buttonContainer);

            RefreshLayout();
        }
    }
}
