using System;

namespace Veldrid.OpenGL
{
    /// <summary>
    ///     A table of raw OpenGL function pointers, populated by <see cref="OpenGLGraphicsDevice" /> during device
    ///     initialisation from the <see cref="OpenGLPlatformInfo.GetProcAddress" /> delegate supplied by the host.
    ///     Callers obtain an instance via <see cref="OpenGLGraphicsDevice.GetGLProcTable" /> (or the
    ///     <see cref="GraphicsDevice.GetGLProcTable" /> virtual on the base class, which returns <c>null</c> for
    ///     non-GL backends).
    ///
    ///     <para>
    ///         Every field is a C# 9 unmanaged function pointer (<c>delegate* unmanaged&lt;…&gt;</c>) whose
    ///         parameter and return types use only primitive blittable types so that callers have no compile-time
    ///         dependency on <c>Veldrid.OpenGLBindings</c>.  All GL enumerants are passed as plain
    ///         <see cref="uint" /> values.
    ///     </para>
    ///
    ///     <para>
    ///         Fields for optional or version-gated extensions (e.g. <see cref="InvalidateFramebuffer" />,
    ///         <see cref="ShaderStorageBlockBinding" />) may be <c>null</c> when the driver does not expose
    ///         the function.  Callers must check for <c>null</c> before invoking.
    ///     </para>
    ///
    ///     <para>
    ///         The calling convention used for every pointer matches <see cref="System.Runtime.InteropServices.CallingConvention.Winapi" />:
    ///         the platform default (Cdecl on POSIX/64-bit Windows; Stdcall on 32-bit Windows).
    ///         This is identical to the convention used by <c>Veldrid.OpenGLBindings.OpenGLNative</c>.
    ///     </para>
    /// </summary>
    public unsafe struct OpenGLProcTable
    {
        // ── Texture units ────────────────────────────────────────────────────
        /// <summary>glActiveTexture(GLenum texture)</summary>
        public delegate* unmanaged<uint, void> ActiveTexture;

        /// <summary>glBindTexture(GLenum target, GLuint texture)</summary>
        public delegate* unmanaged<uint, uint, void> BindTexture;

        /// <summary>glGenTextures(GLsizei n, GLuint* textures)</summary>
        public delegate* unmanaged<int, uint*, void> GenTextures;

        /// <summary>glDeleteTextures(GLsizei n, const GLuint* textures)</summary>
        public delegate* unmanaged<int, uint*, void> DeleteTextures;

        /// <summary>glTexImage2D(GLenum target, GLint level, GLint internalformat, GLsizei width, GLsizei height, GLint border, GLenum format, GLenum type, const void* pixels)</summary>
        public delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> TexImage2D;

        /// <summary>glTexSubImage2D(GLenum target, GLint level, GLint xoffset, GLint yoffset, GLsizei width, GLsizei height, GLenum format, GLenum type, const void* pixels)</summary>
        public delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> TexSubImage2D;

        /// <summary>glTexParameteri(GLenum target, GLenum pname, GLint param)</summary>
        public delegate* unmanaged<uint, uint, int, void> TexParameteri;

        /// <summary>glGenerateMipmap(GLenum target)</summary>
        public delegate* unmanaged<uint, void> GenerateMipmap;

        // ── Framebuffers ─────────────────────────────────────────────────────
        /// <summary>glBindFramebuffer(GLenum target, GLuint framebuffer)</summary>
        public delegate* unmanaged<uint, uint, void> BindFramebuffer;

        /// <summary>glGenFramebuffers(GLsizei n, GLuint* ids)</summary>
        public delegate* unmanaged<int, uint*, void> GenFramebuffers;

        /// <summary>glDeleteFramebuffers(GLsizei n, const GLuint* framebuffers)</summary>
        public delegate* unmanaged<int, uint*, void> DeleteFramebuffers;

        /// <summary>glFramebufferTexture2D(GLenum target, GLenum attachment, GLenum textarget, GLuint texture, GLint level)</summary>
        public delegate* unmanaged<uint, uint, uint, uint, int, void> FramebufferTexture2D;

