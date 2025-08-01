using HelixToolkit.Wpf.SharpDX.Utilities;
using Newtonsoft.Json;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using tutar.Utils;
using tutar_glb.Models;
using tutar_glb.Utils;
using tutar_glb.View;



namespace tutar_glb
{
    /// <summary>
    /// Exception thrown when SDK initialization fails
    /// </summary>
    public static class TutarGlb
    {
        #region Internal Fields
        private static HttpClient? _client;
        private static readonly object _lock = new object();
        internal static string s3BucketUrl = "https://tutar-assets-public.s3.ap-south-1.amazonaws.com";
        internal static string oracleUrl = "https://objectstorage.ap-mumbai-1.oraclecloud.com/n/bm3bhqklizp1/b/tutar-offline-bundles/o/production%2Foffline-bundles%2F";
        private static bool isInitialized = false;
        internal static HttpClient client;
        #endregion

        #region Public Properties

        /// <summary>
        /// Gets whether the SDK has been properly initialized
        /// </summary>
        public static bool IsInitialized => isInitialized;

        /// <summary>
        /// Gets the current API version
        /// </summary>
        public static string Version => "1.3.0";

        /// <summary>
        /// Gets or sets the default download directory for models
        /// </summary>
        public static string DefaultDownloadPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TutarGlb", "Models");

        #endregion

        #region Private Methods

        private static HttpClient GetHttpClient()
        {
            if (_client == null)
            {
                lock (_lock)
                {
                    if (_client == null)
                    {
                        _client = new HttpClient();
                    }
                }
            }
            return _client;
        }

        private static string GetMacAddress()
        {
            return NetworkInterface
                .GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                             nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .FirstOrDefault()?.GetPhysicalAddress().ToString() ?? string.Empty;
        }

        #endregion


        [JsonObject]
        public class InitializationRequest
        {
            [JsonProperty("deviceId", Required = Required.Always)]
            public string DeviceId { get; set; }

            [JsonProperty("apiKey", Required = Required.Always)]
            public string ApiKey { get; set; }
        }

        #region Initialization

        /// <summary>
        /// Initialize the Tutar GLB SDK with API credentials
        /// </summary>
        /// <param name="apiKey">Your Tutar API key</param>
        /// <param name="deviceId">Unique device identifier</param>
        /// <returns>True if initialization successful</returns>
        /// <exception cref="ArgumentNullException">Thrown when apiKey or deviceId is null or empty</exception>
        /// <exception cref="TutarGlbInitializationException">Thrown when initialization fails</exception>
        /// <exception cref="TutarGlbVerificationException">Thrown when API verification fails</exception>
        public static async Task<bool> InitializeAsync(string apiKey, string deviceId)
        {



            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "API key cannot be null or empty");

            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentNullException(nameof(deviceId), "Device ID cannot be null or empty");

