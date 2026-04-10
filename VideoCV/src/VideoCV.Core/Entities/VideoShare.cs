namespace VideoCV.Core.Entities;

public class VideoShare
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VideoId { get; set; }
    public virtual Video? Video { get; set; }
    public string SharedWithEmail { get; set; } = "";
    public string? SharedWithName { get; set; }
    public string? Message { get; set; }
    public int ViewCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ViewedAt { get; set; }
}
