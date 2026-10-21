using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Flow.Launcher.Plugin.Explorer.Search
{
    /// <summary>
    /// A file search split into the name to look for and the extensions typed as ".docx" words,
    /// e.g. "report .docx .pdf" looks for "report" in Word and PDF files only.
    /// </summary>
    public sealed partial class SearchFilter
    {
        [GeneratedRegex(@"^\.[\p{L}\p{N}_\-]{1,20}$")]
        private static partial Regex ExtensionWordRegex();

        public string Name { get; }

        /// <summary>
        /// Extensions without the leading dot, lower case. Empty when no extension was typed.
        /// </summary>
        public IReadOnlyList<string> Extensions { get; }

        private SearchFilter(string name, IReadOnlyList<string> extensions)
        {
            Name = name;
            Extensions = extensions;
        }

        public static SearchFilter Parse(string search)
        {
            var nameWords = new List<string>();
            var extensions = new List<string>();

            foreach (var word in (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (ExtensionWordRegex().IsMatch(word))
                {
                    var extension = word[1..].ToLowerInvariant();
                    if (!extensions.Contains(extension))
                        extensions.Add(extension);
                }
                else
                {
                    nameWords.Add(word);
                }
            }

            return new SearchFilter(string.Join(' ', nameWords), extensions);
        }

        public bool HasExtensions => Extensions.Count > 0;

        /// <summary>
        /// Everything search text: the name, the typed extensions and the excluded extensions and folders, so excluded
        /// files are dropped by Everything itself and do not take up the result limit.
        /// </summary>
        public string ToEverythingSearch(IEnumerable<string> excludedExtensions, IEnumerable<string> excludedFolders = null)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(Name))
                parts.Add(Name);

            if (HasExtensions)
            {
                parts.Add("ext:" + string.Join(';', Extensions));
            }
            else
            {
                var excluded = excludedExtensions.ToList();
                var plain = excluded.Where(x => !IsWildcard(x)).ToList();
                if (plain.Count > 0)
                    parts.Add("!ext:" + string.Join(';', plain));

                // ext: takes no wildcards, so "pixpin*" becomes a file name pattern
                parts.AddRange(excluded.Where(IsWildcard).Select(x => "!*." + x));
            }

            // path: matches the term against the full path; the trailing separator keeps "C:\Temp" from also excluding "C:\Temp2"
            parts.AddRange((excludedFolders ?? [])
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => $"!path:\"{x.TrimEnd('\\')}\\\""));

            return string.Join(' ', parts);
        }

        /// <summary>
        /// Search text for engines that do not understand extension filters. Results still need <see cref="Matches"/>.
        /// </summary>
        public string ToPlainSearch()
        {
            if (!string.IsNullOrEmpty(Name))
                return Name;

            // Only extensions were typed, the name contains ".docx" for most matches
            return HasExtensions ? "." + Extensions[0] : string.Empty;
        }

        /// <summary>
        /// Whether <paramref name="extension"/> (without the dot) is one of <paramref name="excludedExtensions"/>,
        /// which may use * wildcards such as "pixpin*".
        /// </summary>
        public static bool IsExcludedExtension(string extension, IEnumerable<string> excludedExtensions)
        {
            return excludedExtensions.Any(excluded => IsWildcard(excluded)
                ? System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(excluded, extension, ignoreCase: true)
                : string.Equals(excluded, extension, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsWildcard(string extension) => extension.Contains('*');

        /// <summary>
        /// Whether a result passes the typed extensions. Folders never do once an extension is typed.
        /// </summary>
        public bool Matches(SearchResult result)
        {
            if (!HasExtensions)
                return true;

            if (result.Type != ResultType.File)
                return false;

            var extension = System.IO.Path.GetExtension(result.FullPath).TrimStart('.');
            return Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }
    }
}
