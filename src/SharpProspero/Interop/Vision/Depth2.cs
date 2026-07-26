// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Vision;

/// <summary>The pixel form of the source images the depth library reads.</summary>
public enum SceDepth2PixelFormat : int
{
    /// <summary>8-bit luminance.</summary>
    Y8 = 0,
    /// <summary>Packed 4:2:2 luminance and chrominance.</summary>
    Yuv422 = 1,
}

/// <summary>How the depth library treats the source image.</summary>
public enum SceDepth2ExecutionMode : int
{
    /// <summary>Work on the caller's image in place rather than copying it.</summary>
    DoNotCopySourceImage = 1,
}

/// <summary>The depth-generation quality and cost profile.</summary>
public enum SceDepth2Profile : int
{
    /// <summary>Profile 1.5.</summary>
    Profile15 = 2,
    /// <summary>Profile 1.6.</summary>
    Profile16 = 4,
    /// <summary>Profile 2.0.</summary>
    Profile20 = 3,
}

/// <summary>The stereo camera the depth is generated from.</summary>
public enum SceDepth2StereoCameraType : int
{
    /// <summary>The HD stereo camera.</summary>
    HdCamera = 1,
}

/// <summary>Whether the library folds camera metadata into the depth generation.</summary>
public enum SceDepth2UseCameraInformation : int
{
    /// <summary>Do not use the camera metadata.</summary>
    Disable = 0,
    /// <summary>Use the camera metadata to improve the result.</summary>
    Enable = 1,
}

/// <summary>Which produced image <c>sceDepth2GetImage</c> returns.</summary>
public enum SceDepth2ImageType : int
{
    /// <summary>The 16-bit depth map. <c>0xFFFF</c> marks an invalid depth value.</summary>
    Depth16Bit = 0,
}

/// <summary>The size and alignment of the working memory the library needs.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceDepth2QueryMemoryResult
{
    /// <summary>Required working-memory size in bytes.</summary>
    public nuint MemorySize;
    /// <summary>Required working-memory alignment in bytes.</summary>
    public nuint MemoryAlignment;
}

/// <summary>The working memory the caller hands the library.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceDepth2MemoryInformation
{
    /// <summary>The working-memory block.</summary>
    public void* MemoryChunk;
    /// <summary>The block size in bytes.</summary>
    public nuint ChunkSize;
}

/// <summary>The source image's extent and form.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceDepth2InputImageInformation
{
    /// <summary>Source width in pixels.</summary>
    public int Width;
    /// <summary>Source height in pixels.</summary>
    public int Height;
    /// <summary>The source pixel form.</summary>
    public SceDepth2PixelFormat PixelFormat;
}

/// <summary>How the depth is processed: the region, mode, profile, and camera.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceDepth2ProcessingInformation
{
    /// <summary>Processing region width in pixels.</summary>
    public int Width;
    /// <summary>Processing region height in pixels.</summary>
    public int Height;
    /// <summary>The execution mode (see <see cref="SceDepth2ExecutionMode"/>).</summary>
    public int ExecutionMode;
    /// <summary>The depth profile.</summary>
    public SceDepth2Profile DepthProfile;
    private int _reserved0;
    private int _reserved1;
    /// <summary>The stereo camera type.</summary>
    public SceDepth2StereoCameraType CameraType;
}

/// <summary>Which GPU pipe and queue the library runs on.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceDepth2PlatformInformation
{
    /// <summary>The GPU pipe.</summary>
    public int Pipe;
    /// <summary>The GPU queue.</summary>
    public int Queue;
    /// <summary>Reserved context pointer.</summary>
    public void* Context;
}

/// <summary>The full parameter set for <c>sceDepth2Initialize</c> and <c>sceDepth2QueryMemory</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceDepth2InitializeParameter
{
    /// <summary>Set to the size of this structure in bytes.</summary>
    public int SizeofInitializeParameter;
    /// <summary>The source image description.</summary>
    public SceDepth2InputImageInformation InputImageInformation;
    /// <summary>The processing description.</summary>
    public SceDepth2ProcessingInformation ProcessingInformation;
    /// <summary>The pipe and queue to run on.</summary>
    public SceDepth2PlatformInformation PlatformInformation;
}

/// <summary>
/// The stereo-camera depth-generation library. Query the working memory a configuration needs, initialize
/// the library into a caller-provided block to get a handle, then per frame set the source images with a
/// command, submit, wait, and read back the 16-bit depth map. This is the flat interface exactly as the
/// vision header declares it. The per-frame command parameter embeds camera metadata (from the camera
/// library), so it is passed as a pointer the caller builds.
/// </summary>
public static unsafe partial class Depth2
{
    private const string Lib = "libSceDepth2";

    /// <summary>The depth value that marks an invalid (unknown) pixel.</summary>
    public const ushort InvalidDepthValue = 0xffff;

    /// <summary>Reports the working memory the given configuration needs.</summary>
    [LibraryImport(Lib)]
    public static partial int sceDepth2QueryMemory(SceDepth2InitializeParameter* initializeParameter, SceDepth2QueryMemoryResult* memoryQueryResult);

    /// <summary>Initializes the library into the caller's working memory. Returns a non-negative handle, or a negative error.</summary>
    [LibraryImport(Lib)]
    public static partial int sceDepth2Initialize(SceDepth2InitializeParameter* initializeParameter, SceDepth2MemoryInformation* memoryInformation);

    /// <summary>Tears the library down.</summary>
    [LibraryImport(Lib)]
    public static partial int sceDepth2Terminate(int handle);

    /// <summary>Sets the source images and options for the next depth pass. The parameter is a <c>SceDepth2SetCommandParameter</c>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceDepth2SetCommand(int handle, void* parameter);

    /// <summary>Submits the depth pass to the GPU.</summary>
    [LibraryImport(Lib)]
    public static partial int sceDepth2Submit(int handle);

    /// <summary>Waits for the pass to finish and runs its post-processing.</summary>
    [LibraryImport(Lib)]
    public static partial int sceDepth2WaitAndExecutePostProcess(int handle);

    /// <summary>Sets the region of interest, in normalized [0, 1] coordinates.</summary>
    [LibraryImport(Lib)]
    public static partial int sceDepth2SetRoi(int handle, float sx, float sy, float ex, float ey);

    /// <summary>Copies a produced image into the caller's buffer.</summary>
    [LibraryImport(Lib)]
    public static partial int sceDepth2GetImage(int handle, SceDepth2ImageType imageType, void* destinationBuffer, int destinationBufferSize);

}
