using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;
using ImGuiNET;
using System.Numerics;

// Add aliases at the top
using Vec3Numerics = System.Numerics.Vector3;
using Vec2Numerics = System.Numerics.Vector2;
using Vec3OpenTK = OpenTK.Mathematics.Vector3;
using Vec2OpenTK = OpenTK.Mathematics.Vector2;

class Program
{
    static bool UseGpu = false;
    static int cpuTexture = 0;
    static int imageWidth = 400;
    static int imageHeight = 200;
    static byte[] cpuPixels;
    static bool cpuImageDirty = true;
    static int quadVao = 0, quadVbo = 0;
    static int gpuShader = 0;
    static int cpuShader = 0;

    // Camera state
    static float cameraYaw = 0.0f;
    static float cameraPitch = 0.0f;
    static Vec3OpenTK cameraPos = new Vec3OpenTK(0, 0, 0);
    static bool rightMouseDown = false;
    static bool leftMouseDown = false;
    static Vec2Numerics lastMousePos;
    static bool cameraDirty = true;
    static float cameraFov = 90.0f;
    static float cameraMoveSpeed = 2.0f;
    static float cameraFastSpeed = 8.0f;
    static float cameraSlowSpeed = 0.5f;
    static Vec3OpenTK cameraVelocity = Vec3OpenTK.Zero;
    static float cameraAccel = 10.0f;
    static float cameraDamping = 6.0f;

    // Change sphere arrays to lists
    static List<Vec3Numerics> sphereCenters = new List<Vec3Numerics> {
        new Vec3Numerics(0, 0, -1),
        new Vec3Numerics(1, 0, -2),
        new Vec3Numerics(-1, 0, -2),
        new Vec3Numerics(0, -100.5f, -1),
        new Vec3Numerics(2, 0.5f, -3),
        new Vec3Numerics(-2, 0.5f, -3),
        new Vec3Numerics(0, 1.5f, -2.5f),
        new Vec3Numerics(0, -0.5f, -3.5f)
    };
    static List<float> sphereRadii = new List<float> { 0.5f, 0.5f, 0.5f, 100f, 0.5f, 0.5f, 0.3f, 0.7f };
    static List<Vec3Numerics> sphereColors = new List<Vec3Numerics> {
        new Vec3Numerics(1, 0, 0),
        new Vec3Numerics(0, 1, 0),
        new Vec3Numerics(0, 0, 1),
        new Vec3Numerics(1, 1, 0),
        new Vec3Numerics(1, 0, 1),
        new Vec3Numerics(0, 1, 1),
        new Vec3Numerics(1, 0.5f, 0),
        new Vec3Numerics(0.5f, 0.25f, 1)
    };
    static List<float> sphereReflect = new List<float> { 0.5f, 0.2f, 0.8f, 0.0f, 0.6f, 0.3f, 0.1f, 0.7f };
    static Vec3 lightPos = new Vec3(5, 5, -3);
    static Vec3 lightColor = new Vec3(1, 1, 1);
    static Vec3 lightPos2 = new Vec3(-5, 6, -2);
    static Vec3 lightColor2 = new Vec3(0.7, 0.7, 1.0);
    // Add light direction
    static Vec3 lightDir = new Vec3(0, -1, -1);
    static Vec3 lightDir2 = new Vec3(0, -1, 1);

    static double fpsTimer = 0;
    static int fpsFrames = 0;
    static double lastFps = 0;

    static ImGuiController imguiController;

    static bool light1Enabled = true;
    static bool light1Directional = false;
    static bool light2Enabled = true;
    static bool light2Directional = false;

    static void Main(string[] args)
    {
        RunWindow();
    }

