// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Prx;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace SharpProspero.Tests;

// The metadata describes the title to the system. A field carrying a value the system does not
// recognise is wrong however the title is used; a field a finished title always carries but this one
// does not leaves the system reading its own default. The two are reported apart, because only the
// second can be settled without knowing what the title meant.
public sealed class ParamJsonTests
{
    // The shape a finished title carries, which the checks below start from and then break one field
    // at a time.
    private static JsonObject Complete()
    {
        var document = new JsonObject
        {
            ["applicationCategoryType"] = 0,
            ["applicationDrmType"] = "free",
            ["conceptId"] = "99099",
            ["contentBadgeType"] = 1,
            ["contentId"] = "UP9000-PPSA99099_00-PROSPERO00000000",
            ["contentVersion"] = "01.000.000",
            ["masterVersion"] = "01.00",
            ["titleId"] = "PPSA99099",
            ["localizedParameters"] = new JsonObject
            {
                ["defaultLanguage"] = "en-US",
                ["en-US"] = new JsonObject { ["titleName"] = "SharpProspero" },
            },
        };
        ParamJson.Complete(document);
        return document;
    }

    [Fact]
    public void Check_AcceptsWhatCompleteProduces()
    {
        ParamReport report = ParamJson.Check(Complete());
        Assert.True(report.IsValid);
        Assert.Empty(report.Issues);
        Assert.Equal(ApplicationCategory.Game, report.Category);
    }

    [Fact]
    public void Complete_SettlesEverythingItReportsAsSettleable()
    {
        var document = new JsonObject
        {
            ["applicationDrmType"] = "free",
            ["contentId"] = "UP9000-PPSA99099_00-PROSPERO00000000",
            ["contentVersion"] = "01.000.000",
            ["masterVersion"] = "01.00",
            ["titleId"] = "PPSA99099",
            ["localizedParameters"] = new JsonObject
            {
                ["defaultLanguage"] = "en-US",
                ["en-US"] = new JsonObject { ["titleName"] = "SharpProspero" },
            },
        };

        Assert.Contains(ParamJson.Check(document).Issues, i => i.CanComplete);
        ParamJson.Complete(document);
        Assert.Empty(ParamJson.Check(document).Issues);
    }

    // A field already there is left as it is, so completing a title twice cannot change what it says.
    [Fact]
    public void Complete_IsSettledAfterOnePass()
    {
        JsonObject document = Complete();
        Assert.Empty(ParamJson.Complete(document));
    }

    [Fact]
    public void Complete_OrdersTheFields()
    {
        string[] fields = [.. Complete().Select(f => f.Key)];
        Assert.Equal([.. fields.OrderBy(f => f, System.StringComparer.Ordinal)], fields);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(65792)]
    [InlineData(66048)]
    [InlineData(131328)]
    [InlineData(131584)]
    [InlineData(16777216)]
    [InlineData(33554432)]
    [InlineData(50331648)]
    [InlineData(67108864)]
    public void Check_AcceptsEveryKindOfTitle(int category)
    {
        JsonObject document = Complete();
        document["applicationCategoryType"] = category;
        document["contentBadgeType"] = ApplicationCategories.BadgeFor((ApplicationCategory)category);

        ParamReport report = ParamJson.Check(document);
        Assert.True(report.IsValid);
        Assert.Equal((ApplicationCategory)category, report.Category);
    }

    // Anything between the recognised values is a kind of title the system has no handling for.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(65535)]
    [InlineData(131072)]
    [InlineData(-1)]
    public void Check_RefusesAKindOfTitleTheSystemDoesNotKnow(int category)
    {
        JsonObject document = Complete();
        document["applicationCategoryType"] = category;

        ParamReport report = ParamJson.Check(document);
        Assert.False(report.IsValid);
        Assert.Null(report.Category);
        Assert.Contains(report.Faults, i => i.Field == "applicationCategoryType");
    }

    // A kind the system does not know is a mistake to report, not a gap to paper over.
    [Fact]
    public void Complete_LeavesAnUnrecognisedKindAlone()
    {
        JsonObject document = Complete();
        document["applicationCategoryType"] = 4242;

        ParamJson.Complete(document);
        Assert.Equal(4242, (int)document["applicationCategoryType"]!);
        Assert.False(ParamJson.Check(document).IsValid);
    }

