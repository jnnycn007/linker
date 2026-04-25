using linker.libs.extends;
using System;
using System.Security.Cryptography;
using System.Text;

namespace linker.libs
{
    public static class CryptoFactory
    {
        public static ISymmetricCrypto CreateSymmetric(string password, PaddingMode mode = PaddingMode.ANSIX923)
        {
            return new AesCrypto(password, mode);
        }
        public static ISymmetricCryptoGcm CreateSymmetricGcm(string password)
        {
            return new AesGcmFast(password);
        }
    }

    public interface ICrypto
    {
        public byte[] Encode(byte[] buffer);
        public byte[] Encode(byte[] buffer, int offset, int length);
        public Memory<byte> Decode(byte[] buffer);
        public Memory<byte> Decode(byte[] buffer, int offset, int length);

        public bool TryEncode(ReadOnlySpan<byte> plaintext, Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = 0;
            return false;
        }
        public bool TryDecode(ReadOnlySpan<byte> encryptedData, Span<byte> destination, out int bytesWritten)
        {
            bytesWritten = 0;
            return false;
        }

        public void Dispose();
    }

    public interface ISymmetricCrypto : ICrypto
    {
        public string Password { get; set; }
    }
    public sealed class AesCrypto : ISymmetricCrypto
    {
        private ICryptoTransform encryptoTransform;
        private ICryptoTransform decryptoTransform;

        public string Password { get; set; }

        public AesCrypto(string password, PaddingMode mode = PaddingMode.ANSIX923)
        {
            Password = password;
            using Aes aes = Aes.Create();
            aes.Padding = mode;
            (aes.Key, aes.IV) = GenerateKeyAndIV(password);

            encryptoTransform = aes.CreateEncryptor(aes.Key, aes.IV);
            decryptoTransform = aes.CreateDecryptor(aes.Key, aes.IV);
        }
        public byte[] Encode(byte[] buffer)
        {
            return Encode(buffer, 0, buffer.Length);
        }
        public byte[] Encode(byte[] buffer, int offset, int length)
        {
            return encryptoTransform.TransformFinalBlock(buffer, offset, length);
        }
        public Memory<byte> Decode(byte[] buffer)
        {
            return Decode(buffer, 0, buffer.Length);
        }
        public Memory<byte> Decode(byte[] buffer, int offset, int length)
        {
            return decryptoTransform.TransformFinalBlock(buffer, offset, length);
        }

        public void Dispose()
        {
            encryptoTransform.Dispose();
            decryptoTransform.Dispose();
        }
        private static (byte[] Key, byte[] IV) GenerateKeyAndIV(string password)
        {
            byte[] key = new byte[16];
            byte[] iv = new byte[16];
            byte[] hash = SHA384.HashData(Encoding.UTF8.GetBytes(password));
            Array.Copy(hash, 0, key, 0, key.Length);
            Array.Copy(hash, key.Length, iv, 0, iv.Length);
            return (Key: key, IV: iv);

        }

    }

    public interface ISymmetricCryptoGcm : ICrypto
    {
    }
    public sealed class AesGcmFast : ISymmetricCryptoGcm
    {
        private readonly byte[] nonce;
        private readonly AesGcm aesGcm;

        public AesGcmFast(string password)
        {
            nonce = new byte[12];
            aesGcm = new AesGcm(GenerateKey(password), 16);
        }


        public byte[] Encode(byte[] buffer)
        {
            return Encode(buffer, 0, buffer.Length);
        }
        public byte[] Encode(byte[] buffer, int offset, int length)
        {
            byte[] encryptedData = new byte[12 + length + 16];
            if (!TryEncode(buffer.AsSpan(offset, length), encryptedData, out int bytesWritten) || bytesWritten != encryptedData.Length)
            {
                throw new InvalidOperationException("Encryption failed.");
            }
            return encryptedData;
        }
        public Memory<byte> Decode(byte[] buffer)
        {
            return Decode(buffer, 0, buffer.Length);
        }
        public Memory<byte> Decode(byte[] buffer, int offset, int length)
        {
            byte[] decryptedData = new byte[length - 12 - 16];
            if (!TryDecode(buffer.AsSpan(offset, length), decryptedData, out int bytesWritten) || bytesWritten != decryptedData.Length)
            {
                throw new InvalidOperationException("Decryption failed.");
            }
            return decryptedData;
        }


        public bool TryEncode(ReadOnlySpan<byte> plaintext, Span<byte> destination, out int bytesWritten)
        {
            int requiredSize = 12 + plaintext.Length + 16;
            if (destination.Length < requiredSize)
            {
                bytesWritten = 0;
                return false;
            }

            Environment.TickCount64.ToBytes(nonce);
            Environment.TickCount.ToBytes(nonce.AsSpan(8));

            Span<byte> nonceDest = destination.Slice(0, 12);
            Span<byte> ciphertextDest = destination.Slice(12, plaintext.Length);
            Span<byte> tagDest = destination.Slice(12 + plaintext.Length, 16);

            nonce.AsSpan().CopyTo(nonceDest);

            aesGcm.Encrypt(nonce, plaintext, ciphertextDest, tagDest);

            bytesWritten = requiredSize;
            return true;
        }
        public bool TryDecode(ReadOnlySpan<byte> encryptedData, Span<byte> destination, out int bytesWritten)
        {
            if (encryptedData.Length < 12 + 16)
            {
                bytesWritten = 0;
                return false;
            }

            ReadOnlySpan<byte> nonce = encryptedData.Slice(0, 12);
            ReadOnlySpan<byte> ciphertext = encryptedData.Slice(12, encryptedData.Length - 12 - 16);
            ReadOnlySpan<byte> tag = encryptedData.Slice(encryptedData.Length - 16, 16);

            if (destination.Length < ciphertext.Length)
            {
                bytesWritten = 0;
                return false;
            }

            Span<byte> plaintextDest = destination.Slice(0, ciphertext.Length);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextDest);

            bytesWritten = ciphertext.Length;
            return true;
        }

        private static byte[] GenerateKey(string password)
        {
            byte[] key = new byte[16];
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            hash.AsSpan(0, 16).CopyTo(key);
            return key;
        }

        public void Dispose()
        {
            aesGcm.Dispose();
        }
    }
}
