using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NBitcoin.Formatters
{
    abstract class RawFormatter
    {
        protected RawFormatter(Network network)
        {
            this.Network = network;
        }

        public Network Network { get; set; }

        protected abstract void BuildTransaction(JObject json, Transaction tx);
        protected abstract void WriteTransaction(JsonTextWriter writer, Transaction tx);

        public Transaction ParseJson(string str)
        {
            var obj = JObject.Parse(str);

            return Parse(obj);
        }

        [Obsolete("Use RawFormatter.ParseJson method instead")]
        public Transaction Parse(string str)
        {
            var obj = JObject.Parse(str);
            return Parse(obj);
        }

        public Transaction Parse(JObject obj)
        {
            var tx = new Transaction();
            BuildTransaction(obj, tx);
            return tx;
        }

        protected void WritePropertyValue<TValue>(JsonWriter writer, string name, TValue value)
        {
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }

        public string ToString(Transaction transaction)
        {
            var strWriter = new StringWriter();
            var jsonWriter = new JsonTextWriter(strWriter);
            jsonWriter.Formatting = Formatting.Indented;

            jsonWriter.WriteStartObject();
            WriteTransaction(jsonWriter, transaction);
            jsonWriter.WriteEndObject();

            jsonWriter.Flush();
            return strWriter.ToString();
        }
    }
}