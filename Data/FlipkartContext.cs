using Microsoft.EntityFrameworkCore;
using FlipkartBackend.Models;

namespace FlipkartBackend.Data;

public class FlipkartContext : DbContext
{
    public FlipkartContext(DbContextOptions<FlipkartContext> options) : base(options)
    {
    }

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
}
