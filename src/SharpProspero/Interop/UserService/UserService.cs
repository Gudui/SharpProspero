// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.UserService;

/// <summary>
/// User-service bindings. The service resolves the signed-in profile that owns input and display
/// resources. Initialize it once at startup before opening a controller.
/// </summary>
public static unsafe partial class UserService
{
    private const string Lib = "libSceUserService";

    /// <summary>Starts the service with default parameters.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceUserServiceInitialize(void* initParams);

    /// <summary>Stops the service.</summary>
    [LibraryImport(Lib)]
    public static partial int sceUserServiceTerminate();

    /// <summary>Writes the initial (first signed-in) user id to <paramref name="userId"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceUserServiceGetInitialUser(int* userId);

    /// <summary>The most users signed in at once.</summary>
    public const int MaxLoginUsers = 4;

    /// <summary>The longest a user name is, in characters.</summary>
    public const int MaxUserNameLength = 16;

    /// <summary>
    /// Writes the ids of the signed-in users into <paramref name="userIdList"/>, a buffer of
    /// <see cref="MaxLoginUsers"/> ints. Unused slots hold <c>SceUser.Invalid</c>.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceUserServiceGetLoginUserIdList(int* userIdList);

    /// <summary>
    /// Writes <paramref name="userId"/>'s display name into <paramref name="userName"/> as a
    /// null-terminated UTF-8 string, up to <paramref name="size"/> bytes.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceUserServiceGetUserName(int userId, byte* userName, nuint size);

    /// <summary>Writes the number the system assigned <paramref name="userId"/> to <paramref name="number"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceUserServiceGetUserNumber(int userId, int* number);
}
