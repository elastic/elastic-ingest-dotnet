# Elastic.Ingest.APM

A `Elastic.Channel` implementation of `BufferedChannelBase` that allows APM data to be written to `apm-server` over the V2 intake API.


Utilizes `Elastic.Transport` through `Elastic.Ingest.Transport`.


This project is currently still under development and not pushed to Nuget.

We are still working on finishing this implementation as a possible replacement for the PayloadSender that's currently part of `Elastic.Apm`