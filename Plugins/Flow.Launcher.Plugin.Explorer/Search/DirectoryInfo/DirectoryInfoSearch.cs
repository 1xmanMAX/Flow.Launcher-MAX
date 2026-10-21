using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Flow.Launcher.Plugin.SharedCommands;

namespace Flow.Launcher.Plugin.Explorer.Search.DirectoryInfo
{
    public static class DirectoryInfoSearch
    {
        private static readonly string ClassName = nameof(DirectoryInfoSearch);

        internal static IEnumerable<SearchResult> TopLevelDirectorySearch(Query query, string search, CancellationToken token)
        {
            var criteria = ConstructSearchCriteria(search);

            if (search.LastIndexOf(Constants.AllFilesFolderSearchWildcard) >
                search.LastIndexOf(Constants.DirectorySeparator))
                return DirectorySearch(new EnumerationOptions
                {
                    RecurseSubdirectories = true
                }, search, criteria, token);

            return DirectorySearch(new EnumerationOptions(), search, criteria,
                token); // null will be passed as default
        }

        private const int MaxRecursiveResults = 500;

        /// <summary>
        /// Finds files and folders under <paramref name="folderPath"/> whose name contains every word of the filter's name, in order,
        /// and that have one of its extensions when it has any.
        /// </summary>
        internal static IEnumerable<SearchResult> RecursiveSearch(string folderPath, SearchFilter filter, CancellationToken token)
        {
            var criteria = "*" + string.Join('*', filter.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)) + "*";
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            };

            var count = 0;
            // Enumerate everything and match here, so cancellation is checked on every entry and not only on matches
            foreach (var fileSystemInfo in new System.IO.DirectoryInfo(folderPath).EnumerateFileSystemInfos("*", options))
            {
                if (token.IsCancellationRequested)
                    yield break;

                if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(criteria, fileSystemInfo.Name, ignoreCase: true))
                    continue;

                var result = new SearchResult
                {
                    FullPath = fileSystemInfo.FullName,
                    Type = fileSystemInfo is FileInfo ? ResultType.File : ResultType.Folder,
                    WindowsIndexed = false
                };

                if (!filter.Matches(result))
                    continue;

                if (count++ >= MaxRecursiveResults)
                    yield break;

                yield return result;
            }
        }

        public static string ConstructSearchCriteria(string search)
        {
            string incompleteName = "";

            if (!search.EndsWith(Constants.DirectorySeparator))
            {
                var indexOfSeparator = search.LastIndexOf(Constants.DirectorySeparator);

                incompleteName = search[(indexOfSeparator + 1)..].ToLower();

                if (incompleteName.StartsWith(Constants.AllFilesFolderSearchWildcard))
                    incompleteName = string.Concat("*", incompleteName.AsSpan(1));
            }

            incompleteName += "*";

            return incompleteName;
        }

        private static IEnumerable<SearchResult> DirectorySearch(EnumerationOptions enumerationOption, string search,
            string searchCriteria, CancellationToken token)
        {
            var results = new List<SearchResult>();

            var path = FilesFolders.ReturnPreviousDirectoryIfIncompleteString(search);

            try
            {
                var directoryInfo = new System.IO.DirectoryInfo(path);

                foreach (var fileSystemInfo in directoryInfo.EnumerateFileSystemInfos(searchCriteria, enumerationOption))
                {
                    results.Add(new SearchResult
                    {
                        FullPath = fileSystemInfo.FullName,
                        Type = fileSystemInfo switch
                        {
                            System.IO.DirectoryInfo {Parent: null} => ResultType.Volume,
                            System.IO.DirectoryInfo => ResultType.Folder,
                            FileInfo => ResultType.File,
                            _ => throw new ArgumentOutOfRangeException(nameof(fileSystemInfo))
                        },
                        WindowsIndexed = false
                    });

                    if (token.IsCancellationRequested)
                        return results;
                }
            }
            catch (Exception e)
            {
                Main.Context.API.LogException(ClassName, "Error occurred while searching path", e);
                
                throw;
            }

            // Initial ordering, this order can be updated later by UpdateResultView.MainViewModel based on history of user selection.
            return results.OrderBy(r=>r.Type).ThenBy(r=>r.FullPath);
        }
    }
}
