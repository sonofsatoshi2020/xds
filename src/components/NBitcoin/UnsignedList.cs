using System;
using System.Collections.Generic;

namespace NBitcoin
{
    public class UnsignedList<T> : List<T>
        where T : IBitcoinSerializable, new()
    {
        public UnsignedList()
        {
        }

        public UnsignedList(Transaction parent)
        {
            if (parent == null)
                throw new ArgumentNullException("parent");
            this.Transaction = parent;
        }

        public UnsignedList(IEnumerable<T> collection)
            : base(collection)
        {
        }

        public UnsignedList(int capacity)
            : base(capacity)
        {
        }

        public Transaction Transaction { get; internal set; }

        public T this[uint index]
        {
            get => base[(int) index];
            set => base[(int) index] = value;
        }
    }
}