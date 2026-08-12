// Path: BA\Materials\UI\MaterialPreviewHost.xaml.cs
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using BA.BAApplication;
using BA.Materials.Models;

namespace BA.Materials.UI
{
    /// <summary>
    /// Hosts the Three.js sphere preview via WebView2, falling back to a flat color
    /// swatch if the WebView2 Evergreen Runtime is not installed on the machine. Falls
    /// back silently at first paint (no exception dialog), the fallback UI itself tells
    /// the user what happened, per the agreed "soft fallback, inform about install" behavior.
    ///
    /// Virtual host mapping points at a folder that must contain preview.html and
    /// three.min.js (r128) as Content, Copy to Output Directory. This avoids any runtime
    /// dependency on outbound internet access from inside Revit's process.
    /// </summary>
    public partial class MaterialPreviewHost : UserControl
    {
        private const string VirtualHostName = "ba.materialpreview";
        private bool _isWebViewReady;

        public MaterialPreviewHost()
        {
            InitializeComponent();
            Loaded += MaterialPreviewHost_Loaded;
        }

        private async void MaterialPreviewHost_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync();
                await PreviewWebView.EnsureCoreWebView2Async(environment);

                string previewFolder = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "Materials", "UI", "Resources", "MaterialPreview");

                if (!Directory.Exists(previewFolder))
                {
                    AppLogger.LogInfo($"BA.Materials: preview folder not found at '{previewFolder}', falling back to flat swatch.");
                    ShowFallback();
                    return;
                }

                PreviewWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHostName, previewFolder, CoreWebView2HostResourceAccessKind.Allow);

                PreviewWebView.CoreWebView2.Navigate($"https://{VirtualHostName}/preview.html");

                PreviewWebView.Visibility = System.Windows.Visibility.Visible;
                FallbackPanel.Visibility = System.Windows.Visibility.Collapsed;
                _isWebViewReady = true;
            }
            catch (Exception ex)
            {
                // WebView2Exception when the Evergreen Runtime isn't installed lands
                // here as a general Exception in practice, deliberately not narrowing
                // the catch to a specific type since the failure mode users will hit
                // ("runtime not installed") and other environment failures (folder
                // missing, permissions) should both land on the same fallback path.
                AppLogger.LogError("MaterialPreviewHost.MaterialPreviewHost_Loaded", ex);
                ShowFallback();
            }
        }

        private void ShowFallback()
        {
            _isWebViewReady = false;
            PreviewWebView.Visibility = System.Windows.Visibility.Collapsed;
            FallbackPanel.Visibility = System.Windows.Visibility.Visible;
        }

        /// <summary>
        /// Pushes a channel update to the live preview (WebView2 path) or updates the
        /// flat swatch color (fallback path). Safe to call at high frequency, e.g. on
        /// every slider drag frame, PostWebMessageAsJson does not touch Revit and is
        /// cheap relative to the debounced Revit write.
        /// </summary>
        public void UpdateChannels(MaterialChannelSet channels)
        {
            if (channels == null) return;

            if (_isWebViewReady && PreviewWebView.CoreWebView2 != null)
            {
                string payload =
                    "{\"albedoR\":" + channels.AlbedoR +
                    ",\"albedoG\":" + channels.AlbedoG +
                    ",\"albedoB\":" + channels.AlbedoB +
                    ",\"roughness\":" + channels.Roughness.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"reflectivity\":" + channels.Reflectivity.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"bumpAmount\":" + channels.BumpAmount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"emissiveR\":" + channels.EmissiveR +
                    ",\"emissiveG\":" + channels.EmissiveG +
                    ",\"emissiveB\":" + channels.EmissiveB +
                    ",\"emissiveLuminanceCdM2\":" + channels.EmissiveLuminanceCdM2.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"transparency\":" + channels.Transparency.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"cutoutOpacity\":" + channels.CutoutOpacity.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "}";

                try
                {
                    PreviewWebView.CoreWebView2.PostWebMessageAsJson(payload);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("MaterialPreviewHost.UpdateChannels (PostWebMessageAsJson)", ex);
                }
            }
            else
            {
                FallbackSwatch.Background = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(channels.AlbedoR, channels.AlbedoG, channels.AlbedoB));
            }
        }
    }
}
