using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rewloy
{
    /// <summary>The serializer settings the client uses for bodies and answers.</summary>
    internal static class RewloyJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            // A field left null is left out; <c>Optional&lt;T&gt;</c> sends an explicit null.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            // Turkish letters stay letters on the wire (both forms are valid JSON).
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }
}
