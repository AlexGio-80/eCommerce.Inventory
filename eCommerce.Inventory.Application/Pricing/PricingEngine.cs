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
    /// Limite di plausibilità del sovrapprezzo applicato da Card Trader all'acquirente:
    /// una quota fissa più una quota proporzionale al prezzo. Non è una regola commerciale:
    /// serve solo a riconoscere che le due letture di prezzo (export e marketplace) non sono
    /// dello stesso istante, tipicamente perché il prezzo è appena cambiato e il feed del
    /// marketplace espone ancora il valore vecchio. In quel caso la differenza non è utilizzabile.
    /// Tarato su 1.089 coppie reali del 2026-09-25: il sovrapprezzo massimo osservato sta
    /// sotto questa soglia a ogni fascia di prezzo.
    /// </summary>
    private const decimal MaxPlausibleMarketFeeFixed = 0.20m;
    private const decimal MaxPlausibleMarketFeePercent = 5m;

    /// <summary>
    /// Sovrapprezzo da sottrarre quando non si può ricavare dalla mia offerta: il minimo
    /// osservato, applicato a tutte le carte fino a 0,25 €. Sulle carte più care il sovrapprezzo
    /// reale è maggiore, quindi il prezzo proposto resta al più un po' alto, mai sotto il dovuto.
    /// Sottrarre zero, come si faceva prima, sul bulk significava prezzare quasi al doppio.
    /// </summary>
    private const decimal FallbackMarketFee = 0.09m;

    /// <summary>
    /// Valuta il prezzo di una carta.
    /// </summary>
    /// <param name="item">La mia carta a inventario.</param>
    /// <param name="offers">Offerte marketplace grezze per il blueprint, comprese le mie.</param>
    /// <param name="profile">Profilo con filtri e guardrail.</param>
    /// <param name="myUserId">Id venditore da escludere: le proprie offerte non sono un riferimento.</param>
    /// <param name="bypassGuardrail">
    /// Ignora il limite di variazione massima per questa valutazione. Riservato a un gesto
    /// esplicito su una carta già vista bloccata nello storico: il guardrail resta attivo per
    /// tutte le altre, qui si accetta consapevolmente lo scarto più ampio.
    /// </param>
    /// <param name="isNewListing">
    /// Carta a cui l'autopricer non ha mai scritto né confermato un prezzo. Il suo prezzo è quello
    /// di caricamento, spesso alto di proposito: non dice nulla sul valore della carta. Quindi la
    /// fascia si ricalcola sul prezzo proposto, e guardrail e direzione non si applicano: non c'è
    /// un prezzo di mercato da proteggere.
    /// </param>
    public PricingDecision Evaluate(
        InventoryItem item,
        IReadOnlyList<CardTraderMarketplaceProductDto> offers,
        PricingProfile profile,
        int myUserId,
        bool bypassGuardrail = false,
        bool isNewListing = false)
    {
        var currentPrice = item.ListingPrice;

        // 0. Le due grandezze in gioco non sono la stessa cosa e vanno riportate alla stessa scala.
        //    `ListingPrice` è il prezzo che incasso io, quello dell'export di Card Trader.
        //    Le offerte del marketplace sono invece prezzi lato acquirente, comprensivi del
        //    sovrapprezzo che Card Trader aggiunge: la mia stessa inserzione compare nel feed
        //    a un valore più alto di quello che ho impostato. Confrontarli direttamente mi fa
        //    sembrare più economico di quanto sia, e la posizione calcolata risulta sbagliata.
        //    Il sovrapprezzo è un importo a scaglioni, non una percentuale: 0,09 € fino a
        //    0,25 €, 0,10 € fino a circa 5 €, poi qualche decina di centesimi. Si ricava dalla
        //    mia offerta nel feed come differenza, senza dover conoscere la tabella.
        var myOffer = item.CardTraderProductId.HasValue
            ? offers.FirstOrDefault(o => o.Id == item.CardTraderProductId.Value)
            : null;

        var myMarketPrice = myOffer != null ? myOffer.PriceCents / 100m : (decimal?)null;
        var (marketFee, feeDerived) = ResolveMarketFee(currentPrice, myMarketPrice);

        // 1. Le mie inserzioni non sono un riferimento di mercato.
        //    Senza questa esclusione il motore inseguirebbe il proprio prezzo verso il basso
        //    a ogni esecuzione, in una spirale che si autoalimenta.
        var candidates = offers.Where(o => o.User?.Id != myUserId).ToList();

        // 2. Solo offerte realmente confrontabili con la mia carta.
        var comparable = FilterComparable(candidates, item, profile);

        // 3. La regola si sceglie sulla fascia del prezzo CORRENTE della mia carta. Più regole
        //    sulla stessa fascia sono una catena: se il mercato non basta per la prima (filtri
        //    sui venditori, minimo di offerte, posizione richiesta) si prova la successiva.
        var pick = PickRule(comparable, profile, currentPrice);
        if (pick.Skip != null) return pick.Skip;

        // 4-5. Prezzo proposto con la regola scelta.
        var price = ProposePrice(pick, profile, marketFee);

        // Sulla carta nuova il prezzo corrente è quello di caricamento, e la fascia scelta su
        // di esso è casuale: una terra base caricata a 5 € pescherebbe la regola delle carte da
        // 1-25 €. Se il prezzo proposto cade in un'altra fascia si rifà il calcolo con quella.
        // Una sola volta: il secondo prezzo nasce già dalla regola giusta per il suo valore.
        string? newListingNote = null;
        if (isNewListing && (price.Proposed < pick.Rule!.FromPrice || price.Proposed > pick.Rule.ToPrice))
        {
            var repick = PickRule(comparable, profile, price.Proposed);
            if (repick.Skip == null)
            {
                newListingNote =
                    $"Inserzione nuova: fascia scelta sul prezzo di mercato stimato ({price.Proposed:0.00} €) " +
                    $"invece che su quello di caricamento ({currentPrice:0.00} €).";
                pick = repick;
                price = ProposePrice(pick, profile, marketFee);
            }
        }

        var rule = pick.Rule!;
        var chosen = pick.Market!;
        var failures = pick.Failures;
        var outliersRejected = chosen.OutliersRejected;
        var sortedPrices = chosen.SortedPrices;
        candidates = chosen.Offers;
        var reference = price.Reference;
        var proposedMarket = price.ProposedMarket;
        var proposed = price.Proposed;

        var decision = new PricingDecision
        {
            OldPrice = currentPrice,
            ProposedPrice = proposed,
            ReferencePrice = reference,
            ReferenceSellerPrice = Math.Round(reference - marketFee, 2, MidpointRounding.AwayFromZero),
            ComparableOffersCount = candidates.Count,
            OutliersRejectedCount = outliersRejected,
            RuleId = rule.Id,
            Rule = rule
        };

        var context = DescribeContext(
            currentPrice, myMarketPrice, marketFee, feeDerived, reference, proposedMarket,
            sortedPrices, rule, candidates.Count, outliersRejected);

        if (failures.Count > 0)
        {
            context = $"Regola di ripiego {failures.Count} (priorità {rule.Priority}), perché: " +
                      string.Join(" ", failures.Select(f => f.Failure!.TrimEnd('.') + ".")) + " " + context;
        }

        if (newListingNote != null)
        {
            context = newListingNote + " " + context;
        }

        if (proposed == currentPrice)
        {
            decision.Outcome = PricingOutcome.NoChangeNeeded;
            decision.Reason = $"Prezzo già allineato: resta {currentPrice:0.00} €. {context}";
            return decision;
        }

        // Carta nuova: il prezzo di caricamento non è un prezzo di mercato da proteggere, quindi
        // né la direzione né il guardrail hanno senso. Si scrive il prezzo di mercato e basta.
        if (isNewListing)
        {
            decision.Outcome = profile.DryRun ? PricingOutcome.SimulatedDryRun : PricingOutcome.Applied;
            decision.Reason = $"Inserzione nuova, guardrail non applicato: {currentPrice:0.00} € → {proposed:0.00} €. {context}";
            return decision;
        }

        // 6. Direzione consentita dalla regola.
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

        // 7. Guardrail, asimmetrico per direzione: le due non hanno lo stesso costo se sbagliate.
        var guardrailBypassed = false;

        // Sotto la soglia in euro le percentuali non dicono nulla di utile: sul bulk qualunque
        // riallineamento sembra enorme, e bloccarlo lo fermerebbe per sempre.
        var withinExemptAmount = Math.Abs(proposed - currentPrice) <= profile.GuardrailExemptAmount;

        if (currentPrice > 0 && !withinExemptAmount)
        {
            var isIncrease = proposed > currentPrice;
            var limit = isIncrease ? profile.MaxIncreasePercentPerRun : profile.MaxDecreasePercentPerRun;

            if (limit > 0)
            {
                var changePercent = Math.Abs((proposed - currentPrice) / currentPrice * 100m);
                if (changePercent > limit)
                {
                    if (!bypassGuardrail)
                    {
                        decision.Outcome = PricingOutcome.BlockedByGuardrail;
                        decision.Reason =
                            $"{(isIncrease ? "Aumento" : "Ribasso")} del {changePercent:0.0}% oltre il massimo consentito " +
                            $"del {limit:0.0}% ({currentPrice:0.00} € → {proposed:0.00} €). {context}";
                        return decision;
                    }

                    guardrailBypassed = true;
                }
            }
        }

        decision.Outcome = profile.DryRun ? PricingOutcome.SimulatedDryRun : PricingOutcome.Applied;
        decision.Reason = guardrailBypassed
            ? $"Guardrail ignorato su richiesta esplicita. {currentPrice:0.00} € → {proposed:0.00} €. {context}"
            : $"{currentPrice:0.00} € → {proposed:0.00} €. {context}";

        return decision;
    }

    /// <summary>
    /// Differenza fra il prezzo che l'acquirente vede sul marketplace e il prezzo che incasso io.
    /// Si ricava dalla mia stessa inserzione presente nel feed, quindi non richiede di conoscere
    /// la tabella della commissione di Card Trader, che non è documentata.
    ///
    /// È una differenza e non un rapporto perché il sovrapprezzo è un importo a scaglioni:
    /// sul bulk vale 0,09 € su 0,10 €, cioè un rapporto di 1,9. Il vecchio limite di plausibilità
    /// sul rapporto (1,15) scartava tutte le carte sotto 0,25 € e le confrontava senza
    /// conversione, prezzandole circa 0,09 € sopra la posizione configurata.
    ///
    /// Quando non è ricavabile restituisce <see cref="FallbackMarketFee"/>, e la motivazione lo dichiara.
    /// </summary>
    private static (decimal Fee, bool Derived) ResolveMarketFee(decimal sellerPrice, decimal? marketPrice)
    {
        if (sellerPrice <= 0 || marketPrice is null || marketPrice <= 0) return (FallbackMarketFee, false);

        var fee = marketPrice.Value - sellerPrice;
        var maxPlausible = MaxPlausibleMarketFeeFixed + sellerPrice * MaxPlausibleMarketFeePercent / 100m;

        // Il prezzo esposto è sempre maggiore di quello che incasso io. Se non lo è, oppure se
        // il sovrapprezzo risulta implausibile, le due letture non sono dello stesso istante:
        // succede quando il prezzo è appena cambiato e il feed del marketplace espone ancora il
        // valore vecchio. Meglio il minimo noto che una differenza inventata.
        if (fee <= 0 || fee > maxPlausible) return (FallbackMarketFee, false);

        return (fee, true);
    }

    /// <summary>
    /// Ricostruisce a parole il percorso che porta al prezzo proposto. Serve a rendere la
    /// decisione verificabile senza rileggere il codice: quale posizione occupo oggi in vetrina,
    /// quale offerta è stata presa a riferimento e come si torna dal prezzo esposto al mio.
    /// </summary>
    private static string DescribeContext(
        decimal sellerPrice,
        decimal? myMarketPrice,
        decimal fee,
        bool feeDerived,
        decimal reference,
        decimal proposedMarket,
        List<decimal> sortedComparablePrices,
        PricingRule rule,
        int comparableCount,
        int outliersRejected)
    {
        var parts = new List<string>();

        if (myMarketPrice.HasValue && feeDerived)
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
                $"Sovrapprezzo di Card Trader non ricavabile per questa carta, usato il minimo noto di " +
                $"{fee:0.00} € (incasso {sellerPrice:0.00} €, {comparableCount} offerte comparabili)");
        }

        parts.Add($"riferimento {reference:0.00} € ({DescribeReference(rule)} in vetrina)");

        if (rule.AdjustmentAmount != 0 || rule.AdjustmentPercent != 0)
        {
            parts.Add($"con gli scostamenti della regola diventa {proposedMarket:0.00} € in vetrina");
        }

        parts.Add($"che al netto del sovrapprezzo vale {proposedMarket - fee:0.00} € per me");

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
        PricingProfile profile,
        bool onlyCtZero)
    {
        var allowedCountries = ParseCountries(profile.CountryCodesCsv);

        return offers.Where(o =>
        {
            var u = o.User;
            if (u == null) return false;

            if (profile.ExcludeVacationSellers && o.OnVacation) return false;

            if (onlyCtZero && !u.CanSellViaHub) return false;

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

    /// <summary>
    /// Prima regola della catena della fascia di <paramref name="price"/> che ha mercato a sufficienza.
    /// Se nessuna lo ha, <see cref="RulePick.Skip"/> contiene già la decisione da restituire.
    /// </summary>
    private static RulePick PickRule(List<CardTraderMarketplaceProductDto> comparable, PricingProfile profile, decimal price)
    {
        var rules = SelectRules(profile, price);
        if (rules.Count == 0)
        {
            // Senza regole conta comunque prima il mercato: un mercato vuoto è un'informazione
            // più utile di "nessuna regola".
            var market = PrepareMarket(comparable, profile, null);
            if (market.Failure != null)
            {
                return RulePick.Skipped(PricingDecision.Skip(
                    PricingOutcome.InsufficientOffers, price, market.Failure,
                    market.ComparableCount, market.OutliersRejected));
            }

            return RulePick.Skipped(PricingDecision.Skip(
                PricingOutcome.NoMatchingRule,
                price,
                $"Nessuna regola attiva copre il prezzo corrente di {price:0.00} €",
                market.ComparableCount,
                market.OutliersRejected));
        }

        var failures = new List<MarketView>();
        foreach (var rule in rules)
        {
            var market = PrepareMarket(comparable, profile, rule);
            if (market.Failure == null) return new RulePick(rule, market, failures, null);
            failures.Add(market);
        }

        var last = failures[^1];
        var reason = failures.Count == 1
            ? last.Failure!
            : string.Join(" ", failures.Select((f, i) =>
                $"{(i == 0 ? "Regola principale" : $"Ripiego {i}")}: {f.Failure}."));

        return RulePick.Skipped(PricingDecision.Skip(
            PricingOutcome.InsufficientOffers, price, reason, last.ComparableCount, last.OutliersRejected));
    }

    /// <summary>
    /// Prezzo di riferimento e scostamenti, in termini di vetrina. Le regole descrivono una
    /// posizione fra i venditori, quindi vanno applicate ai prezzi che l'acquirente vede; il
    /// risultato viene poi riportato al prezzo venditore, che è l'unico valore che si può scrivere
    /// su Card Trader. Il prezzo minimo non è mai valicabile.
    /// </summary>
    private static PriceProposal ProposePrice(RulePick pick, PricingProfile profile, decimal marketFee)
    {
        var rule = pick.Rule!;
        var reference = ResolveReferencePrice(pick.Market!.SortedPrices, rule);

        var proposedMarket = reference + rule.AdjustmentAmount;
        if (rule.AdjustmentPercent != 0)
        {
            proposedMarket += proposedMarket * (rule.AdjustmentPercent / 100m);
        }

        var proposed = Math.Round(proposedMarket - marketFee, 2, MidpointRounding.AwayFromZero);
        if (proposed < profile.MinPrice)
        {
            proposed = profile.MinPrice;
        }

        return new PriceProposal(reference, proposedMarket, proposed);
    }

    /// <param name="Skip">Decisione già presa quando nessuna regola ha mercato; altrimenti null.</param>
    private sealed record RulePick(PricingRule? Rule, MarketView? Market, List<MarketView> Failures, PricingDecision? Skip)
    {
        public static RulePick Skipped(PricingDecision decision) => new(null, null, new List<MarketView>(), decision);
    }

    private sealed record PriceProposal(decimal Reference, decimal ProposedMarket, decimal Proposed);

    /// <summary>Regole attive della fascia, nell'ordine in cui provarle: la principale e poi i ripieghi.</summary>
    private static List<PricingRule> SelectRules(PricingProfile profile, decimal currentPrice)
    {
        return profile.Rules
            .Where(r => r.IsActive && currentPrice >= r.FromPrice && currentPrice <= r.ToPrice)
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Id)
            .ToList();
    }

    /// <summary>
    /// Le offerte su cui lavora una regola: filtro sui venditori, scarto delle anomalie, minimo di
    /// offerte e posizione richiesta. <see cref="MarketView.Failure"/> dice perché il mercato non
    /// basta, e in una catena fa passare alla regola successiva.
    /// </summary>
    /// <param name="rule">Null quando nessuna regola copre la fascia: valgono le impostazioni del profilo.</param>
    private static MarketView PrepareMarket(
        List<CardTraderMarketplaceProductDto> comparable, PricingProfile profile, PricingRule? rule)
    {
        var onlyCtZero = rule?.OnlyCtZeroSellers ?? profile.IncludeOnlyCtZeroSellers;
        var minOffers = rule?.MinComparableOffers ?? profile.MinComparableOffers;
        var ctZeroNote = onlyCtZero ? " (solo venditori Card Trader Zero)" : "";

        var offers = FilterSellers(comparable, profile, onlyCtZero);
        if (offers.Count == 0)
        {
            return MarketView.Fail(
                "Nessuna offerta comparabile dopo i filtri su comparabilità e venditori" + ctZeroNote, 0, 0);
        }

        // Scarto delle offerte anomale, in due passaggi complementari.
        // Prima un filtro di rapporto sulla mediana, che è grossolano ma funziona a qualunque
        // numero di offerte: intercetta i prezzi di comodo messi altissimi per non sbagliare
        // e quelli irrealistici dei venditori alle prime armi. Poi lo scarto statistico con
        // la MAD, più fine ma affidabile solo con qualche punto a disposizione.
        var outliersRejected = 0;

        if (profile.MaxMedianRatio >= 1m && offers.Count >= 2)
        {
            var beforeCount = offers.Count;
            offers = RejectByMedianRatio(offers, profile.MaxMedianRatio);
            outliersRejected += beforeCount - offers.Count;
        }

        if (profile.EnableOutlierRejection && offers.Count >= profile.MinOffersForOutlierRejection)
        {
            var beforeCount = offers.Count;
            offers = RejectOutliers(offers, profile.OutlierMadThreshold);
            outliersRejected += beforeCount - offers.Count;
        }

        if (offers.Count < minOffers)
        {
            return MarketView.Fail(
                $"Solo {offers.Count} offerte comparabili, il minimo richiesto è {minOffers}{ctZeroNote}",
                offers.Count, outliersRejected);
        }

        var sortedPrices = offers.Select(o => o.PriceCents / 100m).OrderBy(p => p).ToList();

        // Mercato troppo sottile per la posizione richiesta.
        if (rule != null &&
            profile.SkipWhenFewerOffersThanPosition &&
            IsPositional(rule.ReferenceMode) &&
            sortedPrices.Count < rule.Position)
        {
            return MarketView.Fail(
                $"La regola chiede la posizione {rule.Position} ma le offerte comparabili sono {sortedPrices.Count}: " +
                "posizionarsi qui significherebbe allinearsi all'offerta più cara del mercato",
                sortedPrices.Count, outliersRejected);
        }

        return new MarketView(offers, sortedPrices, offers.Count, outliersRejected, null);
    }

    /// <param name="ComparableCount">Offerte rimaste dopo filtri e scarti, anche quando il mercato non basta.</param>
    /// <param name="Failure">Perché il mercato non basta per la regola; null se basta.</param>
    private sealed record MarketView(
        List<CardTraderMarketplaceProductDto> Offers,
        List<decimal> SortedPrices,
        int ComparableCount,
        int OutliersRejected,
        string? Failure)
    {
        public static MarketView Fail(string reason, int comparableCount, int outliersRejected) =>
            new(new List<CardTraderMarketplaceProductDto>(), new List<decimal>(), comparableCount, outliersRejected, reason);
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
