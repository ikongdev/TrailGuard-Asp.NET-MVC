using System.ComponentModel.DataAnnotations;
using TrailGuard.Services;

namespace TrailGuard.Models;

public sealed class PickupPoint
{
    public int Id { get; set; }

    [Required, MaxLength(PickupPointCatalogHelper.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    // Stored to make PostgreSQL uniqueness deterministic and case-insensitive.
    [Required, MaxLength(PickupPointCatalogHelper.MaxNameLength)]
    public string NormalizedName { get; set; } = string.Empty;
}

public sealed class PickupPointMutationModel
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public sealed class PickupPointDeleteModel
{
    public int Id { get; set; }
}
