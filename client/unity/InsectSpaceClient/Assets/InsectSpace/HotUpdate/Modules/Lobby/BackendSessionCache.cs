using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace InsectSpace.Gameplay.Modules
{
    // Platforms install an OS-backed protector; unsupported platforms fail closed for persistence.
    public interface ISessionCacheProtector
    {
        byte[] Protect(byte[] plaintext);
        byte[] Unprotect(byte[] ciphertext);
    }

    public sealed class BackendSessionCache
    {
        [Serializable]
        private sealed class Entry
        {
            public string token;
            public string expiresAt;
            public string authority;
        }

        private readonly string path;
        private readonly string authority;
        private readonly ISessionCacheProtector protector;
        public static Func<ISessionCacheProtector> PlatformProtectorFactory { get; set; }
        public bool Supported => protector != null;
        public bool Exists => File.Exists(path);

        public BackendSessionCache(string endpoint, string directory = null, ISessionCacheProtector protector = null)
        {
            authority = new Uri(endpoint, UriKind.Absolute).AbsoluteUri.TrimEnd('/');
            using (var sha = SHA256.Create())
                path = Path.Combine(directory ?? Path.Combine(Application.persistentDataPath, "SessionCache"),
                    BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(authority))).Replace("-", "") + ".bin");
            this.protector = protector ?? PlatformProtectorFactory?.Invoke();
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (this.protector == null) this.protector = new WindowsSessionProtector();
#endif
        }

        public bool TryLoad(out string token)
        {
            token = null;
            if (!Supported || !File.Exists(path)) return false;
            try
            {
                if (new FileInfo(path).Length > 16384) throw new InvalidDataException();
                var value = JsonUtility.FromJson<Entry>(Encoding.UTF8.GetString(protector.Unprotect(File.ReadAllBytes(path))));
                if (value == null || value.authority != authority || !ValidToken(value.token) ||
                    !DateTimeOffset.TryParse(value.expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry) ||
                    expiry <= DateTimeOffset.UtcNow) { Clear(); return false; }
                token = value.token;
                return true;
            }
            catch (Exception) { Clear(); return false; }
        }

        public bool Save(string token, string expiresAt)
        {
            if (!Supported || !ValidToken(token) ||
                !DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry) ||
                expiry <= DateTimeOffset.UtcNow) { Clear(); return false; }
            var temporary = path + ".tmp";
            try
            {
                var json = JsonUtility.ToJson(new Entry { token = token, expiresAt = expiresAt, authority = authority });
                var ciphertext = protector.Protect(Encoding.UTF8.GetBytes(json));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(temporary, ciphertext);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception) { Clear(); return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        public bool Clear()
        {
            try { if (File.Exists(path)) File.Delete(path); return true; }
            catch (Exception) { return false; }
        }

        private static bool ValidToken(string token)
        {
            if (token == null || token.Length != 64) return false;
            foreach (char character in token) if (!Uri.IsHexDigit(character)) return false;
            return true;
        }
    }

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    internal sealed class WindowsSessionProtector : ISessionCacheProtector
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Blob { public int Length; public IntPtr Data; }
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
        public byte[] Protect(byte[] plaintext) => Transform(plaintext, true);
        public byte[] Unprotect(byte[] ciphertext) => Transform(ciphertext, false);
        private static byte[] Transform(byte[] bytes, bool protect)
        {
            var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
            Blob output = default;
            try
            {
                Marshal.Copy(bytes, 0, input.Data, bytes.Length);
                bool success = protect
                    ? CryptProtectData(ref input, "InsectSpace session", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                    : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
                if (!success) throw new CryptographicException("The operating system could not protect the session cache.");
                var result = new byte[output.Length];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally
            {
                for (int i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
                Marshal.FreeHGlobal(input.Data);
                if (output.Data != IntPtr.Zero)
                {
                    for (int i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                    LocalFree(output.Data);
                }
            }
        }
    }
#endif
}
