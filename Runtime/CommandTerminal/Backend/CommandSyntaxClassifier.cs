namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using UnityEngine;
    using BigInteger = System.Numerics.BigInteger;

    internal sealed class CommandSyntaxClassifier
    {
        private readonly List<CommandToken> _tokens = new();
        private readonly List<char> _quotes = new();
        private readonly List<char> _delimiters = new();
        private readonly List<string> _ignoredForCleanedTypes = new();
        private readonly List<string> _ignoredForComplexTypes = new();
        private readonly List<Type> _doNotCleanTypes = new();
        private readonly List<(string Contents, string VariableName)> _resolvedTokens = new();
        private readonly List<Validation> _validations = new();
        private CultureInfo _validationCulture;
        private long _parserVersion;
        private string _tokenizedLine;
        private string _commandName;
        private string _normalizedName;

        internal static CommandSyntaxRole GetTypeRole(Type type)
        {
            if (type.IsEnum || type == typeof(bool))
            {
                return CommandSyntaxRole.Choice;
            }

            TypeCode typeCode = Type.GetTypeCode(type);
            return typeCode switch
            {
                TypeCode.Byte
                or TypeCode.SByte
                or TypeCode.Int16
                or TypeCode.UInt16
                or TypeCode.Int32
                or TypeCode.UInt32
                or TypeCode.Int64
                or TypeCode.UInt64
                or TypeCode.Single
                or TypeCode.Double
                or TypeCode.Decimal => CommandSyntaxRole.Number,
                TypeCode.Object => type == typeof(BigInteger) || HasComponents(type) ? CommandSyntaxRole.Number : CommandSyntaxRole.String,
                _ => CommandSyntaxRole.String,
            };
        }

        internal static bool HasComponents(Type type)
        {
            return type == typeof(Vector2)
                || type == typeof(Vector3)
                || type == typeof(Vector4)
                || type == typeof(Vector2Int)
                || type == typeof(Vector3Int)
                || type == typeof(Color);
        }

        private static CommandSyntaxRole ValidateArgument(CommandArgument spec, in CommandArg argument)
        {
            try
            {
                bool valid = spec.TryValidateSyntax(argument, out bool isNamedConstant);
                if (!valid)
                {
                    return CommandSyntaxRole.Invalid;
                }

                return isNamedConstant || spec.HasChoices ? CommandSyntaxRole.Choice : spec.SyntaxRole;
            }
            catch (Exception)
            {
                // Custom parsers and validators can fail while the user edits.
                return CommandSyntaxRole.Invalid;
            }
        }

        private static void AddSpan(List<CommandSyntaxSpan> results, int start, int end, CommandSyntaxRole role)
        {
            if (start < end)
            {
                results.Add(new CommandSyntaxSpan(start, end, role));
            }
        }

        private static void AddComponentSpans(List<CommandSyntaxSpan> results, in CommandToken token, string line, Type type)
        {
            int start = token.Start;
            ReadOnlySpan<char> contents = line.AsSpan(start, token.End - start);
            bool hasColorPrefix = type == typeof(Color) && contents.StartsWith("RGBA".AsSpan(), StringComparison.OrdinalIgnoreCase);
            if (hasColorPrefix)
            {
                AddSpan(results, start, start + 4, CommandSyntaxRole.Punctuation);
                start += 4;
            }

            for (int index = start; index < token.End; ++index)
            {
                char character = line[index];
                bool isSurrogate = char.IsSurrogate(character);
                bool isPunctuation = character is '(' or ')' or '[' or ']' or '{' or '}' or '<' or '>' || CommandArg.Delimiters.Contains(character);
                if (isSurrogate || !isPunctuation)
                {
                    continue;
                }

                AddSpan(results, start, index, CommandSyntaxRole.Number);
                AddSpan(results, index, index + 1, CommandSyntaxRole.Punctuation);
                start = index + 1;
            }

            AddSpan(results, start, token.End, CommandSyntaxRole.Number);
        }

        private static bool UpdateSettings<T>(HashSet<T> settings, List<T> snapshot, IEqualityComparer<T> comparer)
        {
            /*
                Preserve enumeration order. The parser uses the first matching
                delimiter and applies text replacements in sequence.
            */
            if (settings.Count == snapshot.Count)
            {
                int index = 0;
                foreach (T item in settings)
                {
                    bool matches = comparer.Equals(item, snapshot[index]);
                    if (!matches)
                    {
                        break;
                    }

                    ++index;
                }

                if (index == snapshot.Count)
                {
                    return false;
                }
            }

            snapshot.Clear();
            snapshot.AddRange(settings);
            return true;
        }

        /// <summary>Writes syntax spans for the current line without invoking a command handler.</summary>
        /// <remarks>Reuse one classifier and result list per input. Custom parsers and validators run on each call.</remarks>
        public void Classify(string line, CommandShell shell, List<CommandSyntaxSpan> results)
        {
            results.Clear();
            UpdateTokens(line);
            RefreshConfiguration();
            shell.EnsureAutoCommandsRegistered();

            bool foundCommandToken = false;
            int argumentIndex = 0;
            int? maxArguments = null;
            CommandBuilder.SubcommandRoute route = null;

            int tokenCount = _tokens.Count;
            for (int index = 0; index < tokenCount; ++index)
            {
                CommandToken token = _tokens[index];
                ReadOnlySpan<char> contents = line.AsSpan(token.Start, token.End - token.Start);
                bool canExpand = token.EndQuote == null && !contents.IsEmpty && contents[0] == '$' && !contents[1..].Trim().IsEmpty;
                if (token.StartQuote != null)
                {
                    AddSpan(results, token.Start - 1, token.Start, CommandSyntaxRole.Punctuation);
                }

                if (token.EndQuote == null && contents.Trim().IsEmpty)
                {
                    continue;
                }

                CommandSyntaxRole role = CommandSyntaxRole.String;
                CommandArgument spec = null;
                if (!foundCommandToken)
                {
                    foundCommandToken = true;
                    CommandArg argument = ResolveArgument(shell, line, contents, token, index, canExpand);
                    string normalizedName = NormalizeName(argument.contents ?? string.Empty);
                    bool foundCommand = shell.TryGetCommand(normalizedName, out CommandInfo command);
                    if (foundCommand)
                    {
                        role = CommandSyntaxRole.Command;
                        route = command.SyntaxRoute;
                        maxArguments = command.maxArgCount;
                    }
                    else
                    {
                        role = CommandSyntaxRole.UnknownCommand;
                    }
                }
                else if (route is { IsRouter: true })
                {
                    CommandArg argument = ResolveArgument(shell, line, contents, token, index, canExpand);
                    bool foundRoute = route.Lookup.TryGetValue(argument.contents ?? string.Empty, out route);
                    role = CommandSyntaxRole.Invalid;
                    if (foundRoute)
                    {
                        role = canExpand ? CommandSyntaxRole.Variable : CommandSyntaxRole.Choice;
                        maxArguments = route.IsRouter || route.HasRemaining ? null : route.Specs.Length;
                    }
                }
                else if (maxArguments is int limit && limit <= argumentIndex)
                {
                    role = CommandSyntaxRole.Invalid;
                }
                else
                {
                    if (route != null)
                    {
                        int specIndex = Math.Min(argumentIndex, route.Specs.Length - 1);
                        spec = route.Specs[specIndex];
                        if (!canExpand && spec.IsSimpleSyntaxValid(contents))
                        {
                            role = spec.HasChoices ? CommandSyntaxRole.Choice : spec.SyntaxRole;
                        }
                        else
                        {
                            CommandArg argument = ResolveArgument(shell, line, contents, token, index, canExpand);
                            role = GetArgumentRole(index, spec, argument);
                        }
                    }

                    if (role != CommandSyntaxRole.Invalid && canExpand)
                    {
                        role = CommandSyntaxRole.Variable;
                    }

                    ++argumentIndex;
                }

                if (role == CommandSyntaxRole.Number && spec is { HasSyntaxComponents: true })
                {
                    AddComponentSpans(results, token, line, spec.DeclaredType);
                }
                else
                {
                    AddSpan(results, token.Start, token.End, role);
                }

                if (token.EndQuote != null)
                {
                    AddSpan(results, token.End, token.End + 1, CommandSyntaxRole.Punctuation);
                }
            }
        }

        internal bool RefreshConfiguration()
        {
            bool delimitersChanged = UpdateSettings(CommandArg.Delimiters, _delimiters, EqualityComparer<char>.Default);
            bool ignoredTextChanged = UpdateSettings(CommandArg.IgnoredValuesForCleanedTypes, _ignoredForCleanedTypes, StringComparer.Ordinal);
            bool ignoredComponentsChanged = UpdateSettings(CommandArg.IgnoredValuesForComplexTypes, _ignoredForComplexTypes, StringComparer.Ordinal);
            bool rawTypesChanged = UpdateSettings(CommandArg.DoNotCleanTypes, _doNotCleanTypes, EqualityComparer<Type>.Default);
            CultureInfo culture = CultureInfo.CurrentCulture;
            bool cultureChanged = !ReferenceEquals(_validationCulture, culture);
            _validationCulture = culture;
            long parserVersion = CommandArg.ParserVersion;
            bool changed = delimitersChanged || ignoredTextChanged || ignoredComponentsChanged || rawTypesChanged || cultureChanged || _parserVersion != parserVersion;
            _parserVersion = parserVersion;
            if (changed)
            {
                ResetValidationCache();
            }

            return changed || !QuotesMatch(CommandArg.Quotes);
        }

        private bool QuotesMatch(List<char> quotes)
        {
            int count = quotes.Count;
            if (_quotes.Count != count)
            {
                return false;
            }

            int index = 0;
            while (index < count && _quotes[index] == quotes[index])
            {
                ++index;
            }

            return index == count;
        }

        private void UpdateTokens(string line)
        {
            List<char> quotes = CommandArg.Quotes;
            if (string.Equals(_tokenizedLine, line, StringComparison.Ordinal) && QuotesMatch(quotes))
            {
                return;
            }

            _tokens.Clear();
            CommandTokenizer.Tokenize(line, _tokens, includeContents: false);
            _tokenizedLine = line;
            _quotes.Clear();
            _quotes.AddRange(quotes);
            int count = _tokens.Count;
            int cached = _resolvedTokens.Count;
            if (count < cached)
            {
                _resolvedTokens.RemoveRange(count, cached - count);
            }
            else
            {
                for (int index = cached; index < count; ++index)
                {
                    _resolvedTokens.Add(default);
                }
            }

            ResetValidationCache();
        }

        private CommandArg ResolveArgument(CommandShell shell, string line, ReadOnlySpan<char> contents, in CommandToken token, int index, bool canExpand)
        {
            (string text, string variable) = _resolvedTokens[index];
            if (text == null || !contents.SequenceEqual(text.AsSpan()))
            {
                text = line.Substring(token.Start, contents.Length);
                variable = null;
                _resolvedTokens[index] = (text, variable);
            }

            if (!canExpand)
            {
                return new CommandArg(text, token.StartQuote, token.EndQuote);
            }

            if (variable == null)
            {
                variable = text.Substring(1);
                _resolvedTokens[index] = (text, variable);
            }

            CommandToken materialized = new(text, token.StartQuote, token.EndQuote, token.Start, token.End);
            shell.TryResolveToken(materialized, variable, out CommandArg argument);
            return argument;
        }

        private void ResetValidationCache()
        {
            _validations.Clear();
            int tokenCount = _tokens.Count;
            for (int index = 0; index < tokenCount; ++index)
            {
                _validations.Add(default);
            }
        }

        private string NormalizeName(string name)
        {
            bool sameName = string.Equals(_commandName, name, StringComparison.Ordinal);
            if (!sameName)
            {
                _commandName = name;
                _normalizedName = CommandShell.NormalizeCommandName(name);
            }

            return _normalizedName;
        }

        private CommandSyntaxRole GetArgumentRole(int index, CommandArgument spec, in CommandArg argument)
        {
            if (!spec.CanCacheSyntax)
            {
                _validations[index] = default;
                return ValidateArgument(spec, argument);
            }

            Validation previous = _validations[index];
            bool matches = ReferenceEquals(previous.Spec, spec) && previous.Input.Equals(argument);
            if (matches)
            {
                return previous.Role;
            }

            CommandSyntaxRole role = ValidateArgument(spec, argument);
            _validations[index] = new Validation(spec, argument, role);
            return role;
        }

        internal enum CommandSyntaxRole
        {
            [Obsolete("Specify a syntax role.")]
            Unknown = 0,
            Command = 1,
            UnknownCommand = 2,
            Choice = 3,
            Number = 4,
            String = 5,
            Variable = 6,
            Punctuation = 7,
            Invalid = 8,
        }

        /// <summary>A half-open range of UTF-16 indices in the original command line.</summary>
        internal readonly struct CommandSyntaxSpan
        {
            public int Start { get; }
            public int End { get; }
            public CommandSyntaxRole Role { get; }

            public CommandSyntaxSpan(int start, int end, CommandSyntaxRole role)
            {
                Start = start;
                End = end;
                Role = role;
            }
        }

        private readonly struct Validation
        {
            public CommandArgument Spec { get; }
            public CommandArg Input { get; }
            public CommandSyntaxRole Role { get; }

            public Validation(CommandArgument spec, CommandArg input, CommandSyntaxRole role)
            {
                Spec = spec;
                Input = input;
                Role = role;
            }
        }
    }
}
