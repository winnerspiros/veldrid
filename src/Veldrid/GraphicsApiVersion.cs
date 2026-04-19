namespace Veldrid
{
    /// <summary>
    ///     Represents a graphics API version with major, minor, subminor, and patch components.
    /// </summary>
    public readonly struct GraphicsApiVersion
    {
        /// <summary>
        ///     Gets an unknown (zeroed) version.
        /// </summary>
        public static GraphicsApiVersion Unknown => default;

        /// <summary>
        ///     Gets the major version component.
        /// </summary>
        public int Major { get; }

        /// <summary>
        ///     Gets the minor version component.
        /// </summary>
        public int Minor { get; }

        /// <summary>
        ///     Gets the subminor version component.
        /// </summary>
        public int Subminor { get; }

        /// <summary>
        ///     Gets the patch version component.
        /// </summary>
        public int Patch { get; }

        /// <summary>
        ///     Gets a value indicating whether this version is known (non-default).
        /// </summary>
        public bool IsKnown => Major != 0 && Minor != 0 && Subminor != 0 && Patch != 0;

        /// <summary>
        ///     Creates a new <see cref="GraphicsApiVersion" /> with the given components.
        /// </summary>
        /// <param name="major">The major version component.</param>
        /// <param name="minor">The minor version component.</param>
        /// <param name="subminor">The subminor version component.</param>
        /// <param name="patch">The patch version component.</param>
        public GraphicsApiVersion(int major, int minor, int subminor, int patch)
        {
            Major = major;
            Minor = minor;
            Subminor = subminor;
            Patch = patch;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{Major}.{Minor}.{Subminor}.{Patch}";
        }

        /// <summary>
        ///     Parses OpenGL version strings with either of following formats:
        ///     <list type="bullet">
        ///         <item>
        ///             <description>major_number.minor_number</description>
        ///         </item>
        ///         <item>
        ///             <description>major_number.minor_number.release_number</description>
        ///         </item>
        ///     </list>
        /// </summary>
        /// <param name="versionString">The OpenGL version string.</param>
        /// <param name="version">The parsed <see cref="GraphicsApiVersion" />.</param>
        /// <returns>True whether the parse succeeded; otherwise false.</returns>
        public static bool TryParseGLVersion(string versionString, out GraphicsApiVersion version)
        {
            string[] versionParts = versionString.Split(' ')[0].Split('.');

            if (!int.TryParse(versionParts[0], out int major) ||
                !int.TryParse(versionParts[1], out int minor))
            {
                version = default;
                return false;
            }

            int releaseNumber = 0;

            if (versionParts.Length == 3)
            {
                if (!int.TryParse(versionParts[2], out releaseNumber))
                {
                    version = default;
                    return false;
                }
            }

            version = new GraphicsApiVersion(major, minor, 0, releaseNumber);
            return true;
        }
    }
}
