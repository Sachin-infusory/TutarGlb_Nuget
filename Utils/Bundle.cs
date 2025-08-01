using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Media.Media3D;
using tutar_glb.Models;

namespace tutar_glb.Utils
{
    // Progress event args for reporting download progress
    public class DownloadProgressEventArgs : EventArgs
    {
        public int FilesDownloaded { get; set; }
        public int TotalFiles { get; set; }
        public string CurrentFileName { get; set; }
        public double OverallProgress { get; set; }
        public string Status { get; set; }
        public bool IsDownloading { get; set; }
    }

    public class FilenameExtractionResult
    {
        public List<string> AllFilenames { get; set; } = new List<string>();
        public List<string> UpdateFilenames { get; set; } = new List<string>();
    }

    internal static class Bundle
    {
        // Event for progress reporting
        public static event EventHandler<DownloadProgressEventArgs> DownloadProgress;

        internal static async Task<bool> Download(IProgress<DownloadProgressEventArgs> progress = null, String saveDir = null)
        {
            string jsonFileName = "syllabus.json";

            // Use saveDir if provided, otherwise default to "Models"
            string targetDirectory = !string.IsNullOrEmpty(saveDir) ? saveDir : "Models";

            string jsonFilePath = Path.Combine(targetDirectory, jsonFileName);
            string jsonContent = "";

            // Report initial progress
            ReportProgress(progress, 0, 1, "Checking syllabus file...", "");

            if (File.Exists(jsonFileName))
            {
                jsonContent = File.ReadAllText(jsonFileName);
            }
            else
            {
                try
                {
                    ReportProgress(progress, 0, 1, "Downloading syllabus file...", jsonFileName);

                    string downloadUrl = $"{TutarGlb.s3BucketUrl}/production/{jsonFileName}";
                    HttpResponseMessage response = await TutarGlb.client.GetAsync(downloadUrl);

                    if (response.IsSuccessStatusCode)
                    {
                        byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();
                        File.WriteAllBytes(jsonFileName, fileBytes);
                        jsonContent = Encoding.UTF8.GetString(fileBytes);
                    }
                    else
                    {
                        ReportProgress(progress, 0, 1, "Failed to download syllabus file", jsonFileName);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    ReportProgress(progress, 0, 1, $"Error downloading syllabus: {ex.Message}", jsonFileName);
                    return false;
                }
            }

            if (string.IsNullOrEmpty(jsonContent))
            {
                ReportProgress(progress, 0, 1, "Invalid syllabus content", "");
                return false;
            }

            ReportProgress(progress, 0, 1, "Parsing syllabus...", "");

            var extractionResult = ExtractFilenames(jsonContent);
            List<string> allFilenames = extractionResult.AllFilenames;
            List<string> updateFilenames = extractionResult.UpdateFilenames;

            if (allFilenames.Count == 0)
            {
                ReportProgress(progress, 0, 1, "No files found in syllabus", "");
                return false;
            }

            ReportProgress(progress, 0, 1, "Checking existing files...", "");

            List<string> missingFiles = GetFilesToDownload(allFilenames, targetDirectory);

            List<string> filesToDownload = missingFiles.Union(updateFilenames).ToList();

            if (filesToDownload.Count == 0)
            {
                ReportProgress(progress, allFilenames.Count, allFilenames.Count, "All files already downloaded", "");
                return true;
            }

            ReportProgress(progress, 0, filesToDownload.Count, $"Ready to download {filesToDownload.Count} files", "");

            await DownloadModelFiles(filesToDownload, progress, targetDirectory);

            ReportProgress(progress, filesToDownload.Count, filesToDownload.Count, "Download complete", "");

            return true;
        }

        private static List<string> GetFilesToDownload(List<string> allFilenames, string targetDirectory)
        {
            List<string> filesToDownload = new List<string>();

            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
                return allFilenames;
            }

            var existingFiles = Directory.GetFiles(targetDirectory)
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string filename in allFilenames)
            {
                string fileName = Path.GetFileName(filename);

                if (!existingFiles.Contains(fileName))
                {
                    filesToDownload.Add(filename);
                }
            }

            return filesToDownload;
        }

