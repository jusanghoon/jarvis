using System;
using System.Collections.Generic;

namespace javis.Services;

public sealed class SoloNotesLimiter
{
    private readonly Queue<DateTimeOffset> _writes = new();
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxPerHour { get; set; } = 12;

    private DateTimeOffset _lastWrite = DateTimeOffset.MinValue;

    public bool CanWriteNow()
    {
        var now = DateTimeOffset.Now;
        if (_lastWrite != DateTimeOffset.MinValue && now - _lastWrite < MinInterval) return false;

        while (_writes.Count > 0 && now - _writes.Peek() > TimeSpan.FromHours(1))
            _writes.Dequeue();

        return _writes.Count < MaxPerHour;
    }

    public void MarkWrote()
    {
        var now = DateTimeOffset.Now;
        _lastWrite = now;
        _writes.Enqueue(now);
    }

    public bool TryAcquire()
    {
        if (!CanWriteNow()) return false;
        MarkWrote();
        return true;
    }
}