            try
            {
                client = GetHttpClient();
                string macAddress = GetMacAddress();

                // Configure HTTP client
                client.BaseAddress = new Uri("https://api.tutarverse.com/");
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.Add("x-tutar-api-key", apiKey);
                client.DefaultRequestHeaders.Add("x-tutar-version", Version);
                client.DefaultRequestHeaders.Add("x-tutar-device-id", deviceId);


                if (!string.IsNullOrEmpty(macAddress))
                {
                    client.DefaultRequestHeaders.Add("x-tutar-mac-address", macAddress);
                }


                var testObj = new InitializationRequest
                {
                    DeviceId = deviceId,
                    ApiKey = apiKey
                };

                string jsonBody = JsonConvert.SerializeObject(testObj);

                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                HttpResponseMessage response = await client.PostAsync("https://tsi.tutarverse.com/tsi/verify", content);

                if (response.IsSuccessStatusCode)
                {
                    string responseContent = await response.Content.ReadAsStringAsync();
                    isInitialized = true;

                    Directory.CreateDirectory(DefaultDownloadPath);

                    return true;
                }
                else
                {
                    isInitialized = false;
                    throw new TutarGlbVerificationException(
                        $"API verification failed with status code: {response.StatusCode}",
                        (int)response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                isInitialized = false;
                throw new TutarGlbInitializationException("Network error during initialization", ex);
            }
            catch (TaskCanceledException ex)
            {
                isInitialized = false;
                throw new TutarGlbInitializationException("Request timeout during initialization", ex);
            }
            catch (JsonException ex)
            {
                isInitialized = false;
                throw new TutarGlbInitializationException("JSON serialization error during initialization", ex);
            }
            catch (Exception ex) when (!(ex is TutarGlbVerificationException))
            {
                isInitialized = false;
                throw new TutarGlbInitializationException("Unexpected error during initialization", ex);
            }
        }


        public static void Dispose()
        {
            lock (_lock)
            {
                _client?.Dispose();
                _client = null;
                isInitialized = false;
            }
        }

        #endregion

        #region Bundle Management

        /// <summary>
        /// Download the complete model bundle
        /// </summary>
        /// <returns>True if download successful</returns>
        /// <exception cref="InvalidOperationException">Thrown when SDK is not initialized</exception>
        public static async Task<bool> DownloadBundleAsync(IProgress<DownloadProgressEventArgs> progress = null, String saveDir = null)
        {
            if (!isInitialized)
                throw new InvalidOperationException("SDK must be initialized before downloading bundles");

            return await Bundle.Download(progress, saveDir);
        }

        /// <summary>
        /// Check if bundle updates are available
        /// </summary>
        /// <returns>True if updates available</returns>
        /// <exception cref="InvalidOperationException">Thrown when SDK is not initialized</exception>
        public static async Task<bool> CheckBundleUpdateAsync(String saveDir = null)
        {
            if (!isInitialized)
                throw new InvalidOperationException("SDK must be initialized before checking for updates");

            return await Bundle.CheckUpdate(saveDir);
        }

        public static async Task<bool> ExtractDownloadedZip(String saveDir = null, IProgress<DownloadProgressEventArgs> progress = null)
        {
            if (!isInitialized)
                throw new InvalidOperationException("SDK must be initialized before checking for updates");

            await ObjExtractor.ExtractAllZipFilesToSingleFolder(saveDir, progress);
            return true;
        }

        public static async Task<bool> checkExractionStatusAsync(String saveDir = null)
        {
            if (!isInitialized)
                throw new InvalidOperationException("SDK must be initialized before checking for updates");

            bool status = await Bundle.checkExtractionStatus(saveDir);
            return status;
        }


        internal static Model3DGroup LoadModal(SingleModelResponse singleModelResponse)
        {
            Stream objStream = null, mtlStream = null;
            bool cacheHit = false;
            SingleModel.Texture mtl = singleModelResponse.data.textures.Find(_texture => _texture.file.metadata.file_type.Equals("model/mtl"));

            try
            {
                objStream = new MemoryStream();
                Cache.ReadCache($@"{singleModelResponse.data.id}\object", objStream, singleModelResponse.data.updatedAt);
                if (mtl != null)
                {
                    mtlStream = new MemoryStream();
                    Cache.ReadCache($@"{singleModelResponse.data.id}\mtl", mtlStream, singleModelResponse.data.updatedAt);
                }
                cacheHit = true;
            }
            catch (Exception)
            {
                cacheHit = false;
            }



            if (!cacheHit)
            {
                WebClient webClient = new WebClient();
                objStream = new MemoryStream(webClient.DownloadData($@"{s3BucketUrl}/{singleModelResponse.data.file.path}"));
                if (mtl != null)
                {
                    mtlStream = new MemoryStream(webClient.DownloadData($@"{s3BucketUrl}/{mtl.file.path}"));
                }

                webClient.Dispose();
                Cache.CreateCache($@"{singleModelResponse.data.id}\object", objStream, singleModelResponse.data.updatedAt);
                MemoryStream tmpObjStream = new MemoryStream();
                Crypto.DecryptStream(objStream, tmpObjStream);
                objStream = tmpObjStream;

                if (mtl != null)
                {
                    Cache.CreateCache($@"{singleModelResponse.data.id}\mtl", mtlStream, singleModelResponse.data.updatedAt);
                    MemoryStream tmpMtlStream = new MemoryStream();
                    Crypto.DecryptStream(mtlStream, tmpMtlStream);
                    mtlStream = tmpMtlStream;
                }
            }

            ObjReader reader = new ObjReader();
            reader.s3_bucket_url = s3BucketUrl;
            reader.model = singleModelResponse.data;
            Model3DGroup model;
            if (mtlStream != null)
            {
                model = reader.Read(objStream, new Stream[] { mtlStream });
            }
            else
            {
                model = reader.Read(objStream);
            }
            objStream.Close();
            objStream.Dispose();
            objStream = null;
            if (mtlStream != null)
            {
                mtlStream.Close();
                mtlStream.Dispose();
                mtlStream = null;
            }
            return model;
        }

        #endregion
    }

