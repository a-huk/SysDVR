using System;
using System.Collections.Generic;
using System.Threading;
using System.Numerics;
using ImGuiNET;
using SysDVR.Client.Core;
using SysDVR.Client.Targets.Player;
using SysDVR.Client.Platform;
using SysDVR.Client.Targets;

using static SDL2.SDL;
using SysDVR.Client.Targets.FileOutput;
using SysDVR.Client.GUI.Components;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.IO;
using System.ComponentModel;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;  // Add this line
using System.Runtime.InteropServices;
using OpenTK.Graphics;
using SDL2;
using FFmpeg.AutoGen;

namespace SysDVR.Client.GUI
{
    class PendingUiNotif : IDisposable
    {
        public string Text;
        public bool ShouldRemove;

        Timer disposeTimer;

        public PendingUiNotif(string text)
        {
            Text = text;
            ShouldRemove = false;
            disposeTimer = new Timer((_) => ShouldRemove = true, null, 5000, Timeout.Infinite);
        }

        public void Dispose()
        {
            disposeTimer.Dispose();
        }
    }

    internal class PlayerCore
    {

        private float totalTime = 0f;
        private System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();


        internal readonly AudioPlayer? Audio;
        internal readonly VideoPlayer? Video;
        internal readonly PlayerManager Manager;

        readonly FramerateCounter fps = new();

        SDL_Rect DisplayRect = new SDL_Rect();
        
        private int shaderProgram;
        private int framebuffer;
        private int textureColorbuffer;

        

        const uint FOURCC_IYUV = 0x56555949; // 'IYUV' format


        public PlayerCore(PlayerManager manager)
        {
            Manager = manager;
            

            Console.WriteLine("Initializing PlayerCore");

            // Initialize SDL with OpenGL support
            if (SDL_Init(SDL_INIT_VIDEO) < 0)
            {
                throw new Exception($"Failed to initialize SDL: {SDL_GetError()}");
            }
            Console.WriteLine("SDL initialized");

            // Set OpenGL attributes
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
            SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL_GLprofile.SDL_GL_CONTEXT_PROFILE_CORE);
            Console.WriteLine("OpenGL attributes set");

            // Create an SDL window with OpenGL context
            // IntPtr window = SDL_CreateWindow("OpenGL Window", SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED, 800, 600, SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL_WindowFlags.SDL_WINDOW_SHOWN);
            // if (window == IntPtr.Zero)
            // {
            //     throw new Exception($"Failed to create SDL window: {SDL_GetError()}");
            // }
            // Console.WriteLine("SDL window created");
            IntPtr window = Program.SdlCtx.WindowHandle;


            // Create an OpenGL context
            IntPtr glContext = SDL_GL_CreateContext(window);
            if (glContext == IntPtr.Zero)
            {
                throw new Exception($"Failed to create OpenGL context: {SDL_GetError()}");
            }
            Console.WriteLine("OpenGL context created");
            SDL_GL_SetSwapInterval(1);  // Enable V-Sync (1)

            // Load OpenGL bindings using the custom bindings context
            GL.LoadBindings(new MySDLBindingsContext());
            Console.WriteLine("OpenGL bindings loaded");
            
            // SyncHelper is disabled if there is only a single stream
            // Note that it can also be disabled via a --debug flag and this is handled by the constructor
            var sync = new StreamSynchronizationHelper(manager.HasAudio && manager.HasVideo);

            if (manager.HasVideo)
            {
                Video = new(Program.Options.DecoderName, Program.Options.HardwareAccel);
                Video.Decoder.SyncHelper = sync;
                manager.VideoTarget.UseContext(Video.Decoder);
                InitializeVideoTexture();
                InitializeLoadingTexture();
                InitializeShader();
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                SDL_GL_SetAttribute(SDL_GLattr.SDL_GL_ALPHA_SIZE, 8);
                InitializeQuad();
                InitializeFramebuffer();

                fps.Start();
            }

            if (manager.HasAudio)
            {
                Audio = new(manager.AudioTarget);
				manager.AudioTarget.Volume = Program.Options.DefaultVolume / 100f;
			}

