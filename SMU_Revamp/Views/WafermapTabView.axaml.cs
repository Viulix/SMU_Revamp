using Avalonia.Controls;
using SMU_Revamp.ViewModels;

namespace SMU_Revamp.Views;

public partial class WafermapTabView : UserControl
{
    public WafermapTabView()
    {
        InitializeComponent();

        var subCellsControl = this.FindControl<ItemsControl>("SubCellsItemsControl");
        if (subCellsControl != null)
        {
            subCellsControl.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, SubCells_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        var waferCellsControl = this.FindControl<ItemsControl>("WaferCellsItemsControl");
        if (waferCellsControl != null)
        {
            waferCellsControl.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, WaferCells_PointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }
    }

    private void WaferCells_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Avalonia.Controls.Control);
        bool isLeft = point.Properties.IsLeftButtonPressed;
        bool isRight = point.Properties.IsRightButtonPressed;
        if (!isLeft && !isRight) return;

        if (DataContext is MainWindowViewModel vm)
        {
            var control = e.Source as Avalonia.Controls.Control;
            if (control?.DataContext is WaferCellViewModel cell)
            {
                bool isCtrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
                bool isAlt = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt);
                
                if (isCtrl || isAlt)
                {
                    e.Handled = true; // Prevent default ToggleButton behavior
                    bool newState = !cell.IsSelected;
                    
                    if (cell.Id.Length == 4)
                    {
                        string rowStr = cell.Id.Substring(0, 2);
                        string colStr = cell.Id.Substring(2, 2);
                        int targetRowInt = int.Parse(rowStr);
                        int targetColInt = int.Parse(colStr);
                        
                        foreach (var c in vm.WaferCells)
                        {
                            if (!c.IsValid || c.Id.Length != 4) continue;
                            
                            bool matchRow = isCtrl && c.Id.StartsWith(rowStr);
                            bool matchCol = isAlt && c.Id.EndsWith(colStr);
                            
                            if (matchRow || matchCol)
                            {
                                bool shouldToggle = true;
                                if (isRight)
                                {
                                    int cRow = int.Parse(c.Id.Substring(0, 2));
                                    int cCol = int.Parse(c.Id.Substring(2, 2));
                                    if (matchRow)
                                        shouldToggle = (cCol % 2) == (targetColInt % 2);
                                    else if (matchCol)
                                        shouldToggle = (cRow % 2) == (targetRowInt % 2);
                                }

                                if (shouldToggle)
                                {
                                    c.IsSelected = newState;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private void SubCells_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(sender as Avalonia.Controls.Control);
        bool isLeft = point.Properties.IsLeftButtonPressed;
        bool isRight = point.Properties.IsRightButtonPressed;
        if (!isLeft && !isRight) return;

        if (DataContext is MainWindowViewModel vm)
        {
            var control = e.Source as Avalonia.Controls.Control;
            if (control?.DataContext is SubCellViewModel cell)
            {
                bool isCtrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
                bool isAlt = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt);
                
                if (isCtrl || isAlt)
                {
                    e.Handled = true;
                    bool newState = !cell.IsSelected;
                    
                    foreach (var c in vm.SubCells)
                    {
                        if (!c.IsValid) continue;
                        
                        bool matchRow = isCtrl && c.Row == cell.Row;
                        bool matchCol = isAlt && c.Column == cell.Column;
                        
                        if (matchRow || matchCol)
                        {
                            bool shouldToggle = true;
                            if (isRight)
                            {
                                if (matchRow)
                                    shouldToggle = (c.Column % 2) == (cell.Column % 2);
                                else if (matchCol)
                                    shouldToggle = (c.Row % 2) == (cell.Row % 2);
                            }

                            if (shouldToggle)
                            {
                                c.IsSelected = newState;
                            }
                        }
                    }
                }
            }
        }
    }
}
