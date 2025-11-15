using UnityEngine;

namespace RocketLib.CustomTriggers
{
    /// <summary>
    /// Extended base class for custom trigger configuration that can run at level start.
    /// Provides a "Run at level start" toggle in the level editor.
    /// </summary>
    /// <remarks>
    /// Use this as your Info base class when creating triggers that should optionally execute
    /// at level start instead of when triggered by connections.
    /// Pair with LevelStartTriggerAction for the runtime behavior.
    /// </remarks>
    public abstract class LevelStartTriggerActionInfo : CustomTriggerActionInfo
    {
        /// <summary>
        /// When true, this trigger executes at level start. When false, executes when activated by trigger connections.
        /// </summary>
        public bool RunAtLevelStart = true;

        /// <summary>
        /// Shows the "Run at level start" toggle in the level editor.
        /// Override this and call base.ShowGUI(gui) to add additional configuration options.
        /// </summary>
        /// <param name="gui">The LevelEditorGUI instance.</param>
        public override void ShowGUI(LevelEditorGUI gui)
        {
            RunAtLevelStart = GUILayout.Toggle(RunAtLevelStart, "Run at level start");
            GUILayout.Space(10);
        }
    }
}
