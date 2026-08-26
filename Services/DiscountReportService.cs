namespace PRReviewAgent.Services;

/// <summary>
/// Generates human-readable discount reports for invoice totals.
/// </summary>
public class DiscountReportService
{
    /// <summary>
    /// Produces a discount summary for a given pre-discount total.
    /// </summary>
    /// <param name="originalTotal">The total before any discount is applied.</param>
    /// <returns>A <see cref="DiscountReport"/> describing the discount applied.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="originalTotal"/> is negative.
    /// </exception>
    public DiscountReport GetReport(decimal originalTotal)
    {
        if (originalTotal < 0)
            throw new ArgumentOutOfRangeException(nameof(originalTotal), "Total must not be negative.");

        decimal rate = originalTotal switch
        {
            > 20_000m => 0.15m,
            > 10_000m => 0.10m,
            _         => 0.00m
        };

        decimal discountAmount = originalTotal * rate;
        decimal finalTotal     = originalTotal - discountAmount;

        return new DiscountReport(
            OriginalTotal  : originalTotal,
            DiscountRate   : rate,
            DiscountAmount : discountAmount,
            FinalTotal     : finalTotal,
            TierApplied    : rate switch
            {
                0.15m => "Tier 2 (>20k): 15% discount",
                0.10m => "Tier 1 (>10k): 10% discount",
                _     => "No discount applied"
            }
        );
    }
}

/// <summary>Represents the result of a discount calculation.</summary>
/// <param name="OriginalTotal">The pre-discount total.</param>
/// <param name="DiscountRate">The rate applied (0.0 = none, 0.10 = 10%, 0.15 = 15%).</param>
/// <param name="DiscountAmount">The monetary value of the discount.</param>
/// <param name="FinalTotal">The total after discount.</param>
/// <param name="TierApplied">Human-readable description of the tier applied.</param>
public record DiscountReport(
    decimal OriginalTotal,
    decimal DiscountRate,
    decimal DiscountAmount,
    decimal FinalTotal,
    string  TierApplied
);
