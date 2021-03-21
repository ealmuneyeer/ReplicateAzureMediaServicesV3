# AzureMediaServices

This application will help you to replicate your AMS v3 account into another account.

It will replicate the following:
1- Account filters
2- Content key policies
3- Transforms
4- Streaming endpoints
5- Assets & assets filters
6- Live events & live outputs
7- Streaming locators

Notes:-
1- You need to create the CDN profiles before running the tool in the destination resource group.
2- If the feature is not supported in the destination Data Center then you will see an exception. For example live event live transcription feature is not supported in all the Data Centers.
3- The tool has been tested. However, there is no guarantees that there is no bugs, use it on your own responsibility.
4- Feel free to adjust the tool to meet your needs.
