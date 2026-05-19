using Microsoft.EntityFrameworkCore;
using FlipkartBackend.Data;
using FlipkartBackend.Models;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// SQLite Database configuration
builder.Services.AddDbContext<FlipkartContext>(opt =>
    opt.UseSqlite("Data Source=flipkart.db"));

// Enable CORS for frontend integration
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseCors();

// Redirect root to API docs
app.MapGet("/", () => Results.Redirect("/scalar/v1"));

// Initialize & seed the database on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FlipkartContext>();
    db.Database.EnsureCreated();
    SeedData(db);
}

#region Category Endpoints

// GET all categories
app.MapGet("/api/categories", async (FlipkartContext db) =>
    await db.Categories.ToListAsync());

#endregion

#region Product Endpoints

// GET products (with optional search and category filters)
app.MapGet("/api/products", async (string? category, string? q, FlipkartContext db) =>
{
    var query = db.Products.AsQueryable();

    if (!string.IsNullOrEmpty(category))
    {
        query = query.Where(p => p.CategoryName.ToLower() == category.ToLower());
    }

    if (!string.IsNullOrEmpty(q))
    {
        query = query.Where(p => p.Title.ToLower().Contains(q.ToLower()) || 
                                 p.Description.ToLower().Contains(q.ToLower()));
    }

    return await query.ToListAsync();
});

// GET specific product by ID
app.MapGet("/api/products/{id}", async (int id, FlipkartContext db) =>
    await db.Products.FindAsync(id)
        is Product product
            ? Results.Ok(product)
            : Results.NotFound(new { Message = $"Product with ID {id} not found." }));

// POST a new product (Admin/Seller)
app.MapPost("/api/products", async (Product product, FlipkartContext db) =>
{
    db.Products.Add(product);
    await db.SaveChangesAsync();
    return Results.Created($"/api/products/{product.Id}", product);
});

#endregion

#region Cart Endpoints

// GET cart items
app.MapGet("/api/cart", async (FlipkartContext db) =>
    await db.CartItems.Include(c => c.Product).ToListAsync());

// POST add/update item in cart
app.MapPost("/api/cart", async (CartItemDto dto, FlipkartContext db) =>
{
    var product = await db.Products.FindAsync(dto.ProductId);
    if (product is null)
    {
        return Results.BadRequest(new { Message = "Product does not exist" });
    }

    var existingCartItem = await db.CartItems.FirstOrDefaultAsync(c => c.ProductId == dto.ProductId);
    if (existingCartItem != null)
    {
        existingCartItem.Quantity += dto.Quantity;
        if (existingCartItem.Quantity <= 0)
        {
            db.CartItems.Remove(existingCartItem);
        }
    }
    else if (dto.Quantity > 0)
    {
        var cartItem = new CartItem
        {
            ProductId = dto.ProductId,
            Quantity = dto.Quantity
        };
        db.CartItems.Add(cartItem);
    }

    await db.SaveChangesAsync();
    var updatedCart = await db.CartItems.Include(c => c.Product).ToListAsync();
    return Results.Ok(updatedCart);
});

// PUT update specific cart item quantity
app.MapPut("/api/cart/{productId}", async (int productId, int quantity, FlipkartContext db) =>
{
    var cartItem = await db.CartItems.FirstOrDefaultAsync(c => c.ProductId == productId);
    if (cartItem is null)
    {
        return Results.NotFound(new { Message = "Cart item not found" });
    }

    if (quantity <= 0)
    {
        db.CartItems.Remove(cartItem);
    }
    else
    {
        cartItem.Quantity = quantity;
    }

    await db.SaveChangesAsync();
    var updatedCart = await db.CartItems.Include(c => c.Product).ToListAsync();
    return Results.Ok(updatedCart);
});

// DELETE product from cart
app.MapDelete("/api/cart/{productId}", async (int productId, FlipkartContext db) =>
{
    var cartItem = await db.CartItems.FirstOrDefaultAsync(c => c.ProductId == productId);
    if (cartItem is null)
    {
        return Results.NotFound(new { Message = "Cart item not found" });
    }

    db.CartItems.Remove(cartItem);
    await db.SaveChangesAsync();
    
    var updatedCart = await db.CartItems.Include(c => c.Product).ToListAsync();
    return Results.Ok(updatedCart);
});

