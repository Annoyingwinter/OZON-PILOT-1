using ICSharpCode.SharpZipLib.Zip;

namespace LitchiAutoUpdate
{
    internal static class ZipExtractor
    {
        public static void Extract(string zipPath, string targetDirectory)
        {
            FastZip zip = new FastZip();
            zip.ExtractZip(zipPath, targetDirectory, null);
        }
    }
}
