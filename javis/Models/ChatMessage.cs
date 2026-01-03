using CommunityToolkit.Mvvm.ComponentModel;

namespace javis.Models;

public partial class ChatMessage : ObservableObject
{
    public ChatMessage(string role, string text)
    {
        Role = role;
        _text = text;
        CreatedAt = DateTime.Now;
    }

    public string Role { get; }
    public DateTime CreatedAt { get; }

    [ObservableProperty]
    private string _text;
}
