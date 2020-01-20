using System;
using System.Collections;
using System.IO;
using NBitcoin.BouncyCastle.util;

namespace NBitcoin.BouncyCastle.asn1
{
    abstract class Asn1Sequence
        : Asn1Object, IEnumerable
    {
        readonly IList seq;

        protected internal Asn1Sequence(
            int capacity)
        {
            this.seq = Platform.CreateArrayList(capacity);
        }

        public virtual Asn1SequenceParser Parser => new Asn1SequenceParserImpl(this);

        /**
         * return the object at the sequence position indicated by index.
         *
         * @param index the sequence number (starting at zero) of the object
         * @return the object at the sequence position indicated by index.
         */
        public virtual Asn1Encodable this[int index] => (Asn1Encodable) this.seq[index];

        [Obsolete("Use 'Count' property instead")]
        public int Size => this.Count;

        public virtual int Count => this.seq.Count;

        public virtual IEnumerator GetEnumerator()
        {
            return this.seq.GetEnumerator();
        }

        /**
         * return an Asn1Sequence from the given object.
         *
         * @param obj the object we want converted.
         * @exception ArgumentException if the object cannot be converted.
         */
        public static Asn1Sequence GetInstance(
            object obj)
        {
            if (obj == null || obj is Asn1Sequence) return (Asn1Sequence) obj;

            if (obj is Asn1SequenceParser) return GetInstance(((Asn1SequenceParser) obj).ToAsn1Object());

            if (obj is byte[])
                try
                {
                    return GetInstance(FromByteArray((byte[]) obj));
                }
                catch (IOException e)
                {
                    throw new ArgumentException("failed to construct sequence from byte[]: " + e.Message);
                }

            if (obj is Asn1Encodable)
            {
                var primitive = ((Asn1Encodable) obj).ToAsn1Object();

                if (primitive is Asn1Sequence) return (Asn1Sequence) primitive;
            }

            throw new ArgumentException("Unknown object in GetInstance: " + Platform.GetTypeName(obj), "obj");
        }

        [Obsolete("Use GetEnumerator() instead")]
        public IEnumerator GetObjects()
        {
            return GetEnumerator();
        }

        [Obsolete("Use 'object[index]' syntax instead")]
        public Asn1Encodable GetObjectAt(
            int index)
        {
            return this[index];
        }

        protected override int Asn1GetHashCode()
        {
            var hc = this.Count;

            foreach (var o in this)
            {
                hc *= 17;
                if (o == null)
                    hc ^= DerNull.Instance.GetHashCode();
                else
                    hc ^= o.GetHashCode();
            }

            return hc;
        }

        protected override bool Asn1Equals(
            Asn1Object asn1Object)
        {
            var other = asn1Object as Asn1Sequence;

            if (other == null)
                return false;

            if (this.Count != other.Count)
                return false;

            var s1 = GetEnumerator();
            var s2 = other.GetEnumerator();

            while (s1.MoveNext() && s2.MoveNext())
            {
                var o1 = GetCurrent(s1).ToAsn1Object();
                var o2 = GetCurrent(s2).ToAsn1Object();

                if (!o1.Equals(o2))
                    return false;
            }

            return true;
        }

        Asn1Encodable GetCurrent(IEnumerator e)
        {
            var encObj = (Asn1Encodable) e.Current;

            // unfortunately null was allowed as a substitute for DER null
            if (encObj == null)
                return DerNull.Instance;

            return encObj;
        }

        protected internal void AddObject(
            Asn1Encodable obj)
        {
            this.seq.Add(obj);
        }

        class Asn1SequenceParserImpl
            : Asn1SequenceParser
        {
            readonly int max;
            readonly Asn1Sequence outer;
            int index;

            public Asn1SequenceParserImpl(
                Asn1Sequence outer)
            {
                this.outer = outer;
                this.max = outer.Count;
            }

            public IAsn1Convertible ReadObject()
            {
                if (this.index == this.max)
                    return null;

                var obj = this.outer[this.index++];

                if (obj is Asn1Sequence)
                    return ((Asn1Sequence) obj).Parser;

                // NB: Asn1OctetString implements Asn1OctetStringParser directly
                //                if (obj is Asn1OctetString)
                //                    return ((Asn1OctetString)obj).Parser;

                return obj;
            }

            public Asn1Object ToAsn1Object()
            {
                return this.outer;
            }
        }
    }
}