        private static async Task DownloadModelFiles(List<string> filenames, IProgress<DownloadProgressEventArgs> progress = null, string targetDirectory = "Models")
        {
            Directory.CreateDirectory(targetDirectory);
            int successCount = 0;
            int failCount = 0;

            for (int i = 0; i < filenames.Count; i++)
            {
                string filename = filenames[i];
                string fileName = Path.GetFileName(filename);

                try
                {
                    string localFilePath = Path.Combine(targetDirectory, fileName);
                    HttpResponseMessage response = null;

                    ReportProgress(progress, successCount, filenames.Count,
                        $"Downloading {fileName}...", fileName, true);

                    if (filename.EndsWith("zip"))
                    {
                        string fileNameExtracted = Path.GetFileName(filename);
                        string zipDownloadUrl = $"{TutarGlb.oracleUrl}{fileNameExtracted}";
                        response = await TutarGlb.client.GetAsync(zipDownloadUrl);
                    }
                    else
                    {
                        string downloadUrl = $"{TutarGlb.s3BucketUrl}/{filename}";
                        response = await TutarGlb.client.GetAsync(downloadUrl);
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();
                        File.WriteAllBytes(localFilePath, fileBytes);
                        successCount++;

                        // Report successful download
                        ReportProgress(progress, successCount, filenames.Count,
                            $"Downloaded {fileName}", fileName, false);
                    }
                    else
                    {
                        failCount++;
                        ReportProgress(progress, successCount, filenames.Count,
                            $"Failed to download {fileName}", fileName, false);
                    }
                }
                catch (Exception ex)
                {
                    failCount++;
                    ReportProgress(progress, successCount, filenames.Count,
                        $"Error downloading {fileName}: {ex.Message}", fileName, false);
                }
            }
        }



        private static void ReportProgress(IProgress<DownloadProgressEventArgs> progress, int filesDownloaded, int totalFiles,
            string status, string currentFileName, bool isDownloading = false)
        {
            double overallProgress = totalFiles > 0 ? (double)filesDownloaded / totalFiles * 100 : 0;

            var args = new DownloadProgressEventArgs
            {
                FilesDownloaded = filesDownloaded,
                TotalFiles = totalFiles,
                CurrentFileName = currentFileName,
                OverallProgress = overallProgress,
                Status = status,
                IsDownloading = isDownloading
            };

            progress?.Report(args);
            DownloadProgress?.Invoke(null, args);
        }

