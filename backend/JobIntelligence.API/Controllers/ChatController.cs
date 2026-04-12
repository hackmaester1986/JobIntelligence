using JobIntelligence.Core.Entities;
using JobIntelligence.Core.Interfaces;
using JobIntelligence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(IChatService chatService, IServiceScopeFactory scopeFactory) : ControllerBase
{
    public record ChatMessageDto(string Role, string Content);
    public record ChatRequestDto(List<ChatMessageDto> Messages, bool? IsUs = null);

    [HttpPost]
    [EnableRateLimiting("chat-per-ip")]
    public async Task<IActionResult> Chat([FromBody] ChatRequestDto request, CancellationToken ct)
    {
        var ip = Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
                 ?? HttpContext.Connection.RemoteIpAddress?.ToString()
                 ?? "unknown";

        var history = request.Messages.Select(m => (m.Role, m.Content)).ToList();
        var reply = await chatService.ChatAsync(history, request.IsUs, ct);

        var userMessage = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.ChatLogs.Add(new ChatLog
                {
                    IpAddress = ip,
                    UserMessage = userMessage,
                    AssistantReply = reply,
                    IsUs = request.IsUs,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }
            catch { /* don't let logging errors affect the response */ }
        });

        return Ok(new { reply });
    }
}
