using System.Diagnostics;
using Serilog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Application.Metrics;
using eCommerce.Inventory.Application.Settings;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Persistence.Repositories;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Mappers;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Policies;
using eCommerce.Inventory.Infrastructure.Services;
using eCommerce.Inventory.Infrastructure.ExternalServices.Scryfall;
using eCommerce.Inventory.Infrastructure.ExternalServices.Scryfall.DTOs;
using eCommerce.Inventory.Api.HealthChecks;
using eCommerce.Inventory.Api.Middleware;
using MediatR;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using HealthChecks.UI;

// Avviato come servizio Windows, il processo eredita come cartella corrente C:\Windows\System32,
// non quella dell'eseguibile. Ogni percorso relativo di configurazione — il file di log, la
// cartella dei backup — verrebbe quindi risolto lì dentro, dove l'account del servizio non ha
// permesso di scrittura: il sink su file falliva in silenzio e l'applicazione girava senza
// lasciare traccia. Va allineata prima di costruire l'host, perché il logger si configura subito.
if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
{
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);
}

var builder = WebApplication.CreateBuilder(args);

// Serilog scarta in silenzio un sink che non riesce a inizializzarsi: se il servizio non ha
// permesso di scrivere nella cartella dei log, l'applicazione gira senza lasciare traccia e il
// problema è indistinguibile da "non è successo nulla". SelfLog scrive il motivo del fallimento
// accanto all'eseguibile, così la diagnosi parte da un messaggio invece che da una cartella vuota.
try
{
    var selfLogPath = Path.Combine(AppContext.BaseDirectory, "serilog-selflog.txt");
    var selfLogWriter = TextWriter.Synchronized(new StreamWriter(selfLogPath, append: true));
    Serilog.Debugging.SelfLog.Enable(selfLogWriter);
}
catch (Exception ex)
{
    // Se nemmeno questo è scrivibile resta la console: non deve impedire l'avvio.
    Console.Error.WriteLine($"Impossibile abilitare Serilog SelfLog: {ex.Message}");
}

// Configure Serilog from appsettings.json (supports environment-specific configs)
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "eCommerce.Inventory")
    .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName));

// Configure as Windows Service
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "eCommerce.Inventory";
});

// Configure Kestrel to listen on port 5152
var apiConfig = builder.Configuration.GetSection("Api");
var apiBaseUrl = apiConfig["BaseUrl"];
builder.WebHost.UseUrls(apiBaseUrl);

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

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
builder.Services.AddSingleton<CardTraderRateLimiter>(); // Singleton to share rate limit across all scopes
builder.Services.AddScoped<INotificationService, eCommerce.Inventory.Api.Services.SignalRNotificationService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IExpansionAnalyticsService, ExpansionAnalyticsService>();

// Autopricer: il motore è senza stato, il servizio orchestra API e persistenza
builder.Services.AddSingleton<eCommerce.Inventory.Application.Pricing.PricingEngine>();
builder.Services.AddScoped<AutoPricingService>();

// Coda condivisa fra i produttori (webhook delle vendite, sincronizzazione delle
// inserzioni) e il worker che la consuma: dev'essere singleton
builder.Services.AddSingleton<IPriceRefreshQueue, PriceRefreshQueue>();

// Esecuzione notturna dell'autopricer (attivabile via AutoPricing:Enabled)
builder.Services.AddHostedService<eCommerce.Inventory.Infrastructure.BackgroundJobs.AutoPricingWorker>();

// Reprice immediato fuori dalla notturna: vendite (AutoPricing:RepriceOnOrder) e nuove
// inserzioni pubblicate dalla maschera (AutoPricing:RepriceOnListingSync). Il worker gira
// sempre, sono i due interruttori a decidere se qualcosa viene accodato.
builder.Services.AddHostedService<eCommerce.Inventory.Infrastructure.BackgroundJobs.PriceRefreshWorker>();

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"];

// La chiave di firma va tenuta fuori dai file versionati: appsettings.json contiene
// solo un segnaposto, il valore reale sta in appsettings.{Environment}.json che è
// escluso da git. Meglio non partire affatto che partire firmando i token con una
// chiave pubblicamente nota: chiunque potrebbe forgiare un token di amministratore.
if (string.IsNullOrWhiteSpace(secretKey))
{
    throw new InvalidOperationException(
        "JwtSettings:SecretKey non configurata. Impostarla in appsettings.{Environment}.json " +
        "(file escluso dal versionamento) con un valore casuale di almeno 32 caratteri.");
}

if (secretKey.StartsWith("REPLACE_ME", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "JwtSettings:SecretKey è ancora il segnaposto di esempio. Generare una chiave casuale " +
        "e impostarla in appsettings.{Environment}.json prima di avviare l'applicazione.");
}

// HMAC-SHA256 richiede almeno 256 bit di chiave.
if (System.Text.Encoding.UTF8.GetByteCount(secretKey) < 32)
{
    throw new InvalidOperationException(
        $"JwtSettings:SecretKey è troppo corta ({System.Text.Encoding.UTF8.GetByteCount(secretKey)} byte): " +
        "servono almeno 32 byte per la firma HMAC-SHA256.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secretKey))
    };
});

