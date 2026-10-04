using System.Text.Json;

namespace Rewloy
{
    /// <summary>One event of a stream.</summary>
    public sealed class ServerSentEvent
    {
        /// <summary>Makes an event.</summary>
        /// <param name="event">The type.</param>
        /// <param name="data">The data.</param>
        /// <param name="id">The last event ID.</param>
        public ServerSentEvent(string @event, string data, string id)
        {
            Event = @event;
            Data = data;
            Id = id;
        }

        /// <summary>The type: the <c>event:</c> field, <c>message</c> when the event had none.</summary>
        public string Event { get; }

        /// <summary>The <c>data:</c> lines, joined with "\n".</summary>
        public string Data { get; }

        /// <summary>The last event ID: the latest <c>id:</c> field the stream has sent (empty if none).</summary>
        public string Id { get; }

        /// <summary>Parses <see cref="Data"/> as JSON (the API's events carry a JSON object).</summary>
        public JsonElement Json()
        {
            using (var doc = JsonDocument.Parse(Data)) return doc.RootElement.Clone();
        }

        /// <inheritdoc />
        public override string ToString() => Event + ": " + Data;
    }
}
