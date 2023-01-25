# Elastic.Ingest.Transport

An abstract `Elastic.Channels` implementation of `BufferedChannelBase` that allows implementes to quickly utilize `Elastic.Transport` to send data over HTTP(S) to one or many receiving endpoints.

This is a core library that does not ship any useful implementation. 

See e.g `Elastic.Ingest.Elasticsearch` for a concrete implementation to push data to Elasticsearch