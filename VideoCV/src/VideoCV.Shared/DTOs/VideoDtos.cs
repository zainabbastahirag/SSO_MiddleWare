namespace VideoCV.Shared.DTOs;

public class GenerateVideoRequest
{
    public string? Email { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? CvText { get; set; }
    public string TemplateName { get; set; } = "professional-dark";
}

public class VideoDto
{
    public Guid Id { get; set; }
    public string ShortId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Title { get; set; }
    public string? Email { get; set; }
    public string? Summary { get; set; }
    public string? Skills { get; set; }
    public string? Script { get; set; }
    public string? VideoUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string Status { get; set; } = "Pending";
    public string? ErrorMessage { get; set; }
    public int DurationSeconds { get; set; }
    public int ViewCount { get; set; }
    public string TemplateName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string ShareUrl { get; set; } = "";
}

public class VideoStatusDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = "";
    public string StatusMessage { get; set; } = "";
    public int ProgressPercent { get; set; }
    public string? VideoUrl { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ShareVideoRequest
{
    public List<string> Emails { get; set; } = new();
    public string? Message { get; set; }
}

public class VideoTemplateDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? PreviewImageUrl { get; set; }
    public string BackgroundColor { get; set; } = "";
    public string AccentColor { get; set; } = "";
    public bool IsPremium { get; set; }
}