        /// <summary>glFramebufferRenderbuffer(GLenum target, GLenum attachment, GLenum renderbuffertarget, GLuint renderbuffer)</summary>
        public delegate* unmanaged<uint, uint, uint, uint, void> FramebufferRenderbuffer;

        /// <summary>glInvalidateFramebuffer(GLenum target, GLsizei numAttachments, const GLenum* attachments) — core in GL 4.3 / GLES 3.0; may be null on older contexts.</summary>
        public delegate* unmanaged<uint, int, uint*, void> InvalidateFramebuffer;

        // ── Renderbuffers ────────────────────────────────────────────────────
        /// <summary>glBindRenderbuffer(GLenum target, GLuint renderbuffer)</summary>
        public delegate* unmanaged<uint, uint, void> BindRenderbuffer;

        /// <summary>glGenRenderbuffers(GLsizei n, GLuint* renderbuffers)</summary>
        public delegate* unmanaged<int, uint*, void> GenRenderbuffers;

        /// <summary>glDeleteRenderbuffers(GLsizei n, const GLuint* renderbuffers)</summary>
        public delegate* unmanaged<int, uint*, void> DeleteRenderbuffers;

        /// <summary>glRenderbufferStorage(GLenum target, GLenum internalformat, GLsizei width, GLsizei height)</summary>
        public delegate* unmanaged<uint, uint, int, int, void> RenderbufferStorage;

        // ── Vertex arrays ────────────────────────────────────────────────────
        /// <summary>glBindVertexArray(GLuint array)</summary>
        public delegate* unmanaged<uint, void> BindVertexArray;

        /// <summary>glGenVertexArrays(GLsizei n, GLuint* arrays)</summary>
        public delegate* unmanaged<int, uint*, void> GenVertexArrays;

        /// <summary>glDeleteVertexArrays(GLsizei n, const GLuint* arrays)</summary>
        public delegate* unmanaged<int, uint*, void> DeleteVertexArrays;

        /// <summary>glEnableVertexAttribArray(GLuint index)</summary>
        public delegate* unmanaged<uint, void> EnableVertexAttribArray;

        /// <summary>glVertexAttribPointer(GLuint index, GLint size, GLenum type, GLboolean normalized, GLsizei stride, const void* pointer)</summary>
        public delegate* unmanaged<uint, int, uint, byte, uint, void*, void> VertexAttribPointer;

        /// <summary>glVertexAttribIPointer(GLuint index, GLint size, GLenum type, GLsizei stride, const void* pointer)</summary>
        public delegate* unmanaged<uint, int, uint, uint, void*, void> VertexAttribIPointer;

        // ── Buffers ──────────────────────────────────────────────────────────
        /// <summary>glBindBuffer(GLenum target, GLuint buffer)</summary>
        public delegate* unmanaged<uint, uint, void> BindBuffer;

        /// <summary>glBindBufferBase(GLenum target, GLuint index, GLuint buffer)</summary>
        public delegate* unmanaged<uint, uint, uint, void> BindBufferBase;

        /// <summary>glGenBuffers(GLsizei n, GLuint* buffers)</summary>
        public delegate* unmanaged<int, uint*, void> GenBuffers;

        /// <summary>glDeleteBuffers(GLsizei n, const GLuint* buffers)</summary>
        public delegate* unmanaged<int, uint*, void> DeleteBuffers;

        /// <summary>glBufferData(GLenum target, GLsizeiptr size, const void* data, GLenum usage)</summary>
        public delegate* unmanaged<uint, UIntPtr, void*, uint, void> BufferData;

        /// <summary>glBufferSubData(GLenum target, GLintptr offset, GLsizeiptr size, const void* data)</summary>
        public delegate* unmanaged<uint, IntPtr, UIntPtr, void*, void> BufferSubData;

        // ── Shaders ──────────────────────────────────────────────────────────
        /// <summary>glCreateShader(GLenum type) → GLuint</summary>
        public delegate* unmanaged<uint, uint> CreateShader;

        /// <summary>glShaderSource(GLuint shader, GLsizei count, const GLchar** string, const GLint* length)</summary>
        public delegate* unmanaged<uint, int, byte**, int*, void> ShaderSource;

