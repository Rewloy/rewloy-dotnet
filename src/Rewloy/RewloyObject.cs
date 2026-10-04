using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rewloy
{
    /// <summary>
    /// The base of every generated object (request body and answer alike).
    /// The API adds fields without notice; the ones this library's types do
    /// not know yet are kept in <see cref="AdditionalProperties"/> when an
    /// answer is read, and written back when a body is sent, so that a field
    /// added to the API is usable before the next regeneration.
    /// </summary>
    public abstract class RewloyObject
    {
        /// <summary>The JSON fields this type has no property for, by name.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
    }
}
