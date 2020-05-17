using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using MongoDB.Driver;
using MongoDB.Bson;
using CryptoQuote.HuobiAPI;
using CryptoQuote.HuobiAPI.DataObjectsModel;

namespace CryptoAzureFunctions
{
    internal class TickerHistoryParameters
    {
        public string symbol { get; set; }
        public TickerPeriod period { get; set; }
        public int size { get; set; }

        public TickerHistoryParameters(string symbol, TickerPeriod period, int size)
        {
            this.symbol = symbol;
            this.period = period;
            this.size = size;
        }
    }

    public class FillMongoDB
    {
        #region variables section
        private readonly string conn = null;
        private readonly int exchange_api_parallel_tasks = 1;
        private readonly int exchange_api_max_retry_count = 1;
        private readonly bool allow_async_insert = false;
        private readonly int? _offer_throughput = null;

        private readonly MongoClient _client = null;
        private readonly IMongoDatabase _db = null;
        private readonly IMongoCollection<BsonDocument> _collection = null;

        private readonly Rest RestApi = new Rest();

        private Dictionary<Tuple<string, string>, BigInteger> _last_ts;
        public Dictionary<Tuple<string, string>, BigInteger> last_ts { get => _last_ts; }
        #endregion

        #region initializer
        /// <summary>
        /// Initialize the core FillMongoDB object linked to a specific database, collection and list of symbols.
        /// </summary>
        /// <param name="connection_string">Connection string to establish connection with NoSql database.</param>
        /// <param name="db_name">Database to use to insert data.</param>
        /// <param name="offer_throughput">Throughput for azure cosmos db in case of new collection.</param>
        /// <param name="collection_name">Collection to use to insert data.</param>
        /// <param name="use_ssl">Use SSL to connect to the database.</param>
        /// <param name="create_default_indexes">Create default index in new collection.</param>
        /// <param name="shard_key">Shard key if newly created collection should be sharded.</param>
        /// <param name="allow_async_insert">Insert new data asynchrounously.</param>
        /// <param name="exchange_api_parallel_tasks">Send n parallel requests to the exchange API.</param>
        /// <param name="exchange_api_max_retry_count">Try n times to request data from the exchange API in case of errors.</param>
        /// <returns>FillMongoDB object linked to a specific database, collection and list of symbols.</returns>
        public FillMongoDB(string connection_string, string db_name, int? offer_throughput, string collection_name, bool use_ssl = false, bool create_default_indexes = false, string shard_key = null,
            bool allow_async_insert = false, int exchange_api_parallel_tasks = 1, int exchange_api_max_retry_count = 1)
        {
            if (string.IsNullOrEmpty(connection_string))
                throw new ArgumentNullException("connection_string");

            if (string.IsNullOrEmpty(db_name))
                throw new ArgumentNullException("db_name");

            if (string.IsNullOrEmpty(collection_name))
                throw new ArgumentNullException("collection_name");

            this.exchange_api_parallel_tasks = exchange_api_parallel_tasks;
            this.exchange_api_max_retry_count = exchange_api_max_retry_count;
            this.allow_async_insert = allow_async_insert;
            this.conn = connection_string;
            _offer_throughput = offer_throughput;

            MongoClientSettings settings = MongoClientSettings.FromUrl(new MongoUrl(connection_string));

            if (use_ssl)
                settings.SslSettings = new SslSettings() { EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 };

            _client = new MongoClient(settings);
            _db = _client.GetDatabase(db_name);

            // Create collection if it is not created and add indexes
            _collection = SetUpDataBase(_db, collection_name, shard_key, create_default_indexes);
            //UpdateOfferThroughput(400);
        }

        /// <summary>
        /// Create a new collection if it is not already present and add some indexes if requested.
        /// </summary>
        /// <param name="db">MongoDB database to use.</param>
        /// <param name="collection_name">Collection name</param>
        /// <param name="shard_key">Shard key if newly created collection should be sharded.</param>
        /// <param name="create_default_indexes">Create default index in new collection.</param>
        /// <returns>List of BsonDocuments and List of all exceptions occured.</returns>
        private IMongoCollection<BsonDocument> SetUpDataBase(IMongoDatabase db, string collection_name, string shard_key, bool create_default_indexes)
        {
            bool newly_created_collection = CreateCollection(collection_name, shard_key);

            // Getting the collection
            var col = db.GetCollection<BsonDocument>(collection_name);

            if (create_default_indexes && newly_created_collection)
                CreateIndex(col, create_default_indexes);

            return col;
        }

