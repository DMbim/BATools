using System.IO;
using System.IO.Compression;

namespace BA.Installer
{
    public static class ZipPayload
    {
        public static string ExtractToTempFolder(string zipPath)
        {
            if (!File.Exists(zipPath))
                throw new FileNotFoundException("Payload zip not found.", zipPath);

            var dir = Path.Combine(Path.GetTempPath(), "BA_payload_" + Guid.NewGuid());
            Directory.CreateDirectory(dir);

            ZipFile.ExtractToDirectory(zipPath, dir);
            return dir;
        }
    }
}
