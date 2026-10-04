using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rewloy
{
    /// <summary>
    /// A request field that can be left out, set to a value or set to
    /// <c>null</c>. A plain nullable property cannot tell "leave it as it is"
    /// from "clear it", and an API that patches a record needs both.
    /// Assign a value directly (<c>Phone = "5321234567"</c>), or
    /// <see cref="Null"/> to send <c>null</c>; an unset field is not sent.
    /// </summary>
    [JsonConverter(typeof(OptionalConverterFactory))]
    public readonly struct Optional<T> : IEquatable<Optional<T>>
    {
        private readonly byte _state; // 0 left out, 1 null, 2 value
        private readonly T _value;

        /// <summary>A field with a value (a <c>null</c> value makes it <see cref="Null"/>).</summary>
        public Optional(T value)
        {
            _state = value is null ? (byte)1 : (byte)2;
            _value = value;
        }

        private Optional(byte state)
        {
            _state = state;
            _value = default!;
        }

        /// <summary>The field is left out: nothing is sent.</summary>
        public static Optional<T> Undefined => default;

        /// <summary>The field is sent as <c>null</c>.</summary>
        public static Optional<T> Null => new Optional<T>(1);

        /// <summary>The field is left out.</summary>
        public bool IsUndefined => _state == 0;

        /// <summary>The field is sent as <c>null</c>.</summary>
        public bool IsNull => _state == 1;

        /// <summary>The field has a value.</summary>
        public bool HasValue => _state == 2;

        /// <summary>The value; throws when the field is left out or null.</summary>
        public T Value => _state == 2 ? _value : throw new InvalidOperationException("The field has no value.");

        /// <summary>A value is an <see cref="Optional{T}"/> with that value.</summary>
        public static implicit operator Optional<T>(T value) => new Optional<T>(value);

        /// <inheritdoc />
        public bool Equals(Optional<T> other) => _state == other._state && System.Collections.Generic.EqualityComparer<T>.Default.Equals(_value, other._value);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => _state == 2 ? System.Collections.Generic.EqualityComparer<T>.Default.GetHashCode(_value!) : _state;

        /// <inheritdoc />
        public override string ToString() => _state switch { 0 => "(undefined)", 1 => "null", _ => _value?.ToString() ?? "null" };

        /// <summary>Equal when both are the same state and value.</summary>
        public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

        /// <summary>Not equal.</summary>
        public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);
    }

    internal sealed class OptionalConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

        public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter?)Activator.CreateInstance(typeof(OptionalConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]));
    }

    internal sealed class OptionalConverter<T> : JsonConverter<Optional<T>>
    {
        public override bool HandleNull => true;

        public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return Optional<T>.Null;
            var value = JsonSerializer.Deserialize<T>(ref reader, options);
            return new Optional<T>(value!);
        }

        public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
        {
            if (value.HasValue) JsonSerializer.Serialize(writer, value.Value, options);
            else writer.WriteNullValue();
        }
    }
}
