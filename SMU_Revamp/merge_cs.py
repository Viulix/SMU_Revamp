import os

base_dir = "/Users/niclas/Documents/Coding/Nano/SMU_Revamp/SMU_Revamp"

with open(f"{base_dir}/Views/DebugWindow.axaml.cs", "r") as f:
    debug_cs = f.read()

with open(f"{base_dir}/Views/SettingsWindow.axaml.cs", "r") as f:
    settings_cs = f.read()

# Replace class and constructor names
merged_cs = debug_cs.replace("public partial class DebugWindow", "public partial class SettingsWindow")
merged_cs = merged_cs.replace("public DebugWindow()", "public SettingsWindow()")

methods = """
    private void ResetButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            vm.Settings.ResetSettings();
        }
    }

    private async void ApplyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            await vm.Settings.ApplySettingsAsync();
            await Task.Delay(500);
            Close();
        }
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }

    private async void TestDbConnectionButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainWindowViewModel vm)
        {
            await vm.Settings.TestDbConnectionAsync();
        }
    }
"""

# Insert the methods before the very last closing brace.
parts = merged_cs.rsplit("}", 1)
merged_cs = parts[0] + methods + "\n}\n"

with open(f"{base_dir}/Views/SettingsWindow.axaml.cs", "w") as f:
    f.write(merged_cs)

# Delete DebugWindow.axaml.cs
os.remove(f"{base_dir}/Views/DebugWindow.axaml.cs")
print("Merge complete")