    public class ModelContainer : UserControl
    {
        private TextBlock? _displayText;
        private string? _currentModelFile;
        private GlbModel3DXLoader? _currentViewport;

        /// <summary>
        /// Initialize the model container
        /// </summary>
        public ModelContainer()
        {
            this.Width = 400;
            this.Height = 400;

            this.Background = Brushes.Transparent;

        }


        private void Show3DMode()
        {
            try
            {
                if (_currentViewport == null)
                {
                    _currentViewport = new GlbModel3DXLoader();
                }

                this.Content = _currentViewport;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to create 3D viewport", ex);
            }
        }

        /// <summary>
        /// Load a model file and display its filename
        /// </summary>
        /// <param name="filename">Path to the model file</param>
        /// <exception cref="ArgumentException">Thrown when filename is invalid</exception>
        /// <exception cref="FileNotFoundException">Thrown when file does not exist</exception>
        /// <exception cref="NotSupportedException">Thrown when file format is not supported</exception>
        public void LoadModel(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("Filename cannot be null or empty", nameof(filename));

            try
            {
                _currentModelFile = filename;

                if (!File.Exists(filename))
                    throw new FileNotFoundException($"Model file not found: {filename}");

                string extension = Path.GetExtension(filename).ToLower();
                if (extension != ".glb" && extension != ".gltf" && extension != ".obj" && extension != ".fbx")
                    throw new NotSupportedException($"Unsupported file format: {extension}. Supported formats: .glb");


                Show3DMode();

                if (_currentViewport != null)
                {
                    _currentViewport.LoadNewModel(filename, "bf3c199c2470cb477d907b1e0917c17b");
                }
                else
                {
                    throw new InvalidOperationException("3D viewport is not available");
                }
            }
            catch (Exception ex) when (!(ex is ArgumentException || ex is FileNotFoundException || ex is NotSupportedException))
            {
                throw new InvalidOperationException("Failed to load model", ex);
            }
        }

        /// <summary>
        /// Update the container size
        /// </summary>
        /// <param name="width">New width</param>
        /// <param name="height">New height</param>
        /// <exception cref="ArgumentException">Thrown when width or height is invalid</exception>
        public void UpdateSize(double width, double height)
        {
            if (_currentViewport != null)
                if (width <= 0)
                    throw new ArgumentException("Width must be greater than 0", nameof(width));

            if (height <= 0)
                throw new ArgumentException("Height must be greater than 0", nameof(height));

            try
            {
                this.Width = width;
                this.Height = height;
                _currentViewport.updateRendererSize(width, height);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to update container size", ex);
            }
        }

        /// <summary>
        /// Get the currently loaded model filename
        /// </summary>
        /// <returns>Current model filename or null if none loaded</returns>
        public string? GetCurrentModel()
        {
            return _currentModelFile;
        }

        /// <summary>
        /// Clear the currently loaded model
        /// </summary>
        public void ClearModel()
        {
            try
            {
                if (_displayText != null)
                {
                    _displayText.Text = "No model loaded";
                }
                _currentModelFile = null;

                if (_currentViewport != null)
                {
                    _currentViewport.Dispose();
                    _currentViewport = null;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to clear model", ex);
            }
        }
    }
}







