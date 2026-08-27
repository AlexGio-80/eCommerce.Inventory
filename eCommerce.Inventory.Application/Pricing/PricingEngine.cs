using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Application.Pricing;

/// <summary>
/// Motore di calcolo del prezzo. È deliberatamente privo di dipendenze da API, database
/// e logging: riceve la mia carta, le offerte del marketplace e il profilo, e restituisce
/// una decisione motivata. Questo lo rende interamente testabile senza rete.
/// </summary>
public class PricingEngine
{
    /// <summary>
    /// Valuta il prezzo di una carta.
    /// </summary>
    /// <param name="item">La mia carta a inventario.</param>
    /// <param name="offers">Offerte marketplace grezze per il blueprint, comprese le mie.</param>
    /// <param name="profile">Profilo con filtri e guardrail.</param>
    /// <param name="myUserId">Id venditore da escludere: le proprie offerte non sono un riferimento.</param>
    public PricingDecision Evaluate(
        InventoryItem item,
        IReadOnlyList<CardTraderMarketplaceProductDto> offers,
        PricingProfile profile,
        int myUserId)
    {
        var currentPrice = item.ListingPrice;

        // 1. Le mie inserzioni non sono un riferimento di mercato.
        //    Senza questa esclusione il motore inseguirebbe il proprio prezzo verso il basso
        //    a ogni esecuzione, in una spirale che si autoalimenta.
        var candidates = offers.Where(o => o.User?.Id != myUserId).ToList();

        // 2. Solo offerte realmente confrontabili con la mia carta.
        candidates = FilterComparable(candidates, item, profile);

        // 3. Filtri sul tipo di venditore.
        candidates = FilterSellers(candidates, profile);

        if (candidates.Count == 0)
        {
            return PricingDecision.Skip(
                PricingOutcome.InsufficientOffers,
                currentPrice,
                "Nessuna offerta comparabile dopo i filtri su comparabilità e venditori");
        }

        // 4. Scarto delle offerte anomale.
        var outliersRejected = 0;
        if (profile.EnableOutlierRejection && candidates.Count >= profile.MinOffersForOutlierRejection)
        {
            var beforeCount = candidates.Count;
            candidates = RejectOutliers(candidates, profile.OutlierMadThreshold);
            outliersRejected = beforeCount - candidates.Count;
        }

        if (candidates.Count < profile.MinComparableOffers)
        {
            return PricingDecision.Skip(
                PricingOutcome.InsufficientOffers,
                currentPrice,
                $"Solo {candidates.Count} offerte comparabili, il minimo richiesto è {profile.MinComparableOffers}",
                candidates.Count,
                outliersRejected);
        }

        // 5. La regola si sceglie sulla fascia del prezzo CORRENTE della mia carta.
        var rule = SelectRule(profile, currentPrice);
        if (rule == null)
        {
            return PricingDecision.Skip(
                PricingOutcome.NoMatchingRule,
                currentPrice,
                $"Nessuna regola attiva copre il prezzo corrente di {currentPrice:0.00} €",
                candidates.Count,
                outliersRejected);
        }

        // 6. Mercato troppo sottile per la posizione richiesta.
        var sortedPrices = candidates.Select(o => o.PriceCents / 100m).OrderBy(p => p).ToList();

        if (profile.SkipWhenFewerOffersThanPosition &&
            IsPositional(rule.ReferenceMode) &&
            sortedPrices.Count < rule.Position)
        {
            return PricingDecision.Skip(
                PricingOutcome.InsufficientOffers,
                currentPrice,
                $"La regola chiede la posizione {rule.Position} ma le offerte comparabili sono {sortedPrices.Count}: " +
                "posizionarsi qui significherebbe allinearsi all'offerta più cara del mercato",
                sortedPrices.Count,
                outliersRejected);
        }

        // 7. Prezzo di riferimento e scostamenti.
        var reference = ResolveReferencePrice(sortedPrices, rule);

        var proposed = reference + rule.AdjustmentAmount;
        if (rule.AdjustmentPercent != 0)
        {
            proposed += proposed * (rule.AdjustmentPercent / 100m);
        }

        proposed = Math.Round(proposed, 2, MidpointRounding.AwayFromZero);

        // 8. Il prezzo minimo non è mai valicabile.
        if (proposed < profile.MinPrice)
        {
            proposed = profile.MinPrice;
        }

        var decision = new PricingDecision
        {
            OldPrice = currentPrice,
            ProposedPrice = proposed,
            ReferencePrice = reference,
            ComparableOffersCount = candidates.Count,
            OutliersRejectedCount = outliersRejected,
            RuleId = rule.Id,
            Rule = rule
        };

        if (proposed == currentPrice)
        {
            decision.Outcome = PricingOutcome.NoChangeNeeded;
            decision.Reason = $"Prezzo già allineato a {proposed:0.00} € (riferimento {reference:0.00} € su {candidates.Count} offerte)";
            return decision;
        }

        // 9. Direzione consentita dalla regola.
        if (proposed > currentPrice && !rule.CanIncrease)
        {
            decision.Outcome = PricingOutcome.BlockedByDirection;
            decision.Reason = $"La regola non consente aumenti: {currentPrice:0.00} € → {proposed:0.00} € scartato";
            return decision;
        }

        if (proposed < currentPrice && !rule.CanDecrease)
        {
            decision.Outcome = PricingOutcome.BlockedByDirection;
            decision.Reason = $"La regola non consente ribassi: {currentPrice:0.00} € → {proposed:0.00} € scartato";
            return decision;
        }

        // 10. Guardrail sulla variazione massima.
        if (currentPrice > 0 && profile.MaxChangePercentPerRun > 0)
        {
            var changePercent = Math.Abs((proposed - currentPrice) / currentPrice * 100m);
            if (changePercent > profile.MaxChangePercentPerRun)
            {
                decision.Outcome = PricingOutcome.BlockedByGuardrail;
                decision.Reason =
                    $"Variazione del {changePercent:0.0}% oltre il massimo consentito del {profile.MaxChangePercentPerRun:0.0}% " +
                    $"({currentPrice:0.00} € → {proposed:0.00} €). Verificare il dato di mercato prima di applicare.";
                return decision;
            }
        }

        decision.Outcome = profile.DryRun ? PricingOutcome.SimulatedDryRun : PricingOutcome.Applied;
        decision.Reason =
            $"{currentPrice:0.00} € → {proposed:0.00} € | riferimento {reference:0.00} € " +
            $"({DescribeReference(rule)}) su {candidates.Count} offerte comparabili" +
            (outliersRejected > 0 ? $", {outliersRejected} anomale scartate" : string.Empty);

        return decision;
    }

