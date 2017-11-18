# Thermo Trains

Automatic detection of isolation deficiencies on rolling trains

## Themobox

### Prerequisites

* Visual Studio
* [FLIR Atlas SDK 5.0](http://flir.custhelp.com/app/devResources/fl_devResources) (needs registration)
  * also install the following from that page:
  * Bonjour: 64-bit
  * Pleora: 64-bit
* Docker (to easily create a redis instance)

### Redis

To create a local redis instance, use the following command:

  docker run -p 6379:6379 --name thermobox-redis -d redis

To connect to it and execute commands, use the following command:

  docker run -it --link thermobox-redis:redis --rm redis redis-cli -h redis -p 6379

#### Pub commands

* `publish cmd:capture:start <timestamp>` Start capturing
* `publish cmd:capture:stop <timestamp>` Stop capturing and send the artifacts via `cmd:delivery:upload`
* `publish cmd:delivery:upload <file>` Uploads the file to a remote server
