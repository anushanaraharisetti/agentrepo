namespace SiemensInterviewTest.Services;

public class InvoiceService
{
    // BUG: discount logic is wrong — stacks incorrectly
    // Missing: null check, XML docs, unit tests
    public decimal CalculateTotal(List<LineItem> items)
    {
        decimal total = 0;
        for (int i = 0; i < items.Count; i++)
        {
            total = total + items[i].Quantity * items[i].UnitPrice;
        }
        if (total > 10000)
        {
            total = total - (total * 0.1m);
        }
        if (total > 20000)
        {
            total = total - (total * 0.05m);
        }
        return total;
    }
}

public record LineItem(int Quantity, decimal UnitPrice);
