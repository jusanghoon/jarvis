using System.Windows;
using System.Windows.Controls;

namespace javis.Pages;

public partial class ChatEntryPage : Page
{
    public ChatEntryPage()
    {
        InitializeComponent();
    }

    private void Select(string mode)
    {
        if (MainChatToggle != null) MainChatToggle.IsChecked = mode == "main";
        if (SoloThinkToggle != null) SoloThinkToggle.IsChecked = mode == "solo";
        if (DuoDebateToggle != null) DuoDebateToggle.IsChecked = mode == "duo";
    }

    private void MainChat_Click(object sender, RoutedEventArgs e)
    {
        Select("main");
        NavigationService?.Navigate(new ChatPage(ChatMode.MainChat));
    }

    private void SoloThink_Click(object sender, RoutedEventArgs e)
    {
        Select("solo");
        NavigationService?.Navigate(new ChatPage(ChatMode.SoloThink));
    }

    private void DuoDebate_Click(object sender, RoutedEventArgs e)
    {
        Select("duo");
        NavigationService?.Navigate(new ChatPage(ChatMode.DuoDebate));
    }
}
