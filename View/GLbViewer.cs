using HelixToolkit.Wpf.SharpDX;
using HelixToolkit.Wpf.SharpDX.Animations;
using HelixToolkit.Wpf.SharpDX.Assimp;
using HelixToolkit.Wpf.SharpDX.Model;
using HelixToolkit.Wpf.SharpDX.Model.Scene;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using tutar_glb.Utils;

namespace tutar_glb.View
{
    internal class GlbModel3DXLoader : Viewport3DX, IDisposable
    {
        private List<IAnimationUpdater> _updaters;
        private readonly Stopwatch _stopwatch;
        private long _initTimestamp;
        private double _endTime;
        private readonly float _playbackSpeed;
        private HelixToolkitScene _currentScene;
        private bool _disposed = false;
        private readonly List<IDisposable> _disposableResources = new List<IDisposable>();
        private readonly List<string> _tempFiles = new List<string>();

        // Store original camera state for reset
        private Point3D _origPosition;
        private Vector3D _origLookDirection;
        private Vector3D _origUpDirection;
        private double _origWidth;

        public GlbModel3DXLoader(float playbackSpeed = 30f)
        {
            _playbackSpeed = playbackSpeed;
            _stopwatch = Stopwatch.StartNew();
            _initTimestamp = 0;
            _endTime = 0;

            this.Camera = new HelixToolkit.Wpf.SharpDX.OrthographicCamera
            {
                Position = new Point3D(0, 10, 30),
                LookDirection = new Vector3D(0, -50, -100),
                UpDirection = new Vector3D(0, 1, 0),
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
            _origLookDirection = this.Camera.LookDirection;
            _origUpDirection = this.Camera.UpDirection;
            if (this.Camera is HelixToolkit.Wpf.SharpDX.OrthographicCamera oc)
            {
                _origWidth = oc.Width;
            }

            // Initialize empty updaters list (no model loaded yet)
            _updaters = new List<IAnimationUpdater>();

            // Start rendering loop
            CompositionTarget.Rendering += OnRendering;
        }

        public GlbModel3DXLoader(string encryptedFilePath, string encryptionKey, float playbackSpeed = 30f)
        {
            if (string.IsNullOrEmpty(encryptionKey))
            {
                throw new ArgumentException("Encryption key is required", nameof(encryptionKey));
            }

            LoadEncryptedGlbModel(encryptedFilePath, encryptionKey);
            this.ZoomExtents();
        }

        internal void updateRendererSize(double width, double height)
        {
            this.Width = width;
            this.Height = height;
            this.InvalidateRender();
        }

        /// <summary>
        /// AGGRESSIVE memory cleanup - forces everything to be released
        /// </summary>
        public void DisposeCurrentModel()
        {
            if (_disposed) return;

            try
            {
                // 1. STOP ALL ANIMATIONS IMMEDIATELY
                CompositionTarget.Rendering -= OnRendering;

                if (_updaters != null)
                {
                    _updaters.Clear();
                    _updaters = null;
                }
                _endTime = 0;
                _initTimestamp = 0;

                // 2. CLEAR VIEWPORT ITEMS AGGRESSIVELY
                if (this.Items != null && this.Items.Count > 0)
                {
                    // Detach and dispose each item
                    var itemsToRemove = this.Items.ToList();
                    foreach (var item in itemsToRemove)
                    {
                        try
                        {
                            if (item is SceneNodeGroupModel3D group && group.SceneNode != null)
                            {
                                group.SceneNode.Detach();
                                ClearSceneNodeAggressively(group.SceneNode);
                            }

                            if (item is IDisposable disposable)
                            {
                                disposable.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                        }
                    }

                    this.Items.Clear();
                }

                if (_currentScene != null)
                {
                    try
                    {
                        if (_currentScene.Root != null)
                        {
                            _currentScene.Root.Detach();
                            ClearSceneNodeAggressively(_currentScene.Root);
                        }

                        // Clear animations - just nullify, don't try to dispose
                        _currentScene.Animations = null;

                        if (_currentScene is IDisposable disposableScene)
                        {
                            disposableScene.Dispose();
                        }

                        _currentScene = null;
                    }
                    catch (Exception ex)
                    {
                    }
                }

                // 4. DISPOSE ALL TRACKED RESOURCES
                foreach (var resource in _disposableResources.ToList())
                {
                    try
                    {
                        resource?.Dispose();
                    }
                    catch (Exception ex)
                    {
                    }
                }
                _disposableResources.Clear();

                // 5. CLEAN ALL TEMP FILES
                foreach (var tempFile in _tempFiles.ToList())
                {
                    CleanupTempFile(tempFile);
                }
                _tempFiles.Clear();

                // 6. FORCE GRAPHICS MEMORY CLEANUP
                this.InvalidateRender();

                // 7. MULTIPLE AGGRESSIVE GARBAGE COLLECTIONS
                for (int i = 0; i < 3; i++)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true);
                    Thread.Sleep(50); // Allow time for cleanup
                }

                // 8. LARGE OBJECT HEAP COMPACTION (if available)
                try
                {
                    Thread.Sleep(100);
                    GC.Collect(2, GCCollectionMode.Forced, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, true);
                }
                catch
                {
                    // Ignore if not available
                }

                // Re-enable rendering for next model
                CompositionTarget.Rendering += OnRendering;
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// Aggressively clear scene node and all its resources
        /// </summary>
        private void ClearSceneNodeAggressively(SceneNode node)
        {
            if (node == null) return;

            try
            {
                // Process all children first
                if (node.Items != null && node.Items.Count > 0)
                {
                    var children = node.Items.ToList();
                    foreach (var child in children)
                    {
                        ClearSceneNodeAggressively(child);
                    }
                }

                // Clear geometry and materials aggressively
                if (node is GeometryNode geoNode)
                {
                    try
                    {
                        // Clear geometry
                        if (geoNode.Geometry != null)
                        {
                            if (geoNode.Geometry is IDisposable disposableGeometry)
                            {
                                disposableGeometry.Dispose();
                            }
                            geoNode.Geometry = null;
                        }

                        // Clear materials
                        if (geoNode is MaterialGeometryNode matNode && matNode.Material != null)
                        {
                            if (matNode.Material is IDisposable disposableMaterial)
                            {
                                disposableMaterial.Dispose();
                            }
                            matNode.Material = null;
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }

                // Dispose the node itself
                if (node is IDisposable disposableNode)
                {
                    disposableNode.Dispose();
                }
            }
            catch (Exception ex)
            {
            }
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
        private void LoadEncryptedGlbModel(string encryptedFilePath, string encryptionKey)
        {
            DisposeCurrentModel();

            try
            {
                byte[] decryptedData = GlbDecryptor.DecryptGlbFile(encryptedFilePath, encryptionKey);

                if (decryptedData != null)
                {
                    // Validate GLB header
                    if (decryptedData.Length >= 4)
                    {
                        string header = Encoding.ASCII.GetString(decryptedData, 0, 4);
                    }

                    // Try memory stream first, then temp file
                    var importedScene = LoadFromMemoryStream(decryptedData) ?? LoadFromTempFile(decryptedData);

                    if (importedScene?.Root != null)
                    {
                        _currentScene = importedScene;
                        ProcessSuccessfulLoad(importedScene);
                    }
                    else
                    {
                        _updaters = new List<IAnimationUpdater>();
                    }
                }
                else
                {
                    _updaters = new List<IAnimationUpdater>();
                }

                // Clear the decrypted data from memory immediately
                decryptedData = null;
                GC.Collect();
            }
            catch (Exception ex)
            {
                _updaters = new List<IAnimationUpdater>();
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
                // Dispose immediately after use
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
                return null;
            }
            finally
            {
                // Cleanup file stream
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

        private void ProcessSuccessfulLoad(HelixToolkitScene importedScene)
        {
            // Attach to effects manager
            importedScene.Root.Attach(this.EffectsManager);
            importedScene.Root.UpdateAllTransformMatrix();

            // Configure materials
            var materialNodes = importedScene.Root.Traverse().OfType<MaterialGeometryNode>().ToList();
            foreach (var node in materialNodes)
            {
                if (node.Material is PBRMaterialCore pbr)
                {
                    pbr.RenderEnvironmentMap = true;
                }
            }

            // Add to viewport
            var currentGroup = new SceneNodeGroupModel3D();
            currentGroup.AddNode(importedScene.Root);
            this.Items.Add(currentGroup);

            // Setup animations
            if (importedScene.Animations != null && importedScene.Animations.Any())
            {
                var dict = importedScene.Animations.CreateAnimationUpdaters();
                _updaters = dict.Values.ToList();
                foreach (var u in _updaters)
                    u.Reset();
                _endTime = importedScene.Animations.Max(a => a.EndTime);
            }
            else
            {
                _updaters = new List<IAnimationUpdater>();
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

        public void ResetToOriginalView()
        {
            if (this.Camera is HelixToolkit.Wpf.SharpDX.PerspectiveCamera pc)
            {
                pc.Position = _origPosition;
                pc.LookDirection = _origLookDirection;
                pc.UpDirection = _origUpDirection;
            }
            else if (this.Camera is HelixToolkit.Wpf.SharpDX.OrthographicCamera oc)
            {
                oc.Position = _origPosition;
                oc.LookDirection = _origLookDirection;
                oc.UpDirection = _origUpDirection;
                oc.Width = _origWidth;
            }
            this.ZoomExtents();
        }

        public new void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                CompositionTarget.Rendering -= OnRendering;
                DisposeCurrentModel();
                _stopwatch?.Stop();

                if (this.EffectsManager is IDisposable disposableEffects)
                {
                    disposableEffects.Dispose();
                }

                base.Dispose();
            }
            catch (Exception ex)
            {
            }
        }


        public void LoadNewModel(string encryptedFilePath, string encryptionKey)
        {
            try
            {
                LoadEncryptedGlbModel(encryptedFilePath, encryptionKey);
                ResetToOriginalView();
            }
            catch (Exception ex)
            {
                throw;
            }
        }
    }
}