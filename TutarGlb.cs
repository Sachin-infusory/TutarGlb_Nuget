using Newtonsoft.Json;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Media.Media3D;
using tutar.Utils;
using tutar_glb.Models;
using tutar_glb.Utils;
using tutar_glb.View;
using DownloadProgressEventArgs = tutar_glb.Utils.DownloadProgressEventArgs;



namespace tutar_glb
{
    public static class TutarGlb
    {
        #region Internal Fields
        private static HttpClient _client;
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

                var verificationTask = MakeVerificationCallAsync(apiKey, deviceId);
                var secondApiTask = MakeSecondVerificationAsync(apiKey, deviceId); 

                var results = await Task.WhenAll(verificationTask, secondApiTask);

                bool verificationSuccess = results[0];
                bool secondApiSuccess = results[1];

                if (verificationSuccess && secondApiSuccess)
                {
                    isInitialized = true;
                    Directory.CreateDirectory(DefaultDownloadPath);
                    return true;
                }
                else
                {
                    isInitialized = false;
                    throw new TutarGlbVerificationException(
                        $"Verification Failed: check if the api key is correct",
                        400);
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

        private static async Task<bool> MakeVerificationCallAsync(string apiKey, string deviceId)
        {
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
                return true;
            }
            else
            {
                throw new TutarGlbVerificationException(
                    $"API verification failed with status code: {response.StatusCode}",
                    (int)response.StatusCode);
            }
        }

        private static async Task<bool> MakeSecondVerificationAsync(string apiKey, string deviceId)
        {
            try
            {
            HttpResponseMessage response = await client.GetAsync("https://api.tutarverse.com/offline-bundles/_/validate");
                string responseContent = await response.Content.ReadAsStringAsync();
                return true;
            }
            catch (Exception ex)
            {
                throw new TutarGlbVerificationException(
                       $"Verification fialed: Error making the api request",500);
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

        public static async Task<bool> ExtractDownloadedObjZip(String saveDir = null, IProgress<DownloadProgressEventArgs> progress = null)
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

        public static GlbModel3DXLoader createGlb3DXLoader()
        {
            if (!isInitialized)
                throw new InvalidOperationException("SDK must be initialized before checking for updates");

            return new GlbModel3DXLoader();
        }


        public static async Task<Model3DGroup> GetModelById(string id, bool fromBundle = false)
        {
            if (!isInitialized)
                throw new InvalidOperationException("SDK must be initialized to Get the model");

            return fromBundle ? ObjBundle.FetchModel(id) : await Online.FetchModel(id);
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

}