            manager.UseSyncManager(sync);
        }
        private int videoTextureId;
private void InitializeVideoTexture()
{
    GL.GenTextures(1, out videoTextureId);
    GL.BindTexture(TextureTarget.Texture2D, videoTextureId);

    // Set texture parameters
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

    // Important: Set the texture wrapping mode to clamp to edge
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

    // Allocate empty texture data (optional)
    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, Video.FrameWidth, Video.FrameHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
}
        private void InitializeShader()
    {
        string vertexShaderSource = @"
        #version 330 core
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec2 aTexCoord;

out vec2 TexCoord;

void main()
{
    gl_Position = vec4(aPos, 0.0, 1.0);
    TexCoord = aTexCoord;
}

    ";  
        string fragmentShaderSource = File.ReadAllText("/home/hukad/test.glsl");
        
        int vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, vertexShaderSource);
        GL.CompileShader(vertexShader);
        CheckShaderCompileStatus(vertexShader);

        int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentShader, fragmentShaderSource);
        GL.CompileShader(fragmentShader);
        CheckShaderCompileStatus(fragmentShader);

        shaderProgram = GL.CreateProgram();
        GL.AttachShader(shaderProgram, vertexShader);
        GL.AttachShader(shaderProgram, fragmentShader);
        GL.LinkProgram(shaderProgram);
        CheckProgramLinkStatus(shaderProgram);

        // Get uniform locations
        int videoTextureUniform = GL.GetUniformLocation(shaderProgram, "videoTexture");
        int projectionLocation = GL.GetUniformLocation(shaderProgram, "projection");
        int timeLocation = GL.GetUniformLocation(shaderProgram, "iTime");
        
        GL.UseProgram(shaderProgram);

        // Set uniforms
        GL.Uniform1(videoTextureUniform, 0); // Texture unit 0
        GL.Uniform1(timeLocation, 0.0f);

        // Initialize the projection matrix to identity (no scaling)
        Matrix4 projection = Matrix4.Identity;
        GL.UniformMatrix4(projectionLocation, false, ref projection);

        // Delete shaders after linking
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);
    }

    private void CheckShaderCompileStatus(int shader)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status == (int)All.False)
        {
            string infoLog = GL.GetShaderInfoLog(shader);
            Console.WriteLine(infoLog);
            throw new Exception($"Shader compilation failed: {infoLog}");
        }
    }

    private void CheckProgramLinkStatus(int program)
    {
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == (int)All.False)
        {
            string infoLog = GL.GetProgramInfoLog(program);
            throw new Exception($"Program linking failed: {infoLog}");
        }
    }
    
    private void InitializeFramebuffer()
{
   GL.GenFramebuffers(1, out framebuffer);
   GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

   GL.GenTextures(1, out textureColorbuffer);
   GL.ActiveTexture(TextureUnit.Texture0);
   GL.BindTexture(TextureTarget.Texture2D, textureColorbuffer);
   GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, 1280, 720, 0, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
   GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
   GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

   GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, textureColorbuffer, 0);

   if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
   {
       throw new Exception("Framebuffer is not complete!");
   }

   GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
}

    //define frame counter
    private int frameCounter = 0;

public void RenderFrame()
{
    // Update viewport to match the window size
    int windowWidth, windowHeight;
    SDL_GetWindowSize(Program.SdlCtx.WindowHandle, out windowWidth, out windowHeight);
    GL.Viewport(0, 0, windowWidth, windowHeight);

    // Clear and bind framebuffer
    GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    GL.Clear(ClearBufferMask.ColorBufferBit);

    float aspectRatio = (float)Video.FrameWidth / Video.FrameHeight;
float windowAspectRatio = (float)windowWidth / windowHeight;


float scaleX, scaleY;

if (windowAspectRatio > aspectRatio)
{
    // Window is wider than the video; scale X accordingly
    scaleX = aspectRatio / windowAspectRatio;
    scaleY = 1.0f;
}
else
{
    // Window is taller than the video; scale Y accordingly
    scaleX = 1.0f;
    scaleY = windowAspectRatio / aspectRatio;
}

// Create the projection matrix with the new scaling factors
Matrix4 projection = Matrix4.CreateScale(scaleX, scaleY, 1.0f);

// Use the shader program and set the projection matrix uniform
GL.UseProgram(shaderProgram);
int projectionLocation = GL.GetUniformLocation(shaderProgram, "projection");
GL.UniformMatrix4(projectionLocation, false, ref projection);

    // Update texture with video frame
    UpdateOpenGLTextureWithVideoFrame();

    // Bind the video texture
    GL.ActiveTexture(TextureUnit.Texture0);
    GL.BindTexture(TextureTarget.Texture2D, videoTextureId);

    // Draw quad with shader
    RenderQuad();

    // Swap buffers
    SDL_GL_SwapWindow(Program.SdlCtx.WindowHandle);
}



