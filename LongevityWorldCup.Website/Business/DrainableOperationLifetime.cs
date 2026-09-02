namespace LongevityWorldCup.Website.Business;

/// <summary>
/// Prevents unrelated operations once shutdown starts while allowing
/// descendants of an admitted logical operation to finish. The drain task
/// completes after every lease in those admitted operation trees has returned.
/// </summary>
internal sealed class DrainableOperationLifetime(string objectName)
{
    private readonly object _sync = new();
    private readonly string _objectName = objectName;
    private readonly AsyncLocal<OperationContext?> _currentOperation = new();
    private TaskCompletionSource? _drained;
    private int _activeOperations;
    private bool _stopping;

    public IDisposable Enter()
        => TryEnter() ?? throw new ObjectDisposedException(_objectName);

    public IDisposable? TryEnter()
    {
        var inheritedOperation = _currentOperation.Value;
        OperationContext operation;
        lock (_sync)
        {
            if (inheritedOperation is not null && inheritedOperation.ActiveLeases > 0)
            {
                operation = inheritedOperation;
            }
            else
            {
                if (_stopping)
                    return null;

                operation = new OperationContext();
            }

            operation.ActiveLeases++;
            _activeOperations++;
        }

        _currentOperation.Value = operation;
        return new OperationLease(this, operation, inheritedOperation);
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

    private void Exit(OperationContext operation, OperationContext? inheritedOperation)
    {
        _currentOperation.Value = inheritedOperation;
        TaskCompletionSource? drained = null;
        lock (_sync)
        {
            operation.ActiveLeases--;
            _activeOperations--;
            if (_stopping && _activeOperations == 0)
                drained = _drained;
        }

        drained?.TrySetResult();
    }

    private sealed class OperationContext
    {
        public int ActiveLeases { get; set; }
    }

    private sealed class OperationLease(
        DrainableOperationLifetime owner,
        OperationContext operation,
        OperationContext? inheritedOperation) : IDisposable
    {
        private DrainableOperationLifetime? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Exit(operation, inheritedOperation);
    }
}
