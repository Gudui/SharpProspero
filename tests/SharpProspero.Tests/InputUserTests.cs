// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Application;
using SharpProspero.Input;
using SharpProspero.Interop;
using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace SharpProspero.Tests;

// A controller, a keyboard and a mouse each belong to a signed-in user. The platform registers the
// handle against the user it was opened for and routes that user's samples to it, so a handle opened
// for the system user is accepted and then never delivers anything: the module draws, the system hands
// it the controller, and every button reads as released for as long as it runs. Nothing reports it.
// The default has to name the launching user, and these pin that.
public sealed class InputUserTests
{
    [Theory]
    [InlineData(typeof(GamePad))]
    [InlineData(typeof(Keyboard))]
    [InlineData(typeof(Mouse))]
    public void Open_DefaultsToTheLaunchingUserRatherThanTheSystem(Type device)
    {
        MethodInfo open = Assert.Single(
            device.GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name == "Open");
        ParameterInfo user = Assert.Single(open.GetParameters());

        Assert.Equal("userId", user.Name);
        Assert.True(user.HasDefaultValue, $"{device.Name}.Open takes a user with no default.");
        Assert.Equal(SceUser.Invalid, user.DefaultValue);
        Assert.NotEqual(SceUser.System, user.DefaultValue);
    }

    [Fact]
    public void AppConfig_OpensTheControllerForTheLaunchingUser()
        => Assert.Equal(SceUser.Invalid, new AppConfig().UserId);

    // The three values the pad service tells apart. The system user is only ever paired with the
    // remote control; a standard pad opened for it is the failure above.
    [Fact]
    public void TheSystemUserIsNotTheSameAsNoUser()
    {
        Assert.Equal(0xFF, SceUser.System);
        Assert.Equal(-1, SceUser.Invalid);
        Assert.NotEqual(SceUser.System, SceUser.Invalid);
    }

    [Theory]
    [InlineData("Input/GamePad.cs")]
    [InlineData("Input/Keyboard.cs")]
    [InlineData("Input/Mouse.cs")]
    [InlineData("Application/AppConfig.cs")]
    public void NoneOfThemOpensForTheSystemUser(string relativePath)
    {
        string source = File.ReadAllText(SourcePath(relativePath));

        // Catching it here names the reason. On the device the symptom is a module that draws
        // perfectly and answers nothing, which points at the interface rather than at the open.
        Assert.DoesNotMatch(new Regex(@"userId\s*=\s*SceUser\.System"), source);
        Assert.DoesNotMatch(new Regex(@"UserId\s*\{\s*get;\s*set;\s*\}\s*=\s*SceUser\.System"), source);
    }

    // The tests run from the build output, so the source tree is found by walking up to the folder
    // that holds it.
    private static string SourcePath(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "SharpProspero", relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not find src/SharpProspero/{relativePath} above the test output.");
    }
}
