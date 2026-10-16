using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Configuration;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Client Anthropic condiviso da riconoscimento e stima della condizione (feature 006). La chiave sta in
/// <c>Anthropic:ApiKey</c>, solo nella configurazione di produzione; il modello in <c>Scan:Model</c>.
/// </summary>
public class AnthropicClientProvider
{
    public const string DefaultModel = "claude-opus-5-5";

    private readonly IConfiguration _configuration;
    private readonly object _lock = new();
    private AnthropicClient? _client;

    public AnthropicClientProvider(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string? ApiKey => _configuration["Anthropic:ApiKey"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public string Model => _configuration["Scan:Model"] ?? DefaultModel;

    /// <exception cref="InvalidOperationException">Chiave non configurata.</exception>
    public AnthropicClient Client
    {
        get
        {
            if (!IsConfigured) throw new InvalidOperationException("Chiave API Anthropic non configurata (Anthropic:ApiKey)");
            lock (_lock)
            {
                return _client ??= new AnthropicClient { ApiKey = ApiKey };
            }
        }
    }

    public static ImageBlockParam ImageBlock(byte[] image, string mediaType) => new()
    {
        Source = new Base64ImageSource
        {
            Data = Convert.ToBase64String(image),
            MediaType = mediaType switch
            {
                "image/png" => MediaType.ImagePng,
                "image/webp" => MediaType.ImageWebP,
                "image/gif" => MediaType.ImageGif,
                _ => MediaType.ImageJpeg,
            },
        },
    };

    /// <summary>Tipo dell'immagine dai primi byte: il grading riceve solo stream e nome del file.</summary>
    public static string DetectMediaType(byte[] image)
    {
        if (image.Length >= 8 && image[0] == 0x89 && image[1] == 0x50 && image[2] == 0x4E && image[3] == 0x47) return "image/png";
        if (image.Length >= 12 && image[0] == 0x52 && image[1] == 0x49 && image[2] == 0x46 && image[3] == 0x46 &&
            image[8] == 0x57 && image[9] == 0x45 && image[10] == 0x42 && image[11] == 0x50) return "image/webp";
        if (image.Length >= 6 && image[0] == 0x47 && image[1] == 0x49 && image[2] == 0x46) return "image/gif";
        return "image/jpeg";
    }
}
