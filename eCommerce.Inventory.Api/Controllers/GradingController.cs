using eCommerce.Inventory.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace eCommerce.Inventory.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class GradingController : ControllerBase
    {
        private readonly IGradingService _gradingService;

        public GradingController(IGradingService gradingService)
        {
            _gradingService = gradingService;
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

            using var stream = image.OpenReadStream();
            var result = await _gradingService.GradeCardAsync(stream, image.FileName);

            return Ok(result);
        }

        /// <summary>
        /// Analyze multiple card images (front, back, etc.) - uses worst grade strategy
        /// </summary>
        [HttpPost("analyze-multi")]
        public async Task<IActionResult> AnalyzeCardMultiple(List<IFormFile> images)
        {
            if (images == null || images.Count == 0)
            {
                return BadRequest("No images uploaded.");
            }

            var imageStreams = new List<(System.IO.Stream ImageStream, string FileName)>();

            foreach (var image in images)
            {
                if (image.Length > 0)
                {
                    imageStreams.Add((image.OpenReadStream(), image.FileName));
                }
            }

            if (imageStreams.Count == 0)
            {
                return BadRequest("No valid images uploaded.");
            }

            var result = await _gradingService.GradeCardFromMultipleImagesAsync(imageStreams);

            // Dispose streams
            foreach (var (stream, _) in imageStreams)
            {
                stream.Dispose();
            }

            return Ok(result);
        }
    }
}