private unsafe void ConvertIYUVToRGB(IntPtr pixelBuffer, int width, int height)
{
    byte* yPlane = (byte*)pixelBuffer;
    byte* uPlane = yPlane + (width * height);
    byte* vPlane = uPlane + (width * height) / 4;

    // Create a buffer for the RGB data
    IntPtr rgbBuffer = Marshal.AllocHGlobal(width * height * 4); // RGBA

            try
            {
                byte* rgb = (byte*)rgbBuffer;

                for (int y = 0; y < height; y++)
                {
                    int yOffset = y * width;
                    int uvOffset = (y / 2) * (width / 2);

                    for (int x = 0; x < width; x++)
                    {
                        int yIndex = yOffset + x;
                        int uvIndex = uvOffset + (x / 2);

                        byte Y = yPlane[yIndex];
                        byte U = uPlane[uvIndex];
                        byte V = vPlane[uvIndex];

                        // Convert YUV to RGB
                        int C = Y - 16;
                        int D = U - 128;
                        int E = V - 128;

                        int R = (298 * C + 409 * E + 128) >> 8;
                        int G = (298 * C - 100 * D - 208 * E + 128) >> 8;
                        int B = (298 * C + 516 * D + 128) >> 8;

                        R = Math.Clamp(R, 0, 255);
                        G = Math.Clamp(G, 0, 255);
                        B = Math.Clamp(B, 0, 255);

                        int rgbIndex = (yIndex) * 4;
                        rgb[rgbIndex] = (byte)R;
                        rgb[rgbIndex + 1] = (byte)G;
                        rgb[rgbIndex + 2] = (byte)B;
                        rgb[rgbIndex + 3] = 255; // Alpha
                    }
                }

                // Update OpenGL texture with the RGB data
                
                GL.BindTexture(TextureTarget.Texture2D, textureColorbuffer);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, pixelBuffer);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            }
            finally
            {
                Marshal.FreeHGlobal(rgbBuffer);
            }
}


private void UpdateOpenGLTextureWithVideoFrame()
{
    
    // Get the decoded frame data as a byte array
    byte[] frameData = Video.GetDecodedFrame();
    if (frameData == null)
        return;

    int width = Video.FrameWidth;
    int height = Video.FrameHeight;

    // Bind the OpenGL texture and upload the frame data
    GL.BindTexture(TextureTarget.Texture2D, videoTextureId);

    // Ensure tight packing of pixel rows
    GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);  // Changed from PixelStorei

    GL.PixelStore(PixelStoreParameter.UnpackRowLength, width); // Set the unpack alignment based on linesize
    GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, frameData);
    GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
}

    private int vao, vbo;

