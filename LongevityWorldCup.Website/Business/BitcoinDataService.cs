using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Business
{
    public sealed class BitcoinDataService
    {
        private const string DonationAddress = "bc1qphwpd3mc9rts7vt4lrxxlxzs5jm3wh33w7hxz7";
        private const int MinConfirmations = 3;
        
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly EventDataService _events;
        private readonly ILogger<BitcoinDataService> _log;

        public BitcoinDataService(IHttpClientFactory httpClientFactory, IMemoryCache cache, EventDataService events, ILogger<BitcoinDataService> log)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _events = events;
            _log = log;
        }

        public async Task<decimal> GetBtcUsdAsync()
        {
            const string cacheKey = "btcToUsdRate";
            if (_cache.TryGetValue(cacheKey, out decimal cachedUsdRate))
                return cachedUsdRate;

            var client = _httpClientFactory.CreateClient();
            try
            {
                var response = await client.GetAsync("https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd");
                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonString);
                    var usd = doc.RootElement.GetProperty("bitcoin").GetProperty("usd").GetDecimal();
                    _cache.Set(cacheKey, usd, TimeSpan.FromMinutes(1));
                    return usd;
                }

                throw new HttpRequestException($"Primary API returned status {response.StatusCode}");
            }
            catch
            {
                try
                {
                    var fallbackResponse = await client.GetAsync("https://blockchain.info/ticker");
                    if (fallbackResponse.IsSuccessStatusCode)
                    {
                        var jsonString = await fallbackResponse.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(jsonString);
                        var usd = doc.RootElement.GetProperty("USD").GetProperty("last").GetDecimal();
                        _cache.Set(cacheKey, usd, TimeSpan.FromMinutes(1));
                        return usd;
                    }
                }
                catch
                {
                }
            }

            throw new InvalidOperationException("Both primary and fallback APIs failed for BTC to USD rate.");
        }

        public string GetDonationAddress() => DonationAddress;

        public async Task<long> GetTotalReceivedSatoshisAsync()
        {
            var cacheExpiration = TimeSpan.FromMinutes(3);
            var cacheKey = $"balance_{MinConfirmations}conf_{DonationAddress}";
            if (_cache.TryGetValue(cacheKey, out long cachedTotal))
                return cachedTotal;

            var client = _httpClientFactory.CreateClient();

            async Task<long?> TryPrimaryAsync()
            {
                long currentHeight = 0;
                try
                {
                    var hResp = await client.GetAsync("https://blockchain.info/q/getblockcount");
                    if (hResp.IsSuccessStatusCode)
                    {
                        var hStr = await hResp.Content.ReadAsStringAsync();
                        long.TryParse(hStr.Trim(), out currentHeight);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Failed to fetch blockchain.info current height");
                }

                var resp = await client.GetAsync($"https://blockchain.info/rawaddr/{DonationAddress}?limit=100");
                if (!resp.IsSuccessStatusCode) return null;

                var jsonString = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonString);
                if (!doc.RootElement.TryGetProperty("txs", out var txs)) return 0;

                long sum = 0;
                foreach (var tx in txs.EnumerateArray())
                {
                    int confirmations = 0;
                    long blockHeight = tx.TryGetProperty("block_height", out var bh) && bh.ValueKind is JsonValueKind.Number ? bh.GetInt64() : 0;
                    if (blockHeight > 0 && currentHeight > 0)
                    {
                        var diff = currentHeight - blockHeight + 1;
                        if (diff > 0 && diff < int.MaxValue) confirmations = (int)diff;
                    }
                    if (confirmations < MinConfirmations) continue;

                    if (tx.TryGetProperty("out", out var outputs))
                    {
                        foreach (var o in outputs.EnumerateArray())
                        {
                            var addr = o.TryGetProperty("addr", out var a) ? a.GetString() : null;
                            if (!string.Equals(addr, DonationAddress, StringComparison.OrdinalIgnoreCase)) continue;

                            var spent = o.TryGetProperty("spent", out var sp) && sp.ValueKind == JsonValueKind.True;
                            if (spent) continue;

                            sum += o.GetProperty("value").GetInt64();
                        }
                    }
                }

                return sum;
            }

            async Task<long?> TryFallbackAsync()
            {
                var resp = await client.GetAsync($"https://api.blockcypher.com/v1/btc/main/addrs/{DonationAddress}/full?limit=50&txlimit=50");
                if (!resp.IsSuccessStatusCode) return null;

                var jsonString = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonString);
                if (!doc.RootElement.TryGetProperty("txs", out var txs)) return 0;

                long sum = 0;
                foreach (var tx in txs.EnumerateArray())
                {
                    var confs = tx.TryGetProperty("confirmations", out var c) && c.ValueKind is JsonValueKind.Number ? c.GetInt32() : 0;
                    if (confs < MinConfirmations) continue;

                    if (tx.TryGetProperty("outputs", out var outputs))
                    {
                        foreach (var o in outputs.EnumerateArray())
                        {
                            bool hasAddr = false;
                            if (o.TryGetProperty("addresses", out var addrs))
                            {
                                foreach (var a in addrs.EnumerateArray())
                                {
                                    if (string.Equals(a.GetString(), DonationAddress, StringComparison.OrdinalIgnoreCase))
                                    {
                                        hasAddr = true;
                                        break;
                                    }
                                }
                            }
                            if (!hasAddr) continue;

                            var isSpent = o.TryGetProperty("spent_by", out var sb) &&
                                          sb.ValueKind == JsonValueKind.String &&
                                          !string.IsNullOrEmpty(sb.GetString());
                            if (isSpent) continue;

                            sum += o.GetProperty("value").GetInt64();
                        }
                    }
                }

                return sum;
            }

            try
            {
                var primary = await TryPrimaryAsync();
                if (primary is long p)
                {
                    _cache.Set(cacheKey, p, cacheExpiration);
                    return p;
                }

                var fb = await TryFallbackAsync();
                if (fb is long f)
                {
                    _cache.Set(cacheKey, f, cacheExpiration);
                    return f;
                }
            }
            catch
            {
                var fb = await TryFallbackAsync();
                if (fb is long f)
                {
                    _cache.Set(cacheKey, f, cacheExpiration);
                    return f;
                }
            }

            throw new InvalidOperationException("Both primary and fallback APIs failed for confirmed balance.");
        }

        public async Task<int> CheckDonationAddressAndCreateEventsAsync(CancellationToken ct = default)
        {
           var txs = await FetchAddressDonationTransactionsAsync(ct);
            if (txs.Count == 0) return 0;

            var items = txs
                .Where(t => t.AmountSatoshis > 0 && t.Confirmations >= MinConfirmations)
                .GroupBy(t => t.TxId)                                // <-- de-dupe by TxId
                .Select(g => g.OrderBy(t => t.OccurredAtUtc).First())// pick a stable canonical timestamp
                .Select(t => (t.TxId, t.OccurredAtUtc, t.AmountSatoshis))
                .OrderBy(t => t.OccurredAtUtc)
                .ToList();

            _events.CreateDonationReceivedEvents(items, skipIfExists: true);
            return items.Count;
        }

        private async Task<IReadOnlyList<DonationTx>> FetchAddressDonationTransactionsAsync(CancellationToken ct)
        {
           var client = _httpClientFactory.CreateClient();

            // --- Primary: blockchain.info ---
            try
            {
                // Get current height to compute confirmations from block_height
                long currentHeight = 0;
                try
                {
                    var hResp = await client.GetAsync("https://blockchain.info/q/getblockcount", ct);
                    if (hResp.IsSuccessStatusCode)
                    {
                        var hStr = await hResp.Content.ReadAsStringAsync(ct);
                        long.TryParse(hStr.Trim(), out currentHeight);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Failed to fetch blockchain.info current height");
                }

                var resp = await client.GetAsync($"https://blockchain.info/rawaddr/{DonationAddress}?limit=50", ct);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(ct);
                    return ParseBlockchainInfoRawAddr(json, DonationAddress, currentHeight);
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Blockchain.info primary failed");
            }

            // --- Fallback: BlockCypher ---
            try
            {
                var resp = await client.GetAsync($"https://api.blockcypher.com/v1/btc/main/addrs/{DonationAddress}/full?limit=50&txlimit=50", ct);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(ct);
                    return ParseBlockCypherFull(json, DonationAddress);
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "BlockCypher fallback failed");
            }

            return Array.Empty<DonationTx>();
        }

        private static IReadOnlyList<DonationTx> ParseBlockchainInfoRawAddr(string json, string address, long currentHeight)
        {
            var list = new List<DonationTx>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("txs", out var txs)) return list;

            foreach (var tx in txs.EnumerateArray())
            {
                var hash = tx.GetProperty("hash").GetString() ?? "";
                var time = tx.TryGetProperty("time", out var t) ? t.GetInt64() : 0;
                var dt = DateTimeOffset.FromUnixTimeSeconds(time).UtcDateTime;

                long amountToAddress = 0;
                if (tx.TryGetProperty("out", out var outputs))
                {
                    foreach (var o in outputs.EnumerateArray())
                    {
                        var addr = o.TryGetProperty("addr", out var a) ? a.GetString() : null;
                        if (string.Equals(addr, address, StringComparison.OrdinalIgnoreCase))
                        {
                            amountToAddress += o.GetProperty("value").GetInt64();
                        }
                    }
                }

                int confirmations = 0;
                long blockHeight = tx.TryGetProperty("block_height", out var bh) && bh.ValueKind is JsonValueKind.Number
                    ? bh.GetInt64()
                    : 0;
                if (blockHeight > 0 && currentHeight > 0)
                {
                    // confirmations = currentHeight - block_height + 1
                    var diff = currentHeight - blockHeight + 1;
                    if (diff > 0 && diff < int.MaxValue) confirmations = (int)diff;
                }

                if (amountToAddress > 0)
                    list.Add(new DonationTx(hash, amountToAddress, dt, confirmations));
            }

            return list;
        }

        private static IReadOnlyList<DonationTx> ParseBlockCypherFull(string json, string address)
        {
            var list = new List<DonationTx>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("txs", out var txs)) return list;

            foreach (var tx in txs.EnumerateArray())
            {
                var hash = tx.GetProperty("hash").GetString() ?? "";

                // Prefer first-seen time for OccurredAtUtc to be consistent with blockchain.info's "time"
                DateTime occurredUtc;
                if (tx.TryGetProperty("received", out var rec) &&
                    rec.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(rec.GetString(), out var recDt))
                {
                    occurredUtc = DateTime.SpecifyKind(recDt, DateTimeKind.Utc);
                }
                else if (tx.TryGetProperty("confirmed", out var conf) &&
                         conf.ValueKind == JsonValueKind.String &&
                         DateTime.TryParse(conf.GetString(), out var confDt))
                {
                    occurredUtc = DateTime.SpecifyKind(confDt, DateTimeKind.Utc);
                }
                else
                {
                    occurredUtc = DateTime.UtcNow;
                }

                int confirmations = tx.TryGetProperty("confirmations", out var c) && c.ValueKind is JsonValueKind.Number
                    ? c.GetInt32()
                    : 0;

                long amountToAddress = 0;
                if (tx.TryGetProperty("outputs", out var outputs))
                {
                    foreach (var o in outputs.EnumerateArray())
                    {
                        if (o.TryGetProperty("addresses", out var addrs))
                        {
                            var matches = addrs.EnumerateArray().Any(a => string.Equals(a.GetString(), address, StringComparison.OrdinalIgnoreCase));
                            if (matches)
                                amountToAddress += o.GetProperty("value").GetInt64();
                        }
                    }
                }

                if (amountToAddress > 0)
                    list.Add(new DonationTx(hash, amountToAddress, occurredUtc, confirmations));
            }

            return list;
        }

        private readonly record struct DonationTx(string TxId, long AmountSatoshis, DateTime OccurredAtUtc, int Confirmations);
    }
}
