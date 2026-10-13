using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GitCompass.Infrastructure.GitHub;

namespace GitCompass.Infrastructure.Local;

/// <summary>Current-user Windows DPAPI. Tokens stay separate from application JSON and child environments.</summary>
public sealed class WindowsTokenStore(string? root = null) : ITokenStore
{
    private readonly string _path = Path.Combine(root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitCompass"), "credentials", "github.bin");
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<TokenSet?> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return null;
            var bytes = Protect(await File.ReadAllBytesAsync(_path, cancellationToken), false);
            try { return JsonSerializer.Deserialize<TokenSet>(bytes); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Win32Exception) { throw new GitHubException(GitHubFailure.SignInRequired, "The protected GitHub credential cannot be opened by this Windows user. Sign in again."); }
        finally { _gate.Release(); }
    }
    public async Task SaveAsync(TokenSet tokens, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokens));
            byte[] protectedBytes;
            try { protectedBytes = Protect(bytes, true); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(bytes); }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken); File.Move(temporary, _path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { _gate.Release(); }
    }
    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { if (File.Exists(_path)) File.Delete(_path); }
        finally { _gate.Release(); }
    }
    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        var input = new DataBlob { Length = bytes.Length, Data = pointer };
        DataBlob output = default;
        try
        {
            var success = encrypt
                ? CryptProtectData(ref input, "GitCompass GitHub", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (var index = 0; index < bytes.Length; index++) Marshal.WriteByte(pointer, index, 0);
            Marshal.FreeHGlobal(pointer);
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
