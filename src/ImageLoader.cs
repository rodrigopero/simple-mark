using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace SimpleMark
{
    /// <summary>
    /// Loads local images referenced by a document and converts them to RTF pictures.
    /// Remote URLs, network/UNC paths, devices and alternate data streams are refused,
    /// and every image is re-encoded so only clean pixel data reaches the viewer.
    /// </summary>
    internal static class ImageLoader
    {
        private const long MaxImageBytes = 20L * 1024 * 1024;
        private const int MaxDimension = 12000;
        private const long JpegQuality = 90L;
        private static readonly string[] AllowedExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };

        public static string TryLoadRtf(string target, string baseDirectory, int maxWidthPx, float dpi)
        {
            string path = ResolveLocalPath(target, baseDirectory);
            if (path == null) return null;

            try
            {
                byte[] bytes = SafeFile.ReadBounded(path, MaxImageBytes);
                using (var input = new MemoryStream(bytes))
                using (Image source = Image.FromStream(input, false, true))
                {
                    if (source.Width <= 0 || source.Height <= 0 || source.Width > MaxDimension || source.Height > MaxDimension)
                    {
                        return null;
                    }
                    return Encode(source, maxWidthPx, dpi);
                }
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException
                    || ex is ArgumentException || ex is OutOfMemoryException || ex is ExternalException)
                {
                    return null;
                }
                throw;
            }
        }

        private static string ResolveLocalPath(string target, string baseDirectory)
        {
            if (baseDirectory == null || target == null) return null;

            string value = target.Trim();
            try
            {
                value = Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return null;
            }

            if (value.Length == 0
                || value.IndexOf("://", StringComparison.Ordinal) >= 0
                || value.StartsWith("//", StringComparison.Ordinal)
                || value.StartsWith("\\\\", StringComparison.Ordinal)
                || (value.Length > 2 && value.IndexOf(':', 2) >= 0)
                || (value.IndexOf(':') == 0)
                || (value.IndexOf(':') == 1 && !char.IsLetter(value[0])))
            {
                return null;
            }

            try
            {
                string fullPath = Path.GetFullPath(Path.Combine(baseDirectory, value.Replace('/', '\\')));
                if (fullPath.StartsWith("\\\\", StringComparison.Ordinal)) return null;

                string extension = Path.GetExtension(fullPath).ToLowerInvariant();
                if (Array.IndexOf(AllowedExtensions, extension) < 0) return null;

                var drive = new DriveInfo(Path.GetPathRoot(fullPath));
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) return null;

                var info = new FileInfo(fullPath);
                if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0) return null;

                return fullPath;
            }
            catch (Exception ex)
            {
                if (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException
                    || ex is ArgumentException || ex is NotSupportedException)
                {
                    return null;
                }
                throw;
            }
        }

        private static string Encode(Image source, int maxWidthPx, float dpi)
        {
            double scale = Math.Min(1.0, maxWidthPx / (double)source.Width);
            int width = Math.Max(1, (int)(source.Width * scale));
            int height = Math.Max(1, (int)(source.Height * scale));
            bool jpeg = ImageFormat.Jpeg.Equals(source.RawFormat);

            using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (var output = new MemoryStream())
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    if (jpeg) g.Clear(Color.White);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(source, 0, 0, width, height);
                }

                if (jpeg) SaveJpeg(bitmap, output);
                else bitmap.Save(output, ImageFormat.Png);

                var rtf = new StringBuilder((int)output.Length * 2 + 128);
                rtf.Append("{\\pict\\").Append(jpeg ? "jpegblip" : "pngblip")
                   .Append("\\picw").Append(width).Append("\\pich").Append(height)
                   .Append("\\picwgoal").Append(ToTwips(width, dpi))
                   .Append("\\pichgoal").Append(ToTwips(height, dpi)).Append('\n');
                AppendHex(rtf, output.GetBuffer(), (int)output.Length);
                rtf.Append('}');
                return rtf.ToString();
            }
        }

        private static void SaveJpeg(Bitmap bitmap, Stream output)
        {
            ImageCodecInfo codec = null;
            foreach (ImageCodecInfo candidate in ImageCodecInfo.GetImageEncoders())
            {
                if (candidate.FormatID == ImageFormat.Jpeg.Guid) codec = candidate;
            }
            if (codec == null)
            {
                bitmap.Save(output, ImageFormat.Jpeg);
                return;
            }

            using (var parameters = new EncoderParameters(1))
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
                bitmap.Save(output, codec, parameters);
            }
        }

        private static int ToTwips(int pixels, float dpi)
        {
            return (int)Math.Round(pixels * 1440.0 / dpi);
        }

        private static void AppendHex(StringBuilder sb, byte[] data, int length)
        {
            const string digits = "0123456789abcdef";
            for (int i = 0; i < length; i++)
            {
                sb.Append(digits[data[i] >> 4]).Append(digits[data[i] & 0xF]);
                if (i % 64 == 63) sb.Append('\n');
            }
        }
    }
}
