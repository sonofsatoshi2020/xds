using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin.BouncyCastle.math;
using NBitcoin.DataEncoders;
using NBitcoin.OpenAsset;
using NBitcoin.Protocol;

namespace NBitcoin
{
    public static class Extensions
    {
        public static Block GetBlock(this INBitcoinBlockRepository repository, uint256 blockId)
        {
            return repository.GetBlockAsync(blockId).GetAwaiter().GetResult();
        }


        public static T ToNetwork<T>(this T obj, Network network) where T : IBitcoinString
        {
            if (network == null)
                throw new ArgumentNullException("network");
            if (obj == null)
                throw new ArgumentNullException("obj");
            if (obj.Network == network)
                return obj;
            if (obj is IBase58Data)
            {
                var b58 = (IBase58Data) obj;
                if (b58.Type != Base58Type.COLORED_ADDRESS)
                {
                    var version = network.GetVersionBytes(b58.Type, true);
                    var inner = Encoders.Base58Check.DecodeData(b58.ToString()).Skip(version.Length).ToArray();
                    var newBase58 = Encoders.Base58Check.EncodeData(version.Concat(inner).ToArray());
                    return Network.Parse<T>(newBase58, network);
                }

                var colored = BitcoinColoredAddress.GetWrappedBase58(obj.ToString(), obj.Network);
                var address = Network.Parse<BitcoinAddress>(colored, obj.Network).ToNetwork(network);
                return (T) (object) address.ToColoredAddress();
            }

            if (obj is IBech32Data)
            {
                var b32 = (IBech32Data) obj;
                var encoder = b32.Network.GetBech32Encoder(b32.Type, true);
                byte wit;
                var data = encoder.Decode(b32.ToString(), out wit);
                encoder = network.GetBech32Encoder(b32.Type, true);
                var str = encoder.Encode(wit, data);
                return Network.Parse<T>(str, network);
            }

            throw new NotSupportedException();
        }

        public static byte[] ReadBytes(this Stream stream, int bytesToRead)
        {
            var buffer = new byte[bytesToRead];
            var num = 0;
            int num2;
            do
            {
                num += num2 = stream.Read(buffer, num, bytesToRead - num);
            } while (num2 > 0 && num < bytesToRead);

            return buffer;
        }

        public static async Task<byte[]> ReadBytesAsync(this Stream stream, int bytesToRead)
        {
            var buffer = new byte[bytesToRead];
            var num = 0;
            int num2;
            do
            {
                num += num2 = await stream.ReadAsync(buffer, num, bytesToRead - num).ConfigureAwait(false);
            } while (num2 > 0 && num < bytesToRead);

            return buffer;
        }

        public static int ReadBytes(this Stream stream, int count, out byte[] result)
        {
            result = new byte[count];
            return stream.Read(result, 0, count);
        }

        public static IEnumerable<T> Resize<T>(this List<T> list, int count)
        {
            if (list.Count == count)
                return new T[0];

            var removed = new List<T>();

            for (var i = list.Count - 1; i + 1 > count; i--)
            {
                removed.Add(list[i]);
                list.RemoveAt(i);
            }

            while (list.Count < count) list.Add(default);
            return removed;
        }

        public static IEnumerable<List<T>> Partition<T>(this IEnumerable<T> source, int max)
        {
            return Partition(source, () => max);
        }

