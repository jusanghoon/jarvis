using System.Windows;
using System.Windows.Controls;

namespace javis.Pages;

public partial class ChatPage : Page
{
    private void OnBusMessage(string text)
    {
        Dispatcher.InvokeAsync(async () =>
        {
            await _vm.SendExternalAsync(text);
        });
    }
}
