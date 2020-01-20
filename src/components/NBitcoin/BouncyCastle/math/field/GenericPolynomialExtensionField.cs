using System;

namespace NBitcoin.BouncyCastle.math.field
{
    class GenericPolynomialExtensionField
        : IPolynomialExtensionField
    {
        protected readonly IPolynomial minimalPolynomial;
        protected readonly IFiniteField subfield;

        internal GenericPolynomialExtensionField(IFiniteField subfield, IPolynomial polynomial)
        {
            this.subfield = subfield;
            this.minimalPolynomial = polynomial;
        }

        public virtual BigInteger Characteristic => this.subfield.Characteristic;

        public virtual int Dimension => this.subfield.Dimension * this.minimalPolynomial.Degree;

        public virtual IFiniteField Subfield => this.subfield;

        public virtual int Degree => this.minimalPolynomial.Degree;

        public virtual IPolynomial MinimalPolynomial => this.minimalPolynomial;

        public override bool Equals(object obj)
        {
            if (this == obj) return true;
            var other = obj as GenericPolynomialExtensionField;
            if (null == other) return false;
            return this.subfield.Equals(other.subfield) && this.minimalPolynomial.Equals(other.minimalPolynomial);
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }
    }
}