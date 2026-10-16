using eCommerce.Inventory.Application.DTOs;

namespace eCommerce.Inventory.Application.Interfaces;

/// <summary>Legge nome, set, numero di raccolta e lingua dalla foto di una carta.</summary>
public interface ICardImageReader
{
    /// <summary>False se manca la configurazione (chiave API): la pagina lo dice invece di fallire.</summary>
    bool IsConfigured { get; }

    /// <param name="image">Foto già ridotta dal telefono.</param>
    /// <param name="mediaType">"image/jpeg", "image/png" o "image/webp".</param>
    Task<CardReading> ReadAsync(byte[] image, string mediaType, CancellationToken cancellationToken = default);
}
