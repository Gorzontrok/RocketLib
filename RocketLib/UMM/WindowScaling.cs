using System;
using UnityEngine;

namespace RocketLib.UMM
{
    /// <summary>
    /// Provides window width detection and UI scaling utilities for UnityModManager GUIs.
    /// Handles the two-pass ImGui rendering correctly to avoid control count mismatch errors.
    /// </summary>
    public static class WindowScaling
    {
        private static float _windowWidth = -1f;
        private const float BASELINE_WIDTH = 1200f;

        /// <summary>
        /// Enable/disable scaling globally. Set this once per frame based on your mod's settings.
        /// Defaults to true.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Captures the window width on the first frame. Call this at the start of your OnGUI method.
        /// Returns true when ready to render UI, false when still detecting width.
        /// </summary>
        /// <returns>True if width has been captured and UI can be rendered</returns>
        public static bool TryCaptureWidth()
        {
            // If already captured, we're ready
            if (_windowWidth >= 0) return true;

            // Not captured yet - do capture but return false for this entire frame
            // This ensures consistent return value during both Layout and Repaint passes
            try
            {
                GUILayout.BeginHorizontal();
                Rect rect = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true));
                if (Event.current.type == EventType.Repaint && rect.width > 1)
                {
                    _windowWidth = rect.width;
                }
                GUILayout.EndHorizontal();
            }
            catch (Exception)
            {
            }

            // Always return false on the capture frame to keep Layout/Repaint consistent
            return false;
        }

        /// <summary>
        /// Gets the detected window width, or -1 if not yet captured.
        /// </summary>
        public static float WindowWidth => _windowWidth;

        /// <summary>
        /// Gets the scale factor based on baseline width (1200px).
        /// Returns 1.0 if width hasn't been captured yet.
        /// </summary>
        public static float ScaleFactor => _windowWidth > 0 ? _windowWidth / BASELINE_WIDTH : 1.0f;

        /// <summary>
        /// Creates a scaled width GUILayoutOption.
        /// Uses the Enabled property to determine whether to apply scaling.
        /// </summary>
        /// <param name="width">The base width value (assumes 1200px baseline)</param>
        /// <returns>GUILayoutOption for use with GUILayout methods</returns>
        public static GUILayoutOption ScaledWidth(float width)
        {
            return GUILayout.Width(Enabled ? width * ScaleFactor : width);
        }

        /// <summary>
        /// Creates scaled horizontal space.
        /// Uses the Enabled property to determine whether to apply scaling.
        /// </summary>
        /// <param name="width">The base space width (assumes 1200px baseline)</param>
        public static void ScaledSpace(float width)
        {
            GUILayout.Space(Enabled ? width * ScaleFactor : width);
        }

        /// <summary>
        /// Resets the cached window width, forcing re-detection on next frame.
        /// Useful for testing or if the window is resized.
        /// </summary>
        public static void Reset()
        {
            _windowWidth = -1f;
        }
    }
}
