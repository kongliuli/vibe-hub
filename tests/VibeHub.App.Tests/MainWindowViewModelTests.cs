using VibeHub.App.ViewModels;

namespace VibeHub.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void ComposesChildViewModels_AndTracksNavigation()
    {
        var workspace = new WorkspaceViewModel();
        var jobs = new JobsViewModel();
        var sessions = new SessionsViewModel();
        var context = new ContextViewModel();
        var sut = new MainWindowViewModel(workspace, jobs, sessions, context);

        Assert.Same(workspace, sut.Workspace);
        Assert.Same(jobs, sut.Jobs);
        Assert.Same(sessions, sut.Sessions);
        Assert.Same(context, sut.Context);
        Assert.Equal("workbench", sut.CurrentPage);

        sut.NavigateCommand.Execute("sessions");
        Assert.Equal("sessions", sut.CurrentPage);

        sut.NavigateCommand.Execute(" ");
        Assert.Equal("sessions", sut.CurrentPage);
    }

    [Fact]
    public void ExistingWorkbenchState_RemainsBoundedAndUpdateable()
    {
        var sut = CreateViewModel();

        sut.SetAgentAvailability("codex", true);
        sut.SetAgentAvailability("missing", true);
        for (var i = 0; i < 13; i++)
            sut.AddActivity($"event-{i}", "detail", "test");

        Assert.Equal("Ready", sut.Agents.Single(agent => agent.ProviderId == "codex").Status);
        Assert.Equal(4, sut.Agents.Count);
        Assert.Equal(12, sut.Activity.Count);
        Assert.Equal("event-12", sut.Activity[0].Title);
        Assert.Equal("event-1", sut.Activity[^1].Title);
    }

    private static MainWindowViewModel CreateViewModel()
        => new(new WorkspaceViewModel(), new JobsViewModel(), new SessionsViewModel(), new ContextViewModel());
}
