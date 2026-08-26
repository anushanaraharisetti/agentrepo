namespace SiemensInterviewTest.Services;

/// <summary>
/// Provides invoice calculation services including tiered discount logic.
/// </summary>
public class InvoiceService
{
    // Discount thresholds and rates as named constants — no magic numbers
    private const decimal Tier1Threshold = 10_000m;
    private const decimal Tier2Threshold = 20_000m;
    private const decimal Tier1Discount  = 0.10m;  // 10%
    private const decimal Tier2Discount  = 0.15m;  // 15% flat (not cumulative)

    /// <summary>
    /// Calculates the total price of all line items with tiered discount applied.
    /// </summary>
    /// <remarks>
    /// Discount tiers (applied exclusively, not cumulatively):
    /// <list type="bullet">
    ///   <item>Orders &gt; 20,000 → 15% discount on total</item>
    ///   <item>Orders &gt; 10,000 → 10% discount on total</item>
    ///   <item>All other orders  → no discount</item>
    /// </list>
    /// </remarks>
    /// <param name="items">The list of line items to total. Must not be null.</param>
    /// <returns>The discounted total as a decimal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when any item has a negative quantity or unit price.</exception>
    public decimal CalculateTotal(List<LineItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (items.Any(i => i.Quantity < 0 || i.UnitPrice < 0))
            throw new ArgumentException("Items must not have negative quantity or unit price.", nameof(items));

        // LINQ is cleaner and more idiomatic than a manual for-loop
        decimal total = items.Sum(item => item.Quantity * item.UnitPrice);

        // Tiered discounts — mutually exclusive, highest tier wins
        return total switch
        {
            > Tier2Threshold => total * (1 - Tier2Discount),   // 15% off
            > Tier1Threshold => total * (1 - Tier1Discount),   // 10% off
            _                => total                           // no discount
        };
    }
}

/// <summary>Represents a single line item on an invoice.</summary>
/// <param name="Quantity">Number of units ordered. Must be non-negative.</param>
/// <param name="UnitPrice">Price per unit. Must be non-negative.</param>
public record LineItem(int Quantity, decimal UnitPrice);