// DELETE clear cart
app.MapDelete("/api/cart/clear", async (FlipkartContext db) =>
{
    db.CartItems.RemoveRange(db.CartItems);
    await db.SaveChangesAsync();
    return Results.Ok(new List<CartItem>());
});

#endregion

#region Order Endpoints

// GET order history
app.MapGet("/api/orders", async (FlipkartContext db) =>
    await db.Orders.Include(o => o.Items).OrderByDescending(o => o.OrderDate).ToListAsync());

// POST place order (Checkout)
app.MapPost("/api/orders", async (OrderCreateDto dto, FlipkartContext db) =>
{
    var cartItems = await db.CartItems.Include(c => c.Product).ToListAsync();
    if (!cartItems.Any())
    {
        return Results.BadRequest(new { Message = "Cannot place order with an empty cart." });
    }

    decimal totalAmount = 0;
    var orderItems = new List<OrderItem>();

    foreach (var item in cartItems)
    {
        if (item.Product == null) continue;

        totalAmount += item.Product.Price * item.Quantity;
        orderItems.Add(new OrderItem
        {
            ProductId = item.ProductId,
            ProductTitle = item.Product.Title,
            Price = item.Product.Price,
            Quantity = item.Quantity,
            ImageUrl = item.Product.ImageUrl
        });

        // Deduct stock
        item.Product.Stock = Math.Max(0, item.Product.Stock - item.Quantity);
    }

    var order = new Order
    {
        OrderDate = DateTime.UtcNow,
        TotalAmount = totalAmount,
        CustomerName = dto.CustomerName,
        Address = dto.Address,
        Phone = dto.Phone,
        Items = orderItems
    };

    db.Orders.Add(order);
    
    // Clear cart after checkout
    db.CartItems.RemoveRange(cartItems);

    await db.SaveChangesAsync();

    return Results.Created($"/api/orders/{order.Id}", order);
});

#endregion

#region Manual Seeding Endpoint

app.MapPost("/api/seed", (FlipkartContext db) =>
{
    SeedData(db, force: true);
    return Results.Ok(new { Message = "Database successfully re-seeded." });
});

#endregion

app.Run();

