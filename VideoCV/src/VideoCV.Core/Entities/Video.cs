namespace VideoCV.Core.Entities;

public class Video
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ShortId { get; set; } = GenerateShortId();
    public string? Email { get; set; }
    public string Name { get; set; } = "";
    public string? Title { get; set; }
    public string? Summary { get; set; }
    public string? Skills { get; set; }
    public string? Experience { get; set; }
    public string? Education { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? CvFileName { get; set; }
    public string? Script { get; set; }
    public string? AudioPath { get; set; }
    public string? VideoPath { get; set; }
    public string? ThumbnailPath { get; set; }
    public VideoStatus Status { get; set; } = VideoStatus.Pending;
    public string? ErrorMessage { get; set; }
    public int DurationSeconds { get; set; }
    public int ViewCount { get; set; }
    public string TemplateName { get; set; } = "professional-dark";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public virtual ICollection<VideoShare> Shares { get; set; } = new List<VideoShare>();

    private static string GenerateShortId()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("/", "_").Replace("+", "-")[..8];
    }
}

public enum VideoStatus
{
    Pending = 0,
    ParsingCV = 1,
    GeneratingScript = 2,
    GeneratingAudio = 3,
    AssemblingVideo = 4,
    Completed = 5,
    Failed = -1
}
