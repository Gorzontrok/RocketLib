using UnityEngine;

namespace RocketLib.CustomTriggers
{
    /// <summary>
    /// Base class for custom trigger action configuration data.
    /// Extend this class to create the "Info" portion of a custom trigger, which stores configuration
    /// and provides the level editor GUI.
    /// </summary>
    /// <remarks>
    /// This class is serialized to level files. All public fields will be saved and loaded.
    /// Override ShowGUI to provide configuration options in the level editor.
    /// </remarks>
    public abstract class CustomTriggerActionInfo : TriggerActionInfo
    {
        /// <summary>
        /// Displays a button in the level editor GUI that allows setting a GridPoint by clicking on the map.
        /// </summary>
        /// <param name="gui">The LevelEditorGUI instance (passed from ShowGUI).</param>
        /// <param name="point">The GridPoint to set. This will be modified when the user clicks on the map.</param>
        /// <param name="label">The label text for the button (e.g., "Set Spawn Point").</param>
        /// <remarks>
        /// Call this from your ShowGUI override to allow users to select grid coordinates visually.
        /// The button shows the current column and row, and clicking it allows the user to click on the map to set a new position.
        /// </remarks>
        public static void ShowGridPointOption(LevelEditorGUI gui, GridPoint point, string label)
        {
            if (GUILayout.Button(string.Concat(new object[] { label, " (currently C ", point.collumn, " R ", point.row, ")" }), new GUILayoutOption[0]))
            {
                gui.settingWaypoint = true;
                gui.waypointToSet = point;
                gui.MarkTargetPoint(point);
            }
        }
    }
}
