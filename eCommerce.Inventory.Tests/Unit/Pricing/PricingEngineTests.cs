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
            // Guardrail e filtro di rapporto disattivati salvo nei test dedicati, così ogni
            // caso verifica una sola cosa.
            MaxIncreasePercentPerRun = 100000m,
            MaxDecreasePercentPerRun = 100000m,
            MaxMedianRatio = 0m,
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
        int quantity = 1,
        bool canSellViaHub = false)
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
                MaxSellableIn24hQuantity = capacity,
                CanSellViaHub = canSellViaHub
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
        profile.MaxDecreasePercentPerRun = 50m;

        var offers = new List<CardTraderMarketplaceProductDto> { Offer(1.00m), Offer(1.10m) };

        var decision = engine.Evaluate(Item(50.00m), offers, profile, MyUserId);

        decision.Outcome.Should().Be(PricingOutcome.BlockedByGuardrail);
        decision.ShouldWrite.Should().BeFalse();
    }

    /// <summary>
    /// Le due direzioni non comportano lo stesso rischio: un rialzo eccessivo lascia la carta
    /// invenduta e si corregge alla prossima esecuzione, un ribasso eccessivo la fa comprare
    /// subito al prezzo sbagliato. Il guardrail deve quindi essere asimmetrico.
    /// </summary>
    private static PricingRule PercentileRule(decimal from, decimal to, decimal percentile, decimal adjust = 0m)
        => new()
        {
            Id = 1,
            FromPrice = from,
            ToPrice = to,
            ReferenceMode = PriceReferenceMode.PercentileOffer,
            Percentile = percentile,
            AdjustmentAmount = adjust,
            CanIncrease = true,
            CanDecrease = true,
            IsActive = true
        };

    /// <summary>
    /// La stessa regola deve mantenere la collocazione relativa al variare della profondità
    /// del mercato: sulle carte reali le offerte comparabili vanno da 3 a 29, e con un ordinale
    /// fisso "la quarta più bassa" significa stare in fondo su un mercato profondo ed essere
    /// il più caro su uno sottile.
    /// </summary>
    [Fact]
    public void Il_percentile_si_adatta_alla_profondita_del_mercato()
    {
        var engine = new PricingEngine();
        var profile = Profile(PercentileRule(1.01m, 1000m, 30m));

        var mercatoSottile = new List<CardTraderMarketplaceProductDto>
        {
            Offer(10.00m), Offer(20.00m), Offer(30.00m), Offer(40.00m), Offer(50.00m)
        };
        engine.Evaluate(Item(25.00m), mercatoSottile, profile, MyUserId)
            .ReferencePrice.Should().Be(20.00m, "su 5 offerte il 30% cade sulla seconda");

        var mercatoProfondo = Enumerable.Range(1, 21).Select(i => Offer(i * 10m)).ToList();
        engine.Evaluate(Item(25.00m), mercatoProfondo, profile, MyUserId)
            .ReferencePrice.Should().Be(70.00m, "su 21 offerte il 30% cade sulla settima");
    }

    /// <summary>
    /// Caso reale di Sigarda's Aid: 4 offerte comparabili su un mercato di 73–96 €, di cui una
    /// a 1019 € messa lì per non sbagliare. Con la soglia dello scarto statistico a 5 il filtro
    /// non partiva e la regola posizionale finiva dritta sul prezzo di comodo.
    /// </summary>
    [Fact]
    public void Il_filtro_di_rapporto_scarta_il_prezzo_di_comodo_anche_su_mercato_sottile()
    {
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(25.01m, 2000m, 4, -0.01m));
        profile.MaxMedianRatio = 4m;

        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(73.74m), Offer(78.96m), Offer(96.56m), Offer(1019.62m)
        };

        var decision = engine.Evaluate(Item(75.72m), offers, profile, MyUserId);

        decision.OutliersRejectedCount.Should().Be(1);
        decision.ProposedPrice.Should().BeLessThan(100m, "il prezzo di comodo non deve tarare il posizionamento");
    }

    [Fact]
    public void Il_filtro_di_rapporto_scarta_anche_il_prezzo_irrealisticamente_basso()
    {
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(1.01m, 1000m, 1));
        profile.MaxMedianRatio = 4m;

        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(0.50m), Offer(40.00m), Offer(42.00m), Offer(44.00m)
        };

        var decision = engine.Evaluate(Item(40.00m), offers, profile, MyUserId);

        decision.OutliersRejectedCount.Should().Be(1);
        decision.ReferencePrice.Should().Be(40.00m, "il venditore alle prime armi non deve trascinare giù il mercato");
    }

    [Fact]
    public void Il_guardrail_e_asimmetrico_fra_salita_e_discesa()
    {
        var engine = new PricingEngine();

        var profile = Profile(NthLowestRule(1.01m, 100m, 1));
        profile.MaxIncreasePercentPerRun = 300m;
        profile.MaxDecreasePercentPerRun = 25m;

        // Il mercato sta molto più in alto: +150% deve passare.
        var salita = engine.Evaluate(
            Item(20.00m),
            new List<CardTraderMarketplaceProductDto> { Offer(50.00m), Offer(55.00m) },
            profile, MyUserId);

        salita.Outcome.Should().Be(PricingOutcome.Applied, "i rialzi reali di mercato sono il motivo per cui l'autopricer esiste");

        // Il mercato sta molto più in basso: -60% deve essere fermato.
        var discesa = engine.Evaluate(
            Item(50.00m),
            new List<CardTraderMarketplaceProductDto> { Offer(20.00m), Offer(22.00m) },
            profile, MyUserId);

        discesa.Outcome.Should().Be(PricingOutcome.BlockedByGuardrail, "un ribasso eccessivo non si recupera");
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
    public void Filtra_solo_venditori_ct_zero()
    {
        // can_sell_via_hub è il campo con cui l'API segnala i venditori Cardtrader Zero:
        // passano dal magazzino/controllo qualità di Card Trader, quindi sono un riferimento
        // più affidabile dei venditori nuovi o casuali che mettono prezzi fuori mercato.
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(1.01m, 100m, 1));
        profile.IncludeOnlyCtZeroSellers = true;

        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(2.00m, canSellViaHub: false), // scartato: non è CT Zero
            Offer(9.00m, canSellViaHub: true)   // ammesso
        };

        var decision = engine.Evaluate(Item(20.00m), offers, profile, MyUserId);

        decision.ComparableOffersCount.Should().Be(1);
        decision.ReferencePrice.Should().Be(9.00m);
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
    public void Se_disattivato_il_salto_non_diventa_comunque_il_piu_caro()
    {
        // Con il salto disattivato la posizione viene troncata alla profondità del mercato,
        // ma senza mai coincidere con l'offerta più cara: una regola di collocazione deve
        // mettermi dentro la scaletta, e su un mercato sottile ci finirebbe sopra da sola.
        var engine = new PricingEngine();
        var profile = Profile(NthLowestRule(1.01m, 100m, 4));
        profile.SkipWhenFewerOffersThanPosition = false;

        var offers = new List<CardTraderMarketplaceProductDto> { Offer(10.00m), Offer(12.00m) };

        var decision = engine.Evaluate(Item(20.00m), offers, profile, MyUserId);

        decision.ReferencePrice.Should().Be(10.00m, "12,00 € è il massimo del mercato e non può essere il riferimento");
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

    /// <summary>
    /// Caso reale osservato su Overgrown Tomb foil (blueprint 275538) il 2026-08-28.
    /// L'export dava la mia carta a 19,99 € mentre nel feed del marketplace la stessa
    /// inserzione compare a 20,26 €, perché i prezzi del marketplace sono lato acquirente.
    /// Confrontando 19,99 € con i 20,2x € dei concorrenti il motore mi credeva più economico
    /// di quanto fossi e proponeva un rialzo, benché fossi già in terza posizione.
    /// </summary>
    [Fact]
    public void Converte_il_prezzo_di_vetrina_prima_di_confrontare_le_posizioni()
    {
        var engine = new PricingEngine();

        var item = Item(19.99m, foil: true);
        item.CardTraderProductId = 341396451;

        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(19.26m, condition: "Slightly Played", foil: true), // non comparabile
            Offer(20.22m, foil: true),
            Offer(20.26m, foil: true),
            Offer(20.27m, foil: true),
            Offer(21.35m, foil: true)
        };

        // La mia inserzione come la vede il mercato: 19,99 € + 0,27 € di sovrapprezzo.
        var mine = Offer(20.26m, userId: MyUserId, foil: true);
        mine.Id = item.CardTraderProductId.Value;
        offers.Add(mine);

        var decision = engine.Evaluate(item, offers, Profile(NthLowestRule(1.01m, 25m, 3, -0.01m)), MyUserId);

        decision.ReferencePrice.Should().Be(20.27m, "la terza offerta comparabile in vetrina");
        decision.ProposedPrice.Should().Be(19.99m,
            "20,26 € di vetrina, riportati al netto del sovrapprezzo, sono i 19,99 € che ho già");
        decision.Outcome.Should().Be(PricingOutcome.NoChangeNeeded);
        decision.Reason.Should().Contain("posizione 3");
    }

    [Fact]
    public void Senza_la_mia_offerta_nel_feed_non_inventa_il_sovrapprezzo()
    {
        var engine = new PricingEngine();

        var item = Item(19.99m, foil: true);
        item.CardTraderProductId = 999999; // non presente fra le offerte

        var offers = new List<CardTraderMarketplaceProductDto>
        {
            Offer(20.22m, foil: true),
            Offer(20.26m, foil: true),
            Offer(20.27m, foil: true)
        };

        var decision = engine.Evaluate(item, offers, Profile(NthLowestRule(1.01m, 25m, 3, -0.01m)), MyUserId);

        decision.ProposedPrice.Should().Be(20.25m,
            "senza fattore di conversione si resta sulla scala del venditore, e il riferimento " +
            "non può essere l'offerta più cara");
        decision.Reason.Should().Contain("non ricavabile");
    }

    /// <summary>
    /// Se il prezzo è stato appena modificato a mano, il marketplace può ancora esporre il
    /// valore vecchio e il rapporto risultare assurdo (osservato: prezzo di vetrina inferiore
    /// a quello che incasso). In quel caso non si converte.
    /// </summary>
    [Fact]
    public void Rapporto_implausibile_viene_ignorato()
    {
        var engine = new PricingEngine();

        var item = Item(117.52m, foil: true);
        item.CardTraderProductId = 424235992;

        var stale = Offer(93.96m, userId: MyUserId, foil: true); // più basso del mio prezzo: impossibile
        stale.Id = item.CardTraderProductId.Value;

        var offers = new List<CardTraderMarketplaceProductDto>
        {
            stale,
            Offer(100.64m, foil: true),
            Offer(112.84m, foil: true),
            Offer(149.16m, foil: true)
        };

        var decision = engine.Evaluate(item, offers, Profile(NthLowestRule(100.01m, 2000m, 3, -0.01m)), MyUserId);

        decision.ProposedPrice.Should().Be(112.83m,
            "senza conversione il riferimento resta grezzo, e non può essere l'offerta più cara");
        decision.Reason.Should().Contain("non ricavabile");
    }
}