        public static IEnumerable<List<T>> Partition<T>(this IEnumerable<T> source, Func<int> max)
        {
            var partitionSize = max();
            var toReturn = new List<T>(partitionSize);
            foreach (var item in source)
            {
                toReturn.Add(item);
                if (toReturn.Count == partitionSize)
                {
                    yield return toReturn;
                    partitionSize = max();
                    toReturn = new List<T>(partitionSize);
                }
            }

            if (toReturn.Any()) yield return toReturn;
        }

#if !NETCORE
        public static int ReadEx(this Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellation
 = default(CancellationToken))
        {
            if(stream == null)
                throw new ArgumentNullException("stream");
            if(buffer == null)
                throw new ArgumentNullException("buffer");
            if(offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException("offset");
            if(count <= 0 || count > buffer.Length)
                throw new ArgumentOutOfRangeException("count"); //Disallow 0 as a debugging aid.
            if(offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException("count");

            int totalReadCount = 0;

            while(totalReadCount < count)
            {
                cancellation.ThrowIfCancellationRequested();

                int currentReadCount;

                //Big performance problem with BeginRead for other stream types than NetworkStream.
                //Only take the slow path if cancellation is possible.
                if(stream is NetworkStream && cancellation.CanBeCanceled)
                {
                    var ar = stream.BeginRead(buffer, offset + totalReadCount, count - totalReadCount, null, null);
                    if(!ar.CompletedSynchronously)
                    {
                        WaitHandle.WaitAny(new WaitHandle[] { ar.AsyncWaitHandle, cancellation.WaitHandle }, -1);
                    }

                    //EndRead might block, so we need to test cancellation before calling it.
                    //This also is a bug because calling EndRead after BeginRead is contractually required.
                    //A potential fix is to use the ReadAsync API. Another fix is to register a callback with BeginRead that calls EndRead in all cases.
                    cancellation.ThrowIfCancellationRequested();

                    currentReadCount = stream.EndRead(ar);
                }
                else
                {
                    //IO interruption not supported in this path.
                    currentReadCount = stream.Read(buffer, offset + totalReadCount, count - totalReadCount);
                }

                if(currentReadCount == 0)
                    return 0;

                totalReadCount += currentReadCount;
            }

            return totalReadCount;
        }
#else

        public static int ReadEx(this Stream stream, byte[] buffer, int offset, int count,
            CancellationToken cancellation = default)
        {
            if (stream == null) throw new ArgumentNullException("stream");
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || offset > buffer.Length) throw new ArgumentOutOfRangeException("offset");
            if (count <= 0 || count > buffer.Length)
                throw new ArgumentOutOfRangeException("count"); //Disallow 0 as a debugging aid.
            if (offset > buffer.Length - count) throw new ArgumentOutOfRangeException("count");

            //IO interruption not supported on these platforms.

            var totalReadCount = 0;

            while (totalReadCount < count)
            {
                cancellation.ThrowIfCancellationRequested();
                var currentReadCount = 0;

                if (stream is NetworkStream && cancellation.CanBeCanceled)
                    currentReadCount = stream
                        .ReadAsync(buffer, offset + totalReadCount, count - totalReadCount, cancellation).GetAwaiter()
                        .GetResult();
                else
                    currentReadCount = stream.Read(buffer, offset + totalReadCount, count - totalReadCount);
                if (currentReadCount == 0)
                    return 0;
                totalReadCount += currentReadCount;
            }

            return totalReadCount;
        }
#endif
        public static void AddOrReplace<TKey, TValue>(this IDictionary<TKey, TValue> dico, TKey key, TValue value)
        {
            if (dico.ContainsKey(key))
                dico[key] = value;
            else
                dico.Add(key, value);
        }

        public static TValue TryGet<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
        {
            TValue value;
            dictionary.TryGetValue(key, out value);
            return value;
        }

        public static bool TryAdd<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (!dictionary.ContainsKey(key))
            {
                dictionary.Add(key, value);
                return true;
            }

            return false;
        }
    }

    public static class ByteArrayExtensions
    {
        internal static bool StartWith(this byte[] data, byte[] versionBytes)
        {
            if (data.Length < versionBytes.Length)
                return false;
            for (var i = 0; i < versionBytes.Length; i++)
                if (data[i] != versionBytes[i])
                    return false;
            return true;
        }

        public static byte[] SafeSubarray(this byte[] array, int offset, int count)
        {
            if (array == null)
                throw new ArgumentNullException("array");
            if (offset < 0 || offset > array.Length)
                throw new ArgumentOutOfRangeException("offset");
            if (count < 0 || offset + count > array.Length)
                throw new ArgumentOutOfRangeException("count");
            if (offset == 0 && array.Length == count)
                return array;
            var data = new byte[count];
            Buffer.BlockCopy(array, offset, data, 0, count);
            return data;
        }

        internal static byte[] SafeSubarray(this byte[] array, int offset)
        {
            if (array == null)
                throw new ArgumentNullException("array");
            if (offset < 0 || offset > array.Length)
                throw new ArgumentOutOfRangeException("offset");

            var count = array.Length - offset;
            var data = new byte[count];
            Buffer.BlockCopy(array, offset, data, 0, count);
            return data;
        }

