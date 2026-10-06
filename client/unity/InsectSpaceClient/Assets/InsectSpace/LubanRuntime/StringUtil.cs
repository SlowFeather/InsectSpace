using System.Collections;
using System.Text;

namespace Luban
{
    // Small runtime companion used by Luban's generated ToString methods for list fields.
    // It intentionally avoids Unity and is shared by the local server through the linked runtime sources.
    public static class StringUtil
    {
        public static string CollectionToString(IEnumerable values)
        {
            if (values == null) return "null";
            var builder = new StringBuilder("[");
            bool first = true;
            foreach (var value in values)
            {
                if (!first) builder.Append(", ");
                first = false;
                builder.Append(value);
            }
            return builder.Append(']').ToString();
        }
    }
}