        private bool CreateCollection(string collection_name, string shard_key)
        {
            // List of all available collections.
            var list_available_collections = _db.ListCollectionNames();
            bool newly_created_collection = false;

            // Create the new collection if not exists.
            if (!list_available_collections.ToList().Contains(collection_name))
            {
                var offer_th = _offer_throughput ?? 0;

                try
                {
                    if (offer_th > 0)
                    {
                        var command = new BsonDocument { { "customAction", "CreateCollection" }, { "collection", collection_name }, { "offerThroughput", _offer_throughput } };
                        if (!string.IsNullOrEmpty(shard_key))
                            command.Add("shardKey", shard_key);

                        var response = _db.RunCommand<BsonDocument>(command);
                    }

                    else if ((offer_th <= 0) && !string.IsNullOrEmpty(shard_key))
                    {
                        var command = new BsonDocument { { "shardCollection", "huobi.spot" }, { "key", shard_key } };
                        var response = _client.GetDatabase("admin").RunCommand<BsonDocument>(command);
                    }

                    else
                    {
                        _db.CreateCollection(collection_name);
                    }

                    newly_created_collection = true;
                }

                catch (Exception ex)
                {
                    throw new Exception($"Error while creating new collection {collection_name} " + (offer_th <= 0 ? string.Empty : $"with {offer_th} RU/s ") +
                        (string.IsNullOrEmpty(shard_key) ? string.Empty : $"and shardKey = {shard_key} ") + $": {ex.Message}.");
                }
            }

            return newly_created_collection;
        }

        private void CreateIndex(IMongoCollection<BsonDocument> col, bool create_default_indexes)
        {
            // Create the indexes if requested
            if (create_default_indexes)
            {
                var builder = Builders<BsonDocument>.IndexKeys;
                var index_models = new List<CreateIndexModel<BsonDocument>> {
                    //new CreateIndexModel<BsonDocument>(builder.Ascending("ts").Ascending("ccy").Ascending("period"), new CreateIndexOptions() { Unique = true }),
                    new CreateIndexModel<BsonDocument>(builder.Ascending("ccy").Ascending("period").Ascending("ts"), new CreateIndexOptions() { Unique = true }),
                };

                try
                {
                    col.Indexes.CreateMany(index_models);
                }
                catch (Exception ex)
                {
                    throw new Exception($"Error while creating indexes for collection {col.CollectionNamespace.FullName}: {ex.Message}.");
                }
            }
        }

        /// <summary>
        /// Update Azure Cosmos DB Throughput.
        /// </summary>
        /// <param name="offer_throughput">Needed RU/s.</param>
        /// <returns>Result of the customAction request.</returns>
        private BsonDocument UpdateOfferThroughput(int? offer_throughput)
        {
            if ((offer_throughput ?? 0) > 0)
            {
                var command = new BsonDocument { { "customAction", "UpdateCollection" }, { "collection", _collection.CollectionNamespace.CollectionName }, { "offerThroughput", offer_throughput } };
                var response = _db.RunCommand<BsonDocument>(command);

                return response;
            }

            return default(BsonDocument);
        }
        #endregion

