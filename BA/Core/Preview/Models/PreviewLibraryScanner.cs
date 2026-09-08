// Path: BA/Core/Content/Preview/PreviewLibraryScanner.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BA.Core.Content.Preview
{
    public static class PreviewLibraryScanner
    {
        /// <summary>
        /// Walks the given root folders for .rfa files and returns the
        /// paths that need a preview generated, either because no PNG or
        /// JPG exists yet, or because the family file itself was modified
        /// more recently than its cached PNG.
        /// </summary>
        public static List<string> FindPathsNeedingPreview(IEnumerable<string> rootFolders)
        {
            var result = new List<string>();

            foreach (string root in rootFolders.Where(Directory.Exists))
            {
                foreach (string familyPath in Directory.EnumerateFiles(root, "*.rfa", SearchOption.AllDirectories))
                {
                    string pngPath = Path.ChangeExtension(familyPath, ".png");
                    string jpgPath = Path.ChangeExtension(familyPath, ".jpg");

                    bool pngExists = File.Exists(pngPath);
                    bool jpgExists = File.Exists(jpgPath);

                    if (!pngExists || !jpgExists)
                    {
                        result.Add(familyPath);
                        continue;
                    }

                    DateTime familyModifiedUtc = File.GetLastWriteTimeUtc(familyPath);
                    DateTime pngModifiedUtc = File.GetLastWriteTimeUtc(pngPath);

                    if (familyModifiedUtc > pngModifiedUtc)
                        result.Add(familyPath);
                }
            }

            return result;
        }
    }
}