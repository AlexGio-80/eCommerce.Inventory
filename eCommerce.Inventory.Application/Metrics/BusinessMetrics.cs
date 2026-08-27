using Prometheus;
using static Prometheus.Metrics;

namespace eCommerce.Inventory.Application.Metrics;

public sealed class BusinessMetrics
{
    // Note: All metrics are created using the static Metrics class from prometheus-net
    // The CreateHistogram, CreateCounter, CreateGauge methods
    // are static factory methods on the Prometheus.Metrics class
    // Sync metrics
    public readonly Histogram SyncDuration = CreateHistogram(
        "ecommerce_sync_duration_seconds",
        "Duration of sync operations in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "operation", "status" },
            Buckets = Histogram.ExponentialBuckets(0.1, 2, 10)
        });

    public readonly Counter SyncTotal = CreateCounter(
        "ecommerce_sync_total",
        "Total number of sync operations",
        new CounterConfiguration
        {
            LabelNames = new[] { "operation", "status" }
        });

    public readonly Gauge SyncInProgress = CreateGauge(
        "ecommerce_sync_in_progress",
        "Number of sync operations currently in progress",
        new GaugeConfiguration
        {
            LabelNames = new[] { "operation" }
        });

    // Order metrics
    public readonly Counter OrdersCreated = CreateCounter(
        "ecommerce_orders_created_total",
        "Total number of orders created",
        new CounterConfiguration
        {
            LabelNames = new[] { "source" }
        });

    public readonly Counter OrdersUpdated = CreateCounter(
        "ecommerce_orders_updated_total",
        "Total number of orders updated",
        new CounterConfiguration
        {
            LabelNames = new[] { "source" }
        });

    public readonly Histogram OrderProcessingDuration = CreateHistogram(
        "ecommerce_order_processing_duration_seconds",
        "Duration of order processing in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "operation" },
            Buckets = Histogram.ExponentialBuckets(0.01, 2, 10)
        });

    // Inventory metrics
    public readonly Gauge InventoryItemsTotal = CreateGauge(
        "ecommerce_inventory_items_total",
        "Total number of inventory items",
        new GaugeConfiguration
        {
            LabelNames = new[] { "status" }
        });

    public readonly Gauge InventoryValue = CreateGauge(
        "ecommerce_inventory_value_euro",
        "Total inventory value in EUR",
        new GaugeConfiguration
        {
            LabelNames = new[] { "currency" }
        });

    // Card Trader API metrics
    public readonly Counter CardTraderApiRequests = CreateCounter(
        "ecommerce_cardtrader_api_requests_total",
        "Total number of Card Trader API requests",
        new CounterConfiguration
        {
            LabelNames = new[] { "endpoint", "status" }
        });

    public readonly Histogram CardTraderApiDuration = CreateHistogram(
        "ecommerce_cardtrader_api_duration_seconds",
        "Duration of Card Trader API requests in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "endpoint" },
            Buckets = Histogram.ExponentialBuckets(0.1, 2, 10)
        });

    // Webhook metrics
    public readonly Counter WebhooksReceived = CreateCounter(
        "ecommerce_webhooks_received_total",
        "Total number of webhooks received",
        new CounterConfiguration
        {
            LabelNames = new[] { "event_type", "status" }
        });

    public readonly Histogram WebhookProcessingDuration = CreateHistogram(
        "ecommerce_webhook_processing_duration_seconds",
        "Duration of webhook processing in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "event_type" },
            Buckets = Histogram.ExponentialBuckets(0.01, 2, 10)
        });

    // SignalR metrics
    public readonly Gauge SignalRConnectedClients = CreateGauge(
        "ecommerce_signalr_connected_clients",
        "Number of currently connected SignalR clients");

    // Database metrics
    public readonly Histogram DbQueryDuration = CreateHistogram(
        "ecommerce_db_query_duration_seconds",
        "Duration of database queries in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "operation" },
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 12)
        });

    public readonly Counter DbErrors = CreateCounter(
        "ecommerce_db_errors_total",
        "Total number of database errors",
        new CounterConfiguration
        {
            LabelNames = new[] { "operation", "error_type" }
        });

    // Cache metrics
    public readonly Counter CacheHits = CreateCounter(
        "ecommerce_cache_hits_total",
        "Total number of cache hits",
        new CounterConfiguration
        {
            LabelNames = new[] { "cache_type" }
        });

    public readonly Counter CacheMisses = CreateCounter(
        "ecommerce_cache_misses_total",
        "Total number of cache misses",
        new CounterConfiguration
        {
            LabelNames = new[] { "cache_type" }
        });

    // Background job metrics
    public readonly Counter BackgroundJobExecutions = CreateCounter(
        "ecommerce_background_job_executions_total",
        "Total number of background job executions",
        new CounterConfiguration
        {
            LabelNames = new[] { "job_name", "status" }
        });

    public readonly Histogram BackgroundJobDuration = CreateHistogram(
        "ecommerce_background_job_duration_seconds",
        "Duration of background job executions in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "job_name" },
            Buckets = Histogram.ExponentialBuckets(1, 2, 10)
        });

    // Authentication metrics
    public readonly Counter AuthAttempts = CreateCounter(
        "ecommerce_auth_attempts_total",
        "Total number of authentication attempts",
        new CounterConfiguration
        {
            LabelNames = new[] { "result" }
        });
}