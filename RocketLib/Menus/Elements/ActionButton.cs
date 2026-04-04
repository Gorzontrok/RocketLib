using System;
using RocketLib.Menus.Utilities;
using UnityEngine;

namespace RocketLib.Menus.Elements
{
    public class ActionButton : LayoutElement
    {
        private string _text = "Button";
        public string Text
        {
            get { return _text; }
            set
            {
                if (_text != value)
                {
                    _text = value;
                    boundsAvailable = false;
                }
            }
        }

        private float _fontSize = 3f;
        public float FontSize
        {
            get { return _fontSize; }
            set
            {
                if (_fontSize != value)
                {
                    _fontSize = value;
                    boundsAvailable = false;
                    fontNeedsUpdate = true;
                }
            }
        }
        private bool fontNeedsUpdate = false;
        public Action OnClick { get; set; }

        private TextMesh textMesh;

        // Auto-width caching
        private bool boundsAvailable = false;
        private float cachedAutoWidth = 200f;
        private readonly float buttonPadding = 40f; // Extra padding for button text

        public ActionButton(string name) : base(name)
        {
            _text = "Button";
            IsFocusable = true;

            // Default size for buttons
            WidthMode = Layout.SizeMode.Fixed;
            HeightMode = Layout.SizeMode.Fixed;
            Width = 200f;
            Height = 40f;
        }

        public override void Render()
        {
            // Create GameObject if needed (even if not visible, so we can hide it)
            if (gameObject == null)
            {
                CreateButtonGameObject();
            }

            if (gameObject != null)
            {
                if (!IsVisible)
                {
                    // Hide the GameObject when not visible
                    gameObject.SetActive(false);
                    return;
                }

                // Update position and size based on ActualPosition/ActualSize set by parent
                gameObject.transform.localPosition = new Vector3(ActualPosition.x, ActualPosition.y, -1f);
                gameObject.transform.localScale = Vector3.one;  // No scaling - use direct character size

                if (textMesh != null)
                {
                    textMesh.text = Text.ToUpper();
                    textMesh.color = Color.white;

                    if (fontNeedsUpdate)
                    {
                        FontManager.ApplyFont(textMesh, BroforceFont.Hudson, FontSize);
                        fontNeedsUpdate = false;
                    }

                    if (WidthMode == Layout.SizeMode.Auto && !boundsAvailable)
                    {
                        MeasureAutoWidth();
                    }
                }

                gameObject.SetActive(true);
            }
        }

        private void CreateButtonGameObject()
        {
            gameObject = new GameObject(Name);
            gameObject.layer = LayerMask.NameToLayer("UI");

            if (menuTransform != null)
            {
                gameObject.transform.SetParent(menuTransform, false);
            }

            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            textMesh = gameObject.AddComponent<TextMesh>();

            textMesh.text = Text.ToUpper();
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = Color.white;

            FontManager.ApplyFont(textMesh, BroforceFont.Hudson, FontSize);

            meshRenderer.sortingOrder = -10;
        }

        public override void Cleanup()
        {
            textMesh = null;
            base.Cleanup();  // Base class handles GameObject destruction
        }

        public override void OnActivated()
        {
            base.OnActivated();

            if (IsEnabled && OnClick != null)
            {
                OnClick();
            }
        }

        private void MeasureAutoWidth()
        {
            string upperText = Text.ToUpper();
            cachedAutoWidth = FontManager.CalculateTextWidth(BroforceFont.Hudson, upperText, FontSize) + buttonPadding;
            boundsAvailable = true;
        }

        public override float GetPreferredWidth()
        {
            if (WidthMode == Layout.SizeMode.Auto)
            {
                if (!boundsAvailable && gameObject != null)
                {
                    MeasureAutoWidth();
                }
                return cachedAutoWidth;
            }
            return base.GetPreferredWidth();
        }
    }
}
