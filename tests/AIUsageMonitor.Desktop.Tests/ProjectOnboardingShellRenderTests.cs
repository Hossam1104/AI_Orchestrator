using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Desktop.ViewModels;
using AIUsageMonitor.Domain.Common;

namespace AIUsageMonitor.Desktop.Tests;

/// <summary>
/// Guards the onboarding host binding in the live shell. The existing visual acceptance coverage
/// starts onboarding <em>before</em> the window is constructed, so the onboarding content binding is
/// evaluated once with a non-null value and a missing change notification stays invisible. A real
/// operator instead starts onboarding while the shell bindings are already live, which is what these
/// tests reproduce.
/// </summary>
[Collection("WPF visual acceptance")]
public sealed class ProjectOnboardingShellRenderTests
{
    private const string OnboardingHeading = "Register an existing workspace";
    private const string OnboardingBlockedText = "Finish or cancel the current onboarding flow first.";

    private readonly WpfRenderHarness _harness;

    public ProjectOnboardingShellRenderTests(WpfRenderHarness harness)
    {
        _harness = harness;
    }

    [Fact]
    public void AddExistingProject_StartedAfterShellBindingsAreLive_RendersOnboardingWizard()
    {
        _harness.Run(() =>
        {
            var projects = CreateProjectsViewModel();
            var viewModel = new MainWindowViewModel(new AiCapacityViewModel(), projects);
            var window = CreateShellWindow(viewModel);

            try
            {
                ShowAndLayout(window);

                // Chronology matters: the shell is on Mission Control with no onboarding yet.
                Assert.Null(projects.Onboarding);
                Assert.False(projects.IsOnboardingVisible);
                Assert.True(viewModel.CanAddExistingProject);
                Assert.Same(viewModel.MissionControl, viewModel.ActiveWorkspace);
                Assert.DoesNotContain(OnboardingHeading, VisibleText(window), StringComparison.OrdinalIgnoreCase);

                viewModel.AddExistingProjectCommand.Execute(null);
                Layout(window);

                // View-model truth.
                Assert.Same(projects, viewModel.ActiveWorkspace);
                Assert.True(viewModel.IsProjectsSelected);
                Assert.NotNull(projects.Onboarding);
                Assert.True(projects.IsOnboardingVisible);
                Assert.False(viewModel.CanAddExistingProject);
                Assert.Equal(OnboardingBlockedText, viewModel.AddExistingProjectStateText);

                // Rendered truth: the onboarding wizard must actually reach the visual tree.
                var host = FindOnboardingHost(window);
                Assert.NotNull(host);
                Assert.Same(projects.Onboarding, host!.Content);
                Assert.Contains(OnboardingHeading, VisibleText(window), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void CancelOnboarding_AfterShellBindingsAreLive_RestoresRegistrySurface()
    {
        _harness.Run(() =>
        {
            var projects = CreateProjectsViewModel();
            var viewModel = new MainWindowViewModel(new AiCapacityViewModel(), projects);
            var window = CreateShellWindow(viewModel);

            try
            {
                ShowAndLayout(window);
                viewModel.AddExistingProjectCommand.Execute(null);
                Layout(window);
                Assert.Contains(OnboardingHeading, VisibleText(window), StringComparison.OrdinalIgnoreCase);

                projects.Onboarding!.CancelCommand.Execute(null);
                Layout(window);

                Assert.Null(projects.Onboarding);
                Assert.False(projects.IsOnboardingVisible);
                Assert.True(viewModel.CanAddExistingProject);
                Assert.Equal(string.Empty, viewModel.AddExistingProjectStateText);

                var host = FindOnboardingHost(window);
                Assert.NotNull(host);
                Assert.Null(host!.Content);
                Assert.DoesNotContain(OnboardingHeading, VisibleText(window), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Close(window);
            }
        });
    }

    [Fact]
    public void ProjectsNavigation_DoesNotStartOnboarding()
    {
        _harness.Run(() =>
        {
            var projects = CreateProjectsViewModel();
            var viewModel = new MainWindowViewModel(new AiCapacityViewModel(), projects);
            var window = CreateShellWindow(viewModel);

            try
            {
                ShowAndLayout(window);

                viewModel.ShowProjectsCommand.Execute(null);
                Layout(window);

                Assert.Same(projects, viewModel.ActiveWorkspace);
                Assert.True(viewModel.IsProjectsSelected);
                Assert.False(viewModel.IsMissionControlSelected);
                Assert.Null(projects.Onboarding);
                Assert.True(viewModel.CanAddExistingProject);
                Assert.DoesNotContain(OnboardingHeading, VisibleText(window), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Close(window);
            }
        });
    }

    private static ProjectsViewModel CreateProjectsViewModel()
    {
        var viewModel = new ProjectsViewModel(
            new EmptyProjectRegistry(),
            repositoryStateService: null,
            new StubOnboardingService(),
            new DefaultAgentCatalog());
        viewModel.InitializeAsync().GetAwaiter().GetResult();
        return viewModel;
    }

    private static MainWindow CreateShellWindow(MainWindowViewModel viewModel)
    {
        var window = new MainWindow(viewModel)
        {
            Width = 1180,
            Height = 760,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Left = -10000,
            Top = -10000
        };
        window.SetPersistenceAvailability(true);
        return window;
    }

    private static void ShowAndLayout(Window window)
    {
        window.Show();
        Layout(window);
    }

    private static void Layout(Window window)
    {
        var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        root.Measure(new Size(window.Width, window.Height));
        root.Arrange(new Rect(0, 0, window.Width, window.Height));
        root.UpdateLayout();
    }

    private static void Close(Window window)
    {
        if (window.IsVisible)
        {
            window.Close();
        }
    }

    /// <summary>
    /// The onboarding host is the single content presenter whose binding target is the
    /// <see cref="ProjectsViewModel.Onboarding"/> property of the active projects workspace.
    /// </summary>
    private static ContentControl? FindOnboardingHost(Window window) =>
        FindVisualDescendants<ContentControl>(window)
            .FirstOrDefault(control =>
                control.GetBindingExpression(ContentControl.ContentProperty) is { } expression &&
                expression.ParentBinding.Path?.Path == nameof(ProjectsViewModel.Onboarding));

    private static string VisibleText(Window window) =>
        string.Join(
            "\n",
            FindVisualDescendants<TextBlock>(window)
                .Where(block => block.IsVisible)
                .Select(block => block.Text));

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed class EmptyProjectRegistry : IProjectRegistryService
    {
        public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Project>>([]);

        public Task<Project> CreateProjectAsync(ProjectEdit edit, CancellationToken cancellationToken = default) =>
            Task.FromException<Project>(new NotSupportedException());

        public Task<Project> UpdateProjectAsync(Guid projectId, ProjectEdit edit, CancellationToken cancellationToken = default) =>
            Task.FromException<Project>(new NotSupportedException());
    }

    private sealed class StubOnboardingService : IProjectOnboardingService
    {
        public Task<LocalRepositoryInspection> InspectRepositoryAsync(string localPath, CancellationToken cancellationToken = default) =>
            Task.FromException<LocalRepositoryInspection>(new NotSupportedException());

        public Task<ProjectOnboardingResult> CompleteAsync(ProjectOnboardingRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<ProjectOnboardingResult>(new NotSupportedException());
    }
}