    [Fact]
    public void Check_RefusesTheCategoryWrittenAsAString()
    {
        JsonObject document = Complete();
        document["applicationCategoryType"] = "0";

        Assert.Contains(ParamJson.Check(document).Faults, i => i.Field == "applicationCategoryType");
    }

    // The badge follows the kind of title: a title running its own module is badged as a game, and a
    // media application as other.
    [Theory]
    [InlineData(ApplicationCategory.Game, 1)]
    [InlineData(ApplicationCategory.MediaApp, 2)]
    [InlineData(ApplicationCategory.RnpsMediaApp, 2)]
    [InlineData(ApplicationCategory.WebMediaApp, 2)]
    [InlineData(ApplicationCategory.ShellApp, 2)]
    public void BadgeFor_FollowsTheKindOfTitle(ApplicationCategory category, int expected)
        => Assert.Equal(expected, ApplicationCategories.BadgeFor(category));

    [Fact]
    public void Check_ReportsABadgeThatDoesNotMatchTheKindOfTitle()
    {
        JsonObject document = Complete();
        document["contentBadgeType"] = 2;

        ParamIssue issue = ParamJson.Check(document).Issues.Single(i => i.Field == "contentBadgeType");
        Assert.Equal(ParamIssueLevel.Incomplete, issue.Level);
        Assert.True(issue.CanComplete);
    }

    [Fact]
    public void Complete_SettlesTheBadgeAgainstTheKindOfTitle()
    {
        JsonObject document = Complete();
        document["applicationCategoryType"] = (int)ApplicationCategory.MediaApp;

        Assert.Contains("contentBadgeType", ParamJson.Complete(document));
        Assert.Equal(2, (int)document["contentBadgeType"]!);
    }

    // A title that runs its own module names how it may be started; a media application does not.
    [Fact]
    public void Check_ExpectsTheWaysAGameIsStarted()
    {
        JsonObject document = Complete();
        document.Remove("gameIntent");

        Assert.Contains(ParamJson.Check(document).Issues, i => i.Field == "gameIntent");
    }

