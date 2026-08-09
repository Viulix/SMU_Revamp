using Avalonia;
using Avalonia.Controls;
using System;

namespace SMU_Revamp.Views
{
    public class SavePromptResult
    {
        public bool Cancelled { get; set; }
        public string Profile { get; set; } = string.Empty;
        public string SampleName { get; set; } = string.Empty;
    }

    public partial class SavePromptWindow : Window
    {
        public SavePromptWindow()
        {
            InitializeComponent();
        }

        public SavePromptWindow(string initialProfile, string initialSampleName, System.Collections.Generic.IEnumerable<string>? existingDevices = null) : this()
        {
            var profileBox = this.FindControl<TextBox>("ProfileTextBox");
            if (profileBox != null)
            {
                profileBox.Text = initialProfile;
            }
            var sampleBox = this.FindControl<AutoCompleteBox>("SampleNameAutoCompleteBox");
            if (sampleBox != null)
            {
                if (existingDevices != null)
                {
                    sampleBox.ItemsSource = existingDevices;
                }
                sampleBox.Text = initialSampleName;
            }
        }

        private void OkButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var profileText = this.FindControl<TextBox>("ProfileTextBox")?.Text ?? string.Empty;
            var sampleText = this.FindControl<AutoCompleteBox>("SampleNameAutoCompleteBox")?.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(profileText))
            {
                return;
            }

            Close(new SavePromptResult
            {
                Cancelled = false,
                Profile = profileText.Trim(),
                SampleName = string.IsNullOrWhiteSpace(sampleText) ? "Empty Device" : sampleText.Trim()
            });
        }

        private void CancelButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close(new SavePromptResult { Cancelled = true });
        }
    }
}
