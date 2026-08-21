using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;

namespace VsTestkit.Testing
{
    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
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
            // Numeric literals in tests are int by default; comparing to a float
            // field would otherwise fail on type rather than on value.
            if (IsNumeric(a) && IsNumeric(b)) return Convert.ToDouble(a).Equals(Convert.ToDouble(b));
            return false;
        }

        static bool IsNumeric(object o) =>
            o is byte || o is short || o is int || o is long ||
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