        /// <summary>
        /// Request data from exchange API and add them to CosmosDB (NoSql MongoDB).
        /// </summary>
        /// <param name="requests_info">Dictionary of requested crypto currencies and their timeframe.</param>
        /// <returns>The execution message and the list of all exceptions to log.</returns>
        public (string message, IEnumerable<Exception> errors) Run(Dictionary<string, List<TickerPeriod>> requests_info)
        {
            int chunksize = exchange_api_parallel_tasks, pos = 0;

            // Initialize the list of NoSql requests to send in one time
            var bson_format_stack = new ConcurrentStack<BsonDocument>();
            var exceptions_stack = new ConcurrentStack<Exception>();

            try
            {
                if (chunksize <= 0)
                    throw new ArgumentOutOfRangeException("chunksize");

                if (pos < 0)
                    throw new ArgumentOutOfRangeException("pos");

                // Compute timestamp and diff
                var elapsed_time = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
                var ts_today = BigInteger.Parse(elapsed_time.TotalSeconds.ToString().Split(".")[0]);

                // Get all parameters for RestApi.GetTickerHistory
                var requests = GetRequestsInfos(requests_info ?? new Dictionary<string, List<TickerPeriod>>(), ts_today);

                // Create ConcurrentStack for all requests parameters
                var all_param_stack = new ConcurrentStack<TickerHistoryParameters>(requests);

                // Execute async requests in chunks because the exchange API does not accept many requests at once
                int previous_count = all_param_stack.Count, count = 1, max_retry_count = exchange_api_max_retry_count;
                while (!all_param_stack.IsEmpty && count <= max_retry_count)
                {
                    int n = Math.Min(chunksize, all_param_stack.Count);
                    var sub_list = new TickerHistoryParameters[n];

                    var nb_elt = all_param_stack.TryPopRange(sub_list, 0, n);

                    if (nb_elt > 0)
                    {
                        var sub_tasks = sub_list.Select(elt => RestApi.GetTickerHistory(elt.symbol, elt.period, elt.size)).ToArray();
                        try
                        {
                            Task.WaitAll(sub_tasks);
                        }
                        catch
                        { }

                        // Fill in bson_format_stack and exceptions_stack in parallel
                        var parallel_result = Parallel.For(0, sub_tasks.Count(), i =>
                        {
                            if (sub_tasks[i].IsCompletedSuccessfully || count == max_retry_count)
                            {
                                // Get list of BsonDocument for current bulk
                                var (bson, ex) = GetListBson(sub_tasks[i], sub_list[i]);

                                // Concatenate all BsonDocuments
                                if (bson.Count > 0)
                                    bson_format_stack.PushRange(bson.ToArray());

                                // Concatenate all Exceptions
                                if (ex != null)
                                    exceptions_stack.Push(ex);
                            }
                            else
                            {
                                all_param_stack.Push(sub_list[i]);
                            }
                        });
                    }

                    count = all_param_stack.Count != previous_count ? 1 : count + 1;
                    previous_count = all_param_stack.Count;
                }

                var total_elts = bson_format_stack.Count;
                InsertQuotations(bson_format_stack);

                if (exceptions_stack.Count <= 0)
                {
                    return (message: $"Success: {total_elts} new documents inserted.", errors: null);
                }
                else
                {
                    return (message: "Partially Failed", errors: exceptions_stack);
                }
            }

            catch (Exception ex)
            {
                exceptions_stack.Push(ex);
                return (message: "Fail", errors: exceptions_stack);
            }
        }

        private void InsertQuotations(ConcurrentStack<BsonDocument> bson_format_stack)
        {
            var total_elts = bson_format_stack.Count;
            if (total_elts > 0)
            {
                try
                {
                    if (allow_async_insert)
                    {
                        var async_insert_tasks = new List<Task>();
                        while (bson_format_stack.Count > 0)
                        {
                            int n = Math.Min(10000, bson_format_stack.Count);
                            var sub_list = new BsonDocument[n];

                            if (bson_format_stack.TryPopRange(sub_list, 0, n) > 0)
                            {
                                async_insert_tasks.Add(_collection.InsertManyAsync(sub_list, new InsertManyOptions() { IsOrdered = false }));
                            }
                        }

                        Task.WaitAll(async_insert_tasks.ToArray());
                    }
                    else
                    {
                        _collection.InsertMany(bson_format_stack);
                    }
                }

                catch (Exception ex)
                {
                    throw new Exception($"Error while calling MongoDB.InsertMany API with {total_elts} BsonDocuments: {ex.Message}.");
                }
            }
        }

        /// <summary>
        /// Parse results from exchange API (Ticker format) to BsonDocument and add it to a list.
        /// </summary>
        /// <param name="all_tasks">Bulk of RestApi.GetTickerHistory tasks to run.</param>
        /// <param name="inputs">Input parameters for RestApi.GetTickerHistory</param>
        /// <returns>List of BsonDocuments and List of all exceptions occured.</returns>
        private (List<BsonDocument> bson, Exception ex) GetListBson(Task<List<Ticker>> task, TickerHistoryParameters inputs)
        {
            string sym = inputs.symbol, per = inputs.period.ToKey();
            Exception res_exception = null;
            List<BsonDocument> bson_format = new List<BsonDocument>();

            if (task.IsCompletedSuccessfully)
            {
                try
                {
                    bson_format = (from elt in task.Result
                                   where elt.Timestamp > _last_ts.GetValueOrDefault(Tuple.Create(sym, per))
                                   select new
                                   {
                                       insts = DateTime.UtcNow,
                                       ts = (UInt64)elt.Timestamp,
                                       ccy = sym,
                                       period = per,
                                       o = elt.Open,
                                       c = elt.Close,
                                       h = elt.High,
                                       l = elt.Low,
                                       vol = elt.Vol,
                                       amt = elt.Amount,
                                       ct = elt.Count
                                   }.ToBsonDocument()).ToList();
                }
                catch (Exception ex)
                {
                    res_exception = new Exception($"Error while parsing Ticker object to anonymous object to BsonDocument: {ex.Message}.");
                }
            }
            else
            {
                res_exception = new Exception($"Error while calling exchange API with parameters ({sym}, {per}, {inputs.size}). Task status: {task.Status}. " +
                    $"Task exception: {task.Exception.Message}");
            }

            return (bson: bson_format, ex: res_exception);
        }

