using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

/// <summary>TPS1 envelope: AES-256-CBC + HMAC-SHA256 encrypt-then-MAC. No custom cipher.
/// 48-byte authenticated header: magic, payload kind, version/reserved, 16-byte key id,
/// random 16-byte IV, little-endian plaintext length; ciphertext; 32-byte authentication tag.
/// The two independent 32-byte keys are generated with the OS CSPRNG. Authentication precedes decryption.</summary>
public static class TpsCrypto
{
    public const int HeaderSize = 48, TagSize = 32;
    public const byte Bundle = 1, Aac = 2;
    public const long MaxPlainBytes = 512L * 1024 * 1024;
    public static byte[] RandomBytes(int length) { var bytes = new byte[length]; using var rng = RandomNumberGenerator.Create(); rng.GetBytes(bytes); return bytes; }
    public static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    public static bool IsTps(byte[] bytes) => bytes != null && bytes.Length >= 4 && bytes[0] == 84 && bytes[1] == 80 && bytes[2] == 83 && bytes[3] == 49;
    public static string KeyId(byte[] bytes)
    {
        ValidateHeader(bytes);
        var id = new byte[16]; Buffer.BlockCopy(bytes, 8, id, 0, 16); return Hex(id);
    }
    public static void ValidateHeader(byte[] bytes)
    {
        if (!IsTps(bytes) || bytes.Length < HeaderSize + 16 + TagSize || bytes[5] != 1 || bytes[6] != 0 || bytes[7] != 0 ||
            (bytes[4] != Bundle && bytes[4] != Aac) || (bytes.Length - HeaderSize - TagSize) % 16 != 0)
            throw new CryptographicException("Invalid TPS envelope.");
        long length = ReadLength(bytes);
        if (length < 0 || length > MaxPlainBytes || length / 16 * 16 + 16 != bytes.Length - HeaderSize - TagSize)
            throw new CryptographicException("Invalid TPS payload length.");
    }
    private static long ReadLength(byte[] bytes)
    {
        ulong length = 0; for (int i = 0; i < 8; i++) length |= (ulong)bytes[40 + i] << (i * 8); return unchecked((long)length);
    }
    public static byte[] Encrypt(byte[] plain, byte kind, byte[] id, byte[] keys)
    {
        if (plain == null || plain.LongLength > MaxPlainBytes || id == null || id.Length != 16 || keys == null || keys.Length != 64 || (kind != Bundle && kind != Aac)) throw new ArgumentException("Invalid TPS encryption input.");
        byte[] iv = RandomBytes(16), encKey = new byte[32], macKey = new byte[32];
        Buffer.BlockCopy(keys, 0, encKey, 0, 32); Buffer.BlockCopy(keys, 32, macKey, 0, 32);
        try
        {
            using var output = new MemoryStream();
            using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
            {
                writer.Write(new byte[] {84, 80, 83, 49, kind, 1, 0, 0}); writer.Write(id); writer.Write(iv); writer.Write(plain.LongLength);
            }
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = encKey; aes.IV = iv;
                using var transform = aes.CreateEncryptor();
                using var crypto = new CryptoStream(output, transform, CryptoStreamMode.Write, true);
                crypto.Write(plain, 0, plain.Length); crypto.FlushFinalBlock();
            }
            byte[] authenticated = output.ToArray();
            using var hmac = new HMACSHA256(macKey);
            byte[] tag = hmac.ComputeHash(authenticated);
            output.Write(tag, 0, tag.Length); return output.ToArray();
        }
        finally { Array.Clear(encKey, 0, encKey.Length); Array.Clear(macKey, 0, macKey.Length); }
    }
    public static byte[] Decrypt(byte[] sealedBytes, byte[] keys, byte expectedKind)
    {
        ValidateHeader(sealedBytes);
        if (keys == null || keys.Length != 64 || sealedBytes[4] != expectedKind) throw new CryptographicException("TPS key or payload kind is invalid.");
        byte[] encKey = new byte[32], macKey = new byte[32], iv = new byte[16];
        Buffer.BlockCopy(keys, 0, encKey, 0, 32); Buffer.BlockCopy(keys, 32, macKey, 0, 32); Buffer.BlockCopy(sealedBytes, 24, iv, 0, 16);
        try
        {
            using var hmac = new HMACSHA256(macKey);
            byte[] expected = hmac.ComputeHash(sealedBytes, 0, sealedBytes.Length - TagSize);
            int difference = 0;
            for (int i = 0; i < TagSize; i++) difference |= expected[i] ^ sealedBytes[sealedBytes.Length - TagSize + i];
            if (difference != 0) throw new CryptographicException("TPS authentication failed.");
            using var aes = Aes.Create(); aes.KeySize = 256; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = encKey; aes.IV = iv;
            using var transform = aes.CreateDecryptor();
            byte[] plain = transform.TransformFinalBlock(sealedBytes, HeaderSize, sealedBytes.Length - HeaderSize - TagSize);
            if (plain.LongLength != ReadLength(sealedBytes)) { Array.Clear(plain, 0, plain.Length); throw new CryptographicException("TPS decoded length mismatch."); }
            return plain;
        }
        finally { Array.Clear(encKey, 0, encKey.Length); Array.Clear(macKey, 0, macKey.Length); }
    }
}
