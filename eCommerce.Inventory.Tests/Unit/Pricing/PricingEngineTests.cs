using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Pricing;
using eCommerce.Inventory.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Pricing;

public class PricingEngineTests
{
    private const int MyUserId = 1939;

    private static PricingProfile Profile(params PricingRule[] rules)
    {
        var profile = new PricingProfile
        {
            Name = "Test",
            DryRun = false,
            MinPrice = 0.05m,
            MaxChangePercentPerRun = 1000m, // disattivato salvo test dedicati
            MinComparableOffers = 1,
            MinOffersForOutlierRejection = 5,
            EnableOutlierRejection = false
        };

        foreach (var r in rules) profile.Rules.Add(r);
        return profile;
    }

    private static PricingRule NthLowestRule(decimal from, decimal to, int position, decimal adjust = 0m)
        => new()
        {
            Id = 1,
            FromPrice = from,
            ToPrice = to,
            ReferenceMode = PriceReferenceMode.NthLowestOffer,
            Position = position,
            AdjustmentAmount = adjust,
            CanIncrease = true,
            CanDecrease = true,
            IsActive = true
        };

    private static InventoryItem Item(decimal price, string condition = "Near Mint", string language = "English", bool foil = false)
        => new()
        {
            Id = 1,
            BlueprintId = 10,
            ListingPrice = price,
            Condition = condition,
            Language = language,
            IsFoil = foil,
            Quantity = 1
        };

    private static CardTraderMarketplaceProductDto Offer(
        decimal price,
        int userId = 999,
        string userType = "normal",
        string country = "IT",
        string condition = "Near Mint",
        string language = "en",
        bool foil = false,
        bool signed = false,
        bool graded = false,
        bool onVacation = false,
        int capacity = 10,
        int quantity = 1)
        => new()
        {
            Id = Random.Shared.Next(1, 100000),
            PriceCents = (int)(price * 100),
            Quantity = quantity,
            Graded = graded,
            OnVacation = onVacation,
            PropertiesHash = new Dictionary<string, object>
            {
                ["condition"] = condition,
                ["mtg_language"] = language,
                ["mtg_foil"] = foil,
                ["signed"] = signed,
                ["altered"] = false
            },
            User = new CardTraderMarketplaceUserDto
            {
                Id = userId,
                UserType = userType,
                CountryCode = country,
                MaxSellableIn24hQuantity = capacity
            }
        };

    [Fact]
    public void Esclude_le_mie_offerte_dal_riferimento()
    {
        // Se la mia inserzione entrasse nel calcolo, il motore inseguirebbe se stesso
        // verso il basso a ogni esecuzione.
        var engine = new PricingEngine();
        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(1.00m, userId: MyUserId), // la mia, la più bassa
            Offer(5.00m),
            Offer(6.00m)
        };

        var decision = engine.Evaluate(Item(1.00m), offers, Profile(NthLowestRule(0.02m, 100m, 1)), MyUserId);