        internal static byte[] Concat(this byte[] arr, params byte[][] arrs)
        {
            var len = arr.Length + arrs.Sum(a => a.Length);
            var ret = new byte[len];
            Buffer.BlockCopy(arr, 0, ret, 0, arr.Length);
            var pos = arr.Length;
            foreach (var a in arrs)
            {
                Buffer.BlockCopy(a, 0, ret, pos, a.Length);
                pos += a.Length;
            }

            return ret;
        }
    }

    public class Utils
    {
        internal static string BITCOIN_SIGNED_MESSAGE_HEADER = "Bitcoin Signed Message:\n";

        internal static byte[] BITCOIN_SIGNED_MESSAGE_HEADER_BYTES =
            Encoding.UTF8.GetBytes(BITCOIN_SIGNED_MESSAGE_HEADER);

        static readonly TraceSource _TraceSource = new TraceSource("NBitcoin");


        static readonly DateTimeOffset unixRef = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static void SafeSet(ManualResetEvent ar)
        {
            try
            {
#if !NETCORE
                if(!ar.SafeWaitHandle.IsClosed && !ar.SafeWaitHandle.IsInvalid)
                    ar.Set();
#else
                ar.Set();
#endif
            }
            catch
            {
            }
        }

        public static bool ArrayEqual(byte[] a, byte[] b)
        {
            if (a == null && b == null)
                return true;
            if (a == null)
                return false;
            if (b == null)
                return false;
            return ArrayEqual(a, 0, b, 0, Math.Max(a.Length, b.Length));
        }

        public static bool ArrayEqual(byte[] a, int startA, byte[] b, int startB, int length)
        {
            if (a == null && b == null)
                return true;
            if (a == null)
                return false;
            if (b == null)
                return false;
            var alen = a.Length - startA;
            var blen = b.Length - startB;

            if (alen < length || blen < length)
                return false;

            for (int ai = startA, bi = startB; ai < startA + length; ai++, bi++)
                if (a[ai] != b[bi])
                    return false;
            return true;
        }

        //http://bitcoinj.googlecode.com/git-history/keychain/core/src/main/java/com/google/bitcoin/core/Utils.java
        internal static byte[] FormatMessageForSigning(byte[] messageBytes)
        {
            var ms = new MemoryStream();

            ms.WriteByte((byte) BITCOIN_SIGNED_MESSAGE_HEADER_BYTES.Length);
            Write(ms, BITCOIN_SIGNED_MESSAGE_HEADER_BYTES);

            var size = new VarInt((ulong) messageBytes.Length);
            Write(ms, size.ToBytes());
            Write(ms, messageBytes);
            return ms.ToArray();
        }

        internal static IPAddress MapToIPv6(IPAddress address)
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return address;
            if (address.AddressFamily != AddressFamily.InterNetwork)
                throw new Exception("Only AddressFamily.InterNetworkV4 can be converted to IPv6");

            var ipv4Bytes = address.GetAddressBytes();
            var ipv6Bytes = new byte[16]
            {
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF,
                ipv4Bytes[0], ipv4Bytes[1], ipv4Bytes[2], ipv4Bytes[3]
            };
            return new IPAddress(ipv6Bytes);
        }

        internal static bool IsIPv4MappedToIPv6(IPAddress address)
        {
            if (address.AddressFamily != AddressFamily.InterNetworkV6)
                return false;

            var bytes = address.GetAddressBytes();

            for (var i = 0; i < 10; i++)
                if (bytes[0] != 0)
                    return false;
            return bytes[10] == 0xFF && bytes[11] == 0xFF;
        }

        static void Write(MemoryStream ms, byte[] bytes)
        {
            ms.Write(bytes, 0, bytes.Length);
        }

        internal static Array BigIntegerToBytes(BigInteger b, int numBytes)
        {
            if (b == null) return null;
            var bytes = new byte[numBytes];
            var biBytes = b.ToByteArray();
            var start = biBytes.Length == numBytes + 1 ? 1 : 0;
            var length = Math.Min(biBytes.Length, numBytes);
            Array.Copy(biBytes, start, bytes, numBytes - length, length);
            return bytes;
        }

        public static byte[] BigIntegerToBytes(BigInteger num)
        {
            if (num.Equals(BigInteger.Zero))
                //Positive 0 is represented by a null-length vector
                return new byte[0];

            var isPositive = true;
            if (num.CompareTo(BigInteger.Zero) < 0)
            {
                isPositive = false;
                num = num.Multiply(BigInteger.ValueOf(-1));
            }

            var array = num.ToByteArray();
            Array.Reverse(array);
            if (!isPositive)
                array[array.Length - 1] |= 0x80;
            return array;
        }

