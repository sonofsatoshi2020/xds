namespace NBitcoin.BouncyCastle.math.ec.multiplier
{
    /**
    * Class holding precomputation data for the WNAF (Window Non-Adjacent Form)
    * algorithm.
    */
    class WNafPreCompInfo
        : PreCompInfo
    {
        /**
         * Array holding the precomputed
         * <code>ECPoint</code>
         * s used for a Window
         * NAF multiplication.
         */
        protected ECPoint[] m_preComp;

        /**
         * Array holding the negations of the precomputed
         * <code>ECPoint</code>
         * s used
         * for a Window NAF multiplication.
         */
        protected ECPoint[] m_preCompNeg;

        /**
         * Holds an
         * <code>ECPoint</code>
         * representing Twice(this). Used for the
         * Window NAF multiplication to create or extend the precomputed values.
         */
        protected ECPoint m_twice;

        public virtual ECPoint[] PreComp
        {
            get => this.m_preComp;
            set => this.m_preComp = value;
        }

        public virtual ECPoint[] PreCompNeg
        {
            get => this.m_preCompNeg;
            set => this.m_preCompNeg = value;
        }

        public virtual ECPoint Twice
        {
            get => this.m_twice;
            set => this.m_twice = value;
        }
    }
}