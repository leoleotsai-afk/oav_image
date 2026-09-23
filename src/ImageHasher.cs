using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;

namespace ImageSearch
{
    // A perceptual hash made of four 64-bit "difference hash" (dHash) planes -
    // luminance, red, green and blue - so that both the shape/lighting layout
    // AND the colors of a picture affect the result. Two images that share the
    // same silhouette but differ in color (e.g. a red part vs. a blue part)
    // will end up with a large Hamming distance instead of looking identical.
    public struct PerceptualHash
    {
        public ulong Y;
        public ulong R;
        public ulong G;
        public ulong B;

        public string ToHex()
        {
            return Y.ToString("X16") + R.ToString("X16") + G.ToString("X16") + B.ToString("X16");
        }

        public static PerceptualHash FromHex(string hex)
        {
            PerceptualHash h = new PerceptualHash();
            if (string.IsNullOrEmpty(hex) || hex.Length < 64)
            {
                return h;
            }
            h.Y = Convert.ToUInt64(hex.Substring(0, 16), 16);
            h.R = Convert.ToUInt64(hex.Substring(16, 16), 16);
            h.G = Convert.ToUInt64(hex.Substring(32, 16), 16);
            h.B = Convert.ToUInt64(hex.Substring(48, 16), 16);
            return h;
        }
    }

    public static class ImageHasher
    {
        public static PerceptualHash ComputeHash(Image source)
        {
            using (Bitmap small = new Bitmap(9, 8))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(source, 0, 0, 9, 8);
                }

                PerceptualHash hash = new PerceptualHash();
                int bit = 0;
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        Color left = small.GetPixel(x, y);
                        Color right = small.GetPixel(x + 1, y);

                        if (Luminance(left) > Luminance(right)) hash.Y |= (1UL << bit);
                        if (left.R > right.R) hash.R |= (1UL << bit);
                        if (left.G > right.G) hash.G |= (1UL << bit);
                        if (left.B > right.B) hash.B |= (1UL << bit);

                        bit++;
                    }
                }
                return hash;
            }
        }

        // Loads the image from bytes (not from the locked file handle) so the source
        // file is never held open, and returns its perceptual hash.
        public static PerceptualHash ComputeHashFromFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                using (Image img = Image.FromStream(ms))
                {
                    return ComputeHash(img);
                }
            }
        }

        private static double Luminance(Color c)
        {
            return 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        }

        private static int HammingDistance(ulong a, ulong b)
        {
            ulong x = a ^ b;
            int count = 0;
            while (x != 0)
            {
                count++;
                x = x & (x - 1);
            }
            return count;
        }

        public static int HammingDistance(PerceptualHash a, PerceptualHash b)
        {
            return HammingDistance(a.Y, b.Y) + HammingDistance(a.R, b.R)
                 + HammingDistance(a.G, b.G) + HammingDistance(a.B, b.B);
        }

        // 1.0 = identical, 0.0 = maximally different (all 256 bits differ).
        public static double Similarity(PerceptualHash a, PerceptualHash b)
        {
            return 1.0 - (HammingDistance(a, b) / 256.0);
        }
    }
}