        public static BigInteger BytesToBigInteger(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException("data");
            if (data.Length == 0)
                return BigInteger.Zero;
            data = data.ToArray();
            var positive = (data[data.Length - 1] & 0x80) == 0;
            if (!positive)
            {
                data[data.Length - 1] &= unchecked((byte) ~0x80);
                Array.Reverse(data);
                return new BigInteger(1, data).Negate();
            }

            return new BigInteger(1, data);
        }

        internal static bool error(string msg)
        {
            _TraceSource.TraceEvent(TraceEventType.Error, 0, msg);
            return false;
        }

        internal static void log(string msg)
        {
            _TraceSource.TraceEvent(TraceEventType.Information, 0, msg);
        }

        public static uint DateTimeToUnixTime(DateTimeOffset dt)
        {
            return (uint) DateTimeToUnixTimeLong(dt);
        }

        internal static ulong DateTimeToUnixTimeLong(DateTimeOffset dt)
        {
            dt = dt.ToUniversalTime();
            if (dt < unixRef)
                throw new ArgumentOutOfRangeException("The supplied datetime can't be expressed in unix timestamp");
            var result = (dt - unixRef).TotalSeconds;
            if (result > uint.MaxValue)
                throw new ArgumentOutOfRangeException("The supplied datetime can't be expressed in unix timestamp");
            return (ulong) result;
        }

        public static DateTimeOffset UnixTimeToDateTime(uint timestamp)
        {
            var span = TimeSpan.FromSeconds(timestamp);
            return unixRef + span;
        }

        public static DateTimeOffset UnixTimeToDateTime(ulong timestamp)
        {
            var span = TimeSpan.FromSeconds(timestamp);
            return unixRef + span;
        }

        public static DateTimeOffset UnixTimeToDateTime(long timestamp)
        {
            var span = TimeSpan.FromSeconds(timestamp);
            return unixRef + span;
        }


        public static string ExceptionToString(Exception exception)
        {
            var ex = exception;
            var stringBuilder = new StringBuilder(128);
            while (ex != null)
            {
                stringBuilder.Append(ex.GetType().Name);
                stringBuilder.Append(": ");
                stringBuilder.Append(ex.Message);
                stringBuilder.AppendLine(ex.StackTrace);
                ex = ex.InnerException;
                if (ex != null) stringBuilder.Append(" ---> ");
            }

            return stringBuilder.ToString();
        }

        public static void Shuffle<T>(T[] arr, Random rand)
        {
            rand = rand ?? new Random();
            for (var i = 0; i < arr.Length; i++)
            {
                var fromIndex = rand.Next(arr.Length);
                var from = arr[fromIndex];

                var toIndex = rand.Next(arr.Length);
                var to = arr[toIndex];

                arr[toIndex] = from;
                arr[fromIndex] = to;
            }
        }

        public static void Shuffle<T>(List<T> arr, Random rand)
        {
            rand = rand ?? new Random();
            for (var i = 0; i < arr.Count; i++)
            {
                var fromIndex = rand.Next(arr.Count);
                var from = arr[fromIndex];

                var toIndex = rand.Next(arr.Count);
                var to = arr[toIndex];

                arr[toIndex] = from;
                arr[fromIndex] = to;
            }
        }

        public static void Shuffle<T>(T[] arr, int seed)
        {
            var rand = new Random(seed);
            Shuffle(arr, rand);
        }

        public static void Shuffle<T>(T[] arr)
        {
            Shuffle(arr, null);
        }

        public static void SafeCloseSocket(Socket socket)
        {
            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            try
            {
                socket.Dispose();
            }
            catch
            {
            }
        }

        public static IPEndPoint EnsureIPv6(IPEndPoint endpoint)
        {
            if (endpoint.AddressFamily == AddressFamily.InterNetworkV6)
                return endpoint;
            return new IPEndPoint(endpoint.Address.MapToIPv6Ex(), endpoint.Port);
        }

        public static byte[] ToBytes(uint value, bool littleEndian)
        {
            if (littleEndian)
                return new[]
                {
                    (byte) value,
                    (byte) (value >> 8),
                    (byte) (value >> 16),
                    (byte) (value >> 24)
                };
            return new[]
            {
                (byte) (value >> 24),
                (byte) (value >> 16),
                (byte) (value >> 8),
                (byte) value
            };
        }

