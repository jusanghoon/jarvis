using System.Collections.Concurrent;

namespace javis.Services;

public static class ChatBus
{
    private static readonly ConcurrentQueue<string> _queue = new();
    public static event Action<string>? MessageQueued;

    public static void Send(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var handler = MessageQueued;
        if (handler != null)
        {
            handler(text);
        }
        else
        {
            _queue.Enqueue(text);
        }
    }

    public static bool TryDequeue(out string text) => _queue.TryDequeue(out text);
}
