namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using System.Text;
    using Attributes;
    using Helper;
    using UnityEngine;

    /*
        Registry of live named values for milestone C4 (#244). Entries are
        registered through Terminal.Watch or discovered from [Watch]
        members; fps and frame are built in. Sampling runs on the main
        thread at the UI's cadence (4 Hz by default) and writes each value
        into a per-entry reused buffer, so an idle HUD allocates nothing
        per frame: a value whose sample equals the last one rewrites no
        text, and unchanged reference-typed getters allocate nothing at
        all. Value-typed getters box once per sample through the
        Func<object> registration path - 4 boxes per second, not per frame.

        Discovery and registration are main-thread operations, mirroring
        shell command registration.
     */
    public sealed class CommandWatchRegistry
    {
        private const int InitialValueCapacity = 32;
        private const float MinimumDeltaTimeSeconds = 1e-5f;
        private const string BuiltInWatchSource = "DxTerminal built-in";

        /// <summary>
        ///     Raised when a pin is set or cleared through
        ///     <see cref="SetPinned"/>. The editor UI persists the pin set
        ///     here; the registry itself never touches UnityEditor APIs.
        /// </summary>
        public event Action PinsChanged;

        private static readonly Lazy<List<DiscoveredWatch>> DiscoveredWatches = new Lazy<List<DiscoveredWatch>>(ScanDiscoveredWatches);

        /// <summary>
        ///     Bumped on every structural change (registration, removal,
        ///     pin state) so views can rebuild from the version instead of
        ///     diffing rows.
        /// </summary>
        public long Version { get; private set; }

        public int Count
        {
            get { return _entries.Count; }
        }

        private readonly List<WatchEntry> _entries;
        private readonly Dictionary<string, WatchEntry> _entriesByName;
        private readonly HashSet<string> _pinnedNames;
        private readonly HashSet<string> _pendingPins;
        private long _nextRegistrationId;
        private bool _discovered;

        public CommandWatchRegistry()
        {
            _entries = new List<WatchEntry>();
            _entriesByName = new Dictionary<string, WatchEntry>(StringComparer.OrdinalIgnoreCase);
            _pinnedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _pendingPins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _nextRegistrationId = 1;
            RegisterBuiltInWatches();
        }

        private static CommandTier NormalizeTier(CommandTier tier)
        {
            return tier is CommandTier.Player or CommandTier.Tester or CommandTier.Developer ? tier : CommandTier.Developer;
        }

        private static bool ValuesEqual(object previous, object current)
        {
            if (previous == null)
            {
                return current == null;
            }

            if (current == null)
            {
                return false;
            }

            return previous.Equals(current);
        }

        private static string NormalizeWatchName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A watch needs a non-empty name.", nameof(name));
            }

            string trimmed = name.Trim();
            if (0 <= trimmed.IndexOf(' ', StringComparison.Ordinal))
            {
                throw new ArgumentException($"Watch name '{trimmed}' must not contain spaces; the watch command addresses watches by name.", nameof(name));
            }

            return trimmed;
        }

        private static List<DiscoveredWatch> ScanDiscoveredWatches()
        {
            List<DiscoveredWatch> discovered = new();
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            int assemblyCount = loadedAssemblies.Length;
            for (int index = 0; index < assemblyCount; ++index)
            {
                Assembly assembly = loadedAssemblies[index];
                if (assembly == null || assembly.IsDynamic)
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                CollectDiscoveredFromTypes(types, discovered);
            }

            return discovered;
        }

        private static void CollectDiscoveredFromTypes(Type[] types, List<DiscoveredWatch> discovered)
        {
            const BindingFlags MemberFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            int typeCount = types.Length;
            for (int index = 0; index < typeCount; ++index)
            {
                Type type = types[index];
                if (type == null)
                {
                    continue;
                }

                /*
                    One try per type: a type whose members cannot reflect
                    (linked-away metadata) must not lose the remaining
                    types' watches.
                 */
                try
                {
                    CollectDiscoveredFromType(type, MemberFlags, discovered);
                }
                catch (Exception)
                {
                    continue;
                }
            }
        }

        private static void CollectDiscoveredFromType(Type type, BindingFlags memberFlags, List<DiscoveredWatch> discovered)
        {
            PropertyInfo[] properties = type.GetProperties(memberFlags);
            int propertyCount = properties.Length;
            for (int index = 0; index < propertyCount; ++index)
            {
                PropertyInfo property = properties[index];
                WatchAttribute attribute = property.GetCustomAttribute<WatchAttribute>(false);
                if (attribute == null || property.GetMethod == null || property.GetMethod.GetParameters().Length != 0)
                {
                    continue;
                }

                attribute.NormalizeName(property);
                AddDiscovered(discovered, attribute, type, property.Name, () => property.GetValue(null));
            }

            FieldInfo[] fields = type.GetFields(memberFlags);
            int fieldCount = fields.Length;
            for (int index = 0; index < fieldCount; ++index)
            {
                FieldInfo field = fields[index];
                WatchAttribute attribute = field.GetCustomAttribute<WatchAttribute>(false);
                if (attribute == null)
                {
                    continue;
                }

                attribute.NormalizeName(field);
                AddDiscovered(discovered, attribute, type, field.Name, () => field.GetValue(null));
            }

            MethodInfo[] methods = type.GetMethods(memberFlags);
            int methodCount = methods.Length;
            for (int index = 0; index < methodCount; ++index)
            {
                MethodInfo method = methods[index];
                WatchAttribute attribute = method.GetCustomAttribute<WatchAttribute>(false);
                if (attribute == null || method.GetParameters().Length != 0 || method.ReturnType == typeof(void))
                {
                    continue;
                }

                attribute.NormalizeName(method);
                AddDiscovered(discovered, attribute, type, method.Name, () => method.Invoke(null, null));
            }
        }

        private static void AddDiscovered(List<DiscoveredWatch> discovered, WatchAttribute attribute, Type declaringType, string memberName, Func<object> getter)
        {
            string name = attribute.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                Debug.LogWarning(LogTextSanitizer.Sanitize($"[DxTerminal] [Watch] on {declaringType.Name}.{memberName} has an empty name and is skipped."));
                return;
            }

            if (IsDuplicateName(discovered, name))
            {
                Debug.LogWarning(LogTextSanitizer.Sanitize($"[DxTerminal] Duplicate [Watch] name '{name}' on {declaringType.Name}; the first registration wins."));
                return;
            }

            string source = $"{declaringType.Name}.{memberName}";
            discovered.Add(new DiscoveredWatch(name, source, attribute.Format, NormalizeTier(attribute.Tier), getter));
        }

        private static bool IsDuplicateName(List<DiscoveredWatch> discovered, string name)
        {
            int discoveredCount = discovered.Count;
            for (int index = 0; index < discoveredCount; ++index)
            {
                if (string.Equals(discovered[index].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static WatchValueRole ClassifyRole(object value)
        {
            if (value == null)
            {
                return WatchValueRole.Body;
            }

            if (value is string)
            {
                return WatchValueRole.String;
            }

            Type type = value.GetType();
            if (type.IsEnum || type == typeof(bool))
            {
                return WatchValueRole.Choice;
            }

            TypeCode code = Type.GetTypeCode(type);
            switch (code)
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                {
                    return WatchValueRole.Number;
                }
                default:
                {
                    return WatchValueRole.Body;
                }
            }
        }

        /// <summary>
        ///     Registers a watch over a getter. The getter runs on the main
        ///     thread whenever the registry samples, so it must be cheap and
        ///     must not mutate game state. Names are unique and case-
        ///     insensitive; registering a taken name throws.
        /// </summary>
        public WatchHandle Watch<T>(string name, Func<T> getter, string source = null, string format = null, CommandTier tier = CommandTier.Developer)
        {
            if (getter == null)
            {
                throw new ArgumentNullException(nameof(getter));
            }

            Func<object> boxedGetter = () => getter();
            return Watch(name, boxedGetter, source, format, tier);
        }

        /// <summary>
        ///     Registers a watch whose getter already returns
        ///     <see cref="object"/>. See the typed overload for the shared
        ///     contract.
        /// </summary>
        public WatchHandle Watch(string name, Func<object> getter, string source = null, string format = null, CommandTier tier = CommandTier.Developer)
        {
            if (getter == null)
            {
                throw new ArgumentNullException(nameof(getter));
            }

            string normalized = NormalizeWatchName(name);
            EnsureDiscovered();
            if (_entriesByName.ContainsKey(normalized))
            {
                throw new ArgumentException($"A watch named '{normalized}' is already registered.", nameof(name));
            }

            return AddEntry(normalized, getter, source, format, tier);
        }

        public bool IsPinned(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && _pinnedNames.Contains(name);
        }

        /// <summary>
        ///     Pins or unpins a registered watch. The HUD shows pinned
        ///     watches only. Returns false when no live watch has that name;
        ///     the pin set itself only changes for live watches so command
        ///     typos cannot seed stale pins.
        /// </summary>
        public bool SetPinned(string name, bool pinned)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            EnsureDiscovered();
            if (!_entriesByName.TryGetValue(name, out WatchEntry entry))
            {
                return false;
            }

            if (entry.Pinned == pinned)
            {
                return true;
            }

            entry.Pinned = pinned;
            if (pinned)
            {
                _pinnedNames.Add(entry.Name);
            }
            else
            {
                _pinnedNames.Remove(entry.Name);
            }

            Version++;
            PinsChanged?.Invoke();
            return true;
        }

        /// <summary>
        ///     Copies the pin set into <paramref name="output"/>, including
        ///     names whose watch is not currently registered, so persistence
        ///     round-trips are lossless.
        /// </summary>
        public void CollectPinnedNames(List<string> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            output.Clear();
            foreach (string pinned in _pinnedNames)
            {
                output.Add(pinned);
            }
        }

        /// <summary>
        ///     Replaces the pin set from storage and applies it to live
        ///     entries. Names without a live watch become pending: the first
        ///     later registration with that name comes back pinned.
        /// </summary>
        public void LoadPins(IReadOnlyList<string> pinnedNames)
        {
            if (pinnedNames == null)
            {
                throw new ArgumentNullException(nameof(pinnedNames));
            }

            _pinnedNames.Clear();
            _pendingPins.Clear();
            for (int index = 0; index < pinnedNames.Count; ++index)
            {
                string name = pinnedNames[index];
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                _pinnedNames.Add(name);
                _pendingPins.Add(name);
            }

            int entryCount = _entries.Count;
            for (int index = 0; index < entryCount; ++index)
            {
                WatchEntry entry = _entries[index];
                entry.Pinned = _pendingPins.Remove(entry.Name);
            }

            Version++;
        }

        public bool TryGetEntry(string name, out WatchEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            EnsureDiscovered();
            return _entriesByName.TryGetValue(name, out entry);
        }

        /// <summary>
        ///     Copies the watch entries visible at <paramref name="activeTier"/>
        ///     in registration order into <paramref name="output"/>. Entries
        ///     above the active tier are hidden, matching how tier-restricted
        ///     commands disappear from player surfaces.
        /// </summary>
        public void CollectVisibleEntries(CommandTier activeTier, List<WatchEntry> output)
        {
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            output.Clear();
            EnsureDiscovered();
            CommandTier normalized = NormalizeTier(activeTier);
            int entryCount = _entries.Count;
            for (int index = 0; index < entryCount; ++index)
            {
                WatchEntry entry = _entries[index];
                if ((int)normalized < (int)entry.Tier)
                {
                    continue;
                }

                output.Add(entry);
            }
        }

        /// <summary>
        ///     Samples every watch visible at <paramref name="activeTier"/>:
        ///     runs each getter on the calling (main) thread and refreshes
        ///     the entry's cached text through its reused buffer. Getters
        ///     that throw are logged once and retried on later samples.
        /// </summary>
        public void Sample(CommandTier activeTier)
        {
            EnsureDiscovered();
            CommandTier normalized = NormalizeTier(activeTier);
            int entryCount = _entries.Count;
            for (int index = 0; index < entryCount; ++index)
            {
                WatchEntry entry = _entries[index];
                if ((int)normalized < (int)entry.Tier)
                {
                    continue;
                }

                SampleEntry(entry);
            }
        }

        internal void RemoveRegistration(long registrationId)
        {
            int entryCount = _entries.Count;
            for (int index = 0; index < entryCount; ++index)
            {
                WatchEntry entry = _entries[index];
                if (entry.RegistrationId != registrationId)
                {
                    continue;
                }

                _entries.RemoveAt(index);
                _entriesByName.Remove(entry.Name);
                if (entry.Pinned)
                {
                    /*
                        The pin intent stays in the pin set keyed by name, so
                        a re-registration (or a later session's discovery)
                        comes back pinned; the set itself only changes
                        through SetPinned and LoadPins.
                     */
                    entry.Pinned = false;
                }

                Version++;
                return;
            }
        }

        /// <summary>
        ///     Applies the deferred [Watch] discovery once. The scan is
        ///     cached statically per domain, so later play sessions
        ///     (disabled domain reload included) do not rescan; registrations
        ///     the scan finds are applied once per registry instance.
        /// </summary>
        internal void EnsureDiscovered()
        {
            if (_discovered)
            {
                return;
            }

            _discovered = true;
            List<DiscoveredWatch> discovered = DiscoveredWatches.Value;
            int discoveredCount = discovered.Count;
            for (int index = 0; index < discoveredCount; ++index)
            {
                DiscoveredWatch candidate = discovered[index];
                if (_entriesByName.ContainsKey(candidate.Name))
                {
                    Debug.LogWarning(
                        LogTextSanitizer.Sanitize($"[DxTerminal] Watch '{candidate.Name}' from {candidate.Source} is already registered; keeping the existing watch.")
                    );
                    continue;
                }

                AddEntry(candidate.Name, candidate.Getter, candidate.Source, candidate.Format, candidate.Tier);
            }
        }

        private void RegisterBuiltInWatches()
        {
            /*
                The handles are dropped on purpose: built-in watches live as
                long as the registry does, and a dropped handle can never
                remove them.
             */
            Watch("fps", (Func<object>)(() => 1f / Mathf.Max(Time.unscaledDeltaTime, MinimumDeltaTimeSeconds)), BuiltInWatchSource, "F1", CommandTier.Player);
            Watch("frame", (Func<object>)(() => Time.frameCount), BuiltInWatchSource, null, CommandTier.Player);
        }

        private WatchHandle AddEntry(string name, Func<object> getter, string source, string format, CommandTier tier)
        {
            WatchEntry entry = new WatchEntry(_nextRegistrationId, name, string.IsNullOrWhiteSpace(source) ? "Script" : source, format, NormalizeTier(tier), getter);
            _nextRegistrationId++;
            _entries.Add(entry);
            _entriesByName.Add(name, entry);
            if (_pinnedNames.Contains(name))
            {
                entry.Pinned = true;
            }

            Version++;
            return new WatchHandle(this, entry.RegistrationId);
        }

        private void SampleEntry(WatchEntry entry)
        {
            object value;
            try
            {
                value = entry.Getter();
            }
            catch (Exception e)
            {
                if (entry.LoggedGetterFailure)
                {
                    return;
                }

                entry.LoggedGetterFailure = true;
                Debug.LogException(e);
                Debug.LogWarning(LogTextSanitizer.Sanitize($"[DxTerminal] Watch '{entry.Name}' getter threw; sampling skips it until it stops throwing."));
                return;
            }

            if (entry.HasValue && ValuesEqual(entry.LastValue, value))
            {
                return;
            }

            entry.LastValue = value;
            entry.LoggedGetterFailure = false;
            entry.Role = ClassifyRole(value);
            entry.ValueText = FormatEntryValue(entry, value);
            entry.HasValue = true;
        }

        private string FormatEntryValue(WatchEntry entry, object value)
        {
            StringBuilder builder = entry.Builder;
            builder.Clear();
            if (value == null)
            {
                builder.Append("null");
            }
            else if (!string.IsNullOrEmpty(entry.Format) && value is IFormattable formattable)
            {
                builder.Append(formattable.ToString(entry.Format, CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(value);
            }

            return builder.ToString();
        }

        private sealed class DiscoveredWatch
        {
            internal readonly string Name;
            internal readonly string Source;
            internal readonly string Format;
            internal readonly CommandTier Tier;
            internal readonly Func<object> Getter;

            internal DiscoveredWatch(string name, string source, string format, CommandTier tier, Func<object> getter)
            {
                Name = name;
                Source = source;
                Format = format;
                Tier = tier;
                Getter = getter;
            }
        }

        /// <summary>One registered watch: identity, display metadata, and the last sampled value.</summary>
        public sealed class WatchEntry
        {
            public string Name { get; }

            public string Source { get; }

            public string Format { get; }

            public CommandTier Tier { get; }

            public bool Pinned { get; internal set; }

            public bool HasValue { get; internal set; }

            public string ValueText { get; internal set; }

            public WatchValueRole Role { get; internal set; }

            internal readonly long RegistrationId;
            internal readonly Func<object> Getter;
            internal readonly StringBuilder Builder;
            internal object LastValue;
            internal bool LoggedGetterFailure;

            internal WatchEntry(long registrationId, string name, string source, string format, CommandTier tier, Func<object> getter)
            {
                RegistrationId = registrationId;
                Name = name;
                Source = source;
                Format = format;
                Tier = tier;
                Getter = getter;
                Builder = new StringBuilder(InitialValueCapacity);
                Role = WatchValueRole.Body;
            }
        }

        /// <summary>The syntax role a watch value renders with, per the DESIGN.md § 3.2 roles.</summary>
        public enum WatchValueRole
        {
            [Obsolete("Use a valid WatchValueRole value.")]
            Unknown = 0,

            /// <summary>Numeric values; renders with syn-number.</summary>
            Number = 1,

            /// <summary>Free text; renders with syn-string.</summary>
            String = 2,

            /// <summary>Enums and bools; renders with syn-choice.</summary>
            Choice = 3,

            /// <summary>Everything else; renders as body text.</summary>
            Body = 4,
        }
    }
}