        public static byte[] ToBytes(ulong value, bool littleEndian)
        {
            if (littleEndian)
                return new[]
                {
                    (byte) value,
                    (byte) (value >> 8),
                    (byte) (value >> 16),
                    (byte) (value >> 24),
                    (byte) (value >> 32),
                    (byte) (value >> 40),
                    (byte) (value >> 48),
                    (byte) (value >> 56)
                };
            return new[]
            {
                (byte) (value >> 56),
                (byte) (value >> 48),
                (byte) (value >> 40),
                (byte) (value >> 32),
                (byte) (value >> 24),
                (byte) (value >> 16),
                (byte) (value >> 8),
                (byte) value
            };
        }

        public static uint ToUInt32(byte[] value, int index, bool littleEndian)
        {
            if (littleEndian)
                return value[index]
                       + ((uint) value[index + 1] << 8)
                       + ((uint) value[index + 2] << 16)
                       + ((uint) value[index + 3] << 24);
            return value[index + 3]
                   + ((uint) value[index + 2] << 8)
                   + ((uint) value[index + 1] << 16)
                   + ((uint) value[index + 0] << 24);
        }


        public static int ToInt32(byte[] value, int index, bool littleEndian)
        {
            return unchecked((int) ToUInt32(value, index, littleEndian));
        }

        public static uint ToUInt32(byte[] value, bool littleEndian)
        {
            return ToUInt32(value, 0, littleEndian);
        }

        internal static ulong ToUInt64(byte[] value, bool littleEndian)
        {
            if (littleEndian)
                return value[0]
                       + ((ulong) value[1] << 8)
                       + ((ulong) value[2] << 16)
                       + ((ulong) value[3] << 24)
                       + ((ulong) value[4] << 32)
                       + ((ulong) value[5] << 40)
                       + ((ulong) value[6] << 48)
                       + ((ulong) value[7] << 56);
            return value[7]
                   + ((ulong) value[6] << 8)
                   + ((ulong) value[5] << 16)
                   + ((ulong) value[4] << 24)
                   + ((ulong) value[3] << 32)
                   + ((ulong) value[2] << 40)
                   + ((ulong) value[1] << 48)
                   + ((ulong) value[0] << 56);
        }

        public static IPAddress ParseIPAddress(string ip)
        {
            IPAddress address = null;

            try
            {
                address = IPAddress.Parse(ip);
            }
            catch (FormatException)
            {
                if (ip.Trim() == string.Empty || Uri.CheckHostName(ip) == UriHostNameType.Unknown)
                    throw;

#if !NETCORE
                address = Dns.GetHostEntry(ip).AddressList[0];
#else
                var adr = DnsLookup(ip).GetAwaiter().GetResult();
                // if not resolved behave like GetHostEntry
                if (adr == string.Empty)
                    throw new SocketException(11001);
                address = IPAddress.Parse(adr);
#endif
            }

            return address;
        }

        public static IPEndPoint ParseIpEndpoint(string endpoint, int port)
        {
            // Get the position of the last ':'.
            var colon = endpoint.LastIndexOf(':');

            if (colon >= 0)
            {
                var bracket = endpoint.LastIndexOf(']');
                var dot = endpoint.LastIndexOf('.');

                // The ':' must be preceded by a ']' or a '.' with no ']' or '.' following the ':'.
                if ((dot != -1 || bracket != -1) && dot < colon && bracket < colon)
                {
                    port = int.Parse(endpoint.Substring(colon + 1));
                    endpoint = endpoint.Substring(0, colon);
                }
            }

            return new IPEndPoint(ParseIPAddress(endpoint), port);
        }

#if NETCORE
        static async Task<string> DnsLookup(string remoteHostName)
        {
            var data = await Dns.GetHostEntryAsync(remoteHostName).ConfigureAwait(false);

            if (data != null && data.AddressList.Count() > 0)
                foreach (var adr in data.AddressList)
                    if (adr != null && adr.IsIPv4())
                        return adr.ToString();
            return string.Empty;
        }
#endif

        public static int GetHashCode(byte[] array)
        {
            unchecked
            {
                if (array == null) return 0;
                var hash = 17;
                for (var i = 0; i < array.Length; i++) hash = hash * 31 + array[i];
                return hash;
            }
        }
    }
}