// Chiuso per difetto: ogni endpoint richiede un utente autenticato con ruolo Admin, e le
// eccezioni si dichiarano una per una con [AllowAnonymous]. Prima valeva il contrario —
// solo due controller su dieci avevano [Authorize] — quindi bastava raggiungere l'API per
// leggere e modificare tutto il magazzino.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole("Admin")
        .Build();
});

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
    client.Timeout = TimeSpan.FromSeconds(120);
})
.AddPolicyHandler(CardTraderPolicies.GetRetryPolicy())
.AddPolicyHandler(CardTraderPolicies.GetCircuitBreakerPolicy());

// Register Card Trader background sync worker
// NOTE: Temporarily disabled for development. Enable once Card Trader API is properly configured.
// builder.Services.AddHostedService<CardTraderSyncWorker>();
builder.Services.AddHostedService<eCommerce.Inventory.Infrastructure.BackgroundJobs.ScheduledProductSyncWorker>();

// Register one-shot ItalianName population service
// Enable via SyncSettings:PopulateItalianNamesOnStartup = true in appsettings.json
builder.Services.AddHostedService<eCommerce.Inventory.Infrastructure.BackgroundJobs.PopulateItalianNamesService>();

// Register one-shot Sealed Product Price population service
// Enable via SyncSettings:PopulateSealedPricesOnStartup = true in appsettings.json
builder.Services.AddHostedService<eCommerce.Inventory.Infrastructure.BackgroundJobs.SealedProductPriceService>();

// Configure and register Backup Service
builder.Services.Configure<BackupSettings>(builder.Configuration.GetSection("Backup"));
builder.Services.AddHostedService<BackupService>();

// Register Grading Service
builder.Services.AddHttpClient<IGradingService, XimilarGradingService>();

// Register Scryfall API Client
builder.Services.AddHttpClient<IScryfallApiClient, ScryfallApiClient>(client =>
{
    client.BaseAddress = new Uri("https://api.scryfall.com/");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("User-Agent", "eCommerceInventory/1.0");
});

// Register SignalR
builder.Services.AddSignalR();

// Register Redis Cache
builder.Services.Configure<RedisSettings>(builder.Configuration.GetSection("Redis"));
builder.Services.Configure<CacheSettings>(builder.Configuration.GetSection("CacheSettings"));
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

// Register Health Checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<CardTraderApiHealthCheck>("cardtrader-api")
    .AddCheck<RedisHealthCheck>("redis");

// Add Health Checks UI (preparazione Fase 2)
builder.Services.AddHealthChecksUI(options =>
{
    options.SetEvaluationTimeInSeconds(30);
    options.MaximumHistoryEntriesPerEndpoint(50);
})
.AddInMemoryStorage();

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService("eCommerce.Inventory")
        .AddAttributes(new Dictionary<string, object>
        {
            ["service.version"] = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            ["deployment.environment"] = builder.Environment.EnvironmentName
        }))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation(options =>
        {
            options.RecordException = true;
            options.EnrichWithHttpRequest = (activity, request) =>
            {
                activity.SetTag("http.request.correlation_id", request.Headers["X-Correlation-ID"].FirstOrDefault());
                activity.SetTag("http.request.user_agent", request.Headers["User-Agent"].FirstOrDefault());
            };
            options.EnrichWithHttpResponse = (activity, response) =>
            {
                activity.SetTag("http.response.status_code", response.StatusCode);
            };
        })
        .AddHttpClientInstrumentation(options =>
        {
            options.RecordException = true;
            options.EnrichWithHttpRequestMessage = (activity, request) =>
            {
                activity.SetTag("http.client.request.uri", request.RequestUri?.ToString());
            };
            options.EnrichWithHttpResponseMessage = (activity, response) =>
            {
                activity.SetTag("http.client.response.status_code", (int)response.StatusCode);
            };
        })
        .AddEntityFrameworkCoreInstrumentation(options =>
        {
            options.SetDbStatementForText = true;
            options.EnrichWithIDbCommand = (activity, command) =>
            {
                activity.SetTag("db.operation", command.CommandText?.Split(' ')[0] ?? "unknown");
            };
        })
        .AddSource("eCommerce.Inventory")
        .AddConsoleExporter())
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddPrometheusExporter()
        .AddConsoleExporter());

// Register custom metrics
builder.Services.AddSingleton<BusinessMetrics>();

