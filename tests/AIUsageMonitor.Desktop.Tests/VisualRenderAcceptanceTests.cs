using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AIUsageMonitor.Application.Agents;
using AIUsageMonitor.Application.MissionControl;
using AIUsageMonitor.Application.Projects;
using AIUsageMonitor.Application.Providers;
using AIUsageMonitor.Application.Orchestration;
using AIUsageMonitor.Application.Validation;
using AIUsageMonitor.Application.Approvals;
using AIUsageMonitor.Desktop.ViewModels;
using AIUsageMonitor.Domain.Common;
using AIUsageMonitor.Domain.Providers;
using AIUsageMonitor.Domain.Quotas;
using AIUsageMonitor.Domain.Subscriptions;

namespace AIUsageMonitor.Desktop.Tests;

[Collection("WPF visual acceptance")]
public sealed class VisualRenderAcceptanceTests
{
    private static readonly DateTimeOffset EvidenceTime =
        new(2026, 9, 11, 9, 30, 0, TimeSpan.Zero);

    private static readonly Guid ProjectId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly WpfRenderHarness _harness;

    public VisualRenderAcceptanceTests(WpfRenderHarness harness)
    {
        _harness = harness;
    }

    [Fact]
    public void RequiredLightAndDarkRenders_AreDeterministicAndStructurallyValid()
    {
        _harness.Run(() =>
        {
            var evidenceDirectory = GetEvidenceDirectory();
            if (Directory.Exists(evidenceDirectory))
            {
                foreach (var artifact in Directory.EnumerateFiles(evidenceDirectory, "*.png"))
                {
                    File.Delete(artifact);
                }
            }
            Directory.CreateDirectory(evidenceDirectory);

            ThemeManager.Apply(ThemeVariant.Light);
            var lightNoProject = RenderShell(
                CreateShell(CreateMissionControlViewModel()),
                "light-mission-control-no-project",
                evidenceDirectory,
                "SELECT A PROJECT");
            var lightMissionControl = RenderShell(
                CreateShell(CreateMissionControlViewModel(CreateProject())),
                "light-mission-control-populated",
                evidenceDirectory,
                "APO Acceptance Workspace",
                "CURRENT WORK");
            var lightProjectsEmpty = RenderShell(
                CreateShell(CreateProjectsViewModel()),
                "light-projects-empty",
                evidenceDirectory,
                "NO PROJECTS YET");
            var lightProjectsPopulated = RenderShell(
                CreateShell(CreateProjectsViewModel(CreateProject())),
                "light-projects-populated",
                evidenceDirectory,
                "APO Acceptance Workspace",
                "PROJECT DETAILS");
            var lightAddExistingProject = RenderShell(
                CreateShell(CreateProjectsViewModel(CreateProject(), showNewProject: true)),
                "light-add-existing-project-dialog",
                evidenceDirectory,
                "ADD EXISTING PROJECT",
                "Choose the existing local folder");
            var lightExistingProjectPreview = RenderShell(
                CreateShell(CreateProjectsViewModel(CreateProject(), showNewProject: true, showPreview: true)),
                "light-existing-project-preview",
                evidenceDirectory,
                "WORKSPACE PREVIEW",
                "HEAD",
                "Remote provider");
            var lightCapacity = RenderShell(
                CreateShell(CreateCapacityViewModel()),
                "light-ai-capacity",
                evidenceDirectory,
                "AI PROVIDERS",
                "Claude",
                "52% remaining");
            var lightProviderDialog = RenderDialog(
                new ProviderConnectionEditorWindow(
                    new ProviderConnectionEditorViewModel(
                        ProviderCode.Copilot,
                        connection: null,
                        new TestConnectionService())),
                "light-provider-connection-dialog",
                evidenceDirectory,
                "PROVIDER SETTINGS",
                "GitHub Copilot settings",
                "Credential");
            var lightFriendlyError = RenderShell(
                CreateShell(CreateProjectsViewModel(failToLoad: true)),
                "light-friendly-error",
                evidenceDirectory,
                "Projects could not be loaded.");
            var lightComboBoxOpen = RenderComboBoxComposition(
                CreateShell(CreateMissionControlViewModel(CreateProject())),
            "light-combobox-open",
            evidenceDirectory);

            ThemeManager.Apply(ThemeVariant.Dark);
            var darkMissionControl = RenderShell(
                CreateShell(CreateMissionControlViewModel(CreateProject())),
                "dark-mission-control-populated",
                evidenceDirectory,
                "APO Acceptance Workspace",
                "CURRENT WORK");
            var darkProjects = RenderShell(
                CreateShell(CreateProjectsViewModel(CreateProject())),
                "dark-projects-populated",
                evidenceDirectory,
                "APO Acceptance Workspace",
                "PROJECT DETAILS");
            var darkCapacity = RenderShell(
                CreateShell(CreateCapacityViewModel()),
                "dark-ai-capacity",
                evidenceDirectory,
                "AI PROVIDERS",
                "Claude",
                "52% remaining");
            var darkAddExistingProject = RenderShell(
                CreateShell(CreateProjectsViewModel(CreateProject(), showNewProject: true)),
                "dark-add-existing-project-dialog",
                evidenceDirectory,
                "ADD EXISTING PROJECT",
                "Choose the existing local folder");
            var darkFriendlyError = RenderShell(
                CreateShell(CreateProjectsViewModel(failToLoad: true)),
                "dark-friendly-error",
                evidenceDirectory,
                "Projects could not be loaded.");

            AssertMateriallyDifferent(lightMissionControl, darkMissionControl);
            AssertMateriallyDifferent(lightProjectsPopulated, darkProjects);
            AssertMateriallyDifferent(lightCapacity, darkCapacity);
            AssertMateriallyDifferent(lightAddExistingProject, darkAddExistingProject);
            AssertMateriallyDifferent(lightFriendlyError, darkFriendlyError);

            File.WriteAllLines(
                Path.Combine(evidenceDirectory, "render-manifest.txt"),
                [
                    "APO-70 deterministic WPF render evidence",
                    $"Generated UTC: {EvidenceTime:O}",
                    "Strategy: structural/render invariants plus retained PNG evidence; no brittle golden-image comparison.",
                    "Light renders:",
                    lightNoProject.Path,
                    lightMissionControl.Path,
                    lightProjectsEmpty.Path,
                    lightProjectsPopulated.Path,
                    lightAddExistingProject.Path,
                    lightExistingProjectPreview.Path,
                    lightCapacity.Path,
                    lightProviderDialog.Path,
                    lightFriendlyError.Path,
                    lightComboBoxOpen.Path,
                    "Dark renders:",
                    darkMissionControl.Path,
                    darkProjects.Path,
                    darkCapacity.Path,
                    darkAddExistingProject.Path,
                    darkFriendlyError.Path
                ]);

            Assert.Equal(15, Directory.EnumerateFiles(evidenceDirectory, "*.png").Count());
            Assert.True(File.Exists(Path.Combine(evidenceDirectory, "render-manifest.txt")));
        });
    }

