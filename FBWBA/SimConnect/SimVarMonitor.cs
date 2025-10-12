using System;
using System.Collections.Generic;

namespace FBWBA.SimConnect
{
    public class SimVarMonitor
    {
        private Dictionary<string, double> previousValues = new Dictionary<string, double>();
        public event EventHandler<SimVarChangeEventArgs> ValueChanged;

        /// <summary>
        /// Controls whether announcements are enabled.
        /// When false, variable changes are tracked but not announced.
        /// </summary>
        public bool AnnouncementsEnabled { get; set; } = false;

        public void ProcessUpdate(string varName, double newValue, string description)
        {
            bool isNewValue = !previousValues.ContainsKey(varName);
            bool hasChanged = isNewValue || Math.Abs(previousValues[varName] - newValue) > 0.001;

            if (hasChanged)
            {
                double oldValue = isNewValue ? 0 : previousValues[varName];
                previousValues[varName] = newValue;

                // Only fire ValueChanged event if announcements are enabled
                // This allows initial values to be collected without announcements
                if (AnnouncementsEnabled)
                {
                    ValueChanged?.Invoke(this, new SimVarChangeEventArgs
                    {
                        VarName = varName,
                        OldValue = oldValue,
                        NewValue = newValue,
                        Description = description,
                        IsInitialValue = isNewValue
                    });
                }
            }
        }

        /// <summary>
        /// Enables announcements for variable changes.
        /// Call this after initial connection to begin monitoring.
        /// </summary>
        public void EnableAnnouncements()
        {
            AnnouncementsEnabled = true;
            System.Diagnostics.Debug.WriteLine("[SimVarMonitor] Announcements enabled - continuous monitoring active");
        }

        public void Reset()
        {
            previousValues.Clear();
            AnnouncementsEnabled = false;
        }
    }

    public class SimVarChangeEventArgs : EventArgs
    {
        public string VarName { get; set; }
        public double OldValue { get; set; }
        public double NewValue { get; set; }
        public string Description { get; set; }
        public bool IsInitialValue { get; set; }
    }
}
