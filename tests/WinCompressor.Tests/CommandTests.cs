using WinCompressor.UI.ViewModels;

namespace WinCompressor.Tests;

public class CommandTests
{
    [Fact]
    public void RelayCommand_ExecutesAndRespectsCanExecute()
    {
        bool executed = false;
        bool canRun = false;

        var cmd = new RelayCommand(() => executed = true, () => canRun);

        Assert.False(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.False(executed);

        canRun = true;
        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);
        Assert.True(executed);
    }

    [Fact]
    public void RelayCommandT_PassesTypedParameter()
    {
        string received = string.Empty;
        var cmd = new RelayCommand<string>(param => received = param ?? string.Empty, p => !string.IsNullOrEmpty(p));

        Assert.False(cmd.CanExecute(null));
        Assert.True(cmd.CanExecute("test"));

        cmd.Execute("hello");
        Assert.Equal("hello", received);
    }

    [Fact]
    public async Task AsyncRelayCommand_ExecutesAndTracksExecutionState()
    {
        var tcs = new TaskCompletionSource();
        var cmd = new AsyncRelayCommand(async () =>
        {
            await tcs.Task;
        });

        Assert.True(cmd.CanExecute(null));
        cmd.Execute(null);

        Assert.True(cmd.IsExecuting);
        Assert.False(cmd.CanExecute(null));

        tcs.SetResult();
        await Task.Delay(50);

        Assert.False(cmd.IsExecuting);
        Assert.True(cmd.CanExecute(null));
    }
}
