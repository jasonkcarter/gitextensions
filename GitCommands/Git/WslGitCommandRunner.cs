using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GitUIPluginInterfaces;
using JetBrains.Annotations;

namespace GitCommands
{
    public sealed class WslGitCommandRunner : IGitCommandRunner
    {
        public const string ExplorerPrefix = @"\\wsl$\";
        public const string LongArgumentRegexPattern = @"^--(?:[a-zA-Z0-9_-]+)=";
        public const string WslPath = @"wsl.exe";
        private static string _fileName;
        private readonly Func<Encoding> _defaultEncoding;
        private readonly string _distroExplorerPrefix;
        private readonly int _distroExplorerPrefixLength;
        private readonly string _distroName;
        private readonly string _workingDir;

        private WslGitCommandRunner(string distroName, string windowsWorkingDir, Func<Encoding> defaultEncoding)
        {
            _distroName = distroName;
            _distroExplorerPrefix = ExplorerPrefix + distroName + @"\";
            _distroExplorerPrefixLength = _distroExplorerPrefix.Length;
            _workingDir = @"/" + windowsWorkingDir.Substring(_distroExplorerPrefixLength).Replace('\\', '/');
            _defaultEncoding = defaultEncoding;
            GitExecutable = new Executable(FileNameProvider, windowsWorkingDir, ArgumentsFilter);
        }

        public IExecutable GitExecutable { get; }

        public string ArgumentsFilter(string arguments)
        {
            var wslArgs = new List<string>(new[] { @"-d", _distroName, @"--", @"git", @"-C", @"'" + _workingDir + @"'" });

            if (arguments?.Length > 0)
            {
                bool ignoreNext = false;
                foreach (string arg in arguments.SplitBySpace())
                {
                    if (ignoreNext)
                    {
                        ignoreNext = false;
                        continue;
                    }

                    ignoreNext = arg == "-C";

                    wslArgs.Add(_transformWslPath(arg));
                }
            }

            return wslArgs.Join(" ");
        }

        [NotNull]
        public IProcess RunDetached(
            ArgumentString arguments = default,
            bool createWindow = false,
            bool redirectInput = false,
            bool redirectOutput = false,
            Encoding outputEncoding = null)
        {
            if (outputEncoding == null && redirectOutput)
            {
                outputEncoding = _defaultEncoding();
            }

            IProcess process =
                GitExecutable.Start(arguments, createWindow, redirectInput, redirectOutput, outputEncoding);
            return process;
        }

        public static string FileNameProvider()
        {
            if (_fileName != null)
            {
                return _fileName;
            }

            _fileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"sysnative", WslPath);
            return _fileName;
        }

        public static bool TryCreate(string windowsWorkingDir, Func<Encoding> defaultEncoding,
            out WslGitCommandRunner commandRunner)
        {
            if (windowsWorkingDir == null)
            {
                commandRunner = null;
                return false;
            }

            if (!windowsWorkingDir.StartsWith(ExplorerPrefix, StringComparison.CurrentCultureIgnoreCase))
            {
                commandRunner = null;
                return false;
            }

            int nextBackslashIndex = windowsWorkingDir.IndexOf('\\', ExplorerPrefix.Length);
            if (nextBackslashIndex < ExplorerPrefix.Length + 2)
            {
                commandRunner = null;
                return false;
            }

            string distroName =
                windowsWorkingDir.Substring(ExplorerPrefix.Length, nextBackslashIndex - ExplorerPrefix.Length);

            commandRunner = new WslGitCommandRunner(distroName, windowsWorkingDir, defaultEncoding);
            return true;
        }

        private string _standardizeLongArguments(string argument)
        {
            var regex = new Regex(LongArgumentRegexPattern, RegexOptions.Compiled);
            Match match = regex.Match(argument);
            if (!match.Success)
            {
                return argument;
            }

            string longArgument = match.Captures[0].Value;
            argument = argument.Substring(longArgument.Length);

            return longArgument + _standardizeQuotes(argument);
        }

        private string _standardizeQuotes(string argument)
        {
            if (argument.StartsWith(@"""") && argument.EndsWith(@""""))
            {
                argument = argument.Substring(1, argument.Length - 2);
                argument = argument.Replace(@"\""", string.Empty);
            }

            argument = @"'" + argument.Replace(@"'", @"\'").Replace(@"""", string.Empty) + @"'";
            return argument;
        }

        private string _transformWslPath(string argument)
        {
            if (argument == null)
            {
                return null;
            }

            string prefix = string.Empty;
            if (argument.StartsWith(@""""))
            {
                argument = _standardizeQuotes(argument);
            }

            if (argument.StartsWith("'"))
            {
                prefix = "'";
            }

            if (!argument.StartsWith(prefix + _distroExplorerPrefix, StringComparison.CurrentCultureIgnoreCase))
            {
                return _standardizeLongArguments(argument);
            }

            string wslPath = prefix + @"/" + argument.Substring(prefix.Length + _distroExplorerPrefixLength).Replace('\\', '/');
            return wslPath;
        }
    }
}
