using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

/// <summary>Offline content keys. Deliberately not a premium entitlement or a DRM boundary.
/// A determined reverse engineer can recover shipped keys; no network is required to play.</summary>
public static class TpsKeyring
{
    [Serializable] public class Entry { public string id, material, name; }
    [Serializable] public class Data { public string root; public List<Entry> keys = new(); }
    public static byte[] Derive(byte[] root, string id)
    {
        if (root == null || root.Length != 32 || id == null || id.Length != 32) throw new CryptographicException("Invalid TPS root or key id.");
        using var hmac = new HMACSHA256(root);
        var key = new byte[64];
        var aes = hmac.ComputeHash(System.Text.Encoding.ASCII.GetBytes("TPS1/AES/" + id));
        var mac = hmac.ComputeHash(System.Text.Encoding.ASCII.GetBytes("TPS1/MAC/" + id));
        Buffer.BlockCopy(aes, 0, key, 0, 32); Buffer.BlockCopy(mac, 0, key, 32, 32);
        Array.Clear(aes, 0, aes.Length); Array.Clear(mac, 0, mac.Length); return key;
    }
    private static Data s_keys;
    public static byte[] Resolve(string id)
    {
        if (s_keys == null)
        {
            var asset = Resources.Load<TextAsset>("TpsKeyring");
            if (asset == null) throw new CryptographicException("Offline TPS keyring is missing.");
            s_keys = JsonUtility.FromJson<Data>(asset.text);
        }
        var entry = s_keys?.keys?.Find(k => k.id == id);
        if (entry == null)
        {
            if (string.IsNullOrEmpty(s_keys?.root)) throw new CryptographicException("Offline TPS content key is missing.");
            var root = Convert.FromBase64String(s_keys.root);
            try { return Derive(root, id); }
            finally { Array.Clear(root, 0, root.Length); }
        }
        var key = Convert.FromBase64String(entry.material);
        if (key.Length != 64) throw new CryptographicException("Invalid offline TPS content key.");
        return key;
    }
    public static byte[] Open(byte[] bytes, byte kind)
    {
        var keys = Resolve(TpsCrypto.KeyId(bytes));
        try { return TpsCrypto.Decrypt(bytes, keys, kind); }
        finally { Array.Clear(keys, 0, keys.Length); }
    }
    public static void Reset() => s_keys = null;
}
