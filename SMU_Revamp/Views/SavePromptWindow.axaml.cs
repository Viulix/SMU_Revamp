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

        public SavePromptWindow(string initialProfile, string initialSampleName) : this()
        {
            var profileBox = this.FindControl<TextBox>("ProfileTextBox");
            if (profileBox != null)
            {
                profileBox.Text = initialProfile;
            }
            var sampleBox = this.FindControl<TextBox>("SampleNameTextBox");
            if (sampleBox != null)
            {
                sampleBox.Text = initialSampleName;
            }
        }

        private void OkButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var profileText = this.FindControl<TextBox>("ProfileTextBox")?.Text ?? string.Empty;
            var sampleText = this.FindControl<TextBox>("SampleNameTextBox")?.Text ?? string.Empty;

            if (string.IsNullOrWhiteSpace(profileText) || string.IsNullOrWhiteSpace(sampleText))
            {
                // Both fields are required to proceed
                return;
            }

            Close(new SavePromptResult
            {
                Cancelled = false,
                Profile = profileText.Trim(),
                SampleName = sampleText.Trim()
            });
        }

        private void CancelButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close(new SavePromptResult { Cancelled = true });
        }
    }
}
