using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TagForge.ViewModels;
using System.Collections.Specialized;
using System.Linq;

namespace TagForge.Views;

public partial class ChatView : UserControl
{
    private ScrollViewer? _scroller;

    public ChatView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _scroller = this.FindControl<ScrollViewer>("ChatScroller");
        
        if (DataContext is ChatViewModel vm)
        {
            vm.Messages.CollectionChanged += OnMessagesChanged;
            vm.RequestScroll += OnRequestScroll;
        }
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (DataContext is ChatViewModel vm)
        {
            vm.Messages.CollectionChanged -= OnMessagesChanged;
            vm.RequestScroll -= OnRequestScroll;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            _scroller?.ScrollToEnd();
        }
    }

    private void OnRequestScroll()
    {
        _scroller?.ScrollToEnd();
    }
}
