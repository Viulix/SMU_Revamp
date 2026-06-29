using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SMU_Revamp.Models;
using System;
using System.Globalization;

namespace SMU_Revamp.Views
{
    public class ParameterTextBox : TextBox
    {
        private Point _dragStartPoint;
        private bool _isDraggingReady;
        private PointerPressedEventArgs? _pressedEventArgs;

        protected override Type StyleKeyOverride => typeof(TextBox);

        public ParameterTextBox()
        {
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, DragOver);
            AddHandler(DragDrop.DropEvent, Drop);

            ContextMenu = null;
            ContextFlyout = null;
        }

        private void DragOver(object? sender, DragEventArgs e)
        {
            if (e.DataTransfer.Contains(DataFormat.Text))
            {
                e.DragEffects = DragDropEffects.Copy;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Drop(object? sender, DragEventArgs e)
        {
            var text = e.DataTransfer.TryGetText();
            if (!string.IsNullOrEmpty(text))
            {
                if (DataContext is MeasurementParameter mp)
                {
                    if (mp.IsTextOrNumber)
                    {
                        string normalizedText = text.Replace(',', '.');
                        if (double.TryParse(normalizedText, CultureInfo.InvariantCulture, out double val))
                        {
                            // Validate bounds
                            if (mp.MinValue.HasValue && val < mp.MinValue.Value)
                            {
                                ToastHelper.ShowToast(this, $"Cannot drop: {val} is below Min ({mp.MinValue.Value})");
                                e.Handled = true;
                                return;
                            }
                            if (mp.MaxValue.HasValue && val > mp.MaxValue.Value)
                            {
                                ToastHelper.ShowToast(this, $"Cannot drop: {val} is above Max ({mp.MaxValue.Value})");
                                e.Handled = true;
                                return;
                            }

                            // Update value
                            UpdateParameterValue(mp, val);
                        }
                        else if (mp.Value is string)
                        {
                            mp.Value = text;
                        }
                        else
                        {
                            ToastHelper.ShowToast(this, $"Invalid format: '{text}' is not a number");
                        }
                    }
                }
            }
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var pointerProps = e.GetCurrentPoint(this).Properties;
            if (pointerProps.IsRightButtonPressed)
            {
                if (DataContext is MeasurementParameter mp && mp.IsTextOrNumber)
                {
                    double currentVal = mp.GetValueAsDouble(double.NaN);
                    if (!double.IsNaN(currentVal))
                    {
                        // Check if negative is allowed
                        if (mp.MinValue.HasValue && mp.MinValue.Value >= 0)
                        {
                            ToastHelper.ShowToast(this, $"Value cannot be negative (Min: {mp.MinValue.Value})");
                            e.Handled = true;
                            return;
                        }

                        double newVal = -currentVal;

                        // Check bounds
                        if (mp.MinValue.HasValue && newVal < mp.MinValue.Value)
                        {
                            ToastHelper.ShowToast(this, $"Value {newVal} is below Min ({mp.MinValue.Value})");
                            e.Handled = true;
                            return;
                        }
                        if (mp.MaxValue.HasValue && newVal > mp.MaxValue.Value)
                        {
                            ToastHelper.ShowToast(this, $"Value {newVal} is above Max ({mp.MaxValue.Value})");
                            e.Handled = true;
                            return;
                        }

                        // Apply new value
                        UpdateParameterValue(mp, newVal);
                        e.Handled = true;
                        return;
                    }
                }
            }
            else if (pointerProps.IsLeftButtonPressed)
            {
                _dragStartPoint = e.GetPosition(this);
                _isDraggingReady = true;
                _pressedEventArgs = e;
            }

            base.OnPointerPressed(e);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_isDraggingReady && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _pressedEventArgs != null)
            {
                var currentPos = e.GetPosition(this);
                var delta = currentPos - _dragStartPoint;
                if (Math.Abs(delta.X) > 5 || Math.Abs(delta.Y) > 5)
                {
                    _isDraggingReady = false;
                    StartDrag();
                    return;
                }
            }
            base.OnPointerMoved(e);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            _isDraggingReady = false;
            _pressedEventArgs = null;
            base.OnPointerReleased(e);
        }

        private async void StartDrag()
        {
            if (_pressedEventArgs == null) return;

            var data = new DataTransfer();
            string textToDrag = this.Text ?? string.Empty;
            if (string.IsNullOrEmpty(textToDrag)) return;

            data.Add(DataTransferItem.Create(DataFormat.Text, textToDrag));

            await DragDrop.DoDragDropAsync(_pressedEventArgs, data, DragDropEffects.Copy);
        }

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

                    UpdateParameterValue(mp, newVal);
                    e.Handled = true;
                }
            }
        }

        private void UpdateParameterValue(MeasurementParameter mp, double newVal)
        {
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

                double step = mp.ScrollStep;
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
        }
    }
}
