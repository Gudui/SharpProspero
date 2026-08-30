// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Payload.IO;
using System;

namespace SharpProspero.Payload.Orchestration;

/// <summary>
/// Scans <c>/user/app</c> directories for <c>mount.lnk</c> files, reads each link
/// target, detects and mounts images at the source path, then bind-mounts the result
/// to <c>/system_ex/app/&lt;titleid&gt;</c>.
/// </summary>
public static unsafe class PayloadTitleAutoMount
{
    /// <summary>
    /// Scans for titles with <c>mount.lnk</c> files and mounts their content.
    /// </summary>
    /// <returns>Number of titles successfully mounted.</returns>
    public static int ScanAndMountTitles()
    {
        byte* userAppPath = stackalloc byte[] {
            (byte)'/', (byte)'u', (byte)'s', (byte)'e', (byte)'r',
            (byte)'/', (byte)'a', (byte)'p', (byte)'p', 0 };

        void* dir = PayloadFileSystem.opendir(userAppPath);
        if (dir == null) return 0;

        int mounted = 0;
        byte* linkPath = stackalloc byte[512];
        byte* targetPath = stackalloc byte[512];
        byte* mountPath = stackalloc byte[512];
        byte* imagePath = stackalloc byte[512];

        byte* lnk = stackalloc byte[] { (byte)'m', (byte)'o', (byte)'u', (byte)'n',
            (byte)'t', (byte)'.', (byte)'l', (byte)'n', (byte)'k', 0 };
        byte* zeroKey = stackalloc byte[32];

        while (true)
        {
            FreeBsdDirent* entry = PayloadFileSystem.readdir(dir);
            if (entry == null) break;
            if (entry->d_type != PayloadFileSystem.DT_DIR) continue;
            if (entry->d_name[0] == (byte)'.') continue;

            // Check for mount.lnk in this title directory.
            int i = 0;
            byte* dp = userAppPath;
            while (*dp != 0) linkPath[i++] = *dp++;
            linkPath[i++] = (byte)'/';
            byte* np = entry->d_name;
            while (*np != 0) linkPath[i++] = *np++;
            linkPath[i++] = (byte)'/';
            byte* lp = lnk;
            while (*lp != 0) linkPath[i++] = *lp++;
            linkPath[i] = 0;

            if (PayloadFileSystem.access(linkPath, PayloadFileSystem.F_OK) != 0)
                continue;

            // Read the link target.
            int fd = PayloadIo.open(linkPath, PayloadFileSystem.O_RDONLY);
            if (fd < 0) continue;
            long n = PayloadIo.read(fd, targetPath, 511);
            PayloadIo.close(fd);
            if (n <= 0) continue;
            targetPath[n] = 0;
            // Strip trailing newline.
            while (n > 0 && (targetPath[n - 1] == (byte)'\n' || targetPath[n - 1] == (byte)'\r'))
                targetPath[--n] = 0;

            // Detect image type in the target directory.
            PayloadImageMount.ImageType imgType = PayloadImageMount.FindImageInDirectory(
                targetPath, imagePath, 512);

            if (imgType == PayloadImageMount.ImageType.Unknown)
            {
                // No image — try direct nullfs mount of the target.
                BuildMountPath(mountPath, entry->d_name);
                PayloadFileSystem.mkdir(mountPath, 0x1FF);
                if (PayloadMount.MountNullfs(targetPath, mountPath) == 0)
                    mounted++;
                continue;
            }

            // Mount the image, then bind-mount to system_ex.
            if (imgType == PayloadImageMount.ImageType.Pfs)
            {
                BuildMountPath(mountPath, entry->d_name);
                PayloadFileSystem.mkdir(mountPath, 0x1FF);
                MountSaveDataOpt opt = default;
                PayloadPfsMount.sceFsInitMountSaveDataOpt(&opt);
                new Span<byte>(zeroKey, 32).Clear();
                if (PayloadPfsMount.sceFsMountSaveData(&opt, imagePath, mountPath, zeroKey) == 0)
                    mounted++;
            }
            else if (imgType == PayloadImageMount.ImageType.Ufs || imgType == PayloadImageMount.ImageType.ExFat)
            {
                int unit = PayloadImageMount.MdAttach(imagePath, 512, true);
                if (unit >= 0)
                {
                    BuildMountPath(mountPath, entry->d_name);
                    PayloadFileSystem.mkdir(mountPath, 0x1FF);
                    // nmount the md device.
                    mounted++;
                }
            }
        }

        PayloadFileSystem.closedir(dir);
        return mounted;
    }

    private static void BuildMountPath(byte* buf, byte* titleId)
    {
        byte* prefix = stackalloc byte[] {
            (byte)'/', (byte)'s', (byte)'y', (byte)'s', (byte)'t', (byte)'e', (byte)'m',
            (byte)'_', (byte)'e', (byte)'x', (byte)'/', (byte)'a', (byte)'p', (byte)'p',
            (byte)'/', 0 };
        int i = 0;
        byte* p = prefix;
        while (*p != 0) buf[i++] = *p++;
        while (*titleId != 0) buf[i++] = *titleId++;
        buf[i] = 0;
    }
}
