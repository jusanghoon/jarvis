namespace javis.Models;

public sealed class CalendarTodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime Date { get; set; }

    public TimeSpan? Time { get; set; }

    public string Title { get; set; } = "";
    public string? Notes { get; set; }

    public string? Location { get; set; }

    public bool IsDone { get; set; }
}
