using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace TagForge.Views
{
    public partial class AgentManagerView : UserControl
    {
        public AgentManagerView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void ToggleModelDropdown(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is Button btn) 
            {
               var parent = btn.Parent as Panel; // Grid
               var box = parent?.Children[0] as AutoCompleteBox;
               if (box != null)
               {
                   if (!box.IsDropDownOpen)
                   {
                       // Show all items explicitly
                       box.ItemFilter = (search, item) => true;
                       
                       box.IsDropDownOpen = true;
                       box.Focus();
                       
                       box.DropDownClosed += Box_DropDownClosed;
                   }
                   else
                   {
                       box.IsDropDownOpen = false;
                   }
               }
            }
        }

        private void Box_DropDownClosed(object? sender, System.EventArgs e)
        {
            if (sender is AutoCompleteBox box)
            {
                // Restore standard filtering
                box.ItemFilter = StandardFilter;
                box.DropDownClosed -= Box_DropDownClosed;
            }
        }

        private void ModelSelector_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
        {
             if (sender is AutoCompleteBox box)
             {
                 // Ensure we are using standard filter when typing
                 // Only reset if we are currently in "Show All" mode (which we can guess if ItemFilter is not StandardFilter)
                 // But safer to just force it. assigning same delegate might verify equality and skip, 
                 // so we might want to check.
                 
                 // If the current filter allows everything (ShowAll), we must switch to Standard.
                 // We can simply assign StandardFilter.
                 box.ItemFilter = StandardFilter;
             }
        }
        
        private bool StandardFilter(string search, object item)
        {
            if (string.IsNullOrEmpty(search)) return true;
            return item?.ToString()?.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
