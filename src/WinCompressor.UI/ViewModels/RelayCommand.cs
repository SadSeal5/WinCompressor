using System.Windows.Input;

namespace WinCompressor.UI.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _execute();
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Predicate<T?>? _canExecute;

    public event EventHandler? CanExecuteChanged;

    public RelayCommand(Action<T?> execute, Predicate<T?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        if (parameter is null && typeof(T).IsValueType)
            return _canExecute?.Invoke(default) ?? true;

        if (parameter is null || parameter is T)
            return _canExecute?.Invoke((T?)parameter) ?? true;

        return false;
    }

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        if (parameter is null && typeof(T).IsValueType)
            _execute(default);
        else
            _execute((T?)parameter);
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting => _isExecuting;

    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            await _executeAsync();
        }
        catch (Exception ex)
        {
            // T4-N5 (inceleme turu 4): async void'dan kaçan istisna süreci düşürür;
            // komut gövdeleri kendi hatalarını yönetse de bu genel catch bir
            // sigortadır. Hata izlenebilirlik için Debug akışına yazılır.
            System.Diagnostics.Debug.WriteLine($"Unhandled async command exception: {ex}");
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _executeAsync;
    private readonly Predicate<T?>? _canExecute;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;

    public bool IsExecuting => _isExecuting;

    public AsyncRelayCommand(Func<T?, Task> executeAsync, Predicate<T?>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        if (_isExecuting)
            return false;

        if (parameter is null && typeof(T).IsValueType)
            return _canExecute?.Invoke(default) ?? true;

        if (parameter is null || parameter is T)
            return _canExecute?.Invoke((T?)parameter) ?? true;

        return false;
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        _isExecuting = true;
        RaiseCanExecuteChanged();

        try
        {
            T? arg = parameter is T val ? val : default;
            await _executeAsync(arg);
        }
        catch (Exception ex)
        {
            // T4-N5 (inceleme turu 4): async void süreci düşürmesin — genel sigorta.
            System.Diagnostics.Debug.WriteLine($"Unhandled async command exception: {ex}");
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
