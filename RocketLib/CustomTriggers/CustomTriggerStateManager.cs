using System;
using System.Collections.Generic;

namespace RocketLib.CustomTriggers
{
    /// <summary>
    /// Manages persistent state across trigger executions and level lifecycle.
    /// Provides a simple key-value store that survives across trigger activations and level restarts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This manager uses a staging pattern to ensure proper initialization order during level start:
    /// </para>
    /// <list type="bullet">
    /// <item><description>SetForLevelStart: Stages values to be applied later in the current level start sequence (after all triggers execute)</description></item>
    /// <item><description>SetDuringLevel: Applies values immediately to the current level session</description></item>
    /// </list>
    /// <para>
    /// Use SetForLevelStart in level-start triggers (when isLevelStart=true) and SetDuringLevel
    /// when the trigger is activated mid-level (when isLevelStart=false).
    /// </para>
    /// </remarks>
    public static class CustomTriggerStateManager
    {
        private static readonly Dictionary<string, object> currentState = new Dictionary<string, object>();
        private static readonly Dictionary<string, object> stagingState = new Dictionary<string, object>();

        private static readonly List<Action> onLevelStartCallbacks = new List<Action>();
        private static readonly List<Action> onLevelEndCallbacks = new List<Action>();

        /// <summary>
        /// Retrieves a value from the current level state.
        /// </summary>
        /// <typeparam name="T">The type of the value to retrieve.</typeparam>
        /// <param name="key">The unique key for this value.</param>
        /// <param name="defaultValue">The value to return if the key is not found. Default is default(T).</param>
        /// <returns>The stored value if found, otherwise the default value.</returns>
        public static T Get<T>(string key, T defaultValue = default(T))
        {
            if (currentState.ContainsKey(key))
                return (T)currentState[key];
            return defaultValue;
        }

        /// <summary>
        /// Stages a value to be applied later in the current level start initialization sequence.
        /// Use this in level-start triggers (when isLevelStart=true in ExecuteAction).
        /// </summary>
        /// <typeparam name="T">The type of the value to store.</typeparam>
        /// <param name="key">The unique key for this value.</param>
        /// <param name="value">The value to store.</param>
        /// <remarks>
        /// <para>
        /// The value is staged and will be applied to current state after all level-start triggers have executed.
        /// This ensures your trigger's state is available to systems that initialize after trigger execution.
        /// </para>
        /// <para>
        /// On level restarts, level-start triggers run again, re-staging their values, which ensures
        /// state persists across restarts as long as the trigger continues to execute.
        /// </para>
        /// </remarks>
        public static void SetForLevelStart<T>(string key, T value)
        {
            stagingState[key] = value;
        }

        /// <summary>
        /// Sets a value immediately in the current level session.
        /// Use this when triggers are activated mid-level (when isLevelStart=false in ExecuteAction).
        /// </summary>
        /// <typeparam name="T">The type of the value to store.</typeparam>
        /// <param name="key">The unique key for this value.</param>
        /// <param name="value">The value to store.</param>
        /// <remarks>
        /// The value is applied immediately and will be available via Get() in the current level session.
        /// This value will be lost when the level restarts unless also set via SetForLevelStart.
        /// </remarks>
        public static void SetDuringLevel<T>(string key, T value)
        {
            currentState[key] = value;
        }

        /// <summary>
        /// Registers a callback to execute when the level starts (including restarts).
        /// </summary>
        /// <param name="callback">The action to execute at level start.</param>
        /// <remarks>
        /// Callbacks are executed after staged state is applied to current state.
        /// Exceptions in callbacks are caught and logged to prevent breaking level initialization.
        /// </remarks>
        public static void RegisterLevelStartAction(Action callback)
        {
            if (callback != null)
                onLevelStartCallbacks.Add(callback);
        }

        /// <summary>
        /// Registers a callback to execute when the level ends.
        /// </summary>
        /// <param name="callback">The action to execute at level end.</param>
        /// <remarks>
        /// Callbacks are executed before state is cleared and staged state is applied.
        /// Useful for cleanup or saving information before the level restarts.
        /// Exceptions in callbacks are caught and logged.
        /// </remarks>
        public static void RegisterLevelEndAction(Action callback)
        {
            if (callback != null)
                onLevelEndCallbacks.Add(callback);
        }

        /// <summary>
        /// Internal method called by RocketLib when a level starts.
        /// Handles the state lifecycle: end callbacks → clear current → apply staging → start callbacks.
        /// </summary>
        internal static void OnLevelStart()
        {
            foreach (var callback in onLevelEndCallbacks)
            {
                try
                {
                    callback?.Invoke();
                }
                catch (Exception ex)
                {
                    RocketMain.Logger.Log("Error in level end callback: " + ex.ToString());
                }
            }

            currentState.Clear();
            foreach (var kvp in stagingState)
                currentState[kvp.Key] = kvp.Value;
            stagingState.Clear();

            foreach (var callback in onLevelStartCallbacks)
            {
                try
                {
                    callback?.Invoke();
                }
                catch (Exception ex)
                {
                    RocketMain.Logger.Log("Error in level start callback: " + ex.ToString());
                }
            }
        }
    }
}
