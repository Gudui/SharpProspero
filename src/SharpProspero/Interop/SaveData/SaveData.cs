// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.SaveData;

/// <summary>How a save is mounted.</summary>
[System.Flags]
public enum SaveDataMountMode : uint
{
    /// <summary>Read only.</summary>
    ReadOnly = 0x00000001,

    /// <summary>Read and write.</summary>
    ReadWrite = 0x00000002,

    /// <summary>Create the save if it does not exist.</summary>
    Create = 0x00000004,

    /// <summary>Copy the application's icon into a newly created save.</summary>
    CopyIcon = 0x00000010,
}

/// <summary>How a save is unmounted.</summary>
[System.Flags]
public enum SaveDataUmountMode : uint
{
    /// <summary>Fail if there are uncommitted changes.</summary>
    Default = 0,

    /// <summary>Commit changes on unmount.</summary>
    Commit = 0x00000001,
}

/// <summary>The key a directory search sorts by.</summary>
public enum SaveDataSortKey : uint
{
    DirName = 0,
    UserParam = 1,
    Blocks = 2,
    ModifiedTime = 3,
    FreeBlocks = 5,
}

/// <summary>The order a directory search sorts in.</summary>
public enum SaveDataSortOrder : uint
{
    Ascending = 0,
    Descending = 1,
}

/// <summary>A title id (10 characters plus padding).</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public unsafe struct SceSaveDataTitleId
{
    public fixed byte Data[10];
    private fixed byte _padding[6];
}

/// <summary>A save directory name (up to 32 characters).</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public unsafe struct SceSaveDataDirName
{
    public fixed byte Data[32];
}

/// <summary>A mount point path (up to 16 characters).</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public unsafe struct SceSaveDataMountPoint
{
    public fixed byte Data[16];
}

/// <summary>The parameters that describe a save: its title, subtitle, detail, and modified time.</summary>
[StructLayout(LayoutKind.Sequential, Size = 1328)]
public unsafe struct SceSaveDataParam
{
    public fixed byte Title[128];
    public fixed byte SubTitle[128];
    public fixed byte Detail[1024];
    public uint UserParam;
    private int _pad0;

    /// <summary>The modified time, as a Unix timestamp in seconds.</summary>
    public long Mtime;
    private fixed byte _reserved[32];
}

/// <summary>The parameters a mount is opened with.</summary>
[StructLayout(LayoutKind.Sequential, Size = 80)]
public unsafe struct SceSaveDataMount3
{
    public int UserId;
    private int _pad0;
    public SceSaveDataDirName* DirName;
    public ulong Blocks;
    public ulong SystemBlocks;
    public SaveDataMountMode MountMode;
    private int _pad1;
    public int Resource;
    private fixed byte _reserved[32];
}

/// <summary>The result of a mount: the mount point path and status.</summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
public unsafe struct SceSaveDataMountResult
{
    public SceSaveDataMountPoint MountPoint;
    public ulong RequiredBlocks;
    public uint Unused;
    public uint MountStatus;
    private fixed byte _reserved[28];
    private int _pad0;
}

/// <summary>The size and free space of a mounted save.</summary>
[StructLayout(LayoutKind.Sequential, Size = 48)]
public unsafe struct SceSaveDataMountInfo
{
    public ulong Blocks;
    public ulong FreeBlocks;
    private fixed byte _reserved[32];
}

/// <summary>The parameters of a delete.</summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
public unsafe struct SceSaveDataDelete
{
    public int UserId;
    private int _pad0;
    public SceSaveDataTitleId* TitleId;
    public SceSaveDataDirName* DirName;
    public uint Unused;
    private fixed byte _reserved[32];
    private int _pad1;
}

/// <summary>The condition a directory search runs under.</summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
public unsafe struct SceSaveDataDirNameSearchCond
{
    public int UserId;
    private int _pad0;
    public SceSaveDataTitleId* TitleId;
    public SceSaveDataDirName* DirName;
    public SaveDataSortKey Key;
    public SaveDataSortOrder Order;
    private fixed byte _reserved[32];
}

/// <summary>The result of a directory search: the matching directories and their parameters.</summary>
[StructLayout(LayoutKind.Sequential, Size = 56)]
public unsafe struct SceSaveDataDirNameSearchResult
{
    public uint HitNum;
    private int _pad0;
    public SceSaveDataDirName* DirNames;
    public uint DirNamesNum;
    public uint SetNum;
    public SceSaveDataParam* Params;
    public void* Infos;
    private fixed byte _reserved[12];
    private int _pad1;
}

/// <summary>Save data bindings.</summary>
public static unsafe partial class SaveData
{
    private const string Lib = "libSceSaveData";

    /// <summary>Starts the save-data service. Pass null for the default parameters.</summary>
    [LibraryImport(Lib)]
    public static partial int sceSaveDataInitialize3(void* initParam);

    /// <summary>Stops the save-data service.</summary>
    [LibraryImport(Lib)]
    public static partial int sceSaveDataTerminate();

    /// <summary>Mounts a save, returning its mount point in <paramref name="result"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceSaveDataMount3(SceSaveDataMount3* mount, SceSaveDataMountResult* result);

    /// <summary>Unmounts a save.</summary>
    [LibraryImport(Lib)]
    public static partial int sceSaveDataUmount2(uint mode, SceSaveDataMountPoint* mountPoint);

    /// <summary>Reads the size and free space of a mounted save.</summary>
    [LibraryImport(Lib)]
    public static partial int sceSaveDataGetMountInfo(SceSaveDataMountPoint* mountPoint, SceSaveDataMountInfo* info);

    /// <summary>Deletes a save.</summary>
    [LibraryImport(Lib)]
    public static partial int sceSaveDataDelete(SceSaveDataDelete* del);

    /// <summary>Searches for save directories matching <paramref name="cond"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceSaveDataDirNameSearch(SceSaveDataDirNameSearchCond* cond, SceSaveDataDirNameSearchResult* result);
}
