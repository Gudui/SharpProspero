// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Audio;

/// <summary>
/// The audio job manager: batched decode and encode for Opus, AAC, MP3, ATRAC9 and the other codecs. Signatures from ajm.h.
/// </summary>
public static unsafe partial class Ajm
{
    private const string Lib = "libSceAjm";

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmInitialize(long initializeFlag, void* pContext);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmFinalize(uint uiContext);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmMemoryRegister(uint uiContext, void* pRegion, nuint szNumPages);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmMemoryUnregister(uint uiContext, void* pRegion);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmModuleRegister(uint uiContext, uint uiCodec, long iReserved);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmModuleUnregister(uint uiContext, uint uiCodec);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmInstanceCreate(uint uiContext, uint uiCodec, ulong uiFlags, void* pInstance);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmInstanceExtend(uint uiContext, uint uiCodec, ulong uiFlags, uint uiInstance);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmInstanceSwitch(uint uiContext, uint uiCodec, ulong uiFlags, uint uiInstance);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmInstanceDestroy(uint uiContext, uint uiInstance);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchInitialize(void* pBuffer, nuint szBuffer, void* pInfo);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobInitialize(void* pInfo, uint uiInstance, void* pCodecParameters, nuint szCodecParametersSize, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobClearContext(void* pInfo, uint uiInstance, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobDecode(void* pInfo, uint uiInstance, void* pBitstreamInput, nuint szBitstreamInputSize, void* pPcmOutput, nuint szPcmOutputSize, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobDecodeSingle(void* pInfo, uint uiInstance, void* pBitstreamInput, nuint szBitstreamInputSize, void* pPcmOutput, nuint szPcmOutputSize, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobDecodeSplit(void* pInfo, uint uiInstance, void* pDataInputBuffers, nuint szNumDataInputBuffers, void* pDataOutputBuffers, nuint szNumDataOutputBuffers, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobEncode(void* pInfo, uint uiInstance, void* pPcmInput, nuint szPcmInputSize, void* pBitstreamOutput, nuint szBitstreamOutputSize, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobGetInfo(void* pInfo, uint uiInstance, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobGetCodecInfo(void* pInfo, uint uiInstance, void* pResult, nuint szResultSize);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobSetGaplessDecode(void* pInfo, uint uiInstance, void* pGaplessDecode, int iReset, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobGetGaplessDecode(void* pInfo, uint uiInstance, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobSetResampleParameters(void* pInfo, uint uiInstance, float fResampleRatio, uint uiFlags, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobSetResampleParametersEx(void* pInfo, uint uiInstance, float fRatioStart, float fRatioChangePerSample, uint uiFlags, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobGetResampleInfo(void* pInfo, uint uiInstance, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchStart(uint uiContext, void* pInfo, int iPriority, void* pBatchError, void* pBatch);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchWait(uint uiContext, uint uiBatch, uint uiTimeout, void* pBatchError);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchCancel(uint uiContext, uint uiBatch);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchErrorDump(void* pInfo, void* pBatchError);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobGetStatistics(void* pInfo, float fInterval, void* pResult);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobControl(void* pInfo, uint uiInstance, ulong uiFlags, void* pSidebandInput, nuint szSidebandInputSize, void* pSidebandOutput, nuint szSidebandOutputSize);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobRun(void* pInfo, uint uiInstance, ulong uiFlags, void* pDataInput, nuint szDataInputSize, void* pDataOutput, nuint szDataOutputSize, void* pSidebandOutput, nuint szSidebandOutputSize);

    /// <summary>Imported from the module.</summary>
    [LibraryImport(Lib)]
    public static partial int sceAjmBatchJobRunSplit(void* pInfo, uint uiInstance, ulong uiFlags, void* pDataInputBuffers, nuint szNumDataInputBuffers, void* pDataOutputBuffers, nuint szNumDataOutputBuffers, void* pSidebandOutput, nuint szSidebandOutputSize);

}