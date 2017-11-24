# Thermo Trains

Automatic detection of isolation deficiencies on rolling trains

## Thermobox

### Prerequisites

* Visual Studio
* [FLIR Atlas SDK 5.0](http://flir.custhelp.com/app/devResources/fl_devResources) (needs registration)
  * also install the following from that page:
  * Bonjour: 64-bit
  * Pleora: 64-bit
* Docker (to easily create a redis instance)

### Redis

All Thermobox components communicate via a Redis instance reachable on localhost.
You can either install your own Redis instance on your computer.

Or you can use a docker image.

#### Redis with Docker

To create a local redis instance, use the following command:

    docker run -p 6379:6379 --name thermobox-redis -d redis

To connect to it and execute commands, use the following command:

    docker run -it --link thermobox-redis:redis --rm redis redis-cli -h redis -p 6379

#### Pub commands

* `publish cmd:capture:start <timestamp>` Start capturing
* `publish cmd:capture:stop 0` Stop capturing and send the artifacts via `cmd:delivery:upload`
* `publish cmd:capture:pause` Pause capturing - pause from recording state
* `publish cmd:capture:resume` Resume capturing - resume from paused state
* `publish cmd:capture:resume` Abort capturing - discard everything allocated so far
* `publish cmd:delivery:compress <file>` Compress FLIR Seq file and send via `cmd:delivery:upload`
* `publish cmd:delivery:upload <file>` Uploads the file to a remote server
* `publish cmd:kill` Stop every component