// Add Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    // Policy 1: General API endpoints (100 requests/minute)
    options.AddPolicy("api", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            }));

    // Policy 2: Card Trader sync endpoints (10 requests/minute)
    options.AddPolicy("cardtrader-sync", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                QueueLimit = 2
            }));

    // Policy 3: Authentication endpoints (5 requests/minute per IP)
    options.AddPolicy("auth", context =>
        System.Threading.RateLimiting.RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4,
                QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Global fallback (200 requests/minute per user/IP)
    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<Microsoft.AspNetCore.Http.HttpContext, string>(context =>
    {
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1)
            });
    });

    // Rejection response
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        if (context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = retryAfter.TotalSeconds.ToString();
        }

        double? retryAfterSeconds = null;
        if (context.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var retry))
        {
            retryAfterSeconds = retry.TotalSeconds;
        }

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Too many requests. Please try again later.",
            retryAfter = retryAfterSeconds
        }, cancellationToken: token);
    };
});

// Add CORS for frontend integration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins("http://localhost:4200", "http://127.0.0.1:4200", "http://inventory.local")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Le migration vengono applicate all'avvio in ogni ambiente.
//
// Prima giravano solo in Development e in produzione erano un passaggio manuale: dimenticarlo
// faceva partire i binari nuovi contro uno schema vecchio, e l'applicazione falliva a ogni
// lettura delle entità modificate senza che nulla lo dichiarasse. Il disallineamento fra codice
// e schema non è una condizione da cui si possa lavorare, quindi va chiuso all'avvio.
//
// Deliberatamente senza interruttore di disattivazione: un flag lasciato a false riprodurrebbe
// esattamente lo stesso disallineamento, stavolta senza nemmeno un passaggio dimenticato da
// ricostruire. E se la migrazione fallisce l'applicazione non parte: un servizio fermo si nota,
// un servizio che scrive su uno schema che non corrisponde al modello no.
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        // L'elenco viene registrato prima di applicarlo: è la traccia di quando lo schema è
        // cambiato e di cosa è cambiato, che con l'applicazione manuale non esisteva.
        var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToList();

        if (pendingMigrations.Count > 0)
        {
            logger.LogInformation(
                "Migration da applicare ({Count}): {Migrations}",
                pendingMigrations.Count, string.Join(", ", pendingMigrations));

            await dbContext.Database.MigrateAsync();

            logger.LogInformation("Migration applicate correttamente");
        }
        else
        {
            logger.LogInformation("Nessuna migration da applicare: lo schema è già allineato al modello");
        }

        // Il profilo di pricing serve ovunque: senza, l'autopricer non ha regole.
        await eCommerce.Inventory.Infrastructure.Persistence.SeedData
            .SeedDefaultPricingProfileAsync(dbContext, logger);

        // I dati dimostrativi restano confinati allo sviluppo: in produzione l'inventario
        // arriva da Card Trader e non va inquinato con giochi e carte fittizi.
        if (app.Environment.IsDevelopment())
        {
            await eCommerce.Inventory.Infrastructure.Persistence.SeedData
                .InitializeAsync(dbContext, logger, app.Configuration["Seed:AdminPassword"]);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Errore nell'applicazione delle migration o nel seed iniziale");
        throw;
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "eCommerce.Inventory API v1");
        c.RoutePrefix = "swagger";
    });
}
else
{
    // Only use HTTPS redirect in production
    app.UseHttpsRedirection();
}

// Use CORS before authorization
app.UseCors("AllowAll");

// Use Correlation ID middleware (MUST be before SerilogRequestLogging for proper enrichment)
app.UseCorrelationId();

// Use Serilog middleware for request/response logging
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.Response.Headers["X-Correlation-ID"].FirstOrDefault());
        diagnosticContext.Set("TraceId", Activity.Current?.TraceId.ToString());
        diagnosticContext.Set("SpanId", Activity.Current?.SpanId.ToString());
    };
});

// Use Global Exception Middleware (AFTER logging, BEFORE authorization)
app.UseMiddleware<eCommerce.Inventory.Api.Middleware.GlobalExceptionMiddleware>();

// Routing MUST be before authentication, authorization, rate limiting, and endpoint mapping
app.UseRouting();

// Use Rate Limiter (AFTER Routing, BEFORE Authentication)
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

// Health check endpoints
// Anonimi per scelta: le probe del servizio Windows e gli scraper non hanno un token.
// Espongono stato dei componenti e volumi di magazzino, quindi vanno protetti a livello
// di rete se un giorno l'API venisse esposta oltre localhost.
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = System.Net.Mime.MediaTypeNames.Application.Json;
        var result = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds,
                data = e.Value.Data
            })
        };
        await context.Response.WriteAsJsonAsync(result);
    },
    Predicate = _ => true,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
}).AllowAnonymous();
app.MapHealthChecksUI(options => options.UIPath = "/health-ui").AllowAnonymous();

// Prometheus metrics endpoint - Use MapMetrics() for minimal API (NOT UseMetricServer)
app.MapMetrics().AllowAnonymous();

app.MapControllers();
// Il client SignalR non allega il token: la connessione resta anonima, ma l'hub si limita a
// diffondere avanzamento delle sync e notifiche, senza esporre dati di magazzino.
app.MapHub<eCommerce.Inventory.Api.Hubs.NotificationHub>("/notificationHub").AllowAnonymous();

try
{
    Log.Information("Starting eCommerce.Inventory.Api application");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
