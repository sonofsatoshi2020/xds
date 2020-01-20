using System;
using System.IO;
using System.Text;
using DBreeze;
using UnnamedCoin.Bitcoin.Configuration;
using UnnamedCoin.Bitcoin.Utilities.JsonConverters;

namespace UnnamedCoin.Bitcoin.Utilities
{
    /// <summary>Allows saving and loading single values to and from key-value storage.</summary>
    public interface IKeyValueRepository : IDisposable
    {
        /// <summary>Persists byte array to the database.</summary>
        void SaveBytes(string key, byte[] bytes);

        /// <summary>Persists any object that <see cref="DBreezeSerializer" /> can serialize to the database.</summary>
        void SaveValue<T>(string key, T value);

        /// <summary>Persists any object to the database. Object is stored as JSON.</summary>
        void SaveValueJson<T>(string key, T value);

        /// <summary>Loads byte array from the database.</summary>
        byte[] LoadBytes(string key);

        /// <summary>Loads an object that <see cref="DBreezeSerializer" /> can deserialize from the database.</summary>
        T LoadValue<T>(string key);

        /// <summary>Loads JSON from the database and deserializes it.</summary>
        T LoadValueJson<T>(string key);
    }

    public class KeyValueRepository : IKeyValueRepository
    {
        const string TableName = "common";

        /// <summary>Access to DBreeze database.</summary>
        readonly DBreezeEngine dbreeze;

        readonly DBreezeSerializer dBreezeSerializer;

        public KeyValueRepository(DataFolder dataFolder, DBreezeSerializer dBreezeSerializer) : this(
            dataFolder.KeyValueRepositoryPath, dBreezeSerializer)
        {
        }

        public KeyValueRepository(string folder, DBreezeSerializer dBreezeSerializer)
        {
            Directory.CreateDirectory(folder);
            this.dbreeze = new DBreezeEngine(folder);
            this.dBreezeSerializer = dBreezeSerializer;
        }

        /// <inheritdoc />
        public void SaveBytes(string key, byte[] bytes)
        {
            var keyBytes = Encoding.ASCII.GetBytes(key);

            using (var transaction = this.dbreeze.GetTransaction())
            {
                transaction.Insert(TableName, keyBytes, bytes);

                transaction.Commit();
            }
        }

        /// <inheritdoc />
        public void SaveValue<T>(string key, T value)
        {
            SaveBytes(key, this.dBreezeSerializer.Serialize(value));
        }

        /// <inheritdoc />
        public void SaveValueJson<T>(string key, T value)
        {
            var json = Serializer.ToString(value);
            var jsonBytes = Encoding.ASCII.GetBytes(json);

            SaveBytes(key, jsonBytes);
        }

        /// <inheritdoc />
        public byte[] LoadBytes(string key)
        {
            var keyBytes = Encoding.ASCII.GetBytes(key);

            using (var transaction = this.dbreeze.GetTransaction())
            {
                transaction.ValuesLazyLoadingIsOn = false;

                var row = transaction.Select<byte[], byte[]>(TableName, keyBytes);

                if (!row.Exists)
                    return null;

                return row.Value;
            }
        }

        /// <inheritdoc />
        public T LoadValue<T>(string key)
        {
            var bytes = LoadBytes(key);

            if (bytes == null)
                return default;

            var value = this.dBreezeSerializer.Deserialize<T>(bytes);
            return value;
        }

        /// <inheritdoc />
        public T LoadValueJson<T>(string key)
        {
            var bytes = LoadBytes(key);

            if (bytes == null)
                return default;

            var json = Encoding.ASCII.GetString(bytes);

            var value = Serializer.ToObject<T>(json);

            return value;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.dbreeze.Dispose();
        }
    }
}