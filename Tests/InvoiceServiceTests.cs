// ============================================================
//  InvoiceServiceTests — full coverage of CalculateTotal
//
//  Covers all scenarios the agent flagged as required:
//  ✅ Happy path (normal use)
//  ✅ Empty list
//  ✅ Negative values
//  ✅ Discount boundary conditions (exactly 10k, exactly 20k)
//  ✅ Tier 1 discount (> 10k, ≤ 20k)
//  ✅ Tier 2 discount (> 20k)
//  ✅ Null input
// ============================================================

using SiemensInterviewTest.Services;

namespace SiemensInterviewTest.Tests;

public class InvoiceServiceTests
{
    private readonly InvoiceService _sut = new();

    // ── Happy path ───────────────────────────────────────────
    [Fact]
    public void CalculateTotal_SingleItem_ReturnsCorrectTotal()
    {
        var items = new List<LineItem> { new(2, 50m) };
        Assert.Equal(100m, _sut.CalculateTotal(items));
    }

    [Fact]
    public void CalculateTotal_MultipleItems_SumsCorrectly()
    {
        var items = new List<LineItem>
        {
            new(3, 100m),   // 300
            new(2, 200m),   // 400
            new(1, 50m)     //  50
        };
        Assert.Equal(750m, _sut.CalculateTotal(items));
    }

    // ── Empty list ───────────────────────────────────────────
    [Fact]
    public void CalculateTotal_EmptyList_ReturnsZero()
    {
        Assert.Equal(0m, _sut.CalculateTotal([]));
    }

    // ── Null input ───────────────────────────────────────────
    [Fact]
    public void CalculateTotal_NullItems_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _sut.CalculateTotal(null!));
    }

    // ── Negative values ──────────────────────────────────────
    [Fact]
    public void CalculateTotal_NegativeQuantity_ThrowsArgumentException()
    {
        var items = new List<LineItem> { new(-1, 100m) };
        Assert.Throws<ArgumentException>(() => _sut.CalculateTotal(items));
    }

    [Fact]
    public void CalculateTotal_NegativeUnitPrice_ThrowsArgumentException()
    {
        var items = new List<LineItem> { new(1, -100m) };
        Assert.Throws<ArgumentException>(() => _sut.CalculateTotal(items));
    }

    // ── Discount boundary: exactly at thresholds (no discount) ──
    [Fact]
    public void CalculateTotal_ExactlyAtTier1Threshold_NoDiscount()
    {
        // Total = exactly 10,000 — discount only applies ABOVE threshold
        var items = new List<LineItem> { new(100, 100m) }; // = 10,000
        Assert.Equal(10_000m, _sut.CalculateTotal(items));
    }

    [Fact]
    public void CalculateTotal_ExactlyAtTier2Threshold_Tier1DiscountApplied()
    {
        // Total = exactly 20,000 — falls into tier 1 (> 10k, not > 20k)
        var items = new List<LineItem> { new(200, 100m) }; // = 20,000
        Assert.Equal(18_000m, _sut.CalculateTotal(items)); // 10% off
    }

    // ── Tier 1 discount: > 10,000 and ≤ 20,000 ─────────────
    [Fact]
    public void CalculateTotal_AboveTier1Threshold_Applies10PercentDiscount()
    {
        var items = new List<LineItem> { new(1, 15_000m) };
        Assert.Equal(13_500m, _sut.CalculateTotal(items)); // 15,000 - 10% = 13,500
    }

    // ── Tier 2 discount: > 20,000 ───────────────────────────
    [Fact]
    public void CalculateTotal_AboveTier2Threshold_Applies15PercentDiscount()
    {
        var items = new List<LineItem> { new(1, 25_000m) };
        Assert.Equal(21_250m, _sut.CalculateTotal(items)); // 25,000 - 15% = 21,250
    }

    [Fact]
    public void CalculateTotal_Tier2_DoesNotStackDiscounts()
    {
        // Key test: proves discounts are NOT cumulative (the original bug)
        // Old buggy code: 25,000 → -10% → 22,500 → -5% → 21,375  (WRONG)
        // Fixed code:     25,000 → -15% flat → 21,250              (CORRECT)
        var items = new List<LineItem> { new(1, 25_000m) };
        decimal result = _sut.CalculateTotal(items);
        Assert.Equal(21_250m, result);        // correct
        Assert.NotEqual(21_375m, result);     // explicitly reject the old buggy result
    }

    // ── Zero quantity edge case ──────────────────────────────
    [Fact]
    public void CalculateTotal_ZeroQuantityItem_ContributesNothing()
    {
        var items = new List<LineItem>
        {
            new(0, 9999m),   // zero qty — contributes 0
            new(1, 100m)     // contributes 100
        };
        Assert.Equal(100m, _sut.CalculateTotal(items));
    }
}
