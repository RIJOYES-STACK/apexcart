namespace FlipkartBackend.Models;

public class Product
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal OriginalPrice { get; set; }
    public int DiscountPercentage { get; set; }
    public double Rating { get; set; }
    public int RatingCount { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int Stock { get; set; }
    public string Highlights { get; set; } = string.Empty; // Comma separated highlights
    public string Seller { get; set; } = string.Empty;
}
