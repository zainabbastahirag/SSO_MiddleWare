namespace VideoCV.Core.Entities;

public class VideoTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string? PreviewImagePath { get; set; }
    public string BackgroundColor { get; set; } = "#0f172a";
    public string AccentColor { get; set; } = "#3b82f6";
    public string FontFamily { get; set; } = "Inter";
    public bool IsActive { get; set; } = true;
    public bool IsPremium { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
