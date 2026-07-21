// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Storage;
using System;
using System.Collections.Generic;
using Xunit;

namespace SharpProspero.Tests;

public sealed class SaveStateTests
{
    [Fact]
    public void Write_ThenRead_RoundTripsVersionAndData()
    {
        JsonValue data = JsonValue.NewObject();
        data["level"] = JsonValue.Of(3d);
        data["name"] = JsonValue.Of("hero");
        string json = new SaveState(2, data).Write();

        SaveState loaded = SaveState.Read(json);
        Assert.Equal(2, loaded.Version);
        Assert.Equal(3, loaded.Data["level"].AsInt());
        Assert.Equal("hero", loaded.Data["name"].AsString());
    }

    [Fact]
    public void Read_RejectsTextThatIsNotASaveDocument()
    {
        Assert.Throws<JsonException>(() => SaveState.Read("{\"data\":{}}")); // no version
        Assert.Throws<JsonException>(() => SaveState.Read("[1,2,3]"));       // not an object
    }

    [Fact]
    public void MigrateTo_AppliesEachStepInTurn()
    {
        JsonValue data = JsonValue.NewObject();
        data["hp"] = JsonValue.Of(100d);
        var save = new SaveState(1, data);

        var migrations = new Dictionary<int, Func<JsonValue, JsonValue>>
        {
            // v1 -> v2: rename hp to health
            [1] = d =>
            {
                JsonValue next = JsonValue.NewObject();
                next["health"] = d["hp"];
                return next;
            },
            // v2 -> v3: add a default shield
            [2] = d =>
            {
                d["shield"] = JsonValue.Of(50d);
                return d;
            },
        };

        SaveState migrated = save.MigrateTo(3, migrations);
        Assert.Equal(3, migrated.Version);
        Assert.Equal(100, migrated.Data["health"].AsInt());
        Assert.Equal(50, migrated.Data["shield"].AsInt());
    }

    [Fact]
    public void Read_RejectsANonNumericOrFractionalVersion()
    {
        Assert.Throws<JsonException>(() => SaveState.Read("{\"version\":\"3\",\"data\":{}}")); // string, not 3
        Assert.Throws<JsonException>(() => SaveState.Read("{\"version\":1.5,\"data\":{}}"));   // fractional
        Assert.Throws<JsonException>(() => SaveState.Read("{\"version\":true}"));              // boolean
    }

    [Fact]
    public void MigrateTo_DoesNotMutateTheOriginalSave()
    {
        JsonValue data = JsonValue.NewObject();
        data["hp"] = JsonValue.Of(10d);
        var original = new SaveState(1, data);

        var migrations = new Dictionary<int, Func<JsonValue, JsonValue>>
        {
            [1] = d => { d["flag"] = JsonValue.Of(true); return d; }, // edits its input in place
        };
        SaveState migrated = original.MigrateTo(2, migrations);

        Assert.True(migrated.Data["flag"].AsBool());
        Assert.False(original.Data.ContainsKey("flag")); // the original save is untouched
    }

    [Fact]
    public void MigrateTo_TheCurrentVersionIsANoOp()
    {
        var save = new SaveState(4, JsonValue.NewObject());
        SaveState same = save.MigrateTo(4, new Dictionary<int, Func<JsonValue, JsonValue>>());
        Assert.Equal(4, same.Version);
    }

    [Fact]
    public void MigrateTo_RejectsAMissingStepOrAnOlderTarget()
    {
        var save = new SaveState(1, JsonValue.NewObject());
        Assert.Throws<InvalidOperationException>(
            () => save.MigrateTo(3, new Dictionary<int, Func<JsonValue, JsonValue>> { [1] = d => d })); // no step from 2
        Assert.Throws<ArgumentOutOfRangeException>(
            () => save.MigrateTo(0, new Dictionary<int, Func<JsonValue, JsonValue>>()));
    }
}
