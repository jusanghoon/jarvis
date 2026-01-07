using System.Windows;
using System.Windows.Controls;

namespace javis.Pages;

public partial class ChatEntryPage : Page
{
    public ChatEntryPage()
    {
        InitializeComponent();
    }

    private void MainChat_Click(object sender, RoutedEventArgs e)
        => NavigationService?.Navigate(new ChatPage(ChatMode.MainChat));

    private void SoloThink_Click(object sender, RoutedEventArgs e)
        => NavigationService?.Navigate(new ChatPage(ChatMode.SoloThink));

    private void DuoDebate_Click(object sender, RoutedEventArgs e)
        => NavigationService?.Navigate(new ChatPage(ChatMode.DuoDebate));
}
