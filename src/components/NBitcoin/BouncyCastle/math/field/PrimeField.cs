namespace NBitcoin.BouncyCastle.math.field
{
    class PrimeField
        : IFiniteField
    {
        protected readonly BigInteger characteristic;

        internal PrimeField(BigInteger characteristic)
        {
            this.characteristic = characteristic;
        }

        public virtual BigInteger Characteristic => this.characteristic;

        public virtual int Dimension => 1;

        public override bool Equals(object obj)
        {
            if (this == obj) return true;
            var other = obj as PrimeField;
            if (null == other) return false;
            return this.characteristic.Equals(other.characteristic);
        }

        public override int GetHashCode()
        {
            return this.characteristic.GetHashCode();
        }
    }
}