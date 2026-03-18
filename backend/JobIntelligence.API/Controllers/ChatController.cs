using JobIntelligence.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController(IChatService chatService) : ControllerBase
{
    public record ChatMessageDto(string Role, string Content);
    public record ChatRequestDto(List<ChatMessageDto> Messages);

    [HttpPost]
    public async Task<IActionResult> Chat([FromBody] ChatRequestDto request, CancellationToken ct)
    {
        var history = request.Messages.Select(m => (m.Role, m.Content)).ToList();
        var reply = await chatService.ChatAsync(history, ct);
        return Ok(new { reply });
    }
}
