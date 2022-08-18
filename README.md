# Replicate Azure Media Services v3 Account

This application will help you to replicate your AMS v3 account into another account.

It will replicate the following:
<ol>
  <li>Account filters.</li>
  <li>Content key policies.</li>
  <li>Transforms.</li>
  <li>Streaming endpoints.</li>
  <li>Assets & assets filters.</li>
  <li>Live events & live outputs.</li>
  <li>Streaming locators.</li>
</ol>

<br>

### Notes:-
<ol>
<li>You need to create the CDN profiles before running the tool in the destination resource group.</li>
  <li>If the feature is not supported in the destination Data Center then you will see an exception. For example live event live transcription feature is not supported in all the Data Centers.</li>
  <li>After running the tool, it is recommended to check the logs for any errors. The file will be saved beside the application in path <b><i>\Logs\yyyyMMddHHmmss.log</i></b></li>
  <li>If for any reason you stopped the tool while copying the blobs, then it should not recopy already copied blobs</li>
  <li>The tool has been tested. However, there is no guarantees that there is no bugs, use it on your own responsibility.</li>
  <li><b>There could be some Azure charges for copying the blobs depending on the used mechanisim and source and destination datacenters<b/></li>
  <li>Feel free to adjust the tool to meet your needs.</li>
  <li>This is not an official tool supported by Microsoft.</li>
  <li>Copying blobs by using <b><i>AzCopy</i></b> is the fastest and recommended way if you have a lot of contents.</li>
</ol>

<br>

### How to use this tool
To run this application you need to:
1. Modify <b><i>appsettings.json</b></i> configuration file with your source and destination AMS accounts service principal authentication, and storage accounts name, key, and Url
2. If you want to copy the blobs using <b><i>AzCopy</i></b> you need to install it into your machine [Download AzCopy](https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-v10)

<br>

### Known issues
<ul>
  <li>This tool support only copying form AMS account that have one attached Storage account, if you have multiple storage account attached and exception will be thrown</li>
</ul>
