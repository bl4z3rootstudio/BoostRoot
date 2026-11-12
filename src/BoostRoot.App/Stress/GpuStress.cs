using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using BoostRoot.Core.Stress;
using static Avalonia.OpenGL.GlConsts;

namespace BoostRoot.App.Stress;

/// <summary>Heavy ray-marching fragment shader drawn every frame in its own window.
/// Works on any vendor (ANGLE/D3D11 on Windows). A lost GL context counts as an error (driver reset).</summary>
public sealed class GpuStressControl : OpenGlControlBase
{
    private int _program, _vbo, _vao, _timeLoc;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    public long Frames;
    public long Lost;
    public bool Failed;

    private const string Vs = """
        attribute vec2 aPos;
        varying vec2 vUv;
        void main() { vUv = aPos * 0.5 + 0.5; gl_Position = vec4(aPos, 0.0, 1.0); }
        """;

    private const string Fs = """
        #ifdef GL_ES
        precision highp float;
        #endif
        varying vec2 vUv;
        uniform float uTime;
        float map(vec3 p) {
            vec3 q = fract(p) - 0.5;
            float d = length(q) - 0.28;
            for (int i = 0; i < 6; i++) { p = abs(p) / dot(p, p) - 0.75; d = min(d, length(p.xy) * 0.06); }
            return d;
        }
        void main() {
            vec2 uv = vUv * 2.0 - 1.0;
            vec3 ro = vec3(0.0, 0.0, uTime * 0.4), rd = normalize(vec3(uv, 1.3));
            float t = 0.0; vec3 col = vec3(0.0);
            for (int i = 0; i < 160; i++) {
                float d = map(ro + rd * t);
                col += vec3(0.66, 0.33, 0.97) * 0.012 / (0.02 + abs(d));
                t += max(abs(d) * 0.5, 0.004);
            }
            gl_FragColor = vec4(col * 0.08, 1.0);
        }
        """;

    private string Header() => GlVersion.Type == GlProfileType.OpenGLES ? "#version 100\n" : "#version 120\n";

    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        var vs = gl.CreateShader(GL_VERTEX_SHADER);
        var fs = gl.CreateShader(GL_FRAGMENT_SHADER);
        if (gl.CompileShaderAndGetError(vs, Header() + Vs) is not null || gl.CompileShaderAndGetError(fs, Header() + Fs) is not null) { Failed = true; return; }
        _program = gl.CreateProgram();
        gl.AttachShader(_program, vs);
        gl.AttachShader(_program, fs);
        gl.BindAttribLocationString(_program, 0, "aPos");
        if (gl.LinkProgramAndGetError(_program) is not null) { Failed = true; return; }
        _timeLoc = gl.GetUniformLocationString(_program, "uTime");
        float[] quad = [-1, -1, 1, -1, -1, 1, 1, 1];
        _vbo = gl.GenBuffer();
        gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
        fixed (float* p = quad) gl.BufferData(GL_ARRAY_BUFFER, quad.Length * 4, (nint)p, GL_STATIC_DRAW);
        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);
        gl.VertexAttribPointer(0, 2, GL_FLOAT, 0, 8, 0);
        gl.EnableVertexAttribArray(0);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!Failed)
        {
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            gl.Viewport(0, 0, (int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
            gl.UseProgram(_program);
            gl.Uniform1f(_timeLoc, (float)_clock.Elapsed.TotalSeconds);
            gl.BindVertexArray(_vao);
            gl.DrawArrays(0x0005 /* GL_TRIANGLE_STRIP */, 0, 4);
            Interlocked.Increment(ref Frames);
        }
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlLost() { Interlocked.Increment(ref Lost); base.OnOpenGlLost(); }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        if (_vbo != 0) gl.DeleteBuffer(_vbo);
        if (_vao != 0) gl.DeleteVertexArray(_vao);
        if (_program != 0) gl.DeleteProgram(_program);
    }
}

public sealed class GpuStressWorkload(string title) : IStressWorkload
{
    private GpuStressControl? _control;
    public StressKind Kind => StressKind.Gpu;
    public long Work => _control is null ? 0 : Interlocked.Read(ref _control.Frames);
    public long Errors => _control is null ? 0 : Interlocked.Read(ref _control.Lost);

    public Task RunAsync(CancellationToken ct)
    {
        var done = new TaskCompletionSource();
        Dispatcher.UIThread.Post(() =>
        {
            _control = new GpuStressControl();
            var w = new Window
            {
                Title = title, Width = 1280, Height = 720, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = _control, Background = Avalonia.Media.Brushes.Black,
            };
            w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
            w.Closed += (_, _) => done.TrySetResult();
            ct.Register(() => Dispatcher.UIThread.Post(w.Close));
            w.Show();
        });
        return done.Task;
    }
}
