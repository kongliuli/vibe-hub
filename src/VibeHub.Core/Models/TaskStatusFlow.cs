namespace VibeHub.Core.Models;

public static class TaskStatusFlow
{
    public static string Next(string? status) => status switch
    {
        "Todo" => "InProgress",
        "InProgress" => "Done",
        "Done" => "Todo",
        _ => "InProgress"
    };
}
