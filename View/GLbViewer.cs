using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Wpf.SharpDX.Animations;
using HelixToolkit.Wpf.SharpDX.Assimp;
using HelixToolkit.Wpf.SharpDX.Model;
using HelixToolkit.Wpf.SharpDX.Model.Scene;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using tutar_glb.Utils;

namespace tutar_glb.View
{
    public class GlbModel3DXLoader : Viewport3DX, IDisposable
    {
        private List<IAnimationUpdater> _updaters;
        private readonly Stopwatch _stopwatch;
        private long _initTimestamp;
        private double _endTime;
        private readonly float _playbackSpeed;
        private bool _disposed = false;
        private readonly List<IDisposable> _disposableResources = new List<IDisposable>();
        private readonly List<string> _tempFiles = new List<string>();
        

        // Store original camera state for reset
        private Point3D _origPosition;
        private double _origWidth;

        public GlbModel3DXLoader(float playbackSpeed = 30f)
        {
            if (!TutarGlb.IsInitialized)
            {
                throw new InvalidOperationException("SDK must be initialized before checking for updates");
            }

            _playbackSpeed = playbackSpeed;
            _stopwatch = Stopwatch.StartNew();
            _initTimestamp = 0;
            _endTime = 0;

            this.Camera = new HelixToolkit.Wpf.SharpDX.OrthographicCamera
            {
                Position = new Point3D(0, 10, 30),
                Width = 5,
                NearPlaneDistance = 0.1,
                FarPlaneDistance = 5000,
            };

            // Interaction & framing flags
            this.ZoomExtentsWhenLoaded = true;
            this.ShowViewCube = true;
            this.IsZoomEnabled = true;
            this.ShowCameraTarget = true;
            this.IsPanEnabled = true;
            this.IsRotationEnabled = true;
            this.RotationSensitivity = 1;
            this.MinimumFieldOfView = 45;
            this.MaximumFieldOfView = 100;
            this.ShowCoordinateSystem = true;
            this.Height = 400; 
            this.Width = 400;

            // Transparent background
            this.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            this.BackgroundColor = Color.FromArgb(0, 0, 0, 0);

            // Effects manager & clear existing items
            this.EffectsManager = new DefaultEffectsManager();
            this.Items.Clear();

            // Remember original camera state
            _origPosition = this.Camera.Position;
            if (this.Camera is HelixToolkit.Wpf.SharpDX.OrthographicCamera oc)
            {
                _origWidth = oc.Width;
            }

            // Initialize empty updaters list (no model loaded yet)
            _updaters = new List<IAnimationUpdater>();

            // Start rendering loop
            CompositionTarget.Rendering += OnRendering;
        }



        /// <summary>
        /// Track disposable resources for cleanup
        /// </summary>
        private void TrackDisposableResource(IDisposable resource)
        {
            if (resource != null && !_disposableResources.Contains(resource))
            {
                _disposableResources.Add(resource);
            }
        }

        /// <summary>
        /// Track temp files for cleanup
        /// </summary>
        private void TrackTempFile(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && !_tempFiles.Contains(filePath)) 
            {
                _tempFiles.Add(filePath);
            }
        }