    /// <summary>
    /// Tiene solo le offerte confrontabili con la mia carta. Confrontare una Near Mint
    /// inglese con una Played tedesca produrrebbe un prezzo privo di senso.
    /// </summary>
    private static List<CardTraderMarketplaceProductDto> FilterComparable(
        List<CardTraderMarketplaceProductDto> offers,
        InventoryItem item,
        PricingProfile profile)
    {
        return offers.Where(o =>
        {
            var p = o.Properties;

            if (profile.ExcludeSigned && p.IsSigned) return false;
            if (profile.ExcludeAltered && p.IsAltered) return false;
            if (profile.ExcludeGraded && o.Graded) return false;

            if (profile.MatchFoil && p.IsFoil != item.IsFoil) return false;

            if (profile.MatchCondition &&
                !string.IsNullOrWhiteSpace(item.Condition) &&
                !ConditionMatches(p.Condition, item.Condition))
            {
                return false;
            }

            if (profile.MatchLanguage &&
                !string.IsNullOrWhiteSpace(item.Language) &&
                !LanguageMatches(p.Language, item.Language))
            {
                return false;
            }

            return o.Quantity > 0;
        }).ToList();
    }

    private static List<CardTraderMarketplaceProductDto> FilterSellers(
        List<CardTraderMarketplaceProductDto> offers,
        PricingProfile profile)
    {
        var allowedCountries = ParseCountries(profile.CountryCodesCsv);

        return offers.Where(o =>
        {
            var u = o.User;
            if (u == null) return false;

            if (profile.ExcludeVacationSellers && o.OnVacation) return false;

            var isPro = string.Equals(u.UserType, "pro", StringComparison.OrdinalIgnoreCase);
            if (isPro && !profile.IncludeProSellers) return false;
            if (!isPro && !profile.IncludeNormalSellers) return false;

            // Capacità sconosciuta: se è stata richiesta una soglia minima l'offerta viene
            // esclusa, perché il filtro serve proprio a tenere fuori i venditori che non
            // sappiamo valutare. Senza soglia impostata il campo è irrilevante.
            if (profile.MinSellerDailyCapacity.HasValue &&
                (u.MaxSellableIn24hQuantity ?? 0) < profile.MinSellerDailyCapacity.Value)
            {
                return false;
            }

            if (allowedCountries.Count > 0 &&
                !allowedCountries.Contains(u.CountryCode ?? string.Empty))
            {
                return false;
            }

            return true;
        }).ToList();
    }

    /// <summary>
    /// Scarta le offerte anomale usando la deviazione assoluta mediana (MAD).
    /// La MAD è preferita alla deviazione standard perché non viene distorta dagli
    /// stessi outlier che deve individuare: basta un prezzo assurdo per gonfiare la
    /// deviazione standard al punto da rendere "normale" qualunque valore.
    /// </summary>
    private static List<CardTraderMarketplaceProductDto> RejectOutliers(
        List<CardTraderMarketplaceProductDto> offers,
        decimal madThreshold)
    {
        var prices = offers.Select(o => o.PriceCents / 100m).OrderBy(p => p).ToList();
        var median = Median(prices);

        var deviations = prices.Select(p => Math.Abs(p - median)).OrderBy(d => d).ToList();
        var mad = Median(deviations);

        // Tutte le offerte allo stesso prezzo: nessuna dispersione, nessun outlier.
        if (mad == 0) return offers;

        // 1.4826 rende la MAD confrontabile con la deviazione standard di una normale,
        // così la soglia si legge nello stesso modo (es. "3 sigma").
        var scaledMad = mad * 1.4826m;

        return offers.Where(o =>
        {
            var price = o.PriceCents / 100m;
            var score = Math.Abs(price - median) / scaledMad;
            return score <= madThreshold;
        }).ToList();
    }

