using System;
using NBitcoin.BouncyCastle.math;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;

namespace NBitcoin
{
    public class TransactionSignature
    {
        string _Id;

        public TransactionSignature(ECDSASignature signature, SigHash sigHash)
        {
            if (sigHash == SigHash.Undefined)
                throw new ArgumentException("sigHash should not be Undefined");
            this.SigHash = sigHash;
            this.Signature = signature;
        }

        public TransactionSignature(ECDSASignature signature)
            : this(signature, SigHash.All)
        {
        }

        public TransactionSignature(byte[] sigSigHash)
        {
            this.Signature = ECDSASignature.FromDER(sigSigHash);
            this.SigHash = (SigHash) sigSigHash[sigSigHash.Length - 1];
        }

        public TransactionSignature(byte[] sig, SigHash sigHash)
        {
            this.Signature = ECDSASignature.FromDER(sig);
            this.SigHash = sigHash;
        }

        public static TransactionSignature Empty { get; } =
            new TransactionSignature(new ECDSASignature(BigInteger.ValueOf(0), BigInteger.ValueOf(0)), SigHash.All);

        public ECDSASignature Signature { get; }

        public SigHash SigHash { get; }

        string Id
        {
            get
            {
                if (this._Id == null) this._Id = Encoders.Hex.EncodeData(ToBytes());
                return this._Id;
            }
        }

        public bool IsLowS => this.Signature.IsLowS;

        /// <summary>
        ///     Check if valid transaction signature
        /// </summary>
        /// <param name="network">The blockchain network class.</param>
        /// <param name="sig">Signature in bytes</param>
        /// <param name="scriptVerify">Verification rules</param>
        /// <returns>True if valid</returns>
        public static bool IsValid(Network network, byte[] sig,
            ScriptVerify scriptVerify = ScriptVerify.DerSig | ScriptVerify.StrictEnc)
        {
            ScriptError error;
            return IsValid(network, sig, scriptVerify, out error);
        }


        /// <summary>
        ///     Check if valid transaction signature
        /// </summary>
        /// <param name="network">The blockchain network class.</param>
        /// <param name="sig">The signature</param>
        /// <param name="scriptVerify">Verification rules</param>
        /// <param name="error">Error</param>
        /// <returns>True if valid</returns>
        public static bool IsValid(Network network, byte[] sig, ScriptVerify scriptVerify, out ScriptError error)
        {
            if (sig == null)
                throw new ArgumentNullException("sig");
            if (sig.Length == 0)
            {
                error = ScriptError.SigDer;
                return false;
            }

            error = ScriptError.OK;
            var ctx = new ScriptEvaluationContext(network)
            {
                ScriptVerify = scriptVerify
            };
            if (!ctx.CheckSignatureEncoding(sig))
            {
                error = ctx.Error;
                return false;
            }

            return true;
        }

        public byte[] ToBytes()
        {
            var sig = this.Signature.ToDER();
            var result = new byte[sig.Length + 1];
            Array.Copy(sig, 0, result, 0, sig.Length);
            result[result.Length - 1] = (byte) this.SigHash;
            return result;
        }

        public static bool ValidLength(int length)
        {
            return 67 <= length && length <= 80 || length == 9; //9 = Empty signature
        }

        public bool Check(Network network, PubKey pubKey, Script scriptPubKey, IndexedTxIn txIn,
            ScriptVerify verify = ScriptVerify.Standard)
        {
            return Check(network, pubKey, scriptPubKey, txIn.Transaction, txIn.Index, verify);
        }

        public bool Check(Network network, PubKey pubKey, Script scriptPubKey, Transaction tx, uint nIndex,
            ScriptVerify verify = ScriptVerify.Standard)
        {
            return new ScriptEvaluationContext(network)
            {
                ScriptVerify = verify,
                SigHash = this.SigHash
            }.CheckSig(this, pubKey, scriptPubKey, tx, nIndex);
        }

        public override bool Equals(object obj)
        {
            var item = obj as TransactionSignature;
            if (item == null)
                return false;
            return this.Id.Equals(item.Id);
        }

        public static bool operator ==(TransactionSignature a, TransactionSignature b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if ((object) a == null || (object) b == null)
                return false;
            return a.Id == b.Id;
        }

        public static bool operator !=(TransactionSignature a, TransactionSignature b)
        {
            return !(a == b);
        }

        public override int GetHashCode()
        {
            return this.Id.GetHashCode();
        }

        public override string ToString()
        {
            return Encoders.Hex.EncodeData(ToBytes());
        }


        /// <summary>
        ///     Enforce LowS on the signature
        /// </summary>
        public TransactionSignature MakeCanonical()
        {
            if (this.IsLowS)
                return this;
            return new TransactionSignature(this.Signature.MakeCanonical(), this.SigHash);
        }
    }
}