using ShepherdAPI.Api.Contracts;
using ShepherdAPI.Api.Models;
using ShepherdAPI.Api.Services;
using Xunit;

namespace ShepherdAPI.Api.Tests.Services;

public class ExecutionRuleConfigurationTests
{
    private static ScoreRule Rule(int id, long version, int phase, int category, decimal weight, bool active = true) => new()
    {
        ScoreRuleID = id, ExperimentVersionID = version, PhaseID = phase, ScoreCategoryID = category,
        WeightPercent = weight, MinScore = 0, MaxScore = 5, Active = active,
    };

    [Fact]
    public void Describe_ReportsExpectedActualAndTheRulesOfThatVersionAndPhaseOnly()
    {
        var rules = new[]
        {
            Rule(1, version: 7, phase: 2, category: 1, weight: 60m),
            Rule(2, version: 7, phase: 2, category: 2, weight: 50m),
            Rule(3, version: 7, phase: 3, category: 1, weight: 40m),   // other phase
            Rule(4, version: 8, phase: 2, category: 1, weight: 30m),   // other version
        };

        var described = ExecutionRuleConfiguration.Describe(7, 2, rules);

        Assert.Equal(7L, described["experimentVersionID"]);
        Assert.Equal(2, described["phaseID"]);
        Assert.Equal(100.00m, described["expectedWeightPercent"]);
        Assert.Equal(110m, described["actualWeightPercent"]);
        var listed = Assert.IsType<List<ExecutionConflictRule>>(described["scoreRules"]);
        Assert.Equal([1, 2], listed.Select(r => r.ScoreRuleID));
    }

    [Fact]
    public void Describe_CountsInactiveRules_TheActiveFlagIsIgnored()
    {
        var rules = new[]
        {
            Rule(1, 7, 2, 1, 40m, active: false),
            Rule(2, 7, 2, 2, 30m, active: true),
        };

        var described = ExecutionRuleConfiguration.Describe(7, 2, rules);

        Assert.Equal(70m, described["actualWeightPercent"]);
        Assert.Equal([1, 2], Assert.IsType<List<ExecutionConflictRule>>(described["scoreRules"]).Select(r => r.ScoreRuleID));
    }

    [Fact]
    public void Describe_ForAPhaseWithNoRules_IsAZeroTotalAndAnEmptyList()
    {
        var described = ExecutionRuleConfiguration.Describe(7, 2, []);

        Assert.Equal(0m, described["actualWeightPercent"]);
        Assert.Empty(Assert.IsType<List<ExecutionConflictRule>>(described["scoreRules"]));
    }

    [Fact]
    public void Describe_OrdersRulesByCategory_ForAStableBody()
    {
        var rules = new[] { Rule(9, 7, 2, category: 5, weight: 10m), Rule(8, 7, 2, category: 3, weight: 10m), Rule(7, 7, 2, category: 4, weight: 10m) };

        var listed = Assert.IsType<List<ExecutionConflictRule>>(ExecutionRuleConfiguration.Describe(7, 2, rules)["scoreRules"]);

        Assert.Equal([3, 4, 5], listed.Select(r => r.ScoreCategoryID));
    }
}
