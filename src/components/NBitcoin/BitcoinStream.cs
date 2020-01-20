using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using NBitcoin.Protocol;

namespace NBitcoin
{
    public enum SerializationType
    {
        Disk,
        Network,
        Hash
    }

    public class Scope : IDisposable
    {
        readonly Action close;

        public Scope(Action open, Action close)
        {
            this.close = close;
            open();
        }

        public static IDisposable Nothing
        {
            get { return new Scope(() => { }, () => { }); }
        }

        #region IDisposable Members

        public void Dispose()
        {
            this.close();
        }

        #endregion
    }

    // TODO: Make NetworkOptions required in the constructors of this class.
    public partial class BitcoinStream
    {
        //ReadWrite<T>(ref T data)
        static readonly MethodInfo readWriteTyped;

        PerformanceCounter counter;

        static BitcoinStream()
        {
            readWriteTyped = typeof(BitcoinStream)
                .GetTypeInfo()
                .DeclaredMethods
                .Where(m => m.Name == "ReadWrite")
                .Where(m => m.IsGenericMethodDefinition)
                .Where(m => m.GetParameters().Length == 1)
                .Where(m => m.GetParameters().Any(p =>
                    p.ParameterType.IsByRef && p.ParameterType.HasElementType &&
                    !p.ParameterType.GetElementType().IsArray))
                .First();
        }

        public BitcoinStream(Stream inner, bool serializing)
        {
            this.ConsensusFactory = new DefaultConsensusFactory();
            this.Serializing = serializing;
            this.Inner = inner;
        }

        public int MaxArraySize { get; set; } = 1024 * 1024;

        public Stream Inner { get; }

        public bool Serializing { get; }

        /// <summary>
        ///     Gets the total processed bytes for read or write.
        /// </summary>
        public long ProcessedBytes => this.Serializing ? this.Counter.WrittenBytes : this.Counter.ReadBytes;

        public PerformanceCounter Counter
        {
            get
            {
                if (this.counter == null)
                    this.counter = new PerformanceCounter();
                return this.counter;
            }
        }

        public bool IsBigEndian { get; set; }

        public ProtocolVersion ProtocolVersion { get; set; } = ProtocolVersion.PROTOCOL_VERSION;

        public TransactionOptions TransactionOptions { get; set; } = TransactionOptions.All;

        /// <summary>
        ///     Set the format to use when serializing and deserializing consensus related types.
        /// </summary>
        public ConsensusFactory ConsensusFactory { get; set; }


        public SerializationType Type { get; set; }

        public CancellationToken ReadCancellationToken { get; set; }

        public Script ReadWrite(Script data)
        {
            if (this.Serializing)
            {
                var bytes = data == null ? Script.Empty.ToBytes(true) : data.ToBytes(true);
                ReadWriteAsVarString(ref bytes);
                return data;
            }

            var varString = new VarString();
            varString.ReadWrite(this);
            return Script.FromBytesUnsafe(varString.GetString(true));
        }

        public void ReadWrite(ref Script script)
        {
            if (this.Serializing)
                ReadWrite(script);
            else
                script = ReadWrite(script);
        }

        public T ReadWrite<T>(T data) where T : IBitcoinSerializable
        {
            ReadWrite(ref data);
            return data;
        }

        public void ReadWriteAsVarString(ref byte[] bytes)
        {
            if (this.Serializing)
            {
                var str = new VarString(bytes);
                str.ReadWrite(this);
            }
            else
            {
                var str = new VarString();
                str.ReadWrite(this);
                bytes = str.GetString(true);
            }
        }

        public void ReadWrite(Type type, ref object obj)
        {
            try
            {
                var parameters = new[] {obj};
                readWriteTyped.MakeGenericMethod(type).Invoke(this, parameters);
                obj = parameters[0];
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException;
            }
        }

        public void ReadWrite(ref byte data)
        {
            ReadWriteByte(ref data);
        }

        public byte ReadWrite(byte data)
        {
            ReadWrite(ref data);
            return data;
        }

        public void ReadWrite(ref bool data)
        {
            var d = data ? (byte) 1 : (byte) 0;
            ReadWriteByte(ref d);
            data = d == 0 ? false : true;
        }

        public void ReadWriteStruct<T>(ref T data) where T : struct, IBitcoinSerializable
        {
            data.ReadWrite(this);
        }

        public void ReadWriteStruct<T>(T data) where T : struct, IBitcoinSerializable
        {
            data.ReadWrite(this);
        }

        public void ReadWrite<T>(ref T data) where T : IBitcoinSerializable
        {
            var obj = data;
            if (obj == null)
            {
                obj = this.ConsensusFactory.TryCreateNew<T>();
                if (obj == null)
                    obj = Activator.CreateInstance<T>();
            }

            obj.ReadWrite(this);
            if (!this.Serializing)
                data = obj;
        }

        public void ReadWrite<T>(ref List<T> list) where T : IBitcoinSerializable, new()
        {
            ReadWriteList<List<T>, T>(ref list);
        }

        public void ReadWrite<TList, TItem>(ref TList list)
            where TList : List<TItem>, new()
            where TItem : IBitcoinSerializable, new()
        {
            ReadWriteList<TList, TItem>(ref list);
        }

