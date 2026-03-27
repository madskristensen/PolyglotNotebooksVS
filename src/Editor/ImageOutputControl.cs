using Microsoft.VisualStudio.Shell;
using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PolyglotNotebooks.Editor
{
    /// <summary>
    /// Factory that creates WPF UIElements for image MIME types.
    /// Raster images (PNG, JPEG, GIF, BMP) are decoded from base64 and shown in a WPF Image.
    /// SVG is rendered inside a <see cref="WebView2OutputHost"/> (inline SVG in HTML).
    /// </summary>
    internal static class ImageOutputControl
    {
        /// <summary>
        /// Creates a UIElement that displays the image described by <paramref name="mimeType"/>
        /// and <paramref name="base64OrRawData"/>.
        /// </summary>
        public static UIElement Create(string mimeType, string base64OrRawData)
        {
            if (string.Equals(mimeType, "image/svg+xml", StringComparison.OrdinalIgnoreCase))
                return CreateSvgElement(base64OrRawData);

            return CreateRasterElement(base64OrRawData);
        }

        // -----------------------------------------------------------------------
        // Raster (PNG / JPEG / GIF / BMP)
        // -----------------------------------------------------------------------

        private static UIElement CreateRasterElement(string base64Data)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(StripDataUri(base64Data));

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = new System.IO.MemoryStream(bytes);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                return new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    MaxWidth = 800,
                    Margin = new Thickness(4),
                };
            }
            catch
            {
                return Fallback("[Image — failed to decode base64 data]");
            }
        }

        // -----------------------------------------------------------------------
        // SVG via WebView2
        // -----------------------------------------------------------------------

        private static UIElement CreateSvgElement(string svgData)
        {
            try
            {
                string svgContent;

                // SVG value may arrive as raw XML or base64-encoded bytes.
                var trimmed = svgData.TrimStart();
                if (trimmed.StartsWith("<"))
                {
                    svgContent = svgData;
                }
                else
                {
                    // Decode base64 → UTF-8 SVG text
                    byte[] bytes = Convert.FromBase64String(StripDataUri(svgData));
                    svgContent = Encoding.UTF8.GetString(bytes);
                }

                var host = new WebView2OutputHost();
                host.SetHtmlContent(svgContent);
                return host;
            }
            catch
            {
                return Fallback("[SVG — failed to render]");
            }
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static string StripDataUri(string data)
        {
            int commaIdx = data.IndexOf(',');
            return commaIdx >= 0 ? data.Substring(commaIdx + 1) : data;
        }

        private static UIElement Fallback(string message)
        {
            var tb = new TextBlock
            {
                Text = message,
                FontStyle = FontStyles.Italic,
                Padding = new Thickness(4),
                FontSize = 12,
            };
            tb.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            return tb;
        }
    }
}
