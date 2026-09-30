using System;

namespace InsectSpace.Client
{
    [Serializable]
    public sealed class ContentPackageVersion
    {
        public string name;
        public string version;

        public void Validate(string coreName)
        {
            if (!IsSafeSegment(name) || !IsSafeSegment(version) || name == coreName)
                throw new InvalidOperationException("Invalid or conflicting content package identity.");
        }

        private static bool IsSafeSegment(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "." || value == ".." || value.Length > 100) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '-' && c != '_' && c != '.') return false;
            return true;
        }
    }
}
