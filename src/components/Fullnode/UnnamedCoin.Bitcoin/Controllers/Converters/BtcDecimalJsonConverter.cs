﻿using System;
using System.Globalization;
using Newtonsoft.Json;

namespace UnnamedCoin.Bitcoin.Controllers.Converters
{
    /// <summary>
    ///     Converts a decimal value to a string with the minimum number of decimals used by bitcoin (8).
    /// </summary>
    public class BtcDecimalJsonConverter : JsonConverter
    {
        const int MinDecimals = 8;

        public override bool CanRead => false;

        /// <summary>
        ///     Method for writing a string formatted decimal to Json that truncates at <see cref="MinDecimals" /> decimal points.
        /// </summary>
        /// <param name="writer">A <see cref="JsonWriter" /> instance.</param>
        /// <param name="value">The value to be written.</param>
        /// <param name="serializer">A <see cref="JsonSerializer" /> instance.</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var btcDecimal = (decimal) value;
            var result = btcDecimal.ToString(CultureInfo.InvariantCulture);
            if (!result.Contains('.') || result.Split('.')[1].Length < MinDecimals)
                result = btcDecimal.ToString("0." + new string('0', MinDecimals), CultureInfo.InvariantCulture);

            writer.WriteRawValue(result);
        }

        /// <summary>
        ///     A method for reading a string formatted decimal in Json that was truncated at <see cref="MinDecimals" /> decimals.
        /// </summary>
        /// <remarks>Not implemented.</remarks>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(decimal);
        }
    }
}