using eCommerce.Inventory.Api.Models;
using eCommerce.Inventory.Api.Models.Reporting;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Api.Controllers;

/// <summary>
/// Reporting and Analytics endpoints
/// Provides sales, inventory, and profitability metrics
/// </summary>
[ApiController]
[Route("api/reporting")]
public class ReportingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ReportingController> _logger;

    public ReportingController(
        ApplicationDbContext context,
        ILogger<ReportingController> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Sales Analytics

    /// <summary>
    /// Get sales metrics for a date range
    /// </summary>
    [HttpGet("sales/metrics")]
    public async Task<ActionResult<ApiResponse<SalesMetricsDto>>> GetSalesMetrics(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            var fromDate = from ?? DateTime.UtcNow.AddMonths(-1);
            var toDate = to ?? DateTime.UtcNow;

            _logger.LogInformation("Fetching sales metrics from {FromDate} to {ToDate}", fromDate, toDate);

            var orders = await _context.Orders
                .AsNoTracking()
                .Where(o => o.PaidAt != null && o.PaidAt >= fromDate && o.PaidAt <= toDate)
                .ToListAsync();

            var totalRevenue = orders.Sum(o => o.SellerSubtotal);
            var totalOrders = orders.Count;
            var averageOrderValue = totalOrders > 0 ? totalRevenue / totalOrders : 0;

            // Calculate growth percentage (compare with previous period)
            var periodDays = (toDate - fromDate).Days;
            var previousFromDate = fromDate.AddDays(-periodDays);
            var previousToDate = fromDate;

            var previousRevenue = await _context.Orders
                .AsNoTracking()
                .Where(o => o.PaidAt != null && o.PaidAt >= previousFromDate && o.PaidAt < previousToDate)
                .SumAsync(o => o.SellerSubtotal);

            var growthPercentage = previousRevenue > 0
                ? ((totalRevenue - previousRevenue) / previousRevenue) * 100
                : 0;

            var metrics = new SalesMetricsDto
            {
                TotalRevenue = totalRevenue,
                TotalOrders = totalOrders,
                AverageOrderValue = averageOrderValue,
                GrowthPercentage = growthPercentage,
                FromDate = fromDate,
                ToDate = toDate
            };

            return Ok(ApiResponse<SalesMetricsDto>.SuccessResult(metrics));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching sales metrics");
            return StatusCode(500, ApiResponse<SalesMetricsDto>.ErrorResult("Failed to fetch sales metrics", ex.Message));
        }
    }

    /// <summary>
    /// Get sales chart data (time series)
    /// </summary>
    [HttpGet("sales/chart")]
    public async Task<ActionResult<ApiResponse<SalesChartDataDto>>> GetSalesChart(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string groupBy = "day")
    {
        try
        {
            var fromDate = from ?? DateTime.UtcNow.AddMonths(-1);
            var toDate = to ?? DateTime.UtcNow;

            _logger.LogInformation("Fetching sales chart data from {FromDate} to {ToDate}, grouped by {GroupBy}",
                fromDate, toDate, groupBy);

            var orders = await _context.Orders
                .AsNoTracking()
                .Where(o => o.PaidAt != null && o.PaidAt >= fromDate && o.PaidAt <= toDate)
                .ToListAsync();

            var chartData = new SalesChartDataDto { GroupBy = groupBy };

            if (groupBy.ToLower() == "day")
            {
                var grouped = orders
                    .GroupBy(o => o.PaidAt!.Value.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new { Date = g.Key, Revenue = g.Sum(o => o.SellerSubtotal) })
                    .ToList();

                chartData.Labels = grouped.Select(g => g.Date.ToString("yyyy-MM-dd")).ToList();
                chartData.Values = grouped.Select(g => g.Revenue).ToList();
            }
            else if (groupBy.ToLower() == "week")
            {
                var grouped = orders
                    .GroupBy(o => GetWeekNumber(o.PaidAt!.Value))
                    .OrderBy(g => g.Key)
                    .Select(g => new { Week = g.Key, Revenue = g.Sum(o => o.SellerSubtotal) })
                    .ToList();

                chartData.Labels = grouped.Select(g => $"Week {g.Week}").ToList();
                chartData.Values = grouped.Select(g => g.Revenue).ToList();
            }
            else if (groupBy.ToLower() == "month")
            {
                var grouped = orders
                    .GroupBy(o => new { o.PaidAt!.Value.Year, o.PaidAt.Value.Month })
                    .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
                    .Select(g => new { g.Key.Year, g.Key.Month, Revenue = g.Sum(o => o.SellerSubtotal) })
                    .ToList();

                chartData.Labels = grouped.Select(g => $"{g.Year}-{g.Month:D2}").ToList();
                chartData.Values = grouped.Select(g => g.Revenue).ToList();
            }

            return Ok(ApiResponse<SalesChartDataDto>.SuccessResult(chartData));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching sales chart data");
            return StatusCode(500, ApiResponse<SalesChartDataDto>.ErrorResult("Failed to fetch sales chart data", ex.Message));
        }
    }

    /// <summary>
    /// Get top selling products
    /// </summary>
    [HttpGet("sales/top-products")]
    public async Task<ActionResult<ApiResponse<List<TopProductDto>>>> GetTopProducts(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 10)
    {
        try
        {
            var fromDate = from ?? DateTime.UtcNow.AddMonths(-1);
            var toDate = to ?? DateTime.UtcNow;

            _logger.LogInformation("Fetching top {Limit} products from {FromDate} to {ToDate}",
                limit, fromDate, toDate);

            var topProducts = await _context.OrderItems
                .AsNoTracking()
                .Include(oi => oi.Blueprint)
                    .ThenInclude(b => b.Expansion)
                    .ThenInclude(e => e.Game)
                .Where(oi => oi.Order.PaidAt != null &&
                             oi.Order.PaidAt >= fromDate &&
                             oi.Order.PaidAt <= toDate &&
                             oi.Blueprint != null)
                .GroupBy(oi => new
                {
                    BlueprintId = oi.BlueprintId!.Value,
                    CardName = oi.Blueprint!.Name,
                    ExpansionName = oi.Blueprint.Expansion.Name,
                    GameName = oi.Blueprint.Expansion.Game.Name
                })
                .Select(g => new TopProductDto
                {
                    BlueprintId = g.Key.BlueprintId,
                    CardName = g.Key.CardName,
                    ExpansionName = g.Key.ExpansionName,
                    GameName = g.Key.GameName,
                    QuantitySold = g.Sum(oi => oi.Quantity),
                    TotalRevenue = g.Sum(oi => oi.Price * oi.Quantity),
                    AveragePrice = g.Average(oi => oi.Price)
                })
                .OrderByDescending(p => p.TotalRevenue)
                .Take(limit)
                .ToListAsync();

            return Ok(ApiResponse<List<TopProductDto>>.SuccessResult(topProducts));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching top products");
            return StatusCode(500, ApiResponse<List<TopProductDto>>.ErrorResult("Failed to fetch top products", ex.Message));
        }
    }

    /// <summary>
    /// Get sales distribution by game
    /// </summary>
    [HttpGet("sales/by-game")]
    public async Task<ActionResult<ApiResponse<List<SalesByGameDto>>>> GetSalesByGame(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            var fromDate = from ?? DateTime.UtcNow.AddMonths(-1);
            var toDate = to ?? DateTime.UtcNow;

            _logger.LogInformation("Fetching sales by game from {FromDate} to {ToDate}", fromDate, toDate);

            var salesByGame = await _context.OrderItems
                .AsNoTracking()
                .Include(oi => oi.Blueprint)
                    .ThenInclude(b => b.Expansion)
                    .ThenInclude(e => e.Game)
                .Where(oi => oi.Order.PaidAt != null &&
                             oi.Order.PaidAt >= fromDate &&
                             oi.Order.PaidAt <= toDate &&
                             oi.Blueprint != null)
                .GroupBy(oi => oi.Blueprint!.Expansion.Game.Name)
                .Select(g => new
                {
                    GameName = g.Key,
                    TotalRevenue = g.Sum(oi => oi.Price * oi.Quantity),
                    OrderCount = g.Select(oi => oi.OrderId).Distinct().Count()
                })
                .ToListAsync();

            var totalRevenue = salesByGame.Sum(s => s.TotalRevenue);

            var result = salesByGame.Select(s => new SalesByGameDto
            {
                GameName = s.GameName,
                TotalRevenue = s.TotalRevenue,
                OrderCount = s.OrderCount,
                Percentage = totalRevenue > 0 ? (s.TotalRevenue / totalRevenue) * 100 : 0
            }).ToList();

            return Ok(ApiResponse<List<SalesByGameDto>>.SuccessResult(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching sales by game");
            return StatusCode(500, ApiResponse<List<SalesByGameDto>>.ErrorResult("Failed to fetch sales by game", ex.Message));
        }
    }

    #endregion

    #region Inventory Analytics

    /// <summary>
    /// Get current inventory value
    /// </summary>
    [HttpGet("inventory/value")]
    public async Task<ActionResult<ApiResponse<InventoryValueDto>>> GetInventoryValue()
    {
        try
        {
            _logger.LogInformation("Fetching inventory value");

            var items = await _context.InventoryItems
                .AsNoTracking()
                .ToListAsync();

            var value = new InventoryValueDto
            {
                TotalValue = items.Sum(i => i.ListingPrice * i.Quantity),
                TotalItems = items.Sum(i => i.Quantity),
                UniqueProducts = items.Count,
                AverageItemValue = items.Count > 0 ? items.Average(i => i.ListingPrice) : 0
            };

            return Ok(ApiResponse<InventoryValueDto>.SuccessResult(value));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching inventory value");
            return StatusCode(500, ApiResponse<InventoryValueDto>.ErrorResult("Failed to fetch inventory value", ex.Message));
        }
    }

    /// <summary>
    /// Get inventory distribution by game
    /// </summary>
    [HttpGet("inventory/distribution")]
    public async Task<ActionResult<ApiResponse<List<InventoryDistributionDto>>>> GetInventoryDistribution()
    {
        try
        {
            _logger.LogInformation("Fetching inventory distribution");

            var distribution = await _context.InventoryItems
                .AsNoTracking()
                .Include(i => i.Blueprint)
                    .ThenInclude(b => b.Expansion)
                    .ThenInclude(e => e.Game)
                .GroupBy(i => i.Blueprint.Expansion.Game.Name)
                .Select(g => new
                {
                    GameName = g.Key,
                    TotalValue = g.Sum(i => i.ListingPrice * i.Quantity),
                    ItemCount = g.Sum(i => i.Quantity)
                })
                .ToListAsync();

            var totalValue = distribution.Sum(d => d.TotalValue);

            var result = distribution.Select(d => new InventoryDistributionDto
            {
                GameName = d.GameName,
                TotalValue = d.TotalValue,
                ItemCount = d.ItemCount,
                Percentage = totalValue > 0 ? (d.TotalValue / totalValue) * 100 : 0
            }).ToList();

            return Ok(ApiResponse<List<InventoryDistributionDto>>.SuccessResult(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching inventory distribution");
            return StatusCode(500, ApiResponse<List<InventoryDistributionDto>>.ErrorResult("Failed to fetch inventory distribution", ex.Message));
        }
    }

    /// <summary>
    /// Get slow-moving inventory items
    /// </summary>
    [HttpGet("inventory/slow-movers")]
    public async Task<ActionResult<ApiResponse<List<SlowMoverDto>>>> GetSlowMovers(
        [FromQuery] int days = 90)
    {
        try
        {
            _logger.LogInformation("Fetching slow movers (items older than {Days} days)", days);

            var cutoffDate = DateTime.UtcNow.AddDays(-days);

            var slowMovers = await _context.InventoryItems
                .AsNoTracking()
                .Include(i => i.Blueprint)
                    .ThenInclude(b => b.Expansion)
                .Where(i => i.DateAdded <= cutoffDate && i.Quantity > 0)
                .Select(i => new SlowMoverDto
                {
                    InventoryItemId = i.Id,
                    CardName = i.Blueprint.Name,
                    ExpansionName = i.Blueprint.Expansion.Name,
                    Quantity = i.Quantity,
                    ListingPrice = i.ListingPrice,
                    DaysInInventory = (int)(DateTime.UtcNow - i.DateAdded).TotalDays,
                    DateAdded = i.DateAdded
                })
                .OrderByDescending(s => s.DaysInInventory)
                .ToListAsync();

            return Ok(ApiResponse<List<SlowMoverDto>>.SuccessResult(slowMovers));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching slow movers");
            return StatusCode(500, ApiResponse<List<SlowMoverDto>>.ErrorResult("Failed to fetch slow movers", ex.Message));
        }
    }

    #endregion

    #region Profitability Analytics

    /// <summary>
    /// Get profitability overview
    /// </summary>
    [HttpGet("profitability/overview")]
    public async Task<ActionResult<ApiResponse<ProfitabilityOverviewDto>>> GetProfitabilityOverview(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            var fromDate = from ?? DateTime.UtcNow.AddMonths(-1);
            var toDate = to ?? DateTime.UtcNow;

            _logger.LogInformation("Fetching profitability overview from {FromDate} to {ToDate}", fromDate, toDate);

            // Get sold items with blueprint info
            var soldItems = await _context.OrderItems
                .AsNoTracking()
                .Include(oi => oi.Blueprint)
                .Where(oi => oi.Order.PaidAt != null &&
                             oi.Order.PaidAt >= fromDate &&
                             oi.Order.PaidAt <= toDate &&
                             oi.BlueprintId != null)
                .ToListAsync();

            var totalRevenue = soldItems.Sum(oi => oi.Price * oi.Quantity);

            // Get average purchase prices for blueprints
            var blueprintIds = soldItems.Select(oi => oi.BlueprintId!.Value).Distinct().ToList();
            var avgPurchasePrices = await _context.InventoryItems
                .AsNoTracking()
                .Where(ii => blueprintIds.Contains(ii.BlueprintId))
                .GroupBy(ii => ii.BlueprintId)
                .Select(g => new { BlueprintId = g.Key, AvgPrice = g.Average(ii => ii.PurchasePrice) })
                .ToDictionaryAsync(x => x.BlueprintId, x => x.AvgPrice);

            // Calculate total cost based on average purchase prices
            var totalCost = soldItems.Sum(oi =>
            {
                var blueprintId = oi.BlueprintId!.Value;
                var avgPrice = avgPurchasePrices.ContainsKey(blueprintId) ? avgPurchasePrices[blueprintId] : 0;
                return avgPrice * oi.Quantity;
            });

            var grossProfit = totalRevenue - totalCost;
            var profitMargin = totalRevenue > 0 ? (grossProfit / totalRevenue) * 100 : 0;
            var roi = totalCost > 0 ? (grossProfit / totalCost) * 100 : 0;

            var overview = new ProfitabilityOverviewDto
            {
                TotalRevenue = totalRevenue,
                TotalCost = totalCost,
                GrossProfit = grossProfit,
                ProfitMarginPercentage = profitMargin,
                ROI = roi,
                FromDate = fromDate,
                ToDate = toDate
            };

            return Ok(ApiResponse<ProfitabilityOverviewDto>.SuccessResult(overview));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching profitability overview");
            return StatusCode(500, ApiResponse<ProfitabilityOverviewDto>.ErrorResult("Failed to fetch profitability overview", ex.Message));
        }
    }

    /// <summary>
    /// Get top performing products by profitability
    /// </summary>
    [HttpGet("profitability/top-performers")]
    public async Task<ActionResult<ApiResponse<List<TopPerformerDto>>>> GetTopPerformers(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 10)
    {
        try
        {
            var fromDate = from ?? DateTime.UtcNow.AddMonths(-1);
            var toDate = to ?? DateTime.UtcNow;

            _logger.LogInformation("Fetching top {Limit} performers from {FromDate} to {ToDate}",
                limit, fromDate, toDate);

            // Get sold items grouped by blueprint
            var topPerformers = await _context.OrderItems
                .AsNoTracking()
                .Include(oi => oi.Blueprint)
                    .ThenInclude(b => b.Expansion)
                .Where(oi => oi.Order.PaidAt != null &&
                             oi.Order.PaidAt >= fromDate &&
                             oi.Order.PaidAt <= toDate &&
                             oi.BlueprintId != null)
                .GroupBy(oi => new
                {
                    BlueprintId = oi.BlueprintId!.Value,
                    CardName = oi.Blueprint!.Name,
                    ExpansionName = oi.Blueprint.Expansion.Name
                })
                .Select(g => new
                {
                    g.Key.BlueprintId,
                    g.Key.CardName,
                    g.Key.ExpansionName,
                    QuantitySold = g.Sum(oi => oi.Quantity),
                    TotalRevenue = g.Sum(oi => oi.Price * oi.Quantity)
                })
                .ToListAsync();

            // Get average purchase prices for these blueprints
            var blueprintIds = topPerformers.Select(p => p.BlueprintId).ToList();
            var avgPurchasePrices = await _context.InventoryItems
                .AsNoTracking()
                .Where(ii => blueprintIds.Contains(ii.BlueprintId))
                .GroupBy(ii => ii.BlueprintId)
                .Select(g => new { BlueprintId = g.Key, AvgPrice = g.Average(ii => ii.PurchasePrice) })
                .ToDictionaryAsync(x => x.BlueprintId, x => x.AvgPrice);

            var result = topPerformers
                .Select(p =>
                {
                    var avgCost = avgPurchasePrices.ContainsKey(p.BlueprintId) ? avgPurchasePrices[p.BlueprintId] : 0;
                    var totalCost = avgCost * p.QuantitySold;
                    var totalProfit = p.TotalRevenue - totalCost;

                    return new TopPerformerDto
                    {
                        BlueprintId = p.BlueprintId,
                        CardName = p.CardName,
                        ExpansionName = p.ExpansionName,
                        QuantitySold = p.QuantitySold,
                        TotalRevenue = p.TotalRevenue,
                        TotalCost = totalCost,
                        TotalProfit = totalProfit,
                        ProfitMarginPercentage = p.TotalRevenue > 0
                            ? (totalProfit / p.TotalRevenue) * 100
                            : 0
                    };
                })
                .OrderByDescending(p => p.TotalProfit)
                .Take(limit)
                .ToList();

            return Ok(ApiResponse<List<TopPerformerDto>>.SuccessResult(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching top performers");
            return StatusCode(500, ApiResponse<List<TopPerformerDto>>.ErrorResult("Failed to fetch top performers", ex.Message));
        }
    }

    #endregion

    #region Expansion Analytics

    /// <summary>
    /// Get sales by expansion
    /// </summary>
    [HttpGet("sales/by-expansion")]
    public async Task<ActionResult<ApiResponse<List<SalesByExpansionDto>>>> GetSalesByExpansion(
        [FromQuery] int limit = 5,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? filter = null)
    {
        try
        {
            _logger.LogInformation("Fetching sales by expansion from ExpansionsROI view with filter {Filter}", filter);

            var query = _context.ExpansionsROI
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filter))
            {
                query = query.Where(x => x.ExpansionName.Contains(filter));
            }

            var salesByExpansion = await query
                .OrderByDescending(x => x.TotaleVenduto)
                .Take(limit)
                .ToListAsync();

            var totalRevenue = salesByExpansion.Sum(s => s.TotaleVenduto ?? 0);

            var result = salesByExpansion.Select(s => new SalesByExpansionDto
            {
                ExpansionName = s.ExpansionName,
                TotalRevenue = s.TotaleVenduto ?? 0,
                OrderCount = 0, // Not available in view
                Percentage = totalRevenue > 0 ? ((s.TotaleVenduto ?? 0) / totalRevenue) * 100 : 0
            }).ToList();

            return Ok(ApiResponse<List<SalesByExpansionDto>>.SuccessResult(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching sales by expansion");
            return StatusCode(500, ApiResponse<List<SalesByExpansionDto>>.ErrorResult("Failed to fetch sales by expansion", ex.Message));
        }
    }

    /// <summary>
    /// Get expansion profitability from ExpansionsROI view
    /// </summary>
    [HttpGet("profitability/by-expansion")]
    public async Task<ActionResult<ApiResponse<List<ExpansionProfitabilityDto>>>> GetExpansionProfitability(
        [FromQuery] int limit = 10,
        [FromQuery] string? filter = null)
    {
        try
        {
            _logger.LogInformation("Fetching expansion profitability from view with filter {Filter}", filter);

            var query = _context.ExpansionsROI
                .AsNoTracking();

            if (!string.IsNullOrWhiteSpace(filter))
            {
                query = query.Where(x => x.ExpansionName.Contains(filter));
            }

            var roiData = await query
                .OrderByDescending(x => (x.TotaleAcquistato ?? 0) > 0 ? (x.Differenza ?? 0) / (x.TotaleAcquistato ?? 0) : 0)
                .Take(limit)
                .ToListAsync();

            var result = roiData.Select(x => new ExpansionProfitabilityDto
            {
                ExpansionName = x.ExpansionName,
                Differenza = x.Differenza ?? 0,
                TotaleVenduto = x.TotaleVenduto ?? 0,
                TotaleAcquistato = x.TotaleAcquistato ?? 0,
                PercentualeDifferenza = (x.TotaleAcquistato ?? 0) > 0
                    ? ((x.Differenza ?? 0) / (x.TotaleAcquistato ?? 0)) * 100
                    : 0
            }).ToList();

            return Ok(ApiResponse<List<ExpansionProfitabilityDto>>.SuccessResult(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching expansion profitability");
            return StatusCode(500, ApiResponse<List<ExpansionProfitabilityDto>>.ErrorResult("Failed to fetch expansion profitability", ex.Message));
        }
    }

    /// <summary>
    /// Get top expansions by average card value
    /// </summary>
    [HttpGet("expansions/top-values")]
    public async Task<ActionResult<ApiResponse<List<TopExpansionValueDto>>>> GetTopExpansionsByValue(
        [FromQuery] int limit = 10)
    {
        try
        {
            _logger.LogInformation("Fetching top {Limit} expansions by average card value", limit);

            var topExpansions = await _context.Expansions
                .AsNoTracking()
                .Include(e => e.Game)
                .Where(e => e.AverageCardValue > 0)
                .OrderByDescending(e => e.AverageCardValue)
                .Take(limit)
                .Select(e => new TopExpansionValueDto
                {
                    ExpansionId = e.Id,
                    ExpansionName = e.Name,
                    GameName = e.Game.Name,
                    AverageCardValue = e.AverageCardValue ?? 0,
                    TotalMinPrice = e.TotalMinPrice ?? 0,
                    LastUpdate = e.LastValueAnalysisUpdate
                })
                .ToListAsync();

            return Ok(ApiResponse<List<TopExpansionValueDto>>.SuccessResult(topExpansions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching top expansions by value");
            return StatusCode(500, ApiResponse<List<TopExpansionValueDto>>.ErrorResult("Failed to fetch top expansions by value", ex.Message));
        }
    }

    #endregion

    #region Helper Methods

    private static int GetWeekNumber(DateTime date)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var calendar = culture.Calendar;
        var weekRule = culture.DateTimeFormat.CalendarWeekRule;
        var firstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;
        return calendar.GetWeekOfYear(date, weekRule, firstDayOfWeek);
    }

    #endregion
}