        /// <summary>glCompileShader(GLuint shader)</summary>
        public delegate* unmanaged<uint, void> CompileShader;

        /// <summary>glGetShaderiv(GLuint shader, GLenum pname, GLint* params)</summary>
        public delegate* unmanaged<uint, uint, int*, void> GetShaderiv;

        /// <summary>glGetShaderInfoLog(GLuint shader, GLsizei maxLength, GLsizei* length, GLchar* infoLog)</summary>
        public delegate* unmanaged<uint, int, int*, byte*, void> GetShaderInfoLog;

        /// <summary>glDeleteShader(GLuint shader)</summary>
        public delegate* unmanaged<uint, void> DeleteShader;

        // ── Programs ─────────────────────────────────────────────────────────
        /// <summary>glCreateProgram() → GLuint</summary>
        public delegate* unmanaged<uint> CreateProgram;

        /// <summary>glAttachShader(GLuint program, GLuint shader)</summary>
        public delegate* unmanaged<uint, uint, void> AttachShader;

        /// <summary>glDetachShader(GLuint program, GLuint shader)</summary>
        public delegate* unmanaged<uint, uint, void> DetachShader;

        /// <summary>glLinkProgram(GLuint program)</summary>
        public delegate* unmanaged<uint, void> LinkProgram;

        /// <summary>glGetProgramiv(GLuint program, GLenum pname, GLint* params)</summary>
        public delegate* unmanaged<uint, uint, int*, void> GetProgramiv;

        /// <summary>glGetProgramInfoLog(GLuint program, GLsizei maxLength, GLsizei* length, GLchar* infoLog)</summary>
        public delegate* unmanaged<uint, int, int*, byte*, void> GetProgramInfoLog;

        /// <summary>glDeleteProgram(GLuint program)</summary>
        public delegate* unmanaged<uint, void> DeleteProgram;

        /// <summary>glUseProgram(GLuint program)</summary>
        public delegate* unmanaged<uint, void> UseProgram;

        // ── Uniforms ─────────────────────────────────────────────────────────
        /// <summary>glGetUniformLocation(GLuint program, const GLchar* name) → GLint</summary>
        public delegate* unmanaged<uint, byte*, int> GetUniformLocation;

        /// <summary>glGetUniformBlockIndex(GLuint program, const GLchar* uniformBlockName) → GLuint</summary>
        public delegate* unmanaged<uint, byte*, uint> GetUniformBlockIndex;

        /// <summary>glUniformBlockBinding(GLuint program, GLuint uniformBlockIndex, GLuint uniformBlockBinding)</summary>
        public delegate* unmanaged<uint, uint, uint, void> UniformBlockBinding;

        /// <summary>glUniform1i(GLint location, GLint v0)</summary>
        public delegate* unmanaged<int, int, void> Uniform1i;

        /// <summary>glUniform1f(GLint location, GLfloat v0)</summary>
        public delegate* unmanaged<int, float, void> Uniform1f;

        /// <summary>glUniformMatrix3fv(GLint location, GLsizei count, GLboolean transpose, const GLfloat* value)</summary>
        public delegate* unmanaged<int, int, byte, float*, void> UniformMatrix3fv;

        /// <summary>glUniformMatrix4fv(GLint location, GLsizei count, GLboolean transpose, const GLfloat* value)</summary>
        public delegate* unmanaged<int, int, byte, float*, void> UniformMatrix4fv;

        // ── Shader storage blocks (GL 4.3+ / GLES 3.1+) ─────────────────────
        /// <summary>glShaderStorageBlockBinding(GLuint program, GLuint storageBlockIndex, GLuint storageBlockBinding) — may be null when compute / SSBO is unsupported.</summary>
        public delegate* unmanaged<uint, uint, uint, void> ShaderStorageBlockBinding;

        /// <summary>glGetProgramResourceIndex(GLuint program, GLenum programInterface, const GLchar* name) → GLuint — may be null on older contexts.</summary>
        public delegate* unmanaged<uint, uint, byte*, uint> GetProgramResourceIndex;

