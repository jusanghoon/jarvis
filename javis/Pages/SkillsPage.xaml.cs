using System;
using System.Windows;
using System.Windows.Controls;
using javis.Services;
using javis.ViewModels;

namespace javis.Pages;

public partial class SkillsPage : Page
{
    private readonly SkillsViewModel _vm = new();

    public SkillsPage()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.NavigateToChatRequested += () =>
        {
            if (Application.Current.MainWindow is MainWindow mw)
                mw.NavigateToChat();
        };
    }

    private void ReloadBtn_Click(object sender, RoutedEventArgs e)
    {
        _vm.Reload();
    }

    private async void CreateSkillBtn_Click(object sender, RoutedEventArgs e)
    {
        var req = CreateSkillBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(req)) return;

        CreateSkillBtn.IsEnabled = false;
        try
        {
            var (skillFile, pluginFile) = await PluginHost.Instance.CreateSkillAsync(req);

            _vm.Reload();

            MessageBox.Show(
                pluginFile is null
                    ? $"\uC2A4\uD0AC \uC0DD\uC131 \uC644\uB8CC: {skillFile}"
                    : $"\uC2A4\uD0AC+\uD50C\uB7EC\uADF8\uC778 \uC0DD\uC131 \uC644\uB8CC:\n- {skillFile}\n- {pluginFile}",
                "DONE");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "\uC0DD\uC131 \uC2E4\uD328");
        }
        finally
        {
            CreateSkillBtn.IsEnabled = true;
        }
    }
}
