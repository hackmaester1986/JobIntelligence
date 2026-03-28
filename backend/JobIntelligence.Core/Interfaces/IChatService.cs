namespace JobIntelligence.Core.Interfaces;

public interface IChatService
{
    Task<string> ChatAsync(List<(string Role, string Content)> history, bool? isUs = null, CancellationToken ct = default);
}
