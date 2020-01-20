using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;

namespace NBitcoin.OpenAsset
{
    public class CoinprismException : Exception
    {
        public CoinprismException()
        {
        }

        public CoinprismException(string message)
            : base(message)
        {
        }

        public CoinprismException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public class CoinprismColoredTransactionRepository : IColoredTransactionRepository
    {
        readonly Network network;

        public CoinprismColoredTransactionRepository(Network network)
        {
            this.network = network;
        }

        class CoinprismTransactionRepository : ITransactionRepository
        {
            #region ITransactionRepository Members

            public Task<Transaction> GetAsync(uint256 txId)
            {
                return Task.FromResult<Transaction>(null);
            }

            public Task PutAsync(uint256 txId, Transaction tx)
            {
                return Task.FromResult(true);
            }

            #endregion
        }

        #region IColoredTransactionRepository Members

        public ITransactionRepository Transactions => new CoinprismTransactionRepository();

        public async Task<ColoredTransaction> GetAsync(uint256 txId)
        {
            try
            {
                var result = new ColoredTransaction();

                var url = string.Empty;
                if (this.network.NetworkType == NetworkType.Testnet || this.network.NetworkType == NetworkType.Regtest)
                    url = string.Format("https://testnet.api.coinprism.com/v1/transactions/{0}", txId);
                else
                    url = string.Format("https://api.coinprism.com/v1/transactions/{0}", txId);

                var req = WebRequest.CreateHttp(url);
                req.Method = "GET";

                using (var response = await req.GetResponseAsync().ConfigureAwait(false))
                {
                    var writer = new StreamReader(response.GetResponseStream());
                    var str = await writer.ReadToEndAsync().ConfigureAwait(false);
                    var json = JObject.Parse(str);
                    var inputs = json["inputs"] as JArray;
                    if (inputs != null)
                        for (var i = 0; i < inputs.Count; i++)
                        {
                            if (inputs[i]["asset_id"].Value<string>() == null)
                                continue;
                            var entry = new ColoredEntry();
                            entry.Index = (uint) i;
                            entry.Asset = new AssetMoney(
                                new BitcoinAssetId(inputs[i]["asset_id"].ToString()).AssetId,
                                inputs[i]["asset_quantity"].Value<ulong>());

                            result.Inputs.Add(entry);
                        }

                    var outputs = json["outputs"] as JArray;
                    if (outputs != null)
                    {
                        var issuance = true;
                        for (var i = 0; i < outputs.Count; i++)
                        {
                            var marker =
                                ColorMarker.TryParse(
                                    new Script(Encoders.Hex.DecodeData(outputs[i]["script"].ToString())));
                            if (marker != null)
                            {
                                issuance = false;
                                result.Marker = marker;
                                continue;
                            }

                            if (outputs[i]["asset_id"].Value<string>() == null)
                                continue;
                            var entry = new ColoredEntry();
                            entry.Index = (uint) i;
                            entry.Asset = new AssetMoney(
                                new BitcoinAssetId(outputs[i]["asset_id"].ToString()).AssetId,
                                outputs[i]["asset_quantity"].Value<ulong>()
                            );

                            if (issuance)
                                result.Issuances.Add(entry);
                            else
                                result.Transfers.Add(entry);
                        }
                    }

                    return result;
                }
            }
            catch (WebException ex)
            {
                try
                {
                    var error = JObject.Parse(new StreamReader(ex.Response.GetResponseStream()).ReadToEnd());
                    if (error["ErrorCode"].ToString() == "InvalidTransactionHash")
                        return null;
                    throw new CoinprismException(error["ErrorCode"].ToString());
                }
                catch (CoinprismException)
                {
                    throw;
                }
                catch
                {
                }

                throw;
            }
        }

        public async Task BroadcastAsync(Transaction transaction)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            var url = string.Empty;
            if (this.network.NetworkType == NetworkType.Testnet || this.network.NetworkType == NetworkType.Regtest)
                url = "https://testnet.api.coinprism.com/v1/sendrawtransaction";
            else
                url = "https://api.coinprism.com/v1/transactions/v1/sendrawtransaction";

            var req = WebRequest.CreateHttp(url);
            req.Method = "POST";
            req.ContentType = "application/json";

            var stream = await req.GetRequestStreamAsync().ConfigureAwait(false);
            var writer = new StreamWriter(stream);
            await writer.WriteAsync("\"" + transaction.ToHex() + "\"").ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
            (await req.GetResponseAsync().ConfigureAwait(false)).Dispose();
        }

        public Task PutAsync(uint256 txId, ColoredTransaction tx)
        {
            return Task.FromResult(false);
        }

        #endregion
    }
}