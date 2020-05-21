# CryptoAzureFunctions
C# code for Azure function used to fill in CosmosDB (MongoDB) database with quotations from different exchanges.

## Dependencies
This project is depending on Nuget Package *[CryptoQuoteAPI](../../../CryptoQuoteAPI/)*.


## Description
Unfortunately, crypto exchange APIs does not allow their users to download an illimited number of historic prices. It is usually limited to some thousands.

But, for machine learning stock prediction, we need a huge amount of data.

With azure functions and CosmosDB, we can create our own repo for crypto quotations and fill it with prices from different exchange APIs.

We are right now supporting only Huobi API.


## How to use this repo
1. Clone this project to your own repository.
2. Create an Azure function
3. Go to YOUR-AZURE-FUNCTION -> Settings -> Configuration
4. Add following environment variables by clicking on 'New application setting' 

Name of the variable | Description
-------------------  | -----------
NOSQL_CONNECTION_STRING  |  str: Connection string to your NoSql database.
PopulateDatabaseTriggerCron  |  str: Period CRON to consider: example 0 0 */12 * * *. See azure timer function doc for more details.
USE_SSL  | bool: Whether or not use SSL when connecting to your database.
BD_NAME  | str: Database name
COLLECTION_NAME  | str: Collection name (will be created if not already exists)
THROUGHPUT  | int: If it is an Azure Cosmos DB and the collection does not already exists, it will be created with this throughput.
SHARD_KEY  | str: If it is an Azure Cosmos DB and the collection does not already exists, it will be created with this shard key.
CREATE_DEFAULT_INDEXES  | bool: If the collection does not already exists, it will be created with default index '{ccy: 1, period: 1, ts: 1}, {unique: true}'
EX_API_PARALLEL_TASKS  | int: How many parallel tasks should be sent to the exchange API when requesting missing quotations.
EX_API_MAX_RETRY  | int: How many times should we retry requesting missing quotations from the exchange API.
ALLOW_ASYNC_INSERT  | bool: Whether or not allow async insertion to the NoSql database.

5. Click on save
6. Go to YOUR-AZURE-FUNCTION -> Deployment -> Deployment Center
7. Choose github deployment for your azure function and provide your github link


## Contributions
Contribution to this project is open. Any kind of help is welcome. Ideas are also welcome.

