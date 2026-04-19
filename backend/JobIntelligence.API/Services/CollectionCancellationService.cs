namespace JobIntelligence.API.Services;

public class CollectionCancellationService
{
    private CancellationTokenSource? _cts;

    public CancellationToken StartNew()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    public void Complete()
    {
        _cts?.Dispose();
        _cts = null;
    }

    public bool IsRunning => _cts is { IsCancellationRequested: false };
}
