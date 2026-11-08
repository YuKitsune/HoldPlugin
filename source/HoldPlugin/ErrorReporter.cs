using vatsys;

namespace HoldPlugin;

public interface IErrorReporter
{
    void ReportError(string message);
    void ReportError(Exception exception);
}

/// <summary>
///     Shows errors in the vatSys error window. Shows each error message one time each minute
///     at most, so that the same error does not flood the error window. You can use this class
///     from all threads.
/// </summary>
public class ErrorReporter : IErrorReporter
{
    static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(1);

    readonly Dictionary<string, DateTimeOffset> _lastShownTimes = new();

    public void ReportError(string message)
    {
        ReportError(new Exception(message));
    }

    public void ReportError(Exception exception)
    {
        lock (_lastShownTimes)
        {
            if (_lastShownTimes.TryGetValue(exception.Message, out var lastShown) &&
                DateTimeOffset.Now - lastShown <= RepeatInterval)
                return;

            Errors.Add(exception, Plugin.Name);
            _lastShownTimes[exception.Message] = DateTimeOffset.Now;
        }
    }
}
