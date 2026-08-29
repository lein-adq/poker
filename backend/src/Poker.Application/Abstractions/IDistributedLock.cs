namespace Poker.Application.Abstractions;

/// <summary>Acquires an exclusive lock for the duration of the returned disposable, releasing on dispose.</summary>
public interface IDistributedLock
{
    Task<IAsyncDisposable> AcquireAsync(string key, TimeSpan timeout);
}
