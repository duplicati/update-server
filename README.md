# Updater service for Duplicati

This project contains a service for serving Duplicati update files.

In essence it shadows a remote storage via [KVPSButter](https://github.com/kenkendk/kvpsbutter) and provides a local cache for files, serving as a kind-of CDN of private storage.

## Environment variables

The service is prepared for Docker usage and is configured via environment variables.

Required variables:

- `PRIMARY`: The KVPSButter connection string where the source files are fetched from
- `CACHEPATH`: The path to where cached files will be stored

Optional variables:

- `MAX_NOT_FOUND`: The maximum number of 404 responses from the remote to store. Can use b/k/m/g/t/p suffix. Default is `10k`.
- `MAX_SIZE`: The maximum size of the disk to use for caching. Can use b/k/m/g/t/p suffix. Default is `10m`.
- `SEQ_URL`: url for logging to Seq.
- `SEQ_APIKEY`: API key for logging to Seq.
- `CACHE_TIME`: The duration items are kept cached. Can use s/m/h/d/w suffix. Default is `1d`.
- `REDIRECT`: A url to redirect to, when accessing `/`.
- `APIKEY`: API key to enable the `/reload` endpoint for forced expiration of cached items.
- `KEEP_FOREVER_REGEX`: Regex to disable expiration on matching items; size limits may still expire items.
- `NO_CACHE_REGEX`: Regex to disable caching on items matching expression.
- `CUSTOM_LOG`: A custom serilog message template to write on requests that complete
- `CUSTOM_LOG_HEADERS`: A semi-colon separated list of request headers to include in log data

- `MAXMIND_ACCOUNT_ID`: The Maxmind accountid for geolite
- `MAXMIND_LICENSE_KEY`: The Maxmind licensekey for geolite
- `MAXMIND_IP_HEADER`: The header with the IP used for geolite lookups

- `NOTFOUND_HTML`: A path into the primary storage where the 404 page is loaded from
- `INDEX_HTML`: A path into the primary storage where the index.html page is loaded from
- `INDEX_HTML_REGEX`: A regex that selects what paths to apply the index html for

## Force expire

To force expire items, set `APIKEY` and use a request similar to:

```
curl -X POST --data '["key1","item/key2"]' 'https://example.com/reload' --header 'X-API-KEY:<APIKEY>'
```
