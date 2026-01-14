using CommunityToolkit.Mvvm.ComponentModel;

namespace javis.Services;

public sealed partial class RuntimeSettings : ObservableObject
{
    public static RuntimeSettings Instance { get; } = new();

    private RuntimeSettings() { }

    [ObservableProperty]
    private string _model = "gemma3:1b";

    [ObservableProperty]
    private string _mainAiName = "재민";

    [ObservableProperty]
    private bool _homeRightPanelEnabled = true;

    [ObservableProperty]
    private double _uiScale = 0.9;
}
