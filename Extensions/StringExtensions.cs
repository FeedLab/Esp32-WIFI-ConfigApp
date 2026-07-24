namespace WifiAP.Extensions
{
    public static class StringExtensions
    {
        public static string ExtractBlock(this string json, string searchWord)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(searchWord))
                return string.Empty;

            // Simple IndexOf without StringComparison
            int index = json.IndexOf("\"" + searchWord + "\"");
            if (index == -1)
                return string.Empty;

            // Find the next '{'
            int start = json.IndexOf('{', index);
            if (start == -1)
                return string.Empty;

            // Walk through braces to find the matching '}'
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') depth--;

                if (depth == 0)
                {
                    return json.Substring(start, i - start + 1);
                }
            }

            return string.Empty;
        }
    }
}