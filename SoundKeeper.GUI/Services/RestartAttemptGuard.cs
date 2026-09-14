namespace SoundKeeper.GUI.Services;

public sealed class RestartAttemptGuard(int maximumAttempts = 3, TimeSpan? window = null)
{
    private readonly Queue<DateTimeOffset> _attempts = new();
    private readonly TimeSpan _window = window ?? TimeSpan.FromMinutes(1);

    public bool TryRegister(DateTimeOffset now)
    {
        while (_attempts.Count > 0 && now - _attempts.Peek() > _window) _attempts.Dequeue();
        if (_attempts.Count >= maximumAttempts) return false;
        _attempts.Enqueue(now);
        return true;
    }

    public void Reset() => _attempts.Clear();
}
