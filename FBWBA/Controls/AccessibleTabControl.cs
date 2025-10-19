namespace FBWBA.Controls;

/// <summary>
/// Custom TabControl that suppresses "Tab control" announcement in NVDA
/// while preserving individual tab name announcements
/// </summary>
public class AccessibleTabControl : TabControl
{
    public AccessibleTabControl()
    {
        // Standard TabControl initialization
    }

    /// <summary>
    /// Override to provide custom accessibility behavior
    /// This is the key method that accessibility experts use to customize screen reader announcements
    /// </summary>
    protected override AccessibleObject CreateAccessibilityInstance()
    {
        return new AccessibleTabControlAccessibleObject(this);
    }

    /// <summary>
    /// Custom AccessibleObject that changes the role to suppress "Tab control" announcement
    /// </summary>
    private class AccessibleTabControlAccessibleObject : ControlAccessibleObject
    {
        public AccessibleTabControlAccessibleObject(AccessibleTabControl owner) : base(owner)
        {
        }

        /// <summary>
        /// Override Role to return Client instead of PageTabList
        /// This tells NVDA to skip announcing the control type
        /// </summary>
        public override AccessibleRole Role
        {
            get
            {
                // Return Client role to suppress "Tab control" announcement
                // NVDA will now only announce the selected tab name
                return AccessibleRole.Client;
            }
        }

        /// <summary>
        /// Override Name to ensure only relevant information is announced
        /// Returns empty string to let child tabs handle their own announcements
        /// </summary>
        public override string? Name
        {
            get
            {
                // Return empty name - let the selected tab page handle its own announcement
                return string.Empty;
            }
        }
    }
}
