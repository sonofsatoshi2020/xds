using System;
using System.Linq;
using System.Net;

namespace UnnamedCoin.Bitcoin.Features.RPC
{
    public class RPCCredentialString
    {
        string _CookieFile;

        NetworkCredential _UsernamePassword;

        /// <summary>
        ///     Use default connection settings of the chain
        /// </summary>
        public bool UseDefault => this.CookieFile == null && this.UserPassword == null;


        /// <summary>
        ///     Path to cookie file
        /// </summary>
        public string CookieFile
        {
            get => this._CookieFile;
            set
            {
                if (value != null)
                    Reset();
                this._CookieFile = value;
            }
        }

        /// <summary>
        ///     Username and password
        /// </summary>
        public NetworkCredential UserPassword
        {
            get => this._UsernamePassword;
            set
            {
                if (value != null)
                    Reset();
                this._UsernamePassword = value;
            }
        }

        public static RPCCredentialString Parse(string str)
        {
            RPCCredentialString r;
            if (!TryParse(str, out r))
                throw new FormatException("Invalid RPC Credential string");
            return r;
        }

        public static bool TryParse(string str, out RPCCredentialString connectionString)
        {
            if (str == null)
                throw new ArgumentNullException(nameof(str));
            str = str.Trim();
            if (str.Equals("default", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(str))
            {
                connectionString = new RPCCredentialString();
                return true;
            }

            if (str.StartsWith("cookiefile=", StringComparison.OrdinalIgnoreCase))
            {
                var path = str.Substring("cookiefile=".Length);
                connectionString = new RPCCredentialString();
                connectionString.CookieFile = path;
                return true;
            }

            if (str.IndexOf(':') != -1)
            {
                var parts = str.Split(new[] {':'}, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    parts[1] = string.Join(":", parts.Skip(1).ToArray());
                    connectionString = new RPCCredentialString();
                    connectionString.UserPassword = new NetworkCredential(parts[0], parts[1]);
                    return true;
                }
            }

            connectionString = null;
            return false;
        }

        void Reset()
        {
            this._CookieFile = null;
            this._UsernamePassword = null;
        }

        public override string ToString()
        {
            return this.UseDefault ? "default" :
                this.CookieFile != null ? "cookiefile=" + this.CookieFile :
                this.UserPassword != null ? $"{this.UserPassword.UserName}:{this.UserPassword.Password}" :
                "default";
        }
    }
}