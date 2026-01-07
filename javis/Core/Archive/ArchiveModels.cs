using System;
using System.Collections.Generic;

namespace Jarvis.Core.Archive;

public enum KnowledgeState
{
    Active,
    Standby,
    Idle,
    Fossil
}

public enum GEMSRole
{
    Logician,
    Emotionist,
    StoryArchitect,
    Connectors,
    Ethicist,
    Recorder
}

public sealed record ArchiveEntry(
    string EventId,
    DateTimeOffset Timestamp,
    string Content,
    GEMSRole Role,
    KnowledgeState State,
    Dictionary<string, object?>? Metadata = null
);
