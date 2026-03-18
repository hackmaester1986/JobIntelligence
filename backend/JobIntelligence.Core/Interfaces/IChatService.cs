namespace JobIntelligence.Core.Interfaces;

public interface IChatService
{
    Task<string> ChatAsync(List<(string Role, string Content)> history, CancellationToken ct = default);
}
