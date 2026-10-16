using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Application.Pricing;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Prezzo delle regole dell'autopricer per una carta che si sta mettendo in vendita dalla maschera
/// "Nuovo Prodotto". Il calcolo è quello del motore (<see cref="PricingEngine.SuggestNewListingPrice"/>),
/// col profilo attivo: qui si recuperano solo profilo e offerte.
///
/// Le offerte passano dalla cache condivisa, così la stessa lettura serve le statistiche della
/// maschera, il prezzo proposto a ogni cambio di condizione o lingua, e il riprezzo dopo la
/// pubblicazione.
/// </summary>
public class ListingPriceSuggestionService
{
    public const string NoActiveProfile = "NoActiveProfile";

    private readonly ApplicationDbContext _context;
    private readonly ICardTraderApiService _cardTraderApi;
    private readonly IMarketplaceOfferCache _offerCache;
    private readonly PricingEngine _engine;
    private readonly int _myUserId;

    public ListingPriceSuggestionService(
        ApplicationDbContext context,
        ICardTraderApiService cardTraderApi,
        IMarketplaceOfferCache offerCache,
        PricingEngine engine,
        IConfiguration configuration)
    {
        _context = context;
        _cardTraderApi = cardTraderApi;
        _offerCache = offerCache;
        _engine = engine;
        _myUserId = configuration.GetValue<int>("CardTraderApi:UserId", 0);
    }

    /// <returns>Null se il blueprint non esiste.</returns>
    public async Task<ListingPriceSuggestionDto?> SuggestAsync(
        int blueprintId,
        string? condition,
        string? language,
        bool isFoil,
        bool isSigned,
        CancellationToken cancellationToken = default)
    {
        var blueprint = await _context.Blueprints
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == blueprintId, cancellationToken);

        if (blueprint == null) return null;

        var profile = await _context.PricingProfiles
            .AsNoTracking()
            .Include(p => p.Rules)
            .FirstOrDefaultAsync(p => p.IsActive, cancellationToken);

        if (profile == null)
        {
            return new ListingPriceSuggestionDto
            {
                Outcome = NoActiveProfile,
                Reason = "Nessun profilo di pricing attivo"
            };
        }

        var offers = await _offerCache.GetOrFetchAsync(
            blueprint.CardTraderId,
            MarketplaceOfferCache.ListingWorkflowMaxAge,
            ct => _cardTraderApi.GetMarketplaceProductsAsync(blueprint.CardTraderId, ct),
            cancellationToken);

        var listing = new InventoryItem
        {
            BlueprintId = blueprintId,
            Condition = condition ?? string.Empty,
            Language = language ?? string.Empty,
            IsFoil = isFoil,
            IsSigned = isSigned,
            Quantity = 1,
            Location = string.Empty
        };

        var decision = _engine.SuggestNewListingPrice(listing, offers, profile, _myUserId);
        var priced = decision.Outcome is PricingOutcome.Applied or PricingOutcome.SimulatedDryRun;

        return new ListingPriceSuggestionDto
        {
            SuggestedPrice = priced ? decision.ProposedPrice : null,
            Outcome = decision.Outcome.ToString(),
            Reason = decision.Reason,
            ComparableOffersCount = decision.ComparableOffersCount,
            ReferencePrice = priced ? decision.ReferencePrice : null,
            ProfileName = profile.Name
        };
    }
}
