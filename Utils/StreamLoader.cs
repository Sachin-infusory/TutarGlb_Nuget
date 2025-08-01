using System.IO;
using System.Net;
using System.Windows.Media.Imaging;
using tutar_glb.Utils;

namespace tutar.Utils
{
    internal static class StreamLoader
    {
        internal static BitmapImage LoadTexture(string localPath, string url, string updatedAt, bool decrypt = true)
        {
            using (Stream textureStream = new MemoryStream())
            {
                try
                {
                    Cache.ReadCache(localPath, textureStream, updatedAt, decrypt);
                    return GetBitmapImageFromStream(textureStream);
                }
                catch { }
            }

            using (WebClient webClient = new WebClient())
            {
                using (Stream textureStream = new MemoryStream(webClient.DownloadData(url)))
                {
                    Cache.CreateCache(localPath, textureStream, updatedAt);
                    using (MemoryStream tmpTextureStream = new MemoryStream())
                    {
                        if (decrypt)
                        {
                            Crypto.DecryptStream(textureStream, tmpTextureStream);
                        }
                        return GetBitmapImageFromStream(decrypt ? tmpTextureStream : textureStream);
                    }
                }
            }
        }

        private static BitmapImage GetBitmapImageFromStream(Stream stream)
        {
            BitmapImage texture = new BitmapImage();
            texture.BeginInit();
            texture.StreamSource = stream;
            texture.CacheOption = BitmapCacheOption.OnLoad;
            texture.EndInit();
            texture.Freeze();
            return texture;
        }
    }
}
