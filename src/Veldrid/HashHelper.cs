using System;

namespace Veldrid
{
    internal static class HashHelper
    {
        public static int Combine(int h1, int h2) => HashCode.Combine(h1, h2);
        public static int Combine(int h1, int h2, int h3) => HashCode.Combine(h1, h2, h3);
        public static int Combine(int h1, int h2, int h3, int h4) => HashCode.Combine(h1, h2, h3, h4);
        public static int Combine(int h1, int h2, int h3, int h4, int h5) => HashCode.Combine(h1, h2, h3, h4, h5);
        public static int Combine(int h1, int h2, int h3, int h4, int h5, int h6) => HashCode.Combine(h1, h2, h3, h4, h5, h6);
        public static int Combine(int h1, int h2, int h3, int h4, int h5, int h6, int h7) => HashCode.Combine(h1, h2, h3, h4, h5, h6, h7);
        public static int Combine(int h1, int h2, int h3, int h4, int h5, int h6, int h7, int h8) => HashCode.Combine(h1, h2, h3, h4, h5, h6, h7, h8);

        public static int Combine(int h1, int h2, int h3, int h4, int h5, int h6, int h7, int h8, int h9)
        {
            var hc = new HashCode();
            hc.Add(h1); hc.Add(h2); hc.Add(h3); hc.Add(h4); hc.Add(h5);
            hc.Add(h6); hc.Add(h7); hc.Add(h8); hc.Add(h9);
            return hc.ToHashCode();
        }

        public static int Combine(int h1, int h2, int h3, int h4, int h5, int h6, int h7, int h8, int h9, int h10)
        {
            var hc = new HashCode();
            hc.Add(h1); hc.Add(h2); hc.Add(h3); hc.Add(h4); hc.Add(h5);
            hc.Add(h6); hc.Add(h7); hc.Add(h8); hc.Add(h9); hc.Add(h10);
            return hc.ToHashCode();
        }

        public static int Array<T>(T[] items)
        {
            if (items is null || items.Length == 0) return 0;

            var hc = new HashCode();
            foreach (var item in items) hc.Add(item);
            return hc.ToHashCode();
        }
    }
}
