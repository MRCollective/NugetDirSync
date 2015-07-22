using System;

namespace NuGetDirSync
{
    public partial class PackageSpecTemplate : PackageSpecTemplateBase
    {
        public readonly Version Version;
        public readonly string PackageName;

        public PackageSpecTemplate(Version version, string packageName)
        {
            Version = version;
            PackageName = packageName;
        }
    }
}