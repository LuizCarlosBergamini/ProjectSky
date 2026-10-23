using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Wraps the save JSON in an envelope signed with HMAC-SHA256, so a file edited by hand is detected and rejected.
/// The JSON stays readable (handy for debugging). This stops casual editing only: the key ships inside the game,
/// as it would with any local save, so it is not meant to stop someone determined.
/// </summary>
public static class SaveSigner
{
    public const int FormatVersion = 1;

    // Changing this key invalidates every existing save.
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("Lucy.FromTheStars.Save::7f3c9a1e-52b4-4d0e-9a6f-c18e2b7d4a90");

    [Serializable]
    private class Envelope
    {
        public int format;
        public string payload;
        public string signature;
    }

    /// <summary>The file text for <paramref name="payloadJson"/>: the payload plus its signature.</summary>
    public static string Wrap(string payloadJson)
    {
        Envelope envelope = new()
        {
            format = FormatVersion,
            payload = payloadJson,
            signature = Sign(payloadJson)
        };
        return JsonUtility.ToJson(envelope, true);
    }

    /// <summary>Extracts the payload. False when the text is not an envelope or its signature does not match.</summary>
    public static bool TryUnwrap(string fileText, out string payloadJson)
    {
        payloadJson = null;
        if (string.IsNullOrWhiteSpace(fileText)) return false;

        Envelope envelope;
        try
        {
            envelope = JsonUtility.FromJson<Envelope>(fileText);
        }
        catch (Exception)
        {
            // Not JSON at all (truncated or garbage file).
            return false;
        }

        if (envelope == null || envelope.format != FormatVersion || envelope.payload == null || envelope.signature == null)
            return false;

        if (!FixedTimeEquals(Sign(envelope.payload), envelope.signature)) return false;

        payloadJson = envelope.payload;
        return true;
    }

    private static string Sign(string payloadJson)
    {
        using HMACSHA256 hmac = new(Key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson)));
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
