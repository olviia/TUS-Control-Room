using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Debug = UnityEngine.Debug;

using System.Runtime.InteropServices; // Required for DllImport
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System.Linq;
using System.Collections.Concurrent;
using TMPro;

public class DLLTest : MonoBehaviour
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void init(IntPtr log_filename, IntPtr username, IntPtr password, IntPtr ip);

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void update();

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void read_device_params_data();

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void read_volumetric_data();

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool new_data_available();

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool new_calib_available();

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool isInitialized();

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr get_vertices(out int vertexCount);

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr get_faces(out int indexCount);

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr get_normals(out int normalsCount);

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int get_num_frames();
    
    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr get_frame_data(out IntPtr serial, out ulong width, out ulong height, out ulong num_pixels, int idx);

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void destroy();

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr get_audio(out int audioCount);

    [DllImport("VolumetricTransmissionClient0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void get_device_params(
        IntPtr deviceId,
        IntPtr cameraView, IntPtr depthToColor, IntPtr colorIntrinsics,
        IntPtr colorResolution, IntPtr cameraPosition,
        IntPtr radialDist123, IntPtr radialDist456, IntPtr tangentialDist);


    [DllImport("VolumetricTransmissionClient0")]
    private static extern void set_exchange_name_id(int id);

    private struct MeshData
    {
        public float[] v_pos;
        public int[] t_pos_idx;
        public float[] v_nrm;
        public int n_vpos;
        public int n_t_pos_idx;
        public int n_vnrm;
    };

    private struct TextureData
    {
        public byte[] data;
        public int width;
        public int height;
        public int index;
    };

    private struct AudioData
    {
        public float[] data;
        public int count;
    };

    private struct CalibrationData
    {
        public Matrix4x4[] view_mat;
        public Matrix4x4[] depth_to_color;
        public Matrix4x4[] intrin_color;
        public List<Vector4> color_res_list;
        public List<Vector4> cam_pos_list;
        public List<Vector4> dist_radial_123_list;
        public List<Vector4> dist_radial_456_list;
        public List<Vector4> dist_tan_list;
        public string[] dev_ids;
        public int n_devs;
    };

    private Thread data_acquisition_thread;
    private volatile bool is_running = true;

    private ConcurrentQueue<MeshData> mesh_data_queue = new ConcurrentQueue<MeshData>();
    private ConcurrentQueue<TextureData[]> texture_data_queue = new ConcurrentQueue<TextureData[]>();
    private ConcurrentQueue<AudioData> audio_data_queue = new ConcurrentQueue<AudioData>();
    private ConcurrentQueue<CalibrationData> calibration_data_queue = new ConcurrentQueue<CalibrationData>();
    private volatile bool new_calibration_data_available_from_thread = false;

    private bool isInitialized_var = false;

    private Mesh mesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    public Material multiViewMaterial;

    private bool received_new_frame = false;
    private GameObject gameObj;
    private Texture2D[] m_Textures;
    private List<Vector3> m_vertices = new List<Vector3>();
    private List<int> m_faces = new List<int>();
    private List<Vector3> m_normals = new List<Vector3>();
    private List<Vector4> m_participatingCams = new List<Vector4>();

    private Matrix4x4[] CameraViewMatrix;
    private Matrix4x4[] DepthToColorMatrix;
    private Matrix4x4[] ColorIntrinsics;

    private List<Vector4> ColorResolutionList = new List<Vector4>();
    private List<Vector4> CameraPositionsList = new List<Vector4>();
    private List<Vector4> RadialDist123List = new List<Vector4>();
    private List<Vector4> RadialDist456List = new List<Vector4>();
    private List<Vector4> TangentialDistList = new List<Vector4>();

    private int m_numDevs;
    private string[] deviceIds;

    private bool reading_parameters = false;

    public string rabbitmq_username = "volumetric";
    public string rabbitmq_password = "capture";
    public string rabbitmq_ip;
    public int exchange_name_id = 0;

    public AudioClip audioClip;
    private AudioSource audioSource;
    public int sampleRate = 44100;
    private int audioCountOld;
    private int audioCount = 0;
    private int writePosition = 0;
    private float[] ringBuffer;
    private int readHead = 0;
    private object audioLock = new object();
    private int writeHead = 0;
    private int bufferSize = 44100 * 2; // 1 seconds buffer
    private int availableSamples = 0; // Number of valid samples in ring buffer

    private byte[] imageData;
    private bool once_rabbitmq = false;

    void AcquireDataBackground()
    {
        while (is_running)
        {
            if (isInitialized())
            {
                if (new_data_available())
                {
                    int vertex_count, index_count, normal_count;
                    IntPtr v_pos_ptr = get_vertices(out vertex_count);
                    IntPtr t_pos_idx_ptr = get_faces(out index_count);
                    IntPtr v_nrm_ptr = get_normals(out normal_count);
                    IntPtr audio_ptr = get_audio(out int audio_count_from_dll);
                    int frame_count = get_num_frames();

                    if (vertex_count > 0)
                    {
                        float[] v_pos_flat = new float[vertex_count * 3];
                        float[] v_nrm_flat = new float[normal_count * 3];
                        int[] t_pos_idx = new int[index_count];

                        Marshal.Copy(v_pos_ptr, v_pos_flat, 0, v_pos_flat.Length);
                        Marshal.Copy(v_nrm_ptr, v_nrm_flat, 0, v_nrm_flat.Length);
                        Marshal.Copy(t_pos_idx_ptr, t_pos_idx, 0, t_pos_idx.Length);

                        // Reorder indices
                        int step = 3;
                        int iterations = t_pos_idx.Length / step;
                        Parallel.For(0, iterations, (j) =>
                        {
                            int i = j * step;
                            if (i + 2 >= t_pos_idx.Length)
                                return;

                            (t_pos_idx[i], t_pos_idx[i + 2]) = (t_pos_idx[i + 2], t_pos_idx[i]);
                        });

                        mesh_data_queue.Enqueue(new MeshData
                        {
                            v_pos = v_pos_flat,
                            t_pos_idx = t_pos_idx,
                            v_nrm = v_nrm_flat,
                            n_vpos = vertex_count,
                            n_t_pos_idx = index_count,
                            n_vnrm = normal_count
                        });

                        TextureData[] textures = new TextureData[frame_count];
                        string[] device_ids_local = new string[frame_count];

                        for (int i = 0; i < frame_count; i++)
                        {
                            IntPtr serial_ptr;
                            ulong width, height, num_pixels;
                            IntPtr frame_data = get_frame_data(out serial_ptr, out width, out height, out num_pixels, i);

                            if (frame_data != IntPtr.Zero && width > 0 && height > 0)
                            {
                                byte[] image_data = new byte[num_pixels * 4];
                                Marshal.Copy(frame_data, image_data, 0, image_data.Length);
                                textures[i] = new TextureData { data = image_data, width = (int)width, height = (int)height, index = i };
                                device_ids_local[i] = Marshal.PtrToStringAnsi(serial_ptr);
                            }
                            else
                            {
                                Debug.LogError("Invalid frame data from camera " + i + " in background thread");
                            }
                        }
                        texture_data_queue.Enqueue(textures);
                    }

                    if (audio_ptr != IntPtr.Zero && audio_count_from_dll > 0)
                    {
                        float[] audio_data = new float[audio_count_from_dll];
                        Marshal.Copy(audio_ptr, audio_data, 0, audio_count_from_dll);
                        audio_data_queue.Enqueue(new AudioData { data = audio_data, count = audio_count_from_dll });
                    }
                }
                
                if (new_calib_available())
                {
                    int frame_count = get_num_frames();
                    // Initialization (runs only once)
                    if (m_Textures == null || m_Textures.Length != frame_count)
                    {
                        m_Textures = new Texture2D[frame_count];
                        deviceIds = new string[frame_count];

                        for (int i = 0; i < frame_count; i++)
                        {
                            IntPtr serialPtr;
                            ulong width, height, num_pixels;
                            IntPtr frame_data = get_frame_data(out serialPtr, out width, out height, out num_pixels, i);

                            if (frame_data == IntPtr.Zero || width == 0 || height == 0)
                            {
                                Debug.LogError("Invalid frame data for camera " + i);
                                continue;
                            }

                            // Create Texture2D once and assign to material
                            m_Textures[i] = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
                            multiViewMaterial.SetTexture("CameraTextures" + i, m_Textures[i]);

                            // Store device serial
                            deviceIds[i] = Marshal.PtrToStringAnsi(serialPtr);
                        }

                    }

                    int num_devs_local = get_num_frames();
                    Matrix4x4[] view_matrix_local = new Matrix4x4[num_devs_local];
                    Matrix4x4[] depth_to_color_local = new Matrix4x4[num_devs_local];
                    Matrix4x4[] intrin_color_local = new Matrix4x4[num_devs_local];
                    List<Vector4> color_res_list_local = new List<Vector4>();
                    List<Vector4> cam_pos_list_local = new List<Vector4>();
                    List<Vector4> dist_rad_123_local = new List<Vector4>();
                    List<Vector4> dist_rad_456_local = new List<Vector4>();
                    List<Vector4> dist_tan_local = new List<Vector4>();
                    string[] device_ids_local = new string[num_devs_local];

                    for (int i = 0; i < num_devs_local; i++)
                    {
                        string device_id = deviceIds[i];
                        IntPtr device_id_ptr = Marshal.StringToHGlobalAnsi(device_id);
                        IntPtr view_mat_ptr = Marshal.AllocHGlobal(16 * sizeof(float));
                        IntPtr depth_to_color_ptr = Marshal.AllocHGlobal(16 * sizeof(float));
                        IntPtr intrin_color_ptr = Marshal.AllocHGlobal(16 * sizeof(float));
                        IntPtr color_res_ptr = Marshal.AllocHGlobal(2 * sizeof(float));
                        IntPtr cam_pos_ptr = Marshal.AllocHGlobal(3 * sizeof(float));
                        IntPtr dist_rad_123_ptr = Marshal.AllocHGlobal(3 * sizeof(float));
                        IntPtr dist_rad_456_ptr = Marshal.AllocHGlobal(3 * sizeof(float));
                        IntPtr dist_tan_ptr = Marshal.AllocHGlobal(2 * sizeof(float));

                        get_device_params(
                            device_id_ptr,
                            view_mat_ptr,
                            depth_to_color_ptr,
                            intrin_color_ptr,
                            color_res_ptr,
                            cam_pos_ptr,
                            dist_rad_123_ptr,
                            dist_rad_456_ptr,
                            dist_tan_ptr
                        );

                        float[] view_mat = new float[16];
                        float[] depth_to_color = new float[16];
                        float[] intrin_color = new float[16];
                        float[] color_res = new float[2];
                        float[] cam_pos = new float[3];
                        float[] dist_rad_123 = new float[3];
                        float[] dist_rad_456 = new float[3];
                        float[] dist_tan = new float[2];

                        Marshal.Copy(view_mat_ptr, view_mat, 0, 16);
                        Marshal.Copy(depth_to_color_ptr, depth_to_color, 0, 16);
                        Marshal.Copy(intrin_color_ptr, intrin_color, 0, 16);
                        Marshal.Copy(color_res_ptr, color_res, 0, 2);
                        Marshal.Copy(cam_pos_ptr, cam_pos, 0, 3);
                        Marshal.Copy(dist_rad_123_ptr, dist_rad_123, 0, 3);
                        Marshal.Copy(dist_rad_456_ptr, dist_rad_456, 0, 3);
                        Marshal.Copy(dist_tan_ptr, dist_tan, 0, 3);

                        Marshal.FreeHGlobal(device_id_ptr);
                        Marshal.FreeHGlobal(view_mat_ptr);
                        Marshal.FreeHGlobal(depth_to_color_ptr);
                        Marshal.FreeHGlobal(intrin_color_ptr);
                        Marshal.FreeHGlobal(color_res_ptr);
                        Marshal.FreeHGlobal(cam_pos_ptr);
                        Marshal.FreeHGlobal(dist_rad_123_ptr);
                        Marshal.FreeHGlobal(dist_rad_456_ptr);
                        Marshal.FreeHGlobal(dist_tan_ptr);

                        view_matrix_local[i] = Matrix4x4.Inverse(ConvertToMatrix4x4(view_mat));
                        depth_to_color_local[i] = ConvertToMatrix4x4(depth_to_color);
                        intrin_color_local[i] = ConvertToMatrix4x4(intrin_color);
                        color_res_list_local.Add(new Vector4(color_res[0], color_res[1], 0, 0));
                        cam_pos_list_local.Add(new Vector4(cam_pos[0], cam_pos[1], cam_pos[2], 0));
                        dist_rad_123_local.Add(new Vector4(dist_rad_123[0], dist_rad_123[1], dist_rad_123[2], 0));
                        dist_rad_456_local.Add(new Vector4(dist_rad_456[0], dist_rad_456[1], dist_rad_456[2], 0));
                        dist_tan_local.Add(new Vector4(dist_tan[0], dist_tan[1], 0, 0));
                    }

                    calibration_data_queue.Enqueue(new CalibrationData 
                    { 
                        view_mat = view_matrix_local,
                        depth_to_color = depth_to_color_local,
                        intrin_color = intrin_color_local,
                        color_res_list = color_res_list_local,
                        cam_pos_list = cam_pos_list_local,
                        dist_radial_123_list = dist_rad_123_local,
                        dist_radial_456_list = dist_rad_123_local,
                        dist_tan_list = dist_tan_local,
                        dev_ids = device_ids_local,
                        n_devs = num_devs_local
                    });
                    new_calibration_data_available_from_thread = true;
                }
            }
            Thread.Sleep(1); //  avoid waiting
        }
    }





    void Start()
    {
        // Path to the DLL inside the Unity project
        string pathToDLL = System.IO.Path.Combine(Application.dataPath, "Plugins");

        if (!SetDllDirectory(pathToDLL))
        {
            Debug.LogError($"Failed to set DLL directory to {pathToDLL}. Error code: {Marshal.GetLastWin32Error()}");
        }
        else
        {
            Debug.Log($"DLL directory set to {pathToDLL}");

            try
            {
                // Path to the log file inside persistent data path (safe for runtime)
                string pathToLogFile = System.IO.Path.Combine(Application.persistentDataPath, "log.txt");

                IntPtr filenamePtr = Marshal.StringToHGlobalAnsi(pathToLogFile);
                IntPtr usernamePtr = Marshal.StringToHGlobalAnsi(rabbitmq_username);
                IntPtr passwordPtr = Marshal.StringToHGlobalAnsi(rabbitmq_password);
                IntPtr ipPtr = Marshal.StringToHGlobalAnsi(rabbitmq_ip);
                
                set_exchange_name_id(exchange_name_id);
                init(filenamePtr, usernamePtr, passwordPtr, ipPtr);
                
               
                Debug.Log("init() called successfully");

                isInitialized_var = true;
            }
            catch (Exception e)
            {
                Debug.LogError("Error calling init(): " + e.Message);
            }

            if (multiViewMaterial.shader == null)
            {
                Debug.LogError("Shader is not assigned or failed to load.");
            }
            else
            {
                Debug.Log("Shader is correctly loaded.");
            }

            // Assign the Mesh to a GameObject
            mesh = new Mesh();
            meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.transform.localScale = new Vector3(1, -1, 1);
            meshFilter.mesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>();

            // Play it
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioClip = AudioClip.Create("LiveAudio", bufferSize, 1, sampleRate, false);

            audioSource.clip = audioClip;
            audioSource.loop = true;
            audioSource.playOnAwake = true;
            ringBuffer = new float[bufferSize];
            audioSource.Play();

            

            //data_acquisition_thread = new Thread(AcquireDataBackground);
            //data_acquisition_thread.Start();

        }
    }



    Matrix4x4 ConvertToMatrix4x4(float[] values)
    {
        return new Matrix4x4(
            new Vector4(values[0], values[4], values[8], values[12]),   // First row (formerly first column)
            new Vector4(values[1], values[5], values[9], values[13]),   // Second row (formerly second column)
            new Vector4(values[2], values[6], values[10], values[14]),  // Third row (formerly third column)
            new Vector4(values[3], values[7], values[11], values[15])   // Fourth row (formerly fourth column)
        );

    }

    void DestroyTextures()
    {
        if (m_Textures != null)
        {
            foreach (var texture in m_Textures)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }
            m_Textures = null; // Set to null to avoid using an invalid array
        }
    }

    // Called by Unity audio system
    void OnAudioFilterRead(float[] data, int channels)
    {
        lock (audioLock)
        {
            for (int i = 0; i < data.Length; i += channels)
            {
                if (availableSamples > 0)
                {
                    float sample = ringBuffer[readHead];
                    readHead = (readHead + 1) % bufferSize;
                    availableSamples--;

                    for (int c = 0; c < channels; c++)
                    {
                        data[i + c] = sample;
                    }
                }
                else
                {
                    for (int c = 0; c < channels; c++)
                    {
                        data[i + c] = 0f; // Silence
                    }
                }
            }
        }
    }

    void Update()
    {
        //update();
        // Process new mesh data
        //if (mesh_data_queue.TryDequeue(out var mesh_data_new))
        //{
        //    mesh.Clear();
        //    mesh.vertices = MemoryMarshal.Cast<float, Vector3>(mesh_data_new.v_pos).ToArray();
        //    mesh.normals = MemoryMarshal.Cast<float, Vector3>(mesh_data_new.v_nrm).ToArray();
        //    mesh.triangles = mesh_data_new.t_pos_idx;
        //    mesh.RecalculateBounds();
        //    mesh.RecalculateNormals();
        //}

        //// process new texture data
        //if (texture_data_queue.TryDequeue(out var textures_new))
        //{
        //    DestroyTextures();
        //    m_Textures = new Texture2D[textures_new.Length];
        //    for (int i = 0; i < textures_new.Length; i++)
        //    {
        //        if (textures_new[i].data != null)
        //        {
        //            m_Textures[i] = new Texture2D(textures_new[i].width, textures_new[i].height, TextureFormat.RGBA32, false);
        //            m_Textures[i].LoadRawTextureData(textures_new[i].data);
        //            m_Textures[i].Apply(false);
        //            multiViewMaterial.SetTexture("CameraTextures" + textures_new[i].index, m_Textures[i]);
        //        }
        //    }
        //}

        //// process new audio data
        //if (audio_data_queue.TryDequeue(out var audio_data_new))
        //{
        //    if (audio_data_new.data != null && audio_data_new.count > 0)
        //    {
        //        lock (audioLock)
        //        {
        //            int samples_to_add = Mathf.Min(audio_data_new.count, bufferSize - availableSamples);
        //            System.Array.Copy(audio_data_new.data, 0, ringBuffer, writeHead, samples_to_add);
        //            writeHead = (writeHead + samples_to_add) % bufferSize;
        //            availableSamples = Mathf.Min(availableSamples + samples_to_add, bufferSize);
        //        }
        //    }
        //}

        //// Process new calibration data
        //if (new_calibration_data_available_from_thread && calibration_data_queue.TryDequeue(out var calib_data_new))
        //{
        //    if (calib_data_new.view_mat != null)
        //    {
        //        multiViewMaterial.SetInt("CameraNumber", calib_data_new.view_mat.Length);
        //        multiViewMaterial.SetMatrixArray("CameraViewMatrix", calib_data_new.view_mat);
        //        multiViewMaterial.SetMatrixArray("DepthToColorMatrix", calib_data_new.depth_to_color);
        //        multiViewMaterial.SetMatrixArray("ColorIntrinsics", calib_data_new.intrin_color);
        //        multiViewMaterial.SetVectorArray("ColorResolution", calib_data_new.color_res_list);
        //        multiViewMaterial.SetVectorArray("CameraPositions", calib_data_new.cam_pos_list);
        //        multiViewMaterial.SetVectorArray("RadialDist123", calib_data_new.dist_radial_123_list);
        //        multiViewMaterial.SetVectorArray("RadialDist456", calib_data_new.dist_radial_456_list);
        //        multiViewMaterial.SetVectorArray("TangentialDist", calib_data_new.dist_tan_list);
        //        deviceIds = calib_data_new.dev_ids;
        //        m_numDevs = calib_data_new.n_devs;
        //        new_calibration_data_available_from_thread = false;
        //        Debug.Log("Set parameters to Shader");

        //    }
        //}
        //meshRenderer.material = multiViewMaterial;

        Stopwatch sw = Stopwatch.StartNew();
        long lastTime = sw.ElapsedMilliseconds;

        update();

        if (!isInitialized())
            return;

        if (!new_data_available())
            return;

        int vertexCount, indexCount, normalCount;
        IntPtr verticesPtr = get_vertices(out vertexCount);
        IntPtr indicesPtr = get_faces(out indexCount);
        IntPtr normalsPtr = get_normals(out normalCount);
        IntPtr audioPtr = get_audio(out audioCount);
        int framesCount = get_num_frames();
        m_numDevs = framesCount;

        if (audioPtr == IntPtr.Zero || audioCount == 0 /*|| audioCountOld == audioCount*/)
        {
            //if (audioSource.isPlaying)
            //    audioSource.Pause(); // or audioSource.mute = true;
            //Debug.LogWarning("No audio data found");
        }
        else
        {
            //if (!audioSource.isPlaying)
            //    audioSource.UnPause(); // or audioSource.mute = false;
            if (audioCountOld != audioCount)
            {
                float[] audioData = new float[audioCount];
                Marshal.Copy(audioPtr, audioData, 0, audioCount);

                lock (audioLock)
                {
                    for (int i = 0; i < audioData.Length; i++)
                    {
                        ringBuffer[writeHead] = audioData[i];
                        writeHead = (writeHead + 1) % bufferSize;

                        // Update availableSamples safely
                        if (availableSamples < bufferSize)
                            availableSamples++;
                    }
                }
                audioCountOld = audioCount;
            }

        }

        if (vertexCount == 0)
            return;

        //Debug.Log("received: " + vertexCount.ToString());

        float[] verticesFlat = new float[vertexCount * 3];
        float[] normalsFlat = new float[normalCount * 3];

        // Copy data from unmanaged memory
        Marshal.Copy(verticesPtr, verticesFlat, 0, verticesFlat.Length);
        Marshal.Copy(normalsPtr, normalsFlat, 0, normalsFlat.Length);

        // Use MemoryMarshal to reinterpret the float array as Vector3 array
        Vector3[] vertices = MemoryMarshal.Cast<float, Vector3>(verticesFlat).ToArray();
        Vector3[] normals = MemoryMarshal.Cast<float, Vector3>(normalsFlat).ToArray();

        // Efficiently copy index data
        int[] indices = new int[indexCount];
        Marshal.Copy(indicesPtr, indices, 0, indexCount);

        DestroyTextures();

        // Initialization (runs only once)
        if (m_Textures == null || m_Textures.Length != framesCount)
        {
            m_Textures = new Texture2D[framesCount];
            deviceIds = new string[framesCount];

            for (int i = 0; i < framesCount; i++)
            {
                IntPtr serialPtr;
                ulong width, height, num_pixels;
                IntPtr frame_data = get_frame_data(out serialPtr, out width, out height, out num_pixels, i);

                if (frame_data == IntPtr.Zero || width == 0 || height == 0)
                {
                    Debug.LogError("Invalid frame data for camera " + i);
                    continue;
                }

                // Create Texture2D once and assign to material
                m_Textures[i] = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
                multiViewMaterial.SetTexture("CameraTextures" + i, m_Textures[i]);

                // Store device serial
                deviceIds[i] = Marshal.PtrToStringAnsi(serialPtr);
            }

        }

        //lastTime = sw.ElapsedMilliseconds;
        // Frame update (runs every frame)
        //Parallel.For(0, framesCount, i => 
        //{
        //    IntPtr serialPtr;
        //    ulong width, height, num_pixels;
        //    IntPtr frame_data = get_frame_data(out serialPtr, out width, out height, out num_pixels, i);

        //    if (frame_data == IntPtr.Zero || width == 0 || height == 0)
        //    {
        //        Debug.LogError("Invalid frame data for camera " + i);
        //        return;
        //    }

        //    // Resize buffer only if needed
        //    if (imageData == null || imageData.Length != (int)(num_pixels * 4))
        //        imageData = new byte[num_pixels * 4];

        //    Marshal.Copy(frame_data, imageData, 0, imageData.Length);

        //    // Reuse existing texture
        //    m_Textures[i].LoadRawTextureData(imageData);
        //    m_Textures[i].Apply(false); // false = don't generate mipmaps
        //});
        for (int i = 0; i < framesCount; i++)
        {
            IntPtr serialPtr;
            ulong width, height, num_pixels;
            IntPtr frame_data = get_frame_data(out serialPtr, out width, out height, out num_pixels, i);

            if (frame_data == IntPtr.Zero || width == 0 || height == 0)
            {
                Debug.LogError("Invalid frame data for camera " + i);
                continue;
            }

            // Resize buffer only if needed
            if (imageData == null || imageData.Length != (int)(num_pixels * 4))
                imageData = new byte[num_pixels * 4];

            Marshal.Copy(frame_data, imageData, 0, imageData.Length);

            // Reuse existing texture
            m_Textures[i].LoadRawTextureData(imageData);
            m_Textures[i].Apply(false); // false = don't generate mipmaps
        }

        //Debug.Log($"[Timing] Initialized textures for {framesCount} frames in {sw.ElapsedMilliseconds - lastTime} ms");
        //lastTime = sw.ElapsedMilliseconds;
        int step = 3;
        int iterations = indices.Length / step;
        Parallel.For(0, iterations, (j) =>
        {
            int i = j * step;
            if (i + 2 >= indices.Length)
                return;

            (indices[i], indices[i + 2]) = (indices[i + 2], indices[i]);
        });
        //for (int i = 0; i < indices.Length; i += 3)
        //{
        //    // Swap to change order
        //    (indices[i], indices[i + 2]) = (indices[i + 2], indices[i]);
        //}
        //Debug.Log($"[Timing] Reordered triangle indices in {sw.ElapsedMilliseconds - lastTime} ms");


        mesh.Clear();
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = indices;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        if (new_calib_available())
        {
            Debug.Log("Getting device parameters data...");

            CameraViewMatrix = new Matrix4x4[m_numDevs];
            DepthToColorMatrix = new Matrix4x4[m_numDevs];
            ColorIntrinsics = new Matrix4x4[m_numDevs];

            for (int i = 0; i < m_numDevs; i++)
            {
                string deviceId = deviceIds[i];

                // Allocate unmanaged memory for the arrays using IntPtr
                IntPtr cameraViewPtr = Marshal.AllocHGlobal(16 * sizeof(float));
                IntPtr depthToColorPtr = Marshal.AllocHGlobal(16 * sizeof(float));
                IntPtr colorIntrinsicsPtr = Marshal.AllocHGlobal(16 * sizeof(float));
                IntPtr colorResolutionPtr = Marshal.AllocHGlobal(2 * sizeof(float));
                IntPtr cameraPositionPtr = Marshal.AllocHGlobal(3 * sizeof(float));
                IntPtr radialDist123Ptr = Marshal.AllocHGlobal(3 * sizeof(float));
                IntPtr radialDist456Ptr = Marshal.AllocHGlobal(3 * sizeof(float));
                IntPtr tangentialDistPtr = Marshal.AllocHGlobal(2 * sizeof(float));

                // Convert the C# string to IntPtr for the const char* argument
                IntPtr deviceIdPtr = Marshal.StringToHGlobalAnsi(deviceId);

                // Call the C++ function
                get_device_params(deviceIdPtr,
                    cameraViewPtr, depthToColorPtr, colorIntrinsicsPtr,
                    colorResolutionPtr, cameraPositionPtr,
                    radialDist123Ptr, radialDist456Ptr, tangentialDistPtr);

                float[] cameraView = new float[16];
                float[] depthToColor = new float[16];
                float[] colorIntrinsics = new float[16];
                float[] colorResolution = new float[2]; // assuming width and height
                float[] cameraPosition = new float[3];
                float[] radialDist123 = new float[3];
                float[] radialDist456 = new float[3];
                float[] tangentialDist = new float[2];

                Marshal.Copy(cameraViewPtr, cameraView, 0, 16);
                Marshal.Copy(depthToColorPtr, depthToColor, 0, 16);
                Marshal.Copy(colorIntrinsicsPtr, colorIntrinsics, 0, 16);
                Marshal.Copy(colorResolutionPtr, colorResolution, 0, 2);
                Marshal.Copy(cameraPositionPtr, cameraPosition, 0, 3);
                Marshal.Copy(radialDist123Ptr, radialDist123, 0, 3);
                Marshal.Copy(radialDist456Ptr, radialDist456, 0, 3);
                Marshal.Copy(tangentialDistPtr, tangentialDist, 0, 2);

                // Free the unmanaged memory after use
                Marshal.FreeHGlobal(deviceIdPtr);
                Marshal.FreeHGlobal(cameraViewPtr);
                Marshal.FreeHGlobal(depthToColorPtr);
                Marshal.FreeHGlobal(colorIntrinsicsPtr);
                Marshal.FreeHGlobal(colorResolutionPtr);
                Marshal.FreeHGlobal(cameraPositionPtr);
                Marshal.FreeHGlobal(radialDist123Ptr);
                Marshal.FreeHGlobal(radialDist456Ptr);
                Marshal.FreeHGlobal(tangentialDistPtr);

                CameraViewMatrix[i] = Matrix4x4.Inverse(ConvertToMatrix4x4(cameraView));
                DepthToColorMatrix[i] = ConvertToMatrix4x4(depthToColor);
                ColorIntrinsics[i] = ConvertToMatrix4x4(colorIntrinsics);

                ColorResolutionList.Add(new Vector4(colorResolution[0], colorResolution[1], 0, 0));
                CameraPositionsList.Add(new Vector4(cameraPosition[0], cameraPosition[1], cameraPosition[2], 0));
                RadialDist123List.Add(new Vector4(radialDist123[0], radialDist123[1], radialDist123[2], 0));
                RadialDist456List.Add(new Vector4(radialDist456[0], radialDist456[1], radialDist456[2], 0));
                TangentialDistList.Add(new Vector4(tangentialDist[0], tangentialDist[1], 0, 0));

            }

            multiViewMaterial.SetInt("CameraNumber", CameraViewMatrix.Length);

            multiViewMaterial.SetMatrixArray("CameraViewMatrix", CameraViewMatrix);
            multiViewMaterial.SetMatrixArray("DepthToColorMatrix", DepthToColorMatrix);
            multiViewMaterial.SetMatrixArray("ColorIntrinsics", ColorIntrinsics);

            multiViewMaterial.SetVectorArray("ColorResolution", ColorResolutionList);
            multiViewMaterial.SetVectorArray("CameraPositions", CameraPositionsList);
            multiViewMaterial.SetVectorArray("RadialDist123", RadialDist123List);
            multiViewMaterial.SetVectorArray("RadialDist456", RadialDist456List);
            multiViewMaterial.SetVectorArray("TangentialDist", TangentialDistList);

            Debug.Log("Have Set parameters to Shader");
        }
        meshRenderer.material = multiViewMaterial;
        //Graphics.DrawMesh(mesh, transform.position, transform.rotation, multiViewMaterial, 0);
    }


    void OnApplicationQuit()
    {
        is_running = false;
        if (data_acquisition_thread != null && data_acquisition_thread.IsAlive )
        {
            data_acquisition_thread.Join();
        }
        destroy();
    }
}