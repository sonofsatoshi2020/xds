using System.ComponentModel.DataAnnotations;
using NBitcoin;

namespace UnnamedCoin.Bitcoin.Features.Wallet.Validations
{
    public class IsBitcoinAddressAttribute : ValidationAttribute
    {
        /// <summary>
        ///     Determines whether this field is optionally validated. If set to false, the address will be only be validated if it
        ///     is not null.
        ///     Defaults to true.
        /// </summary>
        public bool Required { get; set; } = true;

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (!this.Required && value == null) return ValidationResult.Success;

            var network = (Network) validationContext.GetService(typeof(Network));
            try
            {
                BitcoinAddress.Create(value as string, network);
                return ValidationResult.Success;
            }
            catch
            {
                return new ValidationResult("Invalid address");
            }
        }
    }
}