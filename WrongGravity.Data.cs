using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Modding;

namespace WrongGravity
{
    /// <summary>
    /// One per-door override. Scene/gate are the lookup key (case-insensitive).
    /// </summary>
    public class DoorProfile
    {
        /// <summary>Scene that contains the gate (for example "Tutorial_01").</summary>
        public string scene = "";

        /// <summary>Gate GameObject name (for example "top1").</summary>
        public string gate = "";

        /// <summary>
        /// Watchdog timeout for DROPPING_DOWN. Zero means "use Config.TransitionTimeout".
        /// </summary>
        public float forceControlAfter;

        /// <summary>X offset applied to TransitionPoint.entryOffset before EnterScene.</summary>
        public float entryOffsetX;

        /// <summary>Y offset applied to TransitionPoint.entryOffset before EnterScene.</summary>
        public float entryOffsetY;

        /// <summary>
        /// Reserved for a future pass. Parsed for forward compatibility, but the current
        /// code never writes a velocity after EnterScene.
        /// </summary>
        public float entryVelocityX;

        /// <summary>Reserved (see <see cref="entryVelocityX"/>).</summary>
        public float entryVelocityY;

        /// <summary>When true, entryOffsetX/Y replace the name-based fallback.</summary>
        public bool applyEntryOffset;

        /// <summary>Reserved for a future pass.</summary>
        public bool applyEntryVelocity;
    }

    /// <summary>
    /// One per-trigger interaction override. Scene/path are the lookup key.
    /// </summary>
    public class InteractionProfile
    {
        /// <summary>Scene name.</summary>
        public string scene = "";

        /// <summary>
        /// Transform path. The fixer matches an exact path or a suffix
        /// ("Sly/Interaction" also matches "_SceneManager/Sly/Interaction").
        /// </summary>
        public string path = "";

        /// <summary>Y offset from the ground point found by the fixer.</summary>
        public float offsetY;

        /// <summary>Optional replacement BoxCollider2D height.</summary>
        public float sizeY;

        /// <summary>Use <see cref="offsetY"/> instead of the default ground placement.</summary>
        public bool applyOffset;

        /// <summary>Use <see cref="sizeY"/> to resize the trigger.</summary>
        public bool applySize;
    }

    /// <summary>
    /// Loads and indexes the embedded <c>Data/WrongGravityData.json</c> resource.
    ///
    /// The JSON file is embedded at build time (see WrongGravity.csproj), so a mod
    /// installation contains only WrongGravity.dll/PDB. Missing or malformed data is
    /// non-fatal: the mod logs the problem and continues with an empty profile set.
    /// </summary>
    internal sealed class MirrorData
    {
        public const string ResourceName = "WrongGravity.Data.WrongGravityData.json";

        public readonly List<DoorProfile> Doors = new List<DoorProfile>();
        public readonly List<InteractionProfile> Interactions = new List<InteractionProfile>();

        private readonly Dictionary<string, DoorProfile> doorLookup =
            new Dictionary<string, DoorProfile>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, InteractionProfile> interactionLookup =
            new Dictionary<string, InteractionProfile>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Loads the embedded resource. Never throws.</summary>
        public static MirrorData Load()
        {
            MirrorData data = new MirrorData();
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null)
                    {
                        Logger.LogWarn("[WrongGravity] Embedded resource not found: " + ResourceName);
                        foreach (string name in assembly.GetManifestResourceNames())
                            Logger.LogWarn("[WrongGravity] Available resource: " + name);
                        return data;
                    }

                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                        data.Read(reader.ReadToEnd().TrimStart('\uFEFF'));
                }

