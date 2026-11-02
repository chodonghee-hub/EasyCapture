namespace EasyCapture.Core;

public sealed class SingleInstance : IDisposable
{
    public const string DefaultMutexName = @"Local\EasyCapture.SingleInstance";

    private Mutex? _mutex;

    public SingleInstance(string mutexName = DefaultMutexName)
    {
        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (createdNew)
        {
            _mutex = mutex;
            IsFirstInstance = true;
        }
        else
        {
            mutex.Dispose();
        }
    }

    public bool IsFirstInstance { get; }

    public void Dispose()
    {
        var mutex = _mutex;
        if (mutex is null)
        {
            return;
        }

        _mutex = null;
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 생성 스레드가 아닌 곳에서 해제되면 핸들 닫기만으로 충분하다.
        }

        mutex.Dispose();
    }
}