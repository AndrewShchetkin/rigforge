namespace RigForge.Host.Daemon.Hosting;

/// <summary>
/// Session-local mutex so a second <c>RigForge.Daemon</c> in the same logon session exits
/// instead of fighting over USB HID, SMBus, and port 5757.
/// </summary>
public sealed class SingleInstanceMutex : IDisposable
{
    public const string Name = @"Local\RigForge.Daemon.SingleInstance";

    private readonly Mutex _mutex;
    private bool _owned;
    private bool _disposed;

    public SingleInstanceMutex()
    {
        _mutex = new Mutex(initiallyOwned: false, Name);
        try
        {
            _owned = _mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            _owned = true;
        }
    }

    public bool IsFirstInstance => _owned;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_owned)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _owned = false;
        }

        _mutex.Dispose();
    }
}
