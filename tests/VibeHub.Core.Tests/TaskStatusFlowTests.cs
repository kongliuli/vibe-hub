using VibeHub.Core.Models;

namespace VibeHub.Core.Tests;

public sealed class TaskStatusFlowTests
{
    [Theory]
    [InlineData("Todo", "InProgress")]
    [InlineData("InProgress", "Done")]
    [InlineData("Done", "Todo")]
    [InlineData("legacy", "InProgress")]
    public void Next_FollowsCanonicalCycle(string current, string expected)
        => Assert.Equal(expected, TaskStatusFlow.Next(current));
}