        decision.ReferencePrice.Should().Be(5.00m, "la propria offerta non è un riferimento di mercato");
        decision.ComparableOffersCount.Should().Be(2);
    }

    [Fact]
    public void Posizionamento_ennesima_offerta_con_scostamento()
    {
        var engine = new PricingEngine();
        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(10.00m), Offer(12.00m), Offer(14.00m), Offer(16.00m)
        };

        // Voglio essere il 2° venditore, un centesimo sotto.
        var decision = engine.Evaluate(Item(20.00m), offers, Profile(NthLowestRule(1.01m, 25m, 2, -0.01m)), MyUserId);

        decision.ReferencePrice.Should().Be(12.00m);
        decision.ProposedPrice.Should().Be(11.99m);
        decision.Outcome.Should().Be(PricingOutcome.Applied);
    }

    [Fact]
    public void Non_scende_mai_sotto_il_prezzo_minimo()
    {
        var engine = new PricingEngine();
        var offers = new List<CardTraderMarketplaceProductDto> { Offer(0.02m), Offer(0.03m) };

        var decision = engine.Evaluate(Item(0.50m), offers, Profile(NthLowestRule(0.02m, 1m, 1, -0.01m)), MyUserId);

        decision.ProposedPrice.Should().Be(0.05m, "il prezzo minimo del profilo non è valicabile");
    }

    [Fact]
    public void Scarta_offerte_non_comparabili_per_condizione_lingua_e_foil()
    {
        var engine = new PricingEngine();
        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(1.00m, condition: "Played"),      // condizione diversa
            Offer(2.00m, language: "de"),           // lingua diversa
            Offer(3.00m, foil: true),               // foil diverso
            Offer(4.00m, signed: true),             // firmata
            Offer(5.00m, graded: true),             // gradata
            Offer(9.00m)                            // l'unica comparabile
        };

        var decision = engine.Evaluate(Item(20.00m), offers, Profile(NthLowestRule(1.01m, 25m, 1)), MyUserId);

        decision.ComparableOffersCount.Should().Be(1);
        decision.ReferencePrice.Should().Be(9.00m);
    }

    [Fact]
    public void Normalizza_lingua_e_condizione_fra_formati_diversi()
    {
        // Il marketplace usa "en", l'inventario "English": devono combaciare.
        var engine = new PricingEngine();
        var offers = new List<CardTraderMarketplaceProductDto> { Offer(7.00m, language: "en", condition: "Near Mint") };

        var decision = engine.Evaluate(
            Item(20.00m, condition: "NM", language: "English"),
            offers,
            Profile(NthLowestRule(1.01m, 25m, 1)),
            MyUserId);

        decision.ComparableOffersCount.Should().Be(1);
    }

    [Fact]
    public void Scarta_il_prezzo_anomalo_del_neofita()
    {
        // Il caso che il filtro sulle recensioni avrebbe dovuto risolvere:
        // un venditore mette la carta a 0,50 € quando il mercato sta a 10 €.
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(1.01m, 100m, 1));
        profile.EnableOutlierRejection = true;
        profile.MinOffersForOutlierRejection = 5;

        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(0.50m),  // anomalo
            Offer(10.00m), Offer(10.50m), Offer(11.00m), Offer(10.20m), Offer(10.80m)
        };

        var decision = engine.Evaluate(Item(10.00m), offers, profile, MyUserId);

        decision.OutliersRejectedCount.Should().Be(1);
        decision.ReferencePrice.Should().Be(10.00m, "il prezzo fuori scala non deve tarare il posizionamento");
    }

    [Fact]
    public void Il_guardrail_blocca_le_variazioni_eccessive()
    {
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(1.01m, 100m, 1));
        profile.MaxChangePercentPerRun = 50m;

        var offers = new List<CardTraderMarketplaceProductDto> { Offer(1.00m), Offer(1.10m) };

        var decision = engine.Evaluate(Item(50.00m), offers, profile, MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.BlockedByGuardrail);
        decision.ShouldWrite.Should().BeFalse();
    }

    [Fact]
    public void Il_dry_run_calcola_ma_non_scrive()
    {
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(1.01m, 100m, 1, -0.01m));
        profile.DryRun = true;

        var offers = new List<CardTraderMarketplaceProductDto> { Offer(8.00m), Offer(9.00m) };

        var decision = engine.Evaluate(Item(20.00m), offers, profile, MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.SimulatedDryRun);
        decision.ProposedPrice.Should().Be(7.99m, "il calcolo avviene comunque, per poterlo valutare");
        decision.ShouldWrite.Should().BeFalse("in dry-run non si scrive su Card Trader");
    }

    [Fact]
    public void Rispetta_il_divieto_di_ribasso()
    {
        var engine = new PricingEngine();
        var rule = NthLowestRule(1.01m, 100m, 1);
        rule.CanDecrease = false;

        var offers = new List<CardTraderMarketplaceProductDto> { Offer(5.00m), Offer(6.00m) };

        var decision = engine.Evaluate(Item(20.00m), offers, Profile(rule), MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.BlockedByDirection);
    }

    [Fact]
    public void Filtra_per_tipo_venditore_e_paese()
    {
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(1.01m, 100m, 1));
        profile.IncludeNormalSellers = false;   // solo professionali
        profile.CountryCodesCsv = "IT,ES";

        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(3.00m, userType: "normal", country: "IT"), // scartato: non pro
            Offer(4.00m, userType: "pro", country: "US"),    // scartato: paese
            Offer(8.00m, userType: "pro", country: "IT")     // ammesso
        };

        var decision = engine.Evaluate(Item(20.00m), offers, profile, MyUserId);

        decision.ComparableOffersCount.Should().Be(1);
        decision.ReferencePrice.Should().Be(8.00m);
    }

    [Fact]
    public void Esclude_venditori_in_vacanza_e_sotto_la_capacita_minima()
    {
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(1.01m, 100m, 1));
        profile.MinSellerDailyCapacity = 5;

        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(2.00m, onVacation: true),   // scartato: in vacanza
            Offer(3.00m, capacity: 1),        // scartato: troppo piccolo
            Offer(9.00m, capacity: 50)        // ammesso
        };

        var decision = engine.Evaluate(Item(20.00m), offers, profile, MyUserId);

        decision.ComparableOffersCount.Should().Be(1);
        decision.ReferencePrice.Should().Be(9.00m);
    }

    [Fact]
    public void Senza_offerte_sufficienti_non_tocca_il_prezzo()
    {
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(1.01m, 100m, 1));
        profile.MinComparableOffers = 3;

        var offers = new List<CardTraderMarketplaceProductDto> { Offer(5.00m), Offer(6.00m) };

        var decision = engine.Evaluate(Item(20.00m), offers, profile, MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.InsufficientOffers);
        decision.ProposedPrice.Should().Be(20.00m, "senza dati affidabili si lascia il prezzo com'è");
    }

    [Fact]
    public void Senza_regola_per_la_fascia_non_fa_nulla()
    {
        var engine = new PricingEngine();
        // Regola solo per 0,02-1,00 ma la carta sta a 50 €.
        var decision = engine.Evaluate(
            Item(50.00m),
            new List<CardTraderMarketplaceProductDto> { Offer(40.00m), Offer(45.00m) },
            Profile(NthLowestRule(0.02m, 1.00m, 1)),
            MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.NoMatchingRule);
    }

    [Fact]
    public void Su_mercato_sottile_non_riprica_invece_di_allinearsi_al_piu_caro()
    {
        // Caso reale osservato: 2 venditori comparabili e regola "posizione 3".
        // Prendere l'ultimo disponibile significherebbe allinearsi all'offerta più cara,
        // e se anche l'altro venditore usa un autopricer posizionale i due si rincorrono
        // verso l'alto senza limite.
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(1.01m, 100m, 3));
        profile.SkipWhenFewerOffersThanPosition = true;

        var offers = new List<CardTraderMarketplaceProductDto> { Offer(10.00m), Offer(2000.64m) };

        var decision = engine.Evaluate(Item(20.00m), offers, profile, MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.InsufficientOffers);
        decision.ProposedPrice.Should().Be(20.00m);
        decision.Reason.Should().Contain("posizione 3");
    }

    [Fact]
    public void Se_disattivato_il_salto_usa_l_ultima_offerta_disponibile()
    {
        // Comportamento dell'autopricer nativo, mantenuto come opzione esplicita.
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(1.01m, 100m, 4));
        profile.SkipWhenFewerOffersThanPosition = false;

        var offers = new List<CardTraderMarketplaceProductDto> { Offer(10.00m), Offer(12.00m) };

        var decision = engine.Evaluate(Item(20.00m), offers, profile, MyUserId);

        decision.ReferencePrice.Should().Be(12.00m);
    }

    [Fact]
    public void Il_salto_su_mercato_sottile_non_riguarda_mediana_e_media()
    {
        // Mediana e media non dipendono dal numero di venditori come fa il posizionamento.
        var engine = new PricingEngine();
        var rule = NthLowestRule(1.01m, 100m, 10);
        rule.ReferenceMode = PriceReferenceMode.MedianOffer;

        var profile = Profile(rule);
        profile.SkipWhenFewerOffersThanPosition = true;

        var offers = new List<CardTraderMarketplaceProductDto> { Offer(10.00m), Offer(12.00m) };

        var decision = engine.Evaluate(Item(20.00m), offers, profile, MyUserId);

        decision.ReferencePrice.Should().Be(11.00m);
    }

    [Fact]
    public void Le_proprieta_si_leggono_da_properties_hash()
    {
        // L'API v2 non restituisce un oggetto "properties": i valori stanno in
        // "properties_hash" con chiavi dipendenti dal gioco.
        var offer = new CardTraderMarketplaceProductDto
        {
            PriceCents = 500,
            Quantity = 1,
            PropertiesHash = new Dictionary<string, object>
            {
                ["condition"] = "Near Mint",
                ["mtg_language"] = "en",
                ["mtg_foil"] = true,
                ["signed"] = false,
                ["altered"] = false
            },
            User = new CardTraderMarketplaceUserDto { Id = 777, UserType = "pro", CountryCode = "IT" }
        };

        offer.Properties.Condition.Should().Be("Near Mint");
        offer.Properties.Language.Should().Be("en");
        offer.Properties.IsFoil.Should().BeTrue();
    }

    [Fact]
    public void Le_proprieta_funzionano_anche_per_giochi_diversi_da_magic()
    {
        // Pokémon usa "pokemon_language": la lettura è per suffisso, non per gioco.
        var offer = new CardTraderMarketplaceProductDto
        {
            PriceCents = 500,
            PropertiesHash = new Dictionary<string, object>
            {
                ["condition"] = "Near Mint",
                ["pokemon_language"] = "it",
                ["pokemon_foil"] = true
            }
        };

        offer.Properties.Language.Should().Be("it");
        offer.Properties.IsFoil.Should().BeTrue();
    }

    [Fact]
    public void Capacita_venditore_sconosciuta_esclusa_solo_se_richiesta_una_soglia()
    {
        // Card Trader restituisce null su una parte dei venditori.
        var engine = new PricingEngine();
        var offers = new List<CardTraderMarketplaceProductDto> { Offer(5.00m), Offer(6.00m) };
        offers[0].User.MaxSellableIn24hQuantity = null;
        offers[1].User.MaxSellableIn24hQuantity = null;

        var senzaSoglia = engine.Evaluate(Item(20.00m), offers, Profile(NthLowestRule(1.01m, 100m, 1)), MyUserId);
        senzaSoglia.ComparableOffersCount.Should().Be(2, "senza soglia il campo è irrilevante");

        var conSoglia = Profile(NthLowestRule(1.01m, 100m, 1));
        conSoglia.MinSellerDailyCapacity = 5;
        var risultato = engine.Evaluate(Item(20.00m), offers, conSoglia, MyUserId);
        risultato.Outcome.Should().Be(PricingOutcome.InsufficientOffers);
    }

    [Fact]
    public void La_mediana_ignora_le_code()
    {
        var engine = new PricingEngine();
        var rule = NthLowestRule(1.01m, 100m, 1);
        rule.ReferenceMode = PriceReferenceMode.MedianOffer;

        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(5.00m), Offer(10.00m), Offer(11.00m), Offer(12.00m), Offer(90.00m)
        };

        var decision = engine.Evaluate(Item(20.00m), offers, Profile(rule), MyUserId);

        decision.ReferencePrice.Should().Be(11.00m);
    }

    [Fact]
    public void Prezzo_gia_allineato_non_produce_scrittura()
    {
        var engine = new PricingEngine();
        var offers = new List<CardTraderMarketplaceProductDto> { Offer(10.00m), Offer(12.00m) };

        var decision = engine.Evaluate(Item(9.99m), offers, Profile(NthLowestRule(1.01m, 100m, 1, -0.01m)), MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.NoChangeNeeded);
        decision.ShouldWrite.Should().BeFalse();
    }
}
