using System;
using System.Text.Json.Serialization;

namespace javis.Services.ChatActions;

public sealed class ChatActionEnvelope
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "say";

    // say
    [JsonPropertyName("say")] public string? Say { get; set; }

    // todo actions
    [JsonPropertyName("todo")] public TodoAction? Todo { get; set; }

    // numbered delete support
    [JsonPropertyName("index")] public int? Index { get; set; } = null;

    // list query support
    [JsonPropertyName("date")] public string? Date { get; set; } = null; // yyyy-MM-dd or korean relative
    [JsonPropertyName("from")] public string? From { get; set; } = null; // yyyy-MM-dd or korean relative
    [JsonPropertyName("days")] public int? Days { get; set; } = null;    // range length
}

public sealed class TodoAction
{
    [JsonPropertyName("op")] public string Op { get; set; } = "upsert"; // upsert|delete
    [JsonPropertyName("id")] public string? Id { get; set; } = null;     // optional

    [JsonPropertyName("date")] public string? Date { get; set; } = null; // yyyy-MM-dd
    [JsonPropertyName("time")] public string? Time { get; set; } = null; // HH:mm or null

    [JsonPropertyName("title")] public string? Title { get; set; } = null;
    [JsonPropertyName("isDone")] public bool? IsDone { get; set; } = null;
}