        private static FilenameExtractionResult ExtractFilenames(string jsonString)
        {
            var result = new FilenameExtractionResult();
            bool jsonModified = false;

            try
            {
                var jsonArray = JArray.Parse(jsonString);
                for (int i = 1; i < jsonArray.Count; i++)
                {
                    var item = jsonArray[i];

                    var subjects = item["subjects"];
                    if (subjects != null)
                    {
                        foreach (var subject in subjects)
                        {
                            var topics = subject["topics"];
                            if (topics != null)
                            {
                                foreach (var topic in topics)
                                {
                                    var models = topic["models"];
                                    if (models != null)
                                    {
                                        foreach (var model in models)
                                        {
                                            // Extract model filename
                                            var filename = model["filename"]?.ToString();
                                            if (!string.IsNullOrEmpty(filename))
                                            {
                                                // Add to all filenames if not already present
                                                if (!result.AllFilenames.Contains(filename))
                                                {
                                                    result.AllFilenames.Add(filename);
                                                }
                                            }

                                            // Extract thumbnail filename
                                            var thumbnail = model["thumbnail"]?.ToString();
                                            if (!string.IsNullOrEmpty(thumbnail))
                                            {
                                                // Add to all filenames if not already present
                                                if (!result.AllFilenames.Contains(thumbnail))
                                                {
                                                    result.AllFilenames.Add(thumbnail);
                                                }
                                            }

                                            // Check for update tag
                                            var updateTag = model["update"];
                                            if (updateTag != null)
                                            {
                                                // Check if update is true (handle both boolean and string values)
                                                bool hasUpdate = false;

                                                if (updateTag.Type == JTokenType.Boolean)
                                                {
                                                    hasUpdate = updateTag.Value<bool>();
                                                }
                                                else if (updateTag.Type == JTokenType.String)
                                                {
                                                    string updateValue = updateTag.Value<string>();
                                                    hasUpdate = string.Equals(updateValue, "true", StringComparison.OrdinalIgnoreCase);
                                                }

                                                // Add both model and thumbnail to update list if update is true
                                                if (hasUpdate)
                                                {
                                                    if (!string.IsNullOrEmpty(filename) && !result.UpdateFilenames.Contains(filename))
                                                    {
                                                        result.UpdateFilenames.Add(filename);
                                                    }

                                                    if (!string.IsNullOrEmpty(thumbnail) && !result.UpdateFilenames.Contains(thumbnail))
                                                    {
                                                        result.UpdateFilenames.Add(thumbnail);
                                                    }

                                                    // Set update flag to false and mark JSON as modified
                                                    model["update"] = false;
                                                    jsonModified = true;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Save the modified JSON back to file if any updates were found
                if (jsonModified)
                {
                    try
                    {
                        string modifiedJsonString = jsonArray.ToString(Formatting.Indented);
                        File.WriteAllText("syllabus.json", modifiedJsonString);
                    }
                    catch (Exception saveEx)
                    {
                        // Log save error but don't fail the extraction
                        // You might want to add proper logging here
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error or handle as needed
                // Return empty result on error
            }

            return result;
        }

        private static bool AreAllFilesDownloaded(string jsonContent, string saveDir = null)
        {
            try
            {
                // Extract all filenames from the JSON using the correct method
                var extractionResult = ExtractFilenames(jsonContent);
                List<string> requiredFiles = extractionResult.AllFilenames;

                if (requiredFiles.Count == 0)
                {
                    return true;
                }

                string modelsFolder = saveDir != null ? saveDir : "Models";

                // If Models folder doesn't exist, no files are downloaded
                if (!Directory.Exists(modelsFolder))
                {
                    return false;
                }

                // Get list of existing files in Models folder
                var existingFiles = Directory.GetFiles(modelsFolder)
                    .Select(Path.GetFileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Check if each required file exists
                var missingFiles = new List<string>();
                foreach (string filename in requiredFiles)
                {
                    string fileName = Path.GetFileName(filename);
                    if (!existingFiles.Contains(fileName))
                    {
                        missingFiles.Add(fileName);
                    }
                }

                if (missingFiles.Count > 0)
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                // If we can't check properly, assume we need to download
                return false;
            }
        }

        internal static async Task<bool> CheckUpdate(String saveDir = null)
        {
            string jsonFileName = "syllabus.json";

            if (!File.Exists(jsonFileName))
            {
                return true;
            }

            try
            {
                // Read local syllabus file
                string localData = await Task.Run(() => File.ReadAllText(jsonFileName));
                var localJsonContent = JsonConvert.DeserializeObject<JArray>(localData);

                if (localJsonContent == null || localJsonContent.Count == 0 || localJsonContent[0]["version"] == null)
                {
                    return true;
                }

                int prevVersion = localJsonContent[0]["version"].Value<int>();

                // Download the latest syllabus file to compare
                string downloadUrl = $"{TutarGlb.s3BucketUrl}/production/{jsonFileName}";
                HttpResponseMessage response = await TutarGlb.client.GetAsync(downloadUrl);

                if (response.IsSuccessStatusCode)
                {
                    byte[] fileBytes = await response.Content.ReadAsByteArrayAsync();
                    string newContent = Encoding.UTF8.GetString(fileBytes);

                    var newJsonContent = JsonConvert.DeserializeObject<JArray>(newContent);

                    if (newJsonContent == null || newJsonContent.Count == 0 || newJsonContent[0]["version"] == null)
                    {
                        return false;
                    }

                    int newVersion = newJsonContent[0]["version"].Value<int>();

                    // Check version comparison first
                    if (newVersion > prevVersion)
                    {
                        // Update the local syllabus file with the new version
                        string backupFileName = $"{jsonFileName}.backup";
                        if (File.Exists(backupFileName))
                        {
                            File.Delete(backupFileName);
                        }
                        File.Copy(jsonFileName, backupFileName);
                        await Task.Run(() => File.WriteAllBytes(jsonFileName, fileBytes));

                        return true;
                    }
                    else if (newVersion == prevVersion)
                    {
                        // Same version, but check if all files from the JSON exist locally
                        bool allFilesExist = AreAllFilesDownloaded(localData, saveDir);

                        if (!allFilesExist)
                        {
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // Local version is newer, but still check if all files exist
                        bool allFilesExist = AreAllFilesDownloaded(localData);

                        if (!allFilesExist)
                        {
                            return true;
                        }

                        return false;
                    }
                }
                else
                {
                    // Can't check remote version, but check if local files are complete
                    bool allFilesExist = AreAllFilesDownloaded(localData);

                    if (!allFilesExist)
                    {
                        return true;
                    }

                    return false;
                }
            }
            catch (JsonException jsonEx)
            {
                return false;
            }
            catch (HttpRequestException httpEx)
            {
                // Network error, but check if local files are complete
                try
                {
                    string localData = await Task.Run(() => File.ReadAllText(jsonFileName));
                    bool allFilesExist = AreAllFilesDownloaded(localData);

                    if (!allFilesExist)
                    {
                        return true;
                    }

                    return false;
                }
                catch
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }




        internal static async Task<bool> checkExtractionStatus(string sourceFolder = null)
        {
            try
            {
                List<string> zipFiles = Directory.GetFiles(sourceFolder, "*.zip", SearchOption.TopDirectoryOnly).ToList();

                if (zipFiles.Count == 0)
                {
                    return false;
                }
                return true;

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error downloading bundle: {ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
    }
}