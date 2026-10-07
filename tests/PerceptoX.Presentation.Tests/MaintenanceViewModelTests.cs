using PerceptoX.Application.Maintenance;
using PerceptoX.Presentation.Services;
using PerceptoX.Presentation.ViewModels;

namespace PerceptoX.Presentation.Tests;

public sealed class MaintenanceViewModelTests
{
    [Fact]
    public async Task CancelledConfirmationNeverExecutesCleanup()
    {
        FakeMaintenance service = new();
        WorkspaceViewModel workspace = new();
        bool invalidated = false;
        using MaintenanceViewModel vm = new(service, new Confirmation(false), new Picker(), workspace,
            () => [], () => true, _ => invalidated = true);
        await vm.ClearCacheCommand.ExecuteAsync(null);
        Assert.False(service.Executed);
        Assert.False(invalidated);
        Assert.False(vm.IsBusy);
        Assert.Contains("anulată", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConfirmedResetInvalidatesResultsAndRefreshesStorage()
    {
        FakeMaintenance service = new();
        bool reset = false;
        using MaintenanceViewModel vm = new(service, new Confirmation(true), new Picker(), new(),
            () => [], () => true, removed => reset = removed);
        await vm.ResetIndexCommand.ExecuteAsync(null);
        Assert.True(service.Executed);
        Assert.True(reset);
        Assert.Contains("Index:", vm.StorageLabel, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkflowStartedDuringConfirmationPreventsCleanup()
    {
        FakeMaintenance service = new();
        bool idle = true;
        using MaintenanceViewModel vm = new(service, new Confirmation(true, () => idle = false), new Picker(), new(),
            () => [], () => idle, _ => { });
        await vm.ClearCacheCommand.ExecuteAsync(null);
        Assert.False(service.Executed);
        Assert.Contains("Așteptați", vm.StatusMessage, StringComparison.Ordinal);
        Assert.False(vm.ClearCacheCommand.CanExecute(null));
    }

    private sealed class FakeMaintenance : IMaintenanceService
    {
        public bool Executed { get; private set; }
        public Task<StorageUsage> MeasureAsync(MaintenanceRequest request, CancellationToken cancellationToken) => Task.FromResult(new StorageUsage(0, 0, 0));
        public Task<MaintenancePlan> PreviewAsync(MaintenanceRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new MaintenancePlan(Guid.NewGuid(), request, [new("generated.jpg", 100, 1)], [], [], DateTimeOffset.UtcNow));
        public Task<MaintenanceResult> ExecuteAsync(MaintenancePlan confirmedPlan, CancellationToken cancellationToken)
        { Executed = true; return Task.FromResult(new MaintenanceResult(1, 100, false, [], confirmedPlan.Request.Level == MaintenanceLevel.IndexAndCache)); }
    }
    private sealed class Confirmation(bool approve, Action? afterPrompt = null) : IConfirmationService
    {
        public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken)
        { afterPrompt?.Invoke(); return Task.FromResult(approve); }
    }
    private sealed class Picker : IPathPickerService
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<string?> PickImageAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
