using Serilog;
using Microsoft.EntityFrameworkCore;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Persistence.Repositories;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/taffel-inventory-.txt", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
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

// Register DbContext as IApplicationDbContext for dependency injection
builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

// Configure HttpClient for Card Trader API with Bearer Token authentication
var cardTraderApiConfig = builder.Configuration.GetSection("CardTraderApi");
var bearerToken = cardTraderApiConfig["BearerToken"];
var baseUrl = cardTraderApiConfig["BaseUrl"];

builder.Services.AddHttpClient<ICardTraderApiService, CardTraderApiClient>(client =>
{
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {bearerToken}");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Register Card Trader background sync worker
builder.Services.AddHostedService<CardTraderSyncWorker>();

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

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

// Use Serilog middleware for request/response logging
app.UseSerilogRequestLogging();

app.UseAuthorization();

app.MapControllers();

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
