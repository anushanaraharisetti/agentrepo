using SiemensInterviewTest.Services;

namespace SiemensInterviewTest.Tests;

public class DiscountReportServiceTests
{
    private readonly DiscountReportService _sut = new();

    [Fact]
    public void GetReport_BelowTier1_NoDiscount()
    {
        var report = _sut.GetReport(5_000m);
        Assert.Equal(0.00m, report.DiscountRate);
        Assert.Equal(5_000m, report.FinalTotal);
    }

    [Fact]
    public void GetReport_AboveTier1_Applies10Percent()
    {
        var report = _sut.GetReport(15_000m);
        Assert.Equal(0.10m, report.DiscountRate);
        Assert.Equal(13_500m, report.FinalTotal);
    }

    [Fact]
    public void GetReport_AboveTier2_Applies15Percent()
    {
        var report = _sut.GetReport(25_000m);
        Assert.Equal(0.15m, report.DiscountRate);
        Assert.Equal(21_250m, report.FinalTotal);
    }

    [Fact]
    public void GetReport_NegativeTotal_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _sut.GetReport(-1m));
    }

    [Fact]
    public void GetReport_Zero_NoDiscount()
    {
        var report = _sut.GetReport(0m);
        Assert.Equal(0m, report.FinalTotal);
        Assert.Equal("No discount applied", report.TierApplied);
    }
}
