using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>Prova dei modelli sul riconoscimento: spenta senza configurazione, mai sul modello principale.</summary>
public class ScanModelComparisonTests
{
    private static ScanModelComparison Comparison(Dictionary<string, string?> settings) => new(
        null!, null!, new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
        NullLogger<ScanModelComparison>.Instance);

    [Fact]
    public void Models_is_empty_without_configuration()
    {
        Comparison(new()).Models.Should().BeEmpty();
    }

    [Fact]
    public void Models_skips_the_main_model_blanks_and_duplicates()
    {
        var models = Comparison(new()
        {
            ["Scan:CompareModels:0"] = "claude-sonnet-5-5",
            ["Scan:CompareModels:1"] = "claude-opus-5-5",
            ["Scan:CompareModels:2"] = " ",
            ["Scan:CompareModels:3"] = "claude-haiku-5-5",
            ["Scan:CompareModels:4"] = "CLAUDE-SONNET-5-5"
        }).Models;

        models.Should().Equal("claude-sonnet-5-5", "claude-haiku-5-5");
    }
}