    /// <summary>Modalità che dipendono dal numero di venditori presenti sul mercato.</summary>
    private static bool IsPositional(PriceReferenceMode mode)
        => mode is PriceReferenceMode.NthLowestOffer or PriceReferenceMode.AverageOfLowestN;

    private static PricingRule? SelectRule(PricingProfile profile, decimal currentPrice)
    {
        return profile.Rules
            .Where(r => r.IsActive && currentPrice >= r.FromPrice && currentPrice <= r.ToPrice)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .FirstOrDefault();
    }

    private static decimal ResolveReferencePrice(List<decimal> sortedPrices, PricingRule rule)
    {
        switch (rule.ReferenceMode)
        {
            case PriceReferenceMode.LowestOffer:
                return sortedPrices[0];

            case PriceReferenceMode.MedianOffer:
                return Median(sortedPrices);

            case PriceReferenceMode.AverageOffer:
                return Math.Round(sortedPrices.Average(), 2, MidpointRounding.AwayFromZero);

            case PriceReferenceMode.AverageOfLowestN:
            {
                var n = Math.Clamp(rule.Position, 1, sortedPrices.Count);
                return Math.Round(sortedPrices.Take(n).Average(), 2, MidpointRounding.AwayFromZero);
            }

            case PriceReferenceMode.NthLowestOffer:
            default:
            {
                // Se il mercato ha meno venditori della posizione richiesta si usa l'ultimo
                // disponibile: meglio posizionarsi in fondo a una lista corta che rinunciare.
                var index = Math.Clamp(rule.Position, 1, sortedPrices.Count) - 1;
                return sortedPrices[index];
            }
        }
    }

    private static string DescribeReference(PricingRule rule) => rule.ReferenceMode switch
    {
        PriceReferenceMode.LowestOffer => "offerta più bassa",
        PriceReferenceMode.MedianOffer => "mediana",
        PriceReferenceMode.AverageOffer => "media",
        PriceReferenceMode.AverageOfLowestN => $"media delle {rule.Position} più basse",
        _ => $"{rule.Position}ª offerta più bassa"
    };

    private static decimal Median(List<decimal> sortedValues)
    {
        if (sortedValues.Count == 0) return 0m;
        var mid = sortedValues.Count / 2;
        return sortedValues.Count % 2 == 1
            ? sortedValues[mid]
            : Math.Round((sortedValues[mid - 1] + sortedValues[mid]) / 2m, 2, MidpointRounding.AwayFromZero);
    }

    private static HashSet<string> ParseCountries(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Card Trader restituisce le condizioni in inglese esteso ("Near Mint"), mentre
    /// l'inventario locale può usare la stessa forma o l'abbreviazione: si normalizza prima di confrontare.
    /// </summary>
    private static bool ConditionMatches(string offerCondition, string myCondition)
        => NormalizeCondition(offerCondition) == NormalizeCondition(myCondition);

    private static string NormalizeCondition(string condition)
    {
        var c = condition.Trim().ToLowerInvariant().Replace("-", " ").Replace("_", " ");
        return c switch
        {
            "mint" or "m" => "mint",
            "near mint" or "nm" => "near mint",
            "slightly played" or "sp" => "slightly played",
            "moderately played" or "mp" => "moderately played",
            "played" or "pl" => "played",
            "heavily played" or "hp" => "heavily played",
            "poor" or "po" => "poor",
            _ => c
        };
    }

    /// <summary>
    /// Il marketplace usa codici brevi ("en", "it"), l'inventario nomi estesi ("English").
    /// </summary>
    private static bool LanguageMatches(string offerLanguage, string myLanguage)
        => NormalizeLanguage(offerLanguage) == NormalizeLanguage(myLanguage);

    private static string NormalizeLanguage(string language)
    {
        var l = language.Trim().ToLowerInvariant();
        return l switch
        {
            "en" or "english" => "en",
            "it" or "italian" or "italiano" => "it",
            "de" or "german" or "deutsch" => "de",
            "fr" or "french" or "français" or "francais" => "fr",
            "es" or "spanish" or "español" or "espanol" => "es",
            "pt" or "portuguese" or "português" or "portugues" => "pt",
            "ru" or "russian" => "ru",
            "ja" or "jp" or "japanese" => "ja",
            "zh" or "chinese" or "chinese simplified" => "zh",
            "ko" or "korean" => "ko",
            _ => l
        };
    }
}