                Logger.Log("[WrongGravity] Loaded mirror data: " + data.Doors.Count + " doors, " +
                    data.Interactions.Count + " interactions");
            }
            catch (Exception ex)
            {
                Logger.LogError("[WrongGravity] Failed to load mirror data: " + ex);
            }
            return data;
        }

        private void Read(string json)
        {
            Dictionary<string, object> root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null)
                return;

            object value;
            if (root.TryGetValue("doors", out value))
            {
                List<object> list = value as List<object>;
                if (list != null)
                {
                    foreach (object item in list)
                    {
                        Dictionary<string, object> obj = item as Dictionary<string, object>;
                        if (obj == null)
                            continue;

                        DoorProfile profile = new DoorProfile
                        {
                            scene = GetString(obj, "scene"),
                            gate = GetString(obj, "gate"),
                            forceControlAfter = GetFloat(obj, "forceControlAfter"),
                            entryOffsetX = GetFloat(obj, "entryOffsetX"),
                            entryOffsetY = GetFloat(obj, "entryOffsetY"),
                            entryVelocityX = GetFloat(obj, "entryVelocityX"),
                            entryVelocityY = GetFloat(obj, "entryVelocityY"),
                            applyEntryOffset = GetBool(obj, "applyEntryOffset"),
                            applyEntryVelocity = GetBool(obj, "applyEntryVelocity")
                        };

                        if (profile.scene.Length != 0 && profile.gate.Length != 0)
                        {
                            Doors.Add(profile);
                            doorLookup[Key(profile.scene, profile.gate)] = profile;
                        }
                    }
                }
            }

            if (root.TryGetValue("interactions", out value))
            {
                List<object> list = value as List<object>;
                if (list != null)
                {
                    foreach (object item in list)
                    {
                        Dictionary<string, object> obj = item as Dictionary<string, object>;
                        if (obj == null)
                            continue;

                        InteractionProfile profile = new InteractionProfile
                        {
                            scene = GetString(obj, "scene"),
                            path = GetString(obj, "path"),
                            offsetY = GetFloat(obj, "offsetY"),
                            sizeY = GetFloat(obj, "sizeY"),
                            applyOffset = GetBool(obj, "applyOffset"),
                            applySize = GetBool(obj, "applySize")
                        };

                        if (profile.scene.Length != 0 && profile.path.Length != 0)
                        {
                            Interactions.Add(profile);
                            interactionLookup[Key(profile.scene, profile.path)] = profile;
                        }
                    }
                }
            }
        }

        /// <summary>Exact (case-insensitive) door lookup.</summary>
        public DoorProfile GetDoor(string scene, string gate)
        {
            DoorProfile profile;
            return doorLookup.TryGetValue(Key(scene, gate), out profile) ? profile : null;
        }

        /// <summary>Door watchdog timeout, or zero when there is no profile.</summary>
        public float GetDoorTimeout(string scene, string gate)
        {
            DoorProfile profile = GetDoor(scene, gate);
            return profile != null ? profile.forceControlAfter : 0f;
        }

        /// <summary>
        /// Interaction lookup. Exact path wins; otherwise a profile whose path is a
        /// suffix of the live transform path is used. This allows data entries to omit
        /// scene-root prefixes that differ between game versions.
        /// </summary>
        public InteractionProfile FindInteraction(string scene, string path)
        {
            InteractionProfile exact;
            if (interactionLookup.TryGetValue(Key(scene, path), out exact))
                return exact;

            string livePath = path ?? "";
            foreach (InteractionProfile profile in Interactions)
            {
                if (!string.Equals(profile.scene, scene, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.Equals(livePath, profile.path, StringComparison.OrdinalIgnoreCase))
                    return profile;

                if (livePath.EndsWith("/" + profile.path, StringComparison.OrdinalIgnoreCase))
                    return profile;
            }

            return null;
        }

        private static string Key(string scene, string name)
        {
            return (scene ?? "") + "|" + (name ?? "");
        }

        private static string GetString(Dictionary<string, object> obj, string key)
        {
            object value;
            return obj.TryGetValue(key, out value) && value != null ? value.ToString() : "";
        }

        private static float GetFloat(Dictionary<string, object> obj, string key)
        {
            object value;
            if (!obj.TryGetValue(key, out value) || value == null)
                return 0f;

            if (value is double)
                return (float)(double)value;

            float parsed;
            return float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : 0f;
        }

        private static bool GetBool(Dictionary<string, object> obj, string key)
        {
            object value;
            if (!obj.TryGetValue(key, out value) || value == null)
                return false;

            if (value is bool)
                return (bool)value;

            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) && parsed;
        }
    }

    /// <summary>
    /// A deliberately tiny JSON parser used instead of adding a JSON dependency to the
    /// mod. It supports the subset needed by the data file: objects, arrays, strings,
    /// numbers, booleans and null. Parsing is recursive; the input is an embedded,
    /// trusted resource, so there is no explicit depth limit.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null)
                throw new FormatException("JSON is null");
            return new Parser(json).ParseValue();
        }

        private sealed class Parser
        {
            private readonly string text;
            private int index;

            public Parser(string text)
            {
                this.text = text;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (index >= text.Length)
                    throw new FormatException("Unexpected end of JSON");

                char c = text[index];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                            return ParseNumber();
                        throw new FormatException("Unexpected character '" + c + "' at " + index);
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> result =
                    new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                index++;
                SkipWhitespace();
                if (Peek() == '}')
                {
                    index++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();
                    char next = Next();
                    if (next == '}')
                        return result;
                    if (next != ',')
                        throw new FormatException("Expected ',' or '}' at " + index);
                }
            }

            private List<object> ParseArray()
            {
                List<object> result = new List<object>();
                index++;
                SkipWhitespace();
                if (Peek() == ']')
                {
                    index++;
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    char next = Next();
                    if (next == ']')
                        return result;
                    if (next != ',')
                        throw new FormatException("Expected ',' or ']' at " + index);
                }
            }

            private string ParseString()
            {
                Expect('"');
                StringBuilder builder = new StringBuilder();
                while (true)
                {
                    if (index >= text.Length)
                        throw new FormatException("Unterminated string");

                    char c = text[index++];
                    if (c == '"')
                        return builder.ToString();
                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (index >= text.Length)
                        throw new FormatException("Unterminated escape");

                    char escape = text[index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (index + 4 > text.Length)
                                throw new FormatException("Bad unicode escape");
                            string hex = text.Substring(index, 4);
                            builder.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            index += 4;
                            break;
                        default:
                            throw new FormatException("Bad escape '\\" + escape + "'");
                    }
                }
            }

            private double ParseNumber()
            {
                int start = index;
                if (Peek() == '-')
                    index++;

                while (index < text.Length && char.IsDigit(text[index]))
                    index++;

                if (index < text.Length && text[index] == '.')
                {
                    index++;
                    while (index < text.Length && char.IsDigit(text[index]))
                        index++;
                }

                if (index < text.Length && (text[index] == 'e' || text[index] == 'E'))
                {
                    index++;
                    if (index < text.Length && (text[index] == '+' || text[index] == '-'))
                        index++;
                    while (index < text.Length && char.IsDigit(text[index]))
                        index++;
                }

                string number = text.Substring(start, index - start);
                double value;
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                    throw new FormatException("Bad number '" + number + "'");
                return value;
            }

            private void SkipWhitespace()
            {
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;
            }

            private char Peek()
            {
                return index < text.Length ? text[index] : '\0';
            }

            private char Next()
            {
                if (index >= text.Length)
                    throw new FormatException("Unexpected end of JSON");
                return text[index++];
            }

            private void Expect(char c)
            {
                SkipWhitespace();
                if (Next() != c)
                    throw new FormatException("Expected '" + c + "' at " + index);
            }

            private void Expect(string value)
            {
                if (index + value.Length > text.Length ||
                    string.CompareOrdinal(text, index, value, 0, value.Length) != 0)
                    throw new FormatException("Expected '" + value + "' at " + index);
                index += value.Length;
            }
        }
    }
}
