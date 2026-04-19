using JobIntelligence.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace JobIntelligence.API.Controllers;

[ApiController]
[Route("api/resumes")]
public class ResumeController(IResumeService resumeService, ILogger<ResumeController> logger) : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions = [".pdf", ".docx", ".txt"];
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

    [HttpPost("upload")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadResume(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        if (file.Length > MaxFileSizeBytes)
            return BadRequest(new { error = "File exceeds 10 MB limit." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { error = "Only PDF, DOCX, and TXT files are supported." });

        try
        {
            // Buffer the entire file into memory before passing to the service.
            // IFormFile.OpenReadStream() is backed by the request body; ZipArchive and PdfPig
            // both close the stream on dispose, which corrupts ASP.NET Core's request state.
            using var memoryStream = new MemoryStream((int)file.Length);
            await file.CopyToAsync(memoryStream, ct);
            memoryStream.Position = 0;

            var resume = await resumeService.ExtractAndEmbedFromFileAsync(memoryStream, file.FileName, ct);
            return Ok(new
            {
                resume.Id,
                resume.Name,
                resume.Email,
                resume.Location,
                resume.YearsOfExperience,
                resume.EducationLevel,
                resume.EducationField,
                resume.Skills,
                resume.RecentJobTitles,
                resume.Industries,
                resume.CreatedAt
            });
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process uploaded resume {FileName}", file.FileName);
            return StatusCode(500, new { error = "Failed to process resume." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> SubmitResume([FromBody] ResumeSubmitRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Resume text is required." });

        if (request.Text.Length > 50_000)
            return BadRequest(new { error = "Resume text exceeds 50,000 character limit." });

        try
        {
            var resume = await resumeService.ExtractAndEmbedAsync(request.Text, ct);

            return Ok(new
            {
                resume.Id,
                resume.Name,
                resume.Email,
                resume.Location,
                resume.YearsOfExperience,
                resume.EducationLevel,
                resume.EducationField,
                resume.Skills,
                resume.RecentJobTitles,
                resume.Industries,
                resume.CreatedAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process resume submission");
            return StatusCode(500, new { error = "Failed to process resume." });
        }
    }

    [HttpGet("{id:long}/matches")]
    public async Task<IActionResult> GetMatches(
        long id,
        [FromQuery] int limit = 20,
        [FromQuery] bool? isUs = null,
        CancellationToken ct = default)
    {
        if (limit is < 1 or > 100)
            return BadRequest(new { error = "limit must be between 1 and 100." });

        try
        {
            var matches = await resumeService.FindSimilarJobsAsync(id, limit, isUs, ct);

            if (matches.Count == 0)
                return NotFound(new { error = "Resume not found or has no embedding yet." });

            return Ok(new { resumeId = id, matches });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to find matches for resume {ResumeId}", id);
            return StatusCode(500, new { error = "Failed to find matching jobs." });
        }
    }
}

public record ResumeSubmitRequest(string Text);
