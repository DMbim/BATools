using System.IO;
using System.Xml.Linq;

namespace BA.Installer
{
    public static class ManifestWriter
    {
        public static void WriteApplicationAddinManifest(
            string manifestPath,
            string assemblyPath,
            string fullClassName,
            string addinIdGuid,
            string name,
            string vendorId,
            string vendorDescription)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);

            var doc =
                new XDocument(
                    new XDeclaration("1.0", "utf-8", "no"),
                    new XElement("RevitAddIns",
                        new XElement("AddIn",
                            new XAttribute("Type", "Application"),
                            new XElement("Name", name),
                            new XElement("Assembly", assemblyPath),
                            new XElement("AddInId", addinIdGuid),
                            new XElement("FullClassName", fullClassName),
                            new XElement("VendorId", vendorId),
                            new XElement("VendorDescription", vendorDescription)
                        )
                    )
                );

            doc.Save(manifestPath);
        }
    }
}