private void InitializeQuad()
{
    float[] vertices = {
        // positions    // texture coords
        -1.0f, -1.0f,   0.0f, 1.0f, // bottom left
         1.0f, -1.0f,   1.0f, 1.0f, // bottom right
        -1.0f,  1.0f,   0.0f, 0.0f, // top left
         1.0f,  1.0f,   1.0f, 0.0f  // top right
    };

    // Generate and bind a Vertex Array Object (VAO)
    vao = GL.GenVertexArray();
    GL.BindVertexArray(vao);

    // Generate a Vertex Buffer Object (VBO)
    vbo = GL.GenBuffer();
    GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
    GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

    // Define the vertex attributes for position and texture coordinates
    GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
    GL.EnableVertexAttribArray(0);

    GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
    GL.EnableVertexAttribArray(1);

    // Unbind the VBO and VAO
    GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    GL.BindVertexArray(0);
}


    private void RenderQuad()
    {
        // Bind the VAO and draw the quad
        GL.BindVertexArray(vao);
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        GL.BindVertexArray(0);
    }
        public void Start()
        {
            Manager.Begin();
            Audio?.Resume();
        }

        public void Destroy()
        {
            Manager.Stop().GetAwaiter().GetResult();

            // Dispose of unmanaged resources
            Audio?.Dispose();
            Video?.Dispose();

            Manager.Dispose();
        }

        public void ResolutionChanged()
        {
            const double Ratio = (double)StreamInfo.VideoWidth / StreamInfo.VideoHeight;

            var w = (int)Program.SdlCtx.WindowSize.X;
            var h = (int)Program.SdlCtx.WindowSize.Y;

            if (w >= h * Ratio)
            {
                DisplayRect.w = (int)(h * Ratio);
                DisplayRect.h = h;
            }
            else
            {
                DisplayRect.h = (int)(w / Ratio);
                DisplayRect.w = w;
            }

            DisplayRect.x = w / 2 - DisplayRect.w / 2;
            DisplayRect.y = h / 2 - DisplayRect.h / 2;
        }

        int debugFps = 0;
        public string GetDebugString()
        {
            var sb = new StringBuilder();

            if (fps.GetFps(out var f))
                debugFps = f;

            sb.AppendLine($"Video fps: {debugFps} DispRect {DisplayRect.x} {DisplayRect.y} {DisplayRect.w} {DisplayRect.h}");
            sb.AppendLine($"Video pending packets: {Manager.VideoTarget?.Pending}");
            sb.AppendLine($"IsCompatibleAudioStream: {Manager.IsCompatibleAudioStream}");
            return sb.ToString();
        }

        public string? GetChosenDecoder()
        {
            if (Program.Options.DecoderName is not null)
            {
                if (Video.DecoderName != Program.Options.DecoderName)
                {
                    return string.Format(Program.Strings.Player.CustomDecoderError, Program.Options.DecoderName, Video.DecoderName);
                }
                else
                {
                    return string.Format(Program.Strings.Player.CustomDecoderEnabled, Program.Options.DecoderName);
                }
            }

            if (Video.AcceleratedDecotr)
            {
				return string.Format(Program.Strings.Player.CustomDecoderEnabled, Video.DecoderName);
			}

            return null;
        }

        // For imgui usage, this function draws the current frame
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool DrawAsync()
{
    if (Video == null)
        return false;

    if (Video.DecodeFrame())
    {
        fps.OnFrame();
    }

    // Render the frame using OpenGL
    RenderFrame();

    // Signal we're presenting something to SDL to kick the decoding thread
    Video.Decoder.OnFrameEvent.Set();

    return true;
}

        // For legacy player usage, this locks the thread until the next frame is ready
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool DrawLocked()
        {
            if (Video is null)
                return false;

            if (!Video.DecodeFrame())
                return false;

			//SDL_RenderCopy(Program.SdlCtx.RendererHandle, Video.TargetTexture, ref Video.TargetTextureSize, ref DisplayRect);
			Video.Decoder.OnFrameEvent.Set();

            return true;
		}

        private unsafe void InitializeLoadingTexture()
        {
            Program.SdlCtx.BugCheckThreadId();

            // This hardcodes YUV dats
            if (Video.TargetTextureFormat != SDL_PIXELFORMAT_IYUV)
                return;

            byte[] data = null;

            try
            {
                data = Resources.ReadResource(Resources.LoadingImage);
            }
            catch
            {
                // Don't care
            }

            if (data == null)
            {
                // Hardcoded buffer size for a 1280x720 YUV texture
                data = new byte[0x1517F0];
                // Fill with YUV white
                data.AsSpan(0, 0xE1000).Fill(0xFF);
                data.AsSpan(0xE1000, 0x119400 - 0xE1000).Fill(0x7F);
                data.AsSpan(0x119400).Fill(0x80);
            }

            fixed (byte* ptr = data)
                SDL_UpdateYUVTexture(Video.TargetTexture, ref Video.TargetTextureSize,
                    (nint)ptr, 1280, (nint)(ptr + 0xE1000), 640, (nint)(ptr + 0x119400), 640);
        }
    }

    internal class PlayerView : View
    {
        public readonly StringTable.PlayerTable Strings = Program.Strings.Player;

        readonly bool HasAudio;
        readonly bool HasVideo;

        readonly PlayerCore player;

        bool OverlayAlwaysShowing = false;

        List<PendingUiNotif> notifications = new();

        Gui.CenterGroup uiOptCenter;
        Gui.CenterGroup quitOptCenter;
        Gui.Popup quitConfirm = new(Program.Strings.Player.ConfirmQuitPopupTitle);
        Gui.Popup fatalError = new(Program.Strings.General.PopupErrorTitle);

        bool drawUi;
        string fatalMessage;

        public bool IsRecording => videoRecorder is not null;
        string recordingButtonText = Program.Strings.Player.StartRecording;
        Mp4Output? videoRecorder;

        readonly string volumePercentFormat;

        void MessageUi(string message)
        {
            Console.WriteLine(message);
            notifications.Add(new PendingUiNotif(message));
        }

        public override void Created()
        {
            player.Start();
            base.Created();
        }

        public override void DrawDebug()
        {
            ImGui.Text(player.GetDebugString());
        }

        void ShowPlayerOptionMessage()
        {
            var dec = player.GetChosenDecoder();
            if (dec is not null)
                MessageUi(dec);
        }

        public PlayerView(PlayerManager manager)
        {

            Console.WriteLine("Initializing PlayerView");
            // Adaptive rendering causes a lot of stuttering, for now avoid it in the video player
            RenderMode =
                Program.Options.UncapStreaming ? FramerateCapOptions.Uncapped() :
                FramerateCapOptions.Target(36);

            Popups.Add(quitConfirm);
            Popups.Add(fatalError);

            HasVideo = manager.HasVideo;
            HasAudio = manager.HasAudio;

            if (!HasAudio && !HasVideo)
                throw new Exception("Can't start a player with no streams");

            manager.OnFatalError += Manager_OnFatalError;
            manager.OnErrorMessage += Manager_OnErrorMessage;

            player = new PlayerCore(manager);

            if (HasVideo)
                ShowPlayerOptionMessage();

            if (!HasVideo)
                OverlayAlwaysShowing = true;

            drawUi = OverlayAlwaysShowing;

            if (Program.Options.PlayerHotkeys && !Program.IsAndroid) // Android is less likely to have a keyboard so don't show the hint. The hotkeys still work.
                MessageUi(Strings.Shortcuts);

            // Convert C# format to ImGui format
            volumePercentFormat = Strings.VolumePercent
                .Replace("%", "%%") // escape the % sign
                .Replace("{0}", "%d"); // replace the format specifier
        }

        private void Manager_OnErrorMessage(string obj) =>
            MessageUi(obj);

        private void Manager_OnFatalError(Exception obj)
        {
            fatalMessage = obj.ToString();
            Popups.Open(fatalError);
        }

        public override void Draw()
        {
            if (!Gui.BeginWindow("Player", ImGuiWindowFlags.NoBackground))
                return;

            if (!HasVideo)
            {
                Gui.H2();
                Gui.CenterText(Strings.AudioOnlyMode);
				Gui.PopFont();
            }

            for (int i = 0; i < notifications.Count; i++)
            {
                var notif = notifications[i];
                ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1, 0, 0, 1));
                ImGui.TextWrapped(notif.Text);
                ImGui.PopStyleColor();
                if (notif.ShouldRemove)
                {
                    notifications.RemoveAt(i);
                    notif.Dispose();
                }
            }

            if (drawUi)
                DrawOverlayMenu();

            DrawOverlayToggleArea();

            DrawQuitModal();

            if (fatalError.Begin(ImGui.GetIO().DisplaySize * 0.95f))
            {
                ImGui.TextWrapped(fatalMessage);
                if (Gui.CenterButton(GeneralStrings.PopupCloseButton))
                    Program.Instance.PopView();

                Gui.MakeWindowScrollable();
                ImGui.EndPopup();
            }

            ImGui.End();
        }

        public override void OnKeyPressed(SDL_Keysym key)
        {
            if (!Program.Options.PlayerHotkeys)
                return;

			// Handle hotkeys
			if (key.sym == SDL_Keycode.SDLK_s)
				ButtonScreenshot();
			if (key.sym == SDL_Keycode.SDLK_r)
				ButtonToggleRecording();
            if (key.sym == SDL_Keycode.SDLK_f)
                Program.SdlCtx.SetFullScreen(!Program.SdlCtx.IsFullscreen);
            if (key.sym == SDL_Keycode.SDLK_UP && player.Manager.AudioTarget is not null)
                player.Manager.AudioTarget.Volume += 0.1f;
			if (key.sym == SDL_Keycode.SDLK_DOWN && player.Manager.AudioTarget is not null)
				player.Manager.AudioTarget.Volume -= 0.1f;
		}

        public override void BackPressed()
        {
            if (Popups.AnyOpen)
            {
                base.BackPressed();
                return;
            }

            Popups.Open(quitConfirm);
        }

        void DrawOverlayToggleArea()
        {
            if (OverlayAlwaysShowing)
                return;

            var rect = new ImRect()
            {
                Min = ImGui.GetWindowPos(),
                Max = ImGui.GetWindowPos() + ImGui.GetWindowSize(),
            };

            var id = ImGui.GetID("##TepToReveal");
            ImGui.KeepAliveID(id);
            if (ImGui.ButtonBehavior(rect, id, out _, out _, ImGuiButtonFlags.MouseButtonLeft) || ImGui.IsKeyPressed(ImGuiKey.Space))
            {
                drawUi = !drawUi;
            }
        }

        void DrawVolumeSlider(float x, float width) 
        {
			if (player.Manager.AudioTarget is not null)
			{
				var vol = (int)(player.Manager.AudioTarget.Volume * 100);
				var volnew = vol;
				ImGui.SetCursorPosX(x);
				ImGui.PushItemWidth(width);
				ImGui.SliderInt("##VolumeSlider", ref volnew, 0, 100, volumePercentFormat);
				if (vol != volnew)
					player.Manager.AudioTarget.Volume = volnew / 100f;
			}
		}

        void DrawOverlayMenu()
        {
            float OverlayY = ImGui.GetWindowSize().Y;

            if (Program.Instance.IsPortrait)
            {
                OverlayY = OverlayY * 6 / 10;
                ImGui.SetCursorPosY(OverlayY + ImGui.GetStyle().WindowPadding.Y);

                var width = ImGui.GetWindowSize().X;

                var btnwidth = width * 3 / 6;
                var btnheight = (ImGui.GetWindowSize().Y - ImGui.GetCursorPosY()) / 8;
                var btnsize = new System.Numerics.Vector2(btnwidth, btnheight);

                var center = width / 2 - btnwidth / 2;

                if (HasVideo)
                {
                    ImGui.SetCursorPosX(center);
                    if (ImGui.Button(Strings.TakeScreenshot, btnsize)) ButtonScreenshot();
                }

                ImGui.SetCursorPosX(center);
                if (ImGui.Button(recordingButtonText, btnsize)) ButtonToggleRecording();

                ImGui.SetCursorPosX(center);
                if (ImGui.Button(Strings.StopStreaming, btnsize)) ButtonQuit();

                if (Program.Options.Debug.Log)
                {
                    ImGui.SetCursorPosX(center);
                    if (ImGui.Button(Strings.DebugInfo, btnsize)) ButtonStats();
                }

                ImGui.SetCursorPosX(center);
                if (ImGui.Button(Strings.EnterFullScreen, btnsize)) ButtonFullscreen();

                ImGui.NewLine();
                DrawVolumeSlider(center, btnwidth);
            }
            else
            {
                OverlayY = OverlayY * 4 / 6;
                var spacing = ImGui.GetStyle().ItemSpacing.X * 3;

                ImGui.SetCursorPosY(OverlayY + ImGui.GetStyle().WindowPadding.Y);

                uiOptCenter.StartHere();
                if (HasVideo)
                {
                    if (ImGui.Button(Strings.TakeScreenshot)) ButtonScreenshot();
                    ImGui.SameLine();
                }
                if (ImGui.Button(recordingButtonText)) ButtonToggleRecording();
                ImGui.SameLine(0, spacing);
                if (ImGui.Button(Strings.StopStreaming)) ButtonQuit();
                ImGui.SameLine(0, spacing);
                
                if (Program.Options.Debug.Log)
                {
                    if (ImGui.Button(Strings.DebugInfo)) ButtonStats();
                    ImGui.SameLine();
                }

                if (ImGui.Button(Strings.EnterFullScreen)) ButtonFullscreen();
                uiOptCenter.EndHere();

                ImGui.NewLine();
                var w = ImGui.GetWindowSize().X;
				DrawVolumeSlider(w / 4, w / 2);
			}

            if (!OverlayAlwaysShowing)
            {
                ImGui.SetCursorPosY(ImGui.GetWindowSize().Y - ImGui.CalcTextSize("A").Y - ImGui.GetStyle().WindowPadding.Y);
                Gui.CenterText(Strings.HideOverlayLabel);
            }

            ImGui.GetBackgroundDrawList().AddRectFilled(new(0, OverlayY), ImGui.GetWindowSize(), 0xe0000000);
        }

        void DrawQuitModal()
        {
            if (quitConfirm.Begin())
            {
                ImGui.Text(Strings.ConfirmQuitLabel);
                ImGui.Separator();

                var w = ImGui.GetWindowSize().X / 4;
                quitOptCenter.StartHere();

				if (ImGui.Button(GeneralStrings.YesButton, new(w, 0)))
                {
                    quitConfirm.RequestClose();
                    Program.Instance.PopView();
                }

                ImGui.SameLine();

                if (ImGui.Button(GeneralStrings.NoButton, new(w, 0)))
                    quitConfirm.RequestClose();

                quitOptCenter.EndHere();

				ImGui.EndPopup();
            }
        }

        void ScreenshotToClipboard() 
        {
            if (!Program.IsWindows)
                throw new Exception("Screenshots to clipboard are only supported on windows");

			using (var cap = SDLCapture.CaptureTexture(player.Video.TargetTexture))
				Platform.Specific.Win.WinClipboard.CopyCapture(cap);

			MessageUi(Strings.ScreenshotSavedToClip);
		}

        void ScreenshotToFile() 
        {
			var path = Program.Options.GetFilePathForScreenshot();
			SDLCapture.ExportTexture(player.Video.TargetTexture, path);
			MessageUi(string.Format(Strings.ScreenshotSaved, path));
		}

        void ButtonScreenshot()
        {
            try
            {
                if (Program.IsWindows)
                {
                    var clip = Program.Options.Windows_ScreenToClip;
                    // shift inverts the clipboard flag
                    if (Program.Instance.ShiftDown)
                        clip = !clip;

                    if (clip)
                    {
                        ScreenshotToClipboard();
                        return;
                    }
				}

                ScreenshotToFile();
			}
            catch (Exception ex)
            {
                MessageUi($"{GeneralStrings.ErrorMessage} {ex}");
                Console.WriteLine(ex);
#if ANDROID_LIB
                MessageUi(Strings.AndroidPermissionError);
#endif
			}
		}

        void ButtonToggleRecording()
        {
            if (videoRecorder is null)
            {
                try
                {
                    var videoFile = Program.Options.GetFilePathForVideo();

                    Mp4VideoTarget? v = HasVideo ? new() : null;
                    Mp4AudioTarget? a = HasAudio ? new() : null;

                    videoRecorder = new Mp4Output(videoFile, v, a);
                    videoRecorder.Start();

                    player.Manager.ChainTargets(v, a);

                    recordingButtonText = Strings.StopRecording;
                    MessageUi(string.Format(Strings.RecordingStartedMessage, videoFile));
                }
                catch (Exception ex)
                {
                    MessageUi($"{GeneralStrings.ErrorMessage} {ex}");                    
                    videoRecorder?.Dispose();
                    videoRecorder = null;
				}
			}
            else
            {
                player.Manager.UnchainTargets(videoRecorder.VideoTarget, videoRecorder.AudioTarget);
                videoRecorder.Stop();
                videoRecorder.Dispose();
                videoRecorder = null;
                recordingButtonText = Strings.StartRecording;
                MessageUi(Strings.RecordingSuccessMessage);
            }
        }

        void ButtonStats()
        {
            Program.Instance.ShowDebugInfo = !Program.Instance.ShowDebugInfo;
        }

        void ButtonQuit()
        {
            BackPressed();
        }

        void ButtonFullscreen()
        {
            Program.SdlCtx.SetFullScreen(!Program.SdlCtx.IsFullscreen);
        }

        unsafe public override void RawDraw()
        {
            base.RawDraw();
            player.DrawAsync();
        }

        public override void ResolutionChanged()
        {
            player.ResolutionChanged();
        }

        public override void Destroy()
        {
            Program.SdlCtx.BugCheckThreadId();

            if (IsRecording)
                ButtonToggleRecording();

            player.Destroy();
            base.Destroy();
        }
    }
}
