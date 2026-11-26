// Disc input for the Windows release. zlib1.dll ships with the port.
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal static class Disc
{
    [DllImport("zlib1.dll", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    static extern IntPtr gzopen_w([MarshalAs(UnmanagedType.LPWStr)] string file,
                                  [MarshalAs(UnmanagedType.LPStr)] string mode);
    [DllImport("zlib1.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int gzdirect(IntPtr file);
    [DllImport("zlib1.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int gzread(IntPtr file, [Out] byte[] data, uint count);
    [DllImport("zlib1.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr gzerror(IntPtr file, out int code);
    [DllImport("zlib1.dll", CallingConvention = CallingConvention.Cdecl)]
    static extern int gzclose(IntPtr file);

    internal static bool IsGzip(string path)
    {
        return path.EndsWith(".iso.gz", StringComparison.OrdinalIgnoreCase);
    }

    internal static string Open(string path, string cache, Action<string, long> progress)
    {
        path = Path.GetFullPath(path);
        if (!IsGzip(path)) return path;
        byte[] buffer = new byte[1024 * 1024];
        // Keep the source open read-only through both passes. An explicit reimport
        // of the same compressed content needs no further multi-gigabyte write.
        using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var sha = SHA256.Create())
        {
            int n;
            while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.TransformBlock(buffer, 0, n, buffer, 0);
                progress("Checking compressed image", source.Position);
            }
            sha.TransformFinalBlock(buffer, 0, 0);
            string hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            Directory.CreateDirectory(cache);
            foreach (string ready in Directory.GetFiles(cache, hash + "-*.iso"))
            {
                long length;
                string suffix = Path.GetFileNameWithoutExtension(ready).Substring(hash.Length + 1);
                if (long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out length)
                    && new FileInfo(ready).Length == length) return ready;
            }

            string temp = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".part");
            try
            {
                long length = 0;
                IntPtr gzip = gzopen_w(path, "rb");
                if (gzip == IntPtr.Zero) throw new IOException("Could not open the compressed image.");
                int closeResult;
                try
                {
                    if (gzdirect(gzip) != 0) throw new InvalidDataException("This file has no gzip header.");
                    using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        while ((n = gzread(gzip, buffer, (uint)buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, n);
                            length += n;
                            progress("Expanding ISO", length);
                        }
                        int code;
                        IntPtr detail = gzerror(gzip, out code);
                        if (n < 0 || code < 0) throw new InvalidDataException(
                            "Incomplete or damaged gzip image. " + Marshal.PtrToStringAnsi(detail));
                    }
                }
                finally { closeResult = gzclose(gzip); }
                if (closeResult != 0) throw new InvalidDataException("The gzip stream did not finish cleanly.");
                string result = Path.Combine(cache, hash + "-" + length.ToString(CultureInfo.InvariantCulture) + ".iso");
                try { File.Move(temp, result); }
                catch (IOException)
                {
                    if (!File.Exists(result)) throw;
                    if (new FileInfo(result).Length != length) File.Replace(temp, result, null);
                }
                return result;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
