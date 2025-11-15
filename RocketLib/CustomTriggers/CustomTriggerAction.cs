namespace RocketLib.CustomTriggers
{
    /// <summary>
    /// Base class for custom trigger action runtime behavior.
    /// Extend this class to create the "Action" portion of a custom trigger, which implements
    /// the runtime execution logic when the trigger is activated.
    /// </summary>
    /// <typeparam name="TInfo">The CustomTriggerActionInfo type that contains configuration for this action.</typeparam>
    /// <remarks>
    /// Override Start() for initialization and Update() for per-frame logic.
    /// Set this.state to TriggerActionState.Done when execution is complete.
    /// Access configuration via the strongly-typed 'info' field.
    /// </remarks>
    public abstract class CustomTriggerAction<TInfo> : TriggerAction where TInfo : CustomTriggerActionInfo
    {
        /// <summary>
        /// Strongly-typed reference to the configuration data for this trigger action.
        /// </summary>
        protected TInfo info;

        /// <summary>
        /// Gets or sets the trigger action configuration.
        /// </summary>
        public override TriggerActionInfo Info
        {
            get { return info; }
            set { info = (TInfo)value; }
        }
    }
}
