using System.Text.Json;
using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Api;

/// <summary>
/// Configurazione JSON dell'API, tenuta in un punto solo perché i test possano verificare
/// il contratto vero invece di una copia destinata a divergere.
/// </summary>
public static class ApiJsonOptions
{
    public static void Configure(JsonSerializerOptions options)
    {
        // Le entità hanno navigazioni bidirezionali: senza questo la serializzazione va in ciclo.
        options.ReferenceHandler = ReferenceHandler.IgnoreCycles;

        // Gli enum viaggiano come stringa in entrambe le direzioni.
        //
        // In uscita ci andavano già: le mappature dei controller li scrivono con ToString(),
        // perché "PercentileOffer" in una griglia si legge e un 5 no. In entrata invece l'API
        // accettava solo il numero, quindi rimandare indietro un oggetto appena ricevuto —
        // che è esattamente ciò che fa il salvataggio del profilo di pricing con le sue
        // regole — falliva nel binding con un 400, prima ancora di entrare nel controller.
        //
        // Il convertitore continua ad accettare anche i numeri, quindi nessun chiamante
        // esistente si rompe.
        options.Converters.Add(new JsonStringEnumConverter());
    }
}
