using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace eCommerce.Inventory.Api.Controllers
{
    /// <summary>
    /// Stima della condizione dalle foto: dalla scheda del telefono, dalla pagina "AI Grading" e dalla
    /// maschera "Nuovo Prodotto". Ogni stima conta nel limite giornaliero dei riconoscimenti
    /// (<c>Scan:DailyLimit</c>), perché costa come una chiamata a Claude.
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class GradingController : ControllerBase
    {
        private readonly IGradingService _gradingService;
        private readonly ScanDailyCounter _counter;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GradingController> _logger;

        public GradingController(
            IGradingService gradingService,
            ScanDailyCounter counter,
            IConfiguration configuration,
            ILogger<GradingController> logger)
        {
            _gradingService = gradingService;
            _counter = counter;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Analyze a single card image
        /// </summary>
        [HttpPost("analyze")]
        public async Task<IActionResult> AnalyzeCard(IFormFile image)
        {
            if (image == null || image.Length == 0)
            {
                return BadRequest("No image uploaded.");
            }

            return await GradeAsync(new List<IFormFile> { image });
        }

        /// <summary>
        /// Analyze multiple card images (front, back, etc.)
        /// </summary>
        [HttpPost("analyze-multi")]
        public async Task<IActionResult> AnalyzeCardMultiple(List<IFormFile> images)
        {
            if (images == null || images.Count == 0)
            {
                return BadRequest("No images uploaded.");
            }

            return await GradeAsync(images);
        }

        private async Task<IActionResult> GradeAsync(List<IFormFile> images)
        {
            var valid = images.Where(i => i.Length > 0).ToList();
            if (valid.Count == 0)
            {
                return BadRequest("No valid images uploaded.");
            }

            var limit = _configuration.GetValue("Scan:DailyLimit", 200);
            if (!_counter.TryTake(limit, out _))
            {
                return StatusCode(429, new { message = $"Raggiunto il limite di {limit} stime e riconoscimenti per oggi (Scan:DailyLimit)" });
            }

            var imageStreams = valid.Select(i => (i.OpenReadStream(), i.FileName)).ToList();
            try
            {
                // Le foto non si salvano: servono solo per la risposta.
                return Ok(await _gradingService.GradeCardFromMultipleImagesAsync(imageStreams));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("non configurata"))
            {
                return StatusCode(503, new { message = "La stima non è configurata: manca la chiave API sul PC" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stima della condizione fallita");
                return StatusCode(502, new { message = "Stima non riuscita. Riprova con un'altra foto" });
            }
            finally
            {
                foreach (var (stream, _) in imageStreams)
                {
                    stream.Dispose();
                }
            }
        }
    }
}
