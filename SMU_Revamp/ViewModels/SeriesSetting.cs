using System;
using System.ComponentModel;
using Avalonia.Media;
using System.Runtime.CompilerServices;

namespace SMU_Revamp.ViewModels;

public class SeriesSetting : ViewModelBase
{
    private string _seriesName = string.Empty;
    public string SeriesName
    {
        get => _seriesName;
        set { _seriesName = value; OnPropertyChanged(); }
    }

    private string _colorHex = string.Empty;
    public string ColorHex
    {
        get => _colorHex;
        set 
        {
            if (_colorHex != value)
            {
                _colorHex = value; 
                OnPropertyChanged();
                
                try
                {
                    var newColor = Avalonia.Media.Color.Parse(value);
                    if (_pickerColor != newColor)
                    {
                        _pickerColor = newColor;
                        OnPropertyChanged(nameof(PickerColor));
                    }
                }
                catch { }
            }
        }
    }

    private Avalonia.Media.Color _pickerColor;
    public Avalonia.Media.Color PickerColor
    {
        get => _pickerColor;
        set
        {
            if (_pickerColor != value)
            {
                _pickerColor = value;
                OnPropertyChanged();
                
                var newHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                if (_colorHex != newHex)
                {
                    _colorHex = newHex;
                    OnPropertyChanged(nameof(ColorHex));
                }
            }
        }
    }

    public SeriesSetting(string seriesName, string defaultColorHex)
    {
        SeriesName = seriesName;
        ColorHex = defaultColorHex;
    }
}
