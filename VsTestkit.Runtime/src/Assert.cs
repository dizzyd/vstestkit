// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using System;
using System.Collections;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace VsTestkit.Testing
{
    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    /// <summary>
    /// Thrown by <see cref="Vs.Skip"/>: the test found, once running, that this world
    /// cannot host it. Tallied as skipped, never as a failure.
    /// </summary>
    public class SkipException : Exception
    {
        public SkipException(string reason) : base(reason) { }
    }

    /// <summary>
    /// Assertions phrased so a failure message says what was expected and what was
    /// actually there. A bare "assertion failed" costs a whole debugging round trip.
    /// </summary>
    public static class Assert
    {
        public static void Fail(string message) => throw new AssertionException(message);

        public static void True(bool condition, string what = null) =>
            Ok(condition, what ?? "expected true, was false");

        public static void False(bool condition, string what = null) =>
            Ok(!condition, what ?? "expected false, was true");

        public static void Equal(object expected, object actual, string what = null)
        {
            if (Eq(expected, actual)) return;
            throw new AssertionException(Prefix(what) + $"expected {Show(expected)}, was {Show(actual)}");
        }

        public static void NotEqual(object unexpected, object actual, string what = null)
        {
            if (!Eq(unexpected, actual)) return;
            throw new AssertionException(Prefix(what) + $"expected anything but {Show(unexpected)}");
        }

        public static void Null(object value, string what = null) =>
            Ok(value == null, Prefix(what) + $"expected null, was {Show(value)}");

        public static void NotNull(object value, string what = null) =>
            Ok(value != null, Prefix(what) + "expected non-null, was null");

        public static void Greater(IComparable actual, IComparable bound, string what = null)
        {
            if (actual != null && actual.CompareTo(bound) > 0) return;
            throw new AssertionException(Prefix(what) + $"expected > {Show(bound)}, was {Show(actual)}");
        }

        public static void GreaterOrEqual(IComparable actual, IComparable bound, string what = null)
        {
            if (actual != null && actual.CompareTo(bound) >= 0) return;
            throw new AssertionException(Prefix(what) + $"expected >= {Show(bound)}, was {Show(actual)}");
        }

        public static void Less(IComparable actual, IComparable bound, string what = null)
        {
            if (actual != null && actual.CompareTo(bound) < 0) return;
            throw new AssertionException(Prefix(what) + $"expected < {Show(bound)}, was {Show(actual)}");
        }

        public static void LessOrEqual(IComparable actual, IComparable bound, string what = null)
        {
            if (actual != null && actual.CompareTo(bound) <= 0) return;
            throw new AssertionException(Prefix(what) + $"expected <= {Show(bound)}, was {Show(actual)}");
        }

        public static void InRange(double actual, double min, double max, string what = null)
        {
            if (actual >= min && actual <= max) return;
            throw new AssertionException(Prefix(what) + $"expected within [{min}, {max}], was {actual}");
        }

        public static void Close(double actual, double expected, double tolerance, string what = null)
        {
            if (Math.Abs(actual - expected) <= tolerance) return;
            throw new AssertionException(
                Prefix(what) + $"expected {expected} +/- {tolerance}, was {actual} (off by {Math.Abs(actual - expected)})");
        }

        public static void Contains(string haystack, string needle, string what = null)
        {
            if (haystack != null && haystack.Contains(needle)) return;
            throw new AssertionException(Prefix(what) + $"expected to contain {Show(needle)}, was {Show(haystack)}");
        }

        public static void IsType<T>(object value, string what = null)
        {
            if (value is T) return;
            throw new AssertionException(
                Prefix(what) + $"expected {typeof(T).Name}, was {(value == null ? "null" : value.GetType().Name)}");
        }

        /// <summary>Asserts the action throws, and returns the exception for further checks.</summary>
        public static TException Throws<TException>(Action action, string what = null) where TException : Exception
        {
            try { action(); }
            catch (TException e) { return e; }
            catch (Exception e)
            {
                throw new AssertionException(
                    Prefix(what) + $"expected {typeof(TException).Name}, got {e.GetType().Name}: {e.Message}");
            }
            throw new AssertionException(Prefix(what) + $"expected {typeof(TException).Name}, nothing was thrown");
        }

        /// <summary>
        /// Asserts an async operation throws, and returns the exception.
        ///
        /// Always use this rather than Throws(() => something.GetAwaiter()
        /// .GetResult()). Blocking on a task from a test body deadlocks: the body
        /// runs on a game main thread, and the continuation it is waiting for has
        /// to be posted to that same thread.
        /// </summary>
        public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string what = null)
            where TException : Exception
        {
            try { await action(); }
            catch (TException e) { return e; }
            catch (Exception e)
            {
                throw new AssertionException(
                    Prefix(what) + $"expected {typeof(TException).Name}, got {e.GetType().Name}: {e.Message}");
            }
            throw new AssertionException(Prefix(what) + $"expected {typeof(TException).Name}, nothing was thrown");
        }

        static void Ok(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }

        static bool Eq(object a, object b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            if (a.Equals(b)) return true;
            if (a.GetType() == b.GetType() || !IsNumeric(a) || !IsNumeric(b)) return false;

            // Widen integral/decimal values without losing digits. Float widens
            // exactly to double, but mixing either with decimal needs an exact
            // comparison rather than rounding one representation into the other.
            var af = a is float || a is double;
            var bf = b is float || b is double;
            if (!af && !bf) return Convert.ToDecimal(a) == Convert.ToDecimal(b);
            if (af && bf) return Convert.ToDouble(a).Equals(Convert.ToDouble(b));
            return af
                ? FloatingEqualsDecimal(Convert.ToDouble(a), Convert.ToDecimal(b))
                : FloatingEqualsDecimal(Convert.ToDouble(b), Convert.ToDecimal(a));
        }

        static bool FloatingEqualsDecimal(double binary, decimal number)
        {
            if (!double.IsFinite(binary)) return false;

            // Most mixed assertions compare a floating field to an integer
            // literal. The upper bound is exclusive: double rounds long.MaxValue
            // up to 2^63, which cannot be cast back to long.
            if (binary >= long.MinValue && binary < 9223372036854775808d &&
                binary == Math.Truncate(binary))
                return number == (long)binary;

            var bits = BitConverter.DoubleToInt64Bits(binary);
            var exponent = (int)((bits >> 52) & 0x7ff);
            var significand = new BigInteger(bits & 0x000fffffffffffffL);
            if (exponent != 0) significand += BigInteger.One << 52;
            if (bits < 0) significand = -significand;
            var power = exponent == 0 ? -1074 : exponent - 1075;

            Span<int> parts = stackalloc int[4];
            decimal.GetBits(number, parts);
            var unscaled = (BigInteger)(uint)parts[0] +
                           ((BigInteger)(uint)parts[1] << 32) +
                           ((BigInteger)(uint)parts[2] << 64);
            if (parts[3] < 0) unscaled = -unscaled;
            var scale = (parts[3] >> 16) & 0xff;

            // Compare the exact fractions: significand * 2^power and
            // unscaled / 10^scale. No conversion may round a difference away.
            significand *= BigInteger.Pow(10, scale);
            return power >= 0
                ? (significand << power) == unscaled
                : significand == (unscaled << -power);
        }

        static bool IsNumeric(object o) =>
            o is sbyte || o is byte || o is short || o is ushort ||
            o is int || o is uint || o is long || o is ulong ||
            o is float || o is double || o is decimal;

        static string Prefix(string what) => what == null ? "" : what + ": ";

        static string Show(object o)
        {
            switch (o)
            {
                case null: return "null";
                case string s: return "\"" + s + "\"";
                case IEnumerable e when !(o is string):
                    return "[" + string.Join(", ", e.Cast<object>().Take(8).Select(Show)) + "]";
                default: return o.ToString();
            }
        }
    }
}