// Helper method to seed initial data
void SeedData(FlipkartContext db, bool force = false)
{
    if (force)
    {
        db.CartItems.RemoveRange(db.CartItems);
        db.OrderItems.RemoveRange(db.OrderItems);
        db.Orders.RemoveRange(db.Orders);
        db.Products.RemoveRange(db.Products);
        db.Categories.RemoveRange(db.Categories);
        db.SaveChanges();
    }

    if (!db.Categories.Any())
    {
        var categories = new List<Category>
        {
            new() { Name = "Mobiles", ImageUrl = "https://images.unsplash.com/photo-1511707171634-5f897ff02aa9?w=100&auto=format&fit=crop&q=60" },
            new() { Name = "Electronics", ImageUrl = "https://images.unsplash.com/photo-1588872657578-7efd1f1555ed?w=100&auto=format&fit=crop&q=60" },
            new() { Name = "Fashion", ImageUrl = "https://images.unsplash.com/photo-1483985988355-763728e1935b?w=100&auto=format&fit=crop&q=60" },
            new() { Name = "Home", ImageUrl = "https://images.unsplash.com/photo-1524758631624-e2822e304c36?w=100&auto=format&fit=crop&q=60" },
            new() { Name = "Appliances", ImageUrl = "https://images.unsplash.com/photo-1584622650111-993a426fbf0a?w=100&auto=format&fit=crop&q=60" }
        };
        db.Categories.AddRange(categories);
        db.SaveChanges();
    }

    if (!db.Products.Any())
    {
        var products = new List<Product>
        {
            // Mobiles
            new() {
                Title = "iPhone 15 Pro (128 GB) - Natural Titanium",
                Description = "Forged in titanium and featuring the groundbreaking A17 Pro chip, a customizable Action button, and the most powerful iPhone camera system ever.",
                Price = 129900.00m,
                OriginalPrice = 134900.00m,
                DiscountPercentage = 3,
                Rating = 4.7,
                RatingCount = 1845,
                ImageUrl = "https://images.unsplash.com/photo-1695048133142-1a20484d2569?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Mobiles",
                Stock = 25,
                Highlights = "128 GB ROM,15.49 cm (6.1 inch) Super Retina XDR Display,48MP + 12MP + 12MP Camera,A17 Pro Chip Processor",
                Seller = "SuperCom Net"
            },
            new() {
                Title = "Samsung Galaxy S24 Ultra (512 GB) - Titanium Gray",
                Description = "Welcome to the era of mobile AI. With Galaxy S24 Ultra in your hands, you can unleash whole new levels of creativity, productivity and possibility.",
                Price = 139999.00m,
                OriginalPrice = 144999.00m,
                DiscountPercentage = 3,
                Rating = 4.8,
                RatingCount = 922,
                ImageUrl = "https://images.unsplash.com/photo-1610945265064-0e34e5519bbf?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Mobiles",
                Stock = 18,
                Highlights = "512 GB ROM,17.27 cm (6.8 inch) Quad HD+ Display,200MP + 50MP + 12MP + 10MP Camera,Snapdragon 8 Gen 3 Processor",
                Seller = "IndiFlashMart"
            },
            new() {
                Title = "Redmi Note 13 Pro 5G (256 GB) - Midnight Black",
                Description = "Redmi Note 13 Pro 5G features a 200MP camera with OIS, 1.5K 120Hz AMOLED display, and Snapdragon 7s Gen 2 for ultimate performance.",
                Price = 25999.00m,
                OriginalPrice = 29999.00m,
                DiscountPercentage = 13,
                Rating = 4.3,
                RatingCount = 12450,
                ImageUrl = "https://images.unsplash.com/photo-1598327105666-5b89351aff97?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Mobiles",
                Stock = 110,
                Highlights = "8 GB RAM | 256 GB ROM,16.94 cm (6.67 inch) Display,200MP + 8MP + 2MP Camera,5100 mAh Battery",
                Seller = "RetailNet"
            },
            // Electronics
            new() {
                Title = "ASUS ROG Strix G16 (2024) Intel Core i7 - RTX 4060",
                Description = "Draw more frames and win more games with the brand-new ROG Strix G16 and Windows 11 Home. Powered by up to an 13th Gen Intel Core i7 Processor.",
                Price = 114990.00m,
                OriginalPrice = 143990.00m,
                DiscountPercentage = 20,
                Rating = 4.6,
                RatingCount = 412,
                ImageUrl = "https://images.unsplash.com/photo-1603302576837-37561b2e2302?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Electronics",
                Stock = 12,
                Highlights = "Intel Core i7 Processor (13th Gen),16 GB DDR5 RAM | 512 GB SSD,6 GB Graphics | NVIDIA GeForce RTX 4060,40.64 cm (16 inch) Display",
                Seller = "ASUS Retail"
            },
            new() {
                Title = "Sony WH-1000XM5 Wireless Active Noise Cancelling Headphones",
                Description = "Our industry-leading noise-canceling headphones rewrite the rules of distraction-free listening and exceptional call clarity.",
                Price = 27990.00m,
                OriginalPrice = 34990.00m,
                DiscountPercentage = 20,
                Rating = 4.5,
                RatingCount = 8900,
                ImageUrl = "https://images.unsplash.com/photo-1505740420928-5e560c06d30e?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Electronics",
                Stock = 45,
                Highlights = "Active Noise Cancellation (ANC),30 Hours Battery Life,Dual Processor V1,Multipoint Connection",
                Seller = "Sony India"
            },
            new() {
                Title = "Apple iPad Air (5th Gen) 64 GB ROM Wi-Fi Only",
                Description = "iPad Air is powered by the groundbreaking Apple M1 chip. It delivers a massive performance boost to even the most demanding apps.",
                Price = 54900.00m,
                OriginalPrice = 59900.00m,
                DiscountPercentage = 8,
                Rating = 4.7,
                RatingCount = 3512,
                ImageUrl = "https://images.unsplash.com/photo-1544244015-0df4b3ffc6b0?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Electronics",
                Stock = 30,
                Highlights = "64 GB ROM,27.69 cm (10.9 inch) Display,12 MP Primary Camera | 12 MP Front,Apple M1 Chip",
                Seller = "SuperCom Net"
            },
            // Fashion
            new() {
                Title = "Men's Solid Slim Fit Casual Denim Shirt",
                Description = "A classic cotton denim shirt featuring a slim-fit cut, pointed collar, long sleeves, and double chest patch pockets.",
                Price = 899.00m,
                OriginalPrice = 1999.00m,
                DiscountPercentage = 55,
                Rating = 4.1,
                RatingCount = 34500,
                ImageUrl = "https://images.unsplash.com/photo-1501196354995-cbb51c65aaea?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Fashion",
                Stock = 200,
                Highlights = "Fabric: Pure Cotton,Pattern: Solid,Sleeve: Full Sleeve,Fit: Slim Fit",
                Seller = "Solly Denim"
            },
            new() {
                Title = "Women's Floral Print A-Line Dress",
                Description = "Turn heads at any occasion in this gorgeous A-line dress featuring vibrant floral print, lightweight georgette fabric, and comfortable lining.",
                Price = 1299.00m,
                OriginalPrice = 2999.00m,
                DiscountPercentage = 56,
                Rating = 4.2,
                RatingCount = 890,
                ImageUrl = "https://images.unsplash.com/photo-1595777457583-95e059d581b8?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Fashion",
                Stock = 75,
                Highlights = "Fabric: Georgette,Pattern: Floral Print,Style: A-Line,Length: Midi",
                Seller = "Harpa Fashion"
            },
            new() {
                Title = "Nike Air Max SYSTM Sneaker Shoes",
                Description = "Max looks, Max feel. The Air Max SYSTM brings back everything you love about your favorite '80s vibes.",
                Price = 6495.00m,
                OriginalPrice = 8495.00m,
                DiscountPercentage = 23,
                Rating = 4.4,
                RatingCount = 1240,
                ImageUrl = "https://images.unsplash.com/photo-1542291026-7eec264c27ff?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Fashion",
                Stock = 40,
                Highlights = "Type: Sneakers,Outer Material: Mesh,Sole: Rubber,Warranty: 6 Months",
                Seller = "Nike Retail"
            },
            // Home
            new() {
                Title = "Ergonomic High Back Mesh Office Chair",
                Description = "Work comfortably with our adjustable ergonomic office chair featuring breathable mesh, lumbar support, and heavy-duty nylon base.",
                Price = 4999.00m,
                OriginalPrice = 9999.00m,
                DiscountPercentage = 50,
                Rating = 4.3,
                RatingCount = 6500,
                ImageUrl = "https://images.unsplash.com/photo-1505797149-43b0069ec26b?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Home",
                Stock = 60,
                Highlights = "Frame Material: Nylon,Upholstery: Mesh,Adjustable Lumbar Support,Tilt Lock Mechanism",
                Seller = "Green Soul Store"
            },
            new() {
                Title = "100% Cotton Double Bed Sheet with 2 Pillow Covers",
                Description = "Upgrade your bedroom with this premium double bedsheet crafted from soft cotton, featuring traditional mandala prints and vibrant dyes.",
                Price = 699.00m,
                OriginalPrice = 1499.00m,
                DiscountPercentage = 53,
                Rating = 4.0,
                RatingCount = 28000,
                ImageUrl = "https://images.unsplash.com/photo-1616594039964-ae9021a400a0?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Home",
                Stock = 150,
                Highlights = "Size: Double Bed (228cm x 274cm),Material: Cotton,Thread Count: 144,Pack of 3 (1 Bedsheet, 2 Pillow Covers)",
                Seller = "Urban Space"
            },
            // Appliances
            new() {
                Title = "Mi 138 cm (55 inch) Ultra HD (4K) Smart LED TV",
                Description = "Bring home this Mi TV and enjoy an immersive, cinematic viewing experience. Features HDR10+, Dolby Audio, and PatchWall interface.",
                Price = 32999.00m,
                OriginalPrice = 49999.00m,
                DiscountPercentage = 34,
                Rating = 4.4,
                RatingCount = 45900,
                ImageUrl = "https://images.unsplash.com/photo-1593305841991-05c297ba4575?w=500&auto=format&fit=crop&q=80",
                CategoryName = "Appliances",
                Stock = 20,
                Highlights = "Ultra HD (4K) 3840 x 2160 Pixels,30 W Speaker Output,60 Hz Refresh Rate,3 x HDMI | 2 x USB",
                Seller = "Mi Homes"
            }
        };
        db.Products.AddRange(products);
        db.SaveChanges();
    }
}
