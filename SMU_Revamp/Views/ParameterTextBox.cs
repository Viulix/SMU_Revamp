using Avalonia.Controls;
using Avalonia.Input;
using SMU_Revamp.Models;
using System;
using System.Globalization;

namespace SMU_Revamp.Views
{
    public class ParameterTextBox : TextBox
    {
        protected override Type StyleKeyOverride => typeof(TextBox);
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);

            if (DataContext is MeasurementParameter mp && mp.IsTextOrNumber)
            {
                double currentVal = mp.GetValueAsDouble(double.NaN);
                if (!double.IsNaN(currentVal))
                {
                    double step = mp.ScrollStep;
                    double newVal = currentVal;

                    if (e.Delta.Y > 0)
                    {
                        newVal += step;
                    }
                    else if (e.Delta.Y < 0)
                    {
                        newVal -= step;
                    }

                    if (mp.MinValue.HasValue && newVal < mp.MinValue.Value)
                    {
                        newVal = mp.MinValue.Value;
                    }
                    if (mp.MaxValue.HasValue && newVal > mp.MaxValue.Value)
                    {
                        newVal = mp.MaxValue.Value;
                    }

                    // Preserve the type and format where possible
                    if (mp.Value is int)
                    {
                        mp.Value = (int)Math.Round(newVal);
                    }
                    else if (mp.Value is double)
                    {
                        mp.Value = newVal;
                    }
                    else if (mp.Value is float)
                    {
                        mp.Value = (float)newVal;
                    }
                    else
                    {
                        string original = mp.Value?.ToString() ?? string.Empty;
                        int decimalPlaces = 0;
                        int dotIndex = original.IndexOf('.');
                        if (dotIndex < 0)
                        {
                            dotIndex = original.IndexOf(',');
                        }
                        if (dotIndex >= 0)
                        {
                            decimalPlaces = original.Length - dotIndex - 1;
                        }
                        
                        string stepStr = step.ToString(CultureInfo.InvariantCulture);
                        int stepDecimals = 0;
                        int stepDot = stepStr.IndexOf('.');
                        if (stepDot >= 0)
                        {
                            stepDecimals = stepStr.Length - stepDot - 1;
                        }
                        
                        int decimals = Math.Max(decimalPlaces, stepDecimals);
                        bool useComma = original.Contains(',');
                        string newValStr = newVal.ToString("F" + decimals, CultureInfo.InvariantCulture);
                        if (useComma)
                        {
                            newValStr = newValStr.Replace('.', ',');
                        }
                        mp.Value = newValStr;
                    }

                    e.Handled = true;
                }
            }
        }
    }
}
