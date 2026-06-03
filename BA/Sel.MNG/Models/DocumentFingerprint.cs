using System.Security.Cryptography;
using System.Text;

namespace BATools.SelectionManager.Models
{
    public static class DocumentFingerprint
    {
        public static string Compute(string pathName, string title)
        {
            string raw = $"{pathName}|{title}";
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes)[..16];
        }
    }
}