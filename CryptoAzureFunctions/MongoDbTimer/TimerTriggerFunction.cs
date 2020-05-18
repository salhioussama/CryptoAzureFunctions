using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using System.Linq;
using CryptoQuote.HuobiAPI.DataObjectsModel;


namespace CryptoAzureFunctions
{
    /// <summary>
    /// Azure Timer class. Auto generated with visual studio 2019
    /// </summary>
    public static class TimerTriggerFunction
    {
        private static Dictionary<string, List<TickerPeriod>> symbols = new Dictionary<string, List<TickerPeriod>> {
            { "btcusdt", new List<TickerPeriod>(){ TickerPeriod.Day1, TickerPeriod.Hour4, TickerPeriod.Min60, TickerPeriod.Min15, TickerPeriod.Min5, TickerPeriod.Min1} },
            { "ltcusdt", new List<TickerPeriod>(){ TickerPeriod.Day1, TickerPeriod.Hour4, TickerPeriod.Min60, TickerPeriod.Min15, TickerPeriod.Min5, TickerPeriod.Min1} },
            { "ethusdt", new List<TickerPeriod>(){ TickerPeriod.Day1, TickerPeriod.Hour4, TickerPeriod.Min60, TickerPeriod.Min15, TickerPeriod.Min5, TickerPeriod.Min1} },
            { "eosusdt", new List<TickerPeriod>(){ TickerPeriod.Day1, TickerPeriod.Hour4, TickerPeriod.Min60, TickerPeriod.Min15, TickerPeriod.Min5, TickerPeriod.Min1} },
            { "xtzusdt", new List<TickerPeriod>(){ TickerPeriod.Day1, TickerPeriod.Hour4, TickerPeriod.Min60, TickerPeriod.Min15, TickerPeriod.Min5, TickerPeriod.Min1} },
            { "trxusdt", new List<TickerPeriod>(){ TickerPeriod.Day1, TickerPeriod.Hour4, TickerPeriod.Min60, TickerPeriod.Min15, TickerPeriod.Min5, TickerPeriod.Min1} },
            { "xrpusdt", new List<TickerPeriod>(){ TickerPeriod.Day1, TickerPeriod.Hour4, TickerPeriod.Min60, TickerPeriod.Min15, TickerPeriod.Min5, TickerPeriod.Min1} },
            { "bchusdt", new List<TickerPeriod>(){ TickerPeriod.Day1, TickerPeriod.Hour4, TickerPeriod.Min60, TickerPeriod.Min15, TickerPeriod.Min5, TickerPeriod.Min1} },
            { "bsvusdt", new List<TickerPeriod>(){ TickerPeriod.Day1, TickerPeriod.Hour4, TickerPeriod.Min60, TickerPeriod.Min15, TickerPeriod.Min5, TickerPeriod.Min1} },
            { "dashusdt", new List<TickerPeriod>(){ TickerPeriod.Day1, TickerPeriod.Hour4, TickerPeriod.Min60, TickerPeriod.Min15, TickerPeriod.Min5, TickerPeriod.Min1} },
            { "htusdt", new List<TickerPeriod>(){ TickerPeriod.Day1, TickerPeriod.Hour4, TickerPeriod.Min60, TickerPeriod.Min15, TickerPeriod.Min5, TickerPeriod.Min1} }
        };

        private static readonly string connection_string = GetEnvironmentVariable("NOSQL_CONNECTION_STRING");
        private static readonly string db_name = GetEnvironmentVariable("BD_NAME");
        private static readonly string collection_name = GetEnvironmentVariable("COLLECTION_NAME");
        private static readonly string shard_key = GetEnvironmentVariable("SHARD_KEY");

        private static readonly int exchange_api_parallel_tasks = int.Parse(GetEnvironmentVariable("EX_API_PARALLEL_TASKS"));
        private static readonly int exchange_api_max_retry = int.Parse(GetEnvironmentVariable("EX_API_MAX_RETRY"));
        private static readonly int? offer_throughput = int.Parse(GetEnvironmentVariable("THROUGHPUT"));

        private static readonly bool use_ssl = bool.Parse(GetEnvironmentVariable("USE_SSL"));
        private static readonly bool create_default_indexes = bool.Parse(GetEnvironmentVariable("CREATE_DEFAULT_INDEXES"));
        private static readonly bool allow_async_insert = bool.Parse(GetEnvironmentVariable("ALLOW_ASYNC_INSERT"));


        private static readonly FillMongoDB fill_obj = null;

        static TimerTriggerFunction()
        {
            fill_obj = new FillMongoDB(connection_string, db_name, offer_throughput, collection_name, use_ssl, create_default_indexes, shard_key, allow_async_insert, exchange_api_parallel_tasks, exchange_api_max_retry);
        }


        /// <summary>
        /// Entry point for Timer function.
        /// </summary>
        /// <param name="myTimer">Cron timer. Its value should be saved in azure function configuration.</param>
        /// <param name="log"></param>
        [FunctionName("PopulateDatabase")]
        public static void Run([TimerTrigger("%PopulateDatabaseTriggerCron%")]TimerInfo myTimer, ILogger log)
        {
            try
            {
                fill_obj.SetLogger(log);
                var (message, list_exceptions) = fill_obj.Run(symbols);
                var log_exceptions = list_exceptions != null ? string.Join(Environment.NewLine, list_exceptions.Select(x => x.Message)) : string.Empty;

                if (message.Contains("Success", StringComparison.CurrentCultureIgnoreCase))
                {
                    log.LogInformation($"{DateTime.Now}: " + message);
                }
                
                else if (string.Equals(message, "Partially Failed", StringComparison.CurrentCultureIgnoreCase))
                {
                    log.LogError($"{DateTime.Now}: Partially Failed: " + log_exceptions + ".");
                }

                else if (string.Equals(message, "Fail", StringComparison.CurrentCultureIgnoreCase))
                {
                    log.LogError($"{DateTime.Now}: Fail: " + log_exceptions + ".");
                }
            }
            catch (Exception ex)
            {
                log.LogError($"{DateTime.Now}: Error: {ex.Message}.");
            }
        }

        /// <summary>
        /// Utility function to get environment variables. It is necessary to get connexion string, timer CRON, async mode and so on.
        /// This environment variables are comming from Azure function configuration panel.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string GetEnvironmentVariable(string name)
        {
            return Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
        }
    }
}
