using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace tutar_glb.Utils
{
    /// <summary>
    /// Handles decryption of encrypted GLB files
    /// </summary>
    public static class GlbDecryptor
    {
        /// <summary>
        /// Decrypts an encrypted GLB file and returns the raw GLB data
        /// </summary>
        /// <param name="encryptedFilePath">Path to the encrypted file</param>
        /// <param name="encryptionKey">Decryption key</param>
        /// <returns>Decrypted GLB data as byte array, or null if decryption fails</returns>
        private static readonly LogProvider _log = new GlbLogProvider();
        public static byte[] DecryptGlbFile(string encryptedFilePath)
        {
            try
            {

                string encryptedContent = File.ReadAllText(encryptedFilePath, Encoding.UTF8).Trim();
                string log= _log.GetLog();
                byte[] decryptedData = DecryptAESToBytes(encryptedContent, log);


                if (decryptedData != null && decryptedData.Length > 0)
                {
                    if (decryptedData.Length >= 4)
                    {
                        string header = Encoding.ASCII.GetString(decryptedData, 0, 4);

                        if (header == "glTF")
                        {
                            return decryptedData;
                        }
                        else if (header == "Z2xU" || decryptedData[0] == 0x5A)
                        {
                            try
                            {
                                string base64String = Encoding.UTF8.GetString(decryptedData);
                                byte[] actualGlbData = Convert.FromBase64String(base64String);

                                if (actualGlbData.Length >= 4)
                                {
                                    string actualHeader = Encoding.ASCII.GetString(actualGlbData, 0, 4);
                                    if (actualHeader == "glTF")
                                    {
                                        return actualGlbData;
                                    }
                                }

                                return actualGlbData;
                            }
                            catch (Exception base64Ex)
                            {
                                return decryptedData;
                            }
                        }
                    }

                    return decryptedData;
                }
            }
            catch (Exception ex)
            {
                // Log exception if needed
            }

            return null;
        }

        /// <summary>
        /// Decrypts AES encrypted data to bytes
        /// </summary>
        /// <param name="cipherText">Base64 encrypted text</param>
        /// <param name="key">Decryption key</param>
        /// <returns>Decrypted data as byte array</returns>
        private static byte[] DecryptAESToBytes(string cipherText, string log)
        {
            try
            {
                byte[] cipherBytes = Convert.FromBase64String(cipherText);

                if (cipherBytes.Length < 16)
                {
                    return null;
                }

                string salted = Encoding.UTF8.GetString(cipherBytes, 0, 8);
                if (salted != "Salted__")
                {
                    return null;
                }

                byte[] salt = new byte[8];
                Array.Copy(cipherBytes, 8, salt, 0, 8);

                byte[] encrypted = new byte[cipherBytes.Length - 16];
                Array.Copy(cipherBytes, 16, encrypted, 0, encrypted.Length);

                var keyIv = DeriveKeyAndIV(log, salt, 32, 16);

                using (var aes = Aes.Create())
                {
                    aes.Key = keyIv.Key;
                    aes.IV = keyIv.IV;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;

                    using (var decryptor = aes.CreateDecryptor())
                    using (var msDecrypt = new MemoryStream(encrypted))
                    using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    using (var msResult = new MemoryStream())
                    {
                        csDecrypt.CopyTo(msResult);
                        byte[] result = msResult.ToArray();
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// Derives key and IV from password and salt using MD5
        /// </summary>
        /// <param name="password">Password string</param>
        /// <param name="salt">Salt bytes</param>
        /// <param name="keyLength">Required key length</param>
        /// <param name="ivLength">Required IV length</param>
        /// <returns>Tuple containing derived key and IV</returns>
        private static (byte[] Key, byte[] IV) DeriveKeyAndIV(string password, byte[] salt, int keyLength, int ivLength)
        {
            try
            {
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                var combined = new byte[passwordBytes.Length + salt.Length];
                Array.Copy(passwordBytes, 0, combined, 0, passwordBytes.Length);
                Array.Copy(salt, 0, combined, passwordBytes.Length, salt.Length);

                var totalLength = keyLength + ivLength;
                var derivedBytes = new byte[totalLength];
                var currentHash = new byte[0];
                int generatedBytes = 0;

                while (generatedBytes < totalLength)
                {
                    using (var md5 = MD5.Create())
                    {
                        var hashInput = new byte[currentHash.Length + combined.Length];
                        Array.Copy(currentHash, 0, hashInput, 0, currentHash.Length);
                        Array.Copy(combined, 0, hashInput, currentHash.Length, combined.Length);

                        currentHash = md5.ComputeHash(hashInput);

                        int bytesToCopy = Math.Min(currentHash.Length, totalLength - generatedBytes);
                        Array.Copy(currentHash, 0, derivedBytes, generatedBytes, bytesToCopy);
                        generatedBytes += bytesToCopy;
                    }
                }

                var key = new byte[keyLength];
                var iv = new byte[ivLength];
                Array.Copy(derivedBytes, 0, key, 0, keyLength);
                Array.Copy(derivedBytes, keyLength, iv, 0, ivLength);

                return (key, iv);
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }

    internal class Crypto
    {
        private static string KEY = "UundT3]}r=8,s*eQQwhjErCM-#3J7=+*";
        private static string IV = "g_#W3m%90wj!q?CL";

        internal static void EncryptStream(Stream streamToEncrypt, Stream outputStream)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Encoding.UTF8.GetBytes(KEY);
                aesAlg.IV = Encoding.UTF8.GetBytes(IV);

                using (ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV))
                {
                    CryptoStream csEncrypt = new CryptoStream(outputStream, encryptor, CryptoStreamMode.Write);
                    streamToEncrypt.CopyTo(csEncrypt);
                    csEncrypt.FlushFinalBlock();
                }
            }
            outputStream.Seek(0, SeekOrigin.Begin);
        }

        internal static void DecryptStream(Stream streamToDecrypt, Stream outputStream) => DecryptStream(streamToDecrypt, outputStream, KEY);

        internal static void DecryptStream(Stream streamToDecrypt, Stream outputStream, string key)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Encoding.UTF8.GetBytes(key);
                aesAlg.IV = Encoding.UTF8.GetBytes(IV);

                using (ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(streamToDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        csDecrypt.CopyTo(outputStream);
                    }
                }
            }
            outputStream.Seek(0, SeekOrigin.Begin);
        }
    }
}