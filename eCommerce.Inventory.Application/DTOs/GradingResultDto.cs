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

        // Motivazioni in italiano, una per aspetto (stima con Claude; vuote con Ximilar).
        public string? CenteringNote { get; set; }
        public string? CornersNote { get; set; }
        public string? EdgesNote { get; set; }
        public string? SurfaceNote { get; set; }

        /// <summary>Una frase sul perché della condizione proposta, o sui limiti della foto.</summary>
        public string? Summary { get; set; }

        /// <summary>Claude ha detto Near Mint, ma la regola prudente ha proposto Slightly Played: il telefono offre "Usa Near Mint".</summary>
        public bool NearMintPossible { get; set; }
    }
}
