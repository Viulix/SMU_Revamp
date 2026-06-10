using System;
using System.Globalization;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Helper service to load persistently saved parameter defaults with type safety.
    /// </summary>
    public static class ParameterConfigHelper
    {
        /// <summary>
        /// Reads a parameter default value from persistent configuration.
        /// Falls back to fallbackValue if not set.
        /// </summary>
        public static object GetDefaultValue(string planName, string paramName, object fallbackValue)
        {
            var config = ConfigurationService.Instance.GetConfig();
            if (config.DefaultPlanParameters != null &&
                config.DefaultPlanParameters.TryGetValue(planName, out var planParams) &&
                planParams.TryGetValue(paramName, out var stringVal))
            {
                try
                {
                    if (fallbackValue is double)
                    {
                        if (double.TryParse(stringVal?.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                            return d;
                    }
                    else if (fallbackValue is int)
                    {
                        if (int.TryParse(stringVal, out int i))
                            return i;
                    }
                    else if (fallbackValue is bool)
                    {
                        if (bool.TryParse(stringVal, out bool b))
                            return b;
                    }
                    else
                    {
                        return stringVal ?? string.Empty;
                    }
                }
                catch
                {
                    // Fall back if parsing fails
                }
            }
            return fallbackValue;
        }
    }
}
