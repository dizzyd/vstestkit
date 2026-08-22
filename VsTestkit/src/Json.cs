// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace VsTestkit
{
    /// <summary>
    /// Serialization for verb results.
    ///
    /// Game objects are hostile to reflection-based serializers: they hold
    /// back-references to the world, lazy properties that touch other threads, and
    /// graphs deep enough to hang a serializer. So this is deliberately defensive
    /// - loops ignored, depth capped, member errors swallowed - and anything that
    /// still blows up degrades to ToString() instead of failing the call.
    /// </summary>
    public static class Json
    {
        public const int MaxDepth = 12;

        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Include,
            MaxDepth = MaxDepth,
            ContractResolver = new DefaultContractResolver(),
            Error = (sender, args) => { args.ErrorContext.Handled = true; }
        };

        public static string Write(object o)
        {
            try
            {
                return JsonConvert.SerializeObject(o, Settings);
            }
            catch (Exception)
            {
                try { return JsonConvert.SerializeObject(new { unserializable = o?.ToString() }, Settings); }
                catch { return "{\"unserializable\":true}"; }
            }
        }

        public static T Read<T>(string s) => JsonConvert.DeserializeObject<T>(s);

        /// <summary>
        /// Best-effort conversion of an arbitrary value into something that will
        /// survive the trip. Primitives pass through; everything else is attempted
        /// as a real object graph and falls back to a type-tagged ToString().
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
                var json = JsonConvert.SerializeObject(value, Settings);
                return JsonConvert.DeserializeObject(json);
            }
            catch (Exception)
            {
                return new { type = t.FullName, value = value.ToString() };
            }
        }
    }
}
