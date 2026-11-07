// Shims that let the library compile for netstandard2.0 as well as net8.0.
// Everything here is internal and conditional, so the net8.0 build is unaffected.
#nullable enable

#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{
    // Required by `init` accessors and `readonly record struct`.
    internal static class IsExternalInit
    {
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        public bool ReturnValue { get; }
    }
}
#endif

namespace IsWiX.WindowsInstaller
{
    internal static class Compat
    {
        // Encoding.Latin1 is not in netstandard2.0; ISO-8859-1 is code page 28591.
        public static System.Text.Encoding Latin1
        {
            get
            {
#if NET
                return System.Text.Encoding.Latin1;
#else
                return System.Text.Encoding.GetEncoding(28591);
#endif
            }
        }

        // File.Move(source, destination, overwrite) is not in netstandard2.0.
        public static void MoveOverwrite(string source, string destination)
        {
#if NET
            System.IO.File.Move(source, destination, overwrite: true);
#else
            if (System.IO.File.Exists(destination))
            {
                System.IO.File.Delete(destination);
            }

            System.IO.File.Move(source, destination);
#endif
        }

        // Array.Fill is not in netstandard2.0.
        public static void Fill<T>(T[] array, T value)
        {
#if NET
            Array.Fill(array, value);
#else
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = value;
            }
#endif
        }
    }

#if NETSTANDARD2_0
    // Instance members that exist on net8.0 but not on netstandard2.0. Instance methods win overload
    // resolution on net8.0, and this class does not exist there, so there is nothing to conflict.
    internal static class NetStandardExtensions
    {
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
            where TKey : notnull
        {
            if (dictionary.ContainsKey(key))
            {
                return false;
            }

            dictionary.Add(key, value);
            return true;
        }

        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source) => new HashSet<T>(source);

        // Lets `foreach (var (key, value) in dictionary)` compile.
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }

        public static string GetString(this System.Text.Encoding encoding, ReadOnlySpan<byte> bytes)
            => bytes.IsEmpty ? string.Empty : encoding.GetString(bytes.ToArray());
    }
#endif
}
