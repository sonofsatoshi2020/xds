namespace NBitcoin.BouncyCastle.crypto
{
    abstract class AsymmetricKeyParameter
        : ICipherParameters
    {
        protected AsymmetricKeyParameter(
            bool privateKey)
        {
            this.IsPrivate = privateKey;
        }

        public bool IsPrivate { get; }

        public override bool Equals(
            object obj)
        {
            var other = obj as AsymmetricKeyParameter;

            if (other == null) return false;

            return Equals(other);
        }

        protected bool Equals(
            AsymmetricKeyParameter other)
        {
            return this.IsPrivate == other.IsPrivate;
        }

        public override int GetHashCode()
        {
            return this.IsPrivate.GetHashCode();
        }
    }
}