// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace VsTestkit
{
    /// <summary>
    /// Serialization for verb results.
    ///
    /// Game objects are hostile to reflection-based serializers: they hold
    /// back-references to the world, lazy properties that touch other threads, and
    /// graphs deep enough to overflow the stack. Member errors and reference loops
    /// are ignored; an over-depth value is replaced with an explicit marker rather
    /// than returning an incomplete graph.
    /// </summary>
    public static class Json
    {
        public const int MaxDepth = 12;

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            ContractResolver = new DefaultContractResolver(),
            Error = (sender, args) => { args.ErrorContext.Handled = args.ErrorContext.Error is not DepthLimitException; }
        };

        static readonly object Truncated = new { truncated = true, reason = "max_depth", maxDepth = MaxDepth };

        static string Serialize(object value)
        {
            using var text = new StringWriter(CultureInfo.InvariantCulture);
            using var writer = new DepthLimitedWriter(text);
            JsonSerializer.Create(Settings).Serialize(writer, value);
            return text.ToString();
        }

        // MaxDepth is a reader setting in Newtonsoft. Stop at container entry,
        // before its members are visited, and let the error escape member recovery.
        sealed class DepthLimitedWriter : JsonTextWriter
        {
            public DepthLimitedWriter(TextWriter output) : base(output) { AutoCompleteOnClose = false; }

            void CheckDepth()
            {
                if (Top >= MaxDepth) throw new DepthLimitException();
            }

            public override void WriteStartObject() { CheckDepth(); base.WriteStartObject(); }
            public override void WriteStartArray() { CheckDepth(); base.WriteStartArray(); }
            public override void WriteStartConstructor(string name) { CheckDepth(); base.WriteStartConstructor(name); }
        }

        sealed class DepthLimitException : JsonSerializationException { }

        public static string Write(object o)
        {
            try
            {
                return Serialize(o);
            }
            catch (DepthLimitException)
            {
                return Serialize(Truncated);
            }
            catch (Exception)
            {
                try { return Serialize(new { unserializable = o?.ToString() }); }
                catch { return "{\"unserializable\":true}"; }
            }
        }

        public static T Read<T>(string s) => JsonConvert.DeserializeObject<T>(s);

        /// <summary>
        /// Best-effort conversion of an arbitrary value into something that will
        /// survive the trip. Primitives pass through; everything else is attempted
        /// as a real object graph, with an explicit marker when it exceeds MaxDepth.
        /// </summary>
        public static object Simplify(object value)
        {
            if (value == null) return null;

            var t = value.GetType();
            if (t.IsPrimitive || value is string || value is decimal) return value;

            try
            {
                // Round-trip through the serializer so a failure surfaces here,
                // where we can still substitute something readable, rather than
                // halfway through writing the response body.
                var json = Serialize(value);
                return JsonConvert.DeserializeObject(json);
            }
            catch (DepthLimitException)
            {
                return Truncated;
            }
            catch (Exception)
            {
                return new { type = t.FullName, value = value.ToString() };
            }
        }
    }
}
