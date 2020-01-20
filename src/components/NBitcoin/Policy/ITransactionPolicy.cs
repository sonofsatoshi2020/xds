using System;
using System.Linq;

namespace NBitcoin.Policy
{
    public class TransactionPolicyError
    {
        readonly string _Message;

        public TransactionPolicyError()
            : this(null)
        {
        }

        public TransactionPolicyError(string message)
        {
            this._Message = message;
        }

        public override string ToString()
        {
            return this._Message;
        }
    }

    public class TransactionSizePolicyError : TransactionPolicyError
    {
        public TransactionSizePolicyError(int actualSize, int maximumSize)
            : base("Transaction's size is too high. Actual value is " + actualSize + ", but the maximum is " +
                   maximumSize)
        {
            this.ActualSize = actualSize;
            this.MaximumSize = maximumSize;
        }

        public int ActualSize { get; }

        public int MaximumSize { get; }
    }

    public class FeeTooHighPolicyError : TransactionPolicyError
    {
        public FeeTooHighPolicyError(Money fees, Money max)
            : base("Fee too high, actual is " + fees.ToString() + ", policy maximum is " + max.ToString())
        {
            this.ExpectedMaxFee = max;
            this.Fee = fees;
        }

        public Money Fee { get; }

        public Money ExpectedMaxFee { get; }
    }

    public class DustPolicyError : TransactionPolicyError
    {
        public DustPolicyError(Money value, Money dust)
            : base("Dust output detected, output value is " + value.ToString() + ", policy minimum is " +
                   dust.ToString())
        {
            this.Value = value;
            this.DustThreshold = dust;
        }

        public Money Value { get; }

        public Money DustThreshold { get; }
    }

    public class FeeTooLowPolicyError : TransactionPolicyError
    {
        public FeeTooLowPolicyError(Money fees, Money min)
            : base($"Fee of {fees} is too low. The policy minimum is {min}.")
        {
            this.ExpectedMinFee = min;
            this.Fee = fees;
        }

        public Money Fee { get; }

        public Money ExpectedMinFee { get; }
    }

    public class InputPolicyError : TransactionPolicyError
    {
        public InputPolicyError(string message, IndexedTxIn txIn)
            : base(message)
        {
            this.OutPoint = txIn.PrevOut;
            this.InputIndex = txIn.Index;
        }

        public OutPoint OutPoint { get; }

        public uint InputIndex { get; }
    }

    public class DuplicateInputPolicyError : TransactionPolicyError
    {
        public DuplicateInputPolicyError(IndexedTxIn[] duplicated)
            : base("Duplicate input " + duplicated[0].PrevOut)
        {
            this.OutPoint = duplicated[0].PrevOut;
            this.InputIndices = duplicated.Select(d => d.Index).ToArray();
        }

        public OutPoint OutPoint { get; }

        public uint[] InputIndices { get; }
    }

    public class OutputPolicyError : TransactionPolicyError
    {
        public OutputPolicyError(string message, int outputIndex) :
            base(message)
        {
            this.OutputIndex = outputIndex;
        }

        public int OutputIndex { get; }
    }

    public class CoinNotFoundPolicyError : InputPolicyError
    {
        readonly IndexedTxIn _TxIn;

        public CoinNotFoundPolicyError(IndexedTxIn txIn)
            : base("No coin matching " + txIn.PrevOut + " was found", txIn)
        {
            this._TxIn = txIn;
        }

        internal Exception AsException()
        {
            return new CoinNotFoundException(this._TxIn);
        }
    }

    public class ScriptPolicyError : InputPolicyError
    {
        public ScriptPolicyError(IndexedTxIn input, ScriptError error, ScriptVerify scriptVerify, Script scriptPubKey)
            : base("Script error on input " + input.Index + " (" + error + ")", input)
        {
            this.ScriptError = error;
            this.ScriptVerify = scriptVerify;
            this.ScriptPubKey = scriptPubKey;
        }

        public ScriptError ScriptError { get; }

        public ScriptVerify ScriptVerify { get; }

        public Script ScriptPubKey { get; }
    }

    public interface ITransactionPolicy
    {
        /// <summary>
        ///     Check if the given transaction violate the policy
        /// </summary>
        /// <param name="transaction">The transaction</param>
        /// <param name="spentCoins">The previous coins</param>
        /// <returns>Policy errors</returns>
        TransactionPolicyError[] Check(Transaction transaction, ICoin[] spentCoins);
    }
}