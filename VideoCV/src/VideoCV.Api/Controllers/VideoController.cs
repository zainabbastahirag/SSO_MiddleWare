using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VideoCV.Core.Data;
using VideoCV.Core.Entities;
using VideoCV.Shared.DTOs;

namespace VideoCV.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideoController : ControllerBase
{
    private readonly VideoCvDbContext _db;
    private readonly ILogger<VideoController> _logger;

    public VideoController(VideoCvDbContext db, ILogger<VideoController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("generate")]
    public async Task<ActionResult<VideoDto>> Generate([FromBody] GenerateVideoRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CvText) && string.IsNullOrWhiteSpace(request.LinkedInUrl))
            return BadRequest(new { message = "Provide either CV text or LinkedIn URL" });

        var video = new Video
        {
            Email = request.Email,
            Name = ExtractNameFromCv(request.CvText) ?? "Professional",
            Title = ExtractTitleFromCv(request.CvText),
            LinkedInUrl = request.LinkedInUrl,
            Summary = request.CvText?.Length > 500 ? request.CvText[..500] : request.CvText,
            TemplateName = request.TemplateName,
            Status = VideoStatus.ParsingCV
        };

        _db.Videos.Add(video);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Video generation started: {Id}", video.Id);

        // TODO: Queue background job for AI pipeline
        // For now, simulate completion
        video.Script = GenerateSampleScript(video);
        video.Status = VideoStatus.Completed;
        video.DurationSeconds = 90;
        video.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetVideo), new { id = video.Id }, MapToDto(video));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VideoDto>> GetVideo(Guid id)
    {
        var video = await _db.Videos.FindAsync(id);
        if (video == null) return NotFound();
        return Ok(MapToDto(video));
    }

    [HttpGet("{id:guid}/status")]
    public async Task<ActionResult<VideoStatusDto>> GetStatus(Guid id)
    {
        var video = await _db.Videos.FindAsync(id);
        if (video == null) return NotFound();

        return Ok(new VideoStatusDto
        {
            Id = video.Id,
            Status = video.Status.ToString(),
            StatusMessage = video.Status switch
            {
                VideoStatus.Pending => "Waiting in queue...",
                VideoStatus.ParsingCV => "Analyzing your CV...",
                VideoStatus.GeneratingScript => "Writing your introduction script...",
                VideoStatus.GeneratingAudio => "Recording voiceover...",
                VideoStatus.AssemblingVideo => "Assembling your video...",
                VideoStatus.Completed => "Your video is ready!",
                VideoStatus.Failed => video.ErrorMessage ?? "Something went wrong",
                _ => "Processing..."
            },
            ProgressPercent = video.Status switch
            {
                VideoStatus.Pending => 0,
                VideoStatus.ParsingCV => 20,
                VideoStatus.GeneratingScript => 40,
                VideoStatus.GeneratingAudio => 60,
                VideoStatus.AssemblingVideo => 80,
                VideoStatus.Completed => 100,
                _ => 0
            },
            VideoUrl = video.Status == VideoStatus.Completed ? $"/api/video/{video.Id}/stream" : null,
            ErrorMessage = video.ErrorMessage
        });
    }

    [HttpGet("s/{shortId}")]
    public async Task<ActionResult<VideoDto>> GetByShortId(string shortId)
    {
        var video = await _db.Videos.FirstOrDefaultAsync(v => v.ShortId == shortId);
        if (video == null) return NotFound();

        video.ViewCount++;
        await _db.SaveChangesAsync();

        return Ok(MapToDto(video));
    }

    [HttpPost("{id:guid}/share")]
    public async Task<IActionResult> Share(Guid id, [FromBody] ShareVideoRequest request)
    {
        var video = await _db.Videos.FindAsync(id);
        if (video == null) return NotFound();

        foreach (var email in request.Emails.Where(e => !string.IsNullOrWhiteSpace(e)))
        {
            _db.VideoShares.Add(new VideoShare
            {
                VideoId = id,
                SharedWithEmail = email.Trim(),
                Message = request.Message
            });
        }

        await _db.SaveChangesAsync();
        return Ok(new { shared = request.Emails.Count, shareUrl = $"/v/{video.ShortId}" });
    }

    [HttpGet("templates")]
    public async Task<ActionResult<List<VideoTemplateDto>>> GetTemplates()
    {
        var templates = await _db.VideoTemplates
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .Select(t => new VideoTemplateDto
            {
                Id = t.Id,
                Name = t.Name,
                DisplayName = t.DisplayName,
                Description = t.Description,
                BackgroundColor = t.BackgroundColor,
                AccentColor = t.AccentColor,
                IsPremium = t.IsPremium
            })
            .ToListAsync();

        return Ok(templates);
    }

    [HttpGet("recent")]
    public async Task<ActionResult<List<VideoDto>>> GetRecent([FromQuery] int count = 6)
    {
        var videos = await _db.Videos
            .Where(v => v.Status == VideoStatus.Completed)
            .OrderByDescending(v => v.CreatedAt)
            .Take(count)
            .ToListAsync();

        return Ok(videos.Select(MapToDto).ToList());
    }

    private VideoDto MapToDto(Video v) => new()
    {
        Id = v.Id,
        ShortId = v.ShortId,
        Name = v.Name,
        Title = v.Title,
        Email = v.Email,
        Summary = v.Summary,
        Skills = v.Skills,
        Script = v.Script,
        VideoUrl = v.Status == VideoStatus.Completed ? $"/api/video/{v.Id}/stream" : null,
        ThumbnailUrl = v.ThumbnailPath,
        Status = v.Status.ToString(),
        ErrorMessage = v.ErrorMessage,
        DurationSeconds = v.DurationSeconds,
        ViewCount = v.ViewCount,
        TemplateName = v.TemplateName,
        CreatedAt = v.CreatedAt,
        CompletedAt = v.CompletedAt,
        ShareUrl = $"/v/{v.ShortId}"
    };

    private static string? ExtractNameFromCv(string? cv)
    {
        if (string.IsNullOrWhiteSpace(cv)) return null;
        var firstLine = cv.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return firstLine?.Length > 50 ? firstLine[..50] : firstLine;
    }

    private static string? ExtractTitleFromCv(string? cv)
    {
        if (string.IsNullOrWhiteSpace(cv)) return null;
        var lines = cv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 1 ? lines[1].Trim() : null;
    }

    private static string GenerateSampleScript(Video video)
    {
        return $"""
        Hi, I'm {video.Name}.

        {(string.IsNullOrEmpty(video.Title) ? "" : $"I'm a {video.Title}.")}

        {(string.IsNullOrEmpty(video.Summary) ? "I'm a passionate professional looking for new opportunities." : video.Summary)}

        I bring a unique combination of technical skills and collaborative experience.
        I'm excited about opportunities where I can make a real impact.

        Thank you for watching. I'd love to connect and discuss how I can contribute to your team.
        """;
    }
}
