namespace DynamicIsland.Services;

/// Google OAuth client shipped with the build, so a fresh install does not
/// start with two fields to paste.
///
/// These are scrambled, not protected. An installed app cannot keep a secret
/// from the machine it runs on, which is why Google does not treat the secret
/// of a Desktop client as confidential and requires PKCE instead. The point
/// here is only to keep the values out of a plain string search.
///
/// Anything typed in Settings wins over these. If the repository ever becomes
/// public, issue a new secret in the Google Cloud Console.
public static class EmbeddedCredentials
{
    const string Key = "dynamic-island-windows";

    public static string ClientId => Unscramble("V05WUl9YURRaQVhSQw5KHRkPAwUBBRceGlQfAAtHXhZdAF9SWB4NWwAARAMQVw8RHRpNSgYcCw0LEV4SGw0LAQMWCg1AAgIE");

    public static string ClientSecret => Unscramble("IzYtMj0xTk9ZOCJTNg1pEgEvIzhDCj1BBVkUDgYAMCIdOQA=");

    static string Unscramble(string value)
    {
        byte[] raw = System.Convert.FromBase64String(value);
        byte[] key = System.Text.Encoding.UTF8.GetBytes(Key);

        for (int i = 0; i < raw.Length; i++) raw[i] ^= key[i % key.Length];

        return System.Text.Encoding.UTF8.GetString(raw);
    }
}
