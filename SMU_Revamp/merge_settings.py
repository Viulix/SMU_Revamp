import re
import os

base_dir = "/Users/niclas/Documents/Coding/Nano/SMU_Revamp/SMU_Revamp"

# 1. Merge AXAML
with open(f"{base_dir}/Views/DebugWindow.axaml", "r") as f:
    debug_axaml = f.read()

with open(f"{base_dir}/Views/SettingsWindow.axaml", "r") as f:
    settings_axaml = f.read()

# Extract ScrollViewer from SettingsWindow
scroll_viewer_match = re.search(r'(<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">.*?</ScrollViewer>)', settings_axaml, re.DOTALL)
settings_content = scroll_viewer_match.group(1)

# Rename bindings in settings content
settings_content = re.sub(r'\{Binding ([a-zA-Z0-9_]+)\}', r'{Binding Settings.\1}', settings_content)

# Update DebugWindow to SettingsWindow
merged_axaml = debug_axaml.replace('x:Class="SMU_Revamp.Views.DebugWindow"', 'x:Class="SMU_Revamp.Views.SettingsWindow"')
merged_axaml = merged_axaml.replace('Title="Configuration"', 'Title="Settings"')

# Insert Developer tab
developer_tab = f"""            <TabItem Header="Developer">
{settings_content}
            </TabItem>
"""

merged_axaml = merged_axaml.replace("        </TabControl>", developer_tab + "        </TabControl>")

with open(f"{base_dir}/Views/SettingsWindow.axaml", "w") as f:
    f.write(merged_axaml)

# 2. Merge CodeBehind
with open(f"{base_dir}/Views/DebugWindow.axaml.cs", "r") as f:
    debug_cs = f.read()

with open(f"{base_dir}/Views/SettingsWindow.axaml.cs", "r") as f:
    settings_cs = f.read()

# Replace class and constructor names
merged_cs = debug_cs.replace("public partial class DebugWindow", "public partial class SettingsWindow")
merged_cs = merged_cs.replace("public DebugWindow()", "public SettingsWindow()")

# Extract methods from SettingsWindow
methods_pattern = r"(private void ResetButton_Click.*|private async void ApplyButton_Click.*|private void CloseButton_Click.*|private async void TestDbConnectionButton_Click.*)"
# We'll just extract them manually
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

merged_cs = merged_cs.replace("}\n", methods + "}\n")

with open(f"{base_dir}/Views/SettingsWindow.axaml.cs", "w") as f:
    f.write(merged_cs)

# Delete DebugWindow files
os.remove(f"{base_dir}/Views/DebugWindow.axaml")
os.remove(f"{base_dir}/Views/DebugWindow.axaml.cs")
print("Merge complete")