        #region utils
        /// <summary>
        /// Get the last timestamp for each symbol and period, so we can request only the missing data.
        /// This function is important because it helps us to reduce the bandwidth consumption in Azure cloud.
        /// </summary>
        /// <param name="symbols"> List of symbols we are trying to get information about.</param>
        /// <returns>Dictionary of keys: Tuple<symbol, period> and values: last timestamp.</symbol></returns>
        private Dictionary<Tuple<string, string>, BigInteger> GetLastTimestamps(List<string> symbols)
        {
            var res = new Dictionary<Tuple<string, string>, BigInteger>(symbols.Count());

            try
            {
                var aggregate = _collection.Aggregate().Match(new BsonDocument() { { "ccy", new BsonDocument() { { "$in", new BsonArray(symbols) } } } })
                        .Group(new BsonDocument() { { "_id", new BsonDocument() { { "symbol", "$ccy" }, { "period", "$period" } } }, { "timestamp", new BsonDocument("$max", "$ts") } })
                        .Project(new BsonDocument() { { "_id", 0 }, { "symbol", "$_id.symbol" }, { "period", "$_id.period" }, { "timestamp", 1 } });

                // Fill in res dictionary
                aggregate.ToList().ForEach(elt => res.Add(Tuple.Create(elt["symbol"].ToString(), elt["period"].ToString()), BigInteger.Parse(elt["timestamp"].ToString())));

                return res;
            }

            catch (Exception ex)
            {
                throw new Exception($"Error with MongoDB.Aggregate API: {ex.Message}.");
            }

        }

        /// <summary>
        /// Construct the list of all the request we should send to the exchange API.
        /// </summary>
        /// <param name="quote_to_request">List of symbols to insert with their period.</param>
        /// <param name="ts_today">Today UTC Unix timestamp</param>
        /// <returns>List of tuple representing the parameters we should send to RestApi.GetTickerHistory</returns>
        private List<TickerHistoryParameters> GetRequestsInfos(Dictionary<string, List<TickerPeriod>> quote_to_request, BigInteger ts_today)
        {
            var quotes = quote_to_request ?? new Dictionary<string, List<TickerPeriod>>();

            // Get last timestamps for all symbols
            _last_ts = GetLastTimestamps(quotes.Keys.ToList());

            // Generate the list of data that should be requested: 
            // ex: TickerHistoryParameters('btcusdt', '4hour', 2000)
            var res_query = from elt in quotes
                            from prd in elt.Value
                            let count = _last_ts.ContainsKey(Tuple.Create(elt.Key, prd.ToKey())) ?
                                        (int)((ts_today - _last_ts[Tuple.Create(elt.Key, prd.ToKey())]) / prd.ToSeconds()) + 1 : 2000
                            select new TickerHistoryParameters(elt.Key, prd, Math.Min(count, 2000));

            return res_query.ToList();
        }
        #endregion
    }

    public static class UtilsExtensions
    {
        public static Int64 ToSeconds(this TickerPeriod period)
        {
            switch (period)
            {
                case TickerPeriod.Min1:
                    return 60;
                case TickerPeriod.Min5:
                    return 5 * 60;
                case TickerPeriod.Min15:
                    return 15 * 60;
                case TickerPeriod.Min30:
                    return 30 * 60;
                case TickerPeriod.Min60:
                    return 60 * 60;
                case TickerPeriod.Hour4:
                    return 4 * 60 * 60;
                case TickerPeriod.Day1:
                    return 24 * 60 * 60;
                case TickerPeriod.Mon1:
                    return 30 * 24 * 60 * 60;
                case TickerPeriod.Week1:
                    return 7 * 24 * 60 * 60;
                case TickerPeriod.Year1:
                    return 365 * 24 * 60 * 60;

                default:
                    return 0;
            }
        }

        public static string ToKey(this TickerPeriod period)
        {
            switch (period)
            {
                case TickerPeriod.Min1:
                    return "1m";
                case TickerPeriod.Min5:
                    return "5m";
                case TickerPeriod.Min15:
                    return "15m";
                case TickerPeriod.Min30:
                    return "30m";
                case TickerPeriod.Min60:
                    return "1h";
                case TickerPeriod.Hour4:
                    return "4h";
                case TickerPeriod.Day1:
                    return "1d";
                case TickerPeriod.Mon1:
                    return "1mon";
                case TickerPeriod.Week1:
                    return "1w";
                case TickerPeriod.Year1:
                    return "1y";

                default:
                    return period.ToString();
            }
        }
    }
}
