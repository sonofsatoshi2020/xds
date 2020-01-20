using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NBitcoin
{
    public class CachedNoSqlRepository : NoSqlRepository
    {
        readonly HashSet<string> _Added = new HashSet<string>();
        readonly HashSet<string> _Removed = new HashSet<string>();

        readonly Dictionary<string, byte[]> _Table = new Dictionary<string, byte[]>();
        readonly ReaderWriterLock @lock = new ReaderWriterLock();

        public CachedNoSqlRepository(NoSqlRepository inner) : base(inner.Network)
        {
            this.InnerRepository = inner;
        }

        public NoSqlRepository InnerRepository { get; }

        public override async Task PutBatch(IEnumerable<Tuple<string, IBitcoinSerializable>> values)
        {
            await base.PutBatch(values).ConfigureAwait(false);
            await this.InnerRepository.PutBatch(values).ConfigureAwait(false);
        }

        protected override Task PutBytesBatch(IEnumerable<Tuple<string, byte[]>> enumerable)
        {
            using (this.@lock.LockWrite())
            {
                foreach (var data in enumerable)
                    if (data.Item2 == null)
                    {
                        this._Table.Remove(data.Item1);
                        this._Removed.Add(data.Item1);
                        this._Added.Remove(data.Item1);
                    }
                    else
                    {
                        this._Table.AddOrReplace(data.Item1, data.Item2);
                        this._Removed.Remove(data.Item1);
                        this._Added.Add(data.Item1);
                    }
            }

            return Task.FromResult(true);
        }

        protected override async Task<byte[]> GetBytes(string key)
        {
            byte[] result = null;
            bool found;
            using (this.@lock.LockRead())
            {
                found = this._Table.TryGetValue(key, out result);
            }

            if (!found)
            {
                var raw = await this.InnerRepository.GetAsync<Raw>(key).ConfigureAwait(false);
                if (raw != null)
                {
                    result = raw.Data;
                    using (this.@lock.LockWrite())
                    {
                        this._Table.AddOrReplace(key, raw.Data);
                    }
                }
            }

            return result;
        }

        class Raw : IBitcoinSerializable
        {
            byte[] _Data = new byte[0];

            public byte[] Data => this._Data;

            #region IBitcoinSerializable Members

            public void ReadWrite(BitcoinStream stream)
            {
                stream.ReadWriteAsVarString(ref this._Data);
            }

            #endregion
        }
    }
}