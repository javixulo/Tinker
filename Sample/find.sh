#!/usr/bin/env bash
# Calls find_devices with the JSON of the `device` you give (properties to look for; {} lists every device).
#   ./find.sh '{"brand":"Shelly","location":"salon"}'
cd "$(dirname "$0")"
TINKER_TOOL=find_devices exec ./call.sh "$@"
