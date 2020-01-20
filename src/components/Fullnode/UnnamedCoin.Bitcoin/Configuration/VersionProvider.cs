using System.Text.RegularExpressions;
using UnnamedCoin.Bitcoin.Interfaces;

namespace UnnamedCoin.Bitcoin.Configuration
{
    public class VersionProvider : IVersionProvider
    {
        public string GetVersion()
        {
            var match = Regex.Match(GetType().AssemblyQualifiedName,
                "Version=([0-9]+)(\\.([0-9]+)|)(\\.([0-9]+)|)(\\.([0-9]+)|)");
            var major = match.Groups[1].Value;
            var minor = match.Groups[3].Value;
            var build = match.Groups[5].Value;
            var revision = match.Groups[7].Value;

            if (revision == "0")
                return $"{major}.{minor}.{build}";

            return $"{major}.{minor}.{build}.{revision}";
        }
    }
}