        // ── Draw calls ───────────────────────────────────────────────────────
        /// <summary>glDrawElements(GLenum mode, GLsizei count, GLenum type, const void* indices)</summary>
        public delegate* unmanaged<uint, int, uint, void*, void> DrawElements;

        // ── Clear ────────────────────────────────────────────────────────────
        /// <summary>glClear(GLbitfield mask)</summary>
        public delegate* unmanaged<uint, void> Clear;

        /// <summary>glClearColor(GLfloat red, GLfloat green, GLfloat blue, GLfloat alpha)</summary>
        public delegate* unmanaged<float, float, float, float, void> ClearColor;

        /// <summary>glClearDepth(GLdouble depth) — desktop GL only; prefer <see cref="ClearDepthF" /> on GLES.</summary>
        public delegate* unmanaged<double, void> ClearDepth;

        /// <summary>glClearDepthf(GLfloat depth) — GLES / GL 4.1+.</summary>
        public delegate* unmanaged<float, void> ClearDepthF;

        // ── Render state ─────────────────────────────────────────────────────
        /// <summary>glEnable(GLenum cap)</summary>
        public delegate* unmanaged<uint, void> Enable;

        /// <summary>glDisable(GLenum cap)</summary>
        public delegate* unmanaged<uint, void> Disable;

        /// <summary>glColorMask(GLboolean red, GLboolean green, GLboolean blue, GLboolean alpha)</summary>
        public delegate* unmanaged<byte, byte, byte, byte, void> ColorMask;

        /// <summary>glDepthFunc(GLenum func)</summary>
        public delegate* unmanaged<uint, void> DepthFunc;

        /// <summary>glDepthMask(GLboolean flag)</summary>
        public delegate* unmanaged<byte, void> DepthMask;

        /// <summary>glBlendFuncSeparate(GLenum srcRGB, GLenum dstRGB, GLenum srcAlpha, GLenum dstAlpha)</summary>
        public delegate* unmanaged<uint, uint, uint, uint, void> BlendFuncSeparate;

        /// <summary>glBlendEquationSeparate(GLenum modeRGB, GLenum modeAlpha)</summary>
        public delegate* unmanaged<uint, uint, void> BlendEquationSeparate;

        /// <summary>glStencilFunc(GLenum func, GLint ref, GLuint mask)</summary>
        public delegate* unmanaged<uint, int, uint, void> StencilFunc;

        /// <summary>glStencilOp(GLenum sfail, GLenum dpfail, GLenum dppass)</summary>
        public delegate* unmanaged<uint, uint, uint, void> StencilOp;

        // ── Viewport / scissor ───────────────────────────────────────────────
        /// <summary>glViewport(GLint x, GLint y, GLsizei width, GLsizei height)</summary>
        public delegate* unmanaged<int, int, int, int, void> Viewport;

        /// <summary>glScissor(GLint x, GLint y, GLsizei width, GLsizei height)</summary>
        public delegate* unmanaged<int, int, int, int, void> Scissor;

        // ── Pixel transfer ───────────────────────────────────────────────────
        /// <summary>glPixelStorei(GLenum pname, GLint param)</summary>
        public delegate* unmanaged<uint, int, void> PixelStorei;

        /// <summary>glReadPixels(GLint x, GLint y, GLsizei width, GLsizei height, GLenum format, GLenum type, void* data)</summary>
        public delegate* unmanaged<int, int, int, int, uint, uint, void*, void> ReadPixels;

        // ── Queries ──────────────────────────────────────────────────────────
        /// <summary>glGetIntegerv(GLenum pname, GLint* data)</summary>
        public delegate* unmanaged<uint, int*, void> GetIntegerv;

        /// <summary>glGetString(GLenum name) → const GLubyte*</summary>
        public delegate* unmanaged<uint, byte*> GetString;

        // ── Hints ────────────────────────────────────────────────────────────
        /// <summary>glHint(GLenum target, GLenum mode)</summary>
        public delegate* unmanaged<uint, uint, void> Hint;

        // ── Sync ─────────────────────────────────────────────────────────────
        /// <summary>glFinish()</summary>
        public delegate* unmanaged<void> Finish;
    }
}
