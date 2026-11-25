using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DeskAgent;
public static class KeyStore
{
    public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskAgent");
    static string FileFor(string provider)
    {
        if (provider is not "openai" and not "anthropic") throw new ArgumentException("Unknown provider");
        return Path.Combine(DataFolder, provider + ".keyblob");
    }
    public static string Read(string provider)
    {
        try { var path = FileFor(provider); return File.Exists(path) ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)) : ""; }
        catch (CryptographicException) { return ""; }
    }
    public static bool HasSaved(string provider) => File.Exists(FileFor(provider));
    public static void Save(string provider, string key, bool remember)
    {
        var path = FileFor(provider);
        if (!remember || key == "") { if (File.Exists(path)) File.Delete(path); return; }
        Directory.CreateDirectory(DataFolder);
        File.WriteAllBytes(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    }
}
