using Serilog;
using Microsoft.EntityFrameworkCore;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Persistence.Repositories;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Mappers;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using MediatR;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/taffel-inventory-.txt", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

// Configure as Windows Service
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "eCommerce.Inventory";
});

// Configure Kestrel to listen on port 5152
builder.WebHost.UseUrls("http://localhost:5152");

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure DbContext with SQL Server
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
        sqlOptions.MigrationsAssembly("eCommerce.Inventory.Infrastructure")));

// Register repositories
builder.Services.AddScoped<IInventoryItemRepository, InventoryItemRepository>();
builder.Services.AddScoped<IBlueprintRepository, BlueprintRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// Register DbContext as IApplicationDbContext for dependency injection
builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

// Register Card Trader mappers and sync services
builder.Services.AddScoped<CardTraderDtoMapper>();
builder.Services.AddScoped<InventorySyncService>();
builder.Services.AddScoped<WebhookSignatureVerificationService>();
builder.Services.AddScoped<CardTraderSyncOrchestrator>();
builder.Services.AddScoped<INotificationService, eCommerce.Inventory.Api.Services.SignalRNotificationService>();

// Register MediatR for CQRS command handling
builder.Services.AddMediatR(config =>
    config.RegisterServicesFromAssembly(typeof(eCommerce.Inventory.Application.Commands.ProcessCardTraderWebhookCommand).Assembly));

// Configure HttpClient for Card Trader API with Bearer Token authentication
var cardTraderApiConfig = builder.Configuration.GetSection("CardTraderApi");
var bearerToken = cardTraderApiConfig["BearerToken"];
var baseUrl = cardTraderApiConfig["BaseUrl"];
if (string.IsNullOrEmpty(baseUrl))
{
    throw new InvalidOperationException("CardTrader API BaseUrl is missing in configuration.");
}

builder.Services.AddHttpClient<ICardTraderApiService, CardTraderApiClient>(client =>
{
    // Ensure BaseAddress ends with / for proper relative URL concatenation
    var baseAddressUrl = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
    client.BaseAddress = new Uri(baseAddressUrl);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Register Card Trader background sync worker
// NOTE: Temporarily disabled for development. Enable once Card Trader API is properly configured.
// builder.Services.AddHostedService<CardTraderSyncWorker>();
builder.Services.AddHostedService<eCommerce.Inventory.Infrastructure.BackgroundJobs.ScheduledProductSyncWorker>();

// Register SignalR
builder.Services.AddSignalR();

// Add CORS if needed for future frontend integration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Apply migrations and seed data in development
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            // Apply pending migrations
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully");

            // Seed initial data
            // #Alex - Seeding is currently commented out. Uncomment and implement seed data as needed.
            // await eCommerce.Inventory.Infrastructure.Persistence.SeedData.InitializeAsync(dbContext, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying migrations or seeding data");
            throw;
        }
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Only use HTTPS redirect in production
    app.UseHttpsRedirection();
}

// Use CORS before authorization
app.UseCors("AllowAll");

// Use Serilog middleware for request/response logging
app.UseSerilogRequestLogging();

// Use Global Exception Middleware (AFTER logging, BEFORE authorization)
app.UseMiddleware<eCommerce.Inventory.Api.Middleware.GlobalExceptionMiddleware>();

app.UseAuthorization();

app.MapControllers();
app.MapHub<eCommerce.Inventory.Api.Hubs.NotificationHub>("/notificationHub");


try
{
    Log.Information("Starting eCommerce.Inventory.Api application");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
