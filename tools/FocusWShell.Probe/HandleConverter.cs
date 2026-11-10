using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusWShell.Probe;

/// <summary>Serializa handles de ventana (<see cref="nint"/>) como texto hexadecimal.</summary>
internal sealed class HandleConverter : JsonConverter<nint>
{
    public override nint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException();

    public override void Write(Utf8JsonWriter writer, nint value, JsonSerializerOptions options) =>
        writer.WriteStringValue($"0x{value:X}");
}
