using System;
using NBitcoin.BouncyCastle.util;

namespace NBitcoin.BouncyCastle.crypto.parameters
{
    abstract class ECKeyParameters
        : AsymmetricKeyParameter
    {
        static readonly string[] algorithms = {"EC", "ECDSA", "ECDH", "ECDHC", "ECGOST3410", "ECMQV"};

        protected ECKeyParameters(
            string algorithm,
            bool isPrivate,
            ECDomainParameters parameters)
            : base(isPrivate)
        {
            if (algorithm == null)
                throw new ArgumentNullException("algorithm");
            if (parameters == null)
                throw new ArgumentNullException("parameters");

            this.AlgorithmName = VerifyAlgorithmName(algorithm);
            this.Parameters = parameters;
        }

        public string AlgorithmName { get; }

        public ECDomainParameters Parameters { get; }

        public override bool Equals(
            object obj)
        {
            if (obj == this)
                return true;

            var other = obj as ECDomainParameters;

            if (other == null)
                return false;

            return Equals(other);
        }

        protected bool Equals(
            ECKeyParameters other)
        {
            return this.Parameters.Equals(other.Parameters) && base.Equals(other);
        }

        public override int GetHashCode()
        {
            return this.Parameters.GetHashCode() ^ base.GetHashCode();
        }

        internal static string VerifyAlgorithmName(string algorithm)
        {
            var upper = Platform.ToUpperInvariant(algorithm);
            if (Array.IndexOf(algorithms, algorithm, 0, algorithms.Length) < 0)
                throw new ArgumentException("unrecognised algorithm: " + algorithm, "algorithm");
            return upper;
        }
    }
}