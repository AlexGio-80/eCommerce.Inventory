using eCommerce.Inventory.Application.DTOs;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace eCommerce.Inventory.Application.Interfaces
{
    public interface IGradingService
    {
        Task<GradingResultDto> GradeCardAsync(Stream imageStream, string fileName);
        Task<GradingResultDto> GradeCardFromMultipleImagesAsync(List<(Stream ImageStream, string FileName)> images);
    }
}