    static void RunWindow()
    {
        var nativeSettings = new NativeWindowSettings()
        {
            Size = new Vector2i(800, 400),
            Title = "Ray Tracer (CPU Mode)",
        };
        using (var window = new GameWindow(GameWindowSettings.Default, nativeSettings))
        {
            string modeTitle = "Ray Tracer (CPU Mode)";
            window.Load += () =>
            {
                GL.ClearColor(0.2f, 0.2f, 0.2f, 1.0f);
                SetupQuad();
                SetupGpuShader();
                SetupCpuShader();
                cpuTexture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, cpuTexture);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                cpuPixels = new byte[imageWidth * imageHeight * 3];
                cpuImageDirty = true;
                imguiController = new ImGuiController(window.ClientSize.X, window.ClientSize.Y);
            };
            window.Resize += (ResizeEventArgs e) =>
            {
                GL.Viewport(0, 0, e.Width, e.Height);
                imguiController.WindowResized(window.ClientSize.X, window.ClientSize.Y);
            };
            window.MouseDown += (MouseButtonEventArgs e) =>
            {
                if (e.Button == MouseButton.Right)
                {
                    rightMouseDown = true;
                    lastMousePos = new System.Numerics.Vector2(window.MousePosition.X, window.MousePosition.Y);
                }
            };
            window.MouseUp += (MouseButtonEventArgs e) =>
            {
                if (e.Button == MouseButton.Right)
                {
                    rightMouseDown = false;
                }
            };
            window.MouseMove += (MouseMoveEventArgs e) =>
            {
                if (rightMouseDown)
                {
                    float sensitivity = 0.005f;
                    var delta = new System.Numerics.Vector2(e.Position.X, e.Position.Y) - lastMousePos;
                    cameraYaw += delta.X * sensitivity;
                    cameraPitch -= delta.Y * sensitivity;
                    cameraPitch = Math.Clamp(cameraPitch, -1.55f, 1.55f); // ~89 deg
                    lastMousePos = new System.Numerics.Vector2(e.Position.X, e.Position.Y);
                    cameraDirty = true;
                    cpuImageDirty = true;
                }
            };
            window.MouseWheel += (MouseWheelEventArgs e) =>
            {
                cameraFov -= e.OffsetY * 2.0f;
                cameraFov = Math.Clamp(cameraFov, 30.0f, 120.0f);
                cameraDirty = true;
                cpuImageDirty = true;
            };
            window.UpdateFrame += (FrameEventArgs e) =>
            {
                float move = cameraMoveSpeed * (float)e.Time;
                if (window.IsKeyDown(Keys.LeftShift) || window.IsKeyDown(Keys.RightShift)) move = cameraFastSpeed * (float)e.Time;
                if (window.IsKeyDown(Keys.LeftControl) || window.IsKeyDown(Keys.RightControl)) move = cameraSlowSpeed * (float)e.Time;
                var forward = new Vec3OpenTK((float)Math.Sin(cameraYaw) * (float)Math.Cos(cameraPitch),
                                          -(float)Math.Sin(cameraPitch),
                                          (float)Math.Cos(cameraYaw) * (float)Math.Cos(cameraPitch));
                var right = new Vec3OpenTK((float)Math.Cos(cameraYaw), 0, -(float)Math.Sin(cameraYaw));
                var up = Vec3OpenTK.UnitY;
                Vec3OpenTK desiredVel = Vec3OpenTK.Zero;
                if (window.IsKeyDown(Keys.W)) desiredVel += forward;
                if (window.IsKeyDown(Keys.S)) desiredVel -= forward;
                if (window.IsKeyDown(Keys.A)) desiredVel -= right;
                if (window.IsKeyDown(Keys.D)) desiredVel += right;
                if (window.IsKeyDown(Keys.Q)) desiredVel -= up;
                if (window.IsKeyDown(Keys.E)) desiredVel += up;
                if (desiredVel.LengthSquared > 0)
                {
                    desiredVel = desiredVel.Normalized() * move / (float)e.Time;
                }
                // Smooth acceleration
                cameraVelocity += (desiredVel - cameraVelocity) * Math.Min(1.0f, cameraAccel * (float)e.Time);
                // Damping
                cameraVelocity *= (1.0f - Math.Min(1.0f, cameraDamping * (float)e.Time));
                if (cameraVelocity.LengthSquared > 1e-6)
                {
                    cameraPos += cameraVelocity * (float)e.Time;
                    cameraDirty = cpuImageDirty = true;
                }
                if (window.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.G))
                {
                    UseGpu = true;
                    window.Title = "Ray Tracer (GPU Mode)";
                    Console.WriteLine("Switched to GPU mode");
                }
                if (window.IsKeyPressed(OpenTK.Windowing.GraphicsLibraryFramework.Keys.C))
                {
                    UseGpu = false;
                    window.Title = "Ray Tracer (CPU Mode)";
                    Console.WriteLine("Switched to CPU mode");
                }
            };
            window.RenderFrame += (FrameEventArgs e) =>
            {
                imguiController.Update(window, (float)e.Time);
                // FPS counter
                fpsFrames++;
                fpsTimer += e.Time;
                if (fpsTimer >= 0.5)
                {
                    lastFps = fpsFrames / fpsTimer;
                    fpsFrames = 0;
                    fpsTimer = 0;
                    window.Title = $"{modeTitle} - FPS: {lastFps:F1}";
                }
                if (UseGpu)
                {
                    modeTitle = "Ray Tracer (GPU Mode)";
                    GL.UseProgram(gpuShader);
                    const int MAX_SPHERES = 8;
                    int numToSend = Math.Min(sphereCenters.Count, MAX_SPHERES);
                    for (int i = 0; i < MAX_SPHERES; ++i) {
                        Vec3OpenTK center = i < sphereCenters.Count ? new Vec3OpenTK(sphereCenters[i].X, sphereCenters[i].Y, sphereCenters[i].Z) : new Vec3OpenTK(0,0,0);
                        Vec3OpenTK color = i < sphereColors.Count ? new Vec3OpenTK(sphereColors[i].X, sphereColors[i].Y, sphereColors[i].Z) : new Vec3OpenTK(0,0,0);
                        float radius = i < sphereRadii.Count ? sphereRadii[i] : 0f;
                        float reflect = i < sphereReflect.Count ? sphereReflect[i] : 0f;
                        GL.Uniform3(GL.GetUniformLocation(gpuShader, $"spheres[{i}].center"), ref center);
                        GL.Uniform1(GL.GetUniformLocation(gpuShader, $"spheres[{i}].radius"), radius);
                        GL.Uniform3(GL.GetUniformLocation(gpuShader, $"spheres[{i}].color"), ref color);
                        GL.Uniform1(GL.GetUniformLocation(gpuShader, $"spheres[{i}].reflectivity"), reflect);
                    }
                    GL.Uniform1(GL.GetUniformLocation(gpuShader, "numSpheres"), numToSend);
                    var lightPosVec3 = new Vec3OpenTK((float)lightPos.X, (float)lightPos.Y, (float)lightPos.Z);
                    var lightColorVec3 = new Vec3OpenTK((float)lightColor.X, (float)lightColor.Y, (float)lightColor.Z);
                    var lightPos2Vec3 = new Vec3OpenTK((float)lightPos2.X, (float)lightPos2.Y, (float)lightPos2.Z);
                    var lightColor2Vec3 = new Vec3OpenTK((float)lightColor2.X, (float)lightColor2.Y, (float)lightColor2.Z);
                    GL.Uniform3(GL.GetUniformLocation(gpuShader, "lightPos"), ref lightPosVec3);
                    GL.Uniform3(GL.GetUniformLocation(gpuShader, "lightColor"), ref lightColorVec3);
                    GL.Uniform3(GL.GetUniformLocation(gpuShader, "lightPos2"), ref lightPos2Vec3);
                    GL.Uniform3(GL.GetUniformLocation(gpuShader, "lightColor2"), ref lightColor2Vec3);
                    GL.Uniform3(GL.GetUniformLocation(gpuShader, "cameraPos"), ref cameraPos);
                    GL.Uniform1(GL.GetUniformLocation(gpuShader, "cameraYaw"), cameraYaw);
                    GL.Uniform1(GL.GetUniformLocation(gpuShader, "cameraPitch"), cameraPitch);
                    GL.Uniform1(GL.GetUniformLocation(gpuShader, "cameraFov"), cameraFov);
                    GL.BindVertexArray(quadVao);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
                }
                else
                {
                    modeTitle = "Ray Tracer (CPU Mode)";
                    if (cpuImageDirty)
                    {
                        RenderCpuImageToBuffer();
                        GL.BindTexture(TextureTarget.Texture2D, cpuTexture);
                        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, imageWidth, imageHeight, 0, PixelFormat.Rgb, PixelType.UnsignedByte, cpuPixels);
                        cpuImageDirty = false;
                    }
                    GL.UseProgram(cpuShader);
                    GL.ActiveTexture(TextureUnit.Texture0);
                    GL.BindTexture(TextureTarget.Texture2D, cpuTexture);
                    GL.Uniform1(GL.GetUniformLocation(cpuShader, "tex"), 0);
                    GL.BindVertexArray(quadVao);
                    GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
                }
                // ImGui UI code
                ImGui.Begin("Scene Editor");
                ImGui.Text("Spheres");
                ImGui.SameLine();
                if (ImGui.Button("Clear All##Spheres")) {
                    sphereCenters.Clear();
                    sphereColors.Clear();
                    sphereRadii.Clear();
                    sphereReflect.Clear();
                    cpuImageDirty = true;
                }
                ImGui.SameLine();
                if (ImGui.Button("Reset Scene")) {
                    // Reset to defaults
                    sphereCenters.Clear();
                    sphereColors.Clear();
                    sphereRadii.Clear();
                    sphereReflect.Clear();
                    sphereCenters.AddRange(new[]{
                        new Vec3Numerics(0, 0, -1),
                        new Vec3Numerics(1, 0, -2),
                        new Vec3Numerics(-1, 0, -2),
                        new Vec3Numerics(0, -100.5f, -1),
                        new Vec3Numerics(2, 0.5f, -3),
                        new Vec3Numerics(-2, 0.5f, -3),
                        new Vec3Numerics(0, 1.5f, -2.5f),
                        new Vec3Numerics(0, -0.5f, -3.5f)
                    });
                    sphereRadii.AddRange(new[]{ 0.5f, 0.5f, 0.5f, 100f, 0.5f, 0.5f, 0.3f, 0.7f });
                    sphereColors.AddRange(new[]{
                        new Vec3Numerics(1, 0, 0),
                        new Vec3Numerics(0, 1, 0),
                        new Vec3Numerics(0, 0, 1),
                        new Vec3Numerics(1, 1, 0),
                        new Vec3Numerics(1, 0, 1),
                        new Vec3Numerics(0, 1, 1),
                        new Vec3Numerics(1, 0.5f, 0),
                        new Vec3Numerics(0.5f, 0.25f, 1)
                    });
                    sphereReflect.AddRange(new[]{ 0.5f, 0.2f, 0.8f, 0.0f, 0.6f, 0.3f, 0.1f, 0.7f });
                    cpuImageDirty = true;
                }
                for (int i = 0; i < sphereCenters.Count; i++)
                {
                    ImGui.PushID(i);
                    ImGui.Text($"Sphere {i}");
                    var pos = sphereCenters[i];
                    var color = sphereColors[i];
                    float radius = sphereRadii[i];
                    float reflect = sphereReflect[i];
                    if (ImGui.DragFloat3("Position", ref pos)) { sphereCenters[i] = pos; cpuImageDirty = true; }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Drag to move sphere position");
                    if (ImGui.ColorEdit3("Color", ref color)) { sphereColors[i] = color; cpuImageDirty = true; }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Edit sphere color");
                    if (ImGui.DragFloat("Radius", ref radius, 0.01f, 0.05f, 10f)) { sphereRadii[i] = radius; cpuImageDirty = true; }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Adjust sphere radius");
                    if (ImGui.SliderFloat("Reflectivity", ref reflect, 0f, 1f)) { sphereReflect[i] = reflect; cpuImageDirty = true; }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Adjust reflectivity (0=matte, 1=mirror)");
                    ImGui.SameLine();
                    if (ImGui.Button("Duplicate")) {
                        sphereCenters.Add(pos);
                        sphereColors.Add(color);
                        sphereRadii.Add(radius);
                        sphereReflect.Add(reflect);
                        cpuImageDirty = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Randomize")) {
                        var rand = new Random();
                        sphereCenters[i] = new Vec3Numerics((float)(rand.NextDouble()*4-2), (float)(rand.NextDouble()*2-1), (float)(-1-rand.NextDouble()*4));
                        sphereColors[i] = new Vec3Numerics((float)rand.NextDouble(), (float)rand.NextDouble(), (float)rand.NextDouble());
                        sphereRadii[i] = 0.2f + (float)rand.NextDouble()*1.2f;
                        sphereReflect[i] = (float)rand.NextDouble();
                        cpuImageDirty = true;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Remove")) {
                        sphereCenters.RemoveAt(i);
                        sphereColors.RemoveAt(i);
                        sphereRadii.RemoveAt(i);
                        sphereReflect.RemoveAt(i);
                        cpuImageDirty = true;
                        ImGui.PopID();
                        break;
                    }
                    ImGui.PopID();
                }
                if (ImGui.Button("Add Sphere")) {
                    sphereCenters.Add(new Vec3Numerics(0, 0, -2));
                    sphereColors.Add(new Vec3Numerics(1, 1, 1));
                    sphereRadii.Add(0.5f);
                    sphereReflect.Add(0.0f);
                    cpuImageDirty = true;
                }
                ImGui.SameLine();
                if (ImGui.Button("Add Plane (future)")) {
                    // Placeholder for future shape support
                }
                ImGui.Separator();
                ImGui.Text("Lights");
                // Light 1 controls
                ImGui.Checkbox("Enable Light 1", ref light1Enabled);
                ImGui.SameLine();
                ImGui.Checkbox("Directional##1", ref light1Directional);
                if (light1Directional) {
                    var dir1 = new System.Numerics.Vector3((float)lightDir.X, (float)lightDir.Y, (float)lightDir.Z);
                    if (ImGui.DragFloat3("Light 1 Direction", ref dir1, 0.05f)) {
                        if (dir1.Length() > 0.0001f) dir1 = System.Numerics.Vector3.Normalize(dir1);
                        lightDir = new Vec3(dir1);
                        cpuImageDirty = true;
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Edit light 1 direction (unit vector)");
                } else {
                    var pos1 = new System.Numerics.Vector3((float)lightPos.X, (float)lightPos.Y, (float)lightPos.Z);
                    if (ImGui.DragFloat3("Light 1 Position", ref pos1, 0.1f)) {
                        lightPos = new Vec3(pos1);
                        cpuImageDirty = true;
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Edit light 1 position");
                }
                var color1 = new System.Numerics.Vector3((float)lightColor.X, (float)lightColor.Y, (float)lightColor.Z);
                if (ImGui.ColorEdit3("Light 1 Color", ref color1)) {
                    lightColor = new Vec3(color1);
                    cpuImageDirty = true;
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Edit light 1 color");
                // Light 2 controls
                ImGui.Checkbox("Enable Light 2", ref light2Enabled);
                ImGui.SameLine();
                ImGui.Checkbox("Directional##2", ref light2Directional);
                if (light2Directional) {
                    var dir2 = new System.Numerics.Vector3((float)lightDir2.X, (float)lightDir2.Y, (float)lightDir2.Z);
                    if (ImGui.DragFloat3("Light 2 Direction", ref dir2, 0.05f)) {
                        if (dir2.Length() > 0.0001f) dir2 = System.Numerics.Vector3.Normalize(dir2);
                        lightDir2 = new Vec3(dir2);
                        cpuImageDirty = true;
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Edit light 2 direction (unit vector)");
                } else {
                    var pos2 = new System.Numerics.Vector3((float)lightPos2.X, (float)lightPos2.Y, (float)lightPos2.Z);
                    if (ImGui.DragFloat3("Light 2 Position", ref pos2, 0.1f)) {
                        lightPos2 = new Vec3(pos2);
                        cpuImageDirty = true;
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Edit light 2 position");
                }
                var color2 = new System.Numerics.Vector3((float)lightColor2.X, (float)lightColor2.Y, (float)lightColor2.Z);
                if (ImGui.ColorEdit3("Light 2 Color", ref color2)) {
                    lightColor2 = new Vec3(color2);
                    cpuImageDirty = true;
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Edit light 2 color");
                ImGui.Separator();
                if (ImGui.Button("Save Scene")) {
                    // TODO: Save scene to JSON
                }
                ImGui.SameLine();
                if (ImGui.Button("Load Scene")) {
                    // TODO: Load scene from JSON
                }
                ImGui.End();
                imguiController.Render();
                window.SwapBuffers();
            };
            window.Run();
        }
    }

    static void SetupQuad()
    {
        float[] quadVertices = {
            // positions   // texcoords
            -1f, -1f, 0f, 0f,
             1f, -1f, 1f, 0f,
             1f,  1f, 1f, 1f,
            -1f, -1f, 0f, 0f,
             1f,  1f, 1f, 1f,
            -1f,  1f, 0f, 1f
        };
        quadVao = GL.GenVertexArray();
        quadVbo = GL.GenBuffer();
        GL.BindVertexArray(quadVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, quadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * sizeof(float), quadVertices, BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }

    static void SetupGpuShader()
    {
        string vert = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aTex;
out vec2 TexCoord;
void main() {
    TexCoord = aTex;
    gl_Position = vec4(aPos, 0.0, 1.0);
}";
        string frag = @"#version 330 core
#define MAX_SPHERES 8
out vec4 FragColor;
in vec2 TexCoord;

struct Sphere {
    vec3 center;
    float radius;
    vec3 color;
    float reflectivity;
};

uniform Sphere spheres[MAX_SPHERES];
uniform int numSpheres;
uniform vec3 lightPos;
uniform vec3 lightColor;
uniform vec3 lightPos2;
uniform vec3 lightColor2;
uniform vec3 cameraPos;
uniform float cameraYaw;
uniform float cameraPitch;
uniform float cameraFov;

mat3 getCameraMatrix(float yaw, float pitch) {
    float cy = cos(yaw), sy = sin(yaw);
    float cp = cos(pitch), sp = sin(pitch);
    vec3 right = vec3(cy, 0, -sy);
    vec3 up = vec3(sy*sp, cp, cy*sp);
    vec3 forward = vec3(sy*cp, -sp, cy*cp);
    return mat3(right, up, -forward);
}

vec3 rayDirection(vec2 uv, float yaw, float pitch, float fov) {
    float aspect = 2.0;
    float scale = tan(radians(fov * 0.5));
    float x = (uv.x * 2.0 - 1.0) * aspect * scale;
    float y = (uv.y * 1.0 - 0.5) * scale;
    vec3 dir = normalize(vec3(x, y, -1.0));
    return getCameraMatrix(yaw, pitch) * dir;
}

bool hitSphere(Sphere s, vec3 ro, vec3 rd, out float t, out vec3 n, out vec3 col, out float refl) {
    vec3 oc = ro - s.center;
    float a = dot(rd, rd);
    float b = 2.0 * dot(oc, rd);
    float c = dot(oc, oc) - s.radius * s.radius;
    float disc = b * b - 4.0 * a * c;
    if (disc > 0.0) {
        float sqrtd = sqrt(disc);
        float t0 = (-b - sqrtd) / (2.0 * a);
        float t1 = (-b + sqrtd) / (2.0 * a);
        t = t0 > 0.001 ? t0 : (t1 > 0.001 ? t1 : -1.0);
        if (t > 0.0) {
            vec3 p = ro + t * rd;
            n = normalize(p - s.center);
            col = s.color;
            refl = s.reflectivity;
            return true;
        }
    }
    return false;
}

vec3 trace(vec3 ro, vec3 rd) {
    vec3 color = vec3(0);
    float reflectionFactor = 1.0;
    for (int bounce = 0; bounce < 3; ++bounce) {
        float tMin = 1e-3;
        float tMax = 1e6;
        float closest = tMax;
        vec3 n, col;
        float refl;
        bool hit = false;
        for (int i = 0; i < numSpheres; ++i) {
            float t; vec3 tn; vec3 tc; float tr;
            if (hitSphere(spheres[i], ro, rd, t, tn, tc, tr)) {
                if (t < closest) {
                    closest = t; n = tn; col = tc; refl = tr;
                    hit = true;
                }
            }
        }
        if (hit) {
            vec3 p = ro + closest * rd;
            // Lighting
            vec3 toLight = normalize(lightPos - p);
            float diff = max(0.0, dot(n, toLight));
            vec3 toLight2 = normalize(lightPos2 - p);
            float diff2 = max(0.0, dot(n, toLight2));
            vec3 baseCol = col * (diff * 0.6 + diff2 * 0.4) * 0.8 + col * 0.2;
            // Shadow
            bool shadow = false;
            for (int i = 0; i < numSpheres; ++i) {
                float t; vec3 tn; vec3 tc; float tr;
                if (hitSphere(spheres[i], p + n * 0.001, toLight, t, tn, tc, tr)) {
                    if (t > 0.0 && t < length(lightPos - p)) shadow = true;
                }
                if (hitSphere(spheres[i], p + n * 0.001, toLight2, t, tn, tc, tr)) {
                    if (t > 0.0 && t < length(lightPos2 - p)) shadow = true;
                }
            }
            if (shadow) baseCol *= 0.3;
            color += reflectionFactor * (1.0 - refl) * baseCol;
            reflectionFactor *= refl;
            if (reflectionFactor <= 0.0) break;
            ro = p + n * 0.001;
            rd = reflect(rd, n);
        } else {
            float t = 0.5 * (rd.y + 1.0);
            vec3 bg = mix(vec3(1,1,1), vec3(0.5,0.7,1.0), t);
            color += reflectionFactor * bg;
            break;
        }
    }
    return color;
}

void main() {
    vec3 ro = cameraPos;
    vec3 rd = rayDirection(TexCoord, cameraYaw, cameraPitch, cameraFov);
    vec3 color = trace(ro, rd);
    FragColor = vec4(color, 1.0);
}";
        int v = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(v, vert);
        GL.CompileShader(v);
        PrintShaderLog(v, "VERTEX");
        int f = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(f, frag);
        GL.CompileShader(f);
        PrintShaderLog(f, "FRAGMENT");
        gpuShader = GL.CreateProgram();
        GL.AttachShader(gpuShader, v);
        GL.AttachShader(gpuShader, f);
        GL.LinkProgram(gpuShader);
        PrintProgramLog(gpuShader);
        GL.DeleteShader(v);
        GL.DeleteShader(f);
    }

    static void SetupCpuShader()
    {
        string vert = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aTex;
out vec2 TexCoord;
void main() {
    TexCoord = aTex;
    gl_Position = vec4(aPos, 0.0, 1.0);
}";
        string frag = @"#version 330 core
in vec2 TexCoord;
out vec4 FragColor;
uniform sampler2D tex;
void main() {
    FragColor = texture(tex, vec2(TexCoord.x, 1.0 - TexCoord.y));
}";
        int v = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(v, vert);
        GL.CompileShader(v);
        PrintShaderLog(v, "VERTEX");
        int f = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(f, frag);
        GL.CompileShader(f);
        PrintShaderLog(f, "FRAGMENT");
        cpuShader = GL.CreateProgram();
        GL.AttachShader(cpuShader, v);
        GL.AttachShader(cpuShader, f);
        GL.LinkProgram(cpuShader);
        PrintProgramLog(cpuShader);
        GL.DeleteShader(v);
        GL.DeleteShader(f);
    }

    static void PrintShaderLog(int shader, string type)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
        if (status == 0)
        {
            string log = GL.GetShaderInfoLog(shader);
            Console.WriteLine($"{type} SHADER ERROR:\n{log}");
        }
    }
    static void PrintProgramLog(int program)
    {
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0)
        {
            string log = GL.GetProgramInfoLog(program);
            Console.WriteLine($"PROGRAM LINK ERROR:\n{log}");
        }
    }

    static void RenderCpuImageToBuffer()
    {
        // Render the CPU ray traced image into cpuPixels (RGB byte array)
        const int maxDepth = 3;
        var spheres = new List<Sphere>();
        int sphereCount = Math.Min(sphereCenters.Count, sphereRadii.Count);
        for (int i = 0; i < sphereCount; i++)
        {
            spheres.Add(new Sphere(Vec3.FromNumerics(sphereCenters[i]), sphereRadii[i], Vec3.FromNumerics(sphereColors[i]), sphereReflect[i]));
        }
        var lightPos = new Vec3(5, 5, -3);
        var lightColor = new Vec3(1, 1, 1);
        var lightPos2 = new Vec3(-5, 6, -2);
        var lightColor2 = new Vec3(0.7, 0.7, 1.0);
        int idx = 0;
        // Camera basis
        double aspect = 2.0;
        double scale = Math.Tan(cameraFov * 0.5 * Math.PI / 180.0);
        double cy = Math.Cos(cameraYaw), sy = Math.Sin(cameraYaw);
        double cp = Math.Cos(cameraPitch), sp = Math.Sin(cameraPitch);
        var right = new Vec3(cy, 0, -sy);
        var up = new Vec3(sy*sp, cp, cy*sp);
        var forward = new Vec3(sy*cp, -sp, cy*cp);
        for (int j = 0; j < imageHeight; ++j)
        {
            for (int i = 0; i < imageWidth; ++i)
            {
                double u = (double)i / (imageWidth - 1);
                double v = (double)(imageHeight - 1 - j) / (imageHeight - 1);
                double x = (u * 2 - 1) * aspect * scale;
                double y = (v * 1 - 0.5) * scale;
                var dir = new Vec3(x, y, -1);
                dir = right * dir.X + up * dir.Y - forward * dir.Z;
                dir = Vec3.UnitVector(dir);
                var ray = new Ray(new Vec3(cameraPos.X, cameraPos.Y, cameraPos.Z), dir);
                var color = RayColor(ray, spheres, lightPos, lightColor, maxDepth);
                int ir = Clamp((int)(255.99 * color.X), 0, 255);
                int ig = Clamp((int)(255.99 * color.Y), 0, 255);
                int ib = Clamp((int)(255.99 * color.Z), 0, 255);
                cpuPixels[idx++] = (byte)ir;
                cpuPixels[idx++] = (byte)ig;
                cpuPixels[idx++] = (byte)ib;
            }
        }
    }

    static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);

    static Vec3 RayColor(Ray r, List<Sphere> spheres, Vec3 lightPos, Vec3 lightColor, int depth)
    {
        if (depth <= 0)
            return new Vec3(Vec3Numerics.Zero);
        HitRecord rec;
        if (HitScene(spheres, r, 0.001, double.MaxValue, out rec))
        {
            var toLight = Vec3.UnitVector(lightPos - rec.Point);
            double diffuse = Math.Max(0, Vec3.Dot(rec.Normal, toLight));
            var toLight2 = Vec3.UnitVector(lightPos2 - rec.Point);
            double diffuse2 = Math.Max(0, Vec3.Dot(rec.Normal, toLight2));
            var baseColor = rec.Color * (diffuse * 0.6 + diffuse2 * 0.4) * 0.8 + rec.Color * 0.2;
            // Shadow
            var shadowRay = new Ray(rec.Point + 0.001 * rec.Normal, toLight);
            var shadowRay2 = new Ray(rec.Point + 0.001 * rec.Normal, toLight2);
            var lp1 = new OpenTK.Mathematics.Vector3((float)lightPos.X, (float)lightPos.Y, (float)lightPos.Z);
            var lp2 = new OpenTK.Mathematics.Vector3((float)lightPos2.X, (float)lightPos2.Y, (float)lightPos2.Z);
            var recPointVec3 = new OpenTK.Mathematics.Vector3((float)rec.Point.X, (float)rec.Point.Y, (float)rec.Point.Z);
            var dist1 = (lp1 - recPointVec3).Length;
            var dist2 = (lp2 - recPointVec3).Length;
            if (HitScene(spheres, shadowRay, 0.001, dist1, out _) ||
                HitScene(spheres, shadowRay2, 0.001, dist2, out _))
                baseColor *= 0.3;
            if (rec.Reflectivity > 0)
            {
                var reflectedDir = Reflect(Vec3.UnitVector(r.Direction), rec.Normal);
                var reflectedRay = new Ray(rec.Point + 0.001 * rec.Normal, reflectedDir);
                var reflectedColor = RayColor(reflectedRay, spheres, lightPos, lightColor, depth - 1);
                baseColor = (1 - rec.Reflectivity) * baseColor + rec.Reflectivity * reflectedColor;
            }
            return baseColor;
        }
        var unitDirection = Vec3.UnitVector(r.Direction);
        double t = 0.5 * (unitDirection.Y + 1.0);
        return (1.0 - t) * new Vec3(Vec3Numerics.One) + t * new Vec3(0.5, 0.7, 1.0);
    }

    static bool HitScene(List<Sphere> spheres, Ray r, double tMin, double tMax, out HitRecord rec)
    {
        rec = default;
        double closest = tMax;
        bool hitAnything = false;
        foreach (var s in spheres)
        {
            if (s.Hit(r, tMin, closest, out var tempRec))
            {
                hitAnything = true;
                closest = tempRec.T;
                rec = tempRec;
            }
        }
        return hitAnything;
    }

    static Vec3 Reflect(Vec3 v, Vec3 n) => v - 2 * Vec3.Dot(v, n) * n;
}

struct Sphere
{
    public Vec3 Center;
    public double Radius;
    public Vec3 Color;
    public double Reflectivity;
    public Sphere(Vec3 center, double radius, Vec3 color, double reflectivity)
    {
        Center = center; Radius = radius; Color = color; Reflectivity = reflectivity;
    }
    public bool Hit(Ray r, double tMin, double tMax, out HitRecord rec)
    {
        rec = default;
        var oc = r.Origin - Center;
        var a = Vec3.Dot(r.Direction, r.Direction);
        var b = 2.0 * Vec3.Dot(oc, r.Direction);
        var c = Vec3.Dot(oc, oc) - Radius * Radius;
        var discriminant = b * b - 4 * a * c;
        if (discriminant > 0)
        {
            double sqrtD = Math.Sqrt(discriminant);
            double root = (-b - sqrtD) / (2.0 * a);
            if (root < tMax && root > tMin)
            {
                rec = new HitRecord
                {
                    T = root,
                    Point = r.At(root),
                    Normal = (r.At(root) - Center) / Radius,
                    Color = Color,
                    Reflectivity = Reflectivity
                };
                return true;
            }
            root = (-b + sqrtD) / (2.0 * a);
            if (root < tMax && root > tMin)
            {
                rec = new HitRecord
                {
                    T = root,
                    Point = r.At(root),
                    Normal = (r.At(root) - Center) / Radius,
                    Color = Color,
                    Reflectivity = Reflectivity
                };
                return true;
            }
        }
        return false;
    }
}

struct HitRecord
{
    public double T;
    public Vec3 Point;
    public Vec3 Normal;
    public Vec3 Color;
    public double Reflectivity;
}

struct Vec3
{
    public double X, Y, Z;
    public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
    public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(double t, Vec3 v) => new Vec3(t * v.X, t * v.Y, t * v.Z);
    public static Vec3 operator *(Vec3 v, double t) => t * v;
    public static Vec3 operator /(Vec3 v, double t) => new Vec3(v.X / t, v.Y / t, v.Z / t);
    public static Vec3 operator *(Vec3 a, Vec3 b) => new Vec3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static Vec3 operator /(Vec3 a, Vec3 b) => new Vec3(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vec3 UnitVector(Vec3 v) => v / Math.Sqrt(Dot(v, v));
    public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
    public static Vec3 FromNumerics(System.Numerics.Vector3 v) => new Vec3(v.X, v.Y, v.Z);
    public System.Numerics.Vector3 ToNumerics() => new System.Numerics.Vector3((float)X, (float)Y, (float)Z);
    public Vec3(System.Numerics.Vector3 v) { X = v.X; Y = v.Y; Z = v.Z; }
}

struct Ray
{
    public Vec3 Origin, Direction;
    public Ray(Vec3 origin, Vec3 direction) { Origin = origin; Direction = direction; }
    public Vec3 At(double t) => Origin + t * Direction;
}