        void ReadWriteList<TList, TItem>(ref TList data)
            where TList : List<TItem>, new()
            where TItem : IBitcoinSerializable, new()
        {
            var dataArray = data == null ? null : data.ToArray();

            if (this.Serializing && dataArray == null) dataArray = new TItem[0];

            ReadWriteArray(ref dataArray);

            if (!this.Serializing)
            {
                if (data == null)
                    data = new TList();
                else
                    data.Clear();
                data.AddRange(dataArray);
            }
        }

        public void ReadWrite(ref byte[] arr)
        {
            ReadWriteBytes(ref arr);
        }

        public void ReadWrite(ref string str)
        {
            if (this.Serializing)
            {
                var bytes = Encoding.ASCII.GetBytes(str);

                this._VarInt.SetValue((ulong) str.Length);
                ReadWrite(ref this._VarInt);

                ReadWriteBytes(ref bytes);
            }
            else
            {
                this._VarInt.SetValue(0);
                ReadWrite(ref this._VarInt);

                var length = this._VarInt.ToLong();

                var bytes = new byte[length];

                ReadWriteBytes(ref bytes, 0, bytes.Length);

                str = Encoding.ASCII.GetString(bytes);
            }
        }

        public void ReadWrite(ref byte[] arr, int offset, int count)
        {
            ReadWriteBytes(ref arr, offset, count);
        }

        public void ReadWrite<T>(ref T[] arr) where T : IBitcoinSerializable, new()
        {
            ReadWriteArray(ref arr);
        }

        void ReadWriteNumber(ref long value, int size)
        {
            var uvalue = unchecked((ulong) value);
            ReadWriteNumber(ref uvalue, size);
            value = unchecked((long) uvalue);
        }

        void ReadWriteNumber(ref ulong value, int size)
        {
            var bytes = new byte[size];

            for (var i = 0; i < size; i++) bytes[i] = (byte) (value >> (i * 8));
            if (this.IsBigEndian)
                Array.Reverse(bytes);
            ReadWriteBytes(ref bytes);
            if (this.IsBigEndian)
                Array.Reverse(bytes);
            ulong valueTemp = 0;
            for (var i = 0; i < bytes.Length; i++)
            {
                var v = (ulong) bytes[i];
                valueTemp += v << (i * 8);
            }

            value = valueTemp;
        }

        void ReadWriteBytes(ref byte[] data, int offset = 0, int count = -1)
        {
            if (data == null) throw new ArgumentNullException("data");

            if (data.Length == 0) return;

            count = count == -1 ? data.Length : count;

            if (count == 0) return;

            if (this.Serializing)
            {
                this.Inner.Write(data, offset, count);
                this.Counter.AddWritten(count);
            }
            else
            {
                var readen = this.Inner.ReadEx(data, offset, count, this.ReadCancellationToken);
                if (readen == 0)
                    throw new EndOfStreamException("No more byte to read");
                this.Counter.AddRead(readen);
            }
        }

        void ReadWriteByte(ref byte data)
        {
            if (this.Serializing)
            {
                this.Inner.WriteByte(data);
                this.Counter.AddWritten(1);
            }
            else
            {
                var readen = this.Inner.ReadByte();
                if (readen == -1)
                    throw new EndOfStreamException("No more byte to read");
                data = (byte) readen;
                this.Counter.AddRead(1);
            }
        }

        public IDisposable BigEndianScope()
        {
            var old = this.IsBigEndian;
            return new Scope(() => { this.IsBigEndian = true; },
                () => { this.IsBigEndian = old; });
        }

        public IDisposable ProtocolVersionScope(ProtocolVersion version)
        {
            var old = this.ProtocolVersion;
            return new Scope(() => { this.ProtocolVersion = version; },
                () => { this.ProtocolVersion = old; });
        }

        public void CopyParameters(BitcoinStream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");
            this.ProtocolVersion = stream.ProtocolVersion;
            this.TransactionOptions = stream.TransactionOptions;
            this.IsBigEndian = stream.IsBigEndian;
            this.MaxArraySize = stream.MaxArraySize;
            this.Type = stream.Type;
        }

        public IDisposable SerializationTypeScope(SerializationType value)
        {
            var old = this.Type;
            return new Scope(() => { this.Type = value; }, () => { this.Type = old; });
        }

        public void ReadWriteAsVarInt(ref uint val)
        {
            ulong vallong = val;
            ReadWriteAsVarInt(ref vallong);
            if (!this.Serializing)
                val = (uint) vallong;
        }

        public void ReadWriteAsVarInt(ref ulong val)
        {
            var value = new VarInt(val);
            ReadWrite(ref value);
            if (!this.Serializing)
                val = value.ToLong();
        }

        public void ReadWriteAsCompactVarInt(ref uint val)
        {
            var value = new CompactVarInt(val, sizeof(uint));
            ReadWrite(ref value);
            if (!this.Serializing)
                val = (uint) value.ToLong();
        }

        public void ReadWriteAsCompactVarInt(ref ulong val)
        {
            var value = new CompactVarInt(val, sizeof(ulong));
            ReadWrite(ref value);
            if (!this.Serializing)
                val = value.ToLong();
        }
    }
}