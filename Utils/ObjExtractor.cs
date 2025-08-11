using System.IO;
using ICSharpCode.SharpZipLib.Zip;

namespace tutar_glb.Utils
{
    internal class ObjExtractor
    {

        /// <summary>
        /// Extracts all .zip files from a specified folder to an "obj" subfolder
        /// </summary>
        /// <param name="sourceFolder">The folder containing the zip files</param>
        /// <param name="progress">Optional progress callback</param>
        /// <returns>A task representing the async operation</returns>
        public static async Task ExtractAllZipFiles(string sourceFolder, IProgress<DownloadProgressEventArgs> progress = null)
        {
            if (!Directory.Exists(sourceFolder))
            {
                throw new DirectoryNotFoundException($"Source folder not found: {sourceFolder}");
            }

            // Create the "obj" folder inside the source folder
            string extractionFolder = Path.Combine(sourceFolder, "obj");
            Directory.CreateDirectory(extractionFolder);

            // Get all .zip files from the source folder
            List<string> zipFiles = Directory.GetFiles(sourceFolder, "*.zip", SearchOption.TopDirectoryOnly).ToList();

            if (zipFiles.Count == 0)
            {
                return;
            }


            int processedCount = 0;
            int successCount = 0;
            int failCount = 0;

            foreach (string zipFilePath in zipFiles)
            {
                string zipFileName = Path.GetFileNameWithoutExtension(zipFilePath);

                try
                {

                    // Create a subfolder in "obj" for each zip file (optional - remove if you want all files in one folder)
                    string zipExtractionPath = Path.Combine(extractionFolder, zipFileName);
                    Directory.CreateDirectory(zipExtractionPath);

                    await Task.Run(() => ExtractZipFile(zipFilePath, zipExtractionPath));

                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                }

                processedCount++;
            }

        }

        /// <summary>
        /// Extracts a single zip file using SharpZipLib
        /// </summary>
        /// <param name="zipFilePath">Path to the zip file</param>
        /// <param name="extractPath">Path where files should be extracted</param>
        private static void ExtractZipFile(string zipFilePath, string extractPath)
        {
            using (var fileStream = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read))
            using (var zipStream = new ZipInputStream(fileStream))
            {
                ZipEntry entry;
                while ((entry = zipStream.GetNextEntry()) != null)
                {
                    if (!entry.IsFile) continue; // Skip directories

                    // Get the full path for the file
                    string entryFileName = entry.Name;
                    string fullPath = Path.Combine(extractPath, entryFileName);

                    // Create directory if it doesn't exist
                    string directoryName = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(directoryName) && !Directory.Exists(directoryName))
                    {
                        Directory.CreateDirectory(directoryName);
                    }

                    // Extract the file
                    using (var outputStream = File.Create(fullPath))
                    {
                        zipStream.CopyTo(outputStream);
                    }

                    // Preserve file date/time if needed
                    File.SetLastWriteTime(fullPath, entry.DateTime);
                }
            }
        }

        // Alternative version that extracts all files directly to "obj" folder without subfolders
        /// <summary>
        /// Extracts all .zip files from a specified folder to an "obj" subfolder (all files in one folder)
        /// </summary>
        /// <param name="sourceFolder">The folder containing the zip files</param>
        /// <param name="progress">Optional progress callback</param>
        /// <returns>A task representing the async operation</returns>
        public static async Task ExtractAllZipFilesToSingleFolder(string sourceFolder, IProgress<DownloadProgressEventArgs> progress = null)
        {

            if (sourceFolder ==null || sourceFolder == "")
            {
                sourceFolder = "Models";
            }
            if (!Directory.Exists(sourceFolder))
            {
                throw new DirectoryNotFoundException($"Source folder not found: {sourceFolder}");
            }

            List<string> zipFiles = Directory.GetFiles(sourceFolder, "*.zip", SearchOption.TopDirectoryOnly).ToList();

            if (zipFiles.Count == 0)
            {
                return;
            }


            int processedCount = 0;
            int successCount = 0;
            int failCount = 0;

            foreach (string zipFilePath in zipFiles)
            {
                try
                {

                    await Task.Run(() => ExtractZipFile(zipFilePath, sourceFolder));
                    File.Delete(zipFilePath);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                }

                processedCount++;
            }

        }
    }
}
