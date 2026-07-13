using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;

namespace _Morpho4D
{
    /// <summary>
    /// Loads component icons that are embedded in the assembly under the
    /// "Resources" folder. Icons are looked up by the file name (without the
    /// ".png" extension) and cached after the first load.
    /// </summary>
    internal static class IconLoader
    {
        private static readonly ConcurrentDictionary<string, Bitmap> _cache =
            new ConcurrentDictionary<string, Bitmap>();

        private static readonly Assembly _assembly = typeof(IconLoader).Assembly;

        /// <summary>
        /// Returns the embedded icon with the given name (e.g. "Anchor"),
        /// or <c>null</c> if it cannot be found.
        /// </summary>
        public static Bitmap Get(string name)
        {
            return _cache.GetOrAdd(name, key =>
            {
                // The manifest name is "<RootNamespace>.Resources.<key>.png".
                // Match on the suffix so we are independent of the root namespace.
                string suffix = ".Resources." + key + ".png";
                string resourceName = Array.Find(
                    _assembly.GetManifestResourceNames(),
                    n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                    return null;

                using (var stream = _assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;

                    return new Bitmap(stream);
                }
            });
        }
    }
}
