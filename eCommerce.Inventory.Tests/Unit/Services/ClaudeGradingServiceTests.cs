using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Stima della condizione (feature 006, Fase 2): la risposta di Claude deve arrivare alla maschera e al
/// telefono con la condizione nella forma che usano ("Slightly Played"), mai inventata.
/// </summary>
public class ClaudeGradingServiceTests
{
    private const string Answer = """
        {"condition":"SP","confidence":"medium","overallGrade":7,"centering":9,"corners":7,"edges":8,"surface":9,
         "centeringNote":"buona","cornersNote":"angolo in alto a destra sbiancato","edgesNote":"  ",
         "surfaceNote":"nessun graffio visibile","summary":"Usura leggera sugli angoli."}
        """;

    [Fact]
    public void Parse_maps_the_condition_to_the_listing_form_names()
    {
        var result = ClaudeGradingService.Parse(Answer);

        result.ConditionCode.Should().Be("SP");
        result.ConditionName.Should().Be("Slightly Played");
        result.Confidence.Should().Be(0.7m);
        result.Corners.Should().Be(7m);
        result.CornersNote.Should().Be("angolo in alto a destra sbiancato");
        result.EdgesNote.Should().BeNull();
        result.Provider.Should().Be("Claude");
    }

    [Fact]
    public void Parse_falls_back_to_the_worst_condition_and_clamps_scores()
    {
        var result = ClaudeGradingService.Parse("""
            {"condition":"??","confidence":"low","overallGrade":14,"centering":0,"corners":5,"edges":5,"surface":5,
             "centeringNote":"","cornersNote":"","edgesNote":"","surfaceNote":"","summary":""}
            """);

        result.ConditionName.Should().Be("Poor");
        result.OverallGrade.Should().Be(10m);
        result.Centering.Should().Be(1m);
        result.Confidence.Should().Be(0.4m);
    }

    [Theory]
    [InlineData(true, 0.9, "NM")]
    [InlineData(false, 0.9, "SP")]
    [InlineData(true, 0.7, "SP")]
    public void NearMint_needs_the_back_and_high_confidence(bool hasBack, double confidence, string expected)
    {
        var result = new eCommerce.Inventory.Application.DTOs.GradingResultDto
        {
            ConditionCode = "NM", ConditionName = "Near Mint", Confidence = (decimal)confidence
        };

        var guarded = ClaudeGradingService.ApplyNearMintGuard(result, hasBack);

        guarded.ConditionCode.Should().Be(expected);
        guarded.NearMintPossible.Should().Be(expected == "SP");
        if (expected == "SP") guarded.Summary.Should().StartWith("Potrebbe essere Near Mint");
    }

    [Fact]
    public void PhotoRoles_reads_the_phone_names_and_falls_back_to_front_then_back()
    {
        ClaudeGradingService.PhotoRoles(new[] { "fronte.jpg", "dettaglio-1.jpg" })
            .Should().Equal("front", "detail");
        ClaudeGradingService.PhotoRoles(new[] { "fronte.jpg", "retro.jpg", "dettaglio-1.jpg", "dettaglio-2.jpg" })
            .Should().Equal("front", "back", "detail", "detail");
        ClaudeGradingService.PhotoRoles(new[] { "image1.jpg", "image2.jpg", "image3.jpg" })
            .Should().Equal("front", "back", "detail");
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    public void DetectMediaType_reads_the_file_signature(byte[] header, string expected)
    {
        AnthropicClientProvider.DetectMediaType(header).Should().Be(expected);
    }
}
