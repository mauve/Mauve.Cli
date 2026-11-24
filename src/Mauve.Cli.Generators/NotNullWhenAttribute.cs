#pragma warning disable IDE0130 // The compiler recognizes this attribute by its framework-qualified name.
namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
internal sealed class NotNullWhenAttribute(bool returnValue) : Attribute
{
    public bool ReturnValue { get; } = returnValue;
}
#pragma warning restore IDE0130
