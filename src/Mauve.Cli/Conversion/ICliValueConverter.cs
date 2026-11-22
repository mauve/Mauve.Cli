namespace Mauve.Cli.Conversion;

/// <summary>Provides a statically referenced converter without runtime discovery.</summary>
/// <typeparam name="T">The converted value type.</typeparam>
public interface ICliValueConverter<T>
{
    /// <summary>Converts one complete token, returning a safe failure for invalid input.</summary>
    /// <param name="value">The raw token.</param>
    /// <param name="context">Immutable conversion metadata.</param>
    /// <returns>The typed conversion outcome.</returns>
    static abstract ValueConversion<T> Convert(string value, ConversionContext context);
}
