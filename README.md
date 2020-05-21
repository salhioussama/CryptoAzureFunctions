# CryptoAzureFunctions
C# code for Azure function used to fill in CosmosDB (MongoDB) database with quotations from different exchanges.

## Dependencies
This project is depending on Nuget Package *[CryptoQuoteAPI](../../../CryptoQuoteAPI/)*.


## Description
Unfortunately, crypto exchange APIs does not allow their users to download an illimited number of historic prices. It is usually limited to some thousands.

But, for machine learning stock prediction, we need a huge amount of data.

With azure functions and CosmosDB, we can create our own repo for crypto quotations and fill it with prices from different exchange APIs.

We are right now supporting only Huobi API.


## Contributions
Contribution to this project is open. Any kind of help is welcome. Ideas are also welcome.

