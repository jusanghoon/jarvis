using System.Windows;
using Jarvis.Core.Archive;

namespace javis.UI.Dialogs;

public partial class FossilDetailWindow : Window
{
    public FossilDetailWindow(FossilEntry entry)
    {
        InitializeComponent();
        DataContext = entry;

        Loaded += (_, __) => ContentBox.Focus();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        ContentBox.Focus();
        ContentBox.SelectAll();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var text = string.IsNullOrEmpty(ContentBox.SelectedText) ? ContentBox.Text : ContentBox.SelectedText;
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }
}
