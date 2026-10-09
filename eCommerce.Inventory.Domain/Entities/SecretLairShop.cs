namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Prodotto visto nel negozio Secret Lair di Wizards (<c>secretlair.wizards.com/eu</c>). Il negozio
/// mostra solo i prodotti in catalogo: qui restano anche quelli tolti, con la data, così lo storico
/// dei drop (prezzo, scorte, tempo di esaurimento) non si perde.
/// </summary>
public class SecretLairShopProduct
{
    public int Id { get; set; }

    /// <summary>Id del prodotto nel negozio: la pagina è <c>/eu/product/{id}</c>.</summary>
    public string WizardsProductId { get; set; } = string.Empty;

    /// <summary>Codice articolo Wizards (es. <c>D51280000-EU</c>).</summary>
    public string? RefId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Drop di appartenenza, dalla categoria del negozio (es. "Chaos Vault: The Oddlands").</summary>
    public string? DropName { get; set; }

    public bool IsFoil { get; set; }
    public decimal Price { get; set; }

    /// <summary>Copie disponibili secondo il negozio; null se non indicato.</summary>
    public int? Stock { get; set; }

    public bool IsPreorder { get; set; }
    public int? LimitPerCustomer { get; set; }

    public DateTimeOffset? ReleaseDate { get; set; }
    public DateTime? SaleStart { get; set; }
    public DateTime? SaleEnd { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>Prima lettura con scorte a zero.</summary>
    public DateTime? SoldOutAt { get; set; }

    /// <summary>Prima lettura in cui il prodotto non era più nel negozio.</summary>
    public DateTime? RemovedAt { get; set; }

    /// <summary>Quando è stata letta la pagina del prodotto con l'elenco delle carte.</summary>
    public DateTime? ContentsFetchedAt { get; set; }

    /// <summary>
    /// Valore netto stimato prima dell'uscita, quando le carte Secret Lair non hanno ancora prezzi
    /// propri. Non si aggiorna più: dopo l'uscita si confronta con il valore ai prezzi reali.
    /// </summary>
    public decimal? EstimatedNetValue { get; set; }

    /// <summary>Quando è stata congelata <see cref="EstimatedNetValue"/>.</summary>
    public DateTime? EstimatedAt { get; set; }

    public ICollection<SecretLairShopCard> Cards { get; set; } = new List<SecretLairShopCard>();
}

/// <summary>Carta di un prodotto Secret Lair, dall'elenco "Contents" della pagina del prodotto.</summary>
public class SecretLairShopCard
{
    public int Id { get; set; }

    public int SecretLairShopProductId { get; set; }
    public SecretLairShopProduct? Product { get; set; }

    public int Quantity { get; set; } = 1;

    /// <summary>Nome della carta (es. "Winota, Joiner of Forces").</summary>
    public string CardName { get; set; } = string.Empty;

    /// <summary>Nome mostrato sulla carta Secret Lair, se diverso (es. "He-Man, Champion of Eternia").</summary>
    public string? DisplayName { get; set; }
}

public enum SecretLairShopRunOutcome
{
    Running = 0,
    Succeeded = 1,
    Failed = 2
}

/// <summary>
/// Esito di una lettura del negozio. Il negozio si legge da un'interfaccia non documentata: se cambia,
/// il registro lo dice subito invece di lasciar credere che non ci siano drop nuovi.
/// </summary>
public class SecretLairShopRun
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public SecretLairShopRunOutcome Outcome { get; set; }
    public int Products { get; set; }
    public int NewProducts { get; set; }
    public int ContentsFetched { get; set; }
    public string? Message { get; set; }
}
