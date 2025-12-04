namespace eCommerce.Inventory.Application.DTOs
{
    public class GradingResultDto
    {
        public decimal OverallGrade { get; set; }
        public decimal Centering { get; set; }
        public decimal Corners { get; set; }
        public decimal Edges { get; set; }
        public decimal Surface { get; set; }
        public decimal Confidence { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string ConditionCode { get; set; } = string.Empty; // NM, SP, MP, PL, PO
        public string ConditionName { get; set; } = string.Empty; // Near Mint, Slightly Played, etc.
        public int ImagesAnalyzed { get; set; } = 1;
    }
}