    private static string GetEvidenceDirectory() =>
        Path.Combine(Path.GetTempPath(), "AIUsageMonitor", "APO-70-visual-evidence");

    private static MainWindow CreateShell(object activeWorkspace)
    {
        var viewModel = activeWorkspace switch
        {
            MissionControlViewModel missionControl => new MainWindowViewModel(
                missionControl,
                CreateCapacityViewModel(),
                CreateProjectsViewModel()),
            ProjectsViewModel projects => new MainWindowViewModel(
                CreateMissionControlViewModel(),
                CreateCapacityViewModel(),
                projects),
            AiCapacityViewModel capacity => new MainWindowViewModel(
                CreateMissionControlViewModel(),
                capacity,
                CreateProjectsViewModel()),
            _ => throw new ArgumentException("Unsupported visual workspace.", nameof(activeWorkspace))
        };

        viewModel.ShowMissionControlCommand.Execute(null);
        if (activeWorkspace is ProjectsViewModel)
        {
            viewModel.ShowProjectsCommand.Execute(null);
        }
        else if (activeWorkspace is AiCapacityViewModel)
        {
            viewModel.ShowAiCapacityCommand.Execute(null);
        }

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

    private static MissionControlViewModel CreateMissionControlViewModel(Project? project = null)
    {
        var projects = project is null ? Array.Empty<Project>() : [project];
        return new MissionControlViewModel(
            new TestProjectRegistry(projects),
            new TestMissionControlReadModel());
    }

    private static ProjectsViewModel CreateProjectsViewModel(
        Project? project = null,
        bool showNewProject = false,
        bool failToLoad = false,
        bool showPreview = false)
    {
        var projects = project is null ? Array.Empty<Project>() : [project];
        var viewModel = new ProjectsViewModel(
            new TestProjectRegistry(projects, failToLoad),
            repositoryStateService: null,
            new TestOnboardingService(),
            new DefaultAgentCatalog());

        viewModel.InitializeAsync().GetAwaiter().GetResult();
        if (showNewProject)
        {
            viewModel.NewProjectCommand.Execute(null);
            if (showPreview && viewModel.Onboarding is { } onboarding)
            {
                onboarding.Name = "APO Acceptance Workspace";
                onboarding.LocalPath = "C:\\APO Acceptance Workspace";
                onboarding.NextCommand.Execute(null);
                onboarding.InspectRepositoryCommand.Execute(null);
                onboarding.AcceptRepositoryCommand.Execute(null);
            }
        }

        return viewModel;
    }

    private static AiCapacityViewModel CreateCapacityViewModel()
    {
        var viewModel = new AiCapacityViewModel();
        viewModel.Cards.Single(card => card.Code == ProviderCode.Claude).ApplyResult(
            ProviderRefreshResult.Partial(
                ProviderCode.Claude,
                account: null,
                subscription: CreateSubscription("Organization API"),
                [CreateQuota(52)],
                "Usage is available; reset timing is not reported.",
                EvidenceTime));
        viewModel.Cards.Single(card => card.Code == ProviderCode.Codex).ApplyResult(
            ProviderRefreshResult.Unsupported(ProviderCode.Codex, EvidenceTime));
        viewModel.Cards.Single(card => card.Code == ProviderCode.Antigravity).ApplyResult(
            ProviderRefreshResult.Unsupported(ProviderCode.Antigravity, EvidenceTime));
        var now = DateTimeOffset.UtcNow;
        viewModel.Cards.Add(new ProviderCapacityCardViewModel(
            ProviderDefinition.Custom(
                Guid.Parse("92ef0f49-9c90-4db1-a6e8-9bf2d80e89b8"),
                "Research API",
                "Custom registration",
                ProviderAuthenticationMode.ExternalManual,
                ProviderCapacityMode.Manual,
                enabled: true,
                sortOrder: 99,
                now,
                now,
                "Manual registration; no automatic capacity adapter is installed.")));
        return viewModel;
    }

    private static Project CreateProject() => new(
        ProjectId,
        "APO Acceptance Workspace",
        "C:\\APO Acceptance Workspace",
        "main",
        ProjectStatus.Active,
        EvidenceTime.AddHours(-2),
        EvidenceTime,
        repositoryProvider: "Local Git",
        repositoryUrl: null,
        repositoryId: "apo-acceptance-workspace",
        trackerType: "Manual",
        trackerId: "APO-70",
        governanceReferences: ["APO-70 acceptance"]);

    private static MissionControlSnapshot CreateSnapshot(Guid projectId) => new(
        projectId,
        EvidenceTime,
        MissionControlState.Review,
        "The project has fresh local evidence and is waiting for the next human review decision.",
        new(projectId, "APO Acceptance Workspace", ProjectStatus.Active, "C:\\APO Acceptance Workspace", EvidenceTime),
        new(
            IsAuthoritative: true,
            Reference: "APO-70",
            Title: "Visual and functional acceptance completion",
            CurrentStep: "Owner walkthrough and exact-head evidence",
            State: MissionControlState.Review,
            ObservedAt: EvidenceTime,
            ExecutionRunId: null,
            NextSafeAction: "Present the recovered UI to the owner."),
        new(
            [
                new("Planner", "GPT-5.6 Sol", true, AgentAvailability.Available, AgentAuthenticationState.Unknown, AgentEntitlementState.Unknown),
                new("Executor", "GPT-5.6 Luna xHigh", true, AgentAvailability.Available, AgentAuthenticationState.Unknown, AgentEntitlementState.Unknown)
            ],
            "Planner and executor assignments are recorded."),
        new(
            RepositorySelectionState.Inspect,
            RepositoryVerificationStatus.AvailableClean,
            "Repository verified — clean",
            "main",
            "C:\\APO Acceptance Workspace",
            "abc1234",
            "Working tree clean",
            "No local remote",
            EvidenceTime),
        new(
            TrackerReferenceState.ConfiguredUnverified,
            "Manual",
            "APO-70",
            "Reference recorded locally",
            "Tracker reference is configured."),
        new(
            ValidationGateDecisionState.Satisfied,
            "Focused and full validation evidence is available.",
            "tests/AIUsageMonitor.Desktop.Tests",
            EvidenceTime),
        new(
            HasEvidence: true,
            WorkflowState: "Review",
            Verdict: "Pending owner walkthrough",
            Severity: "Medium",
            BlockingFindingCount: 0,
            PendingAdjudicationCount: 0,
            OwnerAttentionRequired: true,
            OwnerAttentionReason: "Owner-visible recovered UI has not been explicitly accepted.",
            NextRequiredAction: "Present the light/dark walkthrough.",
            ObservedAt: EvidenceTime),
        new(
            HasEvidence: true,
            PendingCount: 0,
            StatusText: "No approval is currently pending.",
            TargetSummary: "APO-70 candidate",
            NextRequiredAction: "Await explicit owner response.",
            ObservedAt: EvidenceTime),
        new(
            HasExecutionEvidence: false,
            LatestStatus: null,
            StatusText: "No runtime evidence",
            ProcessEvidenceText: "No APO process evidence is stored.",
            ObservedAt: null,
            RunId: null),
        attentionItems:
        [
            new(
                MissionControlAttentionSeverity.Medium,
                MissionControlState.Review,
                "Owner walkthrough pending",
                "The candidate is technically validated but still needs explicit visual acceptance.",
                "APO-70",
                EvidenceTime,
                "Present the recovered UI to the owner.")
        ],
        limitations: [
            new(
                "Provider capacity",
                MissionControlLimitationKind.NotConfigured,
                "Some provider capacity surfaces remain manual or authentication-required.",
                "Provider adapters",
                EvidenceTime)
        ]);

    private static QuotaWindow CreateQuota(double remainingPercentage) => QuotaWindow.Create(
        "rolling-five-hour",
        QuotaType.Rolling5Hour,
        QuotaUnit.Requests,
        usedValue: 100 - remainingPercentage,
        remainingValue: remainingPercentage,
        limitValue: 100,
        usedPercentage: null,
        remainingPercentage,
        windowStart: EvidenceTime.AddHours(-1),
        resetAt: EvidenceTime.AddHours(4),
        DataSource.OfficialApi,
        ConfidenceLevel.Official,
        EvidenceTime);

    private static Subscription CreateSubscription(string planName) => new(
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        planName,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        DataSource.OfficialApi,
        ConfidenceLevel.Official,
        EvidenceTime);

    private static RenderEvidence RenderShell(
        MainWindow window,
        string name,
        string evidenceDirectory,
        params string[] expectedText)
    {
        return RenderWindow(window, name, evidenceDirectory, expectedText);
    }

    private static RenderEvidence RenderDialog(
        Window window,
        string name,
        string evidenceDirectory,
        params string[] expectedText)
    {
        window.Width = 540;
        window.Height = 680;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.Left = -10000;
        window.Top = -10000;
        return RenderWindow(window, name, evidenceDirectory, expectedText);
    }

    private static RenderEvidence RenderComboBoxComposition(
        MainWindow window,
        string name,
        string evidenceDirectory)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        root.Measure(new Size(window.Width, window.Height));
        root.Arrange(new Rect(0, 0, window.Width, window.Height));
        root.UpdateLayout();
        var comboBox = FindVisualDescendants<ComboBox>(window)
            .FirstOrDefault(value => value.Name == "" || value.Items.Count > 0);
        Assert.NotNull(comboBox);
        comboBox!.IsDropDownOpen = true;
        window.UpdateLayout();
        Assert.True(comboBox.IsDropDownOpen);
        return RenderWindow(
            window,
            name,
            evidenceDirectory,
            "APO Acceptance Workspace");
    }

