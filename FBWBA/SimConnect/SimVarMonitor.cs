using System;
using System.Collections.Generic;

namespace FBWBA.SimConnect
{
    public class SimVarMonitor
    {
        private Dictionary<string, double> previousValues = new Dictionary<string, double>();
        public event EventHandler<SimVarChangeEventArgs> ValueChanged;

        public void ProcessUpdate(string varName, double newValue, string description)
        {
            bool isNewValue = !previousValues.ContainsKey(varName);
            bool hasChanged = isNewValue || Math.Abs(previousValues[varName] - newValue) > 0.001;

            if (hasChanged)
            {
                double oldValue = isNewValue ? 0 : previousValues[varName];
                previousValues[varName] = newValue;

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

        public void Reset()
        {
            previousValues.Clear();
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
