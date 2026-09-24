using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace TrailGuard.Models;

public sealed class AddTrailInputModel
{
    [Required(ErrorMessage = "Trail name is required.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Location is required.")]
    public string Location { get; set; } = string.Empty;

    [Required(ErrorMessage = "Typical duration is required.")]
    [Range(typeof(decimal), "0", "79228162514264337593543950335", MinimumIsExclusive = true,
        ErrorMessage = "Typical duration must be greater than zero.")]
    public decimal? TypicalDurationHours { get; set; }

    [Required(ErrorMessage = "Distance is required.")]
    public double? DistanceKm { get; set; }

    [Required(ErrorMessage = "Elevation gain is required.")]
    [Range(0, int.MaxValue, ErrorMessage = "Elevation gain cannot be negative.")]
    public int? ElevationGainMeters { get; set; }

    [Required(ErrorMessage = "Select a technical trail class.")]
    [Range(1, 4, ErrorMessage = "Select a valid technical trail class.")]
    public int? TrailClass { get; set; }

    [Required(ErrorMessage = "Description is required.")]
    public string Description { get; set; } = string.Empty;

    public List<string> TerrainValues { get; set; } = [];

    public IFormFile? ThumbnailImage { get; set; }

    public List<IFormFile>? AdditionalImages { get; set; }
}
