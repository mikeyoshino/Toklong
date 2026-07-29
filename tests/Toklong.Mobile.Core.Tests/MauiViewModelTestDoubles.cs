using System.Windows.Input;

public sealed class Command(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}

public sealed class Command<T>(Action<T?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) =>
        execute(parameter is null ? default : (T)parameter);
}

public static class Preferences
{
    public static PreferenceStore Default { get; } = new();
}

public sealed class PreferenceStore
{
    private readonly Dictionary<string, object?> values = [];

    public T Get<T>(string key, T defaultValue) =>
        values.TryGetValue(key, out var value) && value is T typed
            ? typed
            : defaultValue;

    public void Set<T>(string key, T value) =>
        values[key] = value;

    public void Clear() => values.Clear();
}

public sealed class Shell
{
    private static readonly AsyncLocal<Shell?> CurrentContext =
        new();

    public static Shell Current
    {
        get => CurrentContext.Value ??= new Shell();
        set => CurrentContext.Value = value;
    }

    public List<string> Routes { get; } = [];
    public List<(
        string Route,
        IReadOnlyDictionary<string, object> Parameters)>
        ParameterizedRoutes { get; } = [];
    public Func<string, Task>? Navigate { get; set; }

    public async Task GoToAsync(string route)
    {
        if (Navigate is not null)
            await Navigate(route);
        Routes.Add(route);
    }

    public async Task GoToAsync(
        string route,
        IDictionary<string, object> parameters)
    {
        if (Navigate is not null)
            await Navigate(route);
        Routes.Add(route);
        ParameterizedRoutes.Add((
            route,
            new Dictionary<string, object>(
                parameters)));
    }
}

namespace Toklong.Mobile.Pages
{
    public sealed class ChangeEmailPage;
    public sealed class CreateOfferPage;
    public sealed class PayoutSettingsPage;
    public sealed class TransactionDetailPage;
    public sealed class VerifyEmailChangePage;
}
