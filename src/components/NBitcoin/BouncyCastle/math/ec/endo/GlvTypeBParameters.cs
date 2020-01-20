namespace NBitcoin.BouncyCastle.math.ec.endo
{
    class GlvTypeBParameters
    {
        protected readonly BigInteger m_beta;
        protected readonly int m_bits;
        protected readonly BigInteger m_g1, m_g2;
        protected readonly BigInteger m_lambda;
        protected readonly BigInteger[] m_v1, m_v2;

        public GlvTypeBParameters(BigInteger beta, BigInteger lambda, BigInteger[] v1, BigInteger[] v2,
            BigInteger g1, BigInteger g2, int bits)
        {
            this.m_beta = beta;
            this.m_lambda = lambda;
            this.m_v1 = v1;
            this.m_v2 = v2;
            this.m_g1 = g1;
            this.m_g2 = g2;
            this.m_bits = bits;
        }

        public virtual BigInteger Beta => this.m_beta;

        public virtual BigInteger Lambda => this.m_lambda;

        public virtual BigInteger[] V1 => this.m_v1;

        public virtual BigInteger[] V2 => this.m_v2;

        public virtual BigInteger G1 => this.m_g1;

        public virtual BigInteger G2 => this.m_g2;

        public virtual int Bits => this.m_bits;
    }
}