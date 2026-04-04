using RocketLib.Menus.Core;
using RocketLib.Menus.Elements;
using RocketLib.Menus.Layout;

namespace RocketLib.Menus.Tests
{
    public class GridLayoutExample : FlexMenu
    {
        public override string MenuTitle => "GRID LAYOUT EXAMPLE";

        public GridLayoutExample()
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
                Padding = 10f,
                Spacing = 10f
            };
        }

        protected override void Start()
        {
            base.Start();

            var title = new TextElement("Title")
            {
                Name = "GridTitle",
                Text = "SELECT AN OPTION",
                HeightMode = SizeMode.Fixed,
                Height = 60f,
                WidthMode = SizeMode.Fill,
                FontSize = 9f
            };
            rootContainer.AddChild(title);

            var gridContainer = new GridLayoutContainer("GridContainer")
            {
                WidthMode = SizeMode.Fill,
                HeightMode = SizeMode.Fill,
                Columns = 3,
                ColumnSpacing = 10f,
                RowSpacing = 10f,
                Padding = 10f,
                CellHorizontalAlignment = HorizontalAlignment.Center,
                CellVerticalAlignment = VerticalAlignment.Center
            };
            rootContainer.AddChild(gridContainer);

            for (int i = 1; i <= 9; i++)
            {
                int index = i;
                gridContainer.AddChild(new ActionButton($"GridButton{i}")
                {
                    Name = $"Option{i}",
                    Text = $"OPTION {i}",
                    WidthMode = SizeMode.Fill,
                    HeightMode = SizeMode.Fill,
                    FontSize = 5f,
                    OnClick = () => RocketMain.Logger.Log($"Grid option {index} selected!")
                });
            }

            var buttonContainer = new VerticalLayoutContainer("ButtonContainer")
            {
                WidthMode = SizeMode.Fill,
                HeightMode = SizeMode.Fixed,
                Height = 40f,
                ChildHorizontalAlignment = HorizontalAlignment.Center
            };
            buttonContainer.AddChild(new ActionButton("BackButton")
            {
                Text = "BACK",
                WidthMode = SizeMode.Fixed,
                Width = 85f,
                HeightMode = SizeMode.Fixed,
                Height = 27f,
                FontSize = 5f,
                OnClick = () => GoBack()
            });
            rootContainer.AddChild(buttonContainer);

            RefreshLayout();
        }
    }
}
