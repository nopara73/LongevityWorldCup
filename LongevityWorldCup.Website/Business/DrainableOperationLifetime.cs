namespace LongevityWorldCup.Website.Business;

/// <summary>
/// Prevents new operations once shutdown starts and exposes a task that
/// completes after every operation admitted before shutdown has returned.
/// </summary>
internal sealed class DrainableOperationLifetime(string objectName)
{
    private readonly object _sync = new();
    private readonly string _objectName = objectName;
    private TaskCompletionSource? _drained;
    private int _activeOperations;
    private bool _stopping;

    public IDisposable Enter()
        => TryEnter() ?? throw new ObjectDisposedException(_objectName);

    public IDisposable? TryEnter()
    {
        lock (_sync)
        {
            if (_stopping)
                return null;

            _activeOperations++;
            return new OperationLease(this);
        }
    }

    public Task StopAndDrainAsync()
    {
        lock (_sync)
        {
            _stopping = true;
            if (_activeOperations == 0)
                return Task.CompletedTask;

            _drained ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _drained.Task;
        }
    }

    private void Exit()
    {
        TaskCompletionSource? drained = null;
        lock (_sync)
        {
            _activeOperations--;
            if (_stopping && _activeOperations == 0)
                drained = _drained;
        }

        drained?.TrySetResult();
    }

    private sealed class OperationLease(DrainableOperationLifetime owner) : IDisposable
    {
        private DrainableOperationLifetime? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Exit();
    }
}
