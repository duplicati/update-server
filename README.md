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
- `SEQ_URL`: url for logging to Seq
- `SEQ_APIKEY`: API key for logging to Seq
- `CACHE_TIME`: The duration items are kept cached. Can use s/m/h/d/w suffix. Default is `1d`.
