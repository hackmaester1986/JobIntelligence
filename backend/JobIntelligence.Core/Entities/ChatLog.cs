namespace JobIntelligence.Core.Entities;

public class ChatLog
{
    public long Id { get; set; }
    public string IpAddress { get; set; } = null!;
    public string UserMessage { get; set; } = null!;
    public string? AssistantReply { get; set; }
    public bool? IsUs { get; set; }
    public DateTime CreatedAt { get; set; }
}
