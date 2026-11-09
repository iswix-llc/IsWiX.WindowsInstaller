namespace WixToolset.Core.Native
{
    // Stands in for the fork's Cabinet class, which the linked ManagedMsmMerge uses only to extract files.
    internal sealed class Cabinet
    {
        public Cabinet(string path)
        {
        }

        public System.Collections.Generic.IEnumerable<string> Extract(string outputFolder)
        {
            throw new System.NotSupportedException();
        }
    }
}
