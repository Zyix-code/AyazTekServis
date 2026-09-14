using System.Collections.Concurrent;

namespace AyazTekServis.Services;

public class LoginAttemptService
{
    private readonly ConcurrentDictionary<string, List<DateTime>> _attempts = new();
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private const int MaxAttempts = 6;

    public bool IsBlocked(string key)
    {
        var list = _attempts.GetOrAdd(key, _ => new List<DateTime>());
        lock (list)
        {
            list.RemoveAll(x => x < DateTime.UtcNow - Window);
            return list.Count >= MaxAttempts;
        }
    }

    public void RegisterFailure(string key)
    {
        var list = _attempts.GetOrAdd(key, _ => new List<DateTime>());
        lock (list)
        {
            list.RemoveAll(x => x < DateTime.UtcNow - Window);
            list.Add(DateTime.UtcNow);
        }
    }

    public void Reset(string key) => _attempts.TryRemove(key, out _);
}
