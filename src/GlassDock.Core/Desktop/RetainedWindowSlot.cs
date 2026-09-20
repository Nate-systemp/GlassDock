namespace GlassDock.Core.Desktop;

/// <summary>Keeps one live window-like instance and rejects stale close callbacks.</summary>
public sealed class RetainedWindowSlot<T> where T : class
{
    public T? Current { get; private set; }
    public bool IsShutdown { get; private set; }

    public T GetOrCreate(Func<T> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        if (IsShutdown)
            throw new InvalidOperationException("The retained window slot is shutting down.");

        return Current ??= create();
    }

    public T? BeginShutdown()
    {
        IsShutdown = true;
        var current = Current;
        Current = null;
        return current;
    }

    public bool Release(T instance)
    {
        if (!ReferenceEquals(Current, instance))
            return false;

        Current = null;
        return true;
    }
}
