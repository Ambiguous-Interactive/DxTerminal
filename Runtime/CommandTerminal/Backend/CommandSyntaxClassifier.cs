namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
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
        private readonly List<string> _variableNames = new();
        private readonly List<Validation> _validations = new();
        private string _tokenizedLine;
        private string _commandName;
        private string _normalizedName;

        private static CommandSyntaxRole ValidateArgument(CommandArgument spec, in CommandArg argument)
        {
            try
            {
                if (!spec.TryValidateSyntax(argument, out bool namedConstant))
                {
                    return CommandSyntaxRole.Invalid;
                }

                Type type = spec.DeclaredType;
                if (namedConstant || spec.HasChoices || type.IsEnum || type == typeof(bool))
                {
                    return CommandSyntaxRole.Choice;
                }

                return IsNumber(type) || IsComponentType(type) ? CommandSyntaxRole.Number : CommandSyntaxRole.String;
            }
            catch (Exception)
            {
                // Custom parsers and validators can fail while the user edits.
                return CommandSyntaxRole.Invalid;
            }
        }

        private static bool IsNumber(Type type)
        {
            return Type.GetTypeCode(type)
                    is TypeCode.Byte
                        or TypeCode.SByte
                        or TypeCode.Int16
                        or TypeCode.UInt16
                        or TypeCode.Int32
                        or TypeCode.UInt32
                        or TypeCode.Int64
                        or TypeCode.UInt64
                        or TypeCode.Single
                        or TypeCode.Double
                        or TypeCode.Decimal
                || type == typeof(BigInteger);
        }

        private static bool IsComponentType(Type type)
        {
            return type == typeof(Vector2)
                || type == typeof(Vector3)
                || type == typeof(Vector4)
                || type == typeof(Vector2Int)
                || type == typeof(Vector3Int)
                || type == typeof(Color);
        }

        private static void AddSpan(List<CommandSyntaxSpan> results, int start, int end, CommandSyntaxRole role)
        {
            if (start < end)
            {
                results.Add(new CommandSyntaxSpan(start, end, role));
            }
        }

        private static void AddComponentSpans(List<CommandSyntaxSpan> results, in CommandToken token, Type type)
        {
            int start = token.Start;
            string contents = token.Contents;
            if (type == typeof(Color) && contents.StartsWith("RGBA", StringComparison.OrdinalIgnoreCase))
            {
                AddSpan(results, start, start + 4, CommandSyntaxRole.Punctuation);
                start += 4;
            }

            for (int index = start; index < token.End; ++index)
            {
                char character = contents[index - token.Start];
                if (char.IsSurrogate(character) || (!CommandArg.Delimiters.Contains(character) && character is not ('(' or ')' or '[' or ']' or '{' or '}')))
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
                    if (!comparer.Equals(item, snapshot[index]))
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
            if (
                UpdateSettings(CommandArg.Delimiters, _delimiters, EqualityComparer<char>.Default)
                | UpdateSettings(CommandArg.IgnoredValuesForCleanedTypes, _ignoredForCleanedTypes, StringComparer.Ordinal)
                | UpdateSettings(CommandArg.IgnoredValuesForComplexTypes, _ignoredForComplexTypes, StringComparer.Ordinal)
                | UpdateSettings(CommandArg.DoNotCleanTypes, _doNotCleanTypes, EqualityComparer<Type>.Default)
            )
            {
                ResetValidationCache();
            }
            shell.EnsureAutoCommandsRegistered();

            bool foundCommandToken = false;
            int argumentIndex = 0;
            int? maxArguments = null;
            CommandBuilder.SubcommandRoute route = null;

            int tokenCount = _tokens.Count;
            for (int index = 0; index < tokenCount; ++index)
            {
                CommandToken token = _tokens[index];
                string variableName = _variableNames[index];
                if (!shell.TryResolveToken(token, variableName, out CommandArg argument))
                {
                    if (token.StartQuote != null)
                    {
                        AddSpan(results, token.Start - 1, token.Start, CommandSyntaxRole.Punctuation);
                    }

                    continue;
                }

                CommandSyntaxRole role = CommandSyntaxRole.String;
                CommandArgument spec = null;
                if (!foundCommandToken)
                {
                    foundCommandToken = true;
                    if (shell.TryGetCommand(NormalizeName(argument.contents ?? string.Empty), out CommandInfo command))
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
                    if (route.Lookup.TryGetValue(argument.contents ?? string.Empty, out route))
                    {
                        role = CommandSyntaxRole.Choice;
                        argumentIndex = 0;
                        maxArguments = route.IsRouter || route.HasRemaining ? null : route.Specs.Length;
                    }
                    else
                    {
                        role = CommandSyntaxRole.Invalid;
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
                        spec = route.Specs[Math.Min(argumentIndex, route.Specs.Length - 1)];
                        role = GetArgumentRole(index, spec, argument);
                    }

                    if (role != CommandSyntaxRole.Invalid && variableName != null)
                    {
                        role = CommandSyntaxRole.Variable;
                    }

                    ++argumentIndex;
                }

                if (token.StartQuote != null)
                {
                    AddSpan(results, token.Start - 1, token.Start, CommandSyntaxRole.Punctuation);
                }

                if (role == CommandSyntaxRole.Number && spec != null && IsComponentType(spec.DeclaredType))
                {
                    AddComponentSpans(results, token, spec.DeclaredType);
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

        private void UpdateTokens(string line)
        {
            List<char> quotes = CommandArg.Quotes;
            if (string.Equals(_tokenizedLine, line, StringComparison.Ordinal) && _quotes.Count == quotes.Count)
            {
                int index = 0;
                while (index < quotes.Count && _quotes[index] == quotes[index])
                {
                    ++index;
                }

                if (index == quotes.Count)
                {
                    return;
                }
            }

            _tokens.Clear();
            CommandTokenizer.Tokenize(line, _tokens);
            _tokenizedLine = line;
            _quotes.Clear();
            _quotes.AddRange(quotes);

            _variableNames.Clear();
            foreach (CommandToken token in _tokens)
            {
                _variableNames.Add(token.GetVariableName());
            }

            ResetValidationCache();
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
            if (!string.Equals(_commandName, name, StringComparison.Ordinal))
            {
                _commandName = name;
                _normalizedName = CommandShell.NormalizeCommandName(name);
            }

            return _normalizedName;
        }

        private CommandSyntaxRole GetArgumentRole(int index, CommandArgument spec, in CommandArg argument)
        {
            Type type = spec.DeclaredType;
            bool cacheable = (IsNumber(type) || IsComponentType(type) || type.IsEnum || type == typeof(bool) || type == typeof(string)) && spec.CanCacheSyntax;
            if (!cacheable)
            {
                _validations[index] = default;
                return ValidateArgument(spec, argument);
            }

            Validation previous = _validations[index];
            if (ReferenceEquals(previous.Spec, spec) && previous.Input.Equals(argument))
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
