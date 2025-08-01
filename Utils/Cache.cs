using System.IO;
using tutar_glb.Utils;

namespace tutar.Utils
{
    internal static class Cache
    {
        internal static void CreateCache(string path, Stream inputStream, string updatedAt)
        {
            string currentDirectory = Directory.GetCurrentDirectory();
            string parentFolder = Directory.GetParent($@"{currentDirectory}\Models\{path}").FullName;
            Directory.CreateDirectory(parentFolder);
            using (FileStream encryptedOutputFileStream = new FileStream($@"{currentDirectory}\Models\{path}", FileMode.Create))
            {
                inputStream.CopyTo(encryptedOutputFileStream);
                inputStream.Seek(0, SeekOrigin.Begin);
            }
            using (StreamWriter streamWriter = File.CreateText($@"{currentDirectory}\Models\{path}_updatedAt"))
            {
                streamWriter.Write(updatedAt);
            }
        }

        internal static void ReadCache(string path, Stream outputStream, string updatedAt, bool decrypt = true)
        {
            string currentDirectory = Directory.GetCurrentDirectory();
            if (!File.Exists($@"{currentDirectory}\Models\{path}"))
            {
                throw new System.Exception($@"{path} missing");
            }
            if (File.Exists($@"{currentDirectory}\Models\{path}_updatedAt"))
            {
                string localUpdatedAt = File.ReadAllText($@"{currentDirectory}\Models\{path}_updatedAt");
                if (localUpdatedAt != updatedAt)
                {
                    throw new System.Exception($@"Outdated cache: {path}");
                }
            }
            else
            {
                throw new System.Exception($@"{path}_updatedAt missing");
            }
            using (FileStream encryptedInputFileStream = new FileStream($@"{currentDirectory}\Models\{path}", FileMode.Open))
            {
                if (decrypt)
                {
                    Crypto.DecryptStream(encryptedInputFileStream, outputStream);
                }
                else
                {
                    encryptedInputFileStream.CopyTo(outputStream);
                    outputStream.Seek(0, SeekOrigin.Begin);
                }
            }
        }
    }
}
