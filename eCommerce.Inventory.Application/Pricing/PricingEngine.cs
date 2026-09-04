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
    /// Limite di plausibilità del sovrapprezzo applicato da Card Trader all'acquirente.
    /// Non è una regola commerciale: serve solo a riconoscere che le due letture di prezzo
    /// (export e marketplace) non sono dello stesso istante, tipicamente perché il prezzo
    /// è stato appena modificato a mano. In quel caso il rapporto non è utilizzabile.
    /// </summary>
    private const decimal MaxPlausibleMarketMarkup = 1.15m;

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

        // 0. Le due grandezze in gioco non sono la stessa cosa e vanno riportate alla stessa scala.
        //    `ListingPrice` è il prezzo che incasso io, quello dell'export di Card Trader.
        //    Le offerte del marketplace sono invece prezzi lato acquirente, comprensivi del
        //    sovrapprezzo che Card Trader aggiunge: la mia stessa inserzione compare nel feed
        //    a un valore più alto di quello che ho impostato. Confrontarli direttamente mi fa
        //    sembrare più economico di quanto sia, e la posizione calcolata risulta sbagliata.
        //    Il rapporto si ricava dalla mia offerta nel feed, senza dover conoscere la
        //    formula della commissione: è esatto per definizione e si aggiorna da solo.
        var myOffer = item.CardTraderProductId.HasValue
            ? offers.FirstOrDefault(o => o.Id == item.CardTraderProductId.Value)
            : null;

        var myMarketPrice = myOffer != null ? myOffer.PriceCents / 100m : (decimal?)null;
        var marketMarkup = ResolveMarketMarkup(currentPrice, myMarketPrice);

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

        // 4. Scarto delle offerte anomale, in due passaggi complementari.
        //    Prima un filtro di rapporto sulla mediana, che è grossolano ma funziona a qualunque
        //    numero di offerte: intercetta i prezzi di comodo messi altissimi per non sbagliare
        //    e quelli irrealistici dei venditori alle prime armi. Poi lo scarto statistico con
        //    la MAD, più fine ma affidabile solo con qualche punto a disposizione.
        var outliersRejected = 0;

        if (profile.MaxMedianRatio >= 1m && candidates.Count >= 2)
        {
            var beforeCount = candidates.Count;
            candidates = RejectByMedianRatio(candidates, profile.MaxMedianRatio);
            outliersRejected += beforeCount - candidates.Count;
        }

        if (profile.EnableOutlierRejection && candidates.Count >= profile.MinOffersForOutlierRejection)
        {
            var beforeCount = candidates.Count;
            candidates = RejectOutliers(candidates, profile.OutlierMadThreshold);
            outliersRejected += beforeCount - candidates.Count;
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

        // 7. Prezzo di riferimento e scostamenti, in termini di vetrina.
        //    Le regole descrivono una posizione fra i venditori, quindi vanno applicate ai
        //    prezzi che l'acquirente vede; il risultato viene poi riportato al prezzo venditore,
        //    che è l'unico valore che si può scrivere su Card Trader.
        var reference = ResolveReferencePrice(sortedPrices, rule);

        var proposedMarket = reference + rule.AdjustmentAmount;
        if (rule.AdjustmentPercent != 0)
        {
            proposedMarket += proposedMarket * (rule.AdjustmentPercent / 100m);
        }

        var proposed = Math.Round(proposedMarket / marketMarkup, 2, MidpointRounding.AwayFromZero);

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

        var context = DescribeContext(
            currentPrice, myMarketPrice, marketMarkup, reference, proposedMarket,
            sortedPrices, rule, candidates.Count, outliersRejected);

        if (proposed == currentPrice)
        {
            decision.Outcome = PricingOutcome.NoChangeNeeded;
            decision.Reason = $"Prezzo già allineato: resta {currentPrice:0.00} €. {context}";
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

        // 10. Guardrail, asimmetrico per direzione: le due non hanno lo stesso costo se sbagliate.
        if (currentPrice > 0)
        {
            var isIncrease = proposed > currentPrice;
            var limit = isIncrease ? profile.MaxIncreasePercentPerRun : profile.MaxDecreasePercentPerRun;

            if (limit > 0)
            {
                var changePercent = Math.Abs((proposed - currentPrice) / currentPrice * 100m);
                if (changePercent > limit)
                {
                    decision.Outcome = PricingOutcome.BlockedByGuardrail;
                    decision.Reason =
                        $"{(isIncrease ? "Aumento" : "Ribasso")} del {changePercent:0.0}% oltre il massimo consentito " +
                        $"del {limit:0.0}% ({currentPrice:0.00} € → {proposed:0.00} €). {context}";
                    return decision;
                }
            }
        }

        decision.Outcome = profile.DryRun ? PricingOutcome.SimulatedDryRun : PricingOutcome.Applied;
        decision.Reason = $"{currentPrice:0.00} € → {proposed:0.00} €. {context}";

        return decision;
    }

    /// <summary>
    /// Rapporto fra il prezzo che l'acquirente vede sul marketplace e il prezzo che incasso io.
    /// Si ricava dalla mia stessa inserzione presente nel feed, quindi non richiede di conoscere
    /// la formula della commissione di Card Trader, che non è documentata e non è una percentuale
    /// fissa (osservata fra lo 0,8% e l'1,4% a seconda della fascia).
    /// Restituisce 1 quando il rapporto non è ricavabile: in quel caso il confronto avviene
    /// comunque, ma sulla scala del prezzo venditore, e la motivazione lo dichiara.
    /// </summary>
    private static decimal ResolveMarketMarkup(decimal sellerPrice, decimal? marketPrice)
    {
        if (sellerPrice <= 0 || marketPrice is null || marketPrice <= 0) return 1m;

        var ratio = marketPrice.Value / sellerPrice;

        // Il prezzo esposto non può essere inferiore a quello che incasso io. Se lo è, oppure se
        // il sovrapprezzo risulta implausibile, le due letture non sono dello stesso istante:
        // succede quando il prezzo è stato appena cambiato a mano e il marketplace non si è
        // ancora allineato. Meglio non convertire che convertire con un fattore inventato.
        if (ratio < 1m || ratio > MaxPlausibleMarketMarkup) return 1m;

        return ratio;
    }

    /// <summary>
    /// Ricostruisce a parole il percorso che porta al prezzo proposto. Serve a rendere la
    /// decisione verificabile senza rileggere il codice: quale posizione occupo oggi in vetrina,
    /// quale offerta è stata presa a riferimento e come si torna dal prezzo esposto al mio.
    /// </summary>
    private static string DescribeContext(
        decimal sellerPrice,
        decimal? myMarketPrice,
        decimal markup,
        decimal reference,
        decimal proposedMarket,
        List<decimal> sortedComparablePrices,
        PricingRule rule,
        int comparableCount,
        int outliersRejected)
    {
        var parts = new List<string>();

        if (myMarketPrice.HasValue && markup > 1m)
        {
            // A parità di prezzo l'altra offerta compare prima della mia, quindi il confronto è
            // "minore o uguale": è la lettura pessimistica, la stessa che si vede sul sito.
            var position = sortedComparablePrices.Count(p => p <= myMarketPrice.Value) + 1;
            parts.Add(
                $"In vetrina la mia carta costa {myMarketPrice.Value:0.00} € (incasso {sellerPrice:0.00} €, " +
                $"Card Trader aggiunge {myMarketPrice.Value - sellerPrice:0.00} €) e sono in posizione {position} " +
                $"su {comparableCount + 1} offerte comparabili");
        }
        else
        {
            parts.Add(
                $"Sovrapprezzo di Card Trader non ricavabile per questa carta: confronto fatto sul prezzo " +
                $"venditore di {sellerPrice:0.00} € su {comparableCount} offerte comparabili");
        }

        parts.Add($"riferimento {reference:0.00} € ({DescribeReference(rule)} in vetrina)");

        if (rule.AdjustmentAmount != 0 || rule.AdjustmentPercent != 0)
        {
            parts.Add($"con gli scostamenti della regola diventa {proposedMarket:0.00} € in vetrina");
        }

        if (markup > 1m)
        {
            parts.Add($"che al netto del sovrapprezzo vale {proposedMarket / markup:0.00} € per me");
        }

        if (outliersRejected > 0)
        {
            parts.Add($"{outliersRejected} offerte anomale scartate");
        }

        return string.Join(", ", parts) + ".";
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

            if (profile.IncludeOnlyCtZeroSellers && !u.CanSellViaHub) return false;

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
    /// <summary>
    /// Scarta le offerte troppo lontane dalla mediana in rapporto, in entrambe le direzioni.
    /// Serve dove la statistica non arriva: con tre o quattro offerte la MAD non è affidabile,
    /// ma un prezzo dieci volte la mediana resta riconoscibile per quello che è.
    /// La mediana non viene ricalcolata dopo lo scarto: è già robusta per costruzione, e
    /// ricalcolarla renderebbe il filtro dipendente dall'ordine di rimozione.
    /// </summary>
    private static List<CardTraderMarketplaceProductDto> RejectByMedianRatio(
        List<CardTraderMarketplaceProductDto> offers,
        decimal maxRatio)
    {
        var prices = offers.Select(o => o.PriceCents / 100m).OrderBy(p => p).ToList();
        var median = Median(prices);
        if (median <= 0) return offers;

        var upperBound = median * maxRatio;
        var lowerBound = median / maxRatio;

        var kept = offers
            .Where(o =>
            {
                var price = o.PriceCents / 100m;
                return price <= upperBound && price >= lowerBound;
            })
            .ToList();

        // Se il filtro non lascia nulla il dato non è interpretabile: meglio restituire le
        // offerte originali e lasciare che siano i controlli a valle a fermare la decisione,
        // piuttosto che proporre un prezzo basato su un insieme vuoto.
        return kept.Count > 0 ? kept : offers;
    }

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

            case PriceReferenceMode.PercentileOffer:
            {
                // Collocazione relativa sulla scaletta: l'indice si ricava dalla percentuale,
                // quindi la stessa regola resta sensata sia su tre offerte che su trenta.
                var pct = Math.Clamp(rule.Percentile, 0m, 100m);
                var index = (int)Math.Round(
                    (sortedPrices.Count - 1) * (pct / 100m), MidpointRounding.AwayFromZero);
                return sortedPrices[CapIndexBelowMostExpensive(index, sortedPrices.Count)];
            }

            case PriceReferenceMode.NthLowestOffer:
            default:
            {
                var index = Math.Clamp(rule.Position, 1, sortedPrices.Count) - 1;
                return sortedPrices[CapIndexBelowMostExpensive(index, sortedPrices.Count)];
            }
        }
    }

    /// <summary>
    /// Impedisce che il riferimento coincida con l'offerta più cara del mercato.
    /// Una regola di collocazione serve a mettermi dentro la scaletta: quando cade sul massimo
    /// non mi sta posizionando, mi sta dicendo di essere il più caro, e su un mercato sottile
    /// ci finisce da sola. Osservato su quattro carte su undici, e in un caso il massimo era un
    /// prezzo di comodo da 1019 € su un mercato di 73–96 €.
    /// Con una sola offerta comparabile non c'è nulla da limitare.
    /// </summary>
    private static int CapIndexBelowMostExpensive(int index, int count)
        => count <= 1 ? 0 : Math.Min(index, count - 2);

    private static string DescribeReference(PricingRule rule) => rule.ReferenceMode switch
    {
        PriceReferenceMode.LowestOffer => "offerta più bassa",
        PriceReferenceMode.MedianOffer => "mediana",
        PriceReferenceMode.AverageOffer => "media",
        PriceReferenceMode.AverageOfLowestN => $"media delle {rule.Position} più basse",
        PriceReferenceMode.PercentileOffer => $"collocazione al {rule.Percentile:0.#}% della scaletta",
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
