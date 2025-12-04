using System.ComponentModel.DataAnnotations;

namespace eCommerce.Inventory.Application.DTOs;

public class CreatePendingListingDto
{
    [Required]
    public int BlueprintId { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1")]
    public int Quantity { get; set; }

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "Price must be non-negative")]
    public decimal Price { get; set; } // Maps to SellingPrice

    [Required]
    public string Condition { get; set; } = string.Empty;

    [Required]
    public string Language { get; set; } = string.Empty;

    public bool IsFoil { get; set; }

    public bool IsSigned { get; set; }

    public string? Location { get; set; }

    public string? Tag { get; set; }

    public decimal PurchasePrice { get; set; }

    // Optional AI Grading data
    public decimal? GradingScore { get; set; }
    public string? GradingConditionCode { get; set; }
    public decimal? GradingCentering { get; set; }
    public decimal? GradingCorners { get; set; }
    public decimal? GradingEdges { get; set; }
    public decimal? GradingSurface { get; set; }
    public decimal? GradingConfidence { get; set; }
    public int? GradingImagesCount { get; set; }
}
