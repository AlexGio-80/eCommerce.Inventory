using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace eCommerce.Inventory.Infrastructure.Services
{
    public class XimilarGradingService : IGradingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<XimilarGradingService> _logger;
        private const string ApiEndpoint = "https://api.ximilar.com/grading/v2/grade";

        public XimilarGradingService(HttpClient httpClient, IConfiguration configuration, ILogger<XimilarGradingService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<GradingResultDto> GradeCardAsync(Stream imageStream, string fileName)
        {
            var images = new List<(Stream ImageStream, string FileName)> { (imageStream, fileName) };
            return await GradeCardFromMultipleImagesAsync(images);
        }

        public async Task<GradingResultDto> GradeCardFromMultipleImagesAsync(List<(Stream ImageStream, string FileName)> images)
        {
            var apiKey = _configuration["Ximilar:ApiKey"];

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Ximilar API Key not found. Returning MOCK data for {Count} images.", images.Count);
                return GetMockGradingResult(images.Count);
            }

            try
            {
                var results = new List<GradingResultDto>();

                foreach (var (imageStream, fileName) in images)
                {
                    using var content = new MultipartFormDataContent();
                    using var streamContent = new StreamContent(imageStream);
                    streamContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                    content.Add(streamContent, "image", fileName);

                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", apiKey);

                    var response = await _httpClient.PostAsync(ApiEndpoint, content);
                    response.EnsureSuccessStatusCode();

                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    results.Add(ParseXimilarResponse(jsonResponse));
                }

                return CombineResults(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Ximilar API");
                throw;
            }
        }

        private GradingResultDto GetMockGradingResult(int imageCount)
        {
            var random = new Random();
            var overallGrade = (decimal)(random.Next(70, 100) / 10.0);
            var result = new GradingResultDto
            {
                OverallGrade = overallGrade,
                Centering = (decimal)(random.Next(80, 100) / 10.0),
                Corners = (decimal)(random.Next(70, 100) / 10.0),
                Edges = (decimal)(random.Next(70, 100) / 10.0),
                Surface = (decimal)(random.Next(60, 100) / 10.0),
                Confidence = 0.95m,
                Provider = "MockService",
                ImagesAnalyzed = imageCount
            };

            ApplyConditionMapping(result);
            return result;
        }

        private GradingResultDto ParseXimilarResponse(string json)
        {
            // TODO: Implement actual parsing logic based on Ximilar JSON structure
            return new GradingResultDto
            {
                OverallGrade = 9.0m,
                Centering = 9.5m,
                Corners = 9.0m,
                Edges = 8.5m,
                Surface = 9.0m,
                Confidence = 0.99m,
                Provider = "Ximilar",
                ImagesAnalyzed = 1
            };
        }

        private GradingResultDto CombineResults(List<GradingResultDto> results)
        {
            if (results.Count == 0)
                throw new InvalidOperationException("No grading results to combine");

            if (results.Count == 1)
            {
                ApplyConditionMapping(results[0]);
                return results[0];
            }

            // Take the WORST grade for each category (most conservative approach)
            var combined = new GradingResultDto
            {
                OverallGrade = results.Min(r => r.OverallGrade),
                Centering = results.Min(r => r.Centering),
                Corners = results.Min(r => r.Corners),
                Edges = results.Min(r => r.Edges),
                Surface = results.Min(r => r.Surface),
                Confidence = results.Average(r => r.Confidence),
                Provider = results.First().Provider,
                ImagesAnalyzed = results.Count
            };

            ApplyConditionMapping(combined);
            return combined;
        }

        private void ApplyConditionMapping(GradingResultDto result)
        {
            var grade = result.OverallGrade;

            if (grade >= 8.0m)
            {
                result.ConditionCode = "NM";
                result.ConditionName = "Near Mint";
            }
            else if (grade >= 6.0m)
            {
                result.ConditionCode = "SP";
                result.ConditionName = "Slightly Played";
            }
            else if (grade >= 4.0m)
            {
                result.ConditionCode = "MP";
                result.ConditionName = "Moderately Played";
            }
            else if (grade >= 2.0m)
            {
                result.ConditionCode = "PL";
                result.ConditionName = "Played";
            }
            else
            {
                result.ConditionCode = "PO";
                result.ConditionName = "Poor";
            }
        }
    }
}