        /// <summary>
        /// Loads the encrypted GLB model directly from memory stream
        /// </summary>
        private void LoadEncryptedGlbModel(string encryptedFilePath)
        {
            try
            {
                byte[] decryptedData = GlbDecryptor.DecryptGlbFile(encryptedFilePath);

                if (decryptedData != null)
                {
                    var scene = LoadFromMemoryStream(decryptedData) ?? LoadFromTempFile(decryptedData);

                    if (scene != null)
                    {
                        scene.Root.Attach(this.EffectsManager);
                        scene.Root.UpdateAllTransformMatrix();

                        foreach (var node in scene.Root.Traverse().OfType<MaterialGeometryNode>())
                            if (node.Material is PBRMaterialCore pbr)
                                pbr.RenderEnvironmentMap = true;

                        var group = new SceneNodeGroupModel3D();
                        group.AddNode(scene.Root);
                        this.Items.Add(group);

                        // FIX: Properly set up animations
                        if (scene.Animations != null && scene.Animations.Any())
                        {
                            var dict = scene.Animations.CreateAnimationUpdaters();
                            _updaters = dict.Values.ToList(); 

                            foreach (var u in _updaters)
                                u.Reset();

                            _endTime = scene.Animations.Max(a => a.EndTime);
                            _initTimestamp = 0;
                        }
                        else
                        {
                            _updaters = new List<IAnimationUpdater>();
                            _endTime = 0;
                        }
                    }
                    else
                    {
                        _updaters = new List<IAnimationUpdater>();
                        _endTime = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                _updaters = new List<IAnimationUpdater>();
                _endTime = 0;
            }
        }

        private HelixToolkitScene LoadFromMemoryStream(byte[] glbData)
        {

            MemoryStream memoryStream = null;
            try
            {
                var importer = new Importer
                {
                    Configuration =
                    {
                        CreateSkeletonForBoneSkinningMesh = false,
                        SkeletonSizeScale = 0.1f,
                        GlobalScale = 1f,
                        FlipWindingOrder = true,
                        ImportMaterialType = MaterialType.Diffuse
                    }
                };

                memoryStream = new MemoryStream(glbData);
                TrackDisposableResource(memoryStream);

                // Try to load from stream (if supported)
                var scene = TryLoadFromStream(importer, memoryStream);

                if (scene != null)
                {

                }
                return scene;
            }
            catch (Exception ex)
            {
                
                return null;
            }
            finally
            {
                if (memoryStream != null)
                {
                    try
                    {
                        memoryStream.Dispose();
                        _disposableResources.Remove(memoryStream);
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
        }

        private HelixToolkitScene TryLoadFromStream(Importer importer, MemoryStream stream)
        {
            try
            {
                var loadMethods = typeof(Importer).GetMethods()
                    .Where(m => m.Name == "Load" && m.GetParameters().Length > 0)
                    .ToList();

                foreach (var method in loadMethods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Any(p => p.ParameterType == typeof(Stream) || p.ParameterType == typeof(MemoryStream)))
                    {
                        try
                        {
                            stream.Position = 0;
                            var result = method.Invoke(importer, new object[] { stream });
                            if (result is HelixToolkitScene scene)
                            {
                                return scene;
                            }
                        }
                        catch (Exception reflEx)
                        {
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// Fallback method using temp file with better error handling
        /// </summary>
        private HelixToolkitScene LoadFromTempFile(byte[] glbData)
        {
            string tempFilePath = null;
            FileStream fileStream = null;
            try
            {
                // Create unique temp file
                string tempDir = Path.GetTempPath();
                string tempFileName = $"helix_glb_{Guid.NewGuid():N}_{DateTime.Now.Ticks}.glb";
                tempFilePath = Path.Combine(tempDir, tempFileName);
                TrackTempFile(tempFilePath);

                // Write with proper disposal
                fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                TrackDisposableResource(fileStream);

                fileStream.Write(glbData, 0, glbData.Length);
                fileStream.Flush();
                fileStream.Close();
                fileStream.Dispose();
                _disposableResources.Remove(fileStream);
                fileStream = null;

                // Verify file
                var fileInfo = new FileInfo(tempFilePath);
                if (!fileInfo.Exists || fileInfo.Length != glbData.Length)
                {
                    return null;
                }

                Thread.Sleep(100); // Allow file system to settle

                // Load with importer
                var importer = new Importer
                {
                    Configuration =
                    {
                        CreateSkeletonForBoneSkinningMesh = false,
                        SkeletonSizeScale = 0.1f,
                        GlobalScale = 1f,
                        FlipWindingOrder = true,
                        ImportMaterialType = MaterialType.Diffuse
                    }
                };

                var scene = importer.Load(tempFilePath);

                if (scene != null)
                {
                }

                return scene;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.InnerException.Message);
                return null;
            }
            finally
            {
                //Cleanup file stream
                if (fileStream != null)
                {
                    try
                    {
                        fileStream.Close();
                        fileStream.Dispose();
                        _disposableResources.Remove(fileStream);
                    }
                    catch (Exception streamEx)
                    {
                    }
                }

                // Immediate temp file cleanup
                if (!string.IsNullOrEmpty(tempFilePath))
                {
                    Task.Run(() => CleanupTempFile(tempFilePath));
                }
            }
        }


        private void CleanupTempFile(string tempFilePath)
        {
            const int maxAttempts = 5;
            const int delayMs = 200;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.SetAttributes(tempFilePath, FileAttributes.Normal);
                        File.Delete(tempFilePath);
                        lock (_tempFiles)
                        {
                            _tempFiles.Remove(tempFilePath);
                        }
                        return;
                    }
                    else
                    {
                        lock (_tempFiles)
                        {
                            _tempFiles.Remove(tempFilePath);
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    if (attempt < maxAttempts)
                    {
                        Thread.Sleep(delayMs);
                    }
                }
            }
        }

        private void OnRendering(object sender, EventArgs e)
        {
            if (_disposed || _updaters == null || _updaters.Count == 0 || _endTime <= 0) return;

            var now = Stopwatch.GetTimestamp();
            if (_initTimestamp == 0) _initTimestamp = now;
            double elapsed = (now - _initTimestamp) / (double)Stopwatch.Frequency;
            float playTime = (float)((elapsed * _playbackSpeed) % _endTime);

            foreach (var upd in _updaters)
            {
                upd.Update(playTime, 1);
            }

            this.InvalidateRender();
        }

        public void ResetToOriginalView(double animationTime = 0)
        {
            if (this.Camera is HelixToolkit.Wpf.SharpDX.PerspectiveCamera pc)
            {
                pc.Position = _origPosition;
            }
            else if (this.Camera is HelixToolkit.Wpf.SharpDX.OrthographicCamera oc)
            {
                oc.Position = _origPosition;
                oc.Width = _origWidth;
            }
            // Re-frame everything
            this.ZoomExtents();
        }

        public void Dispose()
        {
            CompositionTarget.Rendering -= OnRendering;
        }


        public void LoadNewModel(string encryptedFilePath)
        {
            try
            {
                LoadEncryptedGlbModel(encryptedFilePath);
                ResetToOriginalView();
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}