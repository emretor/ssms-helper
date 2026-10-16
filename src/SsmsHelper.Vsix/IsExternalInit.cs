// Polyfill so C# records and init-only setters compile on netstandard2.0.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