    [Fact]
    public void Check_DoesNotExpectTheWaysAMediaApplicationIsStarted()
    {
        JsonObject document = Complete();
        document["applicationCategoryType"] = (int)ApplicationCategory.MediaApp;
        document["contentBadgeType"] = 2;
        document.Remove("gameIntent");

        ParamReport report = ParamJson.Check(document);
        Assert.True(report.IsValid);
        Assert.DoesNotContain(report.Issues, i => i.Field.StartsWith("gameIntent", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Check_RefusesAWayOfStartingTheSystemDoesNotKnow()
    {
        JsonObject document = Complete();
        document["gameIntent"] = new JsonObject
        {
            ["permittedIntents"] = new JsonArray(new JsonObject { ["intentType"] = "launchSomehow" }),
        };

        Assert.Contains(ParamJson.Check(document).Faults, i => i.Field == "gameIntent.permittedIntents");
    }

    [Theory]
    [InlineData("PPSA99099", true)]
    [InlineData("ppsa99099", false)]
    [InlineData("PPSA9909", false)]
    [InlineData("PPSA990999", false)]
    [InlineData("PPS499099", false)]
    public void Check_ReadsTheTitleId(string titleId, bool valid)
    {
        JsonObject document = Complete();
        document["titleId"] = titleId;
        // The content id has to agree with the title id, so it moves with it and keeps its own length.
        string contentId = $"UP9000-{titleId}_00-PROSPERO00000000";
        document["contentId"] = contentId.Length >= 36 ? contentId[..36] : contentId.PadRight(36, '0');

        Assert.Equal(valid, !ParamJson.Check(document).Faults.Any(i => i.Field == "titleId"));
    }

    [Fact]
    public void Check_RefusesAContentIdThatNamesAnotherTitle()
    {
        JsonObject document = Complete();
        document["contentId"] = "UP9000-PPSA00001_00-PROSPERO00000000";

        Assert.Contains(ParamJson.Check(document).Faults, i => i.Field == "contentId");
    }

    [Theory]
    [InlineData("01.000.000", true)]
    [InlineData("01.00", false)]
    [InlineData("01.000.000.000", false)]
    [InlineData("01.000.abc", false)]
    public void Check_ReadsTheContentVersion(string version, bool valid)
    {
        JsonObject document = Complete();
        document["contentVersion"] = version;

        Assert.Equal(valid, !ParamJson.Check(document).Faults.Any(i => i.Field == "contentVersion"));
    }

    // Every way of starting a title that appears in a shipping one is accepted, including the ones no
    // field listing carries: refusing a value a real title uses would stop a build that should run.
    [Theory]
    [InlineData("launchActivity")]
    [InlineData("joinSession")]
    [InlineData("launchMultiplayerActivity")]
    [InlineData("launchByCustomParameters")]
    [InlineData("launchTournamentMatch")]
    public void Check_AcceptsEveryWayATitleIsStarted(string intentType)
    {
        JsonObject document = Complete();
        document["gameIntent"] = new JsonObject
        {
            ["permittedIntents"] = new JsonArray(new JsonObject { ["intentType"] = intentType }),
        };

        Assert.True(ParamJson.Check(document).IsValid);
    }

    [Theory]
    [InlineData("free", true)]
    [InlineData("standard", true)]
    [InlineData("upgradable", true)]
    [InlineData("demo", true)]
    [InlineData("freemium", true)]
    [InlineData("none", false)]
    [InlineData("Free", false)]
    public void Check_ReadsTheRightsModel(string drmType, bool valid)
    {
        JsonObject document = Complete();
        document["applicationDrmType"] = drmType;

        Assert.Equal(valid, ParamJson.Check(document).IsValid);
    }

    [Fact]
    public void Check_RefusesMetadataWithNoNameToDraw()
    {
        JsonObject document = Complete();
        document["localizedParameters"] = new JsonObject { ["defaultLanguage"] = "en-US" };

        Assert.Contains(ParamJson.Check(document).Faults, i => i.Field == "localizedParameters.en-US");
    }

    [Theory]
    [InlineData("0x1000000000000000", true)]
    [InlineData("0x0000000000000000", true)]
    [InlineData("10.00", false)]
    [InlineData("0x100000000000000", false)]
    public void Check_ReadsAPackedVersion(string version, bool valid)
    {
        JsonObject document = Complete();
        document["sdkVersion"] = version;

        Assert.Equal(valid, ParamJson.Check(document).IsValid);
    }

    [Theory]
    [InlineData("Game", ApplicationCategory.Game)]
    [InlineData("game", ApplicationCategory.Game)]
    [InlineData("0", ApplicationCategory.Game)]
    [InlineData("MediaApp", ApplicationCategory.MediaApp)]
    [InlineData("65536", ApplicationCategory.MediaApp)]
    [InlineData("67108864", ApplicationCategory.ShellApp)]
    public void TryParse_ReadsAKindByNameOrNumber(string text, ApplicationCategory expected)
    {
        Assert.True(ApplicationCategories.TryParse(text, out ApplicationCategory category));
        Assert.Equal(expected, category);
    }

    [Theory]
    [InlineData("BigApp")]
    [InlineData("1")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RefusesWhatIsNotAKindOfTitle(string? text)
        => Assert.False(ApplicationCategories.TryParse(text, out _));

    [Fact]
    public void All_HoldsEveryKindWithNoRepeats()
    {
        Assert.Equal(10, ApplicationCategories.All.Count);
        Assert.Equal(ApplicationCategories.All.Count, ApplicationCategories.All.Distinct().Count());
        Assert.All(ApplicationCategories.All, c => Assert.True(ApplicationCategories.IsKnown((int)c)));
    }

    // An application that ships and runs a module of its own is the kind this toolchain builds.
    [Fact]
    public void Default_IsTheKindAnApplicationCarryingItsOwnModuleUses()
        => Assert.Equal(ApplicationCategory.Game, ApplicationCategories.Default);
}
