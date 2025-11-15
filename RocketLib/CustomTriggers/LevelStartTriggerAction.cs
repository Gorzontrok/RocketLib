namespace RocketLib.CustomTriggers
{
    /// <summary>
    /// Simplified base class for custom triggers that can run at level start or when triggered.
    /// Automatically handles state management and provides a single ExecuteAction method.
    /// </summary>
    /// <typeparam name="TInfo">The LevelStartTriggerActionInfo type that contains configuration for this action.</typeparam>
    /// <remarks>
    /// This class simplifies trigger implementation by:
    /// - Automatically setting state to Done after execution
    /// - Providing a single ExecuteAction method instead of Start/Update
    /// - Handling level start vs trigger activation logic
    /// Use this when your trigger performs a one-time action and doesn't need per-frame updates.
    /// </remarks>
    public abstract class LevelStartTriggerAction<TInfo> : CustomTriggerAction<TInfo> where TInfo : LevelStartTriggerActionInfo
    {
        /// <summary>
        /// Implement this method to define your trigger's behavior.
        /// Called once when the trigger executes, either at level start or when triggered by connections.
        /// </summary>
        /// <param name="isLevelStart">
        /// True if executing at level start (info.RunAtLevelStart was true).
        /// False if executing due to trigger activation.
        /// Use this to decide whether to use CustomTriggerStateManager.SetForLevelStart or SetDuringLevel.
        /// </param>
        protected abstract void ExecuteAction(bool isLevelStart);

        /// <summary>
        /// Gets or sets the trigger action configuration.
        /// When set, executes the action immediately if RunAtLevelStart is true.
        /// </summary>
        public override TriggerActionInfo Info
        {
            get { return info; }
            set
            {
                info = (TInfo)value;
                if (info.RunAtLevelStart)
                {
                    ExecuteAction(isLevelStart: true);
                }
            }
        }

        /// <summary>
        /// Called when the trigger is activated by connections (not at level start).
        /// Automatically sets state to Done after execution.
        /// </summary>
        public override void Start()
        {
            base.Start();
            if (!info.RunAtLevelStart)
            {
                ExecuteAction(isLevelStart: false);
            }
            this.state = TriggerActionState.Done;
        }

        /// <summary>
        /// Not used by LevelStartTriggerAction. All logic is in ExecuteAction.
        /// </summary>
        public override void Update() { }
    }
}
