namespace javis.Services.Solo;

public interface ISoloUiSink
{
    void PostSystem(string text);
    void PostAssistant(string text);
    void PostDebug(string text);
}