    private static RenderEvidence RenderWindow(
        Window window,
        string name,
        string evidenceDirectory,
        params string[] expectedText)
    {
        try
        {
            if (!window.IsVisible)
            {
                window.Show();
            }

            var root = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            root.Measure(new Size(window.Width, window.Height));
            root.Arrange(new Rect(0, 0, window.Width, window.Height));
            root.UpdateLayout();

            Assert.True(root.ActualWidth > 0, $"{name} width must be non-zero.");
            Assert.True(root.ActualHeight > 0, $"{name} height must be non-zero.");
            var rootBackground = root switch
            {
                Panel panel => panel.Background,
                Border border => border.Background,
                Control control => control.Background,
                _ => null
            };
            Assert.NotNull(rootBackground);
            Assert.NotEqual(Colors.Transparent, (rootBackground as SolidColorBrush)?.Color);
            Assert.NotNull(System.Windows.Application.Current?.TryFindResource("ThemeVariant"));
            Assert.NotNull(System.Windows.Application.Current?.TryFindResource("CanvasColor"));
            Assert.NotNull(System.Windows.Application.Current?.TryFindResource("BrandSurfaceGradientBrush"));

            var text = string.Join("\n", FindVisualDescendants<TextBlock>(window).Select(block => block.Text));
            foreach (var expected in expectedText)
            {
                Assert.Contains(expected, text, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Contains(FindVisualDescendants<Border>(window), border => border.ActualWidth > 0 && border.ActualHeight > 0);
            Assert.Contains(FindVisualDescendants<Button>(window), button => button.ActualWidth > 0 && button.ActualHeight > 0);
            Assert.DoesNotContain("CAPACITY READY", text, StringComparison.OrdinalIgnoreCase);

            var pixelWidth = (int)Math.Ceiling(root.ActualWidth);
            var pixelHeight = (int)Math.Ceiling(root.ActualHeight);
            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);

            var pixels = new byte[pixelWidth * pixelHeight * 4];
            bitmap.CopyPixels(pixels, pixelWidth * 4, 0);
            Assert.Contains(pixels.Chunk(4), pixel => pixel[3] > 0);

            var path = Path.Combine(evidenceDirectory, $"{name}.png");
            using (var stream = File.Create(path))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }

            return new RenderEvidence(path, pixelWidth, pixelHeight, pixels);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
            }
        }
    }

    private static void AssertMateriallyDifferent(RenderEvidence light, RenderEvidence dark)
    {
        Assert.Equal(light.Width, dark.Width);
        Assert.Equal(light.Height, dark.Height);
        var changedPixels = 0;
        for (var index = 0; index < light.Pixels.Length; index += 4)
        {
            var channelDifference = Math.Abs(light.Pixels[index] - dark.Pixels[index])
                + Math.Abs(light.Pixels[index + 1] - dark.Pixels[index + 1])
                + Math.Abs(light.Pixels[index + 2] - dark.Pixels[index + 2]);
            if (channelDifference > 24)
            {
                changedPixels++;
            }
        }

        var totalPixels = light.Width * light.Height;
        Assert.True(
            changedPixels > totalPixels / 20,
            $"Expected light/dark renders to differ materially; changed pixels: {changedPixels}/{totalPixels}.");
    }

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

    private sealed record RenderEvidence(string Path, int Width, int Height, byte[] Pixels);

    private sealed class TestProjectRegistry : IProjectRegistryService
    {
        private readonly IReadOnlyList<Project> _projects;
        private readonly bool _failToLoad;

        public TestProjectRegistry(IReadOnlyList<Project> projects, bool failToLoad = false)
        {
            _projects = projects;
            _failToLoad = failToLoad;
        }

        public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
            _failToLoad
                ? Task.FromException<IReadOnlyList<Project>>(new IOException("synthetic render failure"))
                : Task.FromResult(_projects);

        public Task<Project> CreateProjectAsync(ProjectEdit edit, CancellationToken cancellationToken = default) =>
            Task.FromException<Project>(new NotSupportedException());

        public Task<Project> UpdateProjectAsync(Guid projectId, ProjectEdit edit, CancellationToken cancellationToken = default) =>
            Task.FromException<Project>(new NotSupportedException());
    }

    private sealed class TestMissionControlReadModel : IMissionControlReadModelService
    {
        public Task<MissionControlSnapshot> ReadAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateSnapshot(projectId));
    }

    private sealed class TestOnboardingService : IProjectOnboardingService
    {
        public Task<LocalRepositoryInspection> InspectRepositoryAsync(string localPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalRepositoryInspection(
                RepositoryVerificationStatus.AvailableClean,
                localPath,
                repositoryRoot: localPath,
                localPathIsRepositoryRoot: true,
                branchName: "main",
                headSha: "abcdef1234567890",
                headShortSha: "abcdef1",
                isClean: true,
                remotes: [new RepositoryRemote("origin", "https://github.com/example/workspace.git")],
                workspace: new ProjectWorkspaceDiscovery(
                    localPath,
                    "APO Acceptance Workspace",
                    exists: true,
                    isReadable: true,
                    governanceFiles: new Dictionary<string, WorkspaceFileState>
                    {
                        ["AGENTS.md"] = WorkspaceFileState.Present,
                        ["CLAUDE.md"] = WorkspaceFileState.Missing
                    },
                    projectFiles: ["AIUsageMonitor.sln"],
                    remoteProvider: "GitHub")));

        public Task<ProjectOnboardingResult> CompleteAsync(ProjectOnboardingRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException<ProjectOnboardingResult>(new NotSupportedException());
    }

    private sealed class TestConnectionService : IProviderConnectionService
    {
        public Task<ProviderConnection?> GetAsync(ProviderCode code, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderConnection?>(null);

        public Task<IReadOnlyList<ProviderConnection>> LoadAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProviderConnection>>([]);

        public Task<ProviderConnection> SaveAsync(ProviderConnectionEdit edit, CancellationToken cancellationToken = default) =>
            Task.FromException<ProviderConnection>(new NotSupportedException());

        public Task<ProviderConnection?> RecordRefreshAsync(ProviderRefreshResult result, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderConnection?>(null);
    }
}

[CollectionDefinition("WPF visual acceptance", DisableParallelization = true)]
public sealed class WpfVisualAcceptanceCollection : ICollectionFixture<WpfRenderHarness>
{